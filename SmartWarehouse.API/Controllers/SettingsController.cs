using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartWarehouse.API.Security;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Services;

namespace SmartWarehouse.API.Controllers;

/// <summary>Настройки приложения: наклейки (что в QR-коде) и шаблон акта на списание. Меняет администратор.</summary>
[ApiController]
[Route("api/settings")]
public class SettingsController(ISettingsService settings) : ControllerBase
{
    // Атрибуты контроллера и метода складываются (нужны оба), поэтому роли — у каждого метода.

    /// <summary>Настройки наклеек — их показывает и страница печати наклеек.</summary>
    [HttpGet("labels")]
    [Authorize(Roles = Roles.Warehouse)]
    public Task<LabelSettingsDto> GetLabels(CancellationToken ct) => settings.GetLabelSettingsAsync(ct);

    [HttpPut("labels")]
    [Authorize(Roles = Roles.Admin)]
    public Task<LabelSettingsDto> SaveLabels(LabelSettingsDto request, CancellationToken ct) =>
        settings.SaveLabelSettingsAsync(request, By(), ct);

    [HttpGet("act-template")]
    [Authorize(Roles = Roles.Admin)]
    public Task<ActTemplateDto> GetActTemplate(CancellationToken ct) => settings.GetActTemplateAsync(ct);

    [HttpPut("act-template")]
    [Authorize(Roles = Roles.Admin)]
    public Task<ActTemplateDto> SaveActTemplate(ActTemplateDto request, CancellationToken ct) =>
        settings.SaveActTemplateAsync(request, By(), ct);

    /// <summary>Образец акта по сохранённому шаблону.</summary>
    [HttpGet("act-template/sample")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Sample([FromServices] IActService acts, CancellationToken ct)
    {
        var (bytes, fileName) = await acts.GetSampleAsync(ct);
        return File(bytes, "application/pdf", fileName);
    }

    private string? By() => User.FindFirstValue(AppClaims.DisplayName) ?? User.Identity?.Name;
}
