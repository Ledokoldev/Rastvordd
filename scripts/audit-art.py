# Аудит мировых ассетов: почему картинка «не из одного мира». Меряет у каждого спрайта
# размер «пикселя» (апскейл ×1/×2/×3/×4), насыщенность, яркость, контраст, тёмный контур, число цветов
# и находит выбросы относительно медианы всего набора. Персонажи, иконки, UI не трогает.
# Запуск: python scripts/audit-art.py  → docs/art-audit.csv, docs/design/img/art-audit-*.png
import csv, colorsys, glob, os, statistics as st
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ART = os.path.join(ROOT, 'game', 'art')
DIRS = ['building', 'clutter', 'containers', 'furniture', 'grass', 'interior', 'loot', 'nodes', 'outdoor', 'plants',
        'props', 'rooftop', 'trees', 'vehicles', 'walls', 'decals', 'tiles']
OUT = os.path.join(ROOT, 'docs', 'design', 'img')


def block_size(im):
    """Самый крупный «пиксель» s (1–4), при котором спрайт почти не меняется от уменьшения и обратного увеличения."""
    w, h = im.size
    best = 1
    for s in (2, 3, 4):
        if w < s * 8 or h < s * 8: break
        ok = False
        for ox in range(s):
            for oy in range(s):
                c = im.crop((ox, oy, ox + (w - ox) // s * s, oy + (h - oy) // s * s))
                back = c.resize((c.width // s, c.height // s), Image.NEAREST).resize(c.size, Image.NEAREST)
                a, b = c.tobytes(), back.tobytes()
                diff = sum(1 for i in range(0, len(a), 4 * 7) if abs(a[i] - b[i]) > 24 or abs(a[i + 3] - b[i + 3]) > 64)
                if diff / max(1, len(a) // (4 * 7)) < 0.04: ok = True; break
            if ok: break
        if ok: best = s
    return best


def stats(path):
    im = Image.open(path).convert('RGBA')
    if im.width * im.height > 512 * 512: im = im.resize((im.width // 2, im.height // 2), Image.NEAREST)
    px = list(im.getdata())
    op = [p for p in px if p[3] > 128]
    if len(op) < 30: return None
    S, V = [], []
    cols = set()
    for r, g, b, a in op[::max(1, len(op) // 4000)]:
        h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
        S.append(s); V.append(v); cols.add((r >> 4, g >> 4, b >> 4))
    # тёмный контур: доля почти чёрных пикселей на границе альфы
    w, hh = im.size; al = im.getchannel('A').load(); pp = im.load(); edge = dark = 0
    for y in range(1, hh - 1, 2):
        for x in range(1, w - 1, 2):
            if al[x, y] > 128 and (al[x - 1, y] < 64 or al[x + 1, y] < 64 or al[x, y - 1] < 64 or al[x, y + 1] < 64):
                edge += 1; r, g, b, _ = pp[x, y]
                if max(r, g, b) < 50: dark += 1
    return dict(px=block_size(im), sat=st.mean(S), val=st.mean(V), con=st.pstdev(V), outline=dark / max(1, edge),
                colors=len(cols), w=im.width, h=im.height)


rows = []
for d in DIRS:
    for p in sorted(glob.glob(os.path.join(ART, d, '*.png'))):
        try: s = stats(p)
        except Exception as e: print('!', p, e); continue
        if s: rows.append(dict(dir=d, name=os.path.basename(p)[:-4], **s))
print('ассетов', len(rows))

world = [r for r in rows if r['dir'] not in ('decals', 'tiles')]
med = {k: st.median(r[k] for r in world) for k in ('sat', 'val', 'con', 'outline')}
mad = {k: st.median(abs(r[k] - med[k]) for r in world) or 0.01 for k in med}
pxmode = st.mode(r['px'] for r in world)
for r in rows:
    z = {k: (r[k] - med[k]) / (1.4826 * mad[k]) for k in med}
    r.update({f'z_{k}': round(v, 2) for k, v in z.items()})
    why = []
    if r['px'] != pxmode: why.append(f'пиксель ×{r["px"]} (у большинства ×{pxmode})')
    if z['sat'] > 2.5: why.append('кислотный')
    if z['sat'] < -2.5: why.append('серый')
    if z['val'] > 2.5: why.append('светлый')
    if z['val'] < -2.5: why.append('тёмный')
    if z['con'] > 2.5: why.append('контрастный')
    if z['con'] < -2.5: why.append('плоский')
    if z['outline'] > 2.5: why.append('чёрный контур')
    r['why'] = '; '.join(why)
    r['score'] = round(sum(max(0, abs(v) - 2) for v in z.values()) + (2 if r['px'] != pxmode else 0), 2)

with open(os.path.join(ROOT, 'docs', 'art-audit.csv'), 'w', newline='', encoding='utf-8-sig') as f:
    wr = csv.DictWriter(f, fieldnames=list(rows[0].keys())); wr.writeheader()
    for r in sorted(rows, key=lambda r: -r['score']): wr.writerow(r)

# сводка по папкам
print(f'медиана: насыщ {med["sat"]:.2f} ярк {med["val"]:.2f} контр {med["con"]:.2f} контур {med["outline"]:.2f}; пиксель ×{pxmode}')
for d in DIRS:
    rs = [r for r in rows if r['dir'] == d]
    if not rs: continue
    pxs = {k: sum(1 for r in rs if r['px'] == k) for k in (1, 2, 3, 4)}
    print(f'{d:11s} {len(rs):4d}  насыщ {st.median(r["sat"] for r in rs):.2f} ярк {st.median(r["val"] for r in rs):.2f} '
          f'контр {st.median(r["con"] for r in rs):.2f} контур {st.median(r["outline"] for r in rs):.2f}  пиксели {pxs}  выбросов {sum(1 for r in rs if r["why"])}')

# контактный лист худших выбросов
try: fnt = ImageFont.truetype('arial.ttf', 12)
except Exception: fnt = ImageFont.load_default()
bad = [r for r in sorted(rows, key=lambda r: -r['score']) if r['why']][:48]
C = 160; sheet = Image.new('RGB', (8 * C, ((len(bad) + 7) // 8) * (C + 30)), (58, 62, 50)); d = ImageDraw.Draw(sheet)
for i, r in enumerate(bad):
    im = Image.open(os.path.join(ART, r['dir'], r['name'] + '.png')).convert('RGBA'); im.thumbnail((C - 8, C - 8), Image.NEAREST)
    x, y = i % 8 * C, i // 8 * (C + 30)
    sheet.paste(im, (x + (C - im.width) // 2, y + (C - im.height) // 2), im)
    d.text((x + 3, y + C), f'{r["dir"]}/{r["name"]}'[:26], font=fnt, fill=(235, 230, 215))
    d.text((x + 3, y + C + 14), r['why'][:28], font=fnt, fill=(255, 170, 120))
sheet.save(os.path.join(OUT, 'art-audit-outliers.png'))
print('выбросов всего', sum(1 for r in rows if r['why']))
