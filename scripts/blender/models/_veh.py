"""Техника РТ (decor/veh_*) — по 2D-спрайтам game/art/vehicles. Ось X — длина (перед +X), Z — вверх, опора на земле.
Кузов — материал car_paint (светлая краска с износом): клиент красит его в цвет спрайта (красная «шестёрка»,
оранжевый самосвал) и в обгоревший для *_wreck/burnt. Стёкла — glass_dark (у обгоревших — чёрные)."""
import math

PAINT, GLASS = "car_paint", "glass_dark"


def taper(ob, h, sx=1.0, sy=1.0, shift=0.0, z0=0.5):
    """Сузить верх коробки (кабина, капот): вершины выше z0·h сжимаются к центру и сдвигаются по X."""
    for v in ob.data.vertices:
        t = max(0.0, (v.co.z / h - z0) / (1 - z0))
        if t > 0:
            v.co.x = v.co.x * (1 + (sx - 1) * t) + shift * t
            v.co.y = v.co.y * (1 + (sy - 1) * t)
    return ob


def wheel(k, x, y, r, w=0.2, z=None, tread=0.6, hub="steel"):
    """Колесо: шина-тор и диск, ось вдоль Y."""
    z = r if z is None else z
    t = k.torus(r - w * 0.5, w * 0.5, (x, y, z), "rubber", seg=18, ring=8, axis="Y", tread=tread)
    k.cyl(r * 0.6, w * 0.8, (x, y - w * 0.4, z), hub, segments=12, axis="Y", bevel=0.01)
    return t


def axle_pair(k, x, track, r, w=0.22, double=False):
    for s in (-1, 1):
        wheel(k, x, s * track / 2, r, w)
        if double:
            wheel(k, x, s * (track / 2 - w * 1.05), r, w)


def windows(k, x0, x1, z0, z1, y, n=2, mat=GLASS, gap=0.08):
    """Окна сбоку (обе стороны) — тонкие пластины чуть снаружи борта."""
    step = (x1 - x0) / n
    for s in (-1, 1):
        for i in range(n):
            k.box((step - gap, 0.01, z1 - z0), (x0 + step * (i + 0.5), s * (y + 0.004), z0), mat, bevel=0)


def lights(k, x, y, z, r=0.07, mat="glass"):
    for s in (-1, 1):
        k.cyl(r, 0.03, (x, s * y, z), mat, segments=10, axis="X", bevel=0)


def track_run(k, x0, x1, y, w, r, n_wheels, mat="rust_dark"):
    """Гусеница с катками: лента-коробка со скруглёнными концами, катки сбоку, ленивец и ведущее."""
    L = x1 - x0
    k.box((L - 2 * r, w, 0.06), ((x0 + x1) / 2, y, 0.0), mat, bevel=0.02)                    # низ ленты
    k.box((L - 2 * r, w, 0.06), ((x0 + x1) / 2, y, 2 * r - 0.06), mat, bevel=0.02)            # верх ленты
    for x in (x0 + r, x1 - r):
        k.cyl(r, w, (x, y - w / 2, r), mat, segments=14, axis="Y", bevel=0.015)
    for i in range(n_wheels):
        x = x0 + r + (L - 2 * r) * (i + 0.5) / n_wheels
        k.cyl(r * 0.78, w * 0.5, (x, y + (w * 0.3 if y > 0 else -w * 0.8), r * 0.95), "steel", segments=12, axis="Y", bevel=0.01)


# ------------------------------------------------------------------ легковые и малые


