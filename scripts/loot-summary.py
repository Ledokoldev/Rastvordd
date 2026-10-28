"""Сводка лута «локация → ящики → лут с шансами» из выгрузки прогрессии (--progdump, разделы loot/crates/rtboxes).

  python scripts/loot-summary.py pd.json docs/design/loot-summary.md

Шансы — сэмплирование настоящего FillCrate (3000 бросков, поздняя фаза режиссёра). Ящик РТ получает вид при первом
наполнении (по весам темы РТ) и держит его до вайпа; респаун меняет только содержимое.
"""
import json, sys, collections
sys.stdout.reconfigure(encoding='utf-8')

RT = [  # ключ темы, название (как Monument.Name), тир РТ, тема словами
    ('gai', 'Пост ГАИ', 0, 'быт, патроны'), ('shop', 'Сельмаг «Продукты»', 0, 'продукты'), ('fuel', 'Заправка «Последняя капля»', 0, 'ГСМ, инструмент'),
    ('garages', 'Гаражи «Ракушки»', 1, 'инструмент'), ('ats', 'АТС «Занято»', 1, 'электрика'), ('depot', 'Депо «Тупик»', 1, 'инструмент, ж/д'),
    ('quarry', 'Карьер «Ковш»', 1, 'патроны, взрывчатка'), ('substation', 'Подстанция «Тихая»', 1, 'электрика'),
    ('warehouse', 'Склад «Центральный»', 2, 'армия, оружие'), ('radio', 'Радиоцентр «Маяк»', 2, 'армия, электрика, ценности'),
    ('weigh', 'Весовая «Последний километр»', 0, 'быт, инструмент'), ('ration', 'Раздаточная «Сухой паёк»', 1, 'продукты, медицина'),
    ('post', 'Почтовый узел «Невручённое»', 1, 'посылки, электрика, ценности'), ('boiler', 'Котельная «Остывший квартал»', 1, 'инструмент'),
    ('seeds', 'Станция семян «Мёртвый посев»', 1, 'продукты, медицина'),
]
BOX = {'wood': 'Бытовой', 'food': 'Продуктовый', 'medical': 'Медицинский', 'tools': 'Инструмент', 'electric': 'Электрика',
       'ammo': 'Патроны', 'army': 'Армейский', 'weapon': 'Оружейный', 'elite': 'Ценности', 'safe': 'Сейф (карта доступа)', 'punch': 'Крейт перфокарты'}
OK_T = lambda b, t: t >= 1 or b not in ('army', 'weapon', 'elite')


def main():
    d = json.load(open(sys.argv[1], encoding='utf-8'))
    out_path = sys.argv[2]
    names = {k: v['name'] for k, v in d['items'].items()}
    loot, rtb = d['loot'], d['rtboxes']
    placed = collections.defaultdict(list)
    for rt, tier, box, n in d['crates']:
        placed[rt].append((tier, box, n))

    def items_table(key, top=16, minp=0.03):
        L = loot.get(key)
        if not L: return '_нет данных_'
        rows = [(names.get(i, i), v['p'], v['avg']) for i, v in L['items'].items() if v['p'] >= minp][:top]
        return ' · '.join(f'{n} **{p * 100:.0f}%**{f" ×{a:g}" if a != 1 else ""}' for n, p, a in rows)

    o = ['# Лут: локация → ящики → что внутри', '',
         '> Сгенерировано `scripts/loot-summary.py` из `--progdump` (настоящий `FillCrate`, 3000 бросков, поздняя фаза).',
         '> **Вид ящика** выбирается при первом наполнении по весам темы РТ и держится до вайпа; респаун меняет только содержимое.',
         '> Количество: тир 1 ×1,6, тир 2 ×2,2 от тира 0. Сплав, канистры, фрагменты чертежей и медицина прогрессии — общий слой тира (`Prog.Loot`), одежда — `Clothing.Loot`.', '']
    for tier in (0, 1, 2):
        o.append(f'## РТ тира {tier}' + (' (без охраны)' if tier == 0 else ' (малая охрана)' if tier == 1 else ' (крупные)'))
        o.append('')
        for key, name, t, theme in RT:
            if t != tier: continue
            pl = placed.get(name)
            o.append(f'### {name}')
            o.append(f'Тема: {theme}.' + ('' if pl else ' **Сейчас в мир не ставится** (малый РТ в коде, вызов размещения выключен).'))
            for ct in sorted({x[0] for x in pl} if pl else {tier}):
                w = [(b, n) for b, n in rtb.get(key, rtb['t%d' % ct]) if OK_T(b, ct)]
                s = sum(n for _, n in w)
                o.append(f'- Ящики тира {ct} — вид по весам: ' + ', '.join(f'{BOX[b]} {n / s * 100:.0f}%' for b, n in w))
            if pl:
                o.append('- Стоит на карте сейчас: ' + ', '.join(f'{BOX.get(b, b)} т{tt} ×{n}' for tt, b, n in sorted(pl)))
            o.append('')
    o.append('## Что внутри: вид ящика × тир')
    o.append('')
    o.append('Шанс — что предмет есть в ящике; ×N — среднее количество, когда выпал.')
    o.append('')
    for b in ['wood', 'food', 'medical', 'tools', 'electric', 'ammo', 'army', 'weapon', 'elite']:
        o.append(f'### {BOX[b]}')
        for t in (0, 1, 2):
            if OK_T(b, t):
                o.append(f'- **Тир {t}:** ' + items_table(f'box_{b}_{t}'))
        o.append('')
    o.append('## Особые контейнеры')
    o.append('')
    for k, lab in [('safe', 'Сейф (карта доступа)'), ('punch_crate', 'Крейт перфокарты'), ('punch_crate_electronics', 'Крейт перфокарты — АТС, Подстанция'),
                   ('event_early', 'Ивент режиссёра — ранняя фаза'), ('event_mid', 'Ивент — средняя фаза'), ('event_late', 'Ивент — поздняя фаза'),
                   ('radio_drop', 'Борт «Маяка»')]:
        o.append(f'- **{lab}:** ' + items_table(k, 14))
    open(out_path, 'w', encoding='utf-8').write('\n'.join(o) + '\n')
    print('ok', out_path, len(o))


main()
