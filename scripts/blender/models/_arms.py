"""Оружие и инструмент в руках (held_<id>) — по 2D-спрайтам game/art/weapons/weapon_<id>.png.
Оси: +X — к дулу/рабочей части, +Z — вверх, начало координат — место хвата (рука).
Размеры — как у прежних моделей (автомат ~1,16 м: приклад до −0,39, дуло до +0,77)."""
import math

GM, WOOD, POLY = "gunmetal", "wood_stock", "polymer"


def bx(k, size, c, mat=GM, ry=0.0, rz=0.0, bevel=0.004):
    """Коробка по центру c, повёрнутая вокруг центра (ry — наклон в вертикальной плоскости)."""
    ob = k.box(size, (0, 0, 0), mat, bevel=bevel)
    for v in ob.data.vertices:
        v.co.z -= size[2] / 2
    ob.rotation_euler = (0, ry, rz)
    ob.location = c
    return ob


def hang(k, size, top, mat=GM, tilt=0.25, bevel=0.004):
    """Деталь, свисающая вниз от точки top с наклоном назад (рукоятка) или вперёд (tilt < 0 — магазин)."""
    ob = k.box(size, (0, 0, 0), mat, bevel=bevel)
    ob.rotation_euler = (0, math.pi + tilt, 0)
    ob.location = top
    return ob


def tube(k, r, x0, x1, z=0.0, mat=GM, seg=10, y=0.0, r1=None):
    """Труба вдоль X от x0 до x1."""
    return k.cyl(r, x1 - x0, (x0, y, z), mat, segments=seg, axis="X", bevel=0, radius_top=r1)


def curved_mag(k, top, n=3, w=0.035, seg_len=0.075, depth=0.06, mat=GM, curve=0.28):
    """Изогнутый магазин (рожок) звеньями вперёд."""
    x, z = top
    a = -0.15
    for i in range(n):
        hang(k, (depth, w, seg_len + 0.01), (x, 0, z), mat, tilt=a)
        x += math.sin(-a) * seg_len
        z -= math.cos(a) * seg_len
        a -= curve


def kalash(k, stock="wood", furn=WOOD, an94=False, saiga=False):
    """Автомат Калашникова и производные: ствольная коробка, цевьё, газовая трубка, рожок, приклад."""
    bx(k, (0.36, 0.045, 0.07), (0.05, 0, 0.0))                                   # ствольная коробка
    bx(k, (0.3, 0.04, 0.012), (0.06, 0, 0.041), bevel=0.003)                       # крышка
    bx(k, (0.2, 0.05, 0.06), (0.32, 0, -0.005), furn, bevel=0.01)                  # цевьё
    bx(k, (0.14, 0.04, 0.035), (0.33, 0, 0.045), furn, bevel=0.008)                # накладка газовой трубки
    tube(k, 0.011, 0.23, 0.77 if not saiga else 0.72, 0.012)                       # ствол
    tube(k, 0.018, 0.69, 0.77, 0.012) if not saiga else tube(k, 0.02, 0.66, 0.72, 0.012)   # дульный тормоз
    bx(k, (0.02, 0.01, 0.04), (0.6, 0, 0.04))                                      # мушка
    bx(k, (0.03, 0.03, 0.025), (0.16, 0, 0.045))                                   # целик
    hang(k, (0.035, 0.032, 0.11), (-0.04, 0, -0.03), furn if furn == POLY else GM if stock == "fold" else WOOD, tilt=0.3)   # рукоятка
    if saiga:
        hang(k, (0.07, 0.035, 0.12), (0.12, 0, -0.03), GM, tilt=-0.05)             # прямой магазин
    else:
        curved_mag(k, (0.13, -0.03), mat=POLY if furn == POLY else GM)
    bx(k, (0.07, 0.008, 0.035), (0.06, 0, -0.055), bevel=0.002)                    # спуск-скоба
    if stock == "wood":
        bx(k, (0.3, 0.04, 0.07), (-0.24, 0, -0.03), furn, ry=-0.12, bevel=0.012)   # приклад
        bx(k, (0.02, 0.045, 0.12), (-0.385, 0, -0.045), "rubber", ry=-0.12)        # затыльник
    elif stock == "fold":
        for z in (0.0, -0.06):
            bx(k, (0.33, 0.012, 0.014), (-0.24, 0, z + 0.005 - (0.0 if z == 0 else 0.0)), GM, ry=0.08 if z else 0)
        bx(k, (0.03, 0.035, 0.09), (-0.39, 0, -0.03), GM)
    else:                                                                          # полимерный скелет (АН-94)
        bx(k, (0.32, 0.04, 0.03), (-0.24, 0, 0.005), POLY)
        bx(k, (0.03, 0.04, 0.12), (-0.39, 0, -0.04), POLY)
        bx(k, (0.25, 0.035, 0.02), (-0.26, 0, -0.07), POLY, ry=-0.15)
    if an94:
        bx(k, (0.2, 0.03, 0.02), (0.05, 0, 0.06), GM)                                # планка с диоптром
        bx(k, (0.03, 0.03, 0.05), (-0.03, 0, 0.08), GM)


