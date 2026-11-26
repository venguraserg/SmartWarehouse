using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Application.Services;

public interface IInventoryService
{
    Task<IReadOnlyList<InventorySummaryDto>> ListAsync(CancellationToken ct = default);

    Task<InventoryDto> StartAsync(StartInventoryRequest request, InventoryActor actor, CancellationToken ct = default);

    Task<InventoryDto> GetAsync(int id, CancellationToken ct = default);

    Task<InventoryPlaceDto> GetPlaceAsync(int id, int locationId, CancellationToken ct = default);

    Task<InventoryPlaceDto> CountAsync(int id, int locationId, InventoryCountRequest request, InventoryActor actor, CancellationToken ct = default);

    Task<InventoryPlaceDto> CheckAsync(int id, int locationId, bool isChecked, InventoryActor actor, CancellationToken ct = default);

    Task<InventorySheetDto> GetSheetAsync(int id, CancellationToken ct = default);

    Task<InventoryPostResultDto> PostAsync(int id, InventoryActor actor, CancellationToken ct = default);

    Task<InventorySummaryDto> CancelAsync(int id, InventoryActor actor, CancellationToken ct = default);

    /// <summary>Идущие инвентаризации, в которые входит это место (для страницы места по QR).</summary>
    Task<IReadOnlyList<InventorySummaryDto>> FindOpenAsync(int locationId, CancellationToken ct = default);
}

