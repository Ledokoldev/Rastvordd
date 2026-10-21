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

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter

ROOT = pathlib.Path(__file__).resolve().parent.parent
PX_HD = 64          # px на метр для HD
PX_PIXEL = 32       # px на метр для пиксель-арта
PIXEL_ICON = 64     # размер иконок и оружия в пиксельном стиле
PALETTE = 48        # цветов на спрайт (если нет общей палитры)
GLOBAL_COLORS = 128 # общая палитра мира: всё приводится к одному набору цветов
_global_palette = None


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
    if _global_palette is not None:
        rgb = rgb.quantize(palette=_global_palette, dither=Image.Dither.NONE).convert("RGB")
    else:
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
    global _global_palette
    raw = Image.open(src)
    if a.get("tile"):
        hd = make_seamless(raw, round(a["tile"] * PX_HD))
        # земля — фон: мягче и спокойнее, чтобы после ужатия не было ряби; детали дают декали
        soft = ImageEnhance.Contrast(hd.filter(ImageFilter.GaussianBlur(1.3))).enhance(0.78)
        soft = ImageEnhance.Color(soft).enhance(1.15)
        keep, _global_palette = _global_palette, None   # у земли своя палитра: в общей мало зелёного
        px = pixelize(soft.convert("RGBA"), (round(a["tile"] * PX_PIXEL),) * 2, colors=64, opaque=True)
        _global_palette = keep
        return hd.convert("RGBA"), px
    im = clean(raw)
    if a.get("rig"):
        part = a["id"].split("_")[2]                     # rig_naked_<part>[_f|_b]
        axis, length = RIG_SIZE[part]
        scale = length / (im.height if axis == "h" else im.width)
        size = (max(1, round(im.width * scale)), max(1, round(im.height * scale)))
        keep = _global_palette
        _global_palette = None
        px = pixelize(im, size, colors=32)
        _global_palette = keep
        return im, px
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
    # мелочи на земле — крупнее реальных, иначе в пикселе не читаются
    h_px = max(4, round(a["h"] * PX_PIXEL * (1.9 if a["cat"] == "clutter" else 1.0)))
    keep = _global_palette
    if a["cat"] in OWN_PALETTE_CATS:
        # трава и растения: своя палитра — в общей мало зелёного, и они превращались в тёмные кляксы
        _global_palette = None
        im = ImageEnhance.Brightness(ImageEnhance.Color(im).enhance(1.2)).enhance(1.12)
    px = pixelize(im, (max(1, round(im.width * h_px / im.height)), h_px))
    _global_palette = keep
    return hd, px


CHAR_FRAME = 64


def process_sheet(a: dict, src: pathlib.Path) -> Image.Image:
    """Лист персонажа cols×rows -> кадры 64×64 (ряд 0 — лицом «f», ряд 1 — спиной «b»).
    Все кадры ряда режутся одной общей рамкой и стоят на одной линии ступней — без дрожи при ходьбе."""
    cols, rows = a["sheet"]
    raw = Image.open(src).convert("RGBA")
    cw, ch = raw.width // cols, raw.height // rows
    target_h = round(a["h"] * PX_PIXEL)
    strip = Image.new("RGBA", (CHAR_FRAME * cols, CHAR_FRAME * rows), (0, 0, 0, 0))
    for r in range(rows):
        cells = []
        for c in range(cols):
            cell = raw.crop((c * cw, r * ch, (c + 1) * cw, (r + 1) * ch))
            rr, gg, bb, al = cell.split()
            al = al.point(lambda v: 0 if v < 70 else v).filter(ImageFilter.MinFilter(3))
            cells.append(Image.merge("RGBA", (rr, gg, bb, al)))
        boxes = [cl.getchannel("A").point(lambda v: 255 if v > 8 else 0).getbbox() for cl in cells]
        boxes = [b for b in boxes if b]
        if not boxes:
            continue
        union = (min(b[0] for b in boxes), min(b[1] for b in boxes), max(b[2] for b in boxes), max(b[3] for b in boxes))
        scale = target_h / (union[3] - union[1])
        for c, cl in enumerate(cells):
            crop = cl.crop(union)
            size = (max(1, round(crop.width * scale)), target_h)
            px = pixelize(crop, size)
            frame = Image.new("RGBA", (CHAR_FRAME, CHAR_FRAME), (0, 0, 0, 0))
            frame.paste(px, ((CHAR_FRAME - px.width) // 2, CHAR_FRAME - target_h - 2), px)
            dst = ROOT / "game" / "art" / a["cat"] / f"{a['id']}_{'fb'[r]}{c}.png"
            dst.parent.mkdir(parents=True, exist_ok=True)
            frame.save(dst)
            strip.paste(frame, (c * CHAR_FRAME, r * CHAR_FRAME), frame)
    return strip


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


OWN_PALETTE_CATS = ("grass", "plants", "clutter", "rig")

# части тела для управляемого персонажа (кат-аут риг): длина по главной оси в пикселях (рост ~58 px при 32 px/м)
RIG_SIZE = {
    "head": ("h", 12), "torso": ("h", 19), "pelvis": ("h", 8),
    "upperarm": ("w", 10), "forearm": ("w", 11), "thigh": ("h", 14), "shin": ("h", 15),
}
WORLD_CATS = ("trees", "plants", "nodes", "props", "tiles", "walls", "decals", "fx")


def build_global_palette(manifest: dict) -> Image.Image | None:
    """Одна палитра на весь мир: монтаж уменьшенных ассетов -> median cut на GLOBAL_COLORS цветов."""
    thumbs = []
    for a in manifest["assets"]:
        src = ROOT / "assets" / "gen" / a["cat"] / f"{a['id']}.png"
        if a["cat"] not in WORLD_CATS or not src.exists():
            continue
        im = Image.open(src).convert("RGBA")
        im.thumbnail((96, 96))
        bg = Image.new("RGB", im.size, (0, 0, 0))
        bg.paste(im, (0, 0), im)
        # тайлы весят больше: их на экране больше всего
        for _ in range(3 if a.get("tile") else 1):
            thumbs.append(bg)
    if not thumbs:
        return None
    cols = 16
    rows = (len(thumbs) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * 96, rows * 96), (60, 64, 48))
    for i, t in enumerate(thumbs):
        sheet.paste(t, ((i % cols) * 96, (i // cols) * 96))
    return sheet.quantize(colors=GLOBAL_COLORS, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)


def main() -> None:
    global _global_palette
    manifest = json.loads((ROOT / "assets" / "manifest.json").read_text(encoding="utf-8"))
    _global_palette = build_global_palette(manifest)
    pix, compare = [], []
    for a in manifest["assets"]:
        src = ROOT / "assets" / "gen" / a["cat"] / f"{a['id']}.png"
        if not src.exists():
            continue
        if a.get("sheet"):
            strip = process_sheet(a, src)
            strip.resize((strip.width * 3, strip.height * 3), Image.NEAREST).save(ROOT / "assets" / f"preview_{a['id']}.png")
            print(f"{a['id']:<24} sheet -> {a['sheet'][0] * a['sheet'][1]} frames")
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
