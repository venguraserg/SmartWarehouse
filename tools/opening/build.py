"""
Файлы начального импорта на основе 1С:
  количество, цены, партии, коды, наименования, МОЛ — из «Сальдовых ведомостей ТМЦ» 1С (на 31.10.2026);
  адреса хранения, уточнения наименований, примечания — из ручной ведомости склада.

Запуск: python3 -I build.py <data.json от parse1c.py> <ручная ведомость.xlsx> <папка результатов> <дата 1С ДД.ММ.ГГГГ> <дата ведомости>

v2 (ноябрь 2026):
  • адреса со стеллажами приводятся к виду Зона.Стеллаж.Полка.Ящик (1.A5.1.3) — norm.py;
  • адрес не по схеме / несколько мест / нет в ведомости — партия всё равно «готово», адрес как есть:
    приложение положит её в «Сортировку» с прежним местом в примечании;
  • количество 1С ≠ ведомости — не блокирует (1С главная), список — для пересчёта;
  • неучтённое и б/у из ведомости — строки «Где» = «Вне учёта» с автоартикулом (NU-…, BU…).
"""
import collections
import datetime as dt
import json
import re
import sys

import openpyxl
from openpyxl.styles import Alignment, Border, Font, PatternFill, Side
from openpyxl.utils import get_column_letter

DATA, MANUAL, OUT, AS_OF, MANUAL_AS_OF = sys.argv[1:6]
sys.path.insert(0, __import__("os").path.dirname(__file__))
from norm import normalize, is_rack  # noqa: E402
MAIN_MOL = "Дорофей С.М."          # МОЛ основного склада
OWN_DEPT = "Отдел эксплуатации технологического оборудования"

UNIT_MAP = {
    "шт": "шт", "м3": "м3", "бал.": "бал", "бал": "бал", "м": "м", "пог. м": "м", "м.пог": "м", "м.пог.": "м", "пог.м": "м", "м.п.": "м", "кг": "кг", "т": "т", "л": "л",
    "упак": "упак", "уп": "упак", "уп.": "упак", "комплект": "компл", "тыс.шт": "тыс. шт", "100 штук": "100 шт", "см": "см", "м2": "м2",
    "рул": "рул", "пара": "пара", "пар": "пара", "км": "км", "пач": "пач", "тыс.м.": "тыс. м", "бут": "бут",
}
# Как в приложении (OpeningImportService.FractionalUnits): в остальных единицах — только целые.
FRACTIONAL = {"м", "кг", "л", "т", "м2", "м3", "км", "см", "тыс. м", "тыс. шт", "100 шт"}
UNIT_UNCLEAR = {"бал.": "«бал.» — баллон? (единица из справочника 1С)"}
LOOK = str.maketrans("АВСЕНКМОРТХ", "ABCEHKMOPTX")
ADDRESS_OK = re.compile(r"^[0-9A-Z]+(\.[0-9A-Z]+)*$")


def num(x):
    if x is None or x == "":
        return None
    if isinstance(x, (int, float)):
        return float(x)
    try:
        return float(str(x).replace("\xa0", "").replace(" ", "").replace(",", "."))
    except ValueError:
        return None


def clean(s):
    return re.sub(r"\s+", " ", str(s)).strip() if s is not None else ""


def norm_name(s):
    return re.sub(r"[^0-9a-zа-яё]", "", s.lower())


def address(raw):
    """→ (адрес по схеме или None, что исправлено)."""
    s = clean(raw)
    if not s:
        return None, None
    if is_rack(s):
        new, status, note = normalize(s)
        return (new, note or None) if new and status != "ask" else (None, None)
    fix = []
    if s.lower().startswith("полка,"):
        s = s[6:].strip()
        fix.append("убрано «полка,»")
    t = s.translate(LOOK)
    if t != s:
        fix.append("русские буквы → латинские")
        s = t
    if s.endswith("."):
        s = s.rstrip(".")
        fix.append("убрана точка")
    s = s.upper() if re.fullmatch(r"[0-9a-zA-Z.]+", s) else s
    return (s, ", ".join(fix) or None) if ADDRESS_OK.match(s) else (None, None)


