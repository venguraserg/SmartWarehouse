using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Application.Services;

public interface IReferenceAdminService
{
    Task<ReferenceChangeResult> UpdateWarehouseAsync(int id, UpdateWarehouseRequest request, CancellationToken ct = default);
    Task<ReferenceChangeResult> DeleteWarehouseAsync(int id, CancellationToken ct = default);

    Task<ReferenceChangeResult> UpdateLocationAsync(int id, UpdateLocationRequest request, CancellationToken ct = default);
    Task<ReferenceChangeResult> DeleteLocationAsync(int id, CancellationToken ct = default);

    Task<ReferenceChangeResult> UpdateCategoryAsync(int id, UpdateCategoryRequest request, CancellationToken ct = default);
    Task<ReferenceChangeResult> DeleteCategoryAsync(int id, CancellationToken ct = default);

    Task<ReferenceChangeResult> UpdateUnitAsync(int id, UpdateUnitRequest request, CancellationToken ct = default);
    Task<ReferenceChangeResult> DeleteUnitAsync(int id, CancellationToken ct = default);

    Task<ReferenceChangeResult> UpdateEquipmentAsync(int id, UpdateEquipmentRequest request, CancellationToken ct = default);
    Task<ReferenceChangeResult> DeleteEquipmentAsync(int id, CancellationToken ct = default);
}

/// <summary>
/// Исправление справочников администратором. Учётные данные при этом не теряются:
/// документы и остатки ссылаются на записи по номеру, а не по коду или названию.
/// Ограничения:
/// — у места хранения при смене кода или вложения меняется полный код (и у всех вложенных) — их QR-наклейки нужно перепечатать;
/// — товар хранится только в конечных местах: вложить место в место с товаром нельзя;
/// — удалить можно только то, что ни разу не использовалось в учёте (иначе пропала бы история).
/// </summary>
public class ReferenceAdminService(IReferenceAdminRepository repo) : IReferenceAdminService
{
    // ---------- Склады ----------

    public async Task<ReferenceChangeResult> UpdateWarehouseAsync(int id, UpdateWarehouseRequest request, CancellationToken ct = default)
    {
        var warehouse = await repo.GetWarehouseAsync(id, ct) ?? throw new NotFoundException($"Склад {id} не найден.");
        var code = CodeRules.NormalizePart(request.Code, "Склад");
        var name = Clean(request.Name) ?? throw new DomainException("Название склада обязательно.");

        if (code != warehouse.Code && await repo.WarehouseCodeExistsAsync(code, id, ct))
            throw new DomainException($"Склад с кодом «{code}» уже существует.");

        warehouse.Name = name;
        warehouse.Description = Clean(request.Description);

        IReadOnlyList<int> changed = [];
        if (code != warehouse.Code)
        {
            // Код склада — начало полного кода каждого его места.
            var all = await repo.GetWarehouseLocationsAsync(id, ct);
            warehouse.Code = code;
            changed = Recode(all, all.Where(l => l.ParentId is null), code);
        }

        await repo.SaveChangesAsync(ct);
        return new ReferenceChangeResult(
            changed.Count == 0
                ? $"Склад {warehouse.Code} сохранён."
                : $"Склад {warehouse.Code} сохранён. Изменились коды мест: {changed.Count} — перепечатайте их QR-наклейки.",
            changed);
    }

    public async Task<ReferenceChangeResult> DeleteWarehouseAsync(int id, CancellationToken ct = default)
    {
        var warehouse = await repo.GetWarehouseAsync(id, ct) ?? throw new NotFoundException($"Склад {id} не найден.");
        if ((await repo.GetWarehouseLocationsAsync(id, ct)).Count > 0)
            throw new DomainException($"В складе {warehouse.Code} есть места хранения — сначала удалите их.");

        repo.Remove(warehouse);
        await repo.SaveChangesAsync(ct);
        return new ReferenceChangeResult($"Склад {warehouse.Code} удалён.");
    }

    // ---------- Места хранения ----------

