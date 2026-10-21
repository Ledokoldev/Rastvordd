# Макет РТ «Подстанция» v2 из игровых пиксельных спрайтов: 32 px/м, высоты ×0.75, тени от силуэта по свету.
from PIL import Image, ImageDraw, ImageFilter, ImageChops
import random, os
A = 'C:/Users/Xbaza/Desktop/Rastvordd/game/art/'
OUT = 'C:/Users/Xbaza/Desktop/Rastvordd/docs/design/img/'
PPM, HS = 32, 0.75
OX, OY, WW, WH = 7, 7, 106, 88
_cache = {}
def L(n):
    if n not in _cache:
        p = A + n + '.png'
        _cache[n] = Image.open(p).convert('RGBA') if os.path.exists(p) else None
    return _cache[n]
img = Image.new('RGBA', (WW * PPM, WH * PPM), (0, 0, 0, 255))
def px(x, y): return int((x + OX) * PPM), int((y + OY) * PPM)

def tile(name, x0, y0, x1, y1, mask=None):
    t = L('tiles/' + name); a, b = px(x0, y0); c, d = px(x1, y1)
    reg = Image.new('RGBA', (c - a, d - b))
    for i in range(0, c - a, t.width):
        for j in range(0, d - b, t.height): reg.paste(t, (i, j))
    img.paste(reg, (a, b), mask.crop((a, b, c, d)) if mask else None)

def soft_mask(poly, blur=10):
    m = Image.new('L', img.size, 0); ImageDraw.Draw(m).polygon([px(*p) for p in poly], fill=255)
    return m.filter(ImageFilter.GaussianBlur(blur))

tile('ground_grass', -OX, -OY, WW - OX, WH - OY)
terr = [(0, 14), (50, 14), (50, 0), (92, 0), (92, 72), (0, 72)]
tile('ground_dirt', -OX, -OY, WW - OX, WH - OY, soft_mask(terr, 3))
tile('ground_gravel', -OX, -OY, WW - OX, WH - OY, soft_mask([(50, 3), (90, 3), (90, 45), (42, 45), (42, 16), (50, 16)], 6))       # ОРУ
for (x0, y0, x1, y1) in [(44, 25.5, 90, 26.5)] + [(bx + 3.5, 12, bx + 4.5, 44) for bx in (52, 66, 80)]: tile('ground_concrete', x0, y0, x1, y1)   # кабельные лотки
for bx in (52, 66, 80): tile('ground_concrete', bx - 3.5, 35, bx + 3.5, 40)   # фундаменты трансформаторов
for bx in (52, 66, 80): tile('ground_concrete', bx - 2, 20, bx + 2, 23)
tile('ground_asphalt', -OX, -OY, WW - OX, WH - OY, soft_mask([(10, 48), (40, 48), (40, 70), (19, 70), (19, 88), (9, 88), (9, 70), (2, 70), (2, 60), (10, 60)], 4))
tile('ground_concrete', -OX, -OY, WW - OX, WH - OY, soft_mask([(52, 48), (78, 48), (78, 70), (52, 70)], 4))  # площадка мастерской
tile('ground_gravel', -OX, -OY, WW - OX, WH - OY, soft_mask([(40, 50), (51, 50), (51, 70), (40, 70)], 8))
tile('floor_tile', 6, 20, 34, 34); tile('floor_tile', 6, 34, 18, 46)
tile('ground_concrete', 54, 52, 76, 64)

