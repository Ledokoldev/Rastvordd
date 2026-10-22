"""Метрики «как игроки Раста» (bot-brain.md, раздел 10): python scripts/bot-rust-metrics.py прогон.jsonl прогон.log [метка]
Прогон: …_console.exe --headless --quit-after 20 --path game -- --simtest=28800 --bots=12 --chronicle=прогон.jsonl > прогон.log"""
import json, re, sys, collections, statistics
sys.stdout.reconfigure(encoding='utf-8')
jpath, lpath = sys.argv[1], sys.argv[2]
label = sys.argv[3] if len(sys.argv) > 3 else jpath

deaths = []          # (t, victim, killer, place)
goals = collections.Counter()
bots = set()
T = 0
for line in open(jpath, encoding='utf-8'):
    d = json.loads(line)
    t = d.get('t', 0); T = max(T, t)
    if d.get('type') == 'death' and d.get('victim') and '(бот)' in d['victim']:
        deaths.append((t, d['victim'], d.get('killer'), d.get('place')))
    elif d.get('type') == 'pos':
        for a in d['actors']:
            if not a.get('bot'): continue
            bots.add(a['name'])
            if not a['dead']: goals[a.get('goal') or '-'] += 1
nb = max(1, len(bots)); hours = T / 3600; bh = nb * hours
print(f'== {label}: {nb} ботов, {hours:.2f} ч, {bh:.0f} бот-часов')
byb = sum(1 for x in deaths if x[2] and '(бот)' in x[2])
print(f'смертей {len(deaths)} ({len(deaths)/bh:.2f} на бот-час), от ботов {byb} ({100*byb/max(1,len(deaths)):.0f} %), вне РТ {sum(1 for x in deaths if not x[3])}')
per = collections.defaultdict(list)
for t, v, k, p in deaths: per[v].append(t)
early, late = [], []
for v, ts in per.items():
    prev = 0
    for t in sorted(ts):
        (early if prev < 7200 else late).append((t - prev) / 60); prev = t
    # «живёт до конца» — тоже интервал (цензурированный, но честнее для медианы)
    (early if prev < 7200 else late).append((T - prev) / 60)
for b in bots:
    if b not in per: (early).append(T / 60)
med = lambda xs: statistics.median(xs) if xs else float('nan')
print(f'медиана между смертями: первые 2 ч {med(early):.1f} мин, после 2 ч {med(late):.1f} мин (интервалов {len(early)}/{len(late)})')
env = sum(1 for x in deaths if not x[2])
print(f'   из них от среды (холод/радиация, убийцы нет) {env}, от охраны РТ {len(deaths)-byb-env}; убийств ботом на бот-час {byb/bh:.2f}')
def medians(keep):
    per2 = collections.defaultdict(list)
    for t, v, k, p in deaths:
        if keep(k): per2[v].append(t)
    e, l = [], []
    for b in bots:
        prev = 0
        for t in sorted(per2.get(b, [])):
            (e if prev < 7200 else l).append((t - prev) / 60); prev = t
        (e if prev < 7200 else l).append((T - prev) / 60)
    return med(e), med(l)
m1 = medians(lambda k: bool(k)); m2 = medians(lambda k: bool(k) and '(бот)' in k)
def split_mean(keep):
    e = sum(1 for t, v, k, p in deaths if keep(k) and t < 7200); l = sum(1 for t, v, k, p in deaths if keep(k) and t >= 7200)
    return nb * min(T, 7200) / 60 / max(1, e), nb * max(0, T - 7200) / 60 / max(1, l)
