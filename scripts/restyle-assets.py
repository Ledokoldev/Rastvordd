# Рестайл готовых ассетов игры (game/art): убрать желтизну и укрупнить пиксель, сохранив резкость.
#   python scripts/restyle-assets.py --test            → docs/design/img/restyle-test.png (до/после на выборке, несколько сил)
#   python scripts/restyle-assets.py --apply --cool=B --pix=2 [--dirs=props,furniture,...]   → переписать файлы в game/art
# Цвет: холоднее баланс белого + приглушить жёлто-оранжевое (красное, ржавчину и яркие лампы не трогаем).
# Пиксель: уменьшить в N раз (BOX — усреднение), вернуть размер «по пикселю» (NEAREST) — те же размеры в игре, пиксели крупнее.
import colorsys, os, sys
from PIL import Image, ImageDraw, ImageFont
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ART = os.path.join(ROOT, 'game', 'art')
COOL = {   # баланс белого (R, G, B) и сколько насыщенности оставить у жёлтого (30–70°)
    'A': ((0.97, 0.985, 1.04), 0.6),
    'B': ((0.94, 0.97, 1.07), 0.35),
}
SKIP_DIRS = {'ui', 'icons', 'fx', 'chars', 'rig', 'doll', 'concept'}   # интерфейс, эффекты, персонажи — не трогаем


def cool(im, key):
    (kr, kg, kb), keep = COOL[key]
    a = np.asarray(im.convert('RGBA')).astype(np.float32) / 255
    rgb, al = a[..., :3], a[..., 3:]
    mx, mn = rgb.max(-1), rgb.min(-1)
    sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    # оттенок (0–360) без цикла по пикселям
    d = np.maximum(mx - mn, 1e-6)
    h = np.where(mx == r, (g - b) / d % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    yellow = ((h >= 28) & (h <= 72)).astype(np.float32)
    bright_lamp = ((mx > 0.9) & (sat > 0.5)).astype(np.float32)          # горящие лампы/огонь — оставить
    w = yellow * (1 - bright_lamp) * np.clip(sat * 3, 0, 1)             # насколько это «жёлтый» пиксель
    lum = (0.299 * r + 0.587 * g + 0.114 * b)[..., None]
    k = (keep + (1 - keep) * (1 - w))[..., None]
    rgb = lum + (rgb - lum) * k                                         # приглушить жёлтое к серому
    rgb = rgb * np.array([kr, kg, kb], np.float32)                      # холоднее в целом
    out = np.concatenate([np.clip(rgb, 0, 1), al], -1)
    return Image.fromarray((out * 255 + 0.5).astype(np.uint8), 'RGBA')


def pixelate(im, n):
    if n <= 1:
        return im
    w, h = im.size
    sw, sh = max(1, round(w / n)), max(1, round(h / n))
    small = im.resize((sw, sh), Image.BOX)
    a = small.getchannel('A').point(lambda v: 255 if v >= 110 else 0)  # край чёткий, без полупрозрачной каши
    small.putalpha(a)
    return small.resize((w, h), Image.NEAREST)


def test():
    names = ['props/crate_wood', 'props/car_wreck', 'props/barrel_oil', 'props/junk_pile', 'props/obj_generator_big', 'props/obj_l1_dorm_bunks',
             'props/int_desk', 'props/chem_tank_vertical', 'props/ats_diesel_gen', 'furniture/shelf_metal_rack', 'containers/crate_army_green',
             'trees/tree_aspen', 'nodes/coast_rock_waves_1']
    cols = [('исходник', None, 1), ('холод A', 'A', 1), ('холод B', 'B', 1), ('B + пиксель ×2', 'B', 2), ('B + пиксель ×3', 'B', 3)]
    Z = 3                                                                # показ ×3, «по пикселю»
    ims = [Image.open(os.path.join(ART, n + '.png')).convert('RGBA') for n in names]
    cw = max(i.width for i in ims) * Z + 24
    rows = [max(60, i.height * Z + 16) for i in ims]
    sheet = Image.new('RGB', (cw * len(cols) + 220, sum(rows) + 60), (52, 54, 50))
    d = ImageDraw.Draw(sheet)
    f = ImageFont.truetype('C:/Windows/Fonts/segoeuib.ttf', 22)
    for c, (t, _, _) in enumerate(cols):
        d.text((220 + c * cw + 10, 16), t, font=f, fill=(235, 232, 220))
    y = 60
    for im, n, rh in zip(ims, names, rows):
        d.text((10, y + rh // 2 - 12), n.split('/')[-1], font=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf', 18), fill=(200, 200, 190))
        for c, (_, ck, px) in enumerate(cols):
            v = im if ck is None else cool(im, ck)
            v = pixelate(v, px)
            v = v.resize((v.width * Z, v.height * Z), Image.NEAREST)
            sheet.paste(v, (220 + c * cw + 10, y + 8), v)
        y += rh
    out = os.path.join(ROOT, 'docs', 'design', 'img', 'restyle-test.png')
    sheet.save(out)
    print('ok', out, sheet.size)


def apply(cool_key, pix, dirs):
    n = 0
    for dp, _, fs in os.walk(ART):
        rel = os.path.relpath(dp, ART).split(os.sep)[0]
        if rel in SKIP_DIRS or (dirs and rel not in dirs):
            continue
        for fn in fs:
            if not fn.endswith('.png'):
                continue
            p = os.path.join(dp, fn)
            im = Image.open(p).convert('RGBA')
            if cool_key:
                im = cool(im, cool_key)
            im = pixelate(im, pix)
            im.save(p)
            n += 1
    print('обработано', n)


if __name__ == '__main__':
    args = dict(a.lstrip('-').split('=', 1) if '=' in a else (a.lstrip('-'), '1') for a in sys.argv[1:])
    if 'apply' in args:
        apply(args.get('cool'), int(args.get('pix', 1)), [x for x in args.get('dirs', '').split(',') if x])
    else:
        test()