def doc_parts(doc):
    """«ТТН 3834079 от 27.05.2026 г.» → («ТТН 3834079», дата)."""
    ms = list(re.finditer(r"от (\d{2})\.(\d{2})\.(\d{4})", doc))
    m = ms[-1] if ms else None   # «УПД от 22.11.2023 368 от 30.11.2023 г.» — номер и дата — последние
    date = dt.date(int(m.group(3)), int(m.group(2)), int(m.group(1))) if m else None
    number = doc[:m.start()].strip() if m else doc.strip()
    return number or None, date


# ---------------------------------------------------------------- данные

batches = json.load(open(DATA))["batches"]

ws = openpyxl.load_workbook(MANUAL, data_only=True).worksheets[0]
manual = []
for i in range(11, ws.max_row + 1):
    r = [c.value for c in ws[i]][:15]
    if r[2] is None:
        continue
    raw = clean(r[0])
    manual.append(dict(line=i, raw=raw, code=raw.lstrip("0") if raw.isdigit() else None, name=clean(r[2]),
                       price=num(r[4]), qty=num(r[11]), addr_raw=clean(r[1]), note=clean(r[14]), unit=clean(r[3])))
by_code = collections.defaultdict(list)
for m in manual:
    if m["code"]:
        by_code[m["code"]].append(m)

codes_1c = {b["code"] for b in batches}

# ---------------------------------------------------------------- разбор партий 1С

rows = []
mismatch = []
for b in batches:
    probs, quest, info = [], [], []
    code = b["code"]
    name = clean(b["name"])
    unit_1c = clean(b["unit"])
    unit = UNIT_MAP.get(unit_1c.lower())
    if unit is None:
        probs.append("единица неясна")
        quest.append(UNIT_UNCLEAR.get(unit_1c.lower(), f"Единица «{unit_1c}» — какая?"))
    qty = b["qty"]
    value = b["value"]
    number, date = doc_parts(b["doc"])
    custody = b["person"] != MAIN_MOL
    dept = b["dept"]
    if dept != OWN_DEPT and not (dept or "").startswith("Отдел эксплуатации"):
        probs.append("другое подразделение")
        quest.append(f"В 1С числится за подразделением «{dept}». Переносить в наш склад?")
    if unit and unit not in FRACTIONAL and qty and qty != int(qty):
        probs.append("дробное количество")
        quest.append(f"В 1С числится {qty:g} {unit} — дробное в «{unit}» приложение не примет. "
                     "Это другая единица (м, кг, упаковка)? Или округлить?")
    if b["acc"] == "002.1":
        probs.append("забалансовый счёт 002.1")
        quest.append("Счёт 002.1 — ТМЦ на ответственном хранении (не наши). Переносить?")
    if value is None or qty in (None, 0):
        probs.append("нет стоимости")
        quest.append("В 1С не указана стоимость партии — какая цена?")

    ms = by_code.get(code, [])
    addr, fix, desc, note, line, sort_why = None, None, None, None, None, None
    if ms:
        line = ", ".join(str(m["line"]) for m in ms[:4])
        # наименование из ручного файла, если отличается, — в описание (оборудование, производитель)
        alt = [m["name"] for m in ms if norm_name(m["name"]) != norm_name(name)]
        desc = alt[0] if alt else None
        notes = {m["note"] for m in ms if m["note"] and m["note"] != "10.9"}
        note = "; ".join(sorted(notes)) or None
    if not custody:
        cand = ms
        exact = [m for m in ms if m["price"] and value and qty and abs(m["price"] - value / qty) < 0.02]
        if exact:
            cand = exact
        good = {address(m["addr_raw"])[0] for m in cand} - {None}
        bad = sorted({m["addr_raw"] or "(пусто)" for m in cand if address(m["addr_raw"])[0] is None})
        # Адрес не определён однозначно — партия всё равно идёт в импорт: приложение положит её
        # в «Сортировку», а этот текст запишет в примечание как «Прежнее место: …».
        if not ms:
            addr, sort_why = "нет в ведомости", "нет в ведомости"
        elif len(good) == 1 and not bad:
            addr = good.pop()
            fix = next((address(m["addr_raw"])[1] for m in cand if address(m["addr_raw"])[0] == addr and address(m["addr_raw"])[1]), None)
        else:
            addr = ", ".join(sorted(good) + bad)
            sort_why = "несколько мест" if len(good) + len(bad) > 1 else "адрес не по схеме"
        manual_qty = sum(m["qty"] or 0 for m in ms)
        qty_1c_main = sum(x["qty"] or 0 for x in batches if x["code"] == code and x["person"] == MAIN_MOL)
        if ms and abs(manual_qty - qty_1c_main) > 1e-6:
            mismatch.append(dict(code=code, name=name, unit=unit or unit_1c, q1c=qty_1c_main, qman=manual_qty,
                                 addr=", ".join(sorted({m["addr_raw"] or "(пусто)" for m in ms})),
                                 line=", ".join(str(m["line"]) for m in ms[:4])))
    rows.append(dict(acc=b["acc"], code=code, name=name, unit=unit or unit_1c, unit_1c=unit_1c, qty=qty, value=value,
                     doc=b["doc"], number=number, date=date, mol=b["person"], custody=custody, dept=dept,
                     addr=addr, fix=fix, desc=desc, note=note, line=line, probs=probs, quest=quest, sort=sort_why, offbook=False,
                     addr_raw=", ".join(sorted({m["addr_raw"] or "(пусто)" for m in ms})) if ms else None))