a1 = split_mean(lambda k: True); a2 = split_mean(lambda k: bool(k)); a3 = split_mean(lambda k: bool(k) and '(бот)' in k)
print(f'   среднее время жизни (бот-минуты / смерти), до 2 ч / после: все {a1[0]:.1f} / {a1[1]:.1f}; без среды {a2[0]:.1f} / {a2[1]:.1f}; только от ботов {a3[0]:.1f} / {a3[1]:.1f} мин')
vc = collections.Counter(v for t, v, k, p in deaths if k and '(бот)' in k)
print('   жертвы ботов: ' + ', '.join(f'{v.split()[0]} {n}' for v, n in vc.most_common()))
print(f'   медиана без смертей от среды: {m1[0]:.1f} / {m1[1]:.1f} мин; только от ботов: {m2[0]:.1f} / {m2[1]:.1f} мин')
weap = collections.Counter((x[2] and '(бот)' in x[2]) and 'бот' or 'npc/мир' for x in deaths)

txt = open(lpath, encoding='utf-8', errors='ignore').read().splitlines()
reasons = collections.Counter(); deposits = 0; th = collections.Counter()
box = collections.defaultdict(list); ms = []
for ln in txt:
    if ln.startswith('МЫСЛЬ'):
        m = re.search(r'→ нападаю: (.*)', ln)
        if m:
            r = m.group(1)
            r = re.sub(r'\(.*', '', r); r = re.sub(r'≈\d+', '', r); r = re.sub(r'РТ «[^»]*(»[^»]*)?»', 'РТ', r)
            r = re.sub(r'\b[А-ЯЁ][а-яё]+ \(бот\)', 'X', r); r = re.sub(r'\d+', 'N', r)
            reasons[r.strip(' :—-')[:60]] += 1
        if 'сложил в ящик' in ln: deposits += 1
        for k in ['проснулся в своём спальнике', 'проснулся на пляже', 'спальник постелен', 'вендетта на 25 мин', 'не мщу', 'месть: иду за',
                  'пора домой сложить лут', 'бросаю погоню', 'гоняюсь слишком долго', 'перехват:', 'я голый — обхожу', 'стелет спальник']:
            if k in ln: th[k] += 1
        if "] отомстил " in ln: th["отомстил (событие)"] += 1
    m = re.match(r'\[\s*(\d+)s\] (.+?) @.*?ящик (\d+)', ln)
    if m: box[int(m.group(1)) // 3600].append(int(m.group(3)))
    if ln.startswith('ВЕХА'): ms.append(ln)
tot = sum(reasons.values())
print(f'нападений (решений «нападаю») {tot}:')
for r, n in reasons.most_common(12): print(f'   {n:5d}  {100*n/tot:4.1f} %  {r}')
print(f'сдач «сложил в ящик» {deposits} ({deposits/bh:.2f} на бот-час)')
print('средний ящик по часам: ' + ', '.join(f'{h}ч {sum(v)/len(v):.0f}' for h, v in sorted(box.items())))
print('мысли:', dict(th))
def first(pat):
    for ln in ms:
        m = re.match(r'ВЕХА \[(\d+):(\d+)\] (.*)', ln)
        if m and re.search(pat, m.group(3)): return f'{int(m.group(1))}:{m.group(2)} ({m.group(3)[:50]})'
    return '—'
for name, pat in [('верстак 1', r'поставил: Верстак( 1|$|\b)(?! ?[23])'), ('верстак 2', r'поставил: Верстак 2|Верстак II'), ('верстак 3', r'поставил: Верстак 3|Верстак III'),
                  ('ствол тир 1', r'первый ствол \(тир 1\)'), ('ствол тир 2', r'первый ствол \(тир 2\)'), ('ствол тир 3', r'первый ствол \(тир 3\)'), ('спальник', r'постелил спальник')]:
    print(f'   первый {name}: {first(pat)}')
cnt = lambda pat: sum(1 for ln in ms if re.search(pat, ln))
print(f'   верстаков 2 поставлено: {cnt(r"поставил: Верстак 2")}, 3: {cnt(r"поставил: Верстак 3")}, стволов тир1/2/3: {cnt(r"тир 1")}/{cnt(r"тир 2")}/{cnt(r"тир 3")}')
alive = sum(goals.values())
print('время по целям (живые боты): ' + ', '.join(f'{g} {100*n/alive:.1f} %' for g, n in goals.most_common(16)))
