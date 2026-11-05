using SmartWarehouse.Application.DTOs;

namespace SmartWarehouse.Application.Interfaces;

/// <summary>Чтение Excel-файлов для импорта и сверки (реализация — в Infrastructure).</summary>
public interface IImportFileReader
{
    /// <summary>Файл «Начальные остатки — готово»: лист «Импорт», колонки по заголовкам.</summary>
    OpeningFile ReadOpening(Stream file);

    /// <summary>«Сальдовая ведомость ТМЦ» из 1С: счёт → подразделение → МОЛ → позиция → партии (уровни группировки).</summary>
    SaldoSheet ReadSaldo(Stream file, string fileName);
}

/// <summary>Прочитанный файл начальных остатков: строки и то, что прочитать не удалось.</summary>
public record OpeningFile(IReadOnlyList<OpeningRow> Rows, IReadOnlyList<ImportIssue> Issues);

/// <summary>Итоги остатков для сверки с 1С.</summary>
public record ItemTotal(int ItemId, string Sku, string Name, string Unit, decimal Quantity, decimal Value, bool ValueIncomplete);

public record CustodyTotal(int EmployeeId, int ItemId, string Sku, string Name, string Unit, decimal Quantity, decimal Value, bool ValueIncomplete);
