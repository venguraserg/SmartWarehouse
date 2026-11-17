"""Приведение адресов со стеллажами к виду Зона.Стеллаж.Полка.Ящик (зона = этаж 1/2)."""
import re
LOOK = str.maketrans("АВСЕНКМОРТХавсенмортх", "ABCEHKMOPTXABCEHMOPTX")  # «к» строчную не трогаем — это отдельный признак
RACK = re.compile(r"[ABCАВС]\d|\d[ABCАВС]|^[ABCАВС]\.")

def is_rack(addr):
    return bool(RACK.search(addr))

def normalize(addr):
    """→ (новый адрес или None, статус, пояснение). Статусы: ok, check (проверить), ask (уточнить)."""
    a = re.sub(r"\s+", " ", addr).strip()
    notes = []
    # несколько мест / фамилия / текст вокруг
    if re.search(r"[,;]", a):
        parts = [p.strip() for p in re.split(r"[,;]", a)]
        if parts[0].lower() == "полка" and len(parts) == 2:
            a = parts[1]; notes.append("убрано слово «полка»")
        else:
            conv = [normalize(p)[0] or p for p in parts]
            return None, "ask", "несколько мест или фамилия: " + " | ".join(conv) + " — оставить одно место, количество разделить"
    if re.search(r"[а-яё]{3,}", a.lower().replace("ящ", "")) and not re.fullmatch(r".*\.(ящ|чсин)", a.lower()):
        if re.search(r"стел+аж$", a.lower()):
            a = re.sub(r"\s*стел+аж$", "", a, flags=re.I); notes.append("убрано слово «стелаж»")
        else:
            return None, "ask", "текстовое место рядом со стеллажом — завести отдельное место или выбрать полку"
    a = a.replace(" ", "")
    head, _, rest0 = a.partition(".")
    fixed = head.translate(LOOK) + ("." + rest0 if _ else "")
    if fixed != a: notes.append("кириллица → латиница")
    a = fixed
    if a.endswith("."): a = a.rstrip("."); notes.append("лишняя точка")
    if re.search(r"\d[кК]$", a): a = a[:-1] + "." + a[-1]; notes.append("добавлена точка перед «к»")
    m = re.fullmatch(r"(\d)?([ABC])(\d*)((?:\.[^.]+)*)", a)
    if not m:
        return None, "ask", "не разобрать"
    floor, letter, num, rest = m.group(1), m.group(2), m.group(3), m.group(4)
    tail = [p for p in rest.split(".") if p]
    status = "ok"
    if floor:            # 2A.5.1.3: номер стеллажа — следующая часть
        if num:          # 1C5… — номер сразу за буквой
            pass
        elif tail and tail[0].isdigit():
            num = tail.pop(0)
        else:
            return None, "ask", "этаж и ряд есть, номера стеллажа нет"
        if floor not in ("1", "2"):
            return None, "ask", f"этаж {floor}?"
        if floor == "1":
            notes.append("«1C» — этаж 1 указан явно (подтверждено)")
    else:
        floor = "1"
        if not num:      # C.5.1.6 → номера стеллажа нет, вероятно C5
            if tail and tail[0].isdigit():
                num = tail.pop(0); notes.append(f"после буквы точка — стеллаж {letter}{num} (подтверждено)")
            else:
                return None, "ask", "нет номера стеллажа"
    if tail and tail[-1] in ("к", "К", "K"):
        tail.pop(); notes.append("убрана «к» (лежит в коробке)")
    elif tail and tail[-1].lower() in ("ящ", "чсин"):
        notes.append(f"убран «{tail.pop()}» (ящик без номера — на полке)")
    if len(tail) > 2:
        return None, "ask", f"больше уровней, чем Полка.Ящик: {'.'.join(tail)}"
    for i, p in enumerate(tail):
        if p in ("к", "К", "K"):
            status = "ask"; notes.append("«к» — что это? нужен латинский код ящика")
        elif p.lower() in ("ящ", "чсин"):
            status = "ask"; notes.append(f"«{p}» — нужен номер/латинский код ящика")
        elif not p.isascii() or not p.isalnum():
            status = "ask"; notes.append(f"«{p}» — недопустимые символы")
    new = ".".join([floor, letter + num] + tail)
    return new, status, "; ".join(notes)

if __name__ == "__main__":
    for t in ["A5.1.3", "2A.5.1.3", "2A.2.1", "2C.4", "B4", "1C.5.2", "C.5.1.6", "A.2.2", "С2.3", "2C.5.3к", "C1.3.к",
              "полка,2C.1.1. 1", "B1.1.1.", "2C.5 стелаж", "За стел.B5", "37.2,38.2.1", "2A.5.1.2, Забелич", "C2.1.Чсин", "B1.0.ящ", "B5.0.К"]:
        print(f"{t!r:24} → {normalize(t)}")
