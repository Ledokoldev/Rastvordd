"""
Разбор летописи вайпа (game/src/Sim/Chronicle.cs): карта маршрутов, посещения РТ, смерти, проблемы мозга ботов.

  python scripts/wipe-report.py путь/к/wipe_….jsonl [выход.html]

Летописи игры: %APPDATA%/Godot/app_userdata/Rastvordd/chronicle/*.jsonl (обычная игра — wipe_*, песочница — sandbox_*).
Пишет HTML-отчёт рядом с файлом и короткую сводку в консоль.

Ищет проблемы:
- «тусовка» — двое и больше живых стоят в 12 м друг от друга дольше 2 мин без смертей и стрельбы рядом;
- «стоит» — бот сдвинулся меньше чем на 3 м за 2 мин (кроме стройки, фарма, ожидания крейта);
- «беготня» — больше 30 % времени за 10 мин в целях бегство/охота/бой без смертей;
- «без РТ» — бот ни разу не был на РТ за весь вайп (если вайп дольше 30 мин);
- «нет прогресса» — 10 мин без вех у бота.
"""
import json, math, sys, os, html, collections
sys.stdout.reconfigure(encoding='utf-8')

STILL_OK = ('строит', 'рубит', 'бьёт', 'копает', 'ждёт', 'крейт', 'плавит', 'стелет', 'ставит', 'кормит', 'улучшает', 'чинит', 'изучает', 'крафт', 'охраняет', 'вскрывает', 'держи', 'печ', 'замок')
PALETTE = ['#e6194b', '#3cb44b', '#ffe119', '#4363d8', '#f58231', '#911eb4', '#46f0f0', '#f032e6', '#bcf60c', '#fabebe',
           '#008080', '#e6beff', '#9a6324', '#fffac8', '#800000', '#aaffc3', '#808000', '#ffd8b1', '#000075', '#a9a9a9']


def mmss(t):
    return f'{int(t // 60):02d}:{int(t % 60):02d}'


def load(path):
    head, pos, ev = None, [], []
    with open(path, encoding='utf-8') as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                r = json.loads(line)
            except Exception:
                continue
            if r['type'] == 'header':
                head = r
            elif r['type'] == 'pos':
                pos.append(r)
            else:
                ev.append(r)
    return head, pos, ev


