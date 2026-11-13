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

    // ---------- Накладная на внутреннее перемещение (.xls / .xlsx) ----------

    /// <summary>
    /// Печатная форма 1С с объединёнными ячейками: шапка («Номер документа», «Дата составления», корр. счёт)
    /// и таблица («№ п/п», наименование, номенклатурный номер, единица, цена, количество, сумма) до строки «Итого».
    /// Колонки ищутся по надписям — сдвиг формы на колонку-другую не мешает.
    /// </summary>
    public InvoiceFile ReadInvoice(Stream file, string fileName)
    {
        var grid = ReadGrid(file, fileName);
        string Norm(object? v) => Regex.Replace(Cell(v) ?? "", @"[\s\-]+", "").ToLowerInvariant();
        (int Row, int Col)? Find(string start, int fromRow = 0, int toRow = int.MaxValue)
        {
            for (var r = fromRow; r < Math.Min(grid.Count, toRow); r++)
                for (var c = 0; c < grid[r].Length; c++)
                    if (Norm(grid[r][c]).StartsWith(start, StringComparison.Ordinal))
                        return (r, c);
            return null;
        }
        object? Below(int row, int col, int depth = 3)
        {
            for (var r = row + 1; r <= row + depth && r < grid.Count; r++)
                if (col < grid[r].Length && Cell(grid[r][col]) is { Length: > 0 })
                    return grid[r][col];
            return null;
        }

        var head = Find("№п/п") ?? throw new DomainException(
            $"«{fileName}»: не похоже на «Накладную на внутреннее перемещение материалов» — нет таблицы с «№ п/п».");
        string? number = Find("номердокумента", 0, head.Row) is { } n ? Cell(Below(n.Row, n.Col)) : null;
        DateOnly? date = Find("датасоставле", 0, head.Row) is { } d ? DateOf(Below(d.Row, d.Col)) : null;
        string? account = Find("счет,субсчет", 0, head.Row) is { } a ? Cell(Below(a.Row, a.Col, 1)) : null;

        int Column(string start, bool required = true) =>
            Find(start, head.Row, head.Row + 2)?.Col
            ?? (required ? throw new DomainException($"«{fileName}»: в таблице накладной нет колонки «{start}»."): -1);
        var nameCol = Column("материальныеценности");
        var codeCol = Column("номенклатур");
        var unitCol = Column("единица");
        var priceCol = Column("цена", required: false);
        var qtyCol = Column("надлежит", required: false);
        var doneCol = Column("отпущено", required: false);
        var sumCol = Column("сумма", required: false);
        if (qtyCol < 0 && doneCol < 0)
            qtyCol = Column("количество");

        var lines = new List<InvoiceLine>();
        var issues = new List<ImportIssue>();
        for (var r = head.Row + 2; r < grid.Count; r++)
        {
            var row = grid[r];
            object? At(int c) => c >= 0 && c < row.Length ? row[c] : null;
            if (Norm(At(head.Col)).StartsWith("итого", StringComparison.Ordinal))
                break;
            var code = Cell(At(codeCol));
            var name = Cell(At(nameCol));
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(name) || (name == "2" && code == "3")) // «3» — строка с номерами граф
                continue;
            var qty = Num(At(doneCol)) is decimal done and > 0 ? done : Num(At(qtyCol));
            if (qty is null)
            {
                issues.Add(new ImportIssue(r + 1, code, "Количество не число."));
                continue;
            }
            lines.Add(new InvoiceLine(r + 1, code, name, Cell(At(unitCol)) ?? "", Num(At(priceCol)), qty.Value, Num(At(sumCol))));
        }
        return new InvoiceFile(number, date, account, lines, issues);
    }

    /// <summary>Первый лист файла .xls или .xlsx — как таблица значений.</summary>
    private static List<object?[]> ReadGrid(Stream file, string fileName)
    {
        // Старый формат .xls хранит текст в кодовых страницах Windows (1251).
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        try
        {
            using var copy = new MemoryStream();
            file.CopyTo(copy);
            copy.Position = 0;
            using var reader = ExcelDataReader.ExcelReaderFactory.CreateReader(copy);
            var grid = new List<object?[]>();
            while (reader.Read())
            {
                var row = new object?[reader.FieldCount];
                for (var c = 0; c < row.Length; c++)
                    row[c] = reader.GetValue(c);
                grid.Add(row);
            }
            return grid;
        }
        catch (Exception e) when (e is not DomainException)
        {
            throw new DomainException($"Не удалось открыть «{fileName}» как Excel (.xls или .xlsx): {e.Message}");
        }
    }

    private static string? Cell(object? value) => value switch
    {
        null => null,
        string s => Regex.Replace(s, @"\s+", " ").Trim() is { Length: > 0 } t ? t : null,
        double d => d.ToString(d == Math.Truncate(d) ? "0" : "0.####", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };

    private static decimal? Num(object? value) => value switch
    {
        double d => (decimal)d,
        int i => i,
        decimal m => m,
        string s when decimal.TryParse(Regex.Replace(s, @"[\s\u00a0]", "").Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null
    };

    private static DateOnly? DateOf(object? value) => value switch
    {
        DateTime dt => DateOnly.FromDateTime(dt),
        double d when d is > 20000 and < 80000 => DateOnly.FromDateTime(DateTime.FromOADate(d)),
        string s when DateOnly.TryParseExact(s.Trim(), ["dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) => parsed,
        _ => null
    };

    // ---------- Ячейки ----------

    private static XLWorkbook Open(Stream file)
    {
        try
        {
            return new XLWorkbook(WithoutDrawings(file));
        }
        catch (Exception e) when (e is not DomainException)
        {
            throw new DomainException("Не удалось открыть файл Excel (.xlsx): " + e.Message);
        }
    }

    [GeneratedRegex(@"^xl/(comments\d*\.xml|drawings/(vmlDrawing\d*\.vml|drawing\d*\.xml|_rels/drawing\d*\.xml\.rels))$", RegexOptions.IgnoreCase)]
    private static partial Regex DrawingPart();

    /// <summary>
    /// Копия файла без примечаний к ячейкам и рисунков. Для чтения данных они не нужны, а файлы, сохранённые
    /// LibreOffice (и некоторые выгрузки), ClosedXML не открывает: «Sequence contains no matching element».
    /// </summary>
    internal static MemoryStream WithoutDrawings(Stream file)
    {
        var copy = new MemoryStream();
        file.CopyTo(copy);
        copy.Position = 0;
        using (var zip = new System.IO.Compression.ZipArchive(copy, System.IO.Compression.ZipArchiveMode.Update, leaveOpen: true))
        {
            var removed = zip.Entries.Where(e => DrawingPart().IsMatch(e.FullName)).ToList();
            if (removed.Count == 0)
            {
                copy.Position = 0;
                return copy;
            }
            var names = removed.Select(e => "/" + e.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in removed)
                entry.Delete();

            foreach (var entry in zip.Entries.ToList())
            {
                var name = entry.FullName;
                if (name.StartsWith("xl/worksheets/_rels/", StringComparison.OrdinalIgnoreCase))
                    Rewrite(entry, doc => doc.Root!.Elements()
                        .Where(r => (string?)r.Attribute("Type") is { } type
                                    && (type.EndsWith("/comments") || type.EndsWith("/vmlDrawing") || type.EndsWith("/drawing")))
                        .ToList().ForEach(r => r.Remove()));
                else if (Regex.IsMatch(name, @"^xl/worksheets/sheet\d*\.xml$", RegexOptions.IgnoreCase))
                    Rewrite(entry, doc => doc.Root!.Elements()
                        .Where(e => e.Name.LocalName is "legacyDrawing" or "drawing")
                        .ToList().ForEach(e => e.Remove()));
                else if (name == "[Content_Types].xml")
                    Rewrite(entry, doc => doc.Root!.Elements()
                        .Where(o => (string?)o.Attribute("PartName") is { } part && names.Contains(part))
                        .ToList().ForEach(o => o.Remove()));
            }
        }
        copy.Position = 0;
        return copy;
    }

    private static void Rewrite(System.IO.Compression.ZipArchiveEntry entry, Action<System.Xml.Linq.XDocument> change)
    {
        System.Xml.Linq.XDocument doc;
        using (var read = entry.Open())
            doc = System.Xml.Linq.XDocument.Load(read);
        change(doc);
        using var write = entry.Open();
        write.SetLength(0);
        doc.Save(write);
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
