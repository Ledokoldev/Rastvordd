# Склейка кадров render-char3d.py в лист 8 направлений × N кадров по 128×128 с пиксель-обработкой (уменьшение BOX ×4,
# чёткая альфа, лёгкая резкость). Запуск: python scripts/pack-char3d.py <папка_кадров> <анимация> <кадров> <выход.png>
import sys, os
from PIL import Image, ImageFilter
src, act, n, out = sys.argv[1], sys.argv[2], int(sys.argv[3]), sys.argv[4]
PIXEL = '--pixel' in sys.argv   # пиксель-арт: общая палитра 20 цветов и тёмный контур 1 px по силуэту
CELL = 128
sheet = Image.new('RGBA', (CELL * n, CELL * 8), (0, 0, 0, 0))
for d in range(8):
    for i in range(n):
        im = Image.open(os.path.join(src, f'{act}_d{d}_f{i}.png')).convert('RGBA')
        # по вертикали ×1,25: ортокамера под 53° даёт высоту ×0,6 и глубину ×0,8 — в игре 0,75 и 1 (Iso.HS)
        st = im.resize((im.width, round(im.height * 1.25)), Image.LANCZOS)
        top = st.height - im.height
        st = st.crop((0, top, im.width, st.height))                     # низ кадра (ступни) на месте
        sm = st.resize((CELL, CELL), Image.BOX)
        rgb = sm.convert('RGB').filter(ImageFilter.UnsharpMask(radius=1, percent=60, threshold=2))
        a = sm.getchannel('A').point(lambda v: 255 if v >= 110 else 0)
        px = rgb.convert('RGBA'); px.putalpha(a)
        sheet.paste(px, (i * CELL, d * CELL))
if PIXEL:
    a = sheet.getchannel('A')
    q = sheet.convert('RGB').quantize(colors=20, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).convert('RGBA')
    q.putalpha(a)
    ring = a.filter(ImageFilter.MaxFilter(3))
    outline = Image.new('RGBA', sheet.size, (22, 20, 24, 255)); outline.putalpha(ring)
    outline.alpha_composite(q); sheet = outline
sheet.save(out); print('ok', sheet.size)