/// <summary>
/// Инвентаризация по местам. Кладовщик сканирует ячейку, видит, что там числится, вводит факт
/// и отмечает место проверенным. При проведении: пересорт между местами — перемещение,
/// недостача — корректировка «−» (партии старые первыми), излишек — корректировка «+» вне учёта.
/// В документ идёт разница «факт − числилось на момент подсчёта», поэтому выдачи после подсчёта её не искажают.
/// </summary>
public class InventoryService(
    IInventoryRepository repo,
    ILocationRepository locations,
    IStockRepository stockRepo,
    StockService stock,
    TimeProvider clock) : IInventoryService
{
    public const int ListTake = 100;

    // ---------- Список, начало, отмена ----------

    public async Task<IReadOnlyList<InventorySummaryDto>> ListAsync(CancellationToken ct = default)
    {
        var result = new List<InventorySummaryDto>();
        foreach (var inv in await repo.ListAsync(ListTake, ct))
        {
            // Число мест считаем только для идущих: для завершённых — сколько проверено.
            if (inv.Status == InventoryStatus.Open)
                result.Add(Summary(inv, await LoadAsync(inv, ct)));
            else
                result.Add(Summary(inv, null));
        }
        return result;
    }

    public async Task<IReadOnlyList<InventorySummaryDto>> FindOpenAsync(int locationId, CancellationToken ct = default)
    {
        var location = await locations.GetLocationAsync(locationId, ct);
        if (location is null)
            return [];
        var tree = LocationTree.Build(await locations.ListLocationsAsync(location.WarehouseId, ct));
        var chain = tree.Chain(locationId).Select(n => n.Id).ToHashSet();
        var result = new List<InventorySummaryDto>();
        foreach (var open in await repo.ListOpenAsync(ct))
            if (chain.Contains(open.RootLocationId))
                result.Add(Summary(open, null) with { RootTitle = tree.Address(open.RootLocationId).GroupTitle });
        return result;
    }

    public async Task<InventoryDto> StartAsync(StartInventoryRequest request, InventoryActor actor, CancellationToken ct = default)
    {
        var root = await locations.GetLocationAsync(request.LocationId, ct)
                   ?? throw new NotFoundException($"Место хранения {request.LocationId} не найдено.");
        var tree = LocationTree.Build(await locations.ListLocationsAsync(root.WarehouseId, ct));

        // Одно и то же место не считают две инвентаризации сразу (ни целиком, ни частью).
        var chain = tree.Chain(root.Id).Select(n => n.Id).ToHashSet();
        foreach (var open in await repo.ListOpenAsync(ct))
        {
            if (open.RootLocation.WarehouseId != root.WarehouseId)
                continue;
            if (chain.Contains(open.RootLocationId) || tree.Chain(open.RootLocationId).Any(n => n.Id == root.Id))
                throw new DomainException(
                    $"Здесь уже идёт инвентаризация {open.Number} ({tree.Address(open.RootLocationId).GroupTitle}). Завершите или отмените её.");
        }

        var inv = new InventoryCount
        {
            RootLocationId = root.Id,
            Status = InventoryStatus.Open,
            StartedAt = clock.GetUtcNow(),
            StartedById = actor.EmployeeId,
            StartedBy = Name(actor),
            Comment = Clean(request.Comment, 500)
        };
        repo.Add(inv);
        await repo.SaveChangesAsync(ct);
        return await GetAsync(inv.Id, ct);
    }

    public async Task<InventorySummaryDto> CancelAsync(int id, InventoryActor actor, CancellationToken ct = default)
    {
        var inv = await OpenAsync(id, ct);
        inv.Status = InventoryStatus.Cancelled;
        inv.FinishedAt = clock.GetUtcNow();
        inv.FinishedBy = Name(actor);
        await repo.SaveChangesAsync(ct);
        return Summary(inv, null);
    }

    // ---------- Места и подсчёт ----------

    public async Task<InventoryDto> GetAsync(int id, CancellationToken ct = default)
    {
        var inv = await FindAsync(id, ct);
        var ctx = await LoadAsync(inv, ct);
        var places = ctx.Places.Select(p => PlaceSummary(inv, ctx, p)).ToList();
        return new InventoryDto(Summary(inv, ctx), places);
    }

    public async Task<InventoryPlaceDto> GetPlaceAsync(int id, int locationId, CancellationToken ct = default)
    {
        var inv = await FindAsync(id, ct);
        var ctx = await LoadAsync(inv, ct);
        return await PlaceAsync(inv, ctx, Place(ctx, locationId), ct);
    }

    public async Task<InventoryPlaceDto> CountAsync(
        int id, int locationId, InventoryCountRequest request, InventoryActor actor, CancellationToken ct = default)
    {
        var inv = await OpenAsync(id, ct);
        var ctx = await LoadAsync(inv, ct);
        var place = Place(ctx, locationId);
        var line = inv.Lines.FirstOrDefault(l => l.LocationId == place.Id && l.ItemId == request.ItemId);

        if (request.Counted is null)
        {
            // Отменить подсчёт позиции.
            if (line is not null)
            {
                inv.Lines.Remove(line);
                repo.Remove(line);
            }
        }
        else
        {
            var counted = request.Counted.Value;
            if (counted < 0)
                throw new DomainException("Количество не может быть отрицательным.");
            // Отслеживаемая позиция: строка ссылается на ту же сущность, что уже загружена со строками
            // (позиция «только для чтения» из другого запроса дала бы конфликт или лишнюю вставку).
            var item = await repo.GetItemAsync(request.ItemId, ct)
                       ?? throw new NotFoundException($"Позиция {request.ItemId} не найдена.");
            if (!item.Unit.IsFractional && counted != decimal.Truncate(counted))
                throw new DomainException($"«{item.Name}» учитывается в «{item.Unit.Code}» — количество должно быть целым.");
            if (line is null && !item.IsActive && ctx.Book(place.Id, item.Id) == 0)
                throw new DomainException($"Позиция «{item.Name}» неактивна — добавить её как найденную нельзя.");

            var now = clock.GetUtcNow();
            if (line is null)
            {
                // Числилось — на момент подсчёта: дальнейшие выдачи не исказят разницу.
                line = new InventoryLine
                {
                    LocationId = place.Id,
                    ItemId = item.Id,
                    Item = item,
                    Expected = ctx.Book(place.Id, item.Id)
                };
                inv.Lines.Add(line);
            }
            line.Counted = counted;
            line.CountedAt = now;
            line.CountedBy = Name(actor);
        }

        await repo.SaveChangesAsync(ct);
        return await PlaceAsync(inv, ctx, place, ct);
    }

    public async Task<InventoryPlaceDto> CheckAsync(int id, int locationId, bool isChecked, InventoryActor actor, CancellationToken ct = default)
    {
        var inv = await OpenAsync(id, ct);
        var ctx = await LoadAsync(inv, ct);
        var place = Place(ctx, locationId);
        var mark = inv.Places.FirstOrDefault(p => p.LocationId == place.Id);

        if (isChecked)
        {
            var notCounted = ctx.BookItems(place.Id)
                .Where(itemId => !inv.Lines.Any(l => l.LocationId == place.Id && l.ItemId == itemId))
                .Select(itemId => ctx.ItemName(itemId))
                .ToList();
            if (notCounted.Count > 0)
                throw new DomainException($"Не посчитано: {string.Join(", ", notCounted.Take(5))}{(notCounted.Count > 5 ? $" и ещё {notCounted.Count - 5}" : "")}. Отметьте «Сходится», введите факт или «Нет».");
            if (mark is null)
                inv.Places.Add(new InventoryPlace { LocationId = place.Id, CheckedAt = clock.GetUtcNow(), CheckedBy = Name(actor) });
        }
        else if (mark is not null)
        {
            inv.Places.Remove(mark);
            repo.Remove(mark);
        }

        await repo.SaveChangesAsync(ct);
        return await PlaceAsync(inv, ctx, place, ct);
    }

    // ---------- Ведомость и проведение ----------

    public async Task<InventorySheetDto> GetSheetAsync(int id, CancellationToken ct = default)
    {
        var inv = await FindAsync(id, ct);
        var ctx = await LoadAsync(inv, ct);
        var plan = Plan(inv, ctx);

        var rows = new List<InventorySheetRowDto>();
        foreach (var line in inv.Lines.Where(l => l.Counted != l.Expected).OrderBy(l => ctx.Order(l.LocationId)).ThenBy(l => l.Item.Name))
        {
            var key = (line.LocationId, line.ItemId);
            var isChecked = plan.Checked.Contains(line.LocationId);
            var moved = plan.Moved.GetValueOrDefault(key);
            var rest = plan.Rest.GetValueOrDefault(key);
            var action = !isChecked || rest == 0 ? InventoryAction.None : rest < 0 ? InventoryAction.Shortage : InventoryAction.Surplus;
            rows.Add(new InventorySheetRowDto(
                line.LocationId, ctx.Title(line.LocationId), isChecked,
                line.ItemId, line.Item.Sku, line.Item.Name, line.Item.Unit.Code,
                line.Expected, line.Counted, line.Counted - line.Expected,
                moved, plan.MoveText.GetValueOrDefault(key), action,
                action == InventoryAction.Shortage ? ctx.ShortageValue(line.LocationId, line.ItemId, -rest) : null));
        }

        var notChecked = ctx.Places.Where(p => !plan.Checked.Contains(p.Id)).Select(p => ctx.Title(p.Id)).ToList();
        return new InventorySheetDto(
            Summary(inv, ctx),
            rows,
            inv.Lines.Count(l => plan.Checked.Contains(l.LocationId)),
            rows.Count(r => r.Action == InventoryAction.Shortage),
            rows.Where(r => r.Value is not null).Sum(r => r.Value!.Value),
            rows.Count(r => r.Action == InventoryAction.Surplus),
            rows.Count(r => r.Moved > 0 && r.Delta < 0),
            notChecked);
    }

    public async Task<InventoryPostResultDto> PostAsync(int id, InventoryActor actor, CancellationToken ct = default)
    {
        var inv = await OpenAsync(id, ct);
        var ctx = await LoadAsync(inv, ct);
        var plan = Plan(inv, ctx);
        if (plan.Checked.Count == 0)
            throw new DomainException("Нет проверенных мест — проводить нечего. Отметьте места «проверено» или отмените инвентаризацию.");

        var comment = $"Инвентаризация {inv.Number}";
        var documents = new List<DocumentRefDto>();

        await repo.InTransactionAsync(async () =>
        {
            // Пересорт — перемещением; затем недостача и излишек. Каждый документ сохраняется сразу:
            // следующий видит уже изменённые остатки. Ошибка любого — откат всего.
            async Task PostDocsAsync(MovementType type, bool offBook, IEnumerable<DocumentLineRequest> lines)
            {
                foreach (var chunk in lines.Chunk(StockService.MaxLines))
                {
                    var request = new CreateDocumentRequest(type, null, null, Name(actor), comment, chunk,
                        PerformedById: actor.EmployeeId, OffBook: offBook);
                    var staged = await stock.StageAsync(request, exceptActId: null, ct);
                    await repo.SaveChangesAsync(ct);
                    documents.Add(new DocumentRefDto(staged.Document.Id, staged.Document.Number));
                }
            }

            await PostDocsAsync(MovementType.Transfer, false,
                plan.Moves.Select(m => new DocumentLineRequest(m.ItemId, m.Quantity, m.From, m.To)));
            await PostDocsAsync(MovementType.Adjustment, false,
                plan.Rest.Where(r => r.Value < 0).Select(r => new DocumentLineRequest(r.Key.ItemId, -r.Value, r.Key.LocationId, null)));
            await PostDocsAsync(MovementType.Adjustment, true,
                plan.Rest.Where(r => r.Value > 0).Select(r => new DocumentLineRequest(r.Key.ItemId, r.Value, null, r.Key.LocationId)));

            var now = clock.GetUtcNow();
            await repo.SetCheckedAsync(plan.Checked.ToList(), now, ct);
            inv.Status = InventoryStatus.Posted;
            inv.FinishedAt = now;
            inv.FinishedBy = Name(actor);
            await repo.SaveChangesAsync(ct);
        }, ct);

        return new InventoryPostResultDto(Summary(inv, ctx), documents);
    }

    // ---------- Расчёт: пересорт, недостача, излишек ----------

    private sealed record Move(int ItemId, int From, int To, decimal Quantity);

    private sealed record PostingPlan(
        HashSet<int> Checked,
        List<Move> Moves,
        Dictionary<(int LocationId, int ItemId), decimal> Moved,
        Dictionary<(int LocationId, int ItemId), string> MoveText,
        Dictionary<(int LocationId, int ItemId), decimal> Rest);

    /// <summary>
    /// Только проверенные места. По каждой позиции недостачи в одних местах покрываются излишками в других —
    /// это перемещения; что осталось — недостача (списать) или излишек (оприходовать вне учёта).
    /// </summary>
    private static PostingPlan Plan(InventoryCount inv, Context ctx)
    {
        var isChecked = inv.Places.Select(p => p.LocationId).Where(ctx.Contains).ToHashSet();
        var moves = new List<Move>();
        var moved = new Dictionary<(int, int), decimal>();
        var texts = new Dictionary<(int, int), List<string>>();
        var rest = new Dictionary<(int LocationId, int ItemId), decimal>();

        foreach (var group in inv.Lines.Where(l => isChecked.Contains(l.LocationId) && l.Counted != l.Expected).GroupBy(l => l.ItemId))
        {
            var shortages = group.Where(l => l.Counted < l.Expected).OrderBy(l => ctx.Order(l.LocationId))
                .Select(l => (l.LocationId, Left: l.Expected - l.Counted)).ToList();
            var surpluses = group.Where(l => l.Counted > l.Expected).OrderBy(l => ctx.Order(l.LocationId))
                .Select(l => (l.LocationId, Left: l.Counted - l.Expected)).ToList();

            int i = 0, j = 0;
            while (i < shortages.Count && j < surpluses.Count)
            {
                var qty = Math.Min(shortages[i].Left, surpluses[j].Left);
                var from = shortages[i].LocationId;
                var to = surpluses[j].LocationId;
                moves.Add(new Move(group.Key, from, to, qty));
                moved[(from, group.Key)] = moved.GetValueOrDefault((from, group.Key)) + qty;
                moved[(to, group.Key)] = moved.GetValueOrDefault((to, group.Key)) + qty;
                Text(from, group.Key).Add($"{Qty(qty)} → {ctx.Title(to)}");
                Text(to, group.Key).Add($"{Qty(qty)} ← {ctx.Title(from)}");
                shortages[i] = shortages[i] with { Left = shortages[i].Left - qty };
                surpluses[j] = surpluses[j] with { Left = surpluses[j].Left - qty };
                if (shortages[i].Left == 0) i++;
                if (surpluses[j].Left == 0) j++;
            }
            foreach (var s in shortages.Where(s => s.Left > 0))
                rest[(s.LocationId, group.Key)] = -s.Left;
            foreach (var s in surpluses.Where(s => s.Left > 0))
                rest[(s.LocationId, group.Key)] = s.Left;
        }

        return new PostingPlan(isChecked, moves, moved,
            texts.ToDictionary(t => t.Key, t => string.Join("; ", t.Value)), rest);

        List<string> Text(int location, int item)
        {
            if (!texts.TryGetValue((location, item), out var list))
                texts[(location, item)] = list = [];
            return list;
        }
    }

    private static string Qty(decimal q) => q.ToString("0.###", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));

    // ---------- Загрузка и представление ----------

    /// <summary>Дерево склада, места инвентаризации в порядке обхода и что в них числится сейчас.</summary>
    private sealed class Context
    {
        public required LocationTree Tree { get; init; }
        public required List<LocationNodeDto> Places { get; init; }
        public required Dictionary<int, int> OrderById { get; init; }
        public required List<StockBalance> Stock { get; init; }
        public required Dictionary<int, string> Names { get; init; }

        public bool Contains(int locationId) => OrderById.ContainsKey(locationId);

        public int Order(int locationId) => OrderById.GetValueOrDefault(locationId, int.MaxValue);

        public decimal Book(int locationId, int itemId) =>
            Stock.Where(b => b.LocationId == locationId && b.ItemId == itemId).Sum(b => b.Quantity);

        public IEnumerable<int> BookItems(int locationId) =>
            Stock.Where(b => b.LocationId == locationId).Select(b => b.ItemId).Distinct();

        public string ItemName(int itemId) => Names.GetValueOrDefault(itemId, itemId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public string Title(int locationId) =>
            Tree.ById.ContainsKey(locationId) ? Tree.Address(locationId).Sticker : locationId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Сумма недостачи: партии в месте, старые первыми (как будет списано).</summary>
        public decimal? ShortageValue(int locationId, int itemId, decimal quantity)
        {
            var parts = new List<(decimal Quantity, decimal? UnitPrice)>();
            var left = quantity;
            foreach (var b in Stock.Where(b => b.LocationId == locationId && b.ItemId == itemId).OrderBy(b => b.Batch?.ReceivedAt))
            {
                if (left <= 0)
                    break;
                var take = Math.Min(left, b.Quantity);
                parts.Add((take, Valuation.Price(b.Batch)));
                left -= take;
            }
            return parts.Count == 0 ? null : Valuation.Of(parts).Value;
        }
    }

    private async Task<Context> LoadAsync(InventoryCount inv, CancellationToken ct)
    {
        var root = inv.RootLocation ?? await locations.GetLocationAsync(inv.RootLocationId, ct)
                   ?? throw new NotFoundException($"Место {inv.RootLocationId} не найдено.");
        var tree = LocationTree.Build(await locations.ListLocationsAsync(root.WarehouseId, ct));
        var subtree = tree.Walk(root.Id).ToList();
        var stockRows = (await repo.ListStockAsync(subtree.Select(n => n.Id).ToList(), ct)).ToList();
        var withStock = stockRows.Select(b => b.LocationId).ToHashSet();
        var withLines = inv.Lines.Select(l => l.LocationId).ToHashSet();

        // Считаем там, где что-то может лежать: конечные места и места, где что-то числится или найдено.
        var places = subtree.Where(n => n.Children.Count == 0 || withStock.Contains(n.Id) || withLines.Contains(n.Id)).ToList();
        var names = stockRows.Select(b => b.Item).Concat(inv.Lines.Where(l => l.Item is not null).Select(l => l.Item))
            .GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First().Name);

        return new Context
        {
            Tree = tree,
            Places = places,
            OrderById = places.Select((p, i) => (p.Id, i)).ToDictionary(x => x.Id, x => x.i),
            Stock = stockRows,
            Names = names
        };
    }

    private static LocationNodeDto Place(Context ctx, int locationId) =>
        ctx.Places.FirstOrDefault(p => p.Id == locationId)
        ?? throw new DomainException("Это место не входит в инвентаризацию. Отсканируйте место этого стеллажа.");

    private async Task<InventoryPlaceDto> PlaceAsync(InventoryCount inv, Context ctx, LocationNodeDto place, CancellationToken ct)
    {
        var lines = inv.Lines.Where(l => l.LocationId == place.Id).ToDictionary(l => l.ItemId);
        var bookByItem = ctx.Stock.Where(b => b.LocationId == place.Id)
            .GroupBy(b => b.ItemId).ToDictionary(g => g.Key, g => (Item: g.First().Item, Qty: g.Sum(b => b.Quantity)));

        // «Лишнее» (по учёту здесь нет) — подсказка, где позиция числится.
        var extraIds = lines.Keys.Where(id => !bookByItem.ContainsKey(id)).ToList();
        var elsewhere = new Dictionary<int, string>();
        if (extraIds.Count > 0)
        {
            foreach (var g in (await repo.ListItemPlacesAsync(extraIds, ct)).GroupBy(x => x.ItemId))
                elsewhere[g.Key] = string.Join(", ", g.Where(x => ctx.Tree.ById.ContainsKey(x.LocationId))
                    .Select(x => $"{ctx.Title(x.LocationId)} — {Qty(x.Quantity)}"));
        }

        var items = bookByItem.Keys.Union(lines.Keys)
            .Select(itemId =>
            {
                var item = bookByItem.TryGetValue(itemId, out var b) ? b.Item : lines[itemId].Item;
                lines.TryGetValue(itemId, out var line);
                return new InventoryItemDto(item.Id, item.Sku, item.Name, item.Unit.Code, item.Unit.IsFractional,
                    bookByItem.TryGetValue(itemId, out var bq) ? bq.Qty : 0,
                    line?.Expected, line?.Counted,
                    elsewhere.TryGetValue(itemId, out var where) && where.Length > 0 ? where : null);
            })
            .OrderBy(i => i.Book == 0 ? 1 : 0).ThenBy(i => i.Name)
            .ToList();

        // Следующее непроверенное место после этого (по кругу).
        var checkedIds = inv.Places.Select(p => p.LocationId).ToHashSet();
        var index = ctx.Places.FindIndex(p => p.Id == place.Id);
        var next = ctx.Places.Skip(index + 1).Concat(ctx.Places.Take(index))
            .FirstOrDefault(p => !checkedIds.Contains(p.Id));

        var address = ctx.Tree.Address(place.Id);
        return new InventoryPlaceDto(inv.Id, inv.Number, inv.Status == InventoryStatus.Open,
            place.Id, place.FullCode, address.Sticker, address.Full,
            address.Area is not null && address.Area != address.Full ? address.Area : null,
            checkedIds.Contains(place.Id), items, next?.Id);
    }

    private static InventoryPlaceSummaryDto PlaceSummary(InventoryCount inv, Context ctx, LocationNodeDto place)
    {
        var lines = inv.Lines.Where(l => l.LocationId == place.Id).ToList();
        var shelf = ctx.Tree.Chain(place.Id).LastOrDefault(n => n.Type == LocationType.Shelf);
        var shelfTitle = shelf is null ? null : PlaceAddress.IsFloor(shelf) ? "Под стеллажом" : $"Полка {shelf.Code}";
        return new InventoryPlaceSummaryDto(place.Id, place.FullCode, ctx.Title(place.Id), shelfTitle,
            inv.Places.Any(p => p.LocationId == place.Id),
            ctx.BookItems(place.Id).Count(),
            lines.Count,
            lines.Count(l => l.Counted != l.Expected));
    }

    private static InventorySummaryDto Summary(InventoryCount inv, Context? ctx)
    {
        var root = ctx?.Tree.ById.GetValueOrDefault(inv.RootLocationId);
        string title;
        if (root is null)
            title = inv.RootLocation?.FullCode ?? inv.RootLocationId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        else
        {
            var a = ctx!.Tree.Address(root.Id);
            title = root.Type == LocationType.Rack ? a.GroupTitle : a.Full;
        }
        var places = ctx?.Places.Count ?? inv.Places.Count;
        return new InventorySummaryDto(inv.Id, inv.Number, inv.RootLocationId, inv.RootLocation?.FullCode ?? root?.FullCode ?? "",
            title, inv.Status, inv.StartedAt, inv.StartedBy, inv.FinishedAt, inv.FinishedBy, inv.Comment,
            places, inv.Places.Count, inv.Lines.Count(l => l.Counted != l.Expected));
    }

    private async Task<InventoryCount> FindAsync(int id, CancellationToken ct) =>
        await repo.GetAsync(id, ct) ?? throw new NotFoundException($"Инвентаризация {id} не найдена.");

    private async Task<InventoryCount> OpenAsync(int id, CancellationToken ct)
    {
        var inv = await FindAsync(id, ct);
        if (inv.Status != InventoryStatus.Open)
            throw new DomainException($"Инвентаризация {inv.Number} уже {(inv.Status == InventoryStatus.Posted ? "проведена" : "отменена")}.");
        return inv;
    }

    private static string Name(InventoryActor actor) =>
        string.IsNullOrWhiteSpace(actor.Name) ? "—" : actor.Name.Trim()[..Math.Min(actor.Name.Trim().Length, 128)];

    private static string? Clean(string? value, int max)
    {
        var s = value?.Trim();
        return string.IsNullOrEmpty(s) ? null : s.Length > max ? s[..max] : s;
    }
}
