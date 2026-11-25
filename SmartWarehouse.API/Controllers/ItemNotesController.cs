using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SmartWarehouse.API.Security;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Services;

namespace SmartWarehouse.API.Controllers;

/// <summary>
/// Заметки сотрудников к номенклатуре: аналог / замена, где стоит, совет, проблема.
/// Пишет любой вошедший пользователь; менять — автор; удалять и закреплять — автор, кладовщик, администратор.
/// </summary>
[ApiController]
public class ItemNotesController(IItemNoteService notes) : ControllerBase
{
    [HttpGet("api/items/{itemId:int}/notes")]
    public Task<IReadOnlyList<ItemNoteDto>> List(int itemId, CancellationToken ct) =>
        notes.ListAsync(itemId, User.ToNoteActor(), ct);

    [HttpPost("api/items/{itemId:int}/notes")]
    public Task<ItemNoteDto> Add(int itemId, SaveItemNoteRequest request, CancellationToken ct) =>
        notes.AddAsync(itemId, request, User.ToNoteActor(), ct);

    [HttpPut("api/notes/{id:int}")]
    public Task<ItemNoteDto> Update(int id, SaveItemNoteRequest request, CancellationToken ct) =>
        notes.UpdateAsync(id, request, User.ToNoteActor(), ct);

    [HttpPost("api/notes/{id:int}/pin")]
    public Task<ItemNoteDto> Pin(int id, PinItemNoteRequest request, CancellationToken ct) =>
        notes.PinAsync(id, request.Pinned, User.ToNoteActor(), ct);

    [HttpDelete("api/notes/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await notes.DeleteAsync(id, User.ToNoteActor(), ct);
        return NoContent();
    }
}

internal static class ItemNoteAccess
{
    public static NoteActor ToNoteActor(this ClaimsPrincipal user) => new(
        user.UserId(),
        user.FindFirstValue(AppClaims.DisplayName) ?? user.Identity?.Name ?? "—",
        user.IsWarehouseStaff());
}
