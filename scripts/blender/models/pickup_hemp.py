"""Конопля (подобрать) — по 2D-спрайту hemp_plant: высокий куст из нескольких стеблей, на каждом ярусы
острых пальчатых листьев (плоские лопасти веером), метёлки сверху. 0,66 × 0,67 × 1,12 м."""
import math


def leaf_fan(k, at, size, yaw, tilt=0.5):
    """Пальчатый лист: 5–7 узких лопастей веером."""
    for j, (da, ln) in enumerate(((0, 1.0), (0.45, 0.85), (-0.45, 0.85), (0.9, 0.6), (-0.9, 0.6))):
        ob = k.box((size * ln, size * 0.12, 0.004), (0, 0, 0), "hemp", bevel=0)
        a = yaw + da
        ob.rotation_euler = (0, -tilt, a)
        ob.location = (at[0] + math.cos(a) * size * ln * 0.45, at[1] + math.sin(a) * size * ln * 0.45, at[2])


def build(k):
    stems = ((0, 0, 1.1, 0), (0.12, 0.08, 0.9, 1.5), (-0.1, 0.1, 0.85, 3.0), (0.02, -0.13, 0.95, 4.4))
    for x, y, h, rot in stems:
        k.cyl(0.012, h, (x, y, 0), "hemp", segments=6, bevel=0)
        for i in range(4):                                                                       # ярусы листьев
            z = h * (0.35 + i * 0.17)
            for s in range(2):
                leaf_fan(k, (x, y, z), 0.28 - i * 0.04, rot + i * 1.1 + s * math.pi, tilt=0.35)
        k.blob(0.05, (x, y, h), "hemp", subdiv=1, scale=(0.7, 0.7, 2.2), jitter=0.2, seed=int(rot * 10))   # метёлка