def analyze(head, pos, ev):
    tracks = collections.defaultdict(list)     # имя → [(t, x, y, dead, goal, doing, place, hp, scrap)]
    for p in pos:
        for a in p['actors']:
            tracks[a['name']].append((p['t'], a['x'], a['y'], a['dead'], a.get('goal') or '', a.get('doing') or '', a.get('place'), a['hp'], a.get('scrap', 0)))
    # персонаж, который за весь вайп не сдвинулся (игрок в отладочном прогоне без ввода), — не участник
    for n in [n for n, tr in tracks.items() if tr and max(math.hypot(s[1] - tr[0][1], s[2] - tr[0][2]) for s in tr) < 5]:
        del tracks[n]
    deaths = [e for e in ev if e['type'] == 'death']
    milestones = [e for e in ev if e['type'] == 'milestone']
    shots_near = [(e['t'], e['x'], e['y']) for e in ev if e['type'] in ('death', 'explosion')]
    issues = []

    # тусовки: пары ближе 12 м, длительность непрерывного соседства
    names = list(tracks)
    by_t = collections.defaultdict(dict)
    for n, tr in tracks.items():
        for s in tr:
            by_t[s[0]][n] = s
    times = sorted(by_t)
    near_since = {}
    for t in times:
        snap = by_t[t]
        alive = [n for n in snap if not snap[n][3]]
        pairs_now = set()
        for i in range(len(alive)):
            for j in range(i + 1, len(alive)):
                a, b = snap[alive[i]], snap[alive[j]]
                if math.hypot(a[1] - b[1], a[2] - b[2]) < 12:
                    pairs_now.add((alive[i], alive[j]))
        for pr in list(near_since):
            if pr not in pairs_now:
                t0 = near_since.pop(pr)
                if t - t0 >= 120:
                    a = by_t[t0][pr[0]]
                    fought = any(t0 - 5 <= st <= t and math.hypot(sx - a[1], sy - a[2]) < 40 for st, sx, sy in shots_near)
                    if not fought:
                        issues.append(('тусовка', t0, t, f'{pr[0]} и {pr[1]} рядом {int(t - t0)} с без боя', a[1], a[2], a[6]))
        for pr in pairs_now:
            near_since.setdefault(pr, t)

    # стоят на месте, беготня, без РТ, нет прогресса
    stats = {}
    for n, tr in tracks.items():
        goals = collections.Counter(s[4] for s in tr if not s[3])
        places = collections.Counter(s[6] for s in tr if s[6])
        dist = sum(math.hypot(tr[i][1] - tr[i - 1][1], tr[i][2] - tr[i - 1][2]) for i in range(1, len(tr)) if not tr[i][3] and not tr[i - 1][3] and math.hypot(tr[i][1] - tr[i - 1][1], tr[i][2] - tr[i - 1][2]) < 120)
        stats[n] = dict(goals=goals, places=places, dist=dist, samples=len(tr),
                        deaths=sum(1 for d in deaths if d.get('victim') == n), kills=sum(1 for d in deaths if d.get('killer') == n),
                        last_scrap=tr[-1][8] if tr else 0)
        win = int(120 / 5)
        i = 0
        while i + win < len(tr):
            a, b = tr[i], tr[i + win]
            if not a[3] and not b[3] and math.hypot(a[1] - b[1], a[2] - b[2]) < 3 and not any(k in (a[5] or '') for k in STILL_OK):
                issues.append(('стоит', a[0], b[0], f'{n}: «{a[5]}» на одном месте 2+ мин', a[1], a[2], a[6]))
                i += win
            else:
                i += 1
        win = int(600 / 5)
        for i in range(0, max(0, len(tr) - win), win // 2):
            seg = tr[i:i + win]
            hot = sum(1 for s in seg if s[4] in ('бегство', 'охота', 'бой'))
            if hot / len(seg) > 0.3 and not any(seg[0][0] <= d['t'] <= seg[-1][0] and n in (d.get('victim'), d.get('killer')) for d in deaths):
                issues.append(('беготня', seg[0][0], seg[-1][0], f'{n}: {int(100 * hot / len(seg))}% времени бегство/охота/бой, без смертей', seg[0][1], seg[0][2], None))
        if tr and tr[-1][0] > 1800 and not places:
            issues.append(('без РТ', 0, tr[-1][0], f'{n}: за {mmss(tr[-1][0])} ни разу не был на РТ', tr[-1][1], tr[-1][2], None))
        ms = sorted(m['t'] for m in milestones if m['text'].split('] ', 1)[-1].startswith(n))
        last = 0
        for t in ms + [tr[-1][0] if tr else 0]:
            if t - last > 600:
                issues.append(('нет прогресса', last, t, f'{n}: {int((t - last) / 60)} мин без вех', None, None, None))
            last = t
    issues.sort(key=lambda x: x[1])
    return tracks, deaths, milestones, issues, stats


def svg_map(head, tracks, deaths, issues, size=900):
    half = head['half']
    sc = size / (2 * half)
    P = lambda x, y: ((x + half) * sc, (y + half) * sc)
    out = [f'<svg viewBox="0 0 {size} {size}" width="{size}" height="{size}" style="background:#1d2420">']
    for m in head['monuments']:
        (x0, y0), (x1, y1) = P(*m['min']), P(*m['max'])
        out.append(f'<rect x="{x0:.1f}" y="{y0:.1f}" width="{x1 - x0:.1f}" height="{y1 - y0:.1f}" fill="#4a4a3a" stroke="#c9b26a" stroke-width="1"/>')
        out.append(f'<text x="{x0:.1f}" y="{y0 - 3:.1f}" fill="#d8cf9a" font-size="10">{html.escape(m["name"])}</text>')
    for k, (n, tr) in enumerate(tracks.items()):
        col = PALETTE[k % len(PALETTE)]
        seg = []
        for s in tr:
            if s[3]:
                if len(seg) > 1:
                    out.append(f'<polyline points="{" ".join(f"{a:.1f},{b:.1f}" for a, b in seg)}" fill="none" stroke="{col}" stroke-width="1.2" opacity="0.8"/>')
                seg = []
                continue
            seg.append(P(s[1], s[2]))
        if len(seg) > 1:
            out.append(f'<polyline points="{" ".join(f"{a:.1f},{b:.1f}" for a, b in seg)}" fill="none" stroke="{col}" stroke-width="1.2" opacity="0.8"/>')
        if tr:
            x, y = P(tr[-1][1], tr[-1][2])
            out.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="3" fill="{col}"/><text x="{x + 4:.1f}" y="{y + 3:.1f}" fill="{col}" font-size="9">{html.escape(n)}</text>')
    # тепло: где шли бои, охота, бегство и смерти (каждые 5 с — точка), клетки 25 м
    heat = collections.Counter()
    for n, tr in tracks.items():
        for s in tr:
            if not s[3] and s[4] in ('бой', 'охота', 'бегство', 'месть'):
                heat[(int(s[1] // 25), int(s[2] // 25))] += 1
    for d in deaths:
        heat[(int(d['x'] // 25), int(d['y'] // 25))] += 8
    if heat:
        mx = max(heat.values())
        for (cx, cy), v in heat.items():
            x, y = P((cx + 0.5) * 25, (cy + 0.5) * 25)
            r = 25 * sc * (0.6 + 1.2 * v / mx)
            out.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{r:.1f}" fill="#ff3010" opacity="{0.15 + 0.5 * v / mx:.2f}"><title>жар {v}</title></circle>')
    for d in deaths:
        x, y = P(d['x'], d['y'])
        out.append(f'<text x="{x - 4:.1f}" y="{y + 4:.1f}" fill="#ff4040" font-size="12" font-weight="bold">✕</text>')
    for kind, t0, t1, text, x, y, place in issues:
        if x is None or kind not in ('тусовка', 'стоит'):
            continue
        cx, cy = P(x, y)
        col = '#ffb000' if kind == 'тусовка' else '#00c0ff'
        out.append(f'<circle cx="{cx:.1f}" cy="{cy:.1f}" r="7" fill="none" stroke="{col}" stroke-width="2"><title>{html.escape(text)}</title></circle>')
    out.append('</svg>')
    return '\n'.join(out)


def main():
    path = sys.argv[1]
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.splitext(path)[0] + '.html'
    head, pos, ev = load(path)
    tracks, deaths, milestones, issues, stats = analyze(head, pos, ev)
    dur = pos[-1]['t'] if pos else 0
    rows = []
    for n, s in stats.items():
        top_goals = ', '.join(f'{g or "—"} {100 * c // max(1, sum(s["goals"].values()))}%' for g, c in s['goals'].most_common(4))
        rts = ', '.join(f'{p} {c * 5 // 60} мин' for p, c in s['places'].most_common(3)) or '—'
        rows.append(f'<tr><td>{html.escape(n)}</td><td>{int(s["dist"])} м</td><td>{s["deaths"]}/{s["kills"]}</td><td>{s["last_scrap"]}</td><td>{html.escape(top_goals)}</td><td>{html.escape(rts)}</td></tr>')
    iss = collections.Counter(i[0] for i in issues)
    ir = ''.join(f'<tr><td>{k}</td><td>{mmss(t0)}–{mmss(t1)}</td><td>{html.escape(text)}</td><td>{html.escape(place or "")}</td></tr>' for k, t0, t1, text, x, y, place in issues[:400])
    dr = ''.join(f'<tr><td>{mmss(d["t"])}</td><td>{html.escape(d.get("victim") or "?")}</td><td>{html.escape(d.get("killer") or "—")}</td><td>{html.escape(d.get("weapon") or "")}</td><td>{html.escape(d.get("place") or "")}</td></tr>' for d in deaths)
    mr = ''.join(f'<li>{html.escape(m["text"])}</li>' for m in milestones[-200:])
    page = f"""<!doctype html><meta charset="utf-8"><title>Летопись вайпа</title>
<style>body{{background:#141816;color:#ddd;font:14px system-ui;margin:20px}} table{{border-collapse:collapse;margin:8px 0}} td,th{{border:1px solid #333;padding:3px 8px}} th{{background:#222}} h2{{margin-top:28px}}</style>
<h1>Летопись вайпа · {html.escape(head.get('started', ''))} · длительность {mmss(dur)}</h1>
<p>Игроков и ботов: {len(tracks)} · смертей: {len(deaths)} · вех: {len(milestones)} · проблем: {', '.join(f'{k} {v}' for k, v in iss.items()) or 'нет'}</p>
<h2>Карта</h2><p>Линии — маршруты, красные пятна — где шли бои/охота/бегство (ярче — жарче), ✕ — смерти, жёлтые круги — «тусовки», голубые — «стоит».</p>{svg_map(head, tracks, deaths, issues)}
<h2>Кто как играл</h2><table><tr><th>Кто</th><th>Прошёл</th><th>Смертей/убийств</th><th>Лом при себе</th><th>Цели (доля времени)</th><th>Время на РТ</th></tr>{''.join(rows)}</table>
<h2>Проблемы</h2><table><tr><th>Вид</th><th>Когда</th><th>Что</th><th>Где</th></tr>{ir}</table>
<h2>Смерти</h2><table><tr><th>Когда</th><th>Кто</th><th>Кем</th><th>Чем</th><th>Где</th></tr>{dr}</table>
<h2>Вехи</h2><ul>{mr}</ul>"""
    open(out, 'w', encoding='utf-8').write(page)
    print(f'летопись {mmss(dur)}, участников {len(tracks)}, смертей {len(deaths)}, вех {len(milestones)}')
    print('проблемы:', dict(iss))
    for k, t0, t1, text, x, y, place in issues[:25]:
        print(f'  [{mmss(t0)}–{mmss(t1)}] {k}: {text}{" · " + place if place else ""}')
    print('отчёт:', out)


if __name__ == '__main__':
    main()
