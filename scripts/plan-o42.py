# План-схема уровня −1 «Объекта 42» (без спрайтов): контур «материка», коридоры, комнаты по типам, районы.
# Сначала утверждаем план, потом по нему рисуем макет из игровых спрайтов (scripts/mock-o42.py).
# Запуск: python scripts/plan-o42.py [seed]  →  docs/design/img/o42-l1-plan.png
import os, random, sys
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, 'docs', 'design', 'img', 'o42-l1-plan.png')
SEED = int(sys.argv[1]) if len(sys.argv) > 1 else 42
rng = random.Random(SEED)
W, H, S, M = 232, 166, 8, 6          # м, пикселей на метр, поле
CW = 4                                # ширина коридора

CONT = [(x * 1.1, y * 1.1) for x, y in [(16, 34), (30, 16), (52, 12), (66, 24), (88, 10), (116, 16), (134, 6), (162, 12), (184, 26), (200, 50), (190, 64),
        (204, 86), (186, 104), (166, 106), (156, 128), (130, 140), (112, 126), (94, 138), (72, 132), (58, 116), (40, 122),
        (22, 108), (8, 88), (18, 66), (6, 50)]]


def inside(x, y):
    c = False
    for (ax, ay), (bx, by) in zip(CONT, CONT[1:] + CONT[:1]):
        if (ay > y) != (by > y) and x < (bx - ax) * (y - ay) / (by - ay) + ax:
            c = not c
    return c


DIST = {   # центр района (м) и смесь типов комнат
    'barracks': ((44, 78), {'barracks': 6, 'washroom': 2, 'storage': 1, 'office': 1}),
    'mess': ((110, 28), {'mess': 3, 'kitchen': 3, 'storage': 2, 'washroom': 1}),
    'staff': ((120, 80), {'office': 4, 'comms': 3, 'archive': 2, 'barracks': 1}),
    'armory': ((104, 124), {'armory': 4, 'storage': 3, 'workshop': 2}),
    'tech': ((196, 92), {'engine': 4, 'workshop': 3, 'storage': 2, 'electrical': 2}),
    'med': ((176, 34), {'medbay': 4, 'storage': 1, 'office': 1, 'washroom': 1}),
    'depot': ((164, 128), {'storage': 5, 'workshop': 2, 'engine': 1}),
}
DIST_RU = {'barracks': 'КАЗАРМЫ', 'mess': 'СТОЛОВАЯ И КУХНИ', 'staff': 'ШТАБ И СВЯЗЬ', 'armory': 'ОРУЖЕЙНЫЙ ДВОР',
           'tech': 'ДИЗЕЛЬНЫЕ', 'med': 'МЕДЧАСТЬ', 'depot': 'СКЛАДЫ'}
KIND = {   # цвет и подпись
    'barracks': ((96, 128, 84), 'кубрик'), 'washroom': ((120, 170, 190), 'душ'), 'storage': ((120, 110, 92), 'склад'),
    'office': ((150, 132, 100), 'канц.'), 'mess': ((170, 120, 70), 'столовая'), 'kitchen': ((190, 150, 90), 'кухня'),
    'comms': ((90, 120, 170), 'связь'), 'archive': ((130, 100, 80), 'архив'), 'armory': ((170, 70, 60), 'оружейка'),
    'workshop': ((140, 140, 140), 'мастерская'), 'engine': ((110, 100, 130), 'дизель'), 'electrical': ((150, 140, 60), 'щитовая'),
    'medbay': ((200, 200, 210), 'медпункт'), 'airlock': ((230, 190, 40), 'ШЛЮЗ'), 'stairs': ((80, 180, 230), 'НА −2'),
}

# сетка «кирпичом»: горизонтальные коридоры каждые 20 м, вертикальные — через ~26 м, в соседних полосах со сдвигом;
# комната ставится, только если целиком внутри контура — край уровня идёт по «берегу», а не прямоугольником
HY = list(range(4, H - 10, 20))
rooms, doors = [], []
hspans = {j: [] for j in range(len(HY))}          # куски горизонтальных коридоров: линия j → [(x0, x1)]
vsegs = []                                         # вертикальные: (x, y0, y1)


