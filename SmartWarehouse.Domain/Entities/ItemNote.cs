using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.Domain.Entities;

/// <summary>
/// Заметка сотрудника к позиции номенклатуры: аналог, где стоит, совет, проблема.
/// Пишут только пользователи с учётной записью; текст участвует в поиске номенклатуры и остатков.
/// </summary>
public class ItemNote
{
    public int Id { get; set; }

    public int ItemId { get; set; }

    public Item Item { get; set; } = null!;

    public NoteKind Kind { get; set; }

    public string Text { get; set; } = string.Empty;

    /// <summary>Закреплённые заметки показываются первыми.</summary>
    public bool Pinned { get; set; }

    /// <summary>Учётная запись автора (null — запись удалена; имя остаётся в <see cref="AuthorName"/>).</summary>
    public int? AuthorUserId { get; set; }

    public UserAccount? AuthorUser { get; set; }

    /// <summary>Имя автора на момент записи — «Иванов П.А.».</summary>
    public string AuthorName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Когда автор последний раз менял текст или вид (null — не менялась).</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}