    public async Task<ReferenceChangeResult> UpdateLocationAsync(int id, UpdateLocationRequest request, CancellationToken ct = default)
    {
        var location = await repo.GetLocationAsync(id, ct) ?? throw new NotFoundException($"Место хранения {id} не найдено.");
        if (!Enum.IsDefined(request.Type))
            throw new DomainException($"Неизвестный тип места хранения: {request.Type}.");

        var code = CodeRules.NormalizePart(request.Code, "Место хранения");
        var description = CodeRules.LocationDescription(request.Name);
        var warehouse = await repo.GetWarehouseAsync(location.WarehouseId, ct)
            ?? throw new NotFoundException($"Склад {location.WarehouseId} не найден.");
        var all = await repo.GetWarehouseLocationsAsync(location.WarehouseId, ct);
        var byId = all.ToDictionary(l => l.Id);
        var self = byId[id]; // та же отслеживаемая сущность
        var children = all.Where(l => l.ParentId == id).ToList();

        StorageLocation? parent = null;
        if (request.ParentId is int parentId)
        {
            if (!byId.TryGetValue(parentId, out parent))
                throw new DomainException("Новое место вложения не найдено в этом складе (между складами переносить нельзя).");
            if (parentId == id || IsDescendant(byId, parentId, id))
                throw new DomainException("Нельзя вложить место само в себя или во вложенное в него.");
            if (request.Type <= parent.Type)
                throw new DomainException(
                    $"Нельзя вложить «{LocationService.TypeName(request.Type)}» в «{LocationService.TypeName(parent.Type)}»: вложенность только вниз (зона → стеллаж → полка → ячейка).");
            if (parent.Id != self.ParentId && await repo.LocationHasStockAsync(parent.Id, ct))
                throw new DomainException($"В месте {parent.FullCode} лежит товар — вложить в него другое место нельзя.");
        }

        if (children.FirstOrDefault(c => c.Type <= request.Type) is { } wrongChild)
            throw new DomainException(
                $"Внутри лежит «{LocationService.TypeName(wrongChild.Type)}» {wrongChild.Code} — тип «{LocationService.TypeName(request.Type)}» для этого места не подходит.");

        self.Name = description;
        self.Type = request.Type;

        var prefix = parent?.FullCode ?? warehouse.Code;
        var newFullCode = $"{prefix}{CodeRules.Separator}{code}";
        IReadOnlyList<int> changed = [];

        if (newFullCode != self.FullCode || request.ParentId != self.ParentId)
        {
            // Новые коды всего поддерева не должны совпасть с кодами других мест.
            var subtree = Subtree(all, self).ToList();
            var oldPrefix = self.FullCode;
            var planned = subtree.ToDictionary(l => l.Id, l => newFullCode + l.FullCode[oldPrefix.Length..]);
            var taken = await repo.FindFullCodesAsync(planned.Values.ToList(), planned.Keys.ToList(), ct);
            if (taken.Count > 0)
                throw new DomainException($"Место «{taken[0]}» уже существует — выберите другой код.");

            self.ParentId = request.ParentId;
            self.Parent = parent;
            self.Code = code;
            foreach (var l in subtree)
                l.FullCode = planned[l.Id];
            changed = subtree.Select(l => l.Id).ToList();
        }

        await repo.SaveChangesAsync(ct);
        return new ReferenceChangeResult(
            changed.Count == 0
                ? $"Место {self.FullCode} сохранено."
                : $"Место сохранено: {self.FullCode}. Изменились коды мест: {changed.Count} — перепечатайте их QR-наклейки (старые наклейки больше не откроются).",
            changed);
    }

    public async Task<ReferenceChangeResult> DeleteLocationAsync(int id, CancellationToken ct = default)
    {
        var location = await repo.GetLocationAsync(id, ct) ?? throw new NotFoundException($"Место хранения {id} не найдено.");
        var all = await repo.GetWarehouseLocationsAsync(location.WarehouseId, ct);
        if (all.Any(l => l.ParentId == id))
            throw new DomainException($"В месте {location.FullCode} есть вложенные места — сначала удалите или перенесите их.");
        if (await repo.LocationHasStockAsync(id, ct))
            throw new DomainException($"В месте {location.FullCode} лежит товар — сначала переместите его.");
        if (await repo.LocationHasMovementsAsync(id, ct))
            throw new DomainException(
                $"Через место {location.FullCode} уже проводились документы — удалить его нельзя (пропала бы история). Его можно переименовать или перенести.");

        repo.Remove(all.First(l => l.Id == id));
        await repo.SaveChangesAsync(ct);
        return new ReferenceChangeResult($"Место {location.FullCode} удалено.");
    }