def ak(k): kalash(k, "wood", WOOD)
def akm(k): kalash(k, "wood", "wood_light")
def an94(k): kalash(k, "skeleton", POLY, an94=True)
def saiga12(k): kalash(k, "fold", POLY, saiga=True)


def smg(k):
    """Пистолет-пулемёт со складным прикладом (как ПП-91/«Кедр»-подобный, 2D weapon_smg)."""
    bx(k, (0.3, 0.045, 0.08), (0.08, 0, 0.0))
    tube(k, 0.012, 0.23, 0.38, 0.01)
    tube(k, 0.02, 0.33, 0.38, 0.01)
    hang(k, (0.035, 0.032, 0.11), (0.0, 0, -0.035), POLY, tilt=0.2)
    hang(k, (0.035, 0.03, 0.14), (0.14, 0, -0.035), GM, tilt=-0.05)
    bx(k, (0.25, 0.012, 0.015), (-0.17, 0, 0.0))
    bx(k, (0.03, 0.035, 0.08), (-0.29, 0, -0.02))
    bx(k, (0.06, 0.02, 0.03), (0.12, 0, 0.055))


def homesmg(k):
    """Самодельный ПП: труба-ствольная коробка, проволочный приклад, магазин сбоку снизу, синяя изолента."""
    tube(k, 0.025, -0.05, 0.25, 0.0, "steel")
    tube(k, 0.012, 0.25, 0.38, 0.0, "rust")
    hang(k, (0.035, 0.03, 0.11), (0.0, 0, -0.02), "planks", tilt=0.25)
    hang(k, (0.03, 0.028, 0.15), (0.12, 0, -0.02), "rust", tilt=0.0)
    bx(k, (0.04, 0.052, 0.03), (0.05, 0, 0.0), "tape", bevel=0)
    for z in (0.0, -0.08):
        tube(k, 0.005, -0.29, -0.05, z, "steel", seg=6)
    bx(k, (0.012, 0.012, 0.09), (-0.29, 0, -0.04), "steel")


def ppsh(k):
    """ППШ: деревянная ложа целиком, кожух ствола с прорезями, барабанный/рожковый магазин."""
    bx(k, (0.62, 0.045, 0.07), (-0.08, 0, -0.035), WOOD, ry=-0.06, bevel=0.012)    # ложа
    bx(k, (0.24, 0.05, 0.05), (0.08, 0, 0.02))                                      # коробка
    tube(k, 0.022, 0.2, 0.38, 0.02)                                                 # кожух
    for i in range(4):
        bx(k, (0.03, 0.047, 0.012), (0.24 + i * 0.035, 0, 0.02), "black", bevel=0)
    k.cyl(0.07, 0.04, (0.12, -0.02, -0.11), GM, segments=16, axis="Y", bevel=0.004)   # диск
    bx(k, (0.03, 0.008, 0.035), (0.04, 0, -0.04), bevel=0.002)


def pps43(k):
    """ППС-43: штампованная коробка, кожух с окнами, длинный рожок, складной приклад сверху."""
    bx(k, (0.3, 0.04, 0.06), (0.06, 0, 0.0))
    tube(k, 0.02, 0.2, 0.38, 0.005)
    for i in range(3):
        bx(k, (0.035, 0.042, 0.012), (0.24 + i * 0.045, 0, 0.005), "black", bevel=0)
    hang(k, (0.03, 0.03, 0.1), (-0.04, 0, -0.03), "rubber", tilt=0.2)
    hang(k, (0.03, 0.028, 0.18), (0.08, 0, -0.03), GM, tilt=-0.12)
    bx(k, (0.26, 0.012, 0.012), (-0.2, 0, 0.03))
    bx(k, (0.26, 0.012, 0.012), (-0.2, 0, -0.03), ry=0.1)
    bx(k, (0.02, 0.03, 0.08), (-0.32, 0, -0.005))


