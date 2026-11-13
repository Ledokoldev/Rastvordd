"""Печь — по 2D-спрайту furnace_lit: каменная купольная печь-горн на цоколе, арочный зев с огнём
(акцент — свечение), обвязка из ржавой полосы, железная труба сверху. 1,1 × 1,1 × 1,8 м."""
import math


def build(k):
    k.box((1.08, 1.08, 0.2), (0, 0, 0), "concrete", bevel=0.03)                                 # цоколь
    k.cyl(0.5, 0.45, (0, 0, 0.2), "concrete", segments=16, bevel=0.03, radius_top=0.48)          # нижний пояс
    k.blob(0.48, (0, 0, 0.62), "concrete", subdiv=3, scale=(1, 1, 0.9), jitter=0.05, seed=3)      # купол
    for i in range(10):                                                                           # камни кладки
        a = i * 2 * math.pi / 10 + 0.2
        k.blob(0.09, (0.5 * math.cos(a), 0.5 * math.sin(a), 0.32 + (i % 2) * 0.18), "concrete",
               subdiv=1, scale=(1, 1, 0.7), jitter=0.2, seed=10 + i)
    k.cyl(0.505, 0.04, (0, 0, 0.62), "rust", segments=16, bevel=0.004)                           # обвязка
    # арочный зев спереди (-Y) с огнём
    k.box((0.42, 0.2, 0.34), (0, -0.42, 0.22), "rust_dark", bevel=0.02)
    k.cyl(0.21, 0.2, (0, -0.52, 0.56), "rust_dark", segments=12, axis="Y", bevel=0.01)
    k.box((0.32, 0.04, 0.26), (0, -0.53, 0.26), "glow", bevel=0)
    k.cyl(0.16, 0.04, (0, -0.57, 0.52), "glow", segments=12, axis="Y", bevel=0)
    k.box((0.5, 0.12, 0.05), (0, -0.55, 0.2), "steel", bevel=0.006)                              # порог
    k.cyl(0.1, 0.75, (0.05, 0.1, 1.0), "rust_dark", segments=12, bevel=0.006)                     # труба
    k.cyl(0.13, 0.05, (0.05, 0.1, 1.72), "rust", segments=12, bevel=0.004)
