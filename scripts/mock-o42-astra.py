# Превью «Объекта 42» (уровень −1) из ассетов Астры: план «материка» (scripts/plan-o42.py) + её железобетонные стены/полы (o42_*)
# и пропы по типу комнаты, аварийный свет. Берёт графику из рабочей копии ветки astra-lab. Только картинка.
# Запуск: python scripts/mock-o42-astra.py [x0 y0 x1 y1]  → docs/design/img/o42-astra-preview*.png
import importlib.util, json, glob, os, random, re, sys
from PIL import Image, ImageDraw, ImageFilter, ImageChops

WT = r'C:\Users\Xbaza\Rastvordd-wt\astra'
HERE = os.path.dirname(os.path.abspath(__file__))


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m); return m


mt = load('mt2', os.path.join(WT, 'scripts', 'mock-tier2.py'))       # движок макетов, графика — из копии Астры
plan = load('plan', os.path.join(HERE, 'plan-o42.py'))               # комнаты и коридоры «материка»
Scene, PPM, L, font = mt.Scene, mt.PPM, mt.L, mt.font
OUT = os.path.join(os.path.dirname(HERE), 'docs', 'design', 'img')

X0, Y0, X1, Y1 = (float(v) for v in sys.argv[1:5]) if len(sys.argv) >= 5 else (58, 24, 178, 104)
rng = random.Random(42)
W, H = X1 - X0, Y1 - Y0
s = Scene('«Объект 42»', W, H, M=2, seed=42)
sx = lambda x: x - X0
sy = lambda y: y - Y0

ART = os.path.join(WT, 'game', 'art')
walls = sorted(os.path.basename(p)[:-4] for p in glob.glob(os.path.join(ART, 'walls', 'o42_wall_*.png')))
floors = sorted(os.path.basename(p)[:-4] for p in glob.glob(os.path.join(ART, 'tiles', 'o42_floor_*.png')))
pick = lambda lst, pat, fb: [w for w in lst if re.search(pat, w)] or fb
W_CORR = pick(walls, r'shotcrete|spall|rebar|formcast|shutterboard|efflorescence|calcite|form_tie|cold_pour', walls)
W_ROOM = pick(walls, r'green|oil|paint|enamel|ochre|limewash|red_primer|roller|stencil', walls)
F_CORR = pick(floors, r'broom|aggregate|cart|boot|drain|puddle|spalled|repair|roller', floors)
F_ROOM = {k: pick(floors, p, floors) for k, p in {
    'barracks': r'crazed|enamel|roller|paint|checker', 'washroom': r'ceramic|drain|sump|puddle', 'storage': r'pallet|fork|cart|drum|scuff',
    'office': r'polished|terrazzo|checker|enamel', 'mess': r'polished|roller|checker', 'kitchen': r'ceramic|drain|acid', 'comms': r'polished|rubber',
    'archive': r'polished|rubber|roller', 'armory': r'antislip|rubber|metal', 'workshop': r'oil|grinding|machine|metal', 'engine': r'oil|drum|battery|machine',
    'electrical': r'rubber|battery|cable', 'medbay': r'ceramic|crazed|enamel', 'airlock': r'antislip|red_|metal', 'stairs': r'antislip|metal'}.items()}

# пропы Астры по ключевым словам (+ старые obj_* бункерные)
props = []
for f in glob.glob(os.path.join(WT, 'assets', 'manifest_astra_*.json')):
    try:
        man = json.load(open(f, encoding='utf-8-sig'))
    except Exception:
        continue
    for a in man.get('assets', []):
        if a.get('cat') in ('props', 'furniture', 'containers') and os.path.exists(os.path.join(ART, a['cat'], a['id'] + '.png')):
            props.append((a['cat'] + '/' + a['id'], (a['id'] + ' ' + a.get('desc', '')).lower()))
for p in glob.glob(os.path.join(ART, 'props', 'obj_*.png')):
    props.append(('props/' + os.path.basename(p)[:-4], os.path.basename(p).lower()))
KW = {'barracks': r'bunk|locker|dorm|footlocker|bed|cot', 'washroom': r'shower|wash|sink|toilet|basin', 'storage': r'crate|shelf|rack|pallet|barrel|drum',
      'office': r'desk|cabinet|safe|typewriter|phone|chair', 'mess': r'mess|table|serving|samovar|bench|canteen', 'kitchen': r'kitchen|stove|pot|cooker|fridge|freezer',
      'comms': r'radio|server|console|switchboard|telephone|teleprinter', 'archive': r'archive|file|shelf|cabinet|microfilm|card',
      'armory': r'weapon|ammo|armory|rifle|gun|cage|magazine', 'workshop': r'lathe|bench|tool|vice|drill|welding|press',
      'engine': r'diesel|generator|pump|fuel|engine|compressor|tank', 'electrical': r'switch|breaker|transformer|battery|busbar|panel|relay',
      'medbay': r'medical|operating|stretcher|oxygen|surgical|lab|specimen|centrifuge|microscope', 'airlock': r'decon|locker|elevator|airlock|shower',
      'stairs': r'stair|ladder|hatch'}
POOL = {k: [n for n, t in props if re.search(p, t)] or [n for n, _ in props] for k, p in KW.items()}
print('стен', len(walls), 'полов', len(floors), 'пропов', len(props))

lights = []


def lamp(x, y, red=False, r=6.5):
    lights.append((x, y, r, (230, 60, 40) if red else (255, 214, 160), 0.55 if red else 0.75))


def inside(x0, y0, x1, y1):
    return x1 > X0 + 1 and x0 < X1 - 1 and y1 > Y0 + 1 and y0 < Y1 - 1


def clip(x0, y0, x1, y1):
    return max(x0, X0), max(y0, Y0), min(x1, X1), min(y1, Y1)


