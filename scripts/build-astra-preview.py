# Страница-превью работы Астры + ревью и дебаты: docs/design/astra-preview.html (иконки и план встроены data:URI)
import base64, json, os
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def b64(p):
    return 'data:image/png;base64,' + base64.b64encode(open(os.path.join(ROOT, p), 'rb').read()).decode()


man = json.load(open(os.path.join(ROOT, 'assets/manifest_components.json'), encoding='utf-8'))
RU = {'icon_comp_fasteners': 'Крепёж', 'icon_comp_canvas': 'Брезент', 'icon_comp_seal': 'Уплотнители', 'icon_comp_bearing': 'Подшипник',
      'icon_comp_winding': 'Обмотка', 'icon_comp_sterile': 'Стерильная укладка', 'icon_comp_optics': 'Оптика', 'icon_comp_handtools': 'Столярный набор',
      'icon_comp_resin': 'Смола', 'icon_comp_outboard': 'Лодочный мотор', 'icon_item_boat': 'Лодка', 'icon_item_anchor': 'Якорь',
      'icon_item_patchkit': 'Ремнабор', 'icon_item_dynamo': 'Динамо-фонарь', 'icon_item_binoculars': 'Бинокль'}
def ico(a):
    name = RU.get(a['id'], a['id'])
    src = b64('game/art/' + a['cat'] + '/' + a['id'] + '.png')
    return f'<figure class="ico"><img src="{src}" alt="{name}" width="64" height="64"><figcaption>{name}</figcaption></figure>'


icons = ''.join(ico(a) for a in man['assets'])
plan = b64('docs/design/img/o42-l1-plan.png')

COMP = [('Пружина', 'Наган · затворная группа', ''), ('Труба', 'Нарезной ствол · самогонный аппарат', ''), ('Изолента', 'Самодельный ПП · 6Б2', ''),
        ('Шестерни', 'Генератор · верстак 3', ''), ('Плата', 'Верстак 3 · коллиматор', ''), ('Линза', 'Прицел 2× · прицел 4×', ''),
        ('УСМ', 'Грач · СВТ-40', ''), ('Затворная группа', 'ППС-43 · АКМ', ''), ('Нарезной ствол', 'СВТ-40 · хромированный ствол', ''),
        ('Корпус пистолета', 'Грач', 'cut'), ('Корпус ПП', 'ППС-43 · ППШ', ''), ('Корпус винтовки', 'СВТ-40 · СВД', ''),
        ('Коробка АК', 'АКМ — главный барьер', 'cut'), ('Хромированный ствол', 'СВД — прямой дорогой рецепт под В3', 'cut'),
        ('Газоотводный узел', 'АКМ · СВД', ''), ('Оружейное масло', 'Ремонт оружия (расходник)', ''), ('Легированная сталь', 'Верстак 3 · АКМ', ''),
        ('Резина', 'Спецовка · плащ ОЗК', ''), ('Медспирт', 'Аптечка · Антирадин', ''), ('Топляк', 'Генератор · коктейль Молотова', ''),
        ('Дизель', 'Отбойник · буровая', ''), ('Электромотор', 'Отбойник · электропечь', ''), ('Медный провод', 'Лампа (+ ценный лом)', 'cut')]
comp_rows = ''.join(f'<tr><th>{n}</th><td>{u}{" <span class=note>одно применение — допустимо по вердикту</span>" if c else ""}</td></tr>' for n, u, c in COMP)

