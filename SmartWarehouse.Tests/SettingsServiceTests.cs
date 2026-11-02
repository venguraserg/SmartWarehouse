using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Exceptions;
using SmartWarehouse.Infrastructure.Pdf;

namespace SmartWarehouse.Tests;

public class SettingsServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeSettingsRepository _repo = new();
    private readonly SettingsService _service;

    public SettingsServiceTests()
    {
        _service = new SettingsService(_repo, new DocumentPrintOptions(" Отдел эксплуатации "), new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task По_умолчанию_код_в_QR_и_стандартный_акт()
    {
        Assert.Equal(new LabelSettingsDto(QrContent.Code, null), await _service.GetLabelSettingsAsync());

        var act = await _service.GetActTemplateAsync();
        Assert.Equal("Отдел эксплуатации", act.Organization); // из файла настроек
        Assert.Equal(SettingsService.DefaultSubtitle, act.Subtitle);
        Assert.Equal(3, act.BlankMembers);
        Assert.True(act.ResponsibleSigns);
        Assert.Empty(act.Members);
        Assert.Empty(_repo.Settings);
    }

    [Fact]
    public async Task Настройки_наклеек_сохраняются_и_проверяются()
    {
        var saved = await _service.SaveLabelSettingsAsync(new LabelSettingsDto(QrContent.Link, "http://sklad:8081/"), "Админ");

        Assert.Equal("http://sklad:8081", saved.BaseUrl);
        Assert.Equal(saved, await _service.GetLabelSettingsAsync());
        var row = Assert.Single(_repo.Settings);
        Assert.Equal("Админ", row.UpdatedBy);
        Assert.Equal(Now, row.UpdatedAt);

        await Assert.ThrowsAsync<DomainException>(() => _service.SaveLabelSettingsAsync(new LabelSettingsDto(QrContent.Link, "sklad"), null));
        await Assert.ThrowsAsync<DomainException>(() => _service.SaveLabelSettingsAsync(new LabelSettingsDto((QrContent)7, null), null));
    }

    [Fact]
    public async Task Шаблон_акта_сохраняется_без_пустых_строк_комиссии()
    {
        await _service.SaveActTemplateAsync(new ActTemplateDto(
            "  ", " на списание запасных частей ", "Комиссия установила:", " ",
            new CommissionMemberDto(" Главный механик ", "Сидоров О. П."),
            [new("Мастер", "Петров И. И."), new(" ", null), new(null, "Козлов А. А.")],
            1, false), "Админ");

        var act = await _service.GetActTemplateAsync();
        Assert.Null(act.Organization);
        Assert.Equal("на списание запасных частей", act.Subtitle);
        Assert.Equal(SettingsService.DefaultApproverPosition, act.ApproverPosition);
        Assert.Equal(new CommissionMemberDto("Главный механик", "Сидоров О. П."), act.Chair);
        Assert.Equal([new("Мастер", "Петров И. И."), new(null, "Козлов А. А.")], act.Members);
        Assert.Equal(1, act.BlankMembers);
        Assert.False(act.ResponsibleSigns);
    }

    [Fact]
    public async Task Неверный_шаблон_акта_отклоняется()
    {
        var ok = _service.DefaultActTemplate;

        await Assert.ThrowsAsync<DomainException>(() => _service.SaveActTemplateAsync(ok with { Subtitle = " " }, null));
        await Assert.ThrowsAsync<DomainException>(() => _service.SaveActTemplateAsync(ok with { Intro = "" }, null));
        await Assert.ThrowsAsync<DomainException>(() => _service.SaveActTemplateAsync(ok with { BlankMembers = 11 }, null));
        await Assert.ThrowsAsync<DomainException>(() => _service.SaveActTemplateAsync(
            ok with { Members = Enumerable.Range(1, 11).Select(i => new CommissionMemberDto("Мастер", $"Член {i}")).ToList() }, null));
        await Assert.ThrowsAsync<DomainException>(() => _service.SaveActTemplateAsync(ok with { Organization = new string('х', 301) }, null));
    }

    [Fact]
    public void Акт_по_шаблону_строится_в_PDF()
    {
        var template = new ActTemplateDto("Комбинат", "на списание запчастей", "Комиссия установила:", "Главный инженер",
            new CommissionMemberDto("Главный механик", "Сидоров Олег Петрович"),
            [new("Мастер участка", "Петров Иван Иванович"), new(null, "Козлов Андрей Андреевич")], 0, false);
        var data = new WriteOffActData("АС-000001", Now, template.Organization, "Иванов Иван Иванович", null, null, null, null, null,
            [new WriteOffActLineData(1, "ЗЧ-1", "Ремень", "шт", 1, null)], Watermark: "ОБРАЗЕЦ", Template: template);

        var pdf = new ActPdfRenderer().RenderWriteOffAct(data);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }
}

internal class FakeSettingsRepository : ISettingsRepository
{
    public List<AppSetting> Settings { get; } = [];

    public Task<AppSetting?> GetAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(Settings.FirstOrDefault(s => s.Key == key));

    public void Add(AppSetting setting) => Settings.Add(setting);

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}
