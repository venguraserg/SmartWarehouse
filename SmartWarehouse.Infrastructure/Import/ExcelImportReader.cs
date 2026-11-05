using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Infrastructure.Import;

/// <summary>Чтение Excel (ClosedXML): файл начальных остатков и «Сальдовая ведомость ТМЦ» из 1С.</summary>
public partial class ExcelImportReader : IImportFileReader
{
    // ---------- Начальные остатки ----------

    /// <summary>Колонки ищутся по заголовку (первая строка листа «Импорт»): порядок может быть любым.</summary>
    private static readonly Dictionary<string, string[]> Columns = new()
    {
        ["account"] = ["Счёт", "Счет"],
        ["code"] = ["Код 1С", "Код"],
        ["name"] = ["Наименование (1С)", "Наименование"],
        ["unit"] = ["Ед. изм."],
        ["qty"] = ["Количество"],
        ["value"] = ["Стоимость, руб.", "Стоимость"],
        ["doc"] = ["Документ поступления", "Накладная"],
        ["date"] = ["Дата документа", "Дата поступления"],
        ["where"] = ["Где"],
        ["mol"] = ["МОЛ в 1С", "МОЛ"],
        ["address"] = ["Адрес хранения", "Адрес"],
        ["description"] = ["Описание (из ведомости склада)", "Описание"],
        ["note"] = ["Примечание"],
    };