THESES = [
    ('Реализм — не опора: рецепт хорош, если рождает решение о походе', 'Согласна', 'ok', 'Принято', 'Доноры и профили «самодел/заводской» отозваны; масло не из угля — как исправление логики'),
    ('Процедурный «Объект 42» на старте — ошибка', 'Частично', 'mid', 'Принято с правкой', 'Сначала ручной прототип; генератор — только если замеры покажут усталость от карты'),
    ('Шесть сквадов под землёй опустошат остров', 'Частично', 'mid', 'Принято с правкой', 'Потолок 2 сквада / 6 человек; на поверхности ≥60% онлайна'),
    ('Мгновенная смерть на 26-й минуте не для Rust-игры', 'Согласна', 'ok', 'Принято', 'Нарастающий урон с 25:00 и два аварийных выхода до 28:00'),
    ('Порох — из серы и угля, а не из лута', 'Согласна', 'ok', 'Принято', 'Рейд остаётся наградой за добычу ресурсов'),
    ('Лодку заморозить; компонентов ≤24, без одиночных назначений', 'Согласна', 'ok', 'Принято с правкой', 'Правило «2 применения» — только для новых; редкий барьер может быть одиночным'),
    ('16 пружин на АКМ — гринд, а не узкое место', 'Частично', 'mid', 'Принято с правкой', 'Потолок 8–10 пружин на первый комплект; сталь 50 и ≈685 лома сохраняются'),
    ('Резерв от переработки — лишняя сложность', 'Частично', 'mid', 'Принято (версия Астры)', 'Только ручная пометка «не включать в „добавить всё“»'),
]
th_rows = ''.join(f'<tr><td class=num>{i}</td><td>{t}</td><td><span class="chip {c}">{a}</span></td><td><span class="chip v">{v}</span><p>{w}</p></td></tr>' for i, (t, a, c, v, w) in enumerate(THESES, 1))

