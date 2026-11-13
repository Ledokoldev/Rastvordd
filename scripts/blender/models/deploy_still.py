"""Самогонный аппарат: медный куб на кирпичной топке, змеевик в бочке-холодильнике, банка-приёмник.
Всё на дощатом настиле. Акцент — медь. След 1,7 × 0,85."""
import math


def build(k):
    k.box((1.66, 0.8, 0.05), (0, 0, 0), "planks", bevel=0.008)                         # настил
    k.box((0.56, 0.56, 0.38), (-0.5, 0, 0.05), "concrete", bevel=0.015)                  # топка
    k.box((0.2, 0.02, 0.14), (-0.5, -0.285, 0.1), "black", bevel=0.004)                  # дверца-зев
    k.cyl(0.05, 1.0, (-0.72, 0.2, 0.43), "rust_dark", segments=10, bevel=0.004)          # труба топки
    k.cyl(0.24, 0.42, (-0.5, 0, 0.43), "copper", segments=20, bevel=0.01, radius_top=0.2)  # куб
    k.cyl(0.21, 0.04, (-0.5, 0, 0.85), "copper", segments=20, bevel=0.008)                # крышка
    k.cyl(0.05, 0.12, (-0.5, 0, 0.89), "copper", segments=12, bevel=0.005, radius_top=0.03)
    for i in range(4):                                                                   # струбцины крышки
        a = i * math.pi / 2 + 0.4
        k.box((0.03, 0.03, 0.08), (-0.5 + 0.22 * math.cos(a), 0.22 * math.sin(a), 0.8), "steel", bevel=0.004)
    k.cyl(0.018, 0.62, (-0.47, 0, 1.0), "copper", segments=8, axis="X", bevel=0)          # пароотвод
    k.cyl(0.018, 0.28, (0.15, 0, 0.74), "copper", segments=8, bevel=0)
    # бочка-холодильник с водой и змеевиком
    k.cyl(0.27, 0.62, (0.3, 0.05, 0.05), "tin", segments=18, bevel=0.01)
    for z in (0.06, 0.62):
        k.cyl(0.282, 0.025, (0.3, 0.05, z), "steel", segments=18, bevel=0.004)
    k.cyl(0.25, 0.01, (0.3, 0.05, 0.6), "glass", segments=18, bevel=0)                   # вода
    for i in range(3):                                                                   # витки змеевика
        k.cyl(0.13 - i * 0.02, 0.025, (0.3, 0.05, 0.69 + i * 0.03), "copper", segments=14, bevel=0.003)
    k.cyl(0.015, 0.14, (0.57, 0.05, 0.22), "copper", segments=8, axis="X", bevel=0)       # кран
    k.cyl(0.075, 0.18, (0.72, 0.05, 0.05), "glass", segments=14, bevel=0.006)            # банка
    k.cyl(0.06, 0.03, (0.72, 0.05, 0.23), "rust", segments=12, bevel=0.003)
    for x in (-0.05, 0.12):                                                              # поленья у топки
        k.cyl(0.05, 0.45, (x - 0.45, -0.3, 0.1), "planks", segments=8, axis="Y", bevel=0.006)
