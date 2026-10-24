# Отчёт по лаборатории Астры (ветки astra-lab / astra-bots) → HTML-страница с встроенными миниатюрами ассетов.
# Запуск: python scripts/build-astra-lab-report.py <out.html>
import base64, glob, io, json, os, re, subprocess, sys, html as H
from PIL import Image

WT = r'C:\Users\Xbaza\Rastvordd-wt\astra'
BOTS = r'C:\Users\Xbaza\Rastvordd-wt\astra-bots'
OUT = sys.argv[1] if len(sys.argv) > 1 else 'astra-lab-report.html'
rd = lambda p: open(p, encoding='utf-8-sig', errors='ignore').read() if os.path.exists(p) else ''
cut = lambda s, n: (s[:n] + '…') if len(s) > n else s
md = lambda s: re.sub(r'`([^`]+)`', r'<code>\1</code>', re.sub(r'\*\*([^*]+)\*\*', r'<b>\1</b>', H.escape(s)))

# роадмап
items = []
for line in rd(os.path.join(WT, 'docs/astra/ROADMAP.md')).splitlines():
    m = re.match(r'\s*-\s*\[(x|~| )\]\s*\*\*([^*]+)\*\*\s*[—-]?\s*(.*)', line)
    if m:
        items.append((m.group(1), m.group(2).strip(), m.group(3).strip()))
done = [i for i in items if i[0] == 'x']; wip = [i for i in items if i[0] == '~']; todo = [i for i in items if i[0] == ' ']


def rows(lst, cls):
    return ''.join(f'<li class="{cls}"><b>{md(t)}</b><span>{md(cut(n, 190))}</span></li>' for _, t, n in lst)


# уроки исследования и вердикты дебатов
res = rd(os.path.join(WT, 'docs/astra/research.md'))
lessons = re.findall(r'^\d+\.\s+\*\*([^*]+)\*\*\s*(.*)$', res.split('## Десять конкретных уроков')[-1].split('## Источники')[0], re.M)
deb = [l for l in rd(os.path.join(WT, 'docs/astra/debates.md')).splitlines() if re.search(r'вердикт', l, re.I)]
deb += [l for l in rd(os.path.join(BOTS, 'docs/astra/bots-debates.md')).splitlines() if re.search(r'вердикт', l, re.I)]
VERD = re.compile(r'^[-*\s]*(Третий путь\s*(/|и)\s*вердикт[^:]*:\s*)?')
deb_html = ''.join('<li>' + md(cut(VERD.sub('', l), 230)) + '</li>' for l in deb[-10:])

# коммиты и журнал ботов
def git(wt, *a):
    try:
        return subprocess.run(['git', '-C', wt, *a], capture_output=True, text=True, encoding='utf-8').stdout
    except Exception:
        return ''
n_main = len(git(WT, 'log', '--oneline', 'main..HEAD').splitlines())
n_bots = len(git(BOTS, 'log', '--oneline', 'astra-lab..HEAD').splitlines())
botlog = [l for l in rd(os.path.join(BOTS, 'docs/astra/LOG.md')).splitlines() if l.startswith('## ')][-6:]
review = rd(os.path.join(WT, 'docs/astra/REVIEW.md'))
open_notes = re.findall(r'^\d+\.\s+\[(\w+)\]\s+(.*)$', review.split('Открытые:')[-1].split('Закрытые')[0], re.M)

# ассеты: миниатюры
thumbs, n_assets = [], 0
for f in sorted(glob.glob(os.path.join(WT, 'assets/manifest_astra_*.json')), key=lambda p: int(re.findall(r'\d+', os.path.basename(p))[0])):
    try:
        man = json.loads(rd(f))
    except Exception:
        continue
    for a in man.get('assets', []):
        p = os.path.join(WT, 'game/art', a.get('cat', ''), a['id'] + '.png')
        if not os.path.exists(p):
            continue
        n_assets += 1
        im = Image.open(p).convert('RGBA'); im.thumbnail((96, 96), Image.NEAREST)
        b = io.BytesIO(); im.save(b, 'PNG')
        thumbs.append(f'<figure><img src="data:image/png;base64,{base64.b64encode(b.getvalue()).decode()}" alt="{a["id"]}"><figcaption>{a["id"].replace("_", " ")}</figcaption></figure>')

