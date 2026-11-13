"""Деревья по породам (как 2D-спрайты trees/tree_<порода>): ствол и ветви — сужающиеся цилиндры,
хвоя — ярусы конусов, листва — комки (k.foliage). Взрослое дерево 6–10 м; возраст масштабирует клиент.
Каждая модель — node_tree_<порода>[_snow|_arid|_swamp]_N.py, вызывающая функцию отсюда с seed."""
import math
import random


def limb(k, a, b, r0, r1, mat="bark", seg=7):
    """Сужающаяся ветвь/ствол от точки a к b (сглаженная)."""
    d = [b[i] - a[i] for i in range(3)]
    ln = math.sqrt(sum(c * c for c in d)) or 0.01
    ob = k.cyl(r0, ln, (0, 0, 0), mat, segments=seg, bevel=0, radius_top=r1)
    ob.rotation_euler = (0, math.acos(max(-1, min(1, d[2] / ln))), math.atan2(d[1], d[0]))
    ob.location = a
    k.smooth_all(ob)
    return ob


def trunk(k, h, r, mat, rng, lean=0.25, parts=3, top_r=None):
    """Ствол из нескольких звеньев с лёгким изгибом; возвращает точки звеньев (низ → верх)."""
    pts = [(0.0, 0.0, -0.15)]
    ang = rng.uniform(0, 2 * math.pi)
    for i in range(1, parts + 1):
        t = i / parts
        off = lean * t * t
        pts.append((off * math.cos(ang) + rng.uniform(-0.08, 0.08), off * math.sin(ang) + rng.uniform(-0.08, 0.08), h * t))
    tr = r * 0.25 if top_r is None else top_r
    for i in range(parts):
        r0 = r + (tr - r) * i / parts
        r1 = r + (tr - r) * (i + 1) / parts
        limb(k, pts[i], pts[i + 1], r0 * (1.35 if i == 0 else 1), r1, mat, seg=8)
    return pts


def branches(k, base, h0, h1, n, length, r, mat, rng, up=0.6, depth=1, twig=0.5):
    """Скелетные ветви от ствола (точки base) между высотами h0..h1; возвращает концы ветвей."""
    ends = []
    for i in range(n):
        z = h0 + (h1 - h0) * (i + rng.uniform(0, 1)) / n
        a = i * 2.39996 + rng.uniform(-0.4, 0.4)
        ln = length * rng.uniform(0.75, 1.15)
        tilt = math.radians(90 - 60 * up) + rng.uniform(-0.15, 0.15)
        s = (base[0], base[1], z)
        e = (s[0] + ln * math.sin(tilt) * math.cos(a), s[1] + ln * math.sin(tilt) * math.sin(a), s[2] + ln * math.cos(tilt))
        limb(k, s, e, r, r * 0.3, mat, seg=6)
        ends.append(e)
        if depth > 1:                                                                     # вторичные ветки
            for j in range(2):
                a2 = a + (j - 0.5) * 1.2
                l2 = ln * twig
                e2 = (e[0] + l2 * 0.7 * math.cos(a2), e[1] + l2 * 0.7 * math.sin(a2), e[2] + l2 * 0.6)
                limb(k, e, e2, r * 0.3, r * 0.1, mat, seg=5)
                ends.append(e2)
    return ends


def snow_caps(k, rng, pts, r):
    """Шапки снега на ярусах / ветвях."""
    for i, p in enumerate(pts):
        k.blob(r * rng.uniform(0.7, 1.1), (p[0], p[1], p[2] + r * 0.3), "snow", subdiv=1, scale=(1, 1, 0.35), jitter=0.25,
               seed=900 + i, smooth=True)


# ---------- хвойные ----------

def ragged(ob, rng, amount=0.18):
    """Ragged lower ring of a cone: spruce paws of different length."""
    zmin = min(v.co.z for v in ob.data.vertices)
    for v in ob.data.vertices:
        if v.co.z < zmin + 0.01:
            f = 1 + rng.uniform(-amount, amount)
            v.co.x *= f
            v.co.y *= f
            v.co.z -= rng.uniform(0, 0.25)