def sedan(k, L=4.1, W=1.62, hatch=False, small=False):
    """Седан-«классика» (2101/2106/Волга/Москвич); hatch — хэтчбек (2109), small — горбатый малыш (ЗАЗ)."""
    r = 0.3
    zb = 0.28
    body_h = 0.62
    k.box((L, W, body_h), (0, 0, zb), PAINT, bevel=0.06)                                        # кузов
    k.box((L + 0.06, W - 0.1, 0.12), (0, 0, zb + 0.08), "steel", bevel=0.03)                    # бамперы-полоса
    cab_l = L * (0.5 if small else 0.48)
    cab_x = -L * 0.02 - (0.25 if hatch else 0) - (0.1 if small else 0)
    ch = 0.56
    cab = k.box((cab_l, W - 0.12, ch), (cab_x, 0, zb + body_h), PAINT, bevel=0.05)
    taper(cab, ch, sx=0.62 if not hatch else 0.72, sy=0.86, shift=-0.1 if not hatch else -0.4, z0=0.0)
    for s in (-1, 1):                                                                            # стёкла по бокам
        k.box((cab_l * 0.78, 0.01, ch * 0.62), (cab_x - 0.04, s * ((W - 0.12) / 2 - 0.03), zb + body_h + 0.06), GLASS, bevel=0)
    k.box((0.02, W * 0.66, ch * 0.62), (cab_x + cab_l * 0.43, 0, zb + body_h + 0.06), GLASS, bevel=0)     # лобовое
    for x in (L * 0.32, -L * 0.32):
        axle_pair(k, x, W - 0.18, r)
    lights(k, L / 2 + 0.01, W * 0.34, zb + body_h * 0.62)
    k.box((0.04, W * 0.4, 0.12), (L / 2 + 0.01, 0, zb + body_h * 0.36), "steel", bevel=0.01)          # решётка
    k.box((0.03, 0.15, 0.06), (-L / 2 - 0.01, W * 0.34, zb + body_h * 0.62), "red", bevel=0)           # фонари
    k.box((0.03, 0.15, 0.06), (-L / 2 - 0.01, -W * 0.34, zb + body_h * 0.62), "red", bevel=0)


def uaz(k):
    """Армейский внедорожник с брезентовым верхом (УАЗ-469 по силуэту): высокий клиренс, запаска сзади."""
    L, W, r = 4.0, 1.78, 0.39
    k.box((L, W, 0.7), (0, 0, 0.52), PAINT, bevel=0.05)
    hood = k.box((1.2, W - 0.08, 0.15), (1.35, 0, 1.2), PAINT, bevel=0.04)
    taper(hood, 0.15, sx=0.95, sy=0.9)
    k.box((0.03, W - 0.2, 0.42), (0.72, 0, 1.22), GLASS, bevel=0)                                 # лобовое
    k.box((0.06, W - 0.06, 0.45), (0.7, 0, 1.2), PAINT, bevel=0.02)                              # рамка
    k.box((2.35, W - 0.04, 0.62), (-0.55, 0, 1.22), "cloth", bevel=0.06)                          # тент
    windows(k, -1.2, 0.5, 1.4, 1.7, (W - 0.04) / 2, n=2)
    for x in (1.3, -1.3):
        axle_pair(k, x, W - 0.1, r, w=0.26)
    k.cyl(r, 0.24, (-L / 2 - 0.25, 0, 0.95), "rubber", segments=18, axis="X", bevel=0.03)         # запаска
    lights(k, L / 2 + 0.01, 0.62, 0.95, r=0.09)
    k.box((0.05, 0.9, 0.3), (L / 2 + 0.01, 0, 0.8), "steel", bevel=0.01)                          # решётка


def van(k, gazelle=False):
    """Фургон-«буханка» (УАЗ-452): капля-короб, окна по борту; gazelle — кабина и тентованный кузов."""
    W, r = 1.94, 0.38
    if gazelle:
        L = 5.4
        cab = k.box((1.5, W, 1.55), (L / 2 - 0.8, 0, 0.6), "white", bevel=0.08)
        taper(cab, 1.55, sx=0.85, shift=-0.12)
        k.box((0.02, W - 0.3, 0.55), (L / 2 - 0.04, 0, 1.4), GLASS, bevel=0)
        k.box((0.03, W * 0.7, 0.3), (L / 2 + 0.02, 0, 0.75), "steel", bevel=0.01)
        k.box((3.4, W + 0.1, 0.12), (-0.85, 0, 0.62), "rust_dark", bevel=0.02)                   # рама кузова
        k.box((3.4, W + 0.1, 1.6), (-0.85, 0, 0.74), "cloth", bevel=0.05)                         # тент
        windows(k, L / 2 - 1.4, L / 2 - 0.4, 1.35, 1.85, W / 2, n=1)
        axle_pair(k, 1.6, W - 0.15, r)
        axle_pair(k, -1.6, W - 0.15, r, double=True)
        return
    L = 4.4
    body = k.box((L, W, 1.62), (0, 0, 0.46), PAINT, bevel=0.22)
    taper(body, 1.62, sx=0.94, sy=0.95, shift=-0.05, z0=0.7)
    windows(k, -1.8, 1.3, 1.35, 1.8, W / 2, n=4)
    k.box((0.04, W - 0.35, 0.5), (L / 2 + 0.005, 0, 1.33), GLASS, bevel=0)
    k.box((0.04, 0.6, 0.15), (L / 2 + 0.01, 0, 0.7), "steel", bevel=0.01)
    lights(k, L / 2 + 0.02, 0.7, 0.85, r=0.09)
    for x in (1.15, -1.15):
        axle_pair(k, x, W - 0.18, r, w=0.24)