def pick(weights):
    tot = sum(weights.values()); v = rng.random() * tot
    for k, w in weights.items():
        v -= w
        if v <= 0:
            return k
    return k


def ok(x0, y0, x1, y1):
    return all(inside(x, y) for x, y in [(x0 + .5, y0 + .5), (x1 - .5, y0 + .5), (x0 + .5, y1 - .5), (x1 - .5, y1 - .5), ((x0 + x1) / 2, (y0 + y1) / 2)])


for j in range(len(HY) - 1):
    by0, by1 = HY[j] + CW, HY[j + 1]
    xs, x = [], 2 + (13 if j % 2 else 0) + rng.uniform(-3, 3)
    while x < W - 2:
        xs.append(x); x += rng.uniform(22, 30)
    xs = [2] + xs if xs[0] > 8 else xs
    for bx_a, bx_b in zip(xs, xs[1:]):
        bx0, bx1 = bx_a + CW, bx_b                  # слева от квартала — вертикальный коридор
        dist = min(DIST, key=lambda d: (DIST[d][0][0] - (bx0 + bx1) / 2) ** 2 + (DIST[d][0][1] - (by0 + by1) / 2) ** 2)
        mix = DIST[dist][1]
        big = rng.random() < 0.12 and ok(bx0, by0, bx1, by1)
        mid = by0 + (by1 - by0) * rng.uniform(0.4, 0.6)
        rows = [(by0, by1, 'both')] if big else [(by0, mid, 'n'), (mid, by1, 's')]
        placed = False
        for (ry0, ry1, face) in rows:
            cuts = [bx0]
            if not big:
                while cuts[-1] < bx1 - 6:
                    cuts.append(min(bx1, cuts[-1] + rng.uniform(4.5, 8.5)))
                if bx1 - cuts[-1] < 3.5: cuts[-1] = bx1
                elif cuts[-1] != bx1: cuts.append(bx1)
            else:
                cuts.append(bx1)
            for (rx0, rx1) in zip(cuts, cuts[1:]):
                if not ok(rx0, ry0, rx1, ry1):
                    continue
                kind = ({'mess': 'mess', 'tech': 'engine', 'depot': 'storage', 'armory': 'armory'}.get(dist) or pick(mix)) if big else pick(mix)
                rooms.append([kind, rx0, ry0, rx1, ry1])
                placed = True
                if face in ('n', 'both'): doors.append((rng.uniform(rx0 + 1.5, rx1 - 1.5), ry0, True)); hspans[j].append((rx0, rx1))
                if face in ('s', 'both'): doors.append((rng.uniform(rx0 + 1.5, rx1 - 1.5), ry1, True)); hspans[j + 1].append((rx0, rx1))
                if rx0 > bx0 + 0.1 and rng.random() < 0.3: doors.append((rx0, (ry0 + ry1) / 2, False))
        if placed and inside(bx_a + CW / 2, (by0 + by1) / 2):
            vsegs.append((bx_a, by0, by1, j))


def merge(sp):
    out = []
    for a, b in sorted(sp):
        if out and a <= out[-1][1] + 4.5: out[-1] = (out[-1][0], max(out[-1][1], b))
        else: out.append((a, b))
    return out


# вертикальный коридор цепляется только к тем линиям, где есть комнаты (иначе — хвост в скалу)
for (x, y0, y1, j) in vsegs:
    for jj in (j, j + 1):
        if hspans[jj]: hspans[jj].append((x, x + CW))
vsegs = [(x, y0 - (CW if hspans[j] else 0), y1 + (CW if hspans[j + 1] else 0)) for (x, y0, y1, j) in vsegs]
hspans = {j: merge(v) for j, v in hspans.items()}


def extreme(key):
    return max((r for r in rooms if 7 <= r[3] - r[1] <= 13), key=key)


