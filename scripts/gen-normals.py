# -*- coding: utf-8 -*-
"""Авто-карты нормалей для спрайтов мира (объём под солнце, см. VisionFx Lit).

Для каждого <name>.png в папках мира пишет <name>_n.png того же размера:
высота = гладкий купол из дистанции до края силуэта (округлые массы) + мелкая деталь по яркости;
нормаль = градиент высоты (Собель). Кодировка как ждёт Godot (OpenGL): R = x вправо, G = y ВВЕРХ, B = z;
в шейдере NORMAL уже экранный (y вниз). Альфа копируется. Только numpy + Pillow.

python scripts/gen-normals.py [--force] [папки...]
"""
import os
import sys
import time

import numpy as np
from PIL import Image

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "game", "art")
FOLDERS = ["trees", "plants", "props", "containers", "furniture", "vehicles", "nodes", "outdoor",
           "clutter", "loot", "building", "rooftop", "interior"]
LOW = 128          # дистанция считается на уменьшенной маске (быстро и гладко), потом растягивается
DOME = 1.1         # высота купола относительно его радиуса
DETAIL = 1.6       # вклад яркостной детали, px высоты
STRENGTH = 1.0     # общий множитель наклона


def blur(a, r):
    """Box-blur r раз по 3 (≈ гаусс), края — повтор."""
    for _ in range(r):
        p = np.pad(a, 1, mode="edge")
        a = (p[:-2, 1:-1] + p[2:, 1:-1] + p[1:-1, :-2] + p[1:-1, 2:] + p[1:-1, 1:-1] * 2) / 6.0
    return a


def distance(mask, cap):
    """Октагональная дистанция до фона эрозиями (4/8-соседство через раз), не дальше cap."""
    d = np.zeros(mask.shape, np.float32)
    cur = mask.copy()
    for i in range(cap):
        if not cur.any():
            break
        d += cur
        p = np.pad(cur, 1, constant_values=False)
        e = p[1:-1, 1:-1] & p[:-2, 1:-1] & p[2:, 1:-1] & p[1:-1, :-2] & p[1:-1, 2:]
        if i % 2:
            e &= p[:-2, :-2] & p[:-2, 2:] & p[2:, :-2] & p[2:, 2:]
        cur = e
    return d


def height(rgba):
    a = rgba[..., 3].astype(np.float32) / 255.0
    h, w = a.shape
    f = max(1.0, max(h, w) / LOW)
    lw, lh = max(2, int(round(w / f))), max(2, int(round(h / f)))
    small = np.asarray(Image.fromarray((a * 255).astype(np.uint8)).resize((lw, lh), Image.BILINEAR)) > 100
    d = distance(small, 64)
    d = blur(d, 2)
    dmax = float(d.max())
    if dmax <= 0:
        return None
    # радиус купола: тонкие части (ветки, ножки) — цилиндры, толстые массы — купол с плоской макушкой
    R = max(1.5, dmax * 1.0)
    u = np.clip(d / R, 0, 1)
    dome = np.sqrt(np.clip(2 * u - u * u, 0, 1)) * R * DOME * f            # в пикселях исходника
    dome = np.asarray(Image.fromarray(dome.astype(np.float32), mode="F").resize((w, h), Image.BILINEAR))
    dome = dome * np.clip(a * 1.5, 0, 1)
    # деталь: локальный контраст яркости (светлое чуть выпуклое), без шума по одному пикселю
    rgb = rgba[..., :3].astype(np.float32) / 255.0
    lum = rgb @ np.array([0.3, 0.59, 0.11], np.float32)
    lum = np.where(a > 0.5, lum, blur(lum, 1))
    hp = (blur(lum, 1) - blur(lum, 4)) + 1.5 * (blur(lum, 3) - blur(lum, 12))   # мелкая фактура + крупные «комки» (кроны, вмятины)
    return dome + hp * DETAIL * 8.0 * (a > 0.5)


def normals(hgt, alpha):
    p = np.pad(hgt, 1, mode="edge")
    gx = (p[:-2, 2:] + 2 * p[1:-1, 2:] + p[2:, 2:] - p[:-2, :-2] - 2 * p[1:-1, :-2] - p[2:, :-2]) / 8.0
    gy = (p[2:, :-2] + 2 * p[2:, 1:-1] + p[2:, 2:] - p[:-2, :-2] - 2 * p[:-2, 1:-1] - p[:-2, 2:]) / 8.0   # y вниз
    nx, ny, nz = -gx * STRENGTH, -gy * STRENGTH, np.ones_like(gx)
    ln = np.sqrt(nx * nx + ny * ny + nz * nz)
    nx, ny, nz = nx / ln, ny / ln, nz / ln
    out = np.zeros(hgt.shape + (4,), np.uint8)
    out[..., 0] = np.clip(nx * 127.5 + 127.5, 0, 255).round()
    out[..., 1] = np.clip(-ny * 127.5 + 127.5, 0, 255).round()                   # G вверх (Godot переворачивает)
    out[..., 2] = np.clip(nz * 127.5 + 127.5, 0, 255).round()
    out[..., 3] = alpha
    return out


def main():
    args = [x for x in sys.argv[1:] if not x.startswith("--")]
    force = "--force" in sys.argv
    folders = args or FOLDERS
    t0, n, skip = time.time(), 0, 0
    for fo in folders:
        d = os.path.join(ROOT, fo)
        if not os.path.isdir(d):
            continue
        for name in sorted(os.listdir(d)):
            if not name.endswith(".png") or name.endswith("_n.png"):
                continue
            src = os.path.join(d, name)
            dst = src[:-4] + "_n.png"
            if not force and os.path.exists(dst) and os.path.getmtime(dst) >= os.path.getmtime(src):
                skip += 1
                continue
            rgba = np.asarray(Image.open(src).convert("RGBA"))
            hgt = height(rgba)
            if hgt is None:
                continue
            Image.fromarray(normals(hgt, rgba[..., 3])).save(dst, optimize=False, compress_level=6)
            n += 1
    print(f"normals: {n} written, {skip} up to date, {time.time() - t0:.1f}s")


if __name__ == "__main__":
    main()