def bus(k):
    """Автобус ПАЗ-образный: короткий капотный нос, окна в ряд, светлая крыша, двери справа."""
    L, W, r = 7.0, 2.44, 0.48
    k.box((L - 0.6, W, 2.3), (-0.3, 0, 0.45), PAINT, bevel=0.15)                                 # салон
    k.box((L - 0.7, W - 0.1, 0.12), (-0.3, 0, 2.75), "white", bevel=0.05)                          # крыша
    k.box((0.9, W - 0.3, 0.9), (L / 2 - 0.45, 0, 0.5), PAINT, bevel=0.12)                          # капот
    windows(k, -3.2, 2.4, 1.6, 2.35, W / 2, n=8)
    k.box((0.05, W - 0.3, 0.8), (2.9, 0, 1.55), GLASS, bevel=0)                                   # лобовое
    k.box((0.8, 0.02, 1.8), (2.2, -W / 2 - 0.01, 0.55), GLASS, bevel=0)                            # дверь
    lights(k, L / 2 + 0.01, 0.9, 0.9, r=0.1)
    for x in (2.2, -2.2):
        axle_pair(k, x, W - 0.25, r, w=0.28, double=x < 0)


# ------------------------------------------------------------------ грузовые


def truck(k, kung=False, tanker=False, mil=False):
    """Капотный грузовик (ЗИЛ-130/ГАЗ-53 по силуэту): капот, кабина, бортовой кузов; kung — будка (радиорелейная),
    mil — три оси и высокий клиренс."""
    W, r = 2.4, 0.5 if mil else 0.47
    L = 6.8
    zf = 0.75 if mil else 0.62
    k.box((L - 0.2, 0.9, 0.18), (0, 0, zf), "rust_dark", bevel=0.02)                             # рама
    hood = k.box((1.3, 1.6, 0.85), (L / 2 - 0.75, 0, zf + 0.15), PAINT, bevel=0.1)
    taper(hood, 0.85, sx=0.9, sy=0.88, shift=0.05, z0=0.3)
    for s in (-1, 1):                                                                            # крылья
        k.box((1.1, 0.42, 0.25), (L / 2 - 0.75, s * (W / 2 - 0.2), zf + 0.45), PAINT, bevel=0.08)
    cab = k.box((1.35, W - 0.1, 1.35), (L / 2 - 2.05, 0, zf + 0.15), PAINT, bevel=0.1)
    taper(cab, 1.35, sx=0.92, shift=-0.05)
    k.box((0.03, W - 0.4, 0.5), (L / 2 - 1.38, 0, zf + 0.95), GLASS, bevel=0)
    windows(k, L / 2 - 2.6, L / 2 - 1.5, zf + 0.9, zf + 1.35, (W - 0.1) / 2, n=1)
    k.box((0.04, 1.0, 0.45), (L / 2 - 0.08, 0, zf + 0.25), "steel", bevel=0.01)                  # облицовка
    lights(k, L / 2 - 0.25, 0.9, zf + 0.65, r=0.1)
    bx0, bx1 = -L / 2 + 0.05, L / 2 - 2.8
    blen = bx1 - bx0
    bc = (bx0 + bx1) / 2
    if kung:
        k.box((blen, W + 0.05, 1.9), (bc, 0, zf + 0.2), PAINT, bevel=0.06)
        k.box((0.5, 0.02, 1.4), (bc - 1.2, -W / 2 - 0.03, zf + 0.3), "steel", bevel=0.01)          # дверь
        k.cyl(0.04, 3.0, (bc + 0.6, 0.6, zf + 2.1), "steel", segments=6, bevel=0)                 # мачта антенны
        k.box((0.6, 0.6, 0.04), (bc + 0.6, 0.6, zf + 5.1), "steel", rot_z=0.4, bevel=0.005)
    elif tanker:
        k.cyl(0.95, blen, (bx0, 0, zf + 1.15), PAINT, segments=20, axis="X", bevel=0.04)
    else:
        k.box((blen, W + 0.05, 0.1), (bc, 0, zf + 0.2), "planks", bevel=0.01)                     # пол кузова
        for s in (-1, 1):
            k.box((blen, 0.06, 0.55), (bc, s * (W / 2), zf + 0.3), PAINT, bevel=0.015)           # борта
        k.box((0.06, W + 0.05, 0.55), (bx0, 0, zf + 0.3), PAINT, bevel=0.015)
        k.box((0.06, W + 0.05, 0.75), (bx1, 0, zf + 0.3), PAINT, bevel=0.015)
    axle_pair(k, L / 2 - 1.0, W - 0.3, r, w=0.3)
    if mil:
        axle_pair(k, -0.9, W - 0.3, r, w=0.3)
        axle_pair(k, -2.3, W - 0.3, r, w=0.3)
    else:
        axle_pair(k, -L / 2 + 1.5, W - 0.35, r, w=0.28, double=True)