mismatch = list({m["code"]: m for m in mismatch}.values())
ready = [r for r in rows if not r["probs"]]
issues = [r for r in rows if r["probs"]]

# Только в ручной ведомости: числовой код, которого нет в 1С; прочие нечисловые коды (кроме неучт/б/у).
only_manual = [m for m in manual if (m["code"] and m["code"] not in codes_1c)
               or (not m["code"] and not m["raw"].lower().startswith(("неуч", "бу", "б/у", "б")) and m["raw"]
                   and m["raw"].lower() != "код")]
special = [m for m in manual if m["raw"].lower().startswith(("неуч", "бу", "б/у")) or m["raw"].lower() == "б"]

# ---------------------------------------------------------------- вне учёта (неучтённое, б/у)
# Автоартикул: «БУ0006» → BU0006 (номер уже есть); прочее — по наименованию и единице: NU-00001 (неучтённое),
# BU-00001 (б/у без номера). Одинаковое наименование — одна позиция, разные места — разные партии.
TRANSLIT = str.maketrans("БУ", "BU")
special_zero = []
auto, counters = {}, collections.Counter()
for m in sorted(special, key=lambda m: m["line"]):
    if not m["qty"] or m["qty"] <= 0:
        special_zero.append(m)
        continue
    raw = m["raw"].upper().replace(" ", "")
    unit = UNIT_MAP.get(m["unit"].lower(), m["unit"] or "шт")
    used = "неучтённое" if raw.startswith("НЕУЧ") else "б/у"
    if re.fullmatch(r"БУ\d+", raw):
        sku = raw.translate(TRANSLIT)
    else:
        prefix = "NU" if used == "неучтённое" else "BU"
        key = (prefix, norm_name(m["name"]), unit)
        if key not in auto:
            counters[prefix] += 1
            auto[key] = f"{prefix}-{counters[prefix]:05d}"
        sku = auto[key]
    good, fix = address(m["addr_raw"])
    note = "; ".join(x for x in (used, m["note"] if m["note"] and m["note"].lower() not in ("б/у", used) else None) if x)
    ready.append(dict(acc=None, code=sku, name=m["name"][:256], unit=unit, unit_1c=m["unit"], qty=m["qty"], value=None,
                      doc=None, number=None, date=None, mol=None, custody=False, dept=None,
                      addr=good or (m["addr_raw"] or "не указано"), fix=fix, desc=None, note=note, line=str(m["line"]),
                      probs=[], quest=[], sort=None if good else "адрес не по схеме", offbook=True))

# ---------------------------------------------------------------- оформление

FONT = "Arial"
HEAD = PatternFill("solid", fgColor="1F5FAD")
HF = Font(name=FONT, bold=True, color="FFFFFF", size=10)
BODY = Font(name=FONT, size=10)
BOLD = Font(name=FONT, size=10, bold=True)
TITLE = Font(name=FONT, size=14, bold=True)
MUTED = Font(name=FONT, size=9, color="6B7280")
INPUT = Font(name=FONT, size=10, color="0000FF")
YELLOW = PatternFill("solid", fgColor="FFF2B3")
PROBLEM = PatternFill("solid", fgColor="FDE2E1")
THIN = Side(style="thin", color="D0D5DD")
GRID = Border(left=THIN, right=THIN, top=THIN, bottom=THIN)


