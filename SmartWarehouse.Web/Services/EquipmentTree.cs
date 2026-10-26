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
}