def dump(k):
    """Бескапотный самосвал (КамАЗ по силуэту): кабина над мостом, три оси, кузов-ковш с козырьком."""
    W, r, L = 2.5, 0.52, 7.0
    zf = 0.75
    k.box((L - 0.2, 0.9, 0.2), (0, 0, zf), "rust_dark", bevel=0.02)
    cab = k.box((1.8, W, 1.95), (L / 2 - 0.95, 0, zf + 0.05), PAINT, bevel=0.12)
    taper(cab, 1.95, sx=0.92, shift=-0.07, z0=0.45)
    k.box((0.03, W - 0.3, 0.75), (L / 2 - 0.02, 0, zf + 1.05), GLASS, bevel=0)
    windows(k, L / 2 - 1.5, L / 2 - 0.4, zf + 1.05, zf + 1.75, W / 2, n=1)
    k.box((0.05, W - 0.4, 0.35), (L / 2, 0, zf + 0.25), "steel", bevel=0.01)
    lights(k, L / 2 + 0.02, 0.95, zf + 0.35, r=0.1)
    bed = k.box((4.2, W + 0.05, 1.05), (-1.05, 0, zf + 0.35), "rust", bevel=0.04)                # ковш
    taper(bed, 1.05, sx=1.0, sy=1.04, z0=0.0)
    k.box((0.6, W, 0.08), (0.95, 0, zf + 1.4), "rust", bevel=0.02)                               # козырёк
    k.box((4.0, W - 0.1, 0.05), (-1.05, 0, zf + 1.31), "concrete", bevel=0.01)                    # груз
    axle_pair(k, L / 2 - 1.0, W - 0.3, r, w=0.32)
    for x in (-0.7, -2.0):
        axle_pair(k, x, W - 0.35, r, w=0.3, double=True)


def radar(k):
    """РЛС на прицепе: кунг-платформа на двух осях, мачта и решётка антенн «волновой канал»."""
    k.box((4.5, 2.2, 0.15), (0, 0, 0.75), "rust_dark", bevel=0.02)
    k.box((2.5, 2.0, 1.2), (-0.6, 0, 0.9), PAINT, bevel=0.06)
    axle_pair(k, 1.0, 2.0, 0.45, w=0.28)
    axle_pair(k, -1.2, 2.0, 0.45, w=0.28)
    k.box((1.8, 0.08, 0.08), (2.9, 0, 0.75), "rust_dark", bevel=0.01)                              # дышло
    k.cyl(0.08, 4.0, (0.8, 0, 0.9), "steel", segments=8, bevel=0)                                 # мачта
    for row in range(2):
        z = 4.3 + row * 0.9
        k.cyl(0.03, 7.5, (0.8, -3.75, z), "steel", segments=6, axis="Y", bevel=0)                 # штанга
        for i in range(10):
            y = -3.5 + i * 0.78
            k.cyl(0.012, 1.4, (0.1, y, z), "steel", segments=4, axis="X", bevel=0)               # вибраторы