def table(ws, start, headers, widths, data, money=(), qty=(), dates=(), wrap=(), text=(), inputs=()):
    for c, (h, w) in enumerate(zip(headers, widths), start=1):
        cell = ws.cell(start, c, h)
        cell.font, cell.fill, cell.border = HF, HEAD, GRID
        cell.alignment = Alignment(horizontal="center", vertical="center", wrap_text=True)
        ws.column_dimensions[get_column_letter(c)].width = w
    ws.row_dimensions[start].height = 32
    for ri, vals in enumerate(data, start=start + 1):
        for c, v in enumerate(vals, start=1):
            cell = ws.cell(ri, c, v)
            cell.font, cell.border = (INPUT if c in inputs else BODY), GRID
            cell.alignment = Alignment(vertical="top", wrap_text=c in wrap)
            if c in money:
                cell.number_format = "#,##0.00##"
            elif c in qty:
                cell.number_format = "#,##0.###"
            elif c in dates:
                cell.number_format = "DD.MM.YYYY"
            elif c in text:
                cell.number_format = "@"
    ws.freeze_panes = ws.cell(start + 1, 1)
    last = start + max(len(data), 1)
    ws.auto_filter.ref = f"A{start}:{get_column_letter(len(headers))}{last}"
    return last


# ----- файл 1: готово
IMP_H = ["Счёт", "Код 1С", "Наименование (1С)", "Ед. изм.", "Количество", "Стоимость, руб.", "Цена за ед., руб.",
         "Документ поступления", "Дата документа", "Где", "МОЛ в 1С", "Адрес хранения", "Описание (из ведомости склада)",
         "Примечание", "Ед. в 1С", "Строки ведомости склада", "Исправлено / куда"]
IMP_W = [7, 9, 52, 8, 11, 14, 13, 26, 12, 20, 16, 13, 44, 20, 9, 14, 30]
book = openpyxl.Workbook()
info = book.active
info.title = "Сводка"
imp = book.create_sheet("Импорт")
data = []
for n, r in enumerate(sorted(ready, key=lambda r: (r["offbook"], r["custody"], r["mol"] or "", r["acc"] or "", r["addr"] or "", r["code"])), start=2):
    where = "Вне учёта" if r["offbook"] else f"Подотчёт: {r['mol']}" if r["custody"] else "Склад"
    fix = "; ".join(x for x in (r["fix"], f"в «Сортировку»: {r['sort']}" if r["sort"] else None) if x) or None
    data.append([r["acc"], r["code"], r["name"], r["unit"], r["qty"], r["value"],
                 None if r["value"] is None else f"=IF(E{n}=0,0,F{n}/E{n})",
                 r["number"], r["date"], where, r["mol"], r["addr"],
                 r["desc"], r["note"], r["unit_1c"], r["line"], fix])
last = table(imp, 1, IMP_H, IMP_W, data, money=(6, 7), qty=(5,), dates=(9,), wrap=(3, 13), text=(2,), inputs=(5, 6))

info["A1"] = "Начальные остатки — готово к импорту"
info["A1"].font = TITLE
info["A2"] = (f"Количество, цены, партии — из сальдовых ведомостей ТМЦ 1С на {AS_OF}. "
              f"Адреса, описания, примечания, неучтённое и б/у — из ведомости склада на {MANUAL_AS_OF}.")
info["A2"].font = MUTED
lines = [
    ("Партий к импорту", f"=COUNTA(Импорт!B2:B{last})", None),
    ("Стоимость, руб.", f"=SUM(Импорт!F2:F{last})", "#,##0.00"),
    ("  на складе", f'=SUMIF(Импорт!J2:J{last},"Склад",Импорт!F2:F{last})', "#,##0.00"),
    ("  в подотчёте", f'=SUMIF(Импорт!J2:J{last},"Подотчёт*",Импорт!F2:F{last})', "#,##0.00"),
    ("Всего по 1С (все партии), руб.", round(sum(b["value"] or 0 for b in batches), 2), "#,##0.00"),
    ("Готово, % от стоимости 1С", "=B5/B8", "0.0%"),
    ("Вне учёта (неучтённое, б/у), партий", f'=COUNTIF(Импорт!J2:J{last},"Вне учёта")', None),
    ("В «Сортировку» (адрес не определён), партий", sum(1 for r in ready if r["sort"]), None),
]
for k, (a, b, fmt) in enumerate(lines, start=4):
    info[f"A{k}"], info[f"B{k}"] = a, b
    info[f"A{k}"].font, info[f"B{k}"].font = BODY, BODY
    if fmt:
        info[f"B{k}"].number_format = fmt