pct = round(100 * len(done) / max(1, len(items)))
page = f'''<title>Лаборатория Астры</title>
<meta name="claude:viewport-fit-sides" content="cover">
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=PT+Sans:wght@400;700&family=PT+Sans+Narrow:wght@700&family=PT+Mono&display=swap">
<style>
/* Журнал испытаний КБ: миллиметровка, графит, красный штамп; статусы — зелёный/янтарный/серый */
:root {{ --paper:#f3f2ee; --sheet:#fbfbf9; --ink:#1f2226; --muted:#5b6168; --line:#d5d6d2; --grid:rgba(70,90,110,.07); --red:#b0352a; --ok:#2f6b45; --mid:#9a6a14; --okbg:#e3efe6; --midbg:#f4ead3; --todo:#8a8f96;
  --display:"PT Sans Narrow","Arial Narrow",sans-serif; --body:"PT Sans","Segoe UI",sans-serif; --mono:"PT Mono",Consolas,monospace; }}
@media (prefers-color-scheme: dark) {{ :root:not([data-theme="light"]) {{ --paper:#16181b; --sheet:#1d2024; --ink:#e6e5e0; --muted:#a1a6ad; --line:#33373d; --grid:rgba(150,170,190,.06); --red:#e0675a; --ok:#7cc28f; --mid:#e0b45c; --okbg:#1f3326; --midbg:#3a2f17; --todo:#7d838b; color-scheme:dark }} }}
:root[data-theme="dark"] {{ --paper:#16181b; --sheet:#1d2024; --ink:#e6e5e0; --muted:#a1a6ad; --line:#33373d; --grid:rgba(150,170,190,.06); --red:#e0675a; --ok:#7cc28f; --mid:#e0b45c; --okbg:#1f3326; --midbg:#3a2f17; --todo:#7d838b; color-scheme:dark }}
body {{ background:var(--paper); color:var(--ink); font:16px/1.55 var(--body); background-image:linear-gradient(var(--grid) 1px,transparent 1px),linear-gradient(90deg,var(--grid) 1px,transparent 1px); background-size:24px 24px; }}
.wrap {{ max-width:1080px; margin:0 auto; padding-block:32px 64px; padding-left:max(16px,env(safe-area-inset-left,0px)); padding-right:max(16px,env(safe-area-inset-right,0px)); display:grid; gap:24px; }}
header {{ display:grid; gap:10px; border-bottom:3px solid var(--ink); padding-bottom:16px; }}
.stamp {{ font:12px var(--mono); letter-spacing:.12em; text-transform:uppercase; color:var(--red); border:2px solid var(--red); padding:2px 8px; justify-self:start; transform:rotate(-1.5deg); }}
h1 {{ font:700 clamp(32px,6vw,52px)/1 var(--display); text-transform:uppercase; margin:0; }}
h2 {{ font:700 24px/1.1 var(--display); text-transform:uppercase; margin:0; display:flex; gap:12px; align-items:baseline; }}
h2 small {{ font:12px var(--mono); color:var(--muted); }}
p {{ margin:0; max-width:74ch; }}
section {{ background:var(--sheet); border:1px solid var(--line); padding:20px; display:grid; gap:12px; min-width:0; }}
.kpis {{ display:grid; grid-template-columns:repeat(auto-fit,minmax(150px,1fr)); gap:10px; }}
.kpi {{ border:1px solid var(--line); padding:12px; display:grid; gap:2px; }}
.kpi b {{ font:700 32px/1 var(--display); font-variant-numeric:tabular-nums; }} .kpi span {{ font:12px var(--mono); color:var(--muted); text-transform:uppercase; letter-spacing:.06em; }}
.bar {{ height:10px; background:var(--line); }} .bar i {{ display:block; height:100%; background:var(--ok); }}
ul.rm {{ list-style:none; margin:0; padding:0; display:grid; gap:6px; }}
ul.rm li {{ display:grid; gap:2px; padding:8px 10px; border-left:4px solid var(--todo); background:transparent; min-width:0; }}
ul.rm li.x {{ border-color:var(--ok); background:var(--okbg); }} ul.rm li.w {{ border-color:var(--mid); background:var(--midbg); }}
ul.rm span {{ color:var(--muted); font-size:14px; overflow-wrap:anywhere; }}
code {{ font:13px var(--mono); }}
ol, ul.plain {{ margin:0; padding-left:20px; display:grid; gap:6px; }}
.thumbs {{ display:grid; grid-template-columns:repeat(auto-fill,minmax(104px,1fr)); gap:8px; }}
figure {{ margin:0; display:grid; justify-items:center; gap:4px; padding:6px; border:1px dashed var(--line); }}
figure img {{ image-rendering:pixelated; max-width:100%; }} figcaption {{ font-size:11px; color:var(--muted); text-align:center; overflow-wrap:anywhere; }}
.note {{ display:grid; grid-template-columns:auto 1fr; gap:10px; align-items:start; }}
.pr {{ font:12px var(--mono); padding:1px 6px; border:1px solid currentColor; }} .pr.высокий {{ color:var(--red); }} .pr.средний {{ color:var(--mid); }} .pr.низкий {{ color:var(--todo); }}
</style>
<div class="wrap">
<header><span class="stamp">Испытания · ветка astra-lab</span><h1>Лаборатория Астры</h1>
<p>Что Астра (Codex, модель gpt-6-astra) сделала сама в отдельных ветках: три дорожки — роадмап, ИИ ботов, ассеты. Ревьюер — Claude, раз в час.</p></header>
<section><h2>Сводка</h2>
<div class="kpis"><div class="kpi"><b>{len(done)}/{len(items)}</b><span>пунктов роадмапа</span></div><div class="kpi"><b>{len(wip)}</b><span>в работе</span></div>
<div class="kpi"><b>{n_main}</b><span>коммитов astra-lab</span></div><div class="kpi"><b>{n_bots}</b><span>коммитов astra-bots</span></div><div class="kpi"><b>{n_assets}</b><span>новых ассетов</span></div></div>
<div class="bar" role="img" aria-label="Готово {pct}%"><i style="width:{pct}%"></i></div></section>
<section><h2>Что реально поменялось <small>главное</small></h2><ul class="plain">
<li><b>«Объект 42» — первый проходимый вертикальный срез:</b> ручная карта (8 модулей, 3 района, шахта), охрана (6 NPC) и 8 ящиков, запуск дизеля (держать E 5 с), демонтаж блока наведения 90 с, хранилище, открывающееся только с питанием.</li>
<li><b>Экономика:</b> единый реестр рецептов (у АКМ одна цена — 250 металла + 25 стали), первый комплект «В3 + стол + АКМ» = 10 пружин и 50 стали вместо 16 пружин, компоненты почищены до 23.</li>
<li><b>Лут охраны по правилу D35:</b> оружие не падает, одежды не больше двух вещей (проверено на 1000 трупов).</li>
<li><b>Боты:</b> стенд дуэлей и журнал боевых ошибок, исправлены перенос перезарядки на новую цель и учёт забытой цели; самообучение пока без роста винрейта.</li>
<li><b>Баги:</b> потеря дробного запаса шкафа, сброс квот редкого лута, случайная броня на полигоне, диагностика оборванных симуляций.</li></ul></section>
<section><h2>Роадмап <small>готово · в работе · впереди</small></h2><ul class="rm">{rows(done, "x")}{rows(wip, "w")}{rows(todo[:12], "t")}</ul>
<p class="muted">Ещё впереди: {max(0, len(todo) - 12)} пунктов.</p></section>
<section><h2>Боты: ИИ и самообучение <small>последние шаги</small></h2><ul class="plain">{''.join(f"<li>{md(cut(l[3:], 160))}</li>" for l in botlog)}</ul></section>
<section><h2>Исследование: 10 уроков <small>Rust, Arc Raiders, Tarkov, DayZ, SCUM, Zomboid, Hunt</small></h2><ol>{''.join(f"<li><b>{md(t)}</b> {md(cut(r, 170))}</li>" for t, r in lessons)}</ol></section>
<section><h2>Дебаты с собой <small>вердикты</small></h2><ul class="plain">{deb_html}</ul></section>
<section><h2>Ассеты <small>{n_assets} шт.</small></h2><div class="thumbs">{''.join(thumbs)}</div></section>
<section><h2>Открытые замечания ревьюера</h2>{''.join(f'<div class="note"><span class="pr {p}">{p}</span><span>{md(cut(t, 260))}</span></div>' for p, t in open_notes)}</section>
<section><h2>Что брать в main <small>рекомендация ревьюера</small></h2><ol>
<li><b>Брать сейчас:</b> единый реестр рецептов, лут охраны по D35, баг-фиксы (запас шкафа, квоты лута, перезарядка/забытая цель ботов) — проверены сборкой и тестами.</li>
<li><b>Брать после показа тебе:</b> пружины 10 и цены первого АКМ — это баланс, решать тебе в игре.</li>
<li><b>Пока не брать:</b> «Объект 42» — срез живой, но без входа/выхода, лифта и эвакуации; самообучение ботов — нет доказанного роста.</li>
<li><b>Ассеты:</b> можно брать пачками по районам «Объекта 42», когда будет его карта в игре.</li></ol></section>
</div>
'''
open(OUT, 'w', encoding='utf-8').write(page)
print('ok', OUT, len(items), 'items', n_assets, 'assets', round(len(page) / 1024), 'KB')
