using Microsoft.AspNetCore.Mvc;
using SmartWarehouse.API.Security;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.API.Controllers;

/// <summary>
/// Акты на списание: сотрудник составляет, руководитель утверждает — тогда позиции списываются.
/// Права проверяет сервис: свои акты видит сотрудник, акты подчинённых — руководитель, все — кладовщик и администратор.
/// </summary>
[ApiController]
[Route("api/acts")]
public class ActsController(IWriteOffActService acts) : ControllerBase
{
    /// <summary>Список. scope=Visible — все доступные; scope=ToApprove — ждут моего утверждения.</summary>
    [HttpGet]
    public Task<IReadOnlyList<ActSummaryDto>> List(
        [FromQuery] ActScope scope = ActScope.Visible, [FromQuery] ActStatus? status = null, [FromQuery] int? employeeId = null,
        CancellationToken ct = default) =>
        acts.ListAsync(scope, status, employeeId, User.ToActActor(), ct);

    /// <summary>Сколько актов ждут моего утверждения (для значка в меню).</summary>
    [HttpGet("to-approve/count")]
    public async Task<object> CountToApprove(CancellationToken ct) =>
        new { count = await acts.CountToApproveAsync(User.ToActActor(), ct) };

    [HttpGet("{id:int}")]
    public Task<ActDto> Get(int id, CancellationToken ct) => acts.GetAsync(id, User.ToActActor(), ct);

    /// <summary>Акт в PDF; неутверждённый — с надписью «ЧЕРНОВИК» / «НА УТВЕРЖДЕНИИ».</summary>
    [HttpGet("{id:int}/pdf")]
    public async Task<IActionResult> Pdf(int id, [FromServices] IActService pdf, CancellationToken ct)
    {
        var (bytes, fileName) = await pdf.GetActPdfAsync(id, User.ToActActor(), ct);
        return File(bytes, "application/pdf", fileName);
    }

    [HttpPost]
    public async Task<ActionResult<ActDto>> Create(SaveActRequest request, CancellationToken ct)
    {
        var act = await acts.CreateAsync(request, User.ToActActor(), ct);
        return CreatedAtAction(nameof(Get), new { id = act.Id }, act);
    }

    [HttpPut("{id:int}")]
    public Task<ActDto> Update(int id, SaveActRequest request, CancellationToken ct) =>
        acts.UpdateAsync(id, request, User.ToActActor(), ct);

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await acts.DeleteAsync(id, User.ToActActor(), ct);
        return NoContent();
    }

    [HttpPost("{id:int}/submit")]
    public Task<ActDto> Submit(int id, CancellationToken ct) => acts.SubmitAsync(id, User.ToActActor(), ct);

    [HttpPost("{id:int}/recall")]
    public Task<ActDto> Recall(int id, CancellationToken ct) => acts.RecallAsync(id, User.ToActActor(), ct);

    [HttpPost("{id:int}/approve")]
    public Task<ActDto> Approve(int id, CancellationToken ct) => acts.ApproveAsync(id, User.ToActActor(), ct);

    [HttpPost("{id:int}/return")]
    public Task<ActDto> Return(int id, ReturnActRequest request, CancellationToken ct) =>
        acts.ReturnAsync(id, request.Remark, User.ToActActor(), ct);
}