def tractor(k):
    """Колёсный трактор (Беларус по силуэту): большие задние колёса, капот, кабина-«аквариум», выхлоп."""
    k.box((2.0, 0.7, 0.55), (0.6, 0, 0.75), PAINT, bevel=0.06)                                   # капот-двигатель
    k.box((1.1, 1.0, 0.45), (-0.65, 0, 0.75), "rust_dark", bevel=0.04)                           # трансмиссия
    cab = k.box((1.25, 1.35, 1.35), (-0.75, 0, 1.2), GLASS, bevel=0.03)
    for x in (-1.35, -0.15):                                                                     # стойки кабины
        for s in (-1, 1):
            k.box((0.06, 0.06, 1.35), (x, s * 0.65, 1.2), PAINT, bevel=0.01)
    k.box((1.35, 1.45, 0.08), (-0.75, 0, 2.55), PAINT, bevel=0.03)                               # крыша
    k.cyl(0.05, 1.0, (1.1, 0.25, 1.3), "rust_dark", segments=8, bevel=0)                          # выхлоп
    for s in (-1, 1):
        wheel(k, -0.75, s * 0.85, 0.78, w=0.45, tread=0.8, hub=PAINT)
        wheel(k, 1.2, s * 0.75, 0.48, w=0.3, tread=0.8, hub=PAINT)
        k.box((1.0, 0.5, 0.06), (-0.75, s * 0.85, 1.6), PAINT, bevel=0.02)                      # крылья
    k.box((0.05, 0.6, 0.4), (1.62, 0, 0.8), "steel", bevel=0.01)


def trailer(k):
    """Тракторный прицеп 2ПТС: бортовой кузов на двух осях, дышло-сцепка."""
    W = 2.3
    k.box((4.2, 1.0, 0.15), (-0.3, 0, 0.75), "rust_dark", bevel=0.02)
    k.box((4.4, W, 0.1), (-0.3, 0, 0.9), "planks", bevel=0.01)
    for s in (-1, 1):
        k.box((4.4, 0.06, 0.6), (-0.3, s * W / 2, 1.0), PAINT, bevel=0.015)
    for x in (-2.5, 1.9):
        k.box((0.06, W, 0.6), (x, 0, 1.0), PAINT, bevel=0.015)
    for x in (0.6, -1.3):
        axle_pair(k, x, W - 0.35, 0.45, w=0.3)
    k.box((1.6, 0.08, 0.08), (2.6, 0, 0.75), "rust_dark", bevel=0.01)
    k.torus(0.08, 0.02, (3.42, 0, 0.79), "rust_dark", seg=10, ring=4)


def moto(k):
    """Мотоцикл с коляской (Урал по силуэту): рама, бак, седло, колёса, коляска-лодочка справа."""
    for x in (0.75, -0.6):
        wheel(k, x, 0.3, 0.33, w=0.12, tread=0.5)
    wheel(k, -0.3, -0.55, 0.33, w=0.12, tread=0.5)
    k.box((1.1, 0.25, 0.3), (0.05, 0.3, 0.45), "rust_dark", bevel=0.04)                          # двигатель
    k.box((0.5, 0.26, 0.2), (0.25, 0.3, 0.78), PAINT, bevel=0.07)                                # бак
    k.box((0.45, 0.22, 0.08), (-0.25, 0.3, 0.8), "leather", bevel=0.03)                          # седло
    k.box((0.04, 0.7, 0.04), (0.55, 0.3, 1.05), "steel", bevel=0.005)                            # руль
    k.cyl(0.03, 0.6, (0.55, 0.3, 0.45), "steel", segments=6, bevel=0)                            # вилка
    side = k.box((1.6, 0.65, 0.45), (-0.1, -0.55, 0.38), PAINT, bevel=0.15)                      # коляска
    taper(side, 0.45, sx=0.85, sy=0.85, shift=0.1)
    k.box((0.6, 0.4, 0.03), (-0.2, -0.55, 0.84), "black", bevel=0)
    k.cyl(0.04, 1.0, (0.3, 0.3, 0.6), "steel", segments=6, axis="X", bevel=0)


# ------------------------------------------------------------------ военные


