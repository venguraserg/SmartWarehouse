using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Drawing.Layout;
using PdfSharp.Pdf;
using QRCoder;
using SmartWarehouse.Application.Services;

namespace SmartWarehouse.Infrastructure.Pdf;

/// <summary>
/// Листы A4 с наклейками: слева QR-код, справа код крупно, наименование и примечание.
/// Сетка выравнивается по центру листа — как у стандартной самоклеящейся бумаги.
/// </summary>
public class LabelPdfRenderer : ILabelPdfRenderer
{
    private const double PageWidthMm = 210;
    private const double PageHeightMm = 297;

    public byte[] Render(IReadOnlyList<LabelData> labels, LabelLayout layout, int skip)
    {
        EmbeddedFontResolver.EnsureRegistered();

        using var pdf = new PdfDocument();
        pdf.Info.Title = "Наклейки";
        pdf.Info.Author = "SmartWarehouse";

        var left = (PageWidthMm - layout.Columns * layout.WidthMm) / 2;
        var top = (PageHeightMm - layout.Rows * layout.HeightMm) / 2;
        var small = layout.HeightMm < 30;

        // Шрифты: на мелких наклейках — мельче.
        var codeSize = small ? 8.5 : 11;
        var titleFont = new XFont(EmbeddedFontResolver.Family, small ? 6.5 : 8, XFontStyleEx.Regular);
        var noteFont = new XFont(EmbeddedFontResolver.Family, small ? 5.5 : 6.5, XFontStyleEx.Regular);

        XGraphics? gfx = null;
        var currentPage = -1;
        try
        {
            for (var i = 0; i < labels.Count; i++)
            {
                var slot = skip + i;
                var pageIndex = slot / layout.PerSheet;
                if (pageIndex != currentPage)
                {
                    gfx?.Dispose();
                    var page = pdf.AddPage();
                    page.Size = PageSize.A4;
                    gfx = XGraphics.FromPdfPage(page);
                    currentPage = pageIndex;
                }

                var position = slot % layout.PerSheet;
                var x = left + position % layout.Columns * layout.WidthMm;
                var y = top + position / layout.Columns * layout.HeightMm;
                DrawLabel(gfx!, labels[i], x, y, layout, codeSize, titleFont, noteFont);
            }
        }
        finally
        {
            gfx?.Dispose();
        }

        if (pdf.PageCount == 0)
            pdf.AddPage().Size = PageSize.A4;

        using var output = new MemoryStream();
        pdf.Save(output, false);
        return output.ToArray();
    }

    private static void DrawLabel(
        XGraphics gfx, LabelData label, double xMm, double yMm, LabelLayout layout,
        double codeSize, XFont titleFont, XFont noteFont)
    {
        var padding = layout.HeightMm < 30 ? 2.0 : 3.0;
        var qrMm = Math.Min(layout.HeightMm - 2 * padding, layout.WidthMm * 0.45);
        var gap = 2.0;

        // QR-код по центру по высоте.
        // Поток и изображение не закрываем: PDFsharp может читать их до сохранения документа.
        var image = XImage.FromStream(new MemoryStream(QrPng(label.Payload)));
        image.Interpolate = false; // чёткие края модулей — лучше читается
        gfx.DrawImage(image, Mm(xMm + padding), Mm(yMm + (layout.HeightMm - qrMm) / 2), Mm(qrMm), Mm(qrMm));

        // Текст справа от QR.
        var textX = xMm + padding + qrMm + gap;
        var textWidth = layout.WidthMm - (padding + qrMm + gap + padding);
        var textTop = yMm + padding;
        var textBottom = yMm + layout.HeightMm - padding;

        // Код — крупно и жирно; если не помещается в ширину, уменьшаем.
        var size = codeSize;
        var codeFont = new XFont(EmbeddedFontResolver.Family, size, XFontStyleEx.Bold);
        while (size > 5 && gfx.MeasureString(label.Code, codeFont).Width > Mm(textWidth))
        {
            size -= 0.5;
            codeFont = new XFont(EmbeddedFontResolver.Family, size, XFontStyleEx.Bold);
        }
        var codeHeight = gfx.MeasureString(label.Code, codeFont).Height;
        gfx.DrawString(label.Code, codeFont, XBrushes.Black,
            new XRect(Mm(textX), Mm(textTop), Mm(textWidth), codeHeight), XStringFormats.TopLeft);

        var formatter = new XTextFormatter(gfx);
        var noteHeight = string.IsNullOrWhiteSpace(label.Note) ? 0 : gfx.MeasureString("Ag", noteFont).Height * 1.1;
        var titleTop = Mm(textTop) + codeHeight + Mm(0.8);
        var titleHeight = Mm(textBottom) - noteHeight - titleTop;

        if (!string.IsNullOrWhiteSpace(label.Title) && titleHeight > 0)
            formatter.DrawString(label.Title, titleFont, XBrushes.Black,
                new XRect(Mm(textX), titleTop, Mm(textWidth), titleHeight), XStringFormats.TopLeft);

        if (noteHeight > 0)
            gfx.DrawString(label.Note!, noteFont, XBrushes.DimGray,
                new XRect(Mm(textX), Mm(textBottom) - noteHeight, Mm(textWidth), noteHeight), XStringFormats.TopLeft);
    }

    /// <summary>QR-код в PNG (средний уровень коррекции — выдерживает потёртость наклейки).</summary>
    internal static byte[] QrPng(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(8);
    }

    private static double Mm(double value) => XUnit.FromMillimeter(value).Point;
}
