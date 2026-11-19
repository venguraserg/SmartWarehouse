using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SmartWarehouse.API.Security;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.API.Controllers;

/// <summary>
/// Запросы на выдачу: сотрудник выбирает из остатков — кладовщик или старший смены собирает и выдаёт
/// (документ выдачи с заполненными строками, POST api/documents с IssueRequestId).
/// </summary>
[ApiController]
[Route("api/issue-requests")]
public class IssueRequestsController(IIssueRequestService requests, IEmployeeService employees) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<IssueRequestSummaryDto>> List([FromQuery] IssueRequestStatus? status, CancellationToken ct) =>
        await requests.ListAsync(status, await User.ToIssueRequestActorAsync(employees, ct), ct);

    /// <summary>Сколько запросов ждёт выдачи (для меню кладовщика и старшего смены).</summary>
    [HttpGet("open-count")]
    public async Task<int> OpenCount(CancellationToken ct) =>
        await requests.CountOpenAsync(await User.ToIssueRequestActorAsync(employees, ct), ct);

    [HttpGet("{id:int}")]
    public async Task<IssueRequestDto> Get(int id, CancellationToken ct) =>
        await requests.GetAsync(id, await User.ToIssueRequestActorAsync(employees, ct), ct);

    [HttpPost]
    public async Task<ActionResult<IssueRequestDto>> Create(CreateIssueRequestRequest request, CancellationToken ct)
    {
        var created = await requests.CreateAsync(request, await User.ToIssueRequestActorAsync(employees, ct), ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<IssueRequestDto> Cancel(int id, CancellationToken ct) =>
        await requests.CancelAsync(id, await User.ToIssueRequestActorAsync(employees, ct), ct);

    [HttpPost("{id:int}/reject")]
    public async Task<IssueRequestDto> Reject(int id, RejectIssueRequestRequest request, CancellationToken ct) =>
        await requests.RejectAsync(id, request.Reason, await User.ToIssueRequestActorAsync(employees, ct), ct);
}

internal static class IssueRequestAccess
{
    /// <summary>
    /// Выдают (и видят все запросы) кладовщик, администратор и старший смены;
    /// руководитель видит запросы свои и подчинённых; сотрудник — свои.
    /// </summary>
    public static async Task<IssueRequestActor> ToIssueRequestActorAsync(this ClaimsPrincipal user, IEmployeeService employees, CancellationToken ct)
    {
        var canIssue = user.SeesAllCustody();
        var me = user.EmployeeId();
        IReadOnlyCollection<int>? visible = canIssue ? null
            : me is not int id ? Array.Empty<int>()
            : user.IsInRole(Roles.Manager) ? await employees.GetTeamAsync(id, ct)
            : [id];
        return new IssueRequestActor(me, user.ToActActor().DisplayName, canIssue, visible);
    }
}