def tank(k, turret=True, old=False):
    """Танк (силуэт Т-55; old — Т-34): гусеницы с катками, корпус с наклонным лбом, литая башня, пушка."""
    L, W = 6.4 if not old else 6.0, 3.25
    for s in (-1, 1):
        track_run(k, -L / 2, L / 2, s * (W / 2 - 0.3), 0.58, 0.42, 5)
    hull = k.box((L - 0.4, W - 1.0, 0.75), (0, 0, 0.55), PAINT, bevel=0.05)
    taper(hull, 0.75, sx=0.92, shift=-0.1, z0=0.2)
    for s in (-1, 1):                                                                            # надгусеничные полки
        k.box((L - 0.3, 0.55, 0.05), (0, s * (W / 2 - 0.3), 0.9), PAINT, bevel=0.01)
    k.box((1.4, 0.8, 0.12), (-L / 2 + 0.9, 0, 1.3), "rust_dark", bevel=0.02)                     # МТО решётка
    for s in (-1, 1):
        k.cyl(0.25, 0.9, (-L / 2 + 0.3, s * 1.0, 1.0), "rust", segments=12, axis="X", bevel=0.02)  # бочки
    if turret:
        if old:
            tur = k.box((1.9, 1.75, 0.65), (0.4, 0, 1.3), PAINT, bevel=0.12)
            taper(tur, 0.65, sx=0.85, sy=0.8)
        else:
            tur = k.blob(1.1, (0.2, 0, 1.3), PAINT, subdiv=3, scale=(1.05, 0.95, 0.42), jitter=0.0, smooth=True)
            for v in tur.data.vertices:
                v.co.z = max(v.co.z, 0.0)
        gl = 3.4 if not old else 2.6
        k.cyl(0.11, 0.5, (1.0, 0, 1.6), PAINT, segments=12, axis="X", bevel=0.01, radius_top=0.09)  # маска
        k.cyl(0.07, gl, (1.4, 0, 1.6), PAINT, segments=10, axis="X", bevel=0)                   # ствол
        k.cyl(0.25, 0.15, (-0.1, 0.4, 1.75), "rust_dark", segments=12, bevel=0.01)              # люк
    else:
        k.cyl(0.95, 0.06, (0.2, 0, 1.28), "black", segments=20, bevel=0)                          # погон без башни


def apc_wheeled(k, axles=4):
    """Колёсный БТР (4 оси) / БРДМ (2 оси): лодочный корпус, клиновой нос, башенка с пулемётом."""
    L = 7.4 if axles == 4 else 5.7
    W = 2.8 if axles == 4 else 2.35
    hull = k.box((L, W - 0.4, 1.25), (0, 0, 0.6), PAINT, bevel=0.08)
    taper(hull, 1.25, sx=0.86, sy=0.84, shift=-0.1, z0=0.35)
    nose = k.box((0.9, W - 0.5, 0.7), (L / 2 + 0.2, 0, 0.6), PAINT, bevel=0.05)
    taper(nose, 0.7, sx=0.4, shift=-0.3, z0=0.0)
    xs = [L / 2 - 1.0 - i * (L - 2.0) / (axles - 1) for i in range(axles)]
    for x in xs:
        axle_pair(k, x, W - 0.45, 0.55, w=0.38)
    k.cyl(0.42, 0.45, (0.9, 0, 1.85), PAINT, segments=10, bevel=0.03, radius_top=0.36)           # башенка
    k.cyl(0.045, 1.3, (1.25, 0, 2.05), "gunmetal", segments=8, axis="X", bevel=0)
    for x in (L / 2 - 1.3, 0.2):
        for s in (-1, 1):
            k.box((0.3, 0.02, 0.2), (x, s * ((W - 0.4) / 2 * 0.86 + 0.03), 1.4), GLASS, bevel=0)   # смотровые