info["A13"] = "По МОЛ"
info["A13"].font = BOLD
for c, h in zip("ABC", ["МОЛ", "Партий", "Стоимость, руб."]):
    info[f"{c}14"] = h
    info[f"{c}14"].font, info[f"{c}14"].fill = HF, HEAD
mols = [m for m, _ in collections.Counter(b["person"] for b in batches).most_common()]
for k, m in enumerate(mols, start=15):
    info[f"A{k}"] = m
    info[f"B{k}"] = f"=COUNTIF(Импорт!K2:K{last},A{k})"
    info[f"C{k}"] = f"=SUMIF(Импорт!K2:K{last},A{k},Импорт!F2:F{last})"
    info[f"C{k}"].number_format = "#,##0.00"
    for c in "ABC":
        info[f"{c}{k}"].font = BODY
k += 2
info[f"A{k}"] = "Как собрано"
info[f"A{k}"].font = BOLD
for t in [
    "Одна строка — одна партия 1С: код, документ поступления, количество, стоимость. Цена = стоимость / количество.",
    "Код — как в 1С (5 цифр). В ведомости склада коды с нулями впереди (0000032353) — это те же коды.",
    "Наименование — из 1С. Если в ведомости склада оно другое (с оборудованием, производителем) — в «Описание».",
    "«Склад» — партии МОЛ основного склада (Дорофей С.М.), с адресом из ведомости склада (по коду, при нескольких — по цене).",
    "Адреса стеллажей приведены к виду Зона.Стеллаж.Полка.Ящик: A5.1.3 → 1.A5.1.3, 2A.5.1.3 → 2.A5.1.3; «к» (коробка) убрана.",
    "Адрес не по схеме, несколько мест или кода нет в ведомости — партия в «Сортировку», прежнее место — в примечании партии (лист «В Сортировку»).",
    "«Подотчёт» — партии других МОЛ: в приложении числятся за этим сотрудником, адрес не нужен.",
    "«Вне учёта» — неучтённое и б/у из ведомости склада, без цены. Артикул: БУ0006 → BU0006, прочее — NU-00001 / BU-00001 по наименованию.",
    "Единицы приведены к одному виду: пог. м, м.п. → м; Комплект → компл; тыс.шт → тыс. шт; пар → пара.",
    "Количество — по 1С. Где 1С и ведомость склада расходятся — список для пересчёта в файле «уточнить» (импорт не блокирует).",
]:
    k += 1
    info[f"A{k}"] = "• " + t
    info[f"A{k}"].font = BODY
info.column_dimensions["A"].width = 32
info.column_dimensions["B"].width = 16
info.column_dimensions["C"].width = 18
info.sheet_view.showGridLines = False

so = book.create_sheet("В Сортировку")
so["A1"] = "Партии без однозначного места: в приложении — место «Сортировка», прежнее место — в примечании партии. Разложить со страницы «Приёмка»."
so["A1"].font = MUTED
sg = collections.defaultdict(list)
for r in ready:
    if r["sort"]:
        sg[(r["addr"], r["sort"])].append(r)
so_rows = [[a, why, len(g), "; ".join(dict.fromkeys(x["name"] for x in g))[:300]]
           for (a, why), g in sorted(sg.items(), key=lambda kv: (-len(kv[1]), kv[0][0]))]
table(so, 2, ["Прежнее место", "Почему", "Партий", "Позиции"], [26, 20, 8, 90], so_rows, wrap=(4,))
book.save(f"{OUT}/Начальные остатки — готово (1С на {AS_OF}).xlsx")

# ----- файл 2: уточнить
book = openpyxl.Workbook()
info = book.active
info.title = "Как заполнять"
st = book.create_sheet("Партии 1С")
ISS_H = ["№", "Что не так", "Что уточнить", "Ответ персонала", "Счёт", "Код 1С", "Наименование (1С)", "Ед. изм.",
         "Количество (1С)", "Стоимость, руб.", "Документ поступления", "МОЛ", "Адрес в ведомости склада",
         "Описание (из ведомости склада)", "Строки ведомости склада"]