def rifle_stock(k, x0, x1, z=-0.02, furn=WOOD, drop=0.1):
    """Цельная винтовочная ложа от приклада x0 до цевья x1."""
    bx(k, (x1 - 0.05, 0.045, 0.05), ((x1 + 0.05) / 2 - 0.02, 0, z + 0.005), furn, bevel=0.01)            # цевьё
    bx(k, (-x0 + 0.05, 0.042, 0.065), (x0 / 2, 0, z - 0.02), furn, ry=-drop, bevel=0.012)                    # приклад
    hang(k, (0.04, 0.035, 0.06), (0.0, 0, z - 0.02), furn, tilt=0.5)                                        # шейка


def svd(k):
    """СВД: длинный ствол с тормозом, скелетный приклад, оптика ПСО, короткий магазин."""
    bx(k, (0.3, 0.045, 0.06), (0.05, 0, 0.0))
    bx(k, (0.22, 0.05, 0.055), (0.32, 0, 0.0), WOOD, bevel=0.01)
    tube(k, 0.011, 0.2, 0.92, 0.01)
    tube(k, 0.016, 0.86, 0.95, 0.01)
    hang(k, (0.06, 0.032, 0.08), (0.12, 0, -0.03), GM, tilt=-0.1)
    bx(k, (0.3, 0.04, 0.03), (-0.22, 0, 0.0), WOOD, bevel=0.008)                   # верх скелета
    bx(k, (0.26, 0.04, 0.025), (-0.24, 0, -0.08), WOOD, ry=-0.12, bevel=0.008)     # низ скелета
    bx(k, (0.03, 0.04, 0.12), (-0.38, 0, -0.04), "rubber")
    hang(k, (0.035, 0.035, 0.09), (-0.06, 0, -0.03), WOOD, tilt=0.15)              # рукоять скелета
    tube(k, 0.02, -0.02, 0.2, 0.08, "black")                                       # прицел
    tube(k, 0.026, 0.14, 0.2, 0.08, "black")
    bx(k, (0.08, 0.02, 0.05), (0.06, 0.0, 0.045))


def bolt_rifle(k, length=1.16, mag=True, scope=False, furn=WOOD):
    tube(k, 0.012, 0.0, length - 0.39, 0.02)
    bx(k, (0.2, 0.04, 0.045), (0.02, 0, 0.02))
    rifle_stock(k, -0.39, length - 0.48, furn=furn)
    k.cyl(0.01, 0.07, (0.06, -0.02, 0.03), "steel", segments=6, axis="Y", bevel=0)   # стебель затвора
    if mag:
        hang(k, (0.07, 0.03, 0.06), (0.1, 0, -0.03), GM, tilt=0.0)
    if scope:
        tube(k, 0.02, -0.05, 0.2, 0.08, "black")


def svt40(k):
    """СВТ-40: длинная ложа, дульный тормоз, отъёмный магазин, металлический кожух сверху."""
    bolt_rifle(k, length=1.26, mag=True)
    tube(k, 0.017, 0.8, 0.87, 0.02)
    bx(k, (0.25, 0.035, 0.02), (0.38, 0, 0.045), GM)


def mp133(k):
    """Помповое ружьё: ствол над трубчатым магазином, цевьё-помпа, деревянный приклад."""
    tube(k, 0.013, 0.05, 0.81, 0.02)
    tube(k, 0.012, 0.1, 0.65, -0.012)
    bx(k, (0.18, 0.048, 0.04), (0.38, 0, -0.012), WOOD, bevel=0.01)
    bx(k, (0.2, 0.045, 0.06), (0.03, 0, 0.0))
    hang(k, (0.04, 0.035, 0.08), (-0.06, 0, -0.02), WOOD, tilt=0.5)
    bx(k, (0.3, 0.042, 0.065), (-0.24, 0, -0.04), WOOD, ry=-0.12, bevel=0.012)


