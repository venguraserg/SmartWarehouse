using SmartWarehouse.Application.DTOs;

namespace SmartWarehouse.Application.Interfaces;

/// <summary>Чтение Excel-файлов для импорта и сверки (реализация — в Infrastructure).</summary>
public interface IImportFileReader
{
    /// <summary>Файл «Начальные остатки — готово»: лист «Импорт», колонки по заголовкам.</summary>
    OpeningFile ReadOpening(Stream file);

    /// <summary>«Накладная на внутреннее перемещение материалов» из 1С (.xls или .xlsx).</summary>
    InvoiceFile ReadInvoice(Stream file, string fileName);

    /// <summary>«Сальдовая ведомость ТМЦ» из 1С: счёт → подразделение → МОЛ → позиция → партии (уровни группировки).</summary>
    SaldoSheet ReadSaldo(Stream file, string fileName);
}

/// <summary>Прочитанный файл начальных остатков: строки и то, что прочитать не удалось.</summary>
public record OpeningFile(IReadOnlyList<OpeningRow> Rows, IReadOnlyList<ImportIssue> Issues);

/// <summary>Учётная партия, введённая вручную и ещё лежащая на складе.</summary>
public record ManualBatch(int ItemId, int BatchId, string? DocumentNumber, DateTimeOffset ReceivedAt, decimal Quantity);

/// <summary>
/// Что числится за МОЛ по позиции — для сверки с 1С: остатки во всех местах (и «Место не указано»)
/// плюс «выдано в работу», по пометке МОЛ партий. <paramref name="MolId"/> null — основной МОЛ. Вне учёта не входит.
/// </summary>
public record MolTotal(int? MolId, int ItemId, string Sku, string Name, string Unit, decimal Quantity, decimal Value, bool ValueIncomplete);