ISS_W = [6, 24, 52, 30, 7, 9, 46, 8, 11, 13, 24, 15, 18, 36, 13]
order = {"нет в ручной ведомости": 3, "адрес не по схеме": 0, "несколько адресов": 1, "количество 1С ≠ ручной ведомости": 2}
iss_sorted = sorted(issues, key=lambda r: (order.get(r["probs"][0], 9), r["addr_raw"] or "", r["code"]))
data = [[n, "; ".join(dict.fromkeys(r["probs"])), "\n".join(dict.fromkeys(r["quest"])), None, r["acc"], r["code"], r["name"],
         r["unit"], r["qty"], r["value"], r["doc"], r["mol"], r["addr_raw"], r["desc"], r["line"]]
        for n, r in enumerate(iss_sorted, start=1)]
last_i = table(st, 1, ISS_H, ISS_W, data, money=(10,), qty=(9,), wrap=(2, 3, 4, 7, 14), text=(6,))
for row in range(2, last_i + 1):
    st.cell(row, 4).fill = YELLOW
    st.cell(row, 2).fill = PROBLEM

# Сверка количества: 1С главная, но расхождения стоит пересчитать (недовнесённые выдачи / приходы)
mm = book.create_sheet("Сверка количества")
mm["A1"] = (f"Код есть и в 1С (МОЛ {MAIN_MOL}, на {AS_OF}), и в ведомости склада (на {MANUAL_AS_OF}), количество разное. "
            "В приложение идёт количество 1С. Пересчитайте фактически — расхождение потом закроем документом.")
mm["A1"].font = MUTED
mm_rows = [[n, m["code"], m["name"], m["unit"], m["q1c"], m["qman"], m["q1c"] - m["qman"], m["addr"], m["line"], None]
           for n, m in enumerate(sorted(mismatch, key=lambda m: m["addr"]), start=1)]
last_m = table(mm, 2, ["№", "Код 1С", "Наименование (1С)", "Ед. изм.", "По 1С", "По ведомости", "1С − ведомость", "Адрес в ведомости",
                       "Строки ведомости", "Фактически"], [6, 9, 52, 8, 10, 12, 13, 22, 13, 12], mm_rows, qty=(5, 6, 7), wrap=(3,), text=(2,))
for row in range(3, last_m + 1):
    mm.cell(row, 10).fill = YELLOW
groups = {}

om = book.create_sheet("Нет в 1С")
om["A1"] = ("Позиции ведомости склада, которых нет в 1С (на " + AS_OF + "): числовой код не найден в 1С или код не числовой. "
            "Неучтённое и б/у — на отдельном листе.")
om["A1"].font = MUTED
om_rows = [[n, m["raw"] or None, m["name"], m["unit"] or None, m["qty"], m["price"], m["addr_raw"] or None, m["note"] or None, m["line"], None]
           for n, m in enumerate(sorted(only_manual, key=lambda m: m["line"]), start=1)]
last_o = table(om, 2, ["№", "Код в ведомости", "Наименование", "Ед. изм.", "Остаток (сентябрь)", "Цена", "Адрес", "Примечание",
                       "Строка ведомости", "Что это / ответ"], [6, 13, 52, 8, 11, 11, 14, 18, 10, 34], om_rows,
               money=(6,), qty=(5,), wrap=(3, 10))
for row in range(3, last_o + 1):
    om.cell(row, 10).fill = YELLOW

sp = book.create_sheet("Неучтённое — остаток 0")
sp["A1"] = "Неучтённое и б/у из ведомости склада с пустым или нулевым остатком — в импорт не попали. Если товар есть — укажите количество."
sp["A1"].font = MUTED
sp_rows = [[n, m["raw"], m["name"], m["unit"] or None, m["qty"], m["addr_raw"] or None, m["note"] or None, m["line"], None]
           for n, m in enumerate(special_zero, start=1)]
last_s = table(sp, 2, ["№", "Код в ведомости", "Наименование", "Ед. изм.", "Остаток", "Адрес", "Примечание", "Строка ведомости", "Фактически"],
               [6, 12, 56, 8, 9, 16, 18, 10, 12], sp_rows, qty=(5,), wrap=(3,))
for row in range(3, last_s + 1):
    sp.cell(row, 9).fill = YELLOW