def spruce(k, seed=0, h=8.5, w=2.2, snow=False):
    """Spruce (2D tree_spruce): dense narrow cone of tiers almost to the ground, ragged drooping edges, spike top."""
    rng = random.Random(seed)
    trunk(k, h, 0.22, "bark", rng, lean=0.1, parts=2)
    tiers = 8
    for i in range(tiers):
        t = i / tiers
        z = 0.45 + (h - 1.3) * t
        r = (w * (1 - t) ** 0.95 + 0.3) * rng.uniform(0.9, 1.08)
        th = (h - 0.5) / tiers * 2.0
        ob = k.cyl(r, th, (rng.uniform(-0.06, 0.06), rng.uniform(-0.06, 0.06), z), "needle" if i % 2 else "needle_dark",
                   segments=14, bevel=0, radius_top=r * 0.05)
        ob.rotation_euler.z = rng.uniform(0, math.pi)
        ragged(ob, rng)
        k.smooth_all(ob)
        if snow:                                                                           # snow on the upper part of a tier
            s = k.cyl(r * 0.8, th * 0.55, (ob.location.x, ob.location.y, z + th * 0.42), "snow", segments=14, bevel=0,
                      radius_top=r * 0.05)
            s.rotation_euler.z = rng.uniform(0, math.pi)
            ragged(s, rng, 0.25)
            k.smooth_all(s)


def pine(k, seed=0, h=9.5, snow=False):
    """Сосна: высокий голый рыжеватый ствол, раскидистая неровная крона наверху плоскими шапками (2D tree_pine)."""
    rng = random.Random(seed)
    pts = trunk(k, h, 0.24, "bark_pine", rng, lean=0.6, parts=3)
    top = pts[-1]
    ends = branches(k, top, h * 0.62, h * 0.95, 6, 1.6, 0.09, "bark_pine", rng, up=0.45)
    ends.append(top)
    caps = []
    for i, e in enumerate(ends):
        c = (e[0], e[1], e[2] + 0.1)
        k.foliage(c, (1.2, 1.1, 0.5), n=6, clump=0.55, mats=("needle", "needle_dark"), seed=seed * 10 + i)
        caps.append((c[0], c[1], c[2] + 0.35))
    if snow:
        snow_caps(k, rng, caps, 0.9)


def coastal(k, seed=0, h=6.0):
    """Приморская сосна: изогнутый ветром ствол, плоские шапки хвои на одну сторону (2D tree_pine_coastal)."""
    rng = random.Random(seed)
    pts = [(0, 0, -0.15), (0.4, 0, 1.6), (-0.3, 0.1, 3.2), (0.6, 0.2, 4.6), (1.4, 0.3, h)]
    for i in range(len(pts) - 1):
        limb(k, pts[i], pts[i + 1], 0.3 - i * 0.05, 0.25 - i * 0.05, "bark_pine", seg=8)
    for i, (dx, dz, s) in enumerate(((1.6, 0.2, 1.0), (0.8, -0.8, 0.8), (2.4, -0.3, 0.7), (-0.2, -1.6, 0.6))):
        k.foliage((pts[-1][0] + dx - 0.8, pts[-1][1], h + dz), (1.3 * s, 0.9 * s, 0.35 * s), n=7, clump=0.38,
                  mats=("needle", "needle_dark"), seed=seed * 10 + i)


# ---------- лиственные ----------

