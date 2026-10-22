# Мягкая грязь для пола РТ (обстановка, Dressing.cs): гладкие полупрозрачные пятна без пиксельной ряби.
# game/art/decals/floor_grime.png (4×3 м) и floor_grime_2.png (вытянутое, вдоль стен). Запуск: python scripts/make-grime.py
import os, random
from PIL import Image, ImageFilter, ImageDraw

R = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'game', 'art', 'decals')


def blob(w, h, seed, amax, col):
    random.seed(seed)
    m = Image.new('L', (w, h), 0)
    d = ImageDraw.Draw(m)
    # несколько эллипсов — неровная форма, потом сильное размытие
    for _ in range(9):
        cx, cy = random.uniform(w * 0.3, w * 0.7), random.uniform(h * 0.3, h * 0.7)
        rx, ry = random.uniform(w * 0.12, w * 0.3), random.uniform(h * 0.12, h * 0.3)
        d.ellipse((cx - rx, cy - ry, cx + rx, cy + ry), fill=random.randint(150, 255))
    m = m.filter(ImageFilter.GaussianBlur(min(w, h) * 0.12))
    # крупная мягкая неоднородность (не шум по пикселям)
    n = Image.new('L', (w // 8, h // 8))
    n.putdata([random.randint(150, 255) for _ in range((w // 8) * (h // 8))])
    n = n.resize((w, h), Image.BICUBIC).filter(ImageFilter.GaussianBlur(4))
    a = Image.eval(Image.composite(m, Image.new('L', (w, h), 0), m), lambda v: v)
    a = Image.blend(a, Image.eval(Image.merge('L', [a]).point(lambda v: v), lambda v: v), 0)
    px = a.load(); nn = n.load()
    for y in range(h):
        for x in range(w):
            px[x, y] = int(px[x, y] * nn[x, y] / 255 * amax / 255)
    img = Image.new('RGBA', (w, h), col + (0,))
    img.putalpha(a)
    return img


blob(128, 96, 3, 120, (34, 29, 22)).save(os.path.join(R, 'floor_grime.png'))
blob(160, 48, 5, 135, (30, 26, 20)).save(os.path.join(R, 'floor_grime_2.png'))
print('ok')