def apc_tracked(k, sp=False, low=False):
    """Гусеничная: БМП (острый нос, башня с пушкой), МТ-ЛБ (low — плоская, пулемётная башенка),
    САУ (sp — большая рубка-башня с короткой толстой гаубицей)."""
    L, W = (6.5, 2.9) if not sp else (7.0, 2.85)
    for s in (-1, 1):
        track_run(k, -L / 2, L / 2, s * (W / 2 - 0.25), 0.45, 0.35, 6 if not sp else 7)
    hh = 0.65 if low else 0.8
    hull = k.box((L - 0.3, W - 0.7, hh), (0, 0, 0.45), PAINT, bevel=0.05)
    taper(hull, hh, sx=0.94, shift=-0.08, z0=0.4)
    nose = k.box((1.2, W - 0.7, hh), (L / 2 - 0.6, 0, 0.45), PAINT, bevel=0.04)
    taper(nose, hh, sx=0.25, shift=-0.5, z0=0.0)
    top = 0.45 + hh
    if sp:
        tur = k.box((2.6, 2.3, 0.95), (-0.9, 0, top), PAINT, bevel=0.08)
        taper(tur, 0.95, sx=0.85, sy=0.85, shift=0.1)
        k.cyl(0.22, 0.9, (0.35, 0, top + 0.45), PAINT, segments=12, axis="X", bevel=0.02)         # маска
        k.cyl(0.11, 3.2, (1.2, 0, top + 0.45), PAINT, segments=10, axis="X", bevel=0)
        k.cyl(0.17, 0.4, (4.3, 0, top + 0.45), PAINT, segments=10, axis="X", bevel=0.01)         # дульный тормоз
    elif low:
        k.cyl(0.38, 0.35, (1.2, 0.5, top), PAINT, segments=10, bevel=0.03, radius_top=0.32)
        k.cyl(0.04, 1.0, (1.5, 0.5, top + 0.2), "gunmetal", segments=8, axis="X", bevel=0)
    else:
        tur = k.cyl(0.9, 0.45, (0.0, 0, top), PAINT, segments=10, bevel=0.03, radius_top=0.65)
        k.cyl(0.09, 2.2, (0.6, 0, top + 0.25), PAINT, segments=10, axis="X", bevel=0)
        k.cyl(0.15, 0.6, (0.35, -0.55, top + 0.35), "rust_dark", segments=8, axis="X", bevel=0.01)  # ПТУР
    for x in (-L / 2 + 0.3, -L / 2 + 0.9):                                                       # десантные люки
        k.box((0.5, 0.6, 0.04), (x, 0.55, top), "rust_dark", bevel=0.01)


def heli_wreck(k):
    """Обломки вертолёта (Ми-8 по силуэту): разорванный фюзеляж на брюхе, отломленная хвостовая балка,
    погнутые лопасти, обломки вокруг."""
    fus = k.cyl(1.2, 7.0, (-1.5, 0, 1.0), PAINT, segments=14, axis="X", bevel=0.04)
    nose = k.blob(1.2, (5.4, 0, 1.0), PAINT, subdiv=3, scale=(1.1, 1.0, 1.0), jitter=0.05, smooth=True)
    for s in (-1, 1):
        k.box((1.6, 0.02, 0.6), (4.8, s * 0.95, 1.3), "glass_dark", bevel=0)
    k.box((3.0, 1.6, 0.7), (2.0, 0, 2.15), "rust_dark", bevel=0.08)                              # двигатели
    for v in fus.data.vertices:                                                                  # смятое брюхо
        if v.co.z < -0.6:
            v.co.z = -0.6
    tail = k.cyl(0.35, 6.0, (0, 0, 0), PAINT, segments=10, axis="X", bevel=0.02, radius_top=0.22)
    tail.rotation_euler = (0.1, 0.05, 0.35)
    tail.location = (-8.8, 1.8, 0.35)
    k.box((0.9, 0.08, 1.4), (-9.0, 4.0, 0.3), PAINT, rot_z=0.35, bevel=0.02)                     # киль
    for i, (a, ln, droop) in enumerate(((0.3, 8.0, -0.2), (1.9, 6.5, -0.4), (3.6, 7.5, -0.1), (5.0, 3.5, 0.0))):
        b = k.box((ln, 0.5, 0.05), (0, 0, 0), "steel", bevel=0.005)                             # лопасти
        b.rotation_euler = (0.05 * i, droop * 0.3, a)
        b.location = (2.0 + math.cos(a) * ln * 0.45, math.sin(a) * ln * 0.45, 2.7 + droop)
    for i in range(10):
        a = i * 0.9
        k.box((0.4 + (i % 3) * 0.3, 0.3, 0.05), (math.cos(a) * (4 + i * 0.3), math.sin(a) * (3 + i * 0.2), 0.0), "rust",
              rot_z=a * 2, bevel=0.01)


