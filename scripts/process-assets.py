"""Обработка сгенерированных ассетов для игры.

assets/gen/<cat>/<id>.png  ->  game/art/<cat>/<id>.png   (пиксельный стиль, идёт в игру)
                           ->  assets/hd/<cat>/<id>.png   (HD, только для превью)

Пиксельный стиль: 32 px на метр, резкость, ограниченная палитра, жёсткая прозрачность.
В игре такие спрайты рисуются с фильтрацией nearest (каждый пиксель арта = 2×2 на экране).

Запуск: python scripts/process-assets.py
Превью: assets/preview.png (пиксель), assets/preview_compare.png (HD против пикселя).
"""
import json
import pathlib

from PIL import Image, ImageDraw, ImageFilter

ROOT = pathlib.Path(__file__).resolve().parent.parent
PX_HD = 64          # px на метр для HD
PX_PIXEL = 32       # px на метр для пиксель-арта
PIXEL_ICON = 64     # размер иконок и оружия в пиксельном стиле
PALETTE = 48        # цветов на спрайт


def clean(im: Image.Image) -> Image.Image:
    """Убирает цветную кайму по краю прозрачности и обрезает пустые поля."""
    im = im.convert("RGBA")
    r, g, b, a = im.split()
    a = a.point(lambda v: 0 if v < 48 else v)
    a = a.filter(ImageFilter.MinFilter(5))
    a = a.filter(ImageFilter.GaussianBlur(0.7))
    out = Image.merge("RGBA", (r, g, b, a))
    bbox = a.point(lambda v: 255 if v > 8 else 0).getbbox()
    return out.crop(bbox) if bbox else out


def pixelize(im: Image.Image, size: tuple[int, int], colors: int = PALETTE, opaque: bool = False) -> Image.Image:
    """Чёткий пиксель-арт: уменьшение с усреднением, резкость, палитра, жёсткая альфа."""
    small = im.resize(size, Image.BOX if im.width >= size[0] * 2 else Image.LANCZOS)
    rgb = small.convert("RGB").filter(ImageFilter.UnsharpMask(radius=1.2, percent=90, threshold=2))
    rgb = rgb.quantize(colors=colors, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).convert("RGB")
    if opaque:
        return rgb.convert("RGBA")
    alpha = small.getchannel("A").point(lambda v: 255 if v >= 110 else 0)
    out = rgb.convert("RGBA")
    out.putalpha(alpha)
    return out


def make_seamless(im: Image.Image, size: int) -> Image.Image:
    """Бесшовный тайл: сдвигаем на полразмера и закрываем швы исходником через мягкую маску по центру."""
    im = im.convert("RGB").resize((size, size), Image.LANCZOS)
    shifted = Image.new("RGB", (size, size))
    h = size // 2
    shifted.paste(im.crop((h, h, size, size)), (0, 0))
    shifted.paste(im.crop((0, h, h, size)), (h, 0))
    shifted.paste(im.crop((h, 0, size, h)), (0, h))
    shifted.paste(im.crop((0, 0, h, h)), (h, h))
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).ellipse((size * 0.12, size * 0.12, size * 0.88, size * 0.88), fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(size * 0.12))
    return Image.composite(im, shifted, mask)


def fit_square(im: Image.Image, box: int) -> Image.Image:
    inner = round(box * 0.9)
    scale = min(inner / im.width, inner / im.height)
    im = im.resize((max(1, round(im.width * scale)), max(1, round(im.height * scale))), Image.LANCZOS)
    square = Image.new("RGBA", (box, box), (0, 0, 0, 0))
    square.paste(im, ((box - im.width) // 2, (box - im.height) // 2), im)
    return square


def process(a: dict, src: pathlib.Path) -> tuple[Image.Image, Image.Image]:
    """Возвращает (hd, pixel)."""
    raw = Image.open(src)
    if a.get("tile"):
        hd = make_seamless(raw, round(a["tile"] * PX_HD))
        px = pixelize(hd.convert("RGBA"), (round(a["tile"] * PX_PIXEL),) * 2, colors=64, opaque=True)
        return hd.convert("RGBA"), px
    im = clean(raw)
    box = a.get("icon") or a.get("weapon")
    if box:
        hd = fit_square(im, box)
        px_box = PIXEL_ICON
        inner = round(px_box * 0.9)
        scale = min(inner / im.width, inner / im.height)
        size = (max(1, round(im.width * scale)), max(1, round(im.height * scale)))
        sprite = pixelize(im, size)
        px = Image.new("RGBA", (px_box, px_box), (0, 0, 0, 0))
        px.paste(sprite, ((px_box - size[0]) // 2, (px_box - size[1]) // 2), sprite)
        return hd, px
    h_hd = max(8, round(a["h"] * PX_HD))
    hd = im.resize((max(1, round(im.width * h_hd / im.height)), h_hd), Image.LANCZOS)
    h_px = max(4, round(a["h"] * PX_PIXEL))
    px = pixelize(im, (max(1, round(im.width * h_px / im.height)), h_px))
    return hd, px


def sheet(items: list[tuple[str, Image.Image]], scale: int, path: pathlib.Path, cols: int = 8) -> None:
    pad = 16
    imgs = [(n, im.resize((im.width * scale, im.height * scale), Image.NEAREST)) for n, im in items]
    cell_w = max(im.width for _, im in imgs) + pad
    cell_h = max(im.height for _, im in imgs) + pad + 16
    rows = (len(imgs) + cols - 1) // cols
    out = Image.new("RGB", (cols * cell_w, rows * cell_h), (74, 80, 56))
    d = ImageDraw.Draw(out)
    for i, (name, im) in enumerate(imgs):
        x0, y0 = (i % cols) * cell_w, (i // cols) * cell_h
        out.paste(im, (x0 + (cell_w - im.width) // 2, y0 + cell_h - 16 - im.height), im)
        d.text((x0 + 4, y0 + cell_h - 14), name, fill=(235, 235, 225))
    out.save(path)
    print(f"preview: {path}")


def main() -> None:
    manifest = json.loads((ROOT / "assets" / "manifest.json").read_text(encoding="utf-8"))
    pix, compare = [], []
    for a in manifest["assets"]:
        src = ROOT / "assets" / "gen" / a["cat"] / f"{a['id']}.png"
        if not src.exists():
            continue
        hd, px = process(a, src)
        for base, im in ((ROOT / "assets" / "hd", hd), (ROOT / "game" / "art", px)):
            dst = base / a["cat"] / f"{a['id']}.png"
            dst.parent.mkdir(parents=True, exist_ok=True)
            im.save(dst)
        pix.append((a["id"], px))
        if a["cat"] in ("trees", "nodes", "props") and len(compare) < 12:
            compare.append((a["id"] + " HD", hd))
            compare.append((a["id"] + " px", px.resize((px.width * 2, px.height * 2), Image.NEAREST)))
        print(f"{a['id']:<24} {px.width}x{px.height}")

    if pix:
        world = [(n, im) for n, im in pix if not n.startswith(("icon_", "weapon_"))]
        icons = [(n, im) for n, im in pix if n.startswith(("icon_", "weapon_"))]
        if world:
            sheet(world, 2, ROOT / "assets" / "preview.png")
        if icons:
            sheet(icons, 2, ROOT / "assets" / "preview_icons.png", cols=10)
    if compare:
        sheet(compare, 1, ROOT / "assets" / "preview_compare.png", cols=6)


if __name__ == "__main__":
    main()