def doublebarrel(k, cut=False):
    """Двустволка (обрез — укороченные стволы и пистолетная шейка)."""
    ln = 0.42 if cut else 0.81
    for y in (-0.012, 0.012):
        tube(k, 0.013, 0.05, ln, 0.02, y=y)
    bx(k, (0.12, 0.05, 0.05), (0.04, 0, 0.0))
    bx(k, (0.18, 0.048, 0.03), (0.2, 0, -0.005), WOOD, bevel=0.008)
    if cut:
        hang(k, (0.06, 0.04, 0.12), (-0.02, 0, -0.01), WOOD, tilt=0.6, bevel=0.012)
    else:
        hang(k, (0.04, 0.035, 0.08), (-0.04, 0, -0.01), WOOD, tilt=0.5)
        bx(k, (0.3, 0.042, 0.065), (-0.22, 0, -0.035), WOOD, ry=-0.12, bevel=0.012)


def sawnoff(k): doublebarrel(k, cut=True)


def pipeshotgun(k):
    """Самодельный однозарядный дробовик: водопроводная труба, доска-приклад, хомуты, изолента."""
    tube(k, 0.018, -0.02, 0.81, 0.02, "rust")
    tube(k, 0.026, -0.05, 0.08, 0.02, "steel")
    bx(k, (0.5, 0.04, 0.06), (-0.12, 0, -0.03), "planks", ry=-0.08, bevel=0.006)
    for x in (0.1, 0.35):
        bx(k, (0.02, 0.05, 0.05), (x, 0, 0.0), "tape", bevel=0)
    hang(k, (0.035, 0.03, 0.08), (0.0, 0, -0.03), "planks", tilt=0.35)


def waterpipe(k):
    """Обрез из трубы: короткая толстая труба, затвор-заглушка, рукоять из доски."""
    tube(k, 0.022, -0.05, 0.5, 0.03, "rust")
    tube(k, 0.03, -0.1, 0.02, 0.03, "steel")
    hang(k, (0.06, 0.04, 0.13), (-0.04, 0, 0.01), "planks", tilt=0.45, bevel=0.008)
    bx(k, (0.03, 0.05, 0.05), (0.25, 0, 0.03), "tape", bevel=0)


def pistol(k, length=0.21, slide_mat=GM, grip_mat="rubber", grip_tilt=0.2, height=0.03):
    bx(k, (length, 0.028, height), (length / 2 - 0.03, 0, 0.02))                   # затвор
    bx(k, (length * 0.7, 0.026, 0.02), (length * 0.4 - 0.03, 0, -0.005))           # рамка
    hang(k, (0.035, 0.03, 0.11), (0.0, 0, 0.0), grip_mat, tilt=grip_tilt, bevel=0.006)
    bx(k, (0.05, 0.008, 0.03), (0.04, 0, -0.025), bevel=0.002)                    # скоба


def aps(k): pistol(k, 0.22, grip_mat=WOOD)
def grach(k): pistol(k, 0.2, grip_mat="polymer")


def revolver(k, length=0.24):
    """Наган/револьвер: барабан, длинный тонкий ствол, деревянная рукоять."""
    tube(k, 0.01, 0.04, length, 0.02)
    k.cyl(0.022, 0.05, (0.0, 0, 0.012), GM, segments=10, axis="X", bevel=0.003)   # барабан
    bx(k, (0.07, 0.024, 0.03), (0.01, 0, 0.02))
    hang(k, (0.035, 0.028, 0.1), (-0.02, 0, 0.005), WOOD, tilt=0.45, bevel=0.008)
    bx(k, (0.012, 0.01, 0.02), (length - 0.01, 0, 0.035))


def nagant(k): revolver(k, 0.25)


def rocket_launcher(k):
    """РПГ-подобный гранатомёт: труба, конус гранаты спереди, рукоять, прицел."""
    tube(k, 0.035, -0.45, 0.35, 0.03, "paint_green")
    k.cyl(0.04, 0.12, (0.35, 0, 0.03), "paint_green", segments=12, axis="X", bevel=0.003)
    k.cyl(0.055, 0.14, (0.47, 0, 0.03), "paint_green", segments=12, axis="X", bevel=0.004, radius_top=0.012)
    bx(k, (0.12, 0.072, 0.075), (-0.1, 0, 0.03), WOOD, bevel=0.01)               # накладка
    hang(k, (0.035, 0.03, 0.1), (0.0, 0, -0.005), WOOD, tilt=0.2)
    bx(k, (0.04, 0.02, 0.05), (0.05, 0.03, 0.08))
    k.cyl(0.042, 0.04, (-0.48, 0, 0.03), "black", segments=12, axis="X", bevel=0.003, radius_top=0.05)


