namespace SmartWarehouse.Application.DTOs;

// ---------- Начальные остатки из файла «Начальные остатки — готово (1С на …)» ----------

/// <summary>Строка файла начальных остатков: одна партия 1С.</summary>
/// <param name="Line">Номер строки в файле (для сообщений).</param>
/// <param name="Custody">Партия числится за сотрудником (МОЛ, не основной склад) — в подотчёт.</param>
/// <param name="Mol">МОЛ в 1С: «Рудаков М.А.».</param>
/// <param name="Address">Адрес хранения на складе: «2A.2.1.3».</param>
public record OpeningRow(
    int Line,
    string? Account,
    string Code,
    string Name,
    string Unit,
    decimal Quantity,
    decimal Value,
    string? Document,
    DateOnly? DocumentDate,
    bool Custody,
    string? Mol,
    string? Address,
    string? Description,
    string? Note);

/// <summary>Замечание по строке импорта: строка пропущена (Skipped) или только предупреждение.</summary>
public record ImportIssue(int Line, string? Code, string Message, bool Skipped = true);

/// <summary>Итог импорта начальных остатков (проверка или загрузка).</summary>
public record OpeningImportResult(
    bool Applied,
    int Rows,
    int RowsImported,
    int ItemsNew,
    int ItemsExisting,
    int UnitsNew,
    int LocationsNew,
    int EmployeesNew,
    int WarehouseBatches,
    int CustodyBatches,
    decimal Value,
    IReadOnlyList<ImportIssue> Issues,
    IReadOnlyList<string> Notes,
    IReadOnlyList<long> DocumentIds);

// ---------- Сверка с сальдовой ведомостью 1С ----------

/// <summary>Партия из «Сальдовой ведомости ТМЦ» 1С.</summary>
public record SaldoBatch(string Account, string? Department, string Mol, string Code, string Name, string Unit,
    string? Document, decimal? Price, decimal Quantity, decimal? Value);

/// <summary>Сальдовая ведомость 1С (один файл): дата остатков и партии.</summary>
public record SaldoSheet(string FileName, string? AsOf, IReadOnlyList<SaldoBatch> Batches);

public enum ReconcileStatus
{
    /// <summary>Количество и стоимость совпадают.</summary>
    Match,

    /// <summary>Количество совпадает, стоимость — нет (цены партий, партии «без цены»).</summary>
    ValueDiffers,

    /// <summary>Количество расходится.</summary>
    QtyDiffers,

    /// <summary>Есть в 1С, нет в приложении.</summary>
    OnlyIn1C,

    /// <summary>Есть в приложении, нет в 1С.</summary>
    OnlyInApp
}

/// <summary>Строка сверки: позиция у МОЛ (склад или сотрудник).</summary>
/// <param name="Place">«Склад» или ФИО сотрудника в приложении.</param>
public record ReconcileRowDto(
    ReconcileStatus Status,
    string Code,
    string Name,
    string? Unit,
    string Mol,
    string Place,
    int? ItemId,
    decimal Qty1C,
    decimal QtyApp,
    decimal? Value1C,
    decimal? ValueApp);

/// <summary>Итог сверки.</summary>
/// <param name="WarehouseMol">МОЛ, чьи остатки сравниваются со складом приложения.</param>
/// <param name="Mols">МОЛ из файлов и с кем сопоставлены («Склад», сотрудник или null — не найден).</param>
public record ReconcileResultDto(
    string? AsOf,
    string? WarehouseMol,
    IReadOnlyList<ReconcileMolDto> Mols,
    IReadOnlyList<ReconcileRowDto> Rows,
    IReadOnlyList<string> Notes);

public record ReconcileMolDto(string Mol, string? Place, int? EmployeeId, int Batches, decimal Value);
