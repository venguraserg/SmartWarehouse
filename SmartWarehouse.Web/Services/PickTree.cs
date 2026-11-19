using SmartWarehouse.Application.DTOs;

namespace SmartWarehouse.Web.Services;

/// <summary>Узел дерева для выбора (место хранения, оборудование).</summary>
/// <param name="Kind">Подпись уровня мелким шрифтом: «Полка», «Ячейка»; null — без неё.</param>
public sealed record PickNode(int Id, int? ParentId, string? Kind, string Label, string? Code, List<PickNode> Children);

/// <summary>Группа узлов верхнего уровня (например, склад, если складов несколько).</summary>
public sealed record PickGroup(string? Title, IReadOnlyList<PickNode> Roots);

/// <summary>Дерево для <c>TreePicker</c>: группы, все узлы, пути-подписи «Цех › Линия › Агрегат».</summary>
public sealed class PickTree
{
    public List<PickGroup> Groups { get; } = [];

    /// <summary>Все узлы в порядке дерева.</summary>
    public List<PickNode> All { get; } = [];

    public Dictionary<int, PickNode> ById { get; } = [];

    public Dictionary<int, string> Paths { get; } = [];

    public PickTree AddGroup(string? title, IReadOnlyList<PickNode> roots)
    {
        Groups.Add(new PickGroup(title, roots));
        Walk(roots, title);
        return this;

        void Walk(IEnumerable<PickNode> nodes, string? parentPath)
        {
            foreach (var node in nodes)
            {
                var path = parentPath is null ? node.Label : $"{parentPath} › {node.Label}";
                Paths[node.Id] = path;
                ById[node.Id] = node;
                All.Add(node);
                Walk(node.Children, path);
            }
        }
    }

    public string PathOf(int id, string? fallback = null) =>
        Paths.TryGetValue(id, out var path) ? path : fallback ?? id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Сам узел и все вложенные.</summary>
    public IEnumerable<int> SubtreeOf(int id)
    {
        if (!ById.TryGetValue(id, out var node))
            yield break;
        var stack = new Stack<PickNode>([node]);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            yield return n.Id;
            foreach (var c in n.Children)
                stack.Push(c);
        }
    }

    /// <summary>Узлы от корня до указанного (включительно).</summary>
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

    /// <summary>Оборудование: цех → линия → агрегат (по коду внутри уровня).</summary>
    public static PickTree FromEquipment(IReadOnlyList<EquipmentDto> all)
    {
        var byParent = all.ToLookup(e => e.ParentId);
        List<PickNode> Build(int? parentId) =>
            byParent[parentId].OrderBy(e => e.Code, StringComparer.OrdinalIgnoreCase)
                .Select(e => new PickNode(e.Id, e.ParentId, null, e.Name, e.Code, Build(e.Id)))
                .ToList();
        return new PickTree().AddGroup(null, Build(null));
    }
}
