"""Буровая установка — по 2D-спрайту drill_rig: четырёхногая сужающаяся вышка из труб с поясами и
раскосами, кронблок, штанга в скважине с отвалом породы, дизель с лебёдкой сбоку. Квадратный след 1,85, ~2,8 м."""
import math


def strut(k, a, b, r, mat="rust"):
    """Труба-распорка от точки a к точке b."""
    d = [b[i] - a[i] for i in range(3)]
    ln = math.sqrt(sum(c * c for c in d))
    ob = k.cyl(r, ln, (0, 0, 0), mat, segments=6, bevel=0)
    ob.rotation_euler = (0, math.acos(d[2] / ln), math.atan2(d[1], d[0]))
    ob.location = a
    return ob


def build(k):
    k.box((1.8, 1.8, 0.1), (0, 0, 0), "planks", bevel=0.01)                                     # настил
    cx, cy = -0.2, 0.2                                                                           # вышка в углу, дизель сбоку
    b, t, H = 0.5, 0.12, 2.75
    feet = [(cx + sx * b, cy + sy * b, 0.1) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    tops = [(cx + sx * t, cy + sy * t, H) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    for f, p in zip(feet, tops):
        strut(k, f, p, 0.04, "paint_green")

    def at(i, h):
        return tuple(feet[i][j] + (tops[i][j] - feet[i][j]) * h for j in range(3))
    for i in range(4):                                                                           # пояса и раскосы
        n = (i + 1) % 4
        for h in (0.28, 0.56, 0.8):
            strut(k, at(i, h), at(n, h), 0.022)
        strut(k, at(i, 0.0), at(n, 0.28), 0.018)
        strut(k, at(i, 0.28), at(n, 0.56), 0.018)
    k.box((0.36, 0.36, 0.14), (cx, cy, H), "rust_dark", bevel=0.01)                              # кронблок
    k.cyl(0.035, 2.6, (cx, cy, 0.1), "steel", segments=10, bevel=0)                               # штанга
    k.cyl(0.08, 0.14, (cx, cy, 1.3), "brass", segments=10, bevel=0.006)                          # вертлюг
    for i in range(9):                                                                           # отвал породы
        a = i * 0.7
        k.box((0.12, 0.1, 0.07), (cx + 0.25 * math.cos(a), cy + 0.22 * math.sin(a), 0.1), "concrete", rot_z=a, bevel=0.02)
    k.box((0.5, 0.45, 0.42), (0.55, -0.55, 0.1), "paint_green", bevel=0.015)                     # дизель
    k.cyl(0.13, 0.4, (0.55, -0.27, 0.36), "steel", segments=12, axis="Y", bevel=0.006)           # лебёдка
    k.cyl(0.03, 0.35, (0.72, -0.4, 0.52), "rust", segments=8, bevel=0)                          # выхлоп
    k.stencil("УБ-2", (0.55, -0.775, 0.32), size=0.07)
