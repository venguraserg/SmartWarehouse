using System.Text.Json;
using System.Text.Json.Serialization;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Application.Services;

public interface ISettingsService
{
    Task<LabelSettingsDto> GetLabelSettingsAsync(CancellationToken ct = default);

    Task<LabelSettingsDto> SaveLabelSettingsAsync(LabelSettingsDto settings, string? by, CancellationToken ct = default);

    Task<ActTemplateDto> GetActTemplateAsync(CancellationToken ct = default);

    Task<ActTemplateDto> SaveActTemplateAsync(ActTemplateDto template, string? by, CancellationToken ct = default);
}

/// <summary>
/// Настройки, которые администратор меняет в приложении. Пока раздел не сохранялся — действуют значения по умолчанию
/// (организация в шапке акта — из файла настроек Documents:Organization).
/// </summary>
public class SettingsService(ISettingsRepository repository, DocumentPrintOptions print, TimeProvider clock) : ISettingsService
{
    public const string LabelsKey = "labels";
    public const string ActTemplateKey = "act-template";

    public const int MaxMembers = 10;
    public const int MaxBlankMembers = 10;

    public const string DefaultSubtitle = "на списание материальных ценностей";

    public const string DefaultIntro =
        "Комиссия в составе лиц, подписавших настоящий акт, установила, что перечисленные ниже материальные " +
        "ценности, числящиеся за материально ответственным лицом, израсходованы (использованы) и подлежат списанию:";

    public const string DefaultApproverPosition = "Руководитель";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static readonly LabelSettingsDto DefaultLabels = new(QrContent.Code, null);

    public ActTemplateDto DefaultActTemplate => new(
        Clean(print.Organization), DefaultSubtitle, DefaultIntro, DefaultApproverPosition, null, [], 3, true);

    // ---------- Наклейки ----------

    public async Task<LabelSettingsDto> GetLabelSettingsAsync(CancellationToken ct = default) =>
        await ReadAsync<LabelSettingsDto>(LabelsKey, ct) ?? DefaultLabels;

    public async Task<LabelSettingsDto> SaveLabelSettingsAsync(LabelSettingsDto settings, string? by, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(settings.QrContent))
            throw new DomainException("Неизвестный вид QR-кода.");
        var clean = new LabelSettingsDto(settings.QrContent, LabelService.NormalizeBaseUrl(settings.BaseUrl));
        await WriteAsync(LabelsKey, clean, by, ct);
        return clean;
    }

    // ---------- Шаблон акта ----------

    public async Task<ActTemplateDto> GetActTemplateAsync(CancellationToken ct = default) =>
        await ReadAsync<ActTemplateDto>(ActTemplateKey, ct) is { } saved
            ? saved with { Members = saved.Members ?? [] }
            : DefaultActTemplate;

    public async Task<ActTemplateDto> SaveActTemplateAsync(ActTemplateDto template, string? by, CancellationToken ct = default)
    {
        var subtitle = Clean(template.Subtitle) ?? throw new DomainException("Укажите строку под заголовком акта.");
        var intro = Clean(template.Intro) ?? throw new DomainException("Укажите текст перед таблицей.");
        var approver = Clean(template.ApproverPosition) ?? DefaultApproverPosition;

        var members = (template.Members ?? [])
            .Select(Member)
            .OfType<CommissionMemberDto>()
            .ToList();
        if (members.Count > MaxMembers)
            throw new DomainException($"Членов комиссии — не больше {MaxMembers}.");
        if (template.BlankMembers is < 0 or > MaxBlankMembers)
            throw new DomainException($"Пустых строк для членов комиссии — от 0 до {MaxBlankMembers}.");

        var clean = new ActTemplateDto(
            Limit(Clean(template.Organization), 300, "Организация"),
            Limit(subtitle, 200, "Строка под заголовком")!,
            Limit(intro, 2000, "Текст перед таблицей")!,
            Limit(approver, 200, "Должность утверждающего")!,
            Member(template.Chair),
            members,
            template.BlankMembers,
            template.ResponsibleSigns);

        await WriteAsync(ActTemplateKey, clean, by, ct);
        return clean;
    }

    private static CommissionMemberDto? Member(CommissionMemberDto? m)
    {
        if (m is null)
            return null;
        var position = Limit(Clean(m.Position), 150, "Должность члена комиссии");
        var name = Limit(Clean(m.Name), 150, "ФИО члена комиссии");
        return position is null && name is null ? null : new CommissionMemberDto(position, name);
    }

    // ---------- Хранение ----------

    private async Task<T?> ReadAsync<T>(string key, CancellationToken ct) where T : class
    {
        var setting = await repository.GetAsync(key, ct);
        if (setting is null)
            return null;
        try
        {
            return JsonSerializer.Deserialize<T>(setting.Value, Json);
        }
        catch (JsonException)
        {
            return null; // повреждённое значение — как будто не сохранялось
        }
    }

    private async Task WriteAsync<T>(string key, T value, string? by, CancellationToken ct)
    {
        var setting = await repository.GetAsync(key, ct);
        if (setting is null)
        {
            setting = new AppSetting { Key = key };
            repository.Add(setting);
        }
        setting.Value = JsonSerializer.Serialize(value, Json);
        setting.UpdatedAt = clock.GetUtcNow();
        setting.UpdatedBy = by;
        await repository.SaveChangesAsync(ct);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Limit(string? value, int max, string field) =>
        value is not null && value.Length > max
            ? throw new DomainException($"{field}: не длиннее {max} символов.")
            : value;
}