s.ground('ground_rock')
# коридоры
for j, sp in plan.hspans.items():
    for (a, b) in sp:
        r = clip(a, plan.HY[j], b, plan.HY[j] + plan.CW)
        if r[2] > r[0] and r[3] > r[1]:
            mt_floor = rng.choice(F_CORR)
            s.ground(mt_floor, s.rect(sx(r[0]), sy(r[1]), sx(r[2]), sy(r[3])), blur=0)
            for xx in range(int(r[0]) + 6, int(r[2]) - 2, 16): lamp(sx(xx), sy(r[1]) + 0.8, red=True)
for (x, y0, y1) in plan.vsegs:
    r = clip(x, y0, x + plan.CW, y1)
    if r[2] > r[0] and r[3] > r[1]:
        s.ground(rng.choice(F_CORR), s.rect(sx(r[0]), sy(r[1]), sx(r[2]), sy(r[3])), blur=0)

# комнаты: пол, стены (облезлый бетон), пропы вдоль стен, свет
for (kind, x0, y0, x1, y1) in plan.rooms:
    if not inside(x0, y0, x1, y1) or x0 < X0 or x1 > X1 or y0 < Y0 or y1 > Y1:
        continue
    s.ground(rng.choice(F_ROOM.get(kind, floors)), s.rect(sx(x0), sy(y0), sx(x1), sy(y1)), blur=0)
    tex = rng.choice(W_ROOM if kind in ('barracks', 'office', 'mess', 'medbay', 'comms', 'archive') else W_CORR)
    door = rng.uniform(x0 + 1.2, x1 - 2.8)
    s.walls([(sx(x0), sy(y0)), (sx(x1), sy(y0)), (sx(x1), sy(y1)), (sx(x0), sy(y1)), (sx(x0), sy(y0))], 3.0, tex, t=0.35,
            gaps=[(sx(door), sy(y0), sx(door + 1.6), sy(y0)), (sx(door), sy(y1), sx(door + 1.6), sy(y1))])
    pool = POOL.get(kind) or [n for n, _ in props]
    used = []
    for _ in range(int((x1 - x0) * (y1 - y0) / 7) + 2):
        n = rng.choice(pool); im = L(n)
        if im is None: continue
        w = im.width / PPM
        if w > (x1 - x0) - 1.2: continue
        x = rng.uniform(x0 + 0.6 + w / 2, x1 - 0.6 - w / 2)
        y = rng.choice([y0 + 1.3, y1 - 0.4, rng.uniform(y0 + 1.6, y1 - 0.5)])
        if any(abs(x - ux) < (w + uw) / 2 + 0.2 and abs(y - uy) < 1.2 for ux, uy, uw in used): continue
        used.append((x, y, w)); s.prop(n, sx(x), sy(y), flip=rng.random() < 0.5)
    if rng.random() < 0.8:
        lamp(sx((x0 + x1) / 2), sy(y0) + 1.0, red=kind in ('engine', 'electrical', 'storage', 'armory', 'airlock'))

# внешний скальный контур — по краям коридоров
for j, sp in plan.hspans.items():
    for (a, b) in sp:
        r = clip(a, plan.HY[j], b, plan.HY[j] + plan.CW)
        if r[2] - r[0] > 1:
            s.walls([(sx(r[0]), sy(r[1])), (sx(r[2]), sy(r[1]))], 3.0, rng.choice(W_CORR), t=0.5)
            s.walls([(sx(r[0]), sy(r[3])), (sx(r[2]), sy(r[3]))], 3.0, rng.choice(W_CORR), t=0.5)

from PIL import ImageFilter as F
sh = s.shadow.filter(F.GaussianBlur(1.2)).point(lambda v: int(v * 0.16))   # тени стен мягче (под землёй нет солнца)
dark = Image.new('RGBA', s.img.size, (12, 14, 10, 255)); dark.putalpha(sh); s.img.alpha_composite(dark)
for _, f in sorted(s.objs, key=lambda o: o[0]): f()
base = s.img.convert('RGB')
glow = Image.new('RGB', base.size, (0, 0, 0))
for (x, y, r, col, k) in lights:
    # каждая лампа — на своём кусочке, складывается с соседними по максимуму (раньше тёмный край затирал соседей)
    cx, cy = s.px(x, y); R = int(r * PPM)
    patch = Image.new('RGB', (2 * R, 2 * R), (0, 0, 0)); pd = ImageDraw.Draw(patch)
    for i in range(8, 0, -1):
        t = i / 8
        pd.ellipse([R - R * t, R - R * t * 0.8, R + R * t, R + R * t * 0.8], fill=tuple(int(v * k * (1 - t) ** 1.4) for v in col))
    box = (cx - R, cy - R, cx + R, cy + R)
    glow.paste(ImageChops.lighter(glow.crop(box), patch), box)
glow = glow.filter(F.BoxBlur(PPM // 2))
light = ImageChops.add(Image.new('RGB', base.size, (52, 54, 62)), glow)
lit = ImageChops.multiply(base, light.point(lambda v: min(255, int(v * 2.0))))
d = ImageDraw.Draw(lit, 'RGBA')
d.rectangle([0, 0, lit.width, 70], fill=(0, 0, 0, 200))
d.text((24, 12), f'«Объект 42» · уровень −1 · фрагмент {int(W)}×{int(H)} м · ассеты Астры: {len(walls)} стен, {len(floors)} полов, {len(props)} пропов',
       font=font(40, True), fill=(238, 234, 222))
lit.save(os.path.join(OUT, 'o42-astra-preview.png'))
lit.resize((lit.width // 2, lit.height // 2), Image.LANCZOS).save(os.path.join(OUT, 'o42-astra-preview-half.png'))
print('ok', lit.size)
