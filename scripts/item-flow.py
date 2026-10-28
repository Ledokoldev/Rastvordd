"""Поток предметов по летописи прогона ботов (строки "items", Economy.ItemIn/ItemOut + что лежит у ботов).

  python scripts/item-flow.py прогон.jsonl [--md out.md]

Прогон: …_console.exe --headless --quit-after 20 --path game -- --simtest=36000 --bots=20 --chronicle=прогон.jsonl
Для каждого предмета: приход (по источникам), расход (по статьям), остаток у ботов в конце, приход на бота в час.
Оценка: «дефицит» — тратится почти всё, что пришло, и почти ничего не лежит; «избыток» — копится, тратится мало.
"""
import json, sys, collections
sys.stdout.reconfigure(encoding='utf-8')


def load(path):
    last, bots, T = None, set(), 1
    for line in open(path, encoding='utf-8'):
        try:
            d = json.loads(line)
        except ValueError:
            continue
        if d.get('type') == 'items':
            last, T = d, max(T, d.get('t', 1))
        elif d.get('type') == 'pos':
            for a in d['actors']:
                if a.get('bot'): bots.add(a['name'])
    return last, max(1, len(bots)), T


def split(m):
    r = collections.defaultdict(dict)
    for k, v in m.items():
        src, _, iid = k.partition('|')
        r[iid][src] = r[iid].get(src, 0) + v
    return r


def main():
    path = sys.argv[1]
    md = sys.argv[sys.argv.index('--md') + 1] if '--md' in sys.argv else None
    d, nb, T = load(path)
    if not d: sys.exit('нет строк items в ' + path)
    names = {}
    try:
        names = {k: v['name'] for k, v in json.load(open('C:/Temp/claude/pd2.json', encoding='utf-8'))['items'].items()}
    except Exception:
        pass
    ins, outs, held = split(d['in']), split(d['out']), d['held']
    ids = sorted(set(ins) | set(outs) | set(held), key=lambda i: -sum(ins.get(i, {}).values()))
    hours = T / 3600
    rows = []
    for i in ids:
        tin, tout, h = sum(v for k, v in ins.get(i, {}).items() if k != 'труп'), sum(outs.get(i, {}).values()), held.get(i, 0)
        if tin < 1 and tout < 1: continue
        use = tout / tin if tin else 9
        verdict = 'дефицит' if tin and use > 0.8 and h < tin * 0.15 else 'избыток' if tin and use < 0.25 and h > tin * 0.4 else 'норма'
        src = ', '.join(f'{k} {v:.0f}' for k, v in sorted(ins.get(i, {}).items(), key=lambda kv: -kv[1])[:4])
        snk = ', '.join(f'{k} {v:.0f}' for k, v in sorted(outs.get(i, {}).items(), key=lambda kv: -kv[1])[:4])
        rows.append((i, names.get(i, i), tin, tout, h, tin / nb / hours, verdict, src, snk))
    out = [f'Прогон: {path} · ботов {nb} · {hours:.1f} ч мира', '',
           '| Предмет | Приход | Расход | Лежит | на бота/ч | Оценка | Откуда | Куда |', '|---|---|---|---|---|---|---|---|']
    for i, n, tin, tout, h, ph, v, src, snk in rows:
        out.append(f'| {n} | {tin:.0f} | {tout:.0f} | {h:.0f} | {ph:.1f} | {v} | {src} | {snk} |')
    text = '\n'.join(out)
    print(text)
    if md: open(md, 'w', encoding='utf-8').write(text + '\n')


main()
