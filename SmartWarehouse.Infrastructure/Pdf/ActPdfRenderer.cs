using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using SmartWarehouse.Application.Services;

namespace SmartWarehouse.Infrastructure.Pdf;

/// <summary>Акт на списание в свободной форме (A4, PDF).</summary>
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

        var approve = section.AddParagraph("УТВЕРЖДАЮ\nНачальник отдела\n\n______________ / ______________\n«____» ______________ 20___ г.");
        approve.Format.Alignment = ParagraphAlignment.Right;
        approve.Format.SpaceAfter = Unit.FromPoint(18);

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
        if (data.Equipment is not null) AddField(section, "Оборудование (куда установлено)", data.Equipment);
        if (!string.IsNullOrWhiteSpace(data.Comment)) AddField(section, "Куда использовано", data.Comment!);
        if (!string.IsNullOrWhiteSpace(data.ExternalNumber)) AddField(section, "Основание (наряд, заявка)", data.ExternalNumber!);

        var intro = section.AddParagraph(
            "Комиссия в составе лиц, подписавших настоящий акт, установила, что перечисленные ниже материальные " +
            "ценности, числящиеся за материально ответственным лицом, израсходованы (использованы) и подлежат списанию:");
        intro.Format.SpaceBefore = Unit.FromPoint(8);
        intro.Format.SpaceAfter = Unit.FromPoint(8);
        intro.Format.Alignment = ParagraphAlignment.Justify;

        // ---------- Таблица ----------
        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.Format.Font.Size = 9.5;
        table.AddColumn(Unit.FromCentimeter(1.0)).Format.Alignment = ParagraphAlignment.Center;
        table.AddColumn(Unit.FromCentimeter(3.2));
        table.AddColumn(Unit.FromCentimeter(8.3));
        table.AddColumn(Unit.FromCentimeter(1.6)).Format.Alignment = ParagraphAlignment.Center;
        table.AddColumn(Unit.FromCentimeter(2.9)).Format.Alignment = ParagraphAlignment.Right;

        var header = table.AddRow();
        header.HeadingFormat = true; // повторяется на каждой странице
        header.Format.Font.Bold = true;
        header.Shading.Color = Colors.LightGray;
        header.VerticalAlignment = VerticalAlignment.Center;
        string[] headers = ["№", "Артикул", "Наименование", "Ед. изм.", "Количество"];
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
        }

        var total = section.AddParagraph($"Всего наименований: {data.Lines.Count}");
        total.Format.SpaceBefore = Unit.FromPoint(6);
        total.Format.SpaceAfter = Unit.FromPoint(24);

        // ---------- Подписи ----------
        var signatures = section.AddTable();
        signatures.AddColumn(Unit.FromCentimeter(6.0));
        signatures.AddColumn(Unit.FromCentimeter(4.5));
        signatures.AddColumn(Unit.FromCentimeter(6.5));

        AddSignature(signatures, "Материально ответственное лицо", data.EmployeeFullName);
        AddSignature(signatures, "Кладовщик", data.PerformedBy ?? "");
        AddSignature(signatures, "Члены комиссии:", "");
        AddSignature(signatures, "", "");
        AddSignature(signatures, "", "");

        // ---------- Подвал ----------
        var footer = section.Footers.Primary.AddParagraph(
            $"Сформировано в SmartWarehouse {DateTime.Now.ToString("dd.MM.yyyy HH:mm", Ru)} · документ {data.Number}");
        footer.Format.Font.Size = 7.5;
        footer.Format.Font.Color = Colors.Gray;

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();

        using var output = new MemoryStream();
        renderer.PdfDocument.Save(output, false);
        return output.ToArray();
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