html = f'''<title>Досье Астры</title>
<meta name="claude:viewport-fit-sides" content="cover">
<link rel="preconnect" href="https://fonts.googleapis.com"><link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=PT+Sans:wght@400;700&family=PT+Sans+Narrow:wght@700&family=PT+Mono&display=swap">
<style>
/* Досье КБ: бумажный лист с миллиметровкой, графит, сигнальный красный — штампы «принято / спорно» */
:root {{
  --paper: #f3f2ee; --sheet: #fbfbf9; --ink: #1f2226; --muted: #5b6168; --line: #d5d6d2; --grid: rgba(70,90,110,.07);
  --red: #b0352a; --ok: #2f6b45; --mid: #9a6a14; --okbg: #e3efe6; --midbg: #f4ead3; --redbg: #f5e1de;
  --display: "PT Sans Narrow", "Arial Narrow", sans-serif; --body: "PT Sans", "Segoe UI", sans-serif; --mono: "PT Mono", Consolas, monospace;
}}
@media (prefers-color-scheme: dark) {{ :root:not([data-theme="light"]) {{
  --paper: #16181b; --sheet: #1d2024; --ink: #e6e5e0; --muted: #a1a6ad; --line: #33373d; --grid: rgba(150,170,190,.06);
  --red: #e0675a; --ok: #7cc28f; --mid: #e0b45c; --okbg: #1f3326; --midbg: #3a2f17; --redbg: #3d211e; color-scheme: dark }} }}
:root[data-theme="dark"] {{
  --paper: #16181b; --sheet: #1d2024; --ink: #e6e5e0; --muted: #a1a6ad; --line: #33373d; --grid: rgba(150,170,190,.06);
  --red: #e0675a; --ok: #7cc28f; --mid: #e0b45c; --okbg: #1f3326; --midbg: #3a2f17; --redbg: #3d211e; color-scheme: dark }}
body {{ background: var(--paper); color: var(--ink); font: 16px/1.55 var(--body);
  background-image: linear-gradient(var(--grid) 1px, transparent 1px), linear-gradient(90deg, var(--grid) 1px, transparent 1px); background-size: 24px 24px; }}
.wrap {{ max-width: 1080px; margin: 0 auto; padding-block: 32px 64px; padding-left: max(16px, env(safe-area-inset-left, 0px)); padding-right: max(16px, env(safe-area-inset-right, 0px)); display: grid; gap: 28px; }}
header {{ display: grid; gap: 10px; border-bottom: 3px solid var(--ink); padding-bottom: 18px; }}
.stamp {{ font: 12px var(--mono); letter-spacing: .12em; text-transform: uppercase; color: var(--red); border: 2px solid var(--red); padding: 2px 8px; justify-self: start; transform: rotate(-1.5deg); }}
h1 {{ font: 700 clamp(34px, 6vw, 56px)/1 var(--display); text-transform: uppercase; letter-spacing: .02em; margin: 0; text-wrap: balance; }}
.lede {{ max-width: 68ch; color: var(--muted); margin: 0; }}
.meta {{ display: flex; flex-wrap: wrap; gap: 8px 18px; font: 13px var(--mono); color: var(--muted); }}
section {{ background: var(--sheet); border: 1px solid var(--line); padding: 22px; display: grid; gap: 14px; min-width: 0; }}
h2 {{ font: 700 26px/1.1 var(--display); text-transform: uppercase; letter-spacing: .03em; margin: 0; display: flex; gap: 12px; align-items: baseline; }}
h2 small {{ font: 12px var(--mono); color: var(--muted); letter-spacing: .08em; }}
h3 {{ font: 700 19px/1.2 var(--display); text-transform: uppercase; margin: 0; }}
p {{ margin: 0; max-width: 72ch; }}
.steps {{ list-style: none; margin: 0; padding: 0; counter-reset: s; display: grid; gap: 10px; }}
.steps li {{ counter-increment: s; display: grid; grid-template-columns: 36px 1fr; gap: 12px; align-items: start; }}
.steps li::before {{ content: counter(s); font: 700 22px/1 var(--display); color: var(--sheet); background: var(--ink); width: 36px; height: 36px; display: grid; place-items: center; }}
.steps b {{ display: block; }}
.tbl {{ overflow-x: auto; }}
table {{ border-collapse: collapse; width: 100%; font-size: 15px; }}
th, td {{ text-align: left; vertical-align: top; padding: 8px 10px; border-bottom: 1px solid var(--line); }}
th {{ font-weight: 700; white-space: nowrap; }}
thead th {{ font: 12px var(--mono); text-transform: uppercase; letter-spacing: .08em; color: var(--muted); border-bottom: 2px solid var(--ink); }}
td p {{ color: var(--muted); font-size: 14px; margin-top: 4px; }}
.num {{ font: 700 18px var(--display); color: var(--red); width: 28px; }}
.note {{ font: 12px var(--mono); color: var(--mid); display: block; }}
.chip {{ font: 12px var(--mono); letter-spacing: .04em; padding: 2px 8px; border: 1px solid currentColor; white-space: nowrap; display: inline-block; }}
.chip.ok {{ color: var(--ok); background: var(--okbg); }} .chip.mid {{ color: var(--mid); background: var(--midbg); }} .chip.v {{ color: var(--ink); }}
.icons {{ display: grid; grid-template-columns: repeat(auto-fill, minmax(104px, 1fr)); gap: 10px; }}
.ico {{ margin: 0; display: grid; justify-items: center; gap: 6px; padding: 10px 6px; border: 1px dashed var(--line); background: repeating-linear-gradient(-45deg, transparent 0 8px, var(--grid) 8px 16px); }}
.ico img {{ image-rendering: pixelated; filter: grayscale(.35); }}
.ico figcaption {{ font-size: 13px; text-align: center; }}
.cols {{ display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 12px; }}
.card {{ border: 1px solid var(--line); padding: 14px; display: grid; gap: 6px; align-content: start; min-width: 0; }}
.card.pick {{ border: 2px solid var(--ink); }}
.card .tag {{ font: 12px var(--mono); color: var(--red); text-transform: uppercase; letter-spacing: .08em; }}
.chain {{ display: grid; grid-template-columns: repeat(auto-fit, minmax(190px, 1fr)); gap: 0; border: 1px solid var(--line); }}
.chain div {{ padding: 12px 14px; border-right: 1px solid var(--line); display: grid; gap: 4px; }}
.chain div:last-child {{ border-right: 0; }}
.chain .t {{ font: 12px var(--mono); color: var(--red); }}
figure.plan {{ margin: 0; display: grid; gap: 6px; }} figure.plan img {{ border: 1px solid var(--line); }}
figure.plan figcaption {{ font-size: 13px; color: var(--muted); }}
.qs {{ display: grid; gap: 10px; }} .q {{ display: grid; gap: 4px; border-left: 3px solid var(--red); padding-left: 12px; }}
.q span {{ color: var(--muted); font-size: 15px; }}
.files {{ font: 13px var(--mono); color: var(--muted); display: grid; gap: 3px; }}
@media (prefers-reduced-motion: reduce) {{ * {{ transition: none !important; }} }}
</style>
<div class="wrap">
<header>
  <span class="stamp">Совершенно не секретно · только думаем</span>
  <h1>Досье Астры: компоненты, крафт и «Объект 42»</h1>
  <p class="lede">Что придумала Астра (Codex), как это разобрал геймдизайнер-ревьюер и чем закончились два раунда дебатов. В код пока ничего не переносилось — это концепты для решения.</p>
  <div class="meta"><span>6 документов</span><span>2 раунда дебатов</span><span>34 → 23 компонента</span><span>15 иконок</span><span>4 вопроса автору</span></div>
</header>

<section>
  <h2>Итог: план в 5 шагов <small>вердикт ревьюера</small></h2>
  <ol class="steps">
    <li><div><b>Единый реестр рецептов.</b> Сейчас у АКМ два рецепта: 300 металла в Weaponry.cs и 250 + 25 стали в RecipeOverride. Убрать расхождение, обновить экономику — без этого все цифры спорны.</div></li>
    <li><div><b>Чистка компонентов.</b> Убрать 10 новых компонентов и оружие с охраны (нарушало D35), пружин на АКМ 8–10 вместо 16, хромированный ствол — прямым рецептом, масло не из угля. Прогон 12 ботов × 10 ч.</div></li>
    <li><div><b>Удобство крафта.</b> Закреплённая цель крафта и честный предпросмотр разборки, затем пометка «не перерабатывать». Заказ цепочки целиком — следующим шагом.</div></li>
    <li><div><b>Ручной прототип «Объекта 42».</b> Один этаж, три района, шахта, дизель, демонтаж 90 с, три выхода, 6 охранников, 8 контейнеров — и сразу с ботами-игроками.</div></li>
    <li><div><b>Вшить «42» в экономику.</b> Сравнить вайп с «42» и без. Второй этаж и генерацию — только по замерам.</div></li>
  </ol>
</section>

<section>
  <h2>Дебаты: 8 тезисов <small>ревьюер → Астра → вердикт</small></h2>
  <div class="tbl"><table>
    <thead><tr><th>№</th><th>Тезис ревьюера</th><th>Астра</th><th>Итог</th></tr></thead>
    <tbody>{th_rows}</tbody>
  </table></div>
  <p>Ревьюер признал и свою ошибку: в лут-таблицах охрана роняла оружие (Наган 8%, СВТ 4%, АКМ 3%) — это против D35, строки убираются.</p>
</section>

<section>
  <h2>Компоненты: 23 после чистки <small>было 34</small></h2>
  <p>Каждый новый компонент нужен минимум в двух вещах. Редкие «барьеры» (коробка АК, хромированный ствол) могут иметь одно применение — в этом их смысл. Крепёж растворился в металле, парусина и кожа — в ткани, уплотнения — в резине.</p>
  <div class="tbl"><table><thead><tr><th>Компонент</th><th>Где нужен</th></tr></thead><tbody>{comp_rows}</tbody></table></div>
</section>

<section>
  <h2>Заморожено <small>иконки уже сделаны, вещей пока нет</small></h2>
  <p>Лодка, якорь, ремнабор, динамо и бинокль ждут водного контента; десять компонентов под них убраны. Иконки лежат в игре без привязки.</p>
  <div class="icons">{icons}</div>
</section>

<section>
  <h2>«Объект 42» <small>20-object42 + дебаты</small></h2>
  <h3>Главная цель рейда: блок наведения</h3>
  <div class="chain">
    <div><span class="t">ШАГ 1 · ДИЗЕЛЬ</span>Держать E 5 с. Гул на 80 м, объявление «Сервисный контур включён».</div>
    <div><span class="t">ШАГ 2 · ДЕМОНТАЖ</span>Пульт на нижнем уровне, 90 с автоматически. Красный маяк, слышно всем внутри.</div>
    <div><span class="t">ШАГ 3 · ЗАБРАТЬ</span>Один блок на сессию. Чужой сквад может перехватить результат.</div>
    <div><span class="t">ШАГ 4 · ВЫНЕСТИ</span>Эвакуация. В переработчике — 120 лома + 8 стали.</div>
  </div>
  <p>Ядерный заряд остаётся на ракете: его нельзя унести, взорвать или превратить в крафт.</p>
  <h3>Три формы уровня</h3>
  <div class="cols">
    <div class="card pick"><span class="tag">Первый прототип</span><b>А. Разорванное кольцо</b><p>Два смещённых кольца вокруг шахты со скальными вырезами. Легко ориентироваться, удобно обходить бой.</p></div>
    <div class="card"><span class="tag">Потом</span><b>Б. Три крыла</b><p>Шахта у края, жилое, штабное и научное крылья разной длины. Районы легче запомнить.</p></div>
    <div class="card"><span class="tag">Потом</span><b>В. Двойная подкова</b><p>Подковы раскрыты в разные стороны, шахта в изгибе. Больше обходов и перехватов.</p></div>
  </div>
  <h3>Правила, о которых договорились</h3>
  <div class="cols">
    <div class="card"><b>Время</b><p>Рейд до 25 мин, с 25:00 нарастающий урон, два аварийных выхода до 28:00 — без мгновенной смерти.</p></div>
    <div class="card"><b>Люди</b><p>Максимум 2 сквада, 6 человек. На поверхности должно оставаться ≥60% онлайна.</p></div>
    <div class="card"><b>Лут</b><p>Награда — как 1,5–2 рейда на РТ2. Охрана не роняет оружие. Не нужно обыскивать всё подряд.</p></div>
  </div>
  <figure class="plan"><img src="{plan}" alt="Схема уровня −1 из генератора plan-o42.py" width="1952" height="1574"><figcaption>Ранний черновик уровня −1 из генератора (до дебатов). По итогам решено начать с ручной карты варианта А.</figcaption></figure>
</section>

<section>
  <h2>Крафт: что берём <small>21-crafting-ideas</small></h2>
  <div class="cols">
    <div class="card pick"><span class="tag">Первым</span><b>Закреплённая цель</b><p>До 3 рецептов на экране: «АКМ: коробка 0/1, сталь 12/25» и подсказка «найти / сделать / переработать».</p></div>
    <div class="card pick"><span class="tag">Первым</span><b>Честный предпросмотр разборки</b><p>Перед переработкой видно, что получишь и что потеряешь.</p></div>
    <div class="card"><span class="tag">Следом</span><b>Пометка «не перерабатывать»</b><p>Ручная, без автоматического резерва по целям.</p></div>
    <div class="card"><span class="tag">Позже</span><b>Заказ цепочки целиком</b><p>«Собрать ППС-43» — вся цепочка и время, в очередь на своих станках.</p></div>
  </div>
</section>

<section>
  <h2>Решать автору <small>4 вопроса</small></h2>
  <div class="qs">
    <div class="q"><b>«Объект 42» — редкий ивент вайпа или регулярный режим?</b><span>Рекомендация: редкий ивент, который директор открывает в поздней фазе вайпа.</span></div>
    <div class="q"><b>Нужна ли новая геометрия «42» каждый вайп?</b><span>Рекомендация: пока ручная карта, меняются лут и выходы.</span></div>
    <div class="q"><b>Что ограничивает первый АКМ?</b><span>Рекомендация: коробка АК как барьер, сталь и лом как цена, пружин не больше 8–10.</span></div>
    <div class="q"><b>Нужен ли водный контент?</b><span>Рекомендация: отложить; если «нет» — лодку вычеркнуть совсем.</span></div>
  </div>
</section>

<section>
  <h2>Документы <small>docs/design</small></h2>
  <div class="files"><span>19-components.md — 34 компонента, 99 рецептов, лут на 14 мест</span><span>20-object42.md — два уровня, процедурная сборка, три формы</span><span>21-crafting-ideas.md — 12 идей по крафту</span><span>22-components-alt.md — реалистичные альтернативы</span><span>23-review.md — ревью геймдизайнера</span><span>24-debate.md — ответ Астры</span><span>25-verdict.md — финальный вердикт</span></div>
</section>
</div>
'''
out = os.path.join(ROOT, 'docs', 'design', 'astra-preview.html')
open(out, 'w', encoding='utf-8').write(html)
print('ok', out, round(len(html) / 1024), 'KB')