def crossbow(k):
    """Арбалет: ложа, поперечные дуги (по Y), тетива, стремя."""
    bx(k, (0.5, 0.04, 0.05), (0.05, 0, 0.0), "planks", bevel=0.008)
    hang(k, (0.035, 0.03, 0.08), (-0.05, 0, -0.02), "planks", tilt=0.4)
    for s in (-1, 1):
        bx(k, (0.03, 0.32, 0.02), (0.28 - 0.04, s * 0.16, 0.01), "steel", rz=s * 0.25)
    tube(k, 0.003, -0.02, 0.0, 0.03, "rubber", seg=4)
    for s in (-1, 1):
        bx(k, (0.3, 0.004, 0.004), (0.12, s * 0.16, 0.03), "rubber", rz=-s * 1.0, bevel=0)
    k.torus(0.04, 0.006, (0.33, 0, 0.0), "steel", seg=10, ring=4, axis="Y")


def bow(k):
    """Лук: дуга из двух плеч вверх-вниз (по Z), рукоять с обмоткой, тетива."""
    for s in (-1, 1):
        for i in range(3):
            a0, a1 = i / 3, (i + 1) / 3
            z0, z1 = s * 0.6 * a0, s * 0.6 * a1
            x0, x1 = 0.08 - 0.09 * a0 * a0, 0.08 - 0.09 * a1 * a1
            ln = math.hypot(z1 - z0, x1 - x0)
            ob = k.cyl(0.014 - i * 0.003, ln, (0, 0, 0), "wood_light", segments=6, bevel=0)
            ob.rotation_euler = (0, math.atan2(x1 - x0, z1 - z0), 0)
            ob.location = (x0, 0, z0)
    bx(k, (0.03, 0.03, 0.12), (0.08, 0, 0.0), "leather")
    k.cyl(0.002, 1.2, (-0.01, 0, -0.6), "rubber", segments=4, bevel=0)


def blade(k, length, width, at=0.1, mat="steel", handle=0.12, handle_mat="leather", curve=0.0, tip=True, guard=True):
    """Клинок вдоль X: рукоять, гарда, полотно (в плоскости XZ), острие."""
    tube(k, 0.016, -0.04, handle - 0.04, 0.0, handle_mat, seg=8)
    if guard:
        bx(k, (0.015, 0.03, width + 0.03), (handle - 0.035, 0, 0.0), "steel")
    bx(k, (length, 0.006, width), (handle - 0.03 + length / 2, 0, 0.0), mat, ry=-curve, bevel=0.002)
    if tip:
        ob = k.cyl(width / 2, width * 1.2, (0, 0, 0), mat, segments=3, bevel=0, radius_top=0.0)
        ob.rotation_euler = (math.pi / 2, math.pi / 2, 0)
        ob.scale = (1, 0.12, 1)
        ob.location = (handle - 0.03 + length, 0, -length * curve * 0.5)


def bone_knife(k): blade(k, 0.2, 0.035, mat="bone", handle=0.13, handle_mat="leather", guard=False)
def cleaver(k):
    tube(k, 0.017, -0.04, 0.12, 0.0, "planks", seg=8)
    bx(k, (0.24, 0.006, 0.1), (0.24, 0, 0.03), "steel", bevel=0.002)
def salvaged_sword(k): blade(k, 0.62, 0.05, mat="rust", handle=0.16, handle_mat="tape")


def spear(k):
    """Копьё: длинное древко, кованый наконечник, обмотка у хвата."""
    tube(k, 0.016, -0.6, 0.95, 0.0, "planks", seg=8)
    ob = k.cyl(0.03, 0.18, (0.95, 0, 0), "rust", segments=4, axis="X", bevel=0, radius_top=0.0)
    ob.scale = (1, 1, 0.3)
    tube(k, 0.02, 0.88, 0.97, 0.0, "steel", seg=8)
    tube(k, 0.019, -0.06, 0.08, 0.0, "leather", seg=8)


