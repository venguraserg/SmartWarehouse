using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Application.Services;

/// <summary>
/// Ядро учёта. Остаток меняется только проведением документа, напрямую его изменить нельзя.
/// Документ проводится целиком или не проводится вовсе; уход в минус запрещён.
/// </summary>
public class StockService(IStockRepository repo, TimeProvider clock) : IStockService
{
    public const int MaxLines = 500;

    public async Task<DocumentDto> PostDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default)
    {
        // Списание — только по утверждённому акту на списание (см. WriteOffActService).
        if (request.Type == MovementType.WriteOff)
            throw new DomainException("Списание оформляется актом на списание: сотрудник составляет акт, руководитель утверждает — тогда позиции списываются.");
        if (request.Type == MovementType.CustodyChange)
            throw new DomainException("Подотчёт по 1С больше не ведётся: МОЛ — пометка партии, меняется по сверке с 1С.");
        if (request.Type == MovementType.MolChange)
            throw new DomainException("МОЛ меняется только по сверке с сальдовой ведомостью 1С.");

        var staged = await StageAsync(request, exceptActId: null, ct);
        await repo.SaveChangesAsync(ct);
        return staged.ToDto();
    }

    public async Task<DocumentRefDto> ChangeMolAsync(ChangeMolRequest request, int? performerId, string? performedBy, CancellationToken ct = default)
    {
        var document = await StageMolChangeAsync(request, performerId, performedBy, ct);
        await repo.SaveChangesAsync(ct);
        return new DocumentRefDto(document.Id, document.Number);
    }

    /// <summary>
    /// Смена МОЛ партий в местах хранения. Если меняется вся партия (она целиком в этом месте) — меняется пометка партии;
    /// если часть — эта часть отделяется в новую партию с той же ценой, датой и примечанием. Количество и место не меняются.
    /// </summary>
    internal async Task<StockDocument> StageMolChangeAsync(ChangeMolRequest request, int? performerId, string? performedBy, CancellationToken ct)
    {
        var lines = (request.Lines ?? []).Where(l => l.Quantity != 0).ToList();
        if (lines.Count == 0)
            throw new DomainException("Отметьте, у чего сменить МОЛ.");
        if (lines.Count > MaxLines)
            throw new DomainException($"За один раз — не больше {MaxLines} строк.");
        if (lines.Any(l => l.Quantity < 0))
            throw new DomainException("Количество должно быть больше нуля.");

        var performer = await GetEmployeeAsync(performerId, "Кто проводит", requireActive: false, ct);
        var storekeepers = await repo.ListStorekeeperIdsAsync(ct);
        foreach (var molId in lines.Select(l => l.MolId).Distinct())
            await EnsureMolAsync(molId, storekeepers, ct);

        var now = clock.GetUtcNow();
        var locations = await repo.GetLocationsAsync(lines.Select(l => l.LocationId).Distinct(), ct);
        var document = new StockDocument
        {
            Type = MovementType.MolChange,
            OccurredAt = now,
            PerformedById = performer?.Id,
            PerformedBy = performer?.ShortName ?? Clean(performedBy),
            Comment = Clean(request.Comment) ?? "Смена МОЛ по сверке с 1С",
            ExternalNumber = Clean(request.ExternalNumber)
        };
        var lineNo = 0;

        foreach (var group in lines.GroupBy(l => l.BatchId))
        {
            var batch = await repo.GetBatchAsync(group.Key, ct) ?? throw new NotFoundException($"Партия {group.Key} не найдена.");
            if (batch.OffBook)
                throw new DomainException($"Партия {batch.Id} — вне учёта: в 1С её нет, МОЛ у неё не ведётся.");
            var total = await repo.GetBatchTotalAsync(batch.Id, ct);
            var moving = group.Where(l => l.MolId != batch.MolId).ToList();
            if (moving.Count == 0)
                continue;

            // Вся партия целиком и к одному МОЛ — меняется пометка, без деления.
            var whole = moving.Sum(l => l.Quantity) == total && moving.Select(l => l.MolId).Distinct().Count() == 1
                        && moving.Count == group.Count();
            foreach (var line in moving)
            {
                if (!locations.TryGetValue(line.LocationId, out var place))
                    throw new NotFoundException($"Место хранения {line.LocationId} не найдено.");
                var balance = (await repo.GetLocationBatchesAsync(batch.ItemId, line.LocationId, ct)).FirstOrDefault(b => b.BatchId == batch.Id);
                if (balance is null || balance.Quantity < line.Quantity)
                    throw new DomainException($"В {place.FullCode} партии {batch.Id} только {balance?.Quantity ?? 0:0.###} — сменить МОЛ у {line.Quantity:0.###} нельзя.");

                if (whole)
                {
                    document.Lines.Add(Movement(batch, line.LocationId, line.LocationId, line.Quantity));
                    continue;
                }

                var part = new Batch
                {
                    ItemId = batch.ItemId, UnitPrice = batch.UnitPrice, ReceivedAt = batch.ReceivedAt,
                    ReceiptDocumentId = batch.ReceiptDocumentId, Note = batch.Note, OffBook = false,
                    Origin = batch.Origin, MolId = line.MolId
                };
                repo.AddBatch(part);
                balance.Quantity -= line.Quantity;
                repo.AddBalance(new StockBalance { ItemId = batch.ItemId, LocationId = line.LocationId, Batch = part, Quantity = line.Quantity });
                document.Lines.Add(Movement(batch, line.LocationId, null, line.Quantity));
                document.Lines.Add(Movement(part, null, line.LocationId, line.Quantity));
            }
            if (whole)
                batch.MolId = moving[0].MolId;
        }

        if (document.Lines.Count == 0)
            throw new DomainException("МОЛ уже такой, как указано, — менять нечего.");
        repo.AddDocument(document);
        return document;

        StockMovement Movement(Batch batch, int? from, int? to, decimal quantity) => new()
        {
            Document = document,
            LineNo = ++lineNo,
            Type = MovementType.MolChange,
            OccurredAt = now,
            ItemId = batch.ItemId,
            BatchId = batch.Id,
            Batch = batch,
            FromLocationId = from,
            ToLocationId = to,
            Quantity = quantity
        };
    }

    /// <summary>Подготовленный к сохранению документ: остатки изменены, документ добавлен, но не сохранён.</summary>
    internal sealed record StagedDocument(
        StockDocument Document,
        IReadOnlyDictionary<int, Item> Items,
        IReadOnlyDictionary<int, LocationInfo> Locations)
    {
        public DocumentDto ToDto() => StockService.ToDto(Document, line =>
        {
            var item = Items[line.ItemId];
            return new DocumentLineDto(
                line.LineNo, item.Id, item.Sku, item.Name, item.Unit.Code, line.Quantity,
                line.FromLocationId is int f ? Locations[f].FullCode : null,
                line.ToLocationId is int t ? Locations[t].FullCode : null,
                line.Batch.Id, line.Batch.UnitPrice, line.Batch.ReceivedAt,
                line.FromLocationId is int fn ? Locations[fn].Name : null,
                line.ToLocationId is int tn ? Locations[tn].Name : null,
                line.Batch.OffBook);
        });
    }

    /// <summary>
    /// Проверяет документ и готовит изменения остатков, не сохраняя их: сохранение — у вызывающего
    /// (так акт на списание и его документ сохраняются одной транзакцией).
    /// </summary>
    /// <param name="exceptActId">Утверждаемый акт: его резерв не мешает списанию.</param>
    internal async Task<StagedDocument> StageAsync(CreateDocumentRequest request, int? exceptActId, CancellationToken ct)
    {
        // ---------- 1. Шапка ----------
        if (!Enum.IsDefined(request.Type))
            throw new DomainException($"Неизвестный тип документа: {request.Type}.");

        var lines = request.Lines ?? Array.Empty<DocumentLineRequest>();
        if (lines.Count == 0)
            throw new DomainException("Документ должен содержать хотя бы одну строку.");
        if (lines.Count > MaxLines)
            throw new DomainException($"В документе не может быть больше {MaxLines} строк.");

        if (request.EquipmentId is int equipmentId)
        {
            if (request.Type is not (MovementType.Issue or MovementType.WriteOff))
                throw new DomainException("Оборудование указывается только для выдачи и списания.");
            if (!await repo.EquipmentExistsAsync(equipmentId, ct))
                throw new NotFoundException($"Оборудование {equipmentId} не найдено.");
        }

        // Сотрудники (в документ записываются и ссылкой, и текстом «Иванов И. П.»):
        // кто проводит и подотчётное лицо — кому выдано / с кого списано / кто вернул.
        var performer = await GetEmployeeAsync(request.PerformedById, "Кто проводит", requireActive: true, ct);
        var accountable = await GetEmployeeAsync(
            request.ReceivedById, AccountableLabel(request.Type),
            requireActive: request.Type == MovementType.Issue, // вернуть или списать можно и с уволенного
            ct);

        if (UsesAccountable(request.Type))
        {
            if (accountable is null)
                throw new DomainException($"Укажите сотрудника: «{AccountableLabel(request.Type)}».");
        }
        else if (accountable is not null)
        {
            throw new DomainException("Сотрудник-получатель указывается только для выдачи, списания и возврата.");
        }

        // Акт списания должен отвечать на вопрос «куда пошло»: оборудование или описание.
        if (request.Type == MovementType.WriteOff && request.EquipmentId is null && string.IsNullOrWhiteSpace(request.Comment))
            throw new DomainException("Укажите, куда использовано: оборудование или описание (например, «замена подшипника на насосе Н-3»).");

        // ---------- 2. Строки: формат ----------
        for (var i = 0; i < lines.Count; i++)
            ValidateLineShape(request.Type, lines[i], i + 1);

        // ---------- 3. Строки: справочные данные ----------
        var items = await repo.GetItemsAsync(lines.Select(l => l.ItemId).Distinct(), ct);
        var locationIds = lines
            .SelectMany(l => new[] { l.FromLocationId, l.ToLocationId })
            .OfType<int>()
            .Distinct();
        var locations = await repo.GetLocationsAsync(locationIds, ct);

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var n = i + 1;

            if (!items.TryGetValue(line.ItemId, out var item))
                throw new NotFoundException($"Строка {n}: позиция {line.ItemId} не найдена.");
            if (!item.IsActive)
                throw new DomainException($"Строка {n}: позиция «{item.Name}» неактивна, операции с ней запрещены.");
            if (!item.Unit.IsFractional && line.Quantity != decimal.Truncate(line.Quantity))
                throw new DomainException($"Строка {n}: «{item.Name}» учитывается в «{item.Unit.Code}», количество должно быть целым.");

            foreach (var locationId in new[] { line.FromLocationId, line.ToLocationId }.OfType<int>())
            {
                // Хранить можно на любом уровне: деталь может лежать прямо на полке, где стоят и ящики.
                if (!locations.ContainsKey(locationId))
                    throw new NotFoundException($"Строка {n}: место хранения {locationId} не найдено.");
            }
        }

        // ---------- 4. Распределение по партиям и расчёт новых остатков (без изменения данных) ----------
        // Остатки ведутся по партиям: в ячейках склада и за сотрудниками (подотчёт).
        // Откуда берётся: указанная партия или самая старая (при нехватке — добор из следующих).
        // Приход и корректировка «+» без партии создают новую партию со своей ценой.
        var now = clock.GetUtcNow();
        var batches = new Dictionary<int, Batch>();           // известные партии
        var newBatches = new Dictionary<int, Batch>();        // временный ключ (< 0) → новая партия
        var stored = new Dictionary<(int ItemId, int LocationId, int BatchKey), StockBalance>();
        var projected = new Dictionary<(int ItemId, int LocationId, int BatchKey), decimal>();
        var storedEmployee = new Dictionary<(int ItemId, int EmployeeId, CustodyKind Kind, int BatchKey), EmployeeBalance>();
        var projectedEmployee = new Dictionary<(int ItemId, int EmployeeId, CustodyKind Kind, int BatchKey), decimal>();
        var reserved = new Dictionary<(int ItemId, int EmployeeId, CustodyKind Kind, int BatchKey), decimal>();
        var loadedLocations = new HashSet<(int ItemId, int LocationId)>();
        var loadedEmployees = new HashSet<(int ItemId, int EmployeeId)>();

        async Task LoadLocationAsync(int itemId, int locationId)
        {
            if (!loadedLocations.Add((itemId, locationId)))
                return;
            foreach (var b in await repo.GetLocationBatchesAsync(itemId, locationId, ct))
            {
                stored[(itemId, locationId, b.BatchId)] = b;
                projected[(itemId, locationId, b.BatchId)] = b.Quantity;
                batches[b.BatchId] = b.Batch;
            }
        }

        async Task LoadEmployeeAsync(int itemId, int employeeId)
        {
            if (!loadedEmployees.Add((itemId, employeeId)))
                return;
            foreach (var b in await repo.GetEmployeeBatchesAsync(itemId, employeeId, ct))
            {
                storedEmployee[(itemId, employeeId, b.Kind, b.BatchId)] = b;
                projectedEmployee[(itemId, employeeId, b.Kind, b.BatchId)] = b.Quantity;
                batches[b.BatchId] = b.Batch;
            }
            // Зарезервированное актами и отчётами вернуть или списать другим документом нельзя.
            foreach (var ((batchId, kind), quantity) in await repo.GetReservedByBatchAsync(itemId, employeeId, exceptActId, ct))
                reserved[(itemId, employeeId, kind, batchId)] = quantity;
        }

        // Порядок «самая старая партия первой».
        IEnumerable<int> Oldest(IEnumerable<int> keys) =>
            keys.OrderBy(k => batches.TryGetValue(k, out var b) ? b.ReceivedAt : DateTimeOffset.MaxValue).ThenBy(k => k);

        // Перевод вида подотчёта: убывает «в работе» и прибывает «по 1С» у того же сотрудника, теми же партиями.
        var isCustodyChange = request.Type == MovementType.CustodyChange;
        int? fromEmployeeId = request.Type is MovementType.WriteOff or MovementType.Return or MovementType.CustodyChange ? accountable!.Id : null;
        int? toEmployeeId = request.Type is MovementType.Issue or MovementType.CustodyChange ? accountable!.Id : null;

        // Вид подотчёта. Выдача со склада — «в работу» (в 1С остаётся на МОЛ кладовщика), если не сказано иное.
        // Возврат и списание берут из указанного вида, а если не указан — по порядку:
        // возврат — сначала «в работу», списание по акту сотрудника — сначала «подотчёт по 1С».
        var issueKind = isCustodyChange ? CustodyKind.Official : request.Custody ?? CustodyKind.Work;
        CustodyKind[] sourceKinds = isCustodyChange
            ? [CustodyKind.Work]
            : request.Custody is CustodyKind only
                ? [only]
                : request.Type == MovementType.Return ? [CustodyKind.Work, CustodyKind.Official] : [CustodyKind.Official, CustodyKind.Work];
        if (request.Custody is not null && (isCustodyChange || !UsesAccountable(request.Type)))
            throw new DomainException("Вид подотчёта указывается только для выдачи, возврата и списания.");
        if (request.Type == MovementType.Issue && issueKind == CustodyKind.Official)
            throw new DomainException("Подотчёт по 1С больше не оформляется: выдача — только «в работу». За кем числится в 1С — пометка МОЛ партии.");

        // МОЛ новых учётных партий — кладовщик: указанный; иначе тот, кто проводит (если он кладовщик);
        // иначе — единственный кладовщик. Пока кладовщиков с учётной записью нет — МОЛ не ставится.
        if (request.MolId is not null && request.Type is not (MovementType.Receipt or MovementType.Adjustment))
            throw new DomainException("МОЛ указывается только при поступлении.");
        int? batchMolId = null;
        if (!request.OffBook && request.Type is (MovementType.Receipt or MovementType.Adjustment)
            && lines.Any(l => l.ToLocationId is not null && l.FromLocationId is null && l.BatchId is null))
        {
            var storekeepers = await repo.ListStorekeeperIdsAsync(ct);
            batchMolId = request.MolId
                         ?? (performer is not null && storekeepers.Contains(performer.Id) ? performer.Id
                             : storekeepers.Count == 1 ? storekeepers[0] : null);
            if (batchMolId is int molId)
                await EnsureMolAsync(molId, storekeepers, ct);
            else if (storekeepers.Count > 1)
                throw new DomainException("Укажите МОЛ — за каким кладовщиком числится поступление.");
        }

        var parts = new List<(DocumentLineRequest Line, int BatchKey, decimal Quantity, CustodyKind? Kind)>();
        var nextTempKey = 0;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var n = i + 1;
            var item = items[line.ItemId];
            var taken = new List<(int BatchKey, decimal Quantity)>();
            CustodyKind?[] takenKinds = [];

            if (line.FromLocationId is int fromId)
            {
                if (line.UnitPrice is not null)
                    throw new DomainException($"Строка {n}: цена указывается только при поступлении (приход, корректировка «+»).");
                await LoadLocationAsync(line.ItemId, fromId);
                var keys = projected.Keys.Where(k => k.ItemId == line.ItemId && k.LocationId == fromId).Select(k => k.BatchKey);
                Allocate(n, item, line, Oldest(keys), k => projected[(line.ItemId, fromId, k)], _ => 0, taken,
                    (available, held) => new InsufficientStockException(item.Sku, locations[fromId].FullCode, available, line.Quantity));
                foreach (var (k, q) in taken)
                    projected[(line.ItemId, fromId, k)] -= q;
            }
            else if (fromEmployeeId is int fromEmp)
            {
                if (line.UnitPrice is not null)
                    throw new DomainException($"Строка {n}: цена указывается только при поступлении (приход, корректировка «+»).");
                await LoadEmployeeAsync(line.ItemId, fromEmp);
                // Ячейки подотчёта: вид × партия. Порядок — по видам (см. sourceKinds), внутри — самая старая партия.
                var slots = sourceKinds
                    .SelectMany(kind => Oldest(projectedEmployee.Keys
                            .Where(k => k.ItemId == line.ItemId && k.EmployeeId == fromEmp && k.Kind == kind)
                            .Select(k => k.BatchKey))
                        .Select(batchKey => (Kind: kind, BatchKey: batchKey)))
                    .Where(s => line.BatchId is not int chosen || s.BatchKey == chosen)
                    // Вне учёта в 1С нет — в подотчёт по 1С его не переводят.
                    .Where(s => !isCustodyChange || batches.GetValueOrDefault(s.BatchKey)?.OffBook != true)
                    .ToList();
                if (line.BatchId is not null && slots.Count == 0)
                    throw new DomainException($"Строка {n} («{item.Name}»): выбранной партии здесь нет.");

                // Резерв актов (подотчёт по 1С) и отчётов об использовании («в работе») — каждый на своём виде.
                Allocate(n, item, line with { BatchId = null }, Enumerable.Range(0, slots.Count),
                    s => projectedEmployee[(line.ItemId, fromEmp, slots[s].Kind, slots[s].BatchKey)],
                    s => reserved.GetValueOrDefault((line.ItemId, fromEmp, slots[s].Kind, slots[s].BatchKey)),
                    taken,
                    (available, held) => new InsufficientStockException(
                        item.Sku, accountable!.ShortName, available, line.Quantity, isEmployee: true, reserved: held));
                takenKinds = taken.Select(x => (CustodyKind?)slots[x.BatchKey].Kind).ToArray();
                for (var s = 0; s < taken.Count; s++)
                {
                    var (kind, batchKey) = slots[taken[s].BatchKey];
                    projectedEmployee[(line.ItemId, fromEmp, kind, batchKey)] -= taken[s].Quantity;
                    taken[s] = (batchKey, taken[s].Quantity);
                }
            }
            else if (line.BatchId is int existingId && request.Type == MovementType.Adjustment)
            {
                // Корректировка «+» в существующую партию (например, нашлось при пересчёте).
                var batch = await repo.GetBatchAsync(existingId, ct)
                    ?? throw new NotFoundException($"Строка {n}: партия {existingId} не найдена.");
                if (batch.ItemId != line.ItemId)
                    throw new DomainException($"Строка {n}: партия {existingId} относится к другой позиции.");
                if (line.UnitPrice is not null)
                    throw new DomainException($"Строка {n}: у существующей партии своя цена — укажите либо партию, либо цену.");
                batches[batch.Id] = batch;
                taken.Add((batch.Id, line.Quantity));
            }
            else
            {
                // Поступление: новая партия по цене из документа. Вне учёта (б/у и т.п.) цена необязательна.
                if (line.UnitPrice is null && !request.OffBook)
                    throw new DomainException($"Строка {n} («{item.Name}»): укажите цену за единицу.");
                var price = line.UnitPrice;
                if (price < 0)
                    throw new DomainException($"Строка {n}: цена не может быть отрицательной.");
                if (price is decimal given && decimal.Round(given, 4) != given)
                    throw new DomainException($"Строка {n}: цена — не больше 4 знаков после запятой.");
                var key = --nextTempKey;
                var note = string.IsNullOrWhiteSpace(line.BatchNote) ? null : line.BatchNote.Trim();
                if (note is { Length: > 256 })
                    note = note[..256];
                newBatches[key] = new Batch
                {
                    ItemId = line.ItemId, UnitPrice = price, ReceivedAt = line.BatchDate ?? now, Note = note,
                    OffBook = request.OffBook, Origin = request.Origin, MolId = batchMolId
                };
                taken.Add((key, line.Quantity));
            }

            for (var s = 0; s < taken.Count; s++)
            {
                var (k, q) = taken[s];
                CustodyKind? kind = s < takenKinds.Length ? takenKinds[s] : null;
                if (line.ToLocationId is int toId)
                {
                    await LoadLocationAsync(line.ItemId, toId);
                    projected[(line.ItemId, toId, k)] = projected.GetValueOrDefault((line.ItemId, toId, k)) + q;
                }
                if (toEmployeeId is int toEmp)
                {
                    await LoadEmployeeAsync(line.ItemId, toEmp);
                    projectedEmployee[(line.ItemId, toEmp, issueKind, k)] = projectedEmployee.GetValueOrDefault((line.ItemId, toEmp, issueKind, k)) + q;
                    kind = issueKind;
                }
                parts.Add((line, k, q, kind));
            }
        }

        // ---------- 5. Применение: всё сохраняется одной транзакцией ----------
        Batch BatchOf(int key) => key < 0 ? newBatches[key] : batches[key];

        var document = new StockDocument
        {
            Type = request.Type,
            OccurredAt = now,
            ExternalNumber = Clean(request.ExternalNumber),
            EquipmentId = request.EquipmentId,
            PerformedById = performer?.Id,
            PerformedBy = performer?.ShortName ?? Clean(request.PerformedBy),
            ReceivedById = accountable?.Id,
            ReceivedBy = accountable?.ShortName,
            Comment = Clean(request.Comment)
        };

        foreach (var batch in newBatches.Values)
        {
            batch.ReceiptDocument = document;
            repo.AddBatch(batch);
        }

        foreach (var (key, quantity) in projected)
        {
            if (stored.TryGetValue(key, out var balance))
                balance.Quantity = quantity;
            else if (quantity > 0)
                repo.AddBalance(NewBalance(new StockBalance { ItemId = key.ItemId, LocationId = key.LocationId, Quantity = quantity }, key.BatchKey));
        }

        foreach (var (key, quantity) in projectedEmployee)
        {
            if (storedEmployee.TryGetValue(key, out var balance))
                balance.Quantity = quantity;
            else if (quantity > 0)
                repo.AddEmployeeBalance(NewBalance(new EmployeeBalance { ItemId = key.ItemId, EmployeeId = key.EmployeeId, Kind = key.Kind, Quantity = quantity }, key.BatchKey));
        }

        // Строка документа может разойтись на несколько партий — каждая партия отдельной строкой журнала.
        var lineNo = 0;
        foreach (var (line, key, quantity, kind) in parts)
        {
            var batch = BatchOf(key);
            document.Lines.Add(new StockMovement
            {
                Document = document,
                LineNo = ++lineNo,
                Type = request.Type,
                OccurredAt = now,
                ItemId = line.ItemId,
                BatchId = key > 0 ? key : 0,
                Batch = batch,
                FromLocationId = line.FromLocationId,
                ToLocationId = line.ToLocationId,
                FromEmployeeId = fromEmployeeId,
                ToEmployeeId = toEmployeeId,
                CustodyKind = kind,
                Quantity = quantity
            });
        }

        repo.AddDocument(document);
        return new StagedDocument(document, items, locations);

        // Новая запись остатка ссылается на партию; у новой партии номер появится при сохранении.
        T NewBalance<T>(T balance, int batchKey) where T : class
        {
            switch (balance)
            {
                case StockBalance sb:
                    if (batchKey > 0) { sb.BatchId = batchKey; sb.Batch = batches[batchKey]; } else sb.Batch = newBatches[batchKey];
                    break;
                case EmployeeBalance eb:
                    if (batchKey > 0) { eb.BatchId = batchKey; eb.Batch = batches[batchKey]; } else eb.Batch = newBatches[batchKey];
                    break;
            }
            return balance;
        }
    }

    /// <summary>
    /// Берёт количество строки из партий: указанной (если задана) или по порядку «самая старая первой».
    /// Не хватает — исключение со свободным количеством (без зарезервированного).
    /// </summary>
    private static void Allocate(
        int n, Item item, DocumentLineRequest line, IEnumerable<int> oldestFirst,
        Func<int, decimal> current, Func<int, decimal> reservedOf,
        List<(int BatchKey, decimal Quantity)> taken,
        Func<decimal, decimal, Exception> shortage)
    {
        var keys = (line.BatchId is int chosen ? new[] { chosen } : oldestFirst).ToList();
        if (line.BatchId is int only && !oldestFirst.Contains(only))
            throw new DomainException($"Строка {n} («{item.Name}»): выбранной партии здесь нет.");

        var left = line.Quantity;
        decimal free = 0, held = 0;
        foreach (var key in keys)
        {
            var available = Math.Max(0, current(key) - reservedOf(key));
            free += available;
            held += reservedOf(key);
            var take = Math.Min(available, left);
            if (take > 0)
            {
                taken.Add((key, take));
                left -= take;
            }
            if (left == 0)
                return;
        }

        throw shortage(free, held);
    }


    public async Task<DocumentDto> GetDocumentAsync(long id, CancellationToken ct = default)
    {
        var document = await repo.GetDocumentAsync(id, ct)
            ?? throw new NotFoundException($"Документ {id} не найден.");

        return ToDto(document, line => new DocumentLineDto(
            line.LineNo, line.ItemId, line.Item.Sku, line.Item.Name, line.Item.Unit.Code, line.Quantity,
            line.FromLocation?.FullCode, line.ToLocation?.FullCode,
            line.BatchId, line.Batch?.UnitPrice, line.Batch?.ReceivedAt,
            line.FromLocation?.Name, line.ToLocation?.Name, line.Batch?.OffBook ?? false, line.Batch?.Mol?.ShortName));
    }

    public async Task<IReadOnlyList<DocumentSummaryDto>> ListDocumentsAsync(
        MovementType? type, int take = 100, IReadOnlyCollection<int>? onlyEmployeeIds = null, CancellationToken ct = default)
    {
        var headers = await repo.ListDocumentsAsync(type, Math.Clamp(take, 1, 500), onlyEmployeeIds, ct);
        return headers
            .Select(h => new DocumentSummaryDto(
                h.Document.Id, h.Document.Number, h.Document.Type, h.Document.OccurredAt,
                h.Document.ExternalNumber, h.Document.PerformedBy, h.LineCount, h.Document.ReceivedBy))
            .ToList();
    }

    public async Task<IReadOnlyList<StockBalanceDto>> GetBalancesAsync(int itemId, CancellationToken ct = default)
    {
        await EnsureItemExistsAsync(itemId, ct);
        var balances = await repo.GetBalancesForItemAsync(itemId, ct);
        return balances
            .GroupBy(b => (b.LocationId, b.Location.FullCode, b.Location.Name))
            .Select(g =>
            {
                var (value, incomplete) = Valuation.Of(g.Select(b => (b.Quantity, Valuation.Price(b.Batch))));
                return new StockBalanceDto(g.Key.LocationId, g.Key.FullCode, g.Sum(b => b.Quantity), value, incomplete, g.Key.Name,
                    g.Where(b => b.Batch?.OffBook == true).Sum(b => b.Quantity));
            })
            .OrderBy(b => b.LocationCode)
            .ToList();
    }

    /// <summary>Партии позиции с остатком: в месте, у сотрудника или всего (склад + подотчёт).</summary>
    public async Task<IReadOnlyList<BatchStockDto>> GetBatchesAsync(int itemId, int? locationId, int? employeeId, CancellationToken ct = default)
    {
        await EnsureItemExistsAsync(itemId, ct);
        var rows = await repo.ListBatchStockAsync(itemId, locationId, employeeId, ct);
        IReadOnlyDictionary<int, decimal> held = employeeId is int emp
            ? (await repo.GetReservedByBatchAsync(itemId, emp, null, ct))
                .GroupBy(kv => kv.Key.BatchId).ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value))
            : new Dictionary<int, decimal>();
        return rows
            .OrderBy(r => r.Batch.ReceivedAt).ThenBy(r => r.Batch.Id)
            .Select(r => new BatchStockDto(
                r.Batch.Id, r.Batch.ReceivedAt, r.Batch.UnitPrice, r.Quantity,
                r.Batch.ReceiptDocumentId, r.DocumentNumber, r.Batch.Note, held.GetValueOrDefault(r.Batch.Id), r.Batch.OffBook))
            .ToList();
    }

    /// <summary>Цена партии (администратор): вписать для остатков «без цены» или исправить ошибку.</summary>
    public async Task SetBatchPriceAsync(int batchId, decimal unitPrice, CancellationToken ct = default)
    {
        if (unitPrice < 0)
            throw new DomainException("Цена не может быть отрицательной.");
        if (decimal.Round(unitPrice, 4) != unitPrice)
            throw new DomainException("Цена — не больше 4 знаков после запятой.");
        var batch = await repo.GetBatchAsync(batchId, ct) ?? throw new NotFoundException($"Партия {batchId} не найдена.");
        batch.UnitPrice = unitPrice;
        await repo.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<MovementDto>> GetMovementsAsync(int itemId, int take = 100, CancellationToken ct = default)
    {
        await EnsureItemExistsAsync(itemId, ct);
        var movements = await repo.GetMovementsForItemAsync(itemId, Math.Clamp(take, 1, 1000), ct);
        return movements
            .Select(m => new MovementDto(
                m.DocumentId, m.Document.Number, m.Type, m.OccurredAt, m.Quantity,
                m.FromLocation?.FullCode, m.ToLocation?.FullCode,
                m.Document.EquipmentId, m.Document.PerformedBy, m.Document.ReceivedBy, m.Batch?.UnitPrice))
            .ToList();
    }

    /// <summary>Сколько строк остатков отдаётся за один запрос.</summary>
    public const int MaxStockRows = 2000;

    public async Task<StockSearchResult> SearchStockAsync(StockQuery query, CancellationToken ct = default)
    {
        var filter = new StockFilter(
            query.WarehouseId,
            LocationCodeMatch.Normalize(query.Location),
            string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            query.CategoryId,
            query.BelowMinimum,
            query.OffBook,
            query.MolId);

        // Берём на одну строку больше лимита, чтобы понять, что результат обрезан.
        var balances = await repo.SearchBalancesAsync(filter, MaxStockRows + 1, ct);

        // Строки остатков — по партиям; в таблице одна строка «позиция в месте» с суммой по партиям.
        var rows = balances
            .Take(MaxStockRows)
            .GroupBy(b => (b.ItemId, b.LocationId))
            .Select(g =>
            {
                var b = g.First();
                var (value, incomplete) = Valuation.Of(g.Select(x => (x.Quantity, Valuation.Price(x.Batch))));
                var offBook = g.Where(x => x.Batch?.OffBook == true).ToList();
                return new StockRowDto(
                    b.ItemId,
                    b.Item.Sku,
                    b.Item.Name,
                    b.Item.Category?.Name,
                    b.Item.Unit.Code,
                    b.Item.ImageUpdatedAt?.ToUnixTimeMilliseconds(),
                    b.Item.MinStock,
                    b.LocationId,
                    b.Location.FullCode,
                    b.Location.Name,
                    b.Location.WarehouseId,
                    b.Location.Warehouse.Code,
                    g.Sum(x => x.Quantity),
                    value,
                    incomplete,
                    offBook.Sum(x => x.Quantity),
                    offBook.Sum(x => x.Quantity * (x.Batch.UnitPrice ?? 0)),
                    MolsOf(g.Select(x => x.Batch)));
            })
            .ToList();

        return new StockSearchResult(rows, balances.Count > MaxStockRows);
    }

    public async Task<IReadOnlyList<LowStockItemDto>> GetBelowMinimumAsync(CancellationToken ct = default)
    {
        var rows = await repo.GetItemsBelowMinimumAsync(ct);
        return rows
            .Select(r => new LowStockItemDto(
                r.Item.Id,
                r.Item.Sku,
                r.Item.Name,
                r.Item.Unit.Code,
                r.Total,
                r.Item.MinStock,
                Math.Max(0, (r.Item.MaxStock ?? r.Item.MinStock) - r.Total)))
            .OrderBy(x => x.Name)
            .ToList();
    }

    public async Task<StockSummaryDto> GetSummaryAsync(CancellationToken ct = default)
    {
        var (items, locations, custodyEmployees, custodyItems) = await repo.CountStockAsync(ct);
        var low = await repo.GetItemsBelowMinimumAsync(ct);
        return new StockSummaryDto(items, locations,
            BelowMinimum: low.Count(x => x.Total > 0),
            OutOfStock: low.Count(x => x.Total <= 0),
            custodyEmployees, custodyItems);
    }

    /// <summary>МОЛ — только кладовщик склада (сотрудник с учётной записью и ролью «Кладовщик»).</summary>
    private async Task EnsureMolAsync(int employeeId, IReadOnlyList<int> storekeepers, CancellationToken ct)
    {
        var employee = await GetEmployeeAsync(employeeId, "МОЛ", requireActive: false, ct);
        if (!storekeepers.Contains(employeeId))
            throw new DomainException($"МОЛ склада — только кладовщик: у {employee!.ShortName} нет учётной записи с ролью «Кладовщик».");
    }

    /// <summary>МОЛ партий: «Дорофей С. М.»; null — не указан.</summary>
    internal static IReadOnlyList<string>? MolsOf(IEnumerable<Batch?> batches)
    {
        var names = batches.Select(b => b?.Mol?.ShortName).OfType<string>().Distinct().OrderBy(n => n).ToList();
        return names.Count == 0 ? null : names;
    }

    private async Task<Employee?> GetEmployeeAsync(int? id, string field, bool requireActive, CancellationToken ct)
    {
        if (id is not int employeeId)
            return null;

        var employee = await repo.GetEmployeeAsync(employeeId, ct)
            ?? throw new NotFoundException($"{field}: сотрудник {employeeId} не найден.");
        if (requireActive && !employee.IsActive)
            throw new DomainException($"{field}: сотрудник {employee.ShortName} отмечен как неработающий.");
        return employee;
    }

    /// <summary>Документы, где обязательно подотчётное лицо.</summary>
    public static bool UsesAccountable(MovementType type) =>
        type is MovementType.Issue or MovementType.WriteOff or MovementType.Return or MovementType.CustodyChange;

    private static string AccountableLabel(MovementType type) => type switch
    {
        MovementType.Issue => "Кому выдано",
        MovementType.WriteOff => "С кого списать",
        MovementType.Return => "Кто возвращает",
        MovementType.CustodyChange => "За кем числится",
        _ => "Сотрудник"
    };

    private async Task EnsureItemExistsAsync(int itemId, CancellationToken ct)
    {
        if (!await repo.ItemExistsAsync(itemId, ct))
            throw new NotFoundException($"Позиция {itemId} не найдена.");
    }

    private static void ValidateLineShape(MovementType type, DocumentLineRequest line, int n)
    {
        if (line.Quantity <= 0)
            throw new DomainException($"Строка {n}: количество должно быть больше нуля.");

        var hasFrom = line.FromLocationId.HasValue;
        var hasTo = line.ToLocationId.HasValue;

        switch (type)
        {
            case MovementType.Receipt:
                if (!hasTo || hasFrom)
                    throw new DomainException($"Строка {n}: для прихода укажите только место, куда поступает товар.");
                break;

            case MovementType.Issue:
                if (!hasFrom || hasTo)
                    throw new DomainException($"Строка {n}: для выдачи укажите только место, откуда берётся товар.");
                break;

            case MovementType.WriteOff:
                if (hasFrom || hasTo)
                    throw new DomainException($"Строка {n}: при списании место не указывается — списывается с сотрудника.");
                break;

            case MovementType.CustodyChange:
                if (hasFrom || hasTo)
                    throw new DomainException($"Строка {n}: при переводе в подотчёт по 1С место не указывается.");
                break;

            case MovementType.Return:
                if (!hasTo || hasFrom)
                    throw new DomainException($"Строка {n}: для возврата укажите только место, куда кладётся товар.");
                break;

            case MovementType.Transfer:
                if (!hasFrom || !hasTo)
                    throw new DomainException($"Строка {n}: для перемещения укажите место отправления и место назначения.");
                if (line.FromLocationId == line.ToLocationId)
                    throw new DomainException($"Строка {n}: место отправления и место назначения совпадают.");
                break;

            case MovementType.Adjustment:
                if (hasFrom == hasTo)
                    throw new DomainException($"Строка {n}: для корректировки укажите ровно одно место (куда — увеличить, откуда — уменьшить).");
                break;
        }
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DocumentDto ToDto(StockDocument d, Func<StockMovement, DocumentLineDto> mapLine) => new(
        d.Id, d.Number, d.Type, d.OccurredAt, d.ExternalNumber, d.EquipmentId, d.PerformedBy, d.Comment,
        d.Lines.OrderBy(l => l.LineNo).Select(mapLine).ToList(),
        d.PerformedById, d.ReceivedById, d.ReceivedBy,
        d.Act?.Id, d.Act?.Number);
}