    public OpeningFile ReadOpening(Stream file)
    {
        using var book = Open(file);
        var sheet = book.Worksheets.FirstOrDefault(w => w.Name.Equals("Импорт", StringComparison.OrdinalIgnoreCase))
                    ?? book.Worksheets.First();
        var header = sheet.Row(1);
        var map = new Dictionary<string, int>();
        foreach (var cell in header.CellsUsed())
        {
            var title = Text(cell)?.Trim();
            if (title is null)
                continue;
            foreach (var (key, names) in Columns)
                if (!map.ContainsKey(key) && names.Any(n => string.Equals(n, title, StringComparison.OrdinalIgnoreCase)))
                    map[key] = cell.Address.ColumnNumber;
        }
        var missing = new[] { "code", "name", "unit", "qty", "value" }.Where(k => !map.ContainsKey(k)).ToList();
        if (missing.Count > 0)
            throw new DomainException("В файле нет колонок: " + string.Join(", ", missing.Select(k => "«" + Columns[k][0] + "»")) +
                                      ". Нужен лист «Импорт» из файла «Начальные остатки — готово».");

        var rows = new List<OpeningRow>();
        var issues = new List<ImportIssue>();
        var last = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var r = 2; r <= last; r++)
        {
            var row = sheet.Row(r);
            string? Get(string key) => map.TryGetValue(key, out var c) ? Text(row.Cell(c))?.Trim() : null;
            var code = Get("code");
            var name = Get("name");
            if (string.IsNullOrEmpty(code) && string.IsNullOrEmpty(name))
                continue;
            var qty = Number(row.Cell(map["qty"]));
            var value = Number(row.Cell(map["value"]));
            if (qty is null || value is null)
            {
                issues.Add(new ImportIssue(r, code, "Не число в «Количество» или «Стоимость»."));
                continue;
            }
            var where = Get("where") ?? "";
            rows.Add(new OpeningRow(
                r, Get("account"), code ?? "", name ?? "", Get("unit") ?? "", qty.Value, value.Value,
                Get("doc"), map.TryGetValue("date", out var dc) ? Date(row.Cell(dc)) : null,
                where.StartsWith("Подотчёт", StringComparison.OrdinalIgnoreCase) || where.StartsWith("Подотчет", StringComparison.OrdinalIgnoreCase),
                Get("mol"), Get("address"), Get("description"), Get("note")));
        }
        return new OpeningFile(rows, issues);
    }

    // ---------- Сальдовая ведомость 1С ----------

    [GeneratedRegex(@"Остатки на:?\s*(\d{2}\.\d{2}\.\d{4})")]
    private static partial Regex AsOfPattern();

    /// <summary>
    /// Уровни группировки строк: 0 — счёт (10.6), 1 — подразделение, 2 — МОЛ, 3 — позиция (код, наименование, ед., «х»),
    /// 4 — партия (код, документ, ед., цена, количество, стоимость).
    /// </summary>
    public SaldoSheet ReadSaldo(Stream file, string fileName)
    {
        using var book = Open(file);
        var sheet = book.Worksheets.First();
        string? asOf = null, account = null, department = null, mol = null, name = null;
        var batches = new List<SaldoBatch>();
        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = 1; r <= last; r++)
        {
            var row = sheet.Row(r);
            var cells = row.CellsUsed().Where(c => !c.IsEmpty()).ToList();
            if (cells.Count == 0)
                continue;
            var first = Text(cells[0])?.Trim() ?? "";
            if (asOf is null && AsOfPattern().Match(first) is { Success: true } m)
            {
                asOf = m.Groups[1].Value;
                continue;
            }
            switch (row.OutlineLevel)
            {
                case 0:
                    if (Regex.IsMatch(first, @"^\d{2,3}\.\d{1,2}$"))
                        account = first;
                    break;
                case 1:
                    department = first;
                    break;
                case 2:
                    mol = first;
                    break;
                case 3:
                    name = cells.Count > 1 ? Text(cells[1])?.Trim() : null;
                    break;
                case 4 when mol is not null && account is not null:
                    var texts = cells.Skip(1).Where(c => c.Value.IsText).Select(c => c.Value.GetText().Trim()).ToList();
                    var numbers = cells.Skip(1).Where(c => c.Value.IsNumber).Select(c => (decimal)c.Value.GetNumber()).ToList();
                    decimal? price = null, value = null;
                    decimal qty;
                    if (numbers.Count >= 3)
                        (price, qty, value) = (numbers[^3], numbers[^2], numbers[^1]);
                    else if (numbers.Count == 2)
                        (qty, value) = (numbers[0], numbers[1]);
                    else if (numbers.Count == 1)
                        qty = numbers[0];
                    else
                        continue;
                    batches.Add(new SaldoBatch(account, department, mol, first, name ?? "", texts.Count > 1 ? texts[1] : "",
                        texts.FirstOrDefault(), price, qty, value));
                    break;
            }
        }
        if (batches.Count == 0)
            throw new DomainException($"«{fileName}»: не похоже на «Сальдовую ведомость ТМЦ» из 1С (нет строк партий с группировкой).");
        return new SaldoSheet(fileName, asOf, batches);
    }

    // ---------- Ячейки ----------

    private static XLWorkbook Open(Stream file)
    {
        try
        {
            return new XLWorkbook(file);
        }
        catch (Exception e) when (e is not DomainException)
        {
            throw new DomainException("Не удалось открыть файл Excel (.xlsx): " + e.Message);
        }
    }

    private static XLCellValue Value(IXLCell cell) => cell.HasFormula ? cell.CachedValue : cell.Value;

    private static string? Text(IXLCell cell)
    {
        var v = Value(cell);
        if (v.IsBlank)
            return null;
        if (v.IsText)
            return v.GetText();
        if (v.IsNumber)
            return v.GetNumber().ToString(CultureInfo.InvariantCulture);
        if (v.IsDateTime)
            return v.GetDateTime().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        return v.ToString();
    }

    private static decimal? Number(IXLCell cell)
    {
        var v = Value(cell);
        if (v.IsNumber)
            return (decimal)v.GetNumber();
        if (v.IsText && decimal.TryParse(v.GetText().Replace(' ', ' ').Replace(" ", "").Replace(',', '.'),
                NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
            return d;
        return null;
    }

    private static DateOnly? Date(IXLCell cell)
    {
        var v = Value(cell);
        if (v.IsDateTime)
            return DateOnly.FromDateTime(v.GetDateTime());
        if (v.IsNumber && v.GetNumber() is > 20000 and < 80000)
            return DateOnly.FromDateTime(DateTime.FromOADate(v.GetNumber()));
        if (v.IsText && DateOnly.TryParseExact(v.GetText().Trim(), ["dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        return null;
    }
}
