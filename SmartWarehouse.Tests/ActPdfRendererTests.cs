using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Infrastructure.Pdf;

namespace SmartWarehouse.Tests;

/// <summary>Проверка, что акт действительно строится (шрифты с кириллицей находятся, PDF не пустой).</summary>
public class ActPdfRendererTests
{
    private static WriteOffActData Data(int lines) => new(
        "АС-000012",
        new DateTimeOffset(2026, 10, 27, 9, 30, 0, TimeSpan.Zero),
        "Отдел эксплуатации технологического оборудования",
        "Петров Сергей Иванович",
        "Слесарь-ремонтник",
        "0123",
        "с 01.10.2026 по 27.10.2026",
        "Наряд № 45",
        "Плановый ремонт линии 1",
        Enumerable.Range(1, lines)
            .Select(i => new WriteOffActLineData(i, $"ЗЧ-{i:D6}", $"Подшипник 6205-2RS, позиция {i}", "шт", i,
                $"L1-P1 — Насос центробежный; замена подшипника; {i:D2}.10.2026"))
            .ToList(),
        new ActApprovalData("Сидоров Олег Петрович", "Начальник участка", new DateTimeOffset(2026, 10, 28, 8, 0, 0, TimeSpan.Zero)),
        null,
        "СП-000031");

    [Fact]
    public void Акт_строится_в_PDF()
    {
        var pdf = new ActPdfRenderer().RenderWriteOffAct(Data(3));

        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void Длинный_акт_переносится_на_несколько_страниц()
    {
        var pdf = new ActPdfRenderer().RenderWriteOffAct(Data(120));
        var text = System.Text.Encoding.Latin1.GetString(pdf);

        // В PDF-файле каждая страница — объект «/Type /Page».
        var pages = System.Text.RegularExpressions.Regex.Matches(text, @"/Type\s*/Page[^s]").Count;
        Assert.True(pages >= 2, $"Ожидалось несколько страниц, получено {pages}.");
    }

    [Fact]
    public void Без_организации_и_необязательных_полей()
    {
        var data = Data(1) with
        {
            Organization = null, Period = null, ExternalNumber = null, Comment = null, EmployeePosition = null,
            EmployeePersonnelNumber = null, Approval = null, DocumentNumber = null,
            Lines = [new WriteOffActLineData(1, "ЗЧ-1", "Ремень", "шт", 1, null)]
        };
        var pdf = new ActPdfRenderer().RenderWriteOffAct(data);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void Черновик_с_надписью_строится()
    {
        var data = Data(40) with { Approval = null, Watermark = "ЧЕРНОВИК", DocumentNumber = null };
        var pdf = new ActPdfRenderer().RenderWriteOffAct(data);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.True(pdf.Length > new ActPdfRenderer().RenderWriteOffAct(data with { Watermark = null }).Length);
    }
}
