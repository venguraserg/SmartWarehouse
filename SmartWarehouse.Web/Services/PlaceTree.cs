using SmartWarehouse.Application.DTOs;

namespace SmartWarehouse.Web.Services;

/// <summary>Места хранения одного склада (подпись склада — если складов несколько).</summary>
public sealed record PlaceGroup(string? Warehouse, IReadOnlyList<LocationNodeDto> Roots);

/// <summary>Все места хранения: деревом по складам, списком и подписями-путями из описаний.</summary>
public sealed class PlaceTree
{
    public List<PlaceGroup> Groups { get; } = [];

    /// <summary>Все места (любого уровня) в порядке дерева.</summary>
    public List<LocationNodeDto> All { get; } = [];

    public Dictionary<int, LocationNodeDto> ById { get; } = [];

    /// <summary>Подпись места: «Склад › Стеллаж у ворот › Подшипники».</summary>
    public Dictionary<int, string> Paths { get; } = [];

    public string PathOf(int id, string? fallback = null) =>
        Paths.TryGetValue(id, out var path) ? path : fallback ?? id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Само место и все вложенные в него.</summary>
    public IEnumerable<int> SubtreeOf(int id)
    {
        if (!ById.TryGetValue(id, out var node))
            yield break;
        var stack = new Stack<LocationNodeDto>([node]);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            yield return n.Id;
            foreach (var c in n.Children)
                stack.Push(c);
        }
    }

    /// <summary>Места от корня до указанного (включительно).</summary>
    public List<int> PathIds(int id)
    {
        var ids = new List<int>();
        int? current = id;
        while (current is int c && ById.TryGetValue(c, out var node) && ids.Count < 50)
        {
            ids.Insert(0, c);
            current = node.ParentId;
        }
        return ids;
    }
}
