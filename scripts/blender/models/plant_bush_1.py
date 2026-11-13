"""Куст сухой (декор, по 2D rprop_bush_dry_01): пучок голых веток веером из одного корня, с редкими
бурыми листьями на концах. 1,77 × 1,71 × 1,1 м."""
import math
import random


def build(k):
    rng = random.Random(9)
    for i in range(14):
        a = i * 2 * math.pi / 14 + rng.uniform(-0.2, 0.2)
        tilt = rng.uniform(0.35, 0.9)
        ln = rng.uniform(0.7, 1.15)
        ob = k.cyl(0.02, ln, (0, 0, 0), "bark", segments=5, bevel=0, radius_top=0.006)
        ob.rotation_euler = (0, tilt, a)
        ex = (math.sin(tilt) * math.cos(a) * ln, math.sin(tilt) * math.sin(a) * ln, math.cos(tilt) * ln)
        if i % 3 == 0:                                                       # редкие сухие листья пучком
            k.blob(0.05, ex, "leaf_dry", subdiv=1, scale=(1.4, 0.8, 0.5), jitter=0.3, seed=60 + i, smooth=True)
        side = k.cyl(0.01, ln * 0.4, (0, 0, 0), "bark", segments=4, bevel=0, radius_top=0.004)   # боковая ветка
        side.rotation_euler = (0, tilt + 0.5, a + 0.4)
        side.location = (ex[0] * 0.5, ex[1] * 0.5, ex[2] * 0.5)
