"""Куст с ягодами (подобрать) — по 2D-спрайту rprop_berry_bush_01: густой зелёный куст из нескольких
комков листвы, синие ягоды гроздями поверх, пара веток у земли. 1,5 × 1,27 × 0,89 м."""
import math


def build(k):
    k.foliage((0, 0, 0.42), (0.62, 0.52, 0.38), n=34, clump=0.15, mats=("leaf", "leaf", "leaf_dark"), seed=8)
    rng = __import__("random").Random(5)
    for i in range(26):                                                                         # ягоды
        a = rng.uniform(0, 2 * math.pi)
        rr = rng.uniform(0.15, 0.6)
        x, y = rr * math.cos(a), rr * math.sin(a) * 0.8
        z = 0.35 + math.sqrt(max(0.0, 0.42 ** 2 - (rr * 0.6) ** 2)) * 0.9
        k.blob(0.035, (x, y, z), "berry", subdiv=1, jitter=0.05, seed=100 + i)
    for i in range(4):                                                                          # ветки у земли
        a = i * 1.6
        ob = k.cyl(0.015, 0.4, (0, 0, 0), "wood_dark", segments=5, bevel=0)
        ob.rotation_euler = (0.9, 0, a)
        ob.location = (0.1 * math.cos(a), 0.1 * math.sin(a), 0.0)
