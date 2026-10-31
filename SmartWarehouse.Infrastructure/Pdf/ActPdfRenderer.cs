using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Drawing;
using SmartWarehouse.Application.Services;

namespace SmartWarehouse.Infrastructure.Pdf;

/// <summary>Акт на списание (A4, PDF): строки с «куда использовано», отметка утверждения, подписи комиссии.</summary>
public class ActPdfRenderer : IActPdfRenderer
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public byte[] RenderWriteOffAct(WriteOffActData data)
    {
        EmbeddedFontResolver.EnsureRegistered();

        var document = new Document();
        document.Info.Title = $"Акт на списание {data.Number}";
        document.Info.Author = "SmartWarehouse";

        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = EmbeddedFontResolver.Family;
        normal.Font.Size = 10;

        var section = document.AddSection();
        var page = section.PageSetup;
        page.PageFormat = PageFormat.A4;
        page.TopMargin = Unit.FromCentimeter(1.5);
        page.BottomMargin = Unit.FromCentimeter(1.5);
        page.LeftMargin = Unit.FromCentimeter(2.5);
        page.RightMargin = Unit.FromCentimeter(1.5);

        // ---------- Шапка ----------
        if (data.Organization is not null)
        {
            var org = section.AddParagraph(data.Organization);
            org.Format.Alignment = ParagraphAlignment.Center;
            org.Format.SpaceAfter = Unit.FromPoint(12);
        }

        AddApproval(section, data.Approval);

        var title = section.AddParagraph($"АКТ № {data.Number}");
        title.Format.Alignment = ParagraphAlignment.Center;
        title.Format.Font.Size = 14;
        title.Format.Font.Bold = true;

        var subtitle = section.AddParagraph("на списание материальных ценностей");
        subtitle.Format.Alignment = ParagraphAlignment.Center;
        subtitle.Format.Font.Size = 12;

        var date = section.AddParagraph($"от {data.Date.ToLocalTime().ToString("dd.MM.yyyy", Ru)}");
        date.Format.Alignment = ParagraphAlignment.Center;
        date.Format.SpaceAfter = Unit.FromPoint(14);

        // ---------- Сведения ----------
        var employee = data.EmployeeFullName;
        if (!string.IsNullOrWhiteSpace(data.EmployeePosition)) employee += $", {data.EmployeePosition}";
        if (!string.IsNullOrWhiteSpace(data.EmployeePersonnelNumber)) employee += $", таб. № {data.EmployeePersonnelNumber}";

        AddField(section, "Материально ответственное лицо", employee);
        if (!string.IsNullOrWhiteSpace(data.Period)) AddField(section, "Период", data.Period!);
        if (!string.IsNullOrWhiteSpace(data.ExternalNumber)) AddField(section, "Основание (наряд, заявка)", data.ExternalNumber!);
        if (!string.IsNullOrWhiteSpace(data.Comment)) AddField(section, "Примечание", data.Comment!);

        var intro = section.AddParagraph(
            "Комиссия в составе лиц, подписавших настоящий акт, установила, что перечисленные ниже материальные " +
            "ценности, числящиеся за материально ответственным лицом, израсходованы (использованы) и подлежат списанию:");
        intro.Format.SpaceBefore = Unit.FromPoint(8);
        intro.Format.SpaceAfter = Unit.FromPoint(8);
        intro.Format.Alignment = ParagraphAlignment.Justify;

        // ---------- Таблица (ширина текста 17 см) ----------
        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.Format.Font.Size = 9;
        table.AddColumn(Unit.FromCentimeter(0.8)).Format.Alignment = ParagraphAlignment.Center;
        table.AddColumn(Unit.FromCentimeter(2.4));
        table.AddColumn(Unit.FromCentimeter(5.4));
        table.AddColumn(Unit.FromCentimeter(1.2)).Format.Alignment = ParagraphAlignment.Center;
        table.AddColumn(Unit.FromCentimeter(1.8)).Format.Alignment = ParagraphAlignment.Right;
        table.AddColumn(Unit.FromCentimeter(5.4));

        var header = table.AddRow();
        header.HeadingFormat = true; // повторяется на каждой странице
        header.Format.Font.Bold = true;
        header.Shading.Color = Colors.LightGray;
        header.VerticalAlignment = VerticalAlignment.Center;
        string[] headers = ["№", "Артикул", "Наименование", "Ед. изм.", "Кол-во", "Куда использовано"];
        for (var i = 0; i < headers.Length; i++)
        {
            header.Cells[i].AddParagraph(headers[i]);
            header.Cells[i].Format.Alignment = ParagraphAlignment.Center;
        }

        foreach (var line in data.Lines.OrderBy(l => l.LineNo))
        {
            var row = table.AddRow();
            row.VerticalAlignment = VerticalAlignment.Center;
            row.Cells[0].AddParagraph(line.LineNo.ToString(Ru));
            row.Cells[1].AddParagraph(line.Sku);
            row.Cells[2].AddParagraph(line.ItemName);
            row.Cells[3].AddParagraph(line.UnitCode);
            row.Cells[4].AddParagraph(line.Quantity.ToString("#,0.###", Ru));
            row.Cells[5].AddParagraph(line.Usage ?? "");
        }

        var total = section.AddParagraph($"Всего наименований: {data.Lines.Count}");
        total.Format.SpaceBefore = Unit.FromPoint(6);
        if (data.DocumentNumber is not null)
            total.AddText($". Списание проведено документом {data.DocumentNumber}.");
        total.Format.SpaceAfter = Unit.FromPoint(24);

        // ---------- Подписи ----------
        var signatures = section.AddTable();
        signatures.AddColumn(Unit.FromCentimeter(6.0));
        signatures.AddColumn(Unit.FromCentimeter(4.5));
        signatures.AddColumn(Unit.FromCentimeter(6.5));

        AddSignature(signatures, "Материально ответственное лицо", data.EmployeeFullName);
        AddSignature(signatures, "Председатель комиссии", "");
        AddSignature(signatures, "Члены комиссии:", "");
        AddSignature(signatures, "", "");
        AddSignature(signatures, "", "");

        // ---------- Подвал ----------
        var footer = section.Footers.Primary.AddParagraph(
            $"Сформировано в SmartWarehouse {DateTime.Now.ToString("dd.MM.yyyy HH:mm", Ru)} · акт {data.Number}");
        footer.Format.Font.Size = 7.5;
        footer.Format.Font.Color = Colors.Gray;

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();

        if (!string.IsNullOrWhiteSpace(data.Watermark))
            DrawWatermark(renderer.PdfDocument, data.Watermark!);

        using var output = new MemoryStream();
        renderer.PdfDocument.Save(output, false);
        return output.ToArray();
    }

    /// <summary>«УТВЕРЖДАЮ» справа: заполнено, если акт утверждён в программе, иначе — строки для подписи.</summary>
    private static void AddApproval(Section section, ActApprovalData? approval)
    {
        var text = approval is null
            ? "УТВЕРЖДАЮ\nРуководитель\n\n______________ / ______________\n«____» ______________ 20___ г."
            : $"УТВЕРЖДАЮ\n{approval.Position ?? "Руководитель"}\n\n______________ / {approval.Name}\n" +
              $"{approval.At.ToLocalTime().ToString("dd.MM.yyyy", Ru)}";

        var last = section.AddParagraph(text);
        last.Format.Alignment = ParagraphAlignment.Right;

        if (approval is not null)
        {
            var mark = section.AddParagraph(
                $"Утверждено в SmartWarehouse {approval.At.ToLocalTime().ToString("dd.MM.yyyy HH:mm", Ru)}");
            mark.Format.Alignment = ParagraphAlignment.Right;
            mark.Format.Font.Size = 8;
            mark.Format.Font.Color = Colors.Gray;
            last = mark;
        }

        last.Format.SpaceAfter = Unit.FromPoint(18);
    }

    /// <summary>Крупная полупрозрачная надпись по диагонали каждой страницы — под текстом акта.</summary>
    private static void DrawWatermark(PdfSharp.Pdf.PdfDocument pdf, string text)
    {
        var font = new XFont(EmbeddedFontResolver.Family, 72, XFontStyleEx.Bold);
        var brush = new XSolidBrush(XColor.FromArgb(45, 200, 30, 30));

        foreach (var page in pdf.Pages)
        {
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Prepend);
            var center = new XPoint(page.Width.Point / 2, page.Height.Point / 2);
            gfx.RotateAtTransform(-45, center);
            gfx.DrawString(text, font, brush, center, XStringFormats.Center);
        }
    }

    private static void AddField(Section section, string label, string value)
    {
        var p = section.AddParagraph();
        p.AddFormattedText($"{label}: ", TextFormat.Bold);
        p.AddText(value);
        p.Format.SpaceAfter = Unit.FromPoint(3);
    }

    private static void AddSignature(Table table, string role, string name)
    {
        var row = table.AddRow();
        row.TopPadding = Unit.FromPoint(10);
        row.Cells[0].AddParagraph(role);
        row.Cells[1].AddParagraph("________________");
        row.Cells[1].Format.Alignment = ParagraphAlignment.Center;
        row.Cells[2].AddParagraph(string.IsNullOrEmpty(name) ? "/ ______________________ /" : $"/ {name} /");
    }
}
