"""
Превью моделей: python scripts/preview-3d-assets.py [фильтр] → screenshots/art3d-preview.png
Читает .glb обратно (тот же формат, что увидит Godot), применяет иерархию узлов и рисует с освещением
под углом камеры игры (сверху ~55°). Нужно, чтобы проверять модели без запуска движка.
"""
import json
import math
import os
import struct
import sys

import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
from mpl_toolkits.mplot3d.art3d import Poly3DCollection  # noqa: E402

ROOT = os.path.join(os.path.dirname(__file__), "..")
SRC = os.path.join(ROOT, "game", "art3d")


def quat_m(q):
    x, y, z, w = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def load(path):
    data = open(path, "rb").read()
    jl = struct.unpack_from("<I", data, 12)[0]
    js = json.loads(data[20:20 + jl])
    bin_ = data[28 + jl:]
    tris = []

    def acc(i):
        a = js["accessors"][i]; bv = js["bufferViews"][a["bufferView"]]
        return np.frombuffer(bin_, dtype=np.float32, count=a["count"] * 3, offset=bv["byteOffset"]).reshape(-1, 3)

    def walk(ni, R, t):
        nd = js["nodes"][ni]
        lr = quat_m(nd["rotation"]) if "rotation" in nd else np.eye(3)
        lt = np.array(nd.get("translation", [0, 0, 0]))
        R2 = R @ lr; t2 = R @ lt + t
        if "mesh" in nd:
            for p in js["meshes"][nd["mesh"]]["primitives"]:
                v = acc(p["attributes"]["POSITION"]) @ R2.T + t2
                col = js["materials"][p["material"]]["pbrMetallicRoughness"]["baseColorFactor"][:3]
                em = js["materials"][p["material"]].get("emissiveFactor")
                for k in range(0, len(v), 3):
                    tris.append((v[k:k + 3], col, em is not None))
        for c in nd.get("children", []):
            walk(c, R2, t2)

    walk(0, np.eye(3), np.zeros(3))
    return tris


def draw(ax, tris, title):
    light = np.array([0.4, 0.8, 0.45]); light /= np.linalg.norm(light)
    polys, cols = [], []
    for v, c, em in tris:
        n = np.cross(v[1] - v[0], v[2] - v[0]); ln = np.linalg.norm(n)
        if ln < 1e-12:
            continue
        n /= ln
        k = 1.0 if em else 0.45 + 0.55 * abs(float(n @ light))
        # в превью: X → X, Z (вправо) → Y графика, Y (вверх) → Z графика
        polys.append([(p[0], p[2], p[1]) for p in v])
        cols.append(tuple(min(1, ch * k) for ch in c))
    pc = Poly3DCollection(polys, facecolors=cols, edgecolors=(0, 0, 0, 0.08), linewidths=0.2)
    ax.add_collection3d(pc)
    allv = np.array([p for poly in polys for p in poly]) if polys else np.zeros((1, 3))
    mn, mx = allv.min(axis=0), allv.max(axis=0)
    c = (mn + mx) / 2; r = max(mx - mn) / 2 + 1e-3
    ax.set_xlim(c[0] - r, c[0] + r); ax.set_ylim(c[1] - r, c[1] + r); ax.set_zlim(c[2] - r, c[2] + r)
    ax.set_box_aspect((1, 1, 1))
    ax.view_init(elev=32, azim=-60)
    ax.set_axis_off()
    size = mx - mn
    ax.set_title(f"{title}\n{size[0]:.2f}×{size[2]:.2f}×{size[1]:.2f} м", fontsize=7)


def main():
    flt = sys.argv[1] if len(sys.argv) > 1 else ""
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(ROOT, "screenshots", "art3d-preview.png")
    names = sorted(f[:-4] for f in os.listdir(SRC) if f.endswith(".glb") and flt in f)
    cols = int(sys.argv[3]) if len(sys.argv) > 3 else 8
    cell = 2.2 * (8 / cols) ** 0.5
    rows = math.ceil(len(names) / cols)
    fig = plt.figure(figsize=(cols * cell, rows * cell * 1.1), facecolor="#d9dde0")
    for i, n in enumerate(names):
        ax = fig.add_subplot(rows, cols, i + 1, projection="3d")
        ax.set_facecolor("#d9dde0")
        draw(ax, load(os.path.join(SRC, n + ".glb")), n)
    plt.tight_layout()
    os.makedirs(os.path.dirname(out), exist_ok=True)
    plt.savefig(out, dpi=80)
    print(out, len(names))


if __name__ == "__main__":
    main()
