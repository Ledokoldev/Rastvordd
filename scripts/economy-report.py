"""Темп экономики по летописи прогона ботов (D49, docs/design/15-economy.md).

  python scripts/economy-report.py прогон.jsonl [ещё.jsonl ...]

Прогон: …_console.exe --headless --quit-after 20 --path game -- --simtest=36000 --bots=12 --chronicle=прогон.jsonl
Для каждого прогона: время верстаков 1–3 и первого ствола тира 1/2/3/элиты — у лучшего бота и медиана по ботам
(кто не дошёл — «—», в медиане считается как «позже конца»); доход лома по источникам (строки "econ", Economy.Ledger):
сырой лом, переработчик (ценный лом / детали), лом «в переработке», взятый из ящиков.
"""
import json, re, sys, statistics, collections
sys.stdout.reconfigure(encoding='utf-8')

KEYS = [('верстак 1', r'поставил: Верстак$'), ('верстак 2', r'поставил: Верстак 2$'), ('верстак 3', r'поставил: Верстак 3$'),
        ('ствол тир 1', r'первый ствол \(тир 1\)'), ('ствол тир 2', r'первый ствол \(тир 2\)'), ('ствол тир 3', r'первый ствол \(тир 3\)'),
        ('элита', r'первый ствол \(элита\)'),
        ('крафт тир 1', r'скрафтил: (Наган|Самоделка)$'), ('крафт тир 2', r'скрафтил: (Грач|ППС-43|СВТ-40)$'),
        ('крафт тир 3', r'скрафтил: (АКМ|АН-94.*|ППШ|СВД)$')]


def hm(t):
    return '—' if t is None else f'{int(t // 3600)}:{int(t % 3600 // 60):02d}'


def report(path):
    first = collections.defaultdict(dict)   # бот -> ключ -> t
    bots, T, ledger, Tl = set(), 0, {}, 1
    for line in open(path, encoding='utf-8'):
        try:
            d = json.loads(line)
        except ValueError:
            continue   # прогон ещё идёт — последняя строка недописана
        t = d.get('t', 0); T = max(T, t)
        if d['type'] == 'pos':
            for a in d['actors']:
                if a.get('bot'): bots.add(a['name'])
        elif d['type'] == 'econ':
            ledger, Tl = d['ledger'], t
        elif d['type'] == 'milestone':
            m = re.match(r'\[\d+:\d+\] (.+?) — (.+)$', d['text'])
            if not m: continue
            who, what = m.groups()
            if who.startswith('МИР'): continue
            for k, rx in KEYS:
                if re.search(rx, what) and k not in first[who]: first[who][k] = t
    hours = T / 3600
    print(f'== {path}: {len(bots)} ботов, {hours:.1f} ч')
    print(f'{"веха":12} {"лучший":>7} {"медиана":>8} {"дошли":>6}')
    for k, _ in KEYS:
        ts = sorted(first[b].get(k) for b in bots if first[b].get(k) is not None)
        allv = [first[b].get(k, float('inf')) for b in bots]
        med = statistics.median(allv) if allv else float('inf')
        print(f'{k:12} {hm(ts[0] if ts else None):>7} {hm(med if med != float("inf") else None):>8} {len(ts):>3}/{len(bots)}')
    if ledger:
        nb = max(1, len(bots)); hours = max(Tl, 1) / 3600   # учёт пишется раз в 10 мин — делим на его время
        groups = collections.defaultdict(float)
        for k, v in ledger.items():
            g = k.split(':')[0]
            groups[g] += v
        print(f'доход лома за прогон (на бот-час, всего):')
        for k, v in sorted(ledger.items(), key=lambda x: -x[1]):
            print(f'   {k:32} {v / nb / hours:7.1f}/бот-ч  {v:8.0f}')
        print('   итого по видам: ' + ', '.join(f'{g} {v / nb / hours:.1f}/бот-ч' for g, v in sorted(groups.items(), key=lambda x: -x[1])))
    print()


for p in sys.argv[1:]:
    report(p)