extreme(lambda r: -r[1])[0] = 'airlock'
extreme(lambda r: r[3])[0] = 'airlock'
extreme(lambda r: r[4] - abs((r[1] + r[3]) / 2 - 105) * 0.3)[0] = 'airlock'
min((r for r in rooms if r[0] != 'airlock'), key=lambda r: ((r[1] + r[3]) / 2 - 105) ** 2 + ((r[2] + r[4]) / 2 - 75) ** 2)[0] = 'stairs'

# ---------- рисунок ----------
img = Image.new('RGB', ((W + 2 * M) * S, (H + 2 * M) * S + 150), (28, 26, 24))
d = ImageDraw.Draw(img)
P = lambda x, y: ((x + M) * S, (y + M) * S + 60)
font = lambda sz, b=False: ImageFont.truetype('C:/Windows/Fonts/' + ('segoeuib.ttf' if b else 'segoeui.ttf'), sz)
d.polygon([P(x, y) for x, y in CONT], outline=(90, 84, 76), fill=(40, 38, 35))               # контур «материка» (скала)
for j, sp in hspans.items():
    for (x0, x1) in sp:
        d.rectangle([P(x0, HY[j]), P(x1, HY[j] + CW)], fill=(72, 92, 70))
for (x, y0, y1) in vsegs:
    d.rectangle([P(x, y0), P(x + CW, y1)], fill=(72, 92, 70))
for (kind, x0, y0, x1, y1) in rooms:
    d.rectangle([P(x0, y0), P(x1, y1)], fill=KIND[kind][0], outline=(20, 18, 16), width=2)
    lab = KIND[kind][1]
    f = font(13, kind in ('airlock', 'stairs'))
    if d.textlength(lab, font=f) < (x1 - x0) * S - 4:
        cx, cy = P((x0 + x1) / 2, (y0 + y1) / 2)
        d.text((cx, cy), lab, font=f, fill=(15, 14, 12), anchor='mm')
for (x, y, hz) in doors:
    a, b = P(x, y)
    d.rectangle([a - 6, b - 2, a + 6, b + 2] if hz else [a - 2, b - 6, a + 2, b + 6], fill=(235, 230, 215))
for name, ((x, y), _) in DIST.items():
    a, b = P(x, y); t = DIST_RU[name]; f = font(26, True); tw = d.textlength(t, font=f)
    d.rounded_rectangle([a - tw / 2 - 10, b - 20, a + tw / 2 + 10, b + 20], 8, fill=(0, 0, 0, 0), outline=(240, 236, 224), width=2)
    d.text((a, b), t, font=f, fill=(240, 236, 224), anchor='mm')
d.text((20, 14), f'«Объект 42» · уровень −1 · план · {W}×{H} м · {len(rooms)} помещений · коридоры 4 м',
       font=font(28, True), fill=(240, 236, 224))
# легенда
x, y = 20, img.height - 80
for k in ['barracks', 'washroom', 'mess', 'kitchen', 'office', 'comms', 'archive', 'armory', 'storage', 'workshop', 'engine', 'electrical', 'medbay', 'airlock', 'stairs']:
    col, lab = KIND[k]
    d.rectangle([x, y, x + 22, y + 22], fill=col, outline=(20, 18, 16))
    d.text((x + 28, y - 2), lab, font=font(18), fill=(225, 220, 210))
    x += 40 + d.textlength(lab, font=font(18))
    if x > img.width - 160:
        x, y = 20, y + 34
d.rectangle([20, img.height - 36, 42, img.height - 14], fill=(72, 92, 70)); d.text((48, img.height - 38), 'коридор', font=font(18), fill=(225, 220, 210))
d.rectangle([140, img.height - 30, 152, img.height - 26], fill=(235, 230, 215)); d.text((160, img.height - 38), 'дверь', font=font(18), fill=(225, 220, 210))
img.save(OUT)
print('ok', img.size, 'rooms', len(rooms))