reasons = collections.Counter(p for r in issues for p in dict.fromkeys(r["probs"]))
info["A1"] = "Начальные остатки — уточнить у персонала"
info["A1"].font = TITLE
info["A2"] = f"Партии 1С (на {AS_OF}), которые нельзя перенести без уточнения, и позиции ведомости склада, которых нет в 1С."
info["A2"].font = MUTED
info["A4"] = "Как заполнять"
info["A4"].font = BOLD
howto = [
    "Количество и цена — из 1С. Адреса, которые не определились, импорт не блокируют: такие партии идут в «Сортировку».",
    "Лист «Партии 1С»: партии, которые нельзя перенести без решения. Вопрос — в «Что уточнить», ответ — в жёлтую колонку.",
    "Лист «Сверка количества»: 1С и ведомость склада расходятся. Импорт не ждёт — пересчитайте, потом закроем документом.",
    "Лист «Нет в 1С»: коды ведомости склада, которых нет в 1С, — оприходовать в 1С, перенести как «вне учёта» или списано?",
    "Лист «Неучтённое — остаток 0»: строки без остатка — есть ли товар фактически?",
]
for k, t in enumerate(howto, start=5):
    info[f"A{k}"] = t if t.startswith(("⚑", "  ")) else "• " + t
    info[f"A{k}"].font = BOLD if t.startswith("⚑") else BODY
k = 5 + len(howto) + 1
info[f"A{k}"] = "Пример ответа"
info[f"A{k}"].font = BOLD
info[f"A{k + 1}"], info[f"B{k + 1}"] = "Что уточнить:", "В 1С числится за подразделением «Цех розлива». Переносить в наш склад?"
info[f"A{k + 2}"], info[f"B{k + 2}"] = "Ответ персонала:", "Нет, это не наше"
info[f"B{k + 2}"].fill = YELLOW
for c in (f"A{k + 1}", f"A{k + 2}", f"B{k + 1}", f"B{k + 2}"):
    info[c].font = BODY
k += 4
for c, h in zip("ABC", ["Причина (лист «Партии 1С»)", "Партий", "Отвечено"]):
    info[f"{c}{k}"] = h
    info[f"{c}{k}"].font, info[f"{c}{k}"].fill = HF, HEAD
for reason, _ in reasons.most_common():
    k += 1
    info[f"A{k}"] = reason
    info[f"B{k}"] = f'=COUNTIF(\'Партии 1С\'!B2:B{last_i},"*"&A{k}&"*")'
    info[f"C{k}"] = f'=COUNTIFS(\'Партии 1С\'!B2:B{last_i},"*"&A{k}&"*",\'Партии 1С\'!D2:D{last_i},"<>")'
    for c in "ABC":
        info[f"{c}{k}"].font = BODY
k += 1
info[f"A{k}"], info[f"B{k}"], info[f"C{k}"] = "Всего партий", f"=COUNTA('Партии 1С'!A2:A{last_i})", f"=COUNTIF('Партии 1С'!D2:D{last_i},\"<>\")"
for c in "ABC":
    info[f"{c}{k}"].font = BOLD
info[f"A{k + 2}"], info[f"B{k + 2}"] = "Нет в 1С (строк ведомости склада)", f"=COUNTA('Нет в 1С'!A3:A{last_o})"
info[f"A{k + 3}"], info[f"B{k + 3}"] = "Сверка количества (кодов)", len(mismatch)
for c in (f"A{k + 2}", f"A{k + 3}", f"B{k + 2}", f"B{k + 3}"):
    info[c].font = BODY
info.column_dimensions["A"].width = 36
info.column_dimensions["B"].width = 14
info.column_dimensions["C"].width = 12
info.sheet_view.showGridLines = False
book.save(f"{OUT}/Начальные остатки — уточнить у персонала (1С на {AS_OF}).xlsx")

print("партий 1С", len(rows), "готово всего", len(ready), "из них вне учёта", sum(r["offbook"] for r in ready),
      "в сортировку", sum(1 for r in ready if r["sort"]), "уточнить", len(issues), "нет в 1С", len(only_manual),
      "неучт остаток 0", len(special_zero), "сверка кол-ва", len(mismatch))
print(reasons.most_common())
print(collections.Counter(r["sort"] for r in ready if r["sort"]))
print("готово, руб", round(sum(r["value"] or 0 for r in ready), 2), "из", round(sum(r["value"] or 0 for r in rows), 2))
