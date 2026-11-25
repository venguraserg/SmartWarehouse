using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.Application.Services;

/// <summary>
/// Места склада деревом в памяти — для адресов словами и обхода «как клеить / как считать»:
/// полки сверху вниз (пол — последним), остальное по номеру.
/// </summary>
public sealed class LocationTree
{
    public IReadOnlyDictionary<int, LocationNodeDto> ById { get; }

    private LocationTree(Dictionary<int, LocationNodeDto> byId) => ById = byId;

    public static LocationTree Build(IEnumerable<StorageLocation> locations)
    {
        var all = locations.ToList();
        var byParent = all.ToLookup(l => l.ParentId);
        var tree = new Dictionary<int, LocationNodeDto>();

        LocationNodeDto Node(StorageLocation l)
        {
            var node = new LocationNodeDto(l.Id, l.ParentId, l.Type, l.Code, l.FullCode, l.Name,
                byParent[l.Id].Select(Node).ToList(), l.CheckedAt);
            tree[l.Id] = node;
            return node;
        }

        foreach (var root in byParent[null])
            Node(root);
        // Место, чей родитель не попал в выборку (не должно быть), — как корень.
        foreach (var l in all.Where(l => !tree.ContainsKey(l.Id)))
            Node(l);
        return new LocationTree(tree);
    }

    /// <summary>Цепочка от верхнего уровня до места.</summary>
    public List<LocationNodeDto> Chain(int id)
    {
        var chain = new List<LocationNodeDto>();
        for (int? at = id; at is int i && ById.TryGetValue(i, out var node); at = node.ParentId)
            chain.Insert(0, node);
        return chain;
    }

    public PlaceAddress Address(int id) => PlaceAddress.Build(Chain(id));

    /// <summary>По номеру, если код — число («2» раньше «10»), иначе по алфавиту.</summary>
    public static List<LocationNodeDto> Ordered(IEnumerable<LocationNodeDto> nodes) =>
        nodes.OrderBy(n => int.TryParse(n.Code, out var x) ? x : int.MaxValue)
             .ThenBy(n => n.Code, StringComparer.OrdinalIgnoreCase)
             .ToList();

    /// <summary>Дети в порядке обхода: полки сверху вниз (пол — последним), затем остальное по номеру.</summary>
    public static List<LocationNodeDto> WalkOrder(LocationNodeDto node)
    {
        var shelves = Ordered(node.Children.Where(c => c.Type == LocationType.Shelf));
        shelves.Reverse();
        return [.. shelves, .. Ordered(node.Children.Where(c => c.Type != LocationType.Shelf))];
    }

    /// <summary>Само место и всё вложенное — в порядке обхода (сверху вниз).</summary>
    public IEnumerable<LocationNodeDto> Walk(int rootId)
    {
        if (!ById.TryGetValue(rootId, out var root))
            yield break;
        var stack = new Stack<LocationNodeDto>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            var children = WalkOrder(node);
            for (var i = children.Count - 1; i >= 0; i--)
                stack.Push(children[i]);
        }
    }
}
