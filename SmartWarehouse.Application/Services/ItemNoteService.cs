using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Application.Services;

public interface IItemNoteService
{
    Task<IReadOnlyList<ItemNoteDto>> ListAsync(int itemId, NoteActor actor, CancellationToken ct = default);

    Task<ItemNoteDto> AddAsync(int itemId, SaveItemNoteRequest request, NoteActor actor, CancellationToken ct = default);

    Task<ItemNoteDto> UpdateAsync(int noteId, SaveItemNoteRequest request, NoteActor actor, CancellationToken ct = default);

    Task<ItemNoteDto> PinAsync(int noteId, bool pinned, NoteActor actor, CancellationToken ct = default);

    Task DeleteAsync(int noteId, NoteActor actor, CancellationToken ct = default);
}

/// <summary>
/// Заметки к номенклатуре. Пишет любой пользователь с учётной записью; менять текст может только автор;
/// удалить и закрепить — автор, кладовщик или администратор.
/// </summary>
public class ItemNoteService(IItemNoteRepository repo, TimeProvider clock) : IItemNoteService
{
    public const int MaxLength = 1000;

    public async Task<IReadOnlyList<ItemNoteDto>> ListAsync(int itemId, NoteActor actor, CancellationToken ct = default)
    {
        if (!await repo.ItemExistsAsync(itemId, ct))
            throw new NotFoundException($"Позиция {itemId} не найдена.");
        return (await repo.ListAsync(itemId, ct)).Select(n => ToDto(n, actor)).ToList();
    }

    public async Task<ItemNoteDto> AddAsync(int itemId, SaveItemNoteRequest request, NoteActor actor, CancellationToken ct = default)
    {
        if (!await repo.ItemExistsAsync(itemId, ct))
            throw new NotFoundException($"Позиция {itemId} не найдена.");
        var text = Validate(request);
        var note = new ItemNote
        {
            ItemId = itemId,
            Kind = request.Kind,
            Text = text,
            Pinned = request.Pinned,
            AuthorUserId = actor.UserId,
            AuthorName = string.IsNullOrWhiteSpace(actor.Name) ? "—" : actor.Name.Trim(),
            CreatedAt = clock.GetUtcNow()
        };
        repo.Add(note);
        await repo.SaveChangesAsync(ct);
        return ToDto(note, actor);
    }

    public async Task<ItemNoteDto> UpdateAsync(int noteId, SaveItemNoteRequest request, NoteActor actor, CancellationToken ct = default)
    {
        var note = await GetAsync(noteId, ct);
        if (note.AuthorUserId != actor.UserId)
            throw new ForbiddenException("Изменить заметку может только её автор.");
        var text = Validate(request);
        if (note.Text != text || note.Kind != request.Kind)
            note.UpdatedAt = clock.GetUtcNow();
        note.Text = text;
        note.Kind = request.Kind;
        note.Pinned = request.Pinned;
        await repo.SaveChangesAsync(ct);
        return ToDto(note, actor);
    }

    public async Task<ItemNoteDto> PinAsync(int noteId, bool pinned, NoteActor actor, CancellationToken ct = default)
    {
        var note = await GetAsync(noteId, ct);
        if (!CanManage(note, actor))
            throw new ForbiddenException("Закрепить чужую заметку могут кладовщик и администратор.");
        note.Pinned = pinned;
        await repo.SaveChangesAsync(ct);
        return ToDto(note, actor);
    }

    public async Task DeleteAsync(int noteId, NoteActor actor, CancellationToken ct = default)
    {
        var note = await GetAsync(noteId, ct);
        if (!CanManage(note, actor))
            throw new ForbiddenException("Удалить чужую заметку могут кладовщик и администратор.");
        repo.Remove(note);
        await repo.SaveChangesAsync(ct);
    }

    private async Task<ItemNote> GetAsync(int noteId, CancellationToken ct) =>
        await repo.GetAsync(noteId, ct) ?? throw new NotFoundException($"Заметка {noteId} не найдена.");

    private static string Validate(SaveItemNoteRequest request)
    {
        if (!Enum.IsDefined(request.Kind))
            throw new DomainException("Укажите вид заметки.");
        var text = request.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
            throw new DomainException("Напишите текст заметки.");
        if (text.Length > MaxLength)
            throw new DomainException($"Заметка длиннее {MaxLength} символов.");
        return text;
    }

    private static bool CanManage(ItemNote note, NoteActor actor) => actor.IsStaff || note.AuthorUserId == actor.UserId;

    private static ItemNoteDto ToDto(ItemNote n, NoteActor actor)
    {
        var mine = n.AuthorUserId == actor.UserId;
        return new ItemNoteDto(n.Id, n.ItemId, n.Kind, n.Text, n.Pinned, n.AuthorName, mine, n.CreatedAt, n.UpdatedAt,
            CanEdit: mine, CanDelete: CanManage(n, actor), CanPin: CanManage(n, actor));
    }

    /// <summary>Отрывок текста вокруг найденного слова: «…подходит 6205-2Z — проверено на гомогенизаторе…».</summary>
    public static string Snippet(string text, string search, int around = 40) =>
        Snippet(text, SearchWords.Split(search), around);

    /// <summary>Отрывок вокруг первого найденного из слов.</summary>
    public static string Snippet(string text, IReadOnlyList<string> words, int around = 40)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var (at, search) = SearchWords.FirstMatch(flat, words);
        if (at < 0)
            return flat.Length <= around * 2 ? flat : flat[..(around * 2)].TrimEnd() + "…";

        var start = Math.Max(0, at - around);
        var end = Math.Min(flat.Length, at + search.Length + around);
        // Не резать слова пополам.
        if (start > 0)
        {
            var space = flat.IndexOf(' ', start);
            if (space >= 0 && space < at)
                start = space + 1;
        }
        if (end < flat.Length)
        {
            var space = flat.LastIndexOf(' ', end);
            if (space > at + search.Length)
                end = space;
        }
        return (start > 0 ? "…" : "") + flat[start..end] + (end < flat.Length ? "…" : "");
    }
}
