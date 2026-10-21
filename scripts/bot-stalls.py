import re, sys
lines = open(sys.argv[1], encoding='utf-8', errors='ignore').read().splitlines()
pat = re.compile(r'\[\s*(\d+)s\] (\S+) \(бот\) @\(([-\d,]+);([-\d,]+)\) (.*?): (.*?) · HP.*?E→(\w+).*?· построек \d+ · ?(.*)$')
prev = {}
runs = []   # (name, start, end, doing, info)
cur = {}
for l in lines:
    m = pat.search(l)
    if not m: continue
    t = int(m.group(1)); name = m.group(2)
    x = float(m.group(3).replace(',', '.')); y = float(m.group(4).replace(',', '.'))
    doing = m.group(6); e = m.group(7); info = m.group(8)[:90]
    if name in prev:
        pt, px, py, pd = prev[name]
        still = ((x - px) ** 2 + (y - py) ** 2) ** 0.5 < 1.5 and pd == doing
        if still:
            if name not in cur: cur[name] = [pt, t, doing, e, info]
            else: cur[name][1] = t; cur[name][3] = e; cur[name][4] = info
        elif name in cur:
            runs.append((name, *cur.pop(name)))
    prev[name] = (t, x, y, doing)
for n, c in cur.items(): runs.append((n, *c))
for r in sorted(runs, key=lambda r: -(r[2] - r[1])):
    if r[2] - r[1] >= 60:
        print(f"{r[2]-r[1]:5d}s  [{r[1]}-{r[2]}] {r[0]}: {r[3]} | E→{r[4]} | {r[5]}")
