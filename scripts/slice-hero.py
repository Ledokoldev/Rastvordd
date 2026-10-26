# Нарезка нарисованного листа персонажа (4 кадра × 2 ряда: вперёд-вправо / назад-вправо) на игровые кадры 128×128:
# фигуры находятся по альфе, масштабируются к одному росту (по самой высокой в ряду — шаг не «прыгает»),
# ступни на общей линии, пиксель-обработка (уменьшение BOX, лёгкая резкость, чёткая альфа).
# Запуск: python scripts/slice-hero.py <лист.png> <база> [рост_px=45]
#   → game/art/chars/<база>_f0..3 и _b0..3 (или <база>_walk_f0.. при базе вида char_hero_walk)
import os, sys
from PIL import Image, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
src, base = sys.argv[1], sys.argv[2]
TALL = int(sys.argv[3]) if len(sys.argv) > 3 else 45
N = int(sys.argv[4]) if len(sys.argv) > 4 else 4   # кадров в ряду
CELL = 128


def blobs(im):
    """Фигуры листа: ряды по пустым горизонтальным полосам, в ряду — по пустым вертикальным."""
    a = im.getchannel('A').point(lambda v: 255 if v > 60 else 0)
    W, H = im.size
    def spans(vals, gap):
        out, s = [], None
        for i, v in enumerate(vals + [False]):
            if v and s is None: s = i
            if not v and s is not None:
                if i - s > gap: out.append((s, i))
                s = None
        return out
    rows = spans([a.crop((0, y, W, y + 1)).getbbox() is not None for y in range(H)], 30)
    res = []
    for y0, y1 in rows:
        strip = a.crop((0, y0, W, y1))
        cols = spans([strip.crop((x, 0, x + 1, y1 - y0)).getbbox() is not None for x in range(W)], 12)
        res.append([(im.crop((x0, y0, x1, y1)), x0, y0) for x0, x1 in cols])
    return res


def core_x(f):
    """Центр корпуса по горизонтали: «центр масс» непрозрачного в полосе плеч–таза (руки и ноги не дёргают кадр)."""
    a = f.getchannel('A').point(lambda v: 1 if v > 60 else 0)
    w, h = f.size
    band = a.crop((0, int(h * 0.18), w, int(h * 0.55)))
    tot = sx = 0
    px = band.load()
    for y in range(band.height):
        for x in range(band.width):
            if px[x, y]: tot += 1; sx += x
    return sx / tot if tot else w / 2


im = Image.open(src).convert('RGBA')
rows = blobs(im)
print('ряды:', [len(r) for r in rows])
out_dir = os.path.join(ROOT, 'game', 'art', 'chars')
for r, tag in zip(rows[:2], 'fb'):
    # ступни — по общей линии земли ряда (нижний край самой низкой фигуры): в прыжке бега ноги отрываются,
    # а не «прилипают» к низу кадра; по горизонтали — по центру корпуса (руки и ноги не трясут кадр)
    figs = []
    for f, x0, y0 in r[:N]:
        bb = f.getchannel('A').point(lambda v: 255 if v > 60 else 0).getbbox()
        figs.append((f.crop(bb), y0 + bb[3]))
    ground = max(b for _, b in figs)
    hmax = max(f.height for f, _ in figs)
    k = TALL / hmax
    for i, (f, bottom) in enumerate(figs):
        w, h = max(1, round(f.width * k)), max(1, round(f.height * k))
        cx = core_x(f) * k
        lift = round((ground - bottom) * k)
        small = f.resize((w * 4, h * 4), Image.LANCZOS).resize((w, h), Image.BOX)
        rgb = small.convert('RGB').filter(ImageFilter.UnsharpMask(radius=1, percent=70, threshold=2))
        px = rgb.convert('RGBA'); px.putalpha(small.getchannel('A').point(lambda v: 255 if v >= 110 else 0))
        cell = Image.new('RGBA', (CELL, CELL), (0, 0, 0, 0))
        cell.alpha_composite(px, (round(CELL / 2 - cx), CELL - 2 - h - lift))
        name = f'{base}_{tag}{i}.png' if '_walk' not in base else f'{base}_{tag}{i}.png'
        os.makedirs(os.path.dirname(os.path.join(out_dir, name)), exist_ok=True)
        cell.save(os.path.join(out_dir, name))
print('ok', base)
