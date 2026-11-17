"""Сальдовые ведомости ТМЦ из 1С: уровни группировки 0 счёт, 1 подразделение, 2 МОЛ, 3 позиция, 4 партия (документ).

Запуск: python3 -I parse1c.py <data.json> <сальдовая1.xlsx> [<сальдовая2.xlsx> ...]
Файлы .xls сначала пересохранить в .xlsx (группировка строк должна сохраниться).
"""
import openpyxl, json, sys
files=sys.argv[2:]
batches=[]; items=[]; odd=[]
for f in files:
    ws=openpyxl.load_workbook(f, data_only=True).worksheets[0]
    acc=dept=person=None; item=None; mol_title=None
    for i,row in enumerate(ws.iter_rows(values_only=True), start=1):
        vals=[v for v in row if v not in (None,'')]
        if not vals: continue
        lvl=ws.row_dimensions[i].outline_level
        if i<10: continue
        t=str(vals[0]).strip()
        if t.startswith('МОЛ:'): mol_title=t; continue
        if lvl==0:
            if t.startswith('Итого'): continue
            acc=t; continue
        if lvl==1: dept=t; continue
        if lvl==2: person=t; continue
        if lvl==3:
            item=dict(code=str(vals[0]), name=str(vals[1]).strip(), unit=str(vals[2]).strip(), value=vals[-1],
                      acc=acc, dept=dept, person=person, file=f.split('/')[-1][:8])
            items.append(item); continue
        if lvl==4:
            r=list(row)
            # колонки: A код, B документ, ... единица, цена, количество, стоимость — берём по позициям непустых справа
            nums=[v for v in r if isinstance(v,(int,float))][1:]  # без кода
            texts=[v for v in r[1:] if isinstance(v,str) and v.strip()]
            if len(nums)>=3: price,qty,value=nums[-3],nums[-2],nums[-1]
            elif len(nums)==2: qty,value=nums; price=round(value/qty,4) if qty else None   # цена не напечатана
            else: qty=nums[0]; price=value=None; odd.append((f[-20:], i, lvl, vals[:6]))
            batches.append(dict(code=str(vals[0]), doc=texts[0].strip() if texts else '', unit=texts[1].strip() if len(texts)>1 else None,
                                price=price, qty=qty, value=value, short=len(nums)<3,
                                acc=acc, dept=dept, person=person, name=item['name'] if item else None, file=f.split('/')[-1][:8], row=i))
            continue
        odd.append((f[-20:], i, lvl, vals[:5]))
    for b in batches:
        if b['file']==f.split('/')[-1][:8] and not b.get('mol'): b['mol']=mol_title
json.dump(dict(batches=batches, items=items, odd=odd), open(sys.argv[1],'w'), ensure_ascii=False, default=str)
print("items",len(items),"batches",len(batches),"odd",len(odd), odd[:5])