def deciduous(k, seed=0, h=8.0, crown=(2.4, 2.4, 2.2), bark="bark", leaves=("leaf", "leaf_dark"), trunk_r=0.28,
              crown_base=0.45, n_br=5, droop=0.0, fruit=None, n_fruit=0, clumps=34, lean=0.3):
    """Лиственное: ствол, 4–6 скелетных ветвей, крона из комков листвы; fruit — ягоды/яблоки поверх."""
    rng = random.Random(seed)
    pts = trunk(k, h * 0.72, trunk_r, bark, rng, lean=lean, parts=3)
    top = pts[-1]
    branches(k, top, h * crown_base, h * 0.7, n_br, crown[0] * 0.75, trunk_r * 0.45, bark, rng, up=0.55, depth=2)
    cz = h - crown[2]
    k.foliage((top[0], top[1], cz), crown, n=clumps, clump=min(crown) * 0.33, mats=leaves, seed=seed, droop=droop)
    for i in range(n_fruit):
        u, v = rng.uniform(0, 2 * math.pi), rng.uniform(0.2, 1.6)
        p = (top[0] + crown[0] * 1.02 * math.sin(v) * math.cos(u), top[1] + crown[1] * 1.02 * math.sin(v) * math.sin(u),
             cz + crown[2] * 1.02 * math.cos(v))
        k.blob(0.09 if fruit == "apple" else 0.12, p, fruit, subdiv=1, jitter=0.1, seed=700 + i,
               scale=(1, 1, 1) if fruit == "apple" else (1, 1, 0.6), smooth=True)


def birch(k, seed=0, h=8.5, snow=False):
    """Берёза: тонкий белый ствол с тёмными чечевичками, лёгкая плакучая светлая крона (2D tree_birch)."""
    rng = random.Random(seed)
    deciduous(k, seed, h, crown=(1.7, 1.7, 2.6), bark="birch", leaves=("leaf_light", "leaf"), trunk_r=0.17,
              crown_base=0.35, n_br=6, droop=0.5, clumps=30, lean=0.4)
    for i in range(7):                                                                        # чёрные полосы на стволе
        z = 0.4 + i * 0.75 + rng.uniform(-0.1, 0.1)
        k.cyl(0.19 - i * 0.012, 0.06, (0, 0, z), "bark_dark", segments=8, bevel=0)
    if snow:
        snow_caps(k, rng, [(rng.uniform(-1, 1), rng.uniform(-1, 1), h - 0.4 - rng.uniform(0, 1.5)) for _ in range(6)], 0.6)


def poplar(k, seed=0, h=10.0):
    """Тополь пирамидальный: узкая высокая колонна кроны (2D tree_poplar)."""
    rng = random.Random(seed)
    trunk(k, h * 0.6, 0.25, "bark", rng, lean=0.05, parts=2)
    k.foliage((0, 0, h * 0.55), (1.0, 1.0, h * 0.42), n=34, clump=0.45, mats=("leaf_light", "leaf"), seed=seed)


def willow(k, seed=0, h=7.0, swamp=False):
    """Ива: толстый наклонный ствол, широкая плакучая крона, свисающие пряди (2D tree_willow)."""
    rng = random.Random(seed)
    deciduous(k, seed, h, crown=(2.8, 2.8, 1.8), bark="bark", leaves=("leaf_willow", "leaf"), trunk_r=0.38,
              crown_base=0.4, n_br=5, droop=1.6, clumps=36, lean=0.8)
    for i in range(10):                                                                       # пряди
        a = i * 0.63 + rng.uniform(-0.2, 0.2)
        x, y = 2.5 * math.cos(a), 2.5 * math.sin(a)
        k.blob(0.35, (x, y, h - 3.3 - rng.uniform(0, 0.8)), "leaf_willow", subdiv=1, scale=(0.6, 0.6, 2.4), jitter=0.2,
               seed=500 + i, smooth=True)


def dead(k, seed=0, h=6.5, bark="bark_dead", snow=False, stump=False):
    """Сухое дерево: голый ствол, корявые ветви в два уровня (2D tree_dead_*, tree_swamp_dead)."""
    rng = random.Random(seed)
    th = h * (0.55 if stump else 0.75)
    pts = trunk(k, th, 0.3, bark, rng, lean=0.5, parts=3, top_r=0.12)
    ends = branches(k, pts[-1], h * 0.35, th, 5, 2.0, 0.12, bark, rng, up=0.65, depth=2, twig=0.6)
    for i in range(3):                                                                        # корни-наплывы
        a = i * 2.1 + rng.uniform(0, 0.5)
        limb(k, (0, 0, 0.5), (0.8 * math.cos(a), 0.8 * math.sin(a), -0.1), 0.18, 0.05, bark, seg=5)
    if snow:
        snow_caps(k, rng, ends[::2], 0.35)
