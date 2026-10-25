# Низкая трава: процедурные пучки из тонких травинок (1 px), цвета — с тайла земли биома.
# Каждая травинка — дуга от корня вверх с наклоном; корень тёмный, кончик светлый (свет сверху-слева).
# Запуск: python scripts/gen-grass-tufts.py  → game/art/grass/tuft_<биом>_<n>.png
import math, os, random
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, 'game', 'art', 'grass')
N = 12   # вариантов на биом

# биом: (тайл земли, оттенок травинок, высота px (мин, макс), травинок (мин, макс), доля сухих)
BIOMES = {
    'meadow': ('ground_grass', (1.05, 1.18, 0.82), (6, 14), (7, 16), 0.12),
    'forest': ('ground_forest', (0.95, 1.12, 0.9), (5, 11), (5, 11), 0.05),
    'dry':    ('ground_dry', (1.15, 1.08, 0.78), (5, 12), (4, 10), 0.6),
    'swamp':  ('ground_grass', (0.9, 1.1, 0.95), (8, 17), (6, 13), 0.15),
}


def palette(tile, tint):
    im = Image.open(os.path.join(ROOT, 'game', 'art', 'tiles', tile + '.png')).convert('RGB')
    px = sorted(im.getdata(), key=lambda c: sum(c))
    n = len(px)
    pick = lambda q: px[int(q * (n - 1))]
    tone = lambda c, k: tuple(max(0, min(255, int(v * t * k))) for v, t in zip(c, tint))
    return [tone(pick(0.35), 0.8), tone(pick(0.55), 1.0), tone(pick(0.75), 1.12), tone(pick(0.92), 1.25)]   # корень → кончик


def tuft(rng, pal, dry_pal, hmin, hmax, bmin, bmax, dry):
    W, H = 22, hmax + 3
    im = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    p = im.load()
    blades = rng.randint(bmin, bmax)
    cx = W // 2
    for _ in range(blades):
        x0 = cx + rng.gauss(0, 2.6)
        h = rng.randint(hmin, hmax)
        lean = rng.uniform(-0.55, 0.55) + (x0 - cx) * 0.08          # крайние травинки расходятся веером
        bend = rng.uniform(0.0, 0.5) * (1 if lean >= 0 else -1)
        cols = dry_pal if rng.random() < dry else pal
        for k in range(h):
            t = k / max(1, h - 1)
            x = x0 + lean * k + bend * t * t * h * 0.5
            y = H - 1 - k
            xi = int(round(x))
            if not (0 <= xi < W and 0 <= y < H): continue
            c = cols[min(3, int(t * 3.999))]
            if xi < cx - 1 and t > 0.4: c = tuple(min(255, v + 10) for v in c)   # левая сторона пучка — к свету
            p[xi, y] = c + (255,)
    return im.crop(im.getbbox()) if im.getbbox() else im


for name, (tile, tint, (hmin, hmax), (bmin, bmax), dry) in BIOMES.items():
    rng = random.Random(sum(map(ord, name)))
    pal = palette(tile, tint)
    dry_pal = palette('ground_dry', (1.2, 1.1, 0.75))
    for i in range(N):
        tuft(rng, pal, dry_pal, hmin, hmax, bmin, bmax, dry).save(os.path.join(OUT, f'tuft_{name}_{i + 1}.png'))
print('ok', len(BIOMES) * N)

# низкие полянки вместо старых куртин-«кочек» grass_<биом>_1/2: 4–7 пучков рядом, шириной ~1–1,5 м
PATCH = {'meadow': 'meadow', 'forest': 'forest', 'swamp': 'swamp', 'arid': 'dry', 'tundra': 'dry', 'beach': 'dry'}
for name, src in PATCH.items():
    rng = random.Random(sum(map(ord, name)) * 7)
    tufts = [Image.open(os.path.join(OUT, f'tuft_{src}_{i + 1}.png')) for i in range(N)]
    for v in (1, 2):
        W, H = 44, 22
        im = Image.new('RGBA', (W, H), (0, 0, 0, 0))
        parts = sorted(((rng.randint(0, W - 16), rng.randint(0, 5)) for _ in range(rng.randint(4, 7))), key=lambda q: q[1])
        for x, dy in parts:
            t = rng.choice(tufts)
            im.alpha_composite(t, (min(x, W - t.width), H - t.height - (5 - dy)))
        im.crop(im.getbbox()).save(os.path.join(OUT, f'grass_{name}_{v}.png'))
print('полянки ok')