def biplane(k):
    """Биплан (Ан-2 по силуэту): толстый фюзеляж, два крыла с подкосами, хвостовое оперение, винт."""
    fus = k.cyl(0.85, 9.5, (-5.0, 0, 1.2), PAINT, segments=12, axis="X", bevel=0.03, radius_top=0.5)
    k.blob(0.9, (4.4, 0, 1.25), PAINT, subdiv=2, scale=(0.8, 1.0, 1.0), jitter=0.02, smooth=True)   # мотор
    k.cyl(0.18, 0.3, (5.1, 0, 1.25), "steel", segments=10, axis="X", bevel=0.01)
    for a in (0.0, 1.57):
        b = k.box((0.05, 3.6, 0.22), (5.4, 0, 1.25), "wood_dark", bevel=0.01)
        b.rotation_euler.x = a
        b.location = (5.42, 0, 1.25 - (0 if a == 0 else 0.0))
    for z, span, ch in ((2.6, 18.0, 2.4), (0.75, 14.0, 2.0)):                                   # крылья
        k.box((ch, span, 0.12), (1.6, 0, z), PAINT, bevel=0.03)
    for y in (-4.5, 4.5, -2.0, 2.0):                                                             # стойки
        k.box((0.08, 0.08, 1.85), (1.6, y, 0.82), "steel", bevel=0.005)
    k.box((1.6, 6.5, 0.08), (-4.9, 0, 1.55), PAINT, bevel=0.02)                                   # стабилизатор
    k.box((1.6, 0.08, 2.2), (-5.0, 0, 1.4), PAINT, bevel=0.02)                                    # киль
    for s in (-1, 1):                                                                            # шасси
        k.cyl(0.05, 1.2, (2.5, s * 1.4, 0.4), "steel", segments=6, bevel=0)
        wheel(k, 2.5, s * 1.4, 0.45, w=0.25)
    for s in (-1, 1):
        k.box((1.0, 0.02, 0.5), (3.0, s * 0.82, 1.6), GLASS, bevel=0)


# ------------------------------------------------------------------ вагоны


def wagon(k, kind="box"):
    """Вагон на двух тележках: box — крытый, flat — платформа, gondola — полувагон с углём."""
    L, W = 13.9, 3.1
    for x in (-L / 2 + 2.2, L / 2 - 2.2):                                                        # тележки
        k.box((2.6, 2.2, 0.45), (x, 0, 0.35), "rust_dark", bevel=0.04)
        for dx in (-0.9, 0.9):
            for s in (-1, 1):
                k.cyl(0.47, 0.12, (x + dx, s * 0.76, 0.47), "rust_dark", segments=14, axis="Y", bevel=0.01)
    k.box((L, W - 0.2, 0.3), (0, 0, 0.85), "rust_dark", bevel=0.03)                              # рама
    for s in (-1, 1):                                                                            # буфера
        for y in (-0.85, 0.85):
            k.cyl(0.18, 0.4, (s * (L / 2 + 0.2) - (0.4 if s > 0 else 0), y, 1.0), "steel", segments=10, axis="X", bevel=0.01)
    if kind == "box":
        k.box((L - 0.2, W, 2.9), (0, 0, 1.15), PAINT, bevel=0.06)
        roof = k.box((L - 0.1, W + 0.05, 0.35), (0, 0, 4.05), PAINT, bevel=0.1)
        taper(roof, 0.35, sy=0.7, z0=0.0)
        for s in (-1, 1):
            k.box((2.0, 0.04, 2.4), (0, s * (W / 2 + 0.02), 1.25), "white", bevel=0.01)          # дверь
            for i in range(10):
                k.box((0.06, 0.03, 2.8), (-L / 2 + 0.7 + i * 1.4, s * (W / 2 + 0.015), 1.2), PAINT, bevel=0.005)  # стойки
    elif kind == "gondola":
        for s in (-1, 1):
            k.box((L - 0.2, 0.1, 1.9), (0, s * (W / 2 - 0.05), 1.15), PAINT, bevel=0.03)
            for i in range(12):
                k.box((0.1, 0.05, 1.9), (-L / 2 + 0.6 + i * 1.15, s * W / 2, 1.15), PAINT, bevel=0.01)
        for x in (-L / 2 + 0.1, L / 2 - 0.1):
            k.box((0.1, W, 1.9), (x, 0, 1.15), PAINT, bevel=0.02)
        coal = k.blob(1.0, (0, 0, 2.7), "black", subdiv=2, scale=(6.6, 1.45, 0.35), jitter=0.12, seed=5, smooth=True)
    else:
        k.box((L - 0.1, W, 0.12), (0, 0, 1.15), "planks", bevel=0.02)
        for x in range(-6, 7, 2):
            for s in (-1, 1):
                k.box((0.1, 0.1, 0.5), (x, s * (W / 2 - 0.05), 1.27), "rust_dark", bevel=0.01)     # стойки-упоры
