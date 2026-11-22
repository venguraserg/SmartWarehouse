using SmartWarehouse.Application.DTOs;

namespace SmartWarehouse.Web.Services;

/// <summary>Раскладывает плоский список оборудования в порядок дерева с уровнем вложенности.</summary>
public static class EquipmentTree
{
    public static List<(EquipmentDto Item, int Depth)> Flatten(IReadOnlyList<EquipmentDto> all)
    {
        var byParent = all.ToLookup(e => e.ParentId);
        var result = new List<(EquipmentDto, int)>();

        void Walk(int? parentId, int depth)
        {
            foreach (var e in byParent[parentId].OrderBy(x => x.Code))
            {
                result.Add((e, depth));
                Walk(e.Id, depth + 1);
            }
        }

        Walk(null, 0);
        return result;
    }

    /// <summary>Дерево для просмотра (как «Склады и места»): верхний уровень — участки, внутри по коду.</summary>
    public static List<EquipmentNode> Build(IReadOnlyList<EquipmentDto> all)
    {
        var byParent = all.ToLookup(e => e.ParentId);
        var ids = all.Select(e => e.Id).ToHashSet();

        List<EquipmentNode> Children(int? parentId, EquipmentNode? parent) =>
            byParent[parentId].OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase).Select(e =>
            {
                var node = new EquipmentNode(e, parent);
                node.Children.AddRange(Children(e.Id, node));
                return node;
            }).ToList();

        // Узлы с потерянным родителем — тоже на верхний уровень, чтобы не пропали из вида.
        var roots = Children(null, null);
        foreach (var orphan in all.Where(e => e.ParentId is int p && !ids.Contains(p)).OrderBy(e => e.Code))
        {
            var node = new EquipmentNode(orphan, null);
            node.Children.AddRange(Children(orphan.Id, node));
            roots.Add(node);
        }
        return roots;
    }
}

/// <summary>Узел дерева оборудования.</summary>
public sealed class EquipmentNode(EquipmentDto item, EquipmentNode? parent)
{
    public EquipmentDto Item { get; } = item;
    public EquipmentNode? Parent { get; } = parent;
    public List<EquipmentNode> Children { get; } = [];
    public int Id => Item.Id;

    /// <summary>Путь от участка: «Цех розлива › Линия 1 › Насос».</summary>
    public IEnumerable<EquipmentNode> Path()
    {
        var chain = new List<EquipmentNode>();
        for (var n = this; n is not null; n = n.Parent)
            chain.Add(n);
        chain.Reverse();
        return chain;
    }

    public IEnumerable<EquipmentNode> Descendants() => Children.SelectMany(c => c.Descendants().Prepend(c));
}
