using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartWarehouse.API.Security;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.API.Controllers;

/// <summary>Складские документы: приход, выдача, возврат, перемещение, корректировка; списание — по актам.</summary>
[ApiController]
[Route("api/documents")]
public class DocumentsController(IStockService stock, IEmployeeService employees) : ControllerBase
{
    /// <summary>
    /// Список документов, новые первыми; можно отфильтровать по типу.
    /// Кладовщик и администратор видят все; руководитель — свои и подчинённых; сотрудник и старший смены — свои.
    /// </summary>
    [HttpGet]
    public async Task<IReadOnlyList<DocumentSummaryDto>> List([FromQuery] MovementType? type, [FromQuery] int take = 100, CancellationToken ct = default)
    {
        var visible = await DocumentVisibility.VisibleEmployeesAsync(User, employees, ct);
        return await stock.ListDocumentsAsync(type, take, visible, ct);
    }

    [HttpGet("{id:long}")]
    public async Task<DocumentDto> Get(long id, CancellationToken ct)
    {
        var document = await stock.GetDocumentAsync(id, ct);
        var visible = await DocumentVisibility.VisibleEmployeesAsync(User, employees, ct);
        if (visible is not null
            && !(document.ReceivedById is int r && visible.Contains(r))
            && !(document.PerformedById is int p && visible.Contains(p)))
            throw new ForbiddenException("Нет доступа к этому документу: он не ваш и не ваших подчинённых.");
        return document;
    }

    /// <summary>Акт на списание (PDF) — только для документов списания.</summary>
    [HttpGet("{id:long}/act")]
    public async Task<IActionResult> WriteOffAct(long id, [FromServices] IActService acts, CancellationToken ct)
    {
        var (pdf, fileName) = await acts.GetWriteOffActAsync(id, User.ToActActor(), ct);
        return File(pdf, "application/pdf", fileName);
    }

    /// <summary>
    /// Создать и провести документ. Проводится целиком или не проводится вовсе.
    /// Списание так не проводится — только утверждением акта на списание (api/acts).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = Roles.Issuing)]
    public async Task<ActionResult<DocumentDto>> Post(CreateDocumentRequest request, CancellationToken ct)
    {
        // Старший смены (без роли кладовщика) — только выдача и возврат.
        if (!User.IsWarehouseStaff() && request.Type is not (MovementType.Issue or MovementType.Return))
            throw new ForbiddenException("Старший смены проводит только выдачу со склада и возврат на склад. Остальное — кладовщик.");

        // «Кто проводит» по умолчанию — сотрудник, под чьей учётной записью вошли.
        if (request.PerformedById is null && string.IsNullOrWhiteSpace(request.PerformedBy) && User.EmployeeId() is int me)
            request = request with { PerformedById = me };

        var document = await stock.PostDocumentAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = document.Id }, document);
    }
}

/// <summary>Чьи документы видит пользователь.</summary>
internal static class DocumentVisibility
{
    /// <summary>
    /// Сотрудники, чьи документы видны: null — все (кладовщик, администратор); руководитель — он и подчинённые;
    /// сотрудник и старший смены — только он сам. Свой документ — где сотрудник получатель/сдающий или проводил его.
    /// </summary>
    public static async Task<IReadOnlyCollection<int>?> VisibleEmployeesAsync(ClaimsPrincipal user, IEmployeeService employees, CancellationToken ct)
    {
        if (user.IsWarehouseStaff())
            return null;
        if (user.EmployeeId() is not int me)
            return Array.Empty<int>();
        return user.IsInRole(Roles.Manager) ? await employees.GetTeamAsync(me, ct) : [me];
    }
}
