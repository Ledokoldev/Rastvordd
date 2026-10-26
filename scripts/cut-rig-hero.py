# Набор частей рига (game/art/rig/rig_<set>_<part>.png) нарезкой ОДНОЙ нарисованной фигуры, а не отдельными генерациями
# (отдельно нарисованные части выходили короткими «обрубками» и разного цвета).
#
# Конвейер:
#   1. Blender (scripts/cut-rig-render.py): манекен в A-позе, ракурс игры, d1 (спереди-справа) и d3 (сзади-справа):
#      затенённый рендер-образец, карта частей (плоские цвета по костям) и суставы в пикселях.
#   2. Генерация (Codex, scripts/gen-assets.ps1): тот же человек в той же позе — образец assets/gen/rigcut_ref/ref_d<N>.png
#      (слева манекен, справа нарисованный герой). Результат: assets/gen/rigcut/<set>_apose_d<N>.png.
#   3. Этот скрипт: совмещает фигуру с картой частей (рамка + перебор масштаба/сдвига по перекрытию силуэтов), раздаёт
#      пиксели частям (ближайшая метка), режет части с нахлёстом на соседей в суставах, поворачивает в оси рига
#      (Rig.cs: бедро/голень вертикально, сустав сверху; плечо/предплечье горизонтально, кулак справа), уменьшает до RIG_SIZE
#      как process-assets.py (BOX, общая палитра, жёсткая альфа).
#
# Запуск:
#   blender -b --python scripts/cut-rig-render.py -- C:/Temp/claude/rigcut/blend 1024      (CHAR_TILT=30 по умолчанию)
#   python scripts/cut-rig-hero.py --set hero --make-ref assets/gen/charvar/hero30_idle.png   (образцы + манифест генерации)
#   python scripts/cut-rig-hero.py --set hero [--blend C:/Temp/claude/rigcut/blend] [--front assets/gen/rigcut/hero_apose_d1.png]
#          [--back assets/gen/rigcut/hero_apose_d3.png] [--out game/art/rig] [--debug C:/Temp/claude/rigcut]
import argparse, json, pathlib, math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = pathlib.Path(__file__).resolve().parent.parent
RIG_SIZE = {"head": ("h", 12), "torso": ("h", 19), "pelvis": ("h", 8),
            "upperarm": ("w", 10), "forearm": ("w", 11), "thigh": ("h", 14), "shin": ("h", 15)}
NECK_CUT = 0.6   # доля кости шеи: ниже — корпус
OV = 2.0          # нахлёст на соседа в суставе, пикселей готового спрайта
COLORS = 32
# штаны на рисунке мешковатые: шире этого ноги в риге сливаются в один брус (бёдра в 9 px друг от друга, LegThick ×1,25)
MAX_W = {"thigh": 6, "shin": 7}


# ---------- фигура ----------
def cut_background(im: Image.Image) -> Image.Image:
    """Прозрачный фон: если генератор дал непрозрачный — заливка от углов по близкому цвету."""
    im = im.convert("RGBA")
    a = np.asarray(im)[..., 3]
    if (a < 20).mean() > 0.05:
        return im
    rgb = im.convert("RGB")
    probe = rgb.copy()
    w, h = rgb.size
    for c in ((0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)):
        ImageDraw.floodfill(probe, c, (255, 0, 255), thresh=40)
    bg = np.all(np.asarray(probe) == (255, 0, 255), axis=-1)
    arr = np.asarray(im).copy()
    arr[bg, 3] = 0
    return Image.fromarray(arr)


def clean_alpha(im: Image.Image) -> Image.Image:
    r, g, b, a = im.split()
    a = a.point(lambda v: 0 if v < 48 else v).filter(ImageFilter.MinFilter(3))
    return Image.merge("RGBA", (r, g, b, a))


def mask_of(im: Image.Image, thr=128) -> np.ndarray:
    return np.asarray(im.convert("RGBA"))[..., 3] >= thr


def bbox(m: np.ndarray):
    ys, xs = np.nonzero(m)
    return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1