def pipe(k):
    """Обрезок трубы с намотанной ручкой."""
    tube(k, 0.016, -0.08, 0.62, 0.0, "rust", seg=10)
    tube(k, 0.019, -0.08, 0.1, 0.0, "tape", seg=10)
    k.cyl(0.024, 0.04, (0.6, 0, 0), "steel", segments=6, axis="X", bevel=0.003)


def torch(k):
    """Факел: палка, обмотка из тряпья, пламя."""
    tube(k, 0.016, -0.05, 0.45, 0.0, "planks", seg=8)
    k.cyl(0.035, 0.1, (0.4, 0, 0), "cloth", segments=10, axis="X", bevel=0.005)
    k.blob(0.05, (0.53, 0, 0.02), "glow", subdiv=1, scale=(1.4, 0.8, 1.0), jitter=0.3, seed=3, smooth=True)


def rock(k):
    k.blob(0.065, (0.06, 0, 0.0), "concrete", subdiv=2, scale=(1.15, 0.85, 0.8), jitter=0.2, seed=17)


def build_plan(k):
    """План постройки: свёрнутый с одного края лист синьки."""
    bx(k, (0.22, 0.2, 0.004), (0.12, 0.0, 0.0), "blueprint", bevel=0)
    k.cyl(0.015, 0.2, (0.0, -0.1, 0.0), "blueprint", segments=10, axis="Y", bevel=0)
    for i in range(4):
        bx(k, (0.15, 0.004, 0.001), (0.13, -0.06 + i * 0.04, 0.003), "white", bevel=0)


def tool_head_handle(k, length=0.6, mat="planks"):
    tube(k, 0.016, -0.1, length - 0.08, 0.0, mat, seg=8)


def tool(k):
    """Запасной инструмент (если нет своей модели): молоток-гвоздодёр."""
    tool_head_handle(k)
    bx(k, (0.04, 0.035, 0.15), (0.53, 0, 0.0), "steel")


def metal_hatchet(k):
    """Стальной топор: черен с обмоткой, кованый обух, широкое лезвие вверх (2D weapon_metal_hatchet)."""
    tool_head_handle(k, 0.6, WOOD)
    bx(k, (0.07, 0.03, 0.05), (0.52, 0, 0.0), "steel")
    ob = bx(k, (0.1, 0.008, 0.12), (0.52, 0, 0.08), "steel", bevel=0.002)
    tube(k, 0.019, -0.1, 0.05, 0.0, "leather", seg=8)


def metal_pickaxe(k):
    """Стальная кирка: длинный черен, двусторонний клюв по Z."""
    tool_head_handle(k, 0.62, WOOD)
    bx(k, (0.05, 0.035, 0.06), (0.52, 0, 0.0), "steel")
    for s in (-1, 1):
        ob = k.cyl(0.018, 0.18, (0.52, 0, s * 0.02), "steel", segments=4, bevel=0, radius_top=0.003)
        if s < 0:
            ob.rotation_euler.x = math.pi
        ob.rotation_euler.y = s * 0.15


def chainsaw(k):
    """Бензопила: корпус-мотор, передняя ручка-дуга, шина с цепью вперёд."""
    bx(k, (0.3, 0.12, 0.16), (0.0, 0, 0.02), "yellow", bevel=0.02)
    bx(k, (0.16, 0.124, 0.06), (0.0, 0, 0.11), "rubber", bevel=0.01)
    bx(k, (0.42, 0.012, 0.07), (0.36, 0, 0.0), "steel", bevel=0.01)
    bx(k, (0.43, 0.014, 0.01), (0.36, 0, 0.037), "black", bevel=0)
    bx(k, (0.43, 0.014, 0.01), (0.36, 0, -0.037), "black", bevel=0)
    k.torus(0.06, 0.01, (0.08, 0, 0.08), "rubber", seg=10, ring=4, axis="X")


def jackhammer(k):
    """Отбойный молоток: Т-образная рукоять, корпус, длинная пика вперёд."""
    tube(k, 0.045, 0.05, 0.4, 0.0, "paint_blue", seg=12)
    tube(k, 0.012, 0.4, 0.7, 0.0, "steel", seg=8)
    bx(k, (0.04, 0.12, 0.025), (0.0, 0, 0.0), "rubber")
    bx(k, (0.06, 0.04, 0.04), (0.04, 0, 0.0), "paint_blue")


def weapon(k): kalash(k, "wood", WOOD)
