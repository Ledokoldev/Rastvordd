# Видео → пиксель-арт «как спрайты игры»: плоские пятна, крупный пиксель, одна палитра на весь ролик, тёмный контур, 12 к/с.
# python scripts/pixelize-video.py вход.mp4 выход.mp4 [ширина=180] [цветов=24]
import os, subprocess, sys, tempfile, shutil
from PIL import Image, ImageFilter, ImageChops, ImageOps

src, dst = sys.argv[1], sys.argv[2]
W = int(sys.argv[3]) if len(sys.argv) > 3 else 180
NC = int(sys.argv[4]) if len(sys.argv) > 4 else 24
FPS, UP = 12, 6
ff = shutil.which('ffmpeg')
tmp = tempfile.mkdtemp()
subprocess.run([ff, '-v', 'error', '-i', src, '-vf', f'fps={FPS}', os.path.join(tmp, 'f%04d.png')], check=True)
frames = sorted(f for f in os.listdir(tmp) if f.endswith('.png'))


def flat(im):
    # сгладить фактуру видео до пятен: сначала уменьшить вдвое больше цели, там сильная медиана (дёшево), потом к цели
    h = round(im.height * W / im.width)
    im = im.resize((W * 3, h * 3), Image.BOX)
    for _ in range(3):
        im = im.filter(ImageFilter.MedianFilter(5))
    im = ImageOps.autocontrast(im, cutoff=1)
    return im.resize((W, h), Image.BOX)


def clean(q):
    # одиночные пиксели — к большинству соседей (пятна вместо «песка»)
    return q.filter(ImageFilter.ModeFilter(3))


# одна палитра на весь ролик — из нескольких кадров, иначе цвета мерцают
sample = [flat(Image.open(os.path.join(tmp, frames[i])).convert('RGB')) for i in range(0, len(frames), max(1, len(frames) // 8))]
strip = Image.new('RGB', (W, sum(s.height for s in sample)))
y = 0
for s in sample:
    strip.paste(s, (0, y)); y += s.height
pal = strip.quantize(colors=NC, method=Image.MEDIANCUT)

outd = os.path.join(tmp, 'out'); os.makedirs(outd)
for k, f in enumerate(frames):
    im = flat(Image.open(os.path.join(tmp, f)).convert('RGB'))
    q = clean(clean(im.quantize(palette=pal, dither=Image.Dither.NONE))).convert('RGB')   # большинство — по номерам палитры, без новых цветов
    # тёмный контур только по сильным краям (силуэты), не по фактуре
    g = q.convert('L').filter(ImageFilter.MedianFilter(3))
    edge = g.filter(ImageFilter.FIND_EDGES).point(lambda v: 255 if v > 120 else 0)
    dark = ImageChops.multiply(q, Image.new('RGB', q.size, (70, 66, 64)))
    q = Image.composite(dark, q, edge)
    q.resize((q.width * UP, q.height * UP), Image.NEAREST).save(os.path.join(outd, f'o{k:04d}.png'))

subprocess.run([ff, '-y', '-v', 'error', '-framerate', str(FPS), '-i', os.path.join(outd, 'o%04d.png'), '-i', src,
                '-map', '0:v', '-map', '1:a?', '-c:v', 'libx264', '-crf', '8', '-pix_fmt', 'yuv444p', '-tune', 'animation',
                '-c:a', 'aac', '-shortest', dst], check=True)
shutil.rmtree(tmp)
print('ok', dst, len(frames), 'кадров')
