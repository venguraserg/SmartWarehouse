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

    public byte[] Render(IReadOnlyList<LabelData> labels, LabelLayout layout, int skip) =>
        Render([], labels, layout, skip);

    public byte[] Render(IReadOnlyList<RackSignData> signs, IReadOnlyList<LabelData> labels, LabelLayout layout, int skip)
    {
        EmbeddedFontResolver.EnsureRegistered();

        using var pdf = new PdfDocument();
        pdf.Info.Title = signs.Count > 0 ? "Таблички и наклейки стеллажа" : "Наклейки";

        // Таблички стеллажей — по листу A5 (альбомный) на каждую, перед листами наклеек.
        foreach (var sign in signs)
        {
            var page = pdf.AddPage();
            // A5 альбомный — размеры задаём явно (ширина больше высоты), без поворота страницы.
            page.Width = XUnit.FromMillimeter(210);
            page.Height = XUnit.FromMillimeter(148);
            using var signGfx = XGraphics.FromPdfPage(page);
            DrawSign(signGfx, sign);
        }
        pdf.Info.Author = "Склад ОЭТО — ОАО «Туровский молочный комбинат»";

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
        if (label.Big is not null)
        {
            DrawPlaceLabel(gfx, label, xMm, yMm, layout);
            return;
        }

        var padding = layout.HeightMm < 30 ? 2.0 : 3.0;
        var qrMm = Math.Min(layout.HeightMm - 2 * padding, layout.WidthMm * 0.45);
        var gap = 2.0;

        // QR-код по центру по высоте — векторными квадратами (чёткая печать при любом масштабе).
        DrawQr(gfx, label.Payload, Mm(xMm + padding), Mm(yMm + (layout.HeightMm - qrMm) / 2), Mm(qrMm));

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

    /// <summary>
    /// Наклейка места: слева QR; справа крупно короткий адрес («C3 · п.1 · яч.1», переносится на 2 строки),
    /// этаж или зона, описание (одна строка), внизу мелко шифр для сверки.
    /// </summary>
    private static void DrawPlaceLabel(XGraphics gfx, LabelData label, double xMm, double yMm, LabelLayout layout)
    {
        var small = layout.HeightMm < 30;
        var padding = small ? 2.0 : 3.0;
        var qrMm = Math.Min(layout.HeightMm - 2 * padding, layout.WidthMm * 0.45);
        var gap = 2.0;
        DrawQr(gfx, label.Payload, Mm(xMm + padding), Mm(yMm + (layout.HeightMm - qrMm) / 2), Mm(qrMm));

        var textX = Mm(xMm + padding + qrMm + gap);
        var textWidth = Mm(layout.WidthMm - (padding + qrMm + gap + padding));
        var top = Mm(yMm + padding);
        var bottom = Mm(yMm + layout.HeightMm - padding);

        var codeFont = new XFont(EmbeddedFontResolver.Family, small ? 5 : 6, XFontStyleEx.Regular);
        var areaFont = new XFont(EmbeddedFontResolver.Family, small ? 6 : 8, XFontStyleEx.Bold);
        var titleFont = new XFont(EmbeddedFontResolver.Family, small ? 5.5 : 7, XFontStyleEx.Regular);

        // Снизу вверх: шифр, описание, этаж — остальное место отдаём адресу.
        var codeHeight = gfx.MeasureString("Ag", codeFont).Height;
        gfx.DrawString(Fit(gfx, label.Code, codeFont, textWidth), codeFont, XBrushes.DimGray,
            new XRect(textX, bottom - codeHeight, textWidth, codeHeight), XStringFormats.TopLeft);
        var reserved = codeHeight + Mm(0.5);

        var below = top;
        var bigBottom = bottom - reserved - LineHeight(gfx, areaFont, label.Area) - LineHeight(gfx, titleFont, label.Title);

        // Крупный адрес: самый большой размер, при котором он помещается в 1–2 строки.
        var (lines, bigFont) = FitBig(gfx, label.Big!, textWidth, bigBottom - top, small ? 15 : 22, small ? 6 : 8);
        var lineHeight = gfx.MeasureString("Ag", bigFont).Height * 0.95;
        foreach (var line in lines)
        {
            gfx.DrawString(line, bigFont, XBrushes.Black, new XRect(textX, below, textWidth, lineHeight), XStringFormats.TopLeft);
            below += lineHeight;
        }

        if (!string.IsNullOrWhiteSpace(label.Area))
        {
            var h = gfx.MeasureString("Ag", areaFont).Height;
            gfx.DrawString(Fit(gfx, label.Area, areaFont, textWidth), areaFont, XBrushes.Black,
                new XRect(textX, below + Mm(0.4), textWidth, h), XStringFormats.TopLeft);
            below += h + Mm(0.4);
        }
        if (!string.IsNullOrWhiteSpace(label.Title))
        {
            var h = gfx.MeasureString("Ag", titleFont).Height;
            gfx.DrawString(Fit(gfx, label.Title, titleFont, textWidth), titleFont, XBrushes.Black,
                new XRect(textX, below + Mm(0.3), textWidth, h), XStringFormats.TopLeft);
        }
    }

    private static double LineHeight(XGraphics gfx, XFont font, string? text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : gfx.MeasureString("Ag", font).Height + Mm(0.4);

    /// <summary>Подбор размера крупной надписи: переносы — только между частями адреса (« · »).</summary>
    internal static (List<string> Lines, XFont Font) FitBig(XGraphics gfx, string text, double width, double height, double maxSize, double minSize)
    {
        var parts = text.Split(" · ");
        XFont font = null!;
        List<string> lines = [];
        for (var size = maxSize; size >= minSize; size -= 0.5)
        {
            font = new XFont(EmbeddedFontResolver.Family, size, XFontStyleEx.Bold);
            lines = Wrap(gfx, parts, font, width);
            var lineHeight = gfx.MeasureString("Ag", font).Height * 0.95;
            if (lines.Count <= 2 && lines.All(l => gfx.MeasureString(l, font).Width <= width) && lines.Count * lineHeight <= height)
                return (lines, font);
        }
        return (lines.Select(l => Fit(gfx, l, font, width)).Take(2).ToList(), font);
    }

    private static List<string> Wrap(XGraphics gfx, string[] parts, XFont font, double width)
    {
        var lines = new List<string>();
        var current = "";
        foreach (var part in parts)
        {
            var candidate = current.Length == 0 ? part : $"{current} · {part}";
            if (current.Length > 0 && gfx.MeasureString(candidate, font).Width > width)
            {
                lines.Add(current);
                current = part;
            }
            else
                current = candidate;
        }
        if (current.Length > 0)
            lines.Add(current);
        return lines;
    }

    /// <summary>Строка в ширину; длинная — с многоточием.</summary>
    private static string Fit(XGraphics gfx, string text, XFont font, double width)
    {
        if (gfx.MeasureString(text, font).Width <= width)
            return text;
        var cut = text;
        while (cut.Length > 1 && gfx.MeasureString(cut + "…", font).Width > width)
            cut = cut[..^1];
        return cut.TrimEnd() + "…";
    }

    /// <summary>
    /// Табличка стеллажа на A5 (альбомный): слева «СТЕЛЛАЖ», огромный код, этаж, описание;
    /// справа QR стеллажа, его шифр и полки сверху вниз (пол — последним, пунктиром).
    /// </summary>
    private static void DrawSign(XGraphics gfx, RackSignData sign)
    {
        const double W = 210, H = 148, Pad = 12, RightW = 62, Gap = 8;
        var leftW = W - 2 * Pad - RightW - Gap;
        var family = EmbeddedFontResolver.Family;

        // Слева.
        var label = new XFont(family, 14, XFontStyleEx.Bold);
        gfx.DrawString("СТЕЛЛАЖ", label, XBrushes.DimGray, new XRect(Mm(Pad), Mm(Pad), Mm(leftW), Mm(8)), XStringFormats.TopLeft);

        var size = 220.0;
        var rackFont = new XFont(family, size, XFontStyleEx.Bold);
        while (size > 40 && (gfx.MeasureString(sign.Rack, rackFont).Width > Mm(leftW) || gfx.MeasureString(sign.Rack, rackFont).Height > Mm(72)))
        {
            size -= 4;
            rackFont = new XFont(family, size, XFontStyleEx.Bold);
        }
        var rackHeight = gfx.MeasureString(sign.Rack, rackFont).Height;
        var y = Mm(Pad + 8);
        gfx.DrawString(sign.Rack, rackFont, XBrushes.Black, new XRect(Mm(Pad), y, Mm(leftW), rackHeight), XStringFormats.TopLeft);
        y += rackHeight;

        if (!string.IsNullOrWhiteSpace(sign.Area))
        {
            var areaFont = new XFont(family, 24, XFontStyleEx.Bold);
            var h = gfx.MeasureString("Ag", areaFont).Height;
            gfx.DrawString(Fit(gfx, sign.Area, areaFont, Mm(leftW)), areaFont, XBrushes.Black, new XRect(Mm(Pad), y, Mm(leftW), h), XStringFormats.TopLeft);
            y += h + Mm(1);
        }
        if (!string.IsNullOrWhiteSpace(sign.Note))
        {
            var noteFont = new XFont(family, 16, XFontStyleEx.Regular);
            var footerTop = Mm(H - Pad - 6);
            if (footerTop - y > Mm(6))
                new XTextFormatter(gfx).DrawString(sign.Note, noteFont, XBrushes.Black,
                    new XRect(Mm(Pad), y, Mm(leftW), footerTop - y - Mm(2)), XStringFormats.TopLeft);
        }
        var footer = new XFont(family, 9, XFontStyleEx.Regular);
        gfx.DrawString("Склад ОЭТО · ОАО «Туровский молочный комбинат»", footer, XBrushes.DimGray,
            new XRect(Mm(Pad), Mm(H - Pad - 5), Mm(leftW), Mm(5)), XStringFormats.TopLeft);

        // Справа: QR, шифр, полки.
        var rx = W - Pad - RightW;
        var qr = RightW - 4;
        DrawQr(gfx, sign.Payload, Mm(rx + 2), Mm(Pad), Mm(qr));
        var codeFont = new XFont(family, 10, XFontStyleEx.Regular);
        var codeY = Mm(Pad + qr + 1);
        gfx.DrawString(Fit(gfx, sign.FullCode, codeFont, Mm(RightW)), codeFont, XBrushes.DimGray,
            new XRect(Mm(rx), codeY, Mm(RightW), Mm(5)), XStringFormats.TopCenter);

        if (sign.Shelves.Count == 0)
            return;
        var tableTop = Pad + qr + 8;
        var available = H - Pad - tableTop;
        var rowH = Math.Min(7.5, available / sign.Shelves.Count);
        var rowFont = new XFont(family, Math.Max(6, Math.Min(12, rowH * 1.6)), XFontStyleEx.Bold);
        var hintFont = new XFont(family, Math.Max(5.5, Math.Min(10, rowH * 1.35)), XFontStyleEx.Regular);
        var pen = new XPen(XColors.Black, 1.2);
        var dashed = new XPen(XColors.DimGray, 0.8) { DashStyle = XDashStyle.Dash };
        gfx.DrawRectangle(pen, Mm(rx), Mm(tableTop), Mm(RightW), Mm(rowH * sign.Shelves.Count));
        for (var i = 0; i < sign.Shelves.Count; i++)
        {
            var shelf = sign.Shelves[i];
            var rowY = tableTop + i * rowH;
            if (i > 0)
                gfx.DrawLine(shelf.Label == "пол" ? dashed : pen, Mm(rx), Mm(rowY), Mm(rx + RightW), Mm(rowY));
            gfx.DrawString(shelf.Label, rowFont, XBrushes.Black,
                new XRect(Mm(rx + 2), Mm(rowY), Mm(16), Mm(rowH)), XStringFormats.CenterLeft);
            if (!string.IsNullOrWhiteSpace(shelf.Hint))
                gfx.DrawString(Fit(gfx, shelf.Hint, hintFont, Mm(RightW - 20)), hintFont, XBrushes.DimGray,
                    new XRect(Mm(rx + 18), Mm(rowY), Mm(RightW - 20), Mm(rowH)), XStringFormats.CenterRight);
        }
    }

    /// <summary>
    /// Рисует QR-код (средний уровень коррекции — выдерживает потёртость наклейки).
    /// Соседние тёмные модули строки объединяются в один прямоугольник.
    /// </summary>
    internal static void DrawQr(XGraphics gfx, string payload, double x, double y, double size)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        var matrix = data.ModuleMatrix; // уже с белой рамкой (quiet zone)
        var count = matrix.Count;
        var module = size / count;

        for (var row = 0; row < count; row++)
        {
            var bits = matrix[row];
            var col = 0;
            while (col < count)
            {
                if (!bits[col])
                {
                    col++;
                    continue;
                }
                var start = col;
                while (col < count && bits[col])
                    col++;
                // Небольшой нахлёст убирает светлые щели между модулями при печати.
                gfx.DrawRectangle(XBrushes.Black, x + start * module, y + row * module, (col - start) * module + 0.05, module + 0.05);
            }
        }
    }

    private static double Mm(double value) => XUnit.FromMillimeter(value).Point;
}
