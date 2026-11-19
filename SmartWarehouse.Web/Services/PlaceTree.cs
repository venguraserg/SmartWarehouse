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

    /// <summary>Места одного склада; подписи-пути строятся из описаний (уровень без описания — «стеллаж A»).</summary>
    /// <summary>То же дерево для общего компонента выбора (TreePicker).</summary>
    public PickTree Pick { get; } = new();

    public PlaceTree AddGroup(string? warehouse, IReadOnlyList<LocationNodeDto> roots)
    {
        Groups.Add(new PlaceGroup(warehouse, roots));
        Walk(roots, warehouse);
        Pick.AddGroup(warehouse, roots.Select(ToPick).ToList());
        return this;

        static PickNode ToPick(LocationNodeDto n) =>
            new(n.Id, n.ParentId, Ui.LocType(n.Type), LabelOf(n), n.FullCode, n.Children.Select(ToPick).ToList());

        void Walk(IEnumerable<LocationNodeDto> nodes, string? parentPath)
        {
            foreach (var node in nodes)
            {
                var label = LabelOf(node);
                var path = parentPath is null ? label : $"{parentPath} › {label}";
                Paths[node.Id] = path;
                ById[node.Id] = node;
                All.Add(node);
                Walk(node.Children, path);
            }
        }
    }

    /// <summary>Подпись места: описание, а без него — тип и код («стеллаж A»).</summary>
    public static string LabelOf(LocationNodeDto n) =>
        string.IsNullOrWhiteSpace(n.Name) ? $"{Ui.LocType(n.Type).ToLowerInvariant()} {n.Code}" : n.Name.Trim();

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
