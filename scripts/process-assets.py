"""Обработка сгенерированных ассетов для игры.

assets/gen/<cat>/<id>.png  ->  game/art/<cat>/<id>.png
- убирает цветную кайму по краю прозрачности (сужает альфу и смягчает край);
- обрезает пустые поля;
- масштабирует до высоты h метров × 64 px (см. assets/manifest.json);
- собирает лист-превью assets/preview.png на фоне земли.

Запуск: python scripts/process-assets.py
"""
import json
import pathlib

from PIL import Image, ImageDraw, ImageFilter

PX_PER_M = 64
ROOT = pathlib.Path(__file__).resolve().parent.parent


def clean(im: Image.Image) -> Image.Image:
    im = im.convert("RGBA")
    r, g, b, a = im.split()
    # почти прозрачное — в ноль, затем сужаем край на ~2 px, где живёт кайма фона
    a = a.point(lambda v: 0 if v < 48 else v)
    a = a.filter(ImageFilter.MinFilter(5))
    a = a.filter(ImageFilter.GaussianBlur(0.7))
    out = Image.merge("RGBA", (r, g, b, a))
    bbox = a.point(lambda v: 255 if v > 8 else 0).getbbox()
    return out.crop(bbox) if bbox else out


def make_seamless(im: Image.Image, size: int) -> Image.Image:
    """Бесшовный тайл: сдвигаем на полразмера и закрываем швы исходником через мягкую маску по центру."""
    im = im.convert("RGB").resize((size, size), Image.LANCZOS)
    shifted = Image.new("RGB", (size, size))
    h = size // 2
    shifted.paste(im.crop((h, h, size, size)), (0, 0))
    shifted.paste(im.crop((0, h, h, size)), (h, 0))
    shifted.paste(im.crop((h, 0, size, h)), (0, h))
    shifted.paste(im.crop((0, 0, h, h)), (h, h))
    # маска: белая в центре (берём исходник), к краям — чёрная (сдвинутый, у него края совпадают)
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).ellipse((size * 0.12, size * 0.12, size * 0.88, size * 0.88), fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(size * 0.12))
    return Image.composite(im, shifted, mask)


def main() -> None:
    manifest = json.loads((ROOT / "assets" / "manifest.json").read_text(encoding="utf-8"))
    done = []
    for a in manifest["assets"]:
        src = ROOT / "assets" / "gen" / a["cat"] / f"{a['id']}.png"
        if not src.exists():
            continue
        dst = ROOT / "game" / "art" / a["cat"] / f"{a['id']}.png"
        dst.parent.mkdir(parents=True, exist_ok=True)
        if a.get("tile"):
            tile = make_seamless(Image.open(src), round(a["tile"] * PX_PER_M))
            tile.save(dst)
            # для превью показываем 2×2, чтобы были видны швы
            prev = Image.new("RGB", (tile.width * 2, tile.height * 2))
            for dx in (0, tile.width):
                for dy in (0, tile.height):
                    prev.paste(tile, (dx, dy))
            done.append((a["id"], prev.resize((tile.width, tile.height)).convert("RGBA")))
            print(f"{a['id']:<20} tile {tile.width}px")
            continue
        im = clean(Image.open(src))
        box = a.get("icon") or a.get("weapon")
        if box:
            # иконка/оружие: вписываем в квадрат box×box с полями
            inner = round(box * 0.9)
            scale = min(inner / im.width, inner / im.height)
            im = im.resize((max(1, round(im.width * scale)), max(1, round(im.height * scale))), Image.LANCZOS)
            square = Image.new("RGBA", (box, box), (0, 0, 0, 0))
            square.paste(im, ((box - im.width) // 2, (box - im.height) // 2), im)
            square.save(dst)
            done.append((a["id"], square))
            print(f"{a['id']:<20} icon {box}px")
            continue
        target_h = max(8, round(a["h"] * PX_PER_M))
        scale = target_h / im.height
        im = im.resize((max(1, round(im.width * scale)), target_h), Image.LANCZOS)
        im.save(dst)
        done.append((a["id"], im))
        print(f"{a['id']:<20} {im.width}x{im.height}")

    if not done:
        return
    # лист-превью: всё в игровом масштабе на фоне травы
    cols, pad = 6, 24
    cell_w = max(im.width for _, im in done) + pad
    cell_h = max(im.height for _, im in done) + pad + 18
    rows = (len(done) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cell_w, rows * cell_h), (92, 101, 66))
    draw = ImageDraw.Draw(sheet)
    for i, (name, im) in enumerate(done):
        x0, y0 = (i % cols) * cell_w, (i // cols) * cell_h
        x = x0 + (cell_w - im.width) // 2
        y = y0 + cell_h - 18 - im.height
        sheet.paste(im, (x, y), im)
        draw.text((x0 + 6, y0 + cell_h - 16), name, fill=(235, 235, 225))
    sheet.save(ROOT / "assets" / "preview.png")
    print(f"preview: {ROOT / 'assets' / 'preview.png'}")


if __name__ == "__main__":
    main()
