"""Куст зелёный (декор, по 2D bush_1): густая рваная шапка листвы из мелких комков двух тонов,
пара веток у земли. 1,36 × 1,29 × 0,85 м."""
import math


def build(k):
    k.foliage((0, 0, 0.42), (0.55, 0.5, 0.36), n=30, clump=0.15, seed=4)
    for i in range(4):
        a = i * 1.7
        ob = k.cyl(0.02, 0.35, (0, 0, 0), "bark", segments=5, bevel=0)
        ob.rotation_euler = (0.8, 0, a)
        ob.location = (0.05 * math.cos(a), 0.05 * math.sin(a), 0.0)
