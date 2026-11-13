"""Куча хлама (ресурс) — по 2D-спрайту junk_pile: невысокий холм из ржавого лома — покрышки, листы,
трубы, бочка на боку, ящики, кирпичи. Без акцентов, но с пятнами старой краски. 2,15 × 1,9 × 0,67 м."""
import math


def build(k):
    k.blob(0.85, (0, 0, 0), "rust_dark", subdiv=2, scale=(1.2, 1.05, 0.42), jitter=0.18, seed=7, flat=True)   # основа
    k.torus(0.24, 0.07, (-0.55, 0.35, 0.22), "rubber", rot=(0.5, 0.2, 0), tread=0.6)            # покрышки
    k.torus(0.24, 0.07, (0.45, -0.45, 0.12), "rubber", rot=(0.2, -0.3, 0), tread=0.6)
    k.torus(0.22, 0.065, (0.15, 0.55, 0.3), "rubber", rot=(1.2, 0, 0.6), tread=0.6)
    sheets = ((0.0, 0.0, 0.42, 0.9, 0.6, 0.35, 0.4, "tin"), (-0.4, -0.2, 0.3, 0.7, 0.5, -0.5, 1.1, "paint_green"),
              (0.6, 0.15, 0.25, 0.6, 0.45, 0.6, -0.4, "paint_blue"), (-0.2, 0.45, 0.35, 0.5, 0.4, -0.3, 2.2, "rust"))
    for x, y, z, l, w, tilt, yaw, m in sheets:                                                  # листы
        ob = k.box((l, w, 0.012), (0, 0, 0), m, bevel=0.003)
        ob.rotation_euler = (tilt, 0.2, yaw)
        ob.location = (x, y, z)
    for x, y, z, ln, yaw in ((0.2, -0.1, 0.55, 1.1, 0.4), (-0.5, 0.0, 0.4, 0.8, -0.9)):          # трубы
        ob = k.cyl(0.04, ln, (0, 0, 0), "rust", segments=8, axis="X", bevel=0)
        ob.rotation_euler.z = yaw
        ob.location = (x - ln / 2 * math.cos(yaw), y - ln / 2 * math.sin(yaw), z)
    b = k.cyl(0.22, 0.6, (0, 0, 0), "paint_green", segments=14, axis="X", bevel=0.01)          # бочка на боку
    b.rotation_euler.z = 0.5
    b.location = (-0.85, -0.35, 0.22)
    k.box((0.4, 0.3, 0.25), (0.75, 0.4, 0.05), "planks_green", rot_z=0.3, bevel=0.01)          # ящик
    for i in range(5):                                                                          # кирпичи
        a = i * 1.3
        k.box((0.22, 0.11, 0.07), (0.85 * math.cos(a), 0.75 * math.sin(a), 0.0), "concrete", rot_z=a, bevel=0.008)