random.seed(5)
def dec(n, x, y, s=1.0):
    d = L(n)
    if d is None: return
    if s != 1: d = d.resize((int(d.width * s), int(d.height * s)), Image.NEAREST)
    a, b = px(x, y); img.alpha_composite(d, (a - d.width // 2, b - d.height // 2))
for (x, y) in [(53, 40.6), (67, 40.8), (79, 40.5), (80, 67), (46, 42), (64, 58)]: dec('decals/decal_oil_stain', x, y, 1.4)
for x in range(20, 44, 2): dec('decals/decal_cables', x + random.uniform(-.3, .3), 47.5 + random.uniform(-.3, .3))   # кабели от ОПУ к ОРУ
for y in range(30, 46, 2): dec('decals/decal_cables', 40 + random.uniform(-.3, .3), y)
for _ in range(110):
    x, y = random.uniform(1, 91), random.uniform(15, 71)
    if 6 <= x <= 34 and 20 <= y <= 46: continue
    dec(random.choice(['decals/decal_grass_tuft_1', 'decals/decal_grass_tuft_2', 'decals/decal_pebbles', 'decals/decal_debris', 'decals/decal_leaves']), x, y)
for (x, y) in [(30, 58), (60, 46), (86, 50), (14, 52)]: dec('decals/decal_puddle', x, y, 1.6)

objs = []
import json
FOOT = {a['id']: a.get('foot') for a in json.load(open('C:/Users/Xbaza/Desktop/Rastvordd/assets/manifest.json', encoding='utf-8'))['assets']}
HIT = []   # хитбоксы для отладочной картинки
LIGHT = (0.55, -0.35)   # тень: от источника сверху-слева падает вправо и немного «назад» (вверх по экрану)

def shadow_of(spr, flat=0.42):
    """Тень от силуэта: альфа, сплющенная по высоте и скошенная вправо; низ тени совпадает с низом спрайта."""
    a = spr.getchannel('A').point(lambda v: 255 if v > 100 else 0)
    w, h = a.size
    sh_h = max(2, int(h * flat))
    a = a.resize((w, sh_h), Image.NEAREST)
    shear = int(sh_h * 1.1)
    out = Image.new('L', (w + shear, sh_h), 0)
    for row in range(sh_h):
        off = int(shear * (1 - row / sh_h))
        out.paste(a.crop((0, row, w, row + 1)), (off, row))
    out = out.filter(ImageFilter.GaussianBlur(1.2)).point(lambda v: int(v * 0.42))
    s = Image.new('RGBA', out.size, (12, 14, 10, 255)); s.putalpha(out)
    return s

def wall(x0, y0, x1, y1, h, tex, t=0.3):
    if x0 == x1: x0, x1 = x0 - t / 2, x1 + t / 2
    if y0 == y1: y0, y1 = y0 - t / 2, y1 + t / 2
    def draw():
        H = int(h * PPM * HS); a, b = px(x0, y0); c, d = px(x1, y1)
        w = max(1, c - a)
        # тень стены на землю справа-сверху
        sh = Image.new('RGBA', (w + H // 2, max(1, d - b) + 4), (12, 14, 10, 90))
        img.alpha_composite(sh, (a + 3, b - 2)) if False else None
        face = L('walls/' + tex) or L('walls/wallface_shutter'); f = Image.new('RGBA', (w, H))
        for i in range(0, w, face.width):
            for j in range(0, H, face.height): f.paste(face, (i, j))
        img.alpha_composite(f, (a, d - H))
        avg = face.resize((1, 1)).getpixel((0, 0))
        top = Image.new('RGBA', (w, max(1, d - b)), tuple(int(v * 0.5) for v in avg[:3]) + (255,))
        img.alpha_composite(top, (a, b - H))
        ImageDraw.Draw(img).rectangle([a, b - H, a + w - 1, b - H + max(1, d - b) - 1], outline=(18, 18, 16, 255))
    objs.append((y1, draw))

def prop(n, x, y, flip=False, shadow=True):
    s = L(n)
    if s is None: return
    if flip: s = s.transpose(Image.FLIP_LEFT_RIGHT)
    def draw():
        a, b = px(x, y)
        img.alpha_composite(s, (a - s.width // 2, b - s.height))
    if shadow:
        sh = shadow_of(s)
        a, b = px(x, y)
        objs.append((-1, lambda sh=sh, a=a, b=b: img.alpha_composite(sh, (a - s.width // 2, b - sh.height))))
    f = FOOT.get(n.split('/')[-1])
    if isinstance(f, dict):
        w, d, h = f['legs']
        HIT.append((x - w / 2, y - d, x - w / 2 + d, y, h)); HIT.append((x + w / 2 - d, y - d, x + w / 2, y, h))
    elif f:
        w, d, h = f
        HIT.append((x - w / 2, y - d, x + w / 2, y, h))
    objs.append((y, draw))

F, B, SH, G = 'wallface_fence_po2', 'wallface_brick', 'wallface_shutter', 'wallface_garage'
# забор с проходами: ворота (юг), калитка (восток), пролом под упавшей опорой (север)
for s in [((0, 14), (50, 14)), ((50, 0), (50, 14)), ((50, 0), (70, 0)), ((77, 0), (92, 0)), ((92, 0), (92, 57)), ((92, 61), (92, 72)),
          ((0, 72), (9, 72)), ((19, 72), (92, 72)), ((0, 14), (0, 72))]:
    wall(*s[0], *s[1], 2.2, F)
# ОПУ
for s in [((6, 20), (34, 20)), ((6, 20), (6, 46)), ((34, 20), (34, 28)), ((34, 33), (34, 34)), ((18, 34), (34, 34)), ((18, 34), (18, 41)), ((18, 43), (18, 46)),
          ((6, 46), (11.4, 46)), ((12.6, 46), (18, 46)), ((18, 20), (18, 22.4)), ((18, 23.6), (18, 34)), ((18, 27), (34, 27)), ((6, 34), (11.4, 34)), ((12.6, 34), (18, 34))]:
    wall(*s[0], *s[1], 2.8, B)
wall(34, 28, 34, 33, 2.8, SH, 0.35)
# мастерская с гаражными воротами
for s in [((54, 52), (76, 52)), ((54, 52), (54, 56)), ((54, 57.2), (54, 64)), ((76, 52), (76, 64)), ((54, 64), (57, 64)), ((63, 64), (66, 64)), ((72, 64), (76, 64))]:
    wall(*s[0], *s[1], 3.0, B)
wall(57, 64, 63, 64, 3.0, G); wall(66, 64, 72, 64, 3.0, G)
# огнестены между трансформаторами
for x in [59, 73]: wall(x, 30, x, 42, 3.4, B, 1.0)

# ОРУ
BAYS = [52, 66, 80]
for x in BAYS: prop('props/subst_portal', x, 10)
for x in BAYS: prop('props/subst_disconnector', x, 17)
for x in BAYS: prop('props/subst_breaker', x, 22.5)
for x in BAYS: prop('props/subst_transformer', x, 39)
for x in BAYS: prop('props/subst_relay_cabinet', x + 5, 43.5)
prop('props/subst_ktp', 88, 24); prop('props/subst_insulator_pallet', 88, 36)
for x in BAYS: prop('props/subst_disconnector', x, 30.5)
for x in BAYS: prop('props/subst_relay_cabinet', x - 5, 25)
prop('props/subst_oil_tank', 45, 44)
prop('props/subst_pylon_fallen', 73, 6)
for (x, y) in [(53, 4), (90, 44)]: prop('props/subst_lamp', x, y)
# двор
for (x, y) in [(42, 53), (46, 55.5), (43, 59), (48, 62), (44.5, 66)]: prop('props/subst_cable_drum', x, y, random.random() < .5)
prop('props/subst_reel_stack', 49, 68); prop('props/subst_insulator_pallet', 41, 69.5); prop('props/subst_insulator_pallet', 50, 52)
prop('props/subst_truck', 82, 68.5); prop('props/subst_ktp', 86, 56); prop('props/subst_oil_tank', 84, 49)
prop('props/subst_kpp', 5, 69.5); prop('props/subst_lamp', 21, 49)
for (x, y) in [(41, 47), (90, 30), (52, 46), (8, 16.5)]: prop('clutter/subst_sign', x, y)
# мастерская внутри
prop('props/workbench', 60, 54.2); prop('props/int_shelf', 66, 52.6); prop('props/int_shelf', 70, 52.6); prop('props/subst_reel_stack', 73, 58)
prop('props/barrel_oil', 56, 55); prop('props/barrel_oil', 56.8, 55.4)
# ОПУ внутри
prop('props/subst_switchgear', 22, 20.6); prop('props/subst_switchgear', 27.2, 20.6); prop('props/subst_main_panel', 32, 20.6)
prop('props/int_shelf', 21, 27.6); prop('props/crate_punch', 26, 31.8); prop('props/subst_insulator_pallet', 31, 33.4)
prop('props/int_lockers', 9, 20.6); prop('props/int_desk', 11, 26); prop('props/int_table', 14, 30.5)
prop('props/int_cot', 8, 38); prop('props/int_burzhuika', 16, 35.2); prop('props/int_table', 12, 42); prop('props/int_desk', 9, 45)
# мелочь
for (x, y) in [(2, 16), (90, 2), (90, 70), (36, 70), (2, 50), (64, 70), (52.5, 30), (3, 30)]: prop('props/barrel_oil', x, y)
for (x, y) in [(36, 40), (80, 49), (24, 62)]: prop('props/junk_pile', x, y)
for (x, y) in [(28, 54), (60, 47), (88, 63)]: prop('props/crate_military', x, y)
CL = ['clutter/clutter_tire', 'clutter/clutter_pallet', 'clutter/clutter_planks', 'clutter/clutter_canister', 'clutter/clutter_bucket', 'clutter/clutter_cone']
for (x, y) in [(37, 52), (38, 64), (51, 48), (78, 56), (88, 47), (24, 58), (31, 66), (4, 56), (45, 46), (86, 18), (36, 30), (26, 48)]: prop(random.choice(CL), x, y)
for (x, y) in [(13, 55), (17, 55)]: prop('props/concrete_barrier', x, y)
prop('props/car_wreck', 26, 79)
prop('props/car_wreck', 15, 62, True); prop('props/car_wreck', 30, 57)
for (x, y) in [(24, 66), (27, 66), (35, 60)]: prop('props/concrete_barrier', x, y)
for (x, y) in [(3, 63), (4, 64.2), (3.2, 65.4), (5, 63.2)]: prop('props/barrel_oil', x, y)
prop('props/subst_reel_stack', 33, 69); prop('props/subst_insulator_pallet', 22, 60)
prop('props/int_lockers', 11, 20.6); prop('props/int_lockers', 13, 20.6); prop('props/int_shelf', 7.2, 31); prop('props/int_desk', 15, 26)
for (x, y) in [(1.5, 40), (38, 71), (91, 40), (51, 15.5), (40, 18), (91, 8), (20, 71), (70, 71)]: prop('plants/bush_1', x, y)
prop('chars/char_guard_f0', 66, 30); prop('chars/char_guard_f1', 12, 28)
# лес вокруг
TR = ['trees/tree_pine_1', 'trees/tree_pine_2', 'trees/tree_birch', 'trees/tree_leafy', 'trees/tree_oak_dead', 'plants/bush_1']
for _ in range(900):
    x, y = random.uniform(-8, 100), random.uniform(-9, 82)
    if (-2 < x < 94 and -2 < y < 74) or (6 < x < 22 and y > 70): continue
    if random.random() < 0.82: continue
    prop(random.choice(TR), x, y, random.random() < 0.5)

for _, f in sorted(objs, key=lambda o: o[0]): f()

# провода: портал → разъединитель → выключатель → разъединитель → трансформатор (рисуются поверх, это высоко)
def wire(p0, h0, p1, h1, sag=0.5):
    d = ImageDraw.Draw(img)
    a = px(*p0); b = px(*p1); a = (a[0], a[1] - int(h0 * PPM * HS)); b = (b[0], b[1] - int(h1 * PPM * HS))
    pts = []
    for i in range(13):
        t = i / 12; sg = sag * PPM * 4 * t * (1 - t)
        pts.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t + sg))
    d.line(pts, fill=(28, 26, 22, 255), width=1)
for bx in BAYS:
    for k in (-1.3, 0, 1.3):
        wire((bx + k, 10), 5.5, (bx + k * .5, 17), 2.5, .6)
        wire((bx + k * .5, 17), 2.5, (bx + k * .6, 22.5), 2.8, .2)
        wire((bx + k * .6, 22.5), 2.8, (bx + k * .5, 30.5), 2.5, .3)
        wire((bx + k * .5, 30.5), 2.5, (bx + k * .6, 39), 3.8, .3)
for i in range(3): wire((45, 9.6 + i * .5), 5.9, (92, 9.6 + i * .5), 5.9, 1.0)
for i in range(3): wire((73 + i * .6, -8), 7, (73 + i * .6, 9.6), 5.9, .8)

img = img.convert('RGB')
img.save(OUT + 'subst-mock.png')
hb = img.copy(); dd = ImageDraw.Draw(hb, 'RGBA')
for (x0, y0, x1, y1, h) in HIT:
    col = (255, 60, 60, 110) if h >= 1.8 else (255, 210, 60, 110)
    dd.rectangle([px(x0, y0), px(x1, y1)], fill=col, outline=col[:3] + (255,))
hb.save(OUT + 'subst-mock-hitbox.png')
c = img.crop(px(4, 16) + px(46, 50)); c.resize((c.width * 2, c.height * 2), Image.NEAREST).save(OUT + 'subst-mock-zoom.png')
print(img.size)