def align(fig: Image.Image, lab_mask: np.ndarray) -> Image.Image:
    """Фигура → кадр карты частей: по рамкам, затем перебор масштаба по X/Y и сдвига (максимум перекрытия силуэтов)."""
    H, W = lab_mask.shape
    fx0, fy0, fx1, fy1 = bbox(mask_of(fig))
    lx0, ly0, lx1, ly1 = bbox(lab_mask)
    sx0, sy0 = (lx1 - lx0) / (fx1 - fx0), (ly1 - ly0) / (fy1 - fy0)
    k = 4   # перебор на уменьшенных масках
    small_lab = np.asarray(Image.fromarray(lab_mask.astype(np.uint8) * 255).resize((W // k, H // k), Image.BOX)) > 127
    fa = fig.getchannel("A")

    def warp(sx, sy, tx, ty, size, scale=1):
        # точка кадра (u, v) ← точка фигуры: x = fx0 + (u/scale - lx0 - tx) / sx
        return fa.transform(size, Image.AFFINE,
                            (1 / (sx * scale), 0, fx0 - (lx0 + tx) / sx, 0, 1 / (sy * scale), fy0 - (ly0 + ty) / sy),
                            Image.BILINEAR)
    best = (-1, None)
    for dsx in np.linspace(-0.08, 0.08, 9):
        for dsy in np.linspace(-0.06, 0.06, 7):
            sx, sy = sx0 * (1 + dsx), sy0 * (1 + dsy)
            # центр рамки на месте: сдвиг компенсирует изменение масштаба
            cx = ((fx1 - fx0) * sx0 - (fx1 - fx0) * sx) / 2
            cy = ((fy1 - fy0) * sy0 - (fy1 - fy0) * sy) / 2
            for tx in range(-24, 25, 8):
                for ty in range(-16, 17, 8):
                    m = np.asarray(warp(sx, sy, cx + tx, cy + ty, (W // k, H // k), 1 / k)) > 127
                    iou = (m & small_lab).sum() / max(1, (m | small_lab).sum())
                    if iou > best[0]:
                        best = (iou, (sx, sy, cx + tx, cy + ty))
    sx, sy, tx, ty = best[1]
    print(f"  совмещение: IoU {best[0]:.3f}, масштаб {sx:.3f}/{sy:.3f}, сдвиг {tx:.0f},{ty:.0f}")
    out = fig.transform((W, H), Image.AFFINE, (1 / sx, 0, fx0 - (lx0 + tx) / sx, 0, 1 / sy, fy0 - (ly0 + ty) / sy), Image.BICUBIC)
    return out, best[0]


# ---------- карта частей ----------
def read_labels(path: pathlib.Path, names_rgb: dict) -> np.ndarray:
    """Номер части на пиксель (−1 — пусто): ближайший из цветов карты."""
    arr = np.asarray(Image.open(path).convert("RGBA")).astype(np.float32)
    lin = np.array([names_rgb[n] for n in names_rgb], np.float32)
    # Blender пишет цвета материала через sRGB: 0,5 линейного → ~188
    cols = np.where(lin <= 0.0031308, lin * 12.92, 1.055 * np.power(lin, 1 / 2.4) - 0.055) * 255
    d = ((arr[..., None, :3] - cols[None, None]) ** 2).sum(-1)
    lab = d.argmin(-1)
    lab[arr[..., 3] < 128] = -1
    return lab


def grow(lab: np.ndarray, want: np.ndarray) -> np.ndarray:
    """Пиксели фигуры вне карты получают ближайшую метку (наращивание по соседям)."""
    lab = lab.copy()
    lab[~want] = -1
    todo = want & (lab < 0)
    full = lab.copy()
    src = np.where(want, -1, -1)
    known = full >= 0
    cur = np.where(known, full, -1)
    # растим метки карты (вся карта, не только внутри фигуры) пока не покроем фигуру
    for _ in range(400):
        if not (todo & (cur < 0)).any():
            break
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            sh = np.roll(np.roll(cur, dy, 0), dx, 1)
            fill = (cur < 0) & (sh >= 0)
            cur[fill] = sh[fill]
    return cur


def smooth(lab: np.ndarray, n: int, r: float = 6) -> np.ndarray:
    """Сглаживает границы частей: карта по граням манекена «пилой» — размытие масок и выбор наибольшей."""
    want = lab >= 0
    acc = np.stack([np.asarray(Image.fromarray(((lab == i) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(r)), np.float32)
                    for i in range(n)])
    out = acc.argmax(0)
    out[~want] = -1
    return out


# ---------- геометрия ----------
def P(j, bone, end="head"):
    return np.array(j[bone][end][:2], np.float64)


def rotate_crop(img: Image.Image, mask: np.ndarray, A, axis_ang, target_ang):
    """Поворот вокруг A так, чтобы направление axis_ang стало target_ang; возвращает RGBA (вне маски прозрачно) и функцию точки."""
    arr = np.asarray(img).copy()
    arr[~mask, 3] = 0
    im = Image.fromarray(arr)
    rot = math.degrees(target_ang - axis_ang)          # против часовой в координатах экрана (y вниз) — знак ниже
    # PIL.rotate: положительный угол — против часовой на экране; в координатах y-вниз это поворот на −angle
    out = im.rotate(-rot, resample=Image.BICUBIC, center=tuple(A), expand=False)
    c, s = math.cos(math.radians(rot)), math.sin(math.radians(rot))
    def tp(p):
        d = np.asarray(p, np.float64) - A
        return A + np.array([c * d[0] - s * d[1], s * d[0] + c * d[1]])
    return out, tp


def pixelize(im: Image.Image, size, palette: Image.Image) -> Image.Image:
    small = im.resize(size, Image.BOX if im.width >= size[0] * 2 else Image.LANCZOS)
    rgb = small.convert("RGB").filter(ImageFilter.UnsharpMask(radius=1.2, percent=90, threshold=2))
    rgb = rgb.quantize(palette=palette, dither=Image.Dither.NONE).convert("RGB")
    out = rgb.convert("RGBA")
    out.putalpha(small.getchannel("A").point(lambda v: 255 if v >= 110 else 0))
    return out


def cut_part(fig, lab, names, j, part, side, scale_hint=None):
    """Возвращает (кусок большого разрешения, повернутый и обрезанный по окну, финальный размер)."""
    idx = {n: i for i, n in enumerate(names)}
    discs = {}
    S = side
    if part == "thigh":
        A, B = P(j, f"DEF-thigh.{S}"), P(j, f"DEF-thigh.{S}", "tail")
        own, nb = [f"thigh_{S}"], {f"pelvis": A, f"shin_{S}": B}
        target = math.pi / 2
    elif part == "shin":
        A, B = P(j, f"DEF-shin.{S}"), P(j, f"DEF-shin.{S}", "tail")
        own, nb = [f"shin_{S}"], {f"thigh_{S}": A}
        target = math.pi / 2
    elif part == "upperarm":
        A, B = P(j, f"DEF-upper_arm.{S}"), P(j, f"DEF-upper_arm.{S}", "tail")
        own, nb = [f"upperarm_{S}"], {"torso": A, f"forearm_{S}": B}
        target = 0.0
    elif part == "forearm":
        A, B = P(j, f"DEF-forearm.{S}"), P(j, f"DEF-hand.{S}", "tail")
        own, nb = [f"forearm_{S}"], {f"upperarm_{S}": A}
        target = 0.0
    elif part == "torso":
        A, B = P(j, "DEF-spine.002"), P(j, "DEF-neck")          # талия → основание шеи
        own, nb = ["torso"], {"pelvis": A, "head": B}
        discs = {f"upperarm_{x}": P(j, f"DEF-upper_arm.{x}") for x in "LR"}
        target = -math.pi / 2
    elif part == "pelvis":
        A, B = P(j, "DEF-hips"), P(j, "DEF-spine.002")
        own, nb = ["pelvis"], {"torso": B}
        target = -math.pi / 2
    else:  # head
        A, B = P(j, "DEF-neck"), P(j, "DEF-head", "tail")
        own, nb = ["head"], {}
        target = -math.pi / 2
    if part in ("head", "torso"):
        # шея: голове — только выше среза NECK_CUT по кости шеи (воротник ниже — корпусу)
        N0, N1 = P(j, "DEF-neck"), P(j, "DEF-neck", "tail")
        C = N0 + (N1 - N0) * NECK_CUT
        un = (N1 - N0) / np.linalg.norm(N1 - N0)
        H_, W_ = lab.shape
        yy_, xx_ = np.mgrid[0:H_, 0:W_]
        above = (xx_ - C[0]) * un[0] + (yy_ - C[1]) * un[1] > 0
        lab = lab.copy()
        lab[(lab == idx["head"]) & ~above] = idx["torso"]
        # воротник по бокам от шеи: ниже черепа голове — только в пределах ширины черепа
        Hh = P(j, "DEF-head")
        sp = (xx_ - Hh[0]) * un[1] - (yy_ - Hh[1]) * un[0]
        th = (xx_ - Hh[0]) * un[0] + (yy_ - Hh[1]) * un[1]
        hm = lab == idx["head"]
        skull = hm & (th > np.percentile(th[hm], 99.5) * 0.5)   # верхняя половина головы — точно череп, без воротника
        if skull.any():
            lo, hi = np.percentile(sp[skull], 1), np.percentile(sp[skull], 99)
            lab[(lab == idx["head"]) & ~skull & ((sp < lo * 1.05) | (sp > hi * 1.05))] = idx["torso"]
        if part == "head":
            A = C
    axis = math.atan2(B[1] - A[1], B[0] - A[0])
    u = np.array([math.cos(axis), math.sin(axis)])
    H, W = lab.shape
    yy, xx = np.mgrid[0:H, 0:W]
    mask = np.isin(lab, [idx[o] for o in own])
    # оценка масштаба: длина главной оси в спрайте
    kind, L = RIG_SIZE[part]
    seg = float(np.linalg.norm(B - A))
    if part in ("thigh", "upperarm"):
        s = (L - 1 - OV) / seg                              # сустав A в 1 px от края, дальний сустав на OV px раньше конца
    else:
        s = scale_hint or None
    ov_px = OV / s if s else 30.0
    # нахлёст: пиксели соседа до OV px за суставом вдоль оси части (полоса, края скругляет силуэт соседа)
    perp = np.abs((xx - A[0]) * u[1] - (yy - A[1]) * u[0])   # расстояние от оси части
    hw = np.percentile(perp[mask], 92) if mask.any() else 20
    for n, J in nb.items():
        t = (xx - J[0]) * u[0] + (yy - J[1]) * u[1]
        sign = 1 if np.dot(J - A, u) > seg / 2 else -1        # дальний сустав — за ним «плюс», ближний — «минус»
        band = (t * sign <= ov_px) & (np.hypot(xx - J[0], yy - J[1]) <= hw * 1.3 + ov_px) & (perp <= hw * 1.1)
        mask |= band & (lab == idx[n])
    for n, J in discs.items():
        mask |= (np.hypot(xx - J[0], yy - J[1]) <= ov_px) & (lab == idx[n])
    img, tp = rotate_crop(fig, mask, A, axis, target)
    rm = mask_of(img, 60)
    if not rm.any():
        raise SystemExit(f"пустая часть {part}")
    x0, y0, x1, y1 = bbox(rm)
    A2, B2 = tp(A), tp(B)
    if part in ("thigh", "shin"):
        top = A2[1] - 1 / (s or (L - 1) / (y1 - A2[1]))
        if part == "shin":
            s = (L - 1) / (y1 - A2[1])                       # колено в 1 px от верха, подошва — низ
            top = A2[1] - 1 / s
        win = (x0, top, x1, top + L / s)
        size = (max(1, round((x1 - x0) * s)), L)
    elif part in ("upperarm", "forearm"):
        if part == "forearm":
            s = (L - 1) / (x1 - A2[0])                       # локоть в 1 px от края, кулак — правый край
        left = A2[0] - 1 / s
        win = (left, y0, left + L / s, y1)
        size = (L, max(1, round((y1 - y0) * s)))
    else:
        # корпус/таз/голова: по высоте рамки, как process-assets
        if part == "torso":
            bot = A2[1] + ov_px                               # талия + нахлёст в таз
            y1 = min(y1, bot)
        s = L / (y1 - y0)
        win = (x0, y0, x1, y1)
        size = (max(1, round((x1 - x0) * s)), L)
    crop = img.crop(tuple(round(v) for v in win))
    if part in MAX_W:
        size = (min(size[0], MAX_W[part]), size[1])
    return crop, size, s


def make_ref(blend: pathlib.Path, set_name: str, sample: str, boxes, outfit: str):
    """Шаг 2: образцы для генерации (слева манекен в A-позе, справа нарисованный персонаж) и манифест для gen-assets.ps1."""
    src = Image.open(sample).convert("RGB")
    refdir = ROOT / "assets" / "gen" / "rigcut_ref"
    refdir.mkdir(parents=True, exist_ok=True)
    views = {1: "three-quarter view from the front-right, seen from slightly above; he faces towards the lower right",
             3: "three-quarter view from behind (back-right), seen from slightly above; we see his back and the back of his head"}
    assets = []
    for d, box in zip((1, 3), boxes):
        ap = Image.open(blend / f"apose_d{d}.png").convert("RGBA")
        ref = Image.new("RGB", (1536, 1024), (255, 255, 255))
        ref.paste(ap, (0, 0), ap)
        ref.paste(src.crop(box), (1150, 120))
        dr = ImageDraw.Draw(ref)
        dr.line((1080, 0, 1080, 1024), fill=(0, 0, 0), width=3)
        dr.text((20, 20), "POSE + CAMERA (copy exactly)", fill=(0, 0, 0))
        dr.text((1100, 40), "OUTFIT / FACE / STYLE", fill=(0, 0, 0))
        ref.save(refdir / f"ref_{set_name}_d{d}.png")
        assets.append({"id": f"{set_name}_apose_d{d}", "cat": "rigcut", "ref": f"assets/gen/rigcut_ref/ref_{set_name}_d{d}.png", "desc": (
            "ONE man, full body, standing in an A-pose: copy the pose, body proportions, limb angles and camera angle of the grey 3D mannequin "
            f"in the LEFT panel of the reference image EXACTLY (arms straight and lowered about 40 degrees from horizontal, held away from the body "
            f"so they do not overlap the torso, legs straight and slightly apart, same {views[d]}). Dress and paint him like the man in the RIGHT panel: "
            f"{outfit}. Same silhouette size and placement as the mannequin, the whole figure visible, nothing in the hands. Only the one figure — "
            "do not draw the mannequin, the panels, labels or the right-panel image. Hand-painted like Project Zomboid, muted colours, light from the top-left, transparent background.")})
    (refdir / f"manifest_{set_name}.json").write_text(json.dumps({"assets": assets}, ensure_ascii=False, indent=1), encoding="utf8")
    print(f"образцы готовы; генерация: powershell scripts/gen-assets.ps1 -Parallel 2 -Force -Manifest assets/gen/rigcut_ref/manifest_{set_name}.json")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--set", default="hero")
    ap.add_argument("--blend", default="C:/Temp/claude/rigcut/blend")
    ap.add_argument("--front"); ap.add_argument("--back")
    ap.add_argument("--out", default=str(ROOT / "game" / "art" / "rig"))
    ap.add_argument("--debug", default="C:/Temp/claude/rigcut")
    ap.add_argument("--make-ref", metavar="SAMPLE_PNG", help="только собрать образцы для генерации из листа персонажа")
    ap.add_argument("--boxes", default="80,20,310,490;100,520,340,1005", help="рамки фигуры спереди и сзади в листе-образце")
    ap.add_argument("--outfit", default="athletic heavy build, short dark hair, stubble, square jaw, worn quilted khaki telogreika jacket "
                    "(sleeves rolled to the elbow) over a dark t-shirt, dark grey work trousers, brown boots")
    a = ap.parse_args()
    if a.make_ref:
        boxes = [tuple(int(v) for v in b.split(",")) for b in a.boxes.split(";")]
        make_ref(pathlib.Path(a.blend), a.set, a.make_ref, boxes, a.outfit)
        return
    blend, dbg = pathlib.Path(a.blend), pathlib.Path(a.debug)
    dbg.mkdir(parents=True, exist_ok=True)
    front = pathlib.Path(a.front or ROOT / "assets" / "gen" / "rigcut" / f"{a.set}_apose_d1.png")
    back = pathlib.Path(a.back or ROOT / "assets" / "gen" / "rigcut" / f"{a.set}_apose_d3.png")
    names_rgb = json.load(open(blend / "labels.json"))
    names = list(names_rgb)

    views = {}
    for d, src in ((1, front), (3, back)):
        print(f"d{d}: {src}")
        lab0 = read_labels(blend / f"label_d{d}.png", names_rgb)
        fig = clean_alpha(cut_background(Image.open(src)))
        fig, iou = align(fig, lab0 >= 0)
        want = mask_of(fig, 60)
        lab = grow(lab0, want)
        lab[~want] = -1
        lab = smooth(lab, len(names))
        j = json.load(open(blend / f"joints_d{d}.json"))
        views[d] = (fig, lab, j)
        # отладка: фигура поверх карты
        lv = np.zeros(lab.shape + (3,), np.uint8)
        for i, n in enumerate(names):
            lv[lab == i] = np.array(names_rgb[n]) * 255
        Image.blend(fig.convert("RGB"), Image.fromarray(lv), 0.45).save(dbg / f"assign_d{d}.png")

    # общая палитра частей — по всей фигуре (цвета частей согласованы)
    thumbs = [v[0].resize((v[0].width // 8, v[0].height // 8), Image.BOX) for v in views.values()]
    sheet = Image.new("RGB", (sum(t.width for t in thumbs), thumbs[0].height), (0, 0, 0))
    x = 0
    for t in thumbs:
        rgb = Image.new("RGB", t.size, (0, 0, 0)); m = t.getchannel("A").point(lambda v: 255 if v > 128 else 0)
        sheet.paste(t.convert("RGB"), (x, 0), m); x += t.width
    flat = np.asarray(sheet).reshape(-1, 3)
    keep = flat[flat.sum(1) > 0]
    pal_src = Image.fromarray(keep.reshape(1, -1, 3).astype(np.uint8))
    palette = pal_src.quantize(colors=COLORS, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)

    # конечности — ближняя к камере сторона на виде спереди
    fig1, lab1, j1 = views[1]
    def depth(side):
        return np.mean([j1[f"DEF-{b}.{side}"]["head"][2] for b in ("upper_arm", "thigh", "shin", "forearm")])
    near = "L" if depth("L") < depth("R") else "R"
    print("  ближняя сторона:", near)

    out = {}
    plan = [("head_f", 1, "head"), ("torso_f", 1, "torso"), ("head_b", 3, "head"), ("torso_b", 3, "torso"),
            ("pelvis", 1, "pelvis"), ("thigh", 1, "thigh"), ("shin", 1, "shin"), ("upperarm", 1, "upperarm"), ("forearm", 1, "forearm")]
    for name, d, part in plan:
        fig, lab, j = views[d]
        crop, size, s = cut_part(fig, lab, names, j, part, near)
        crop.save(dbg / f"hd_{name}.png")
        out[name] = pixelize(crop, size, palette)
        print(f"  {name}: {size}")
    outdir = pathlib.Path(a.out)
    for name, im in out.items():
        im.save(outdir / f"rig_{a.set}_{name}.png")
    # превью частей ×6
    sc, pad = 6, 8
    W = sum(im.width * sc + pad for im in out.values()) + pad
    H = max(im.height for im in out.values()) * sc + 2 * pad
    pv = Image.new("RGBA", (W, H), (70, 74, 70, 255)); x = pad
    for im in out.values():
        big = im.resize((im.width * sc, im.height * sc), Image.NEAREST)
        pv.paste(big, (x, pad), big); x += big.width + pad
    pv.save(dbg / "parts.png")
    print("готово:", outdir)


if __name__ == "__main__":
    main()
