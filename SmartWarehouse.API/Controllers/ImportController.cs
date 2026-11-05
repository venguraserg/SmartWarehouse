using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartWarehouse.API.Security;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.API.Controllers;

/// <summary>Загрузка из Excel: начальные остатки (администратор) и сверка с сальдовыми ведомостями 1С (склад).</summary>
[ApiController]
[Route("api/import")]
public class ImportController(IImportFileReader reader, IOpeningImportService opening, IReconcileService reconcile) : ControllerBase
{
    private const long MaxFileBytes = 30 * 1024 * 1024;

    /// <summary>
    /// Начальные остатки из файла «Начальные остатки — готово (1С на …)» (multipart, поле «file»).
    /// apply=false — только проверка (ничего не меняет), apply=true — загрузка.
    /// </summary>
    [HttpPost("opening")]
    [Authorize(Roles = Roles.Admin)]
    [RequestSizeLimit(MaxFileBytes + 1024 * 1024)]
    public async Task<OpeningImportResult> Opening(IFormFile file, [FromQuery] int warehouseId, [FromQuery] bool apply, CancellationToken ct)
    {
        await using var stream = await BufferAsync(file, ct);
        var data = reader.ReadOpening(stream);
        return await opening.ImportAsync(data, warehouseId, apply, new ImportActor(User.EmployeeId(), User.FindFirst(AppClaims.DisplayName)?.Value), ct);
    }

    /// <summary>Сверка с «Сальдовыми ведомостями ТМЦ» 1С (multipart, поле «files», один или несколько файлов).</summary>
    [HttpPost("reconcile")]
    [Authorize(Roles = Roles.Warehouse)]
    [RequestSizeLimit(MaxFileBytes + 1024 * 1024)]
    public async Task<ReconcileResultDto> Reconcile(List<IFormFile> files, [FromQuery] string? warehouseMol, CancellationToken ct)
    {
        if (files.Count == 0)
            throw new DomainException("Выберите один или несколько файлов «Сальдовая ведомость ТМЦ» из 1С.");
        var sheets = new List<SaldoSheet>();
        foreach (var file in files)
        {
            await using var stream = await BufferAsync(file, ct);
            sheets.Add(reader.ReadSaldo(stream, file.FileName));
        }
        return await reconcile.ReconcileAsync(sheets, warehouseMol, ct);
    }

    private static async Task<MemoryStream> BufferAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0)
            throw new DomainException("Файл пустой.");
        if (file.Length > MaxFileBytes)
            throw new DomainException($"Файл больше {MaxFileBytes / 1024 / 1024} МБ.");
        var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        return buffer;
    }
}