    /// <summary>Пересчитывает полные коды мест начиная с указанных корней; возвращает изменившиеся.</summary>
    private static List<int> Recode(List<StorageLocation> all, IEnumerable<StorageLocation> roots, string prefix)
    {
        var changed = new List<int>();
        var byParent = all.ToLookup(l => l.ParentId);

        void Walk(StorageLocation node, string parentCode)
        {
            var full = $"{parentCode}{CodeRules.Separator}{node.Code}";
            if (node.FullCode != full)
            {
                node.FullCode = full;
                changed.Add(node.Id);
            }
            foreach (var child in byParent[node.Id])
                Walk(child, full);
        }

        foreach (var root in roots.ToList())
            Walk(root, prefix);
        return changed;
    }

    private static IEnumerable<StorageLocation> Subtree(List<StorageLocation> all, StorageLocation root)
    {
        var byParent = all.ToLookup(l => l.ParentId);
        var stack = new Stack<StorageLocation>([root]);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            foreach (var child in byParent[node.Id])
                stack.Push(child);
        }
    }

    /// <summary>Лежит ли место <paramref name="candidateId"/> внутри места <paramref name="ancestorId"/>.</summary>
    private static bool IsDescendant(Dictionary<int, StorageLocation> byId, int candidateId, int ancestorId)
    {
        var current = byId[candidateId].ParentId;
        var guard = 0;
        while (current is int c && guard++ < 100)
        {
            if (c == ancestorId) return true;
            current = byId.TryGetValue(c, out var next) ? next.ParentId : null;
        }
        return false;
    }

    // ---------- Категории ----------

    public async Task<ReferenceChangeResult> UpdateCategoryAsync(int id, UpdateCategoryRequest request, CancellationToken ct = default)
    {
        var category = await repo.GetCategoryAsync(id, ct) ?? throw new NotFoundException($"Категория {id} не найдена.");
        var name = Clean(request.Name) ?? throw new DomainException("Название категории обязательно.");
        if (!string.Equals(name, category.Name, StringComparison.OrdinalIgnoreCase) && await repo.CategoryNameExistsAsync(name, id, ct))
            throw new DomainException($"Категория «{name}» уже существует.");

        category.Name = name;
        category.Description = Clean(request.Description);
        category.SkuPrefix = CodeRules.NormalizeSkuPrefix(request.SkuPrefix);
        await repo.SaveChangesAsync(ct);
        return new ReferenceChangeResult($"Категория «{name}» сохранена.");
    }

    public async Task<ReferenceChangeResult> DeleteCategoryAsync(int id, CancellationToken ct = default)
    {
        var category = await repo.GetCategoryAsync(id, ct) ?? throw new NotFoundException($"Категория {id} не найдена.");

        // Категория — только группировка: позиции остаются, просто без категории.
        var cleared = await repo.ClearCategoryAsync(id, ct);
        repo.Remove(category);
        await repo.SaveChangesAsync(ct);
        return new ReferenceChangeResult(cleared == 0
            ? $"Категория «{category.Name}» удалена."
            : $"Категория «{category.Name}» удалена; у позиций ({cleared}) категория теперь не указана.");
    }

    // ---------- Единицы измерения ----------

    public async Task<ReferenceChangeResult> UpdateUnitAsync(int id, UpdateUnitRequest request, CancellationToken ct = default)
    {
        var unit = await repo.GetUnitAsync(id, ct) ?? throw new NotFoundException($"Единица измерения {id} не найдена.");
        var code = Clean(request.Code) ?? throw new DomainException("Обозначение единицы обязательно.");
        var name = Clean(request.Name) ?? throw new DomainException("Название единицы обязательно.");
        if (code.Length > 16) throw new DomainException("Обозначение — не длиннее 16 символов.");

        if (!string.Equals(code, unit.Code, StringComparison.OrdinalIgnoreCase) && await repo.UnitCodeExistsAsync(code, id, ct))
            throw new DomainException($"Единица «{code}» уже существует.");

        // Сделать единицу «целой» можно, только если дробных количеств по ней нет.
        if (unit.IsFractional && !request.IsFractional && await repo.UnitHasFractionalQuantitiesAsync(id, ct))
            throw new DomainException($"По позициям в «{unit.Code}» уже есть дробные количества — сделать единицу целой нельзя.");

        unit.Code = code;
        unit.Name = name;
        unit.IsFractional = request.IsFractional;
        await repo.SaveChangesAsync(ct);
        return new ReferenceChangeResult($"Единица «{code}» сохранена.");
    }

    public async Task<ReferenceChangeResult> DeleteUnitAsync(int id, CancellationToken ct = default)
    {
        var unit = await repo.GetUnitAsync(id, ct) ?? throw new NotFoundException($"Единица измерения {id} не найдена.");
        var count = await repo.CountItemsWithUnitAsync(id, ct);
        if (count > 0)
            throw new DomainException($"Единица «{unit.Code}» указана у позиций ({count}) — удалить нельзя. Её можно переименовать.");

        repo.Remove(unit);
        await repo.SaveChangesAsync(ct);
        return new ReferenceChangeResult($"Единица «{unit.Code}» удалена.");
    }

    // ---------- Оборудование ----------

    public async Task<ReferenceChangeResult> UpdateEquipmentAsync(int id, UpdateEquipmentRequest request, CancellationToken ct = default)
    {
        var equipment = await repo.GetEquipmentAsync(id, ct) ?? throw new NotFoundException($"Оборудование {id} не найдено.");
        var code = Clean(request.Code) ?? throw new DomainException("Код оборудования обязателен.");
        var name = Clean(request.Name) ?? throw new DomainException("Наименование оборудования обязательно.");

        if (!string.Equals(code, equipment.Code, StringComparison.OrdinalIgnoreCase) && await repo.EquipmentCodeExistsAsync(code, id, ct))
            throw new DomainException($"Оборудование с кодом «{code}» уже существует.");

        if (request.ParentId is int parentId)
        {
            var hierarchy = (await repo.ListEquipmentHierarchyAsync(ct)).ToDictionary(h => h.Id, h => h.ParentId);
            if (!hierarchy.ContainsKey(parentId))
                throw new NotFoundException($"Оборудование {parentId} не найдено.");

            // Подняться от нового родителя вверх: если встретим само оборудование — получится цикл.
            int? current = parentId;
            var guard = 0;
            while (current is int c && guard++ < 1000)
            {
                if (c == id)
                    throw new DomainException("Нельзя вложить оборудование само в себя или в свой узел.");
                current = hierarchy.GetValueOrDefault(c);
            }
        }

        equipment.Code = code;
        equipment.Name = name;
        equipment.ParentId = request.ParentId;
        await repo.SaveChangesAsync(ct);
        return new ReferenceChangeResult($"Оборудование «{code}» сохранено.");
    }

    public async Task<ReferenceChangeResult> DeleteEquipmentAsync(int id, CancellationToken ct = default)
    {
        var equipment = await repo.GetEquipmentAsync(id, ct) ?? throw new NotFoundException($"Оборудование {id} не найдено.");
        if ((await repo.ListEquipmentHierarchyAsync(ct)).Any(h => h.ParentId == id))
            throw new DomainException($"В «{equipment.Code}» входят узлы — сначала удалите или перенесите их.");
        if (await repo.EquipmentIsUsedAsync(id, ct))
            throw new DomainException($"«{equipment.Code}» указано в документах или актах — удалить нельзя (пропала бы история). Его можно переименовать.");

        repo.Remove(equipment);
        await repo.SaveChangesAsync(ct);
        return new ReferenceChangeResult($"Оборудование «{equipment.Code}» удалено.");
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
