"""
Генератор low-poly моделей для 3D-клиента: python scripts/gen-3d-assets.py  →  game/art3d/*.glb

Всё собрано из примитивов glb3d.py — это настоящие .glb, их можно открыть в Blender и доработать
(или заменить своими/бесплатными с тем же именем файла). Договорённость: 1 ед. = 1 м, Y вверх, перед — +X.
Персонажи состоят из частей hips/torso/head/arm_l/arm_r/leg_l/leg_r — клиент двигает их (ходьба, прицел, присед).
"""
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
from glb3d import (M, Part, Geo, box, cyl, cyl_between, blob, prism, quad, rot_x, rot_y, rot_z, write_glb, read_glb)  # noqa: E402

OUT = os.path.join(os.path.dirname(__file__), "..", "game", "art3d")
made = []


def save(name, root):
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, name + ".glb")
    write_glb(path, root)
    read_glb(path)   # проверка структуры
    made.append(name)


def single(name, geo):
    root = Part(name)
    root.add(geo)
    save(name, root)


# ================================================================ палитра

SKIN = M("skin", "#d8b096", 0.8)
SKIN_D = M("skin_d", "#b98d72", 0.8)
HAIR = M("hair", "#3b2c22", 0.9)
EYE = M("eye", "#1b1b1f", 0.6)
BOOT = M("boot", "#2b2420", 0.9)
BELT = M("belt", "#3a2d22", 0.8)
METAL_D = M("metal_d", "#2a2b2e", 0.45, 0.7)
METAL = M("metal", "#5c5f63", 0.4, 0.75)
STEEL = M("steel", "#8d9297", 0.35, 0.8)
RUST = M("rust", "#7a4a2e", 0.8, 0.3)
WOOD = M("wood", "#7b5434", 0.85)
WOOD_D = M("wood_d", "#553a24", 0.9)
WOOD_L = M("wood_l", "#a27a50", 0.85)
PLANK = M("plank", "#8e6a43", 0.9)
STONE = M("stone", "#8a8986", 0.95)
STONE_D = M("stone_d", "#62615e", 0.95)
CLOTH_R = M("cloth_r", "#8c2e26", 0.95)
OLIVE = M("olive", "#556040", 0.9)
GLOW_FIRE = M("fire", "#ff8a33", 0.5, 0, emissive="#ff7a22")
GLOW_LAMP = M("lamp", "#fff1c8", 0.4, 0, emissive="#ffe6a8")
GREEN_M = M("green_m", "#3f5d45", 0.6, 0.4)
YELLOW_M = M("yellow_m", "#c9a227", 0.6, 0.3)
BLUE_M = M("blue_m", "#36526e", 0.6, 0.4)
RED_M = M("red_m", "#9a2f25", 0.6, 0.3)
WHITE_M = M("white_m", "#d9d9d2", 0.7, 0.1)
BLACK = M("black", "#1a1a1a", 0.8)
ROPE = M("rope", "#a68e62", 0.95)


# ================================================================ персонажи

def character(name, jacket, pants, extra=None, helmet=None, vest=None, hair=HAIR, cap=None):
    """Человек ~1,8 м из частей с суставами в нужных местах. Перед — +X, правая сторона — +Z."""
    root = Part(name)
    hips = root.child("hips", (0, 0.88, 0))
    hips.add(box(pants, (0, 0.0, 0), (0.24, 0.16, 0.36)))
    hips.add(box(BELT, (0, 0.07, 0), (0.25, 0.05, 0.37)))

    torso = hips.child("torso", (0, 0.06, 0))
    # корпус: чуть шире в плечах, low-poly «трапеция»
    torso.add(prism(jacket, 0.0, 0.5, [(-0.12, -0.18), (0.12, -0.18), (0.12, 0.18), (-0.12, 0.18)],
                    [(-0.13, -0.23), (0.13, -0.23), (0.13, 0.23), (-0.13, 0.23)]))
    torso.add(box(jacket, (0, 0.53, 0), (0.24, 0.07, 0.38)))          # плечи
    torso.add(box(jacket, (0.13, 0.32, 0), (0.02, 0.28, 0.06)))       # молния
    if vest is not None:
        torso.add(box(vest, (0.0, 0.29, 0), (0.29, 0.33, 0.42)))
        torso.add(box(vest, (0.15, 0.25, -0.08), (0.04, 0.1, 0.1)))   # подсумки
        torso.add(box(vest, (0.15, 0.25, 0.08), (0.04, 0.1, 0.1)))
    torso.add(box(SKIN_D, (0, 0.59, 0), (0.09, 0.07, 0.09)))         # шея

    head = torso.child("head", (0, 0.62, 0))
    head.add(box(SKIN, (0, 0.12, 0), (0.21, 0.24, 0.2)))
    head.add(box(SKIN_D, (0.11, 0.09, 0), (0.03, 0.05, 0.04)))       # нос
    head.add(box(EYE, (0.106, 0.15, -0.05), (0.01, 0.025, 0.035)))
    head.add(box(EYE, (0.106, 0.15, 0.05), (0.01, 0.025, 0.035)))
    if helmet is not None:
        head.add(box(helmet, (-0.005, 0.26, 0), (0.26, 0.08, 0.25)))
        head.add(box(helmet, (-0.02, 0.2, 0), (0.24, 0.08, 0.26)))
    elif cap is not None:
        head.add(box(cap, (0, 0.26, 0), (0.23, 0.06, 0.22)))
        head.add(box(cap, (0.14, 0.235, 0), (0.1, 0.02, 0.2)))        # козырёк
    else:
        head.add(box(hair, (-0.01, 0.255, 0), (0.23, 0.06, 0.22)))
        head.add(box(hair, (-0.095, 0.16, 0), (0.04, 0.16, 0.21)))

    for side, z in (("l", -0.25), ("r", 0.25)):
        arm = torso.child("arm_" + side, (0, 0.5, z))
        arm.add(box(jacket, (0, -0.14, 0), (0.13, 0.3, 0.13)))        # плечо
        arm.add(box(jacket, (0, -0.39, 0), (0.115, 0.24, 0.115)))     # предплечье
        arm.add(box(SKIN, (0, -0.56, 0), (0.09, 0.1, 0.08)))          # кисть
        leg = hips.child("leg_" + side, (0, -0.04, z * 0.44))
        leg.add(box(pants, (0, -0.2, 0), (0.16, 0.42, 0.16)))          # бедро
        leg.add(box(pants, (0, -0.58, 0), (0.14, 0.36, 0.14)))         # голень
        leg.add(box(BOOT, (0.04, -0.79, 0), (0.26, 0.1, 0.15)))        # ботинок
    if extra:
        extra(root, torso, head)
    save(name, root)


def backpack(root, torso, head):
    torso.add(box(OLIVE, (-0.2, 0.29, 0), (0.16, 0.38, 0.3)))
    torso.add(box(WOOD_D, (-0.2, 0.49, 0), (0.18, 0.04, 0.32)))


character("char_player", M("jacket_p", "#3d6b8a", 0.9), M("pants_p", "#2f3238", 0.95), extra=backpack)
character("char_bot", M("jacket_b", "#9a6a3c", 0.9), M("pants_b", "#4a4038", 0.95), cap=M("cap_b", "#5a3a26", 0.9))
character("char_guard", M("jacket_g", "#5b6447", 0.9), M("pants_g", "#4b5239", 0.95),
          helmet=M("helmet", "#4a5139", 0.6, 0.2), vest=M("vest", "#3c4231", 0.9))
character("char_naked", SKIN, M("shorts", "#3c3a44", 0.95))


# ================================================================ оружие и инструменты
# держится за рукоятку в (0,0,0), ствол/рабочая часть — вдоль +X, верх — +Y

def rifle(name, stock=WOOD, body=METAL_D, barrel_len=0.48, mag="ak", scope=False, bayonet=False, length=0.0):
    g = Geo()
    g.merge(box(stock, (-0.2, -0.02, 0), (0.32, 0.1, 0.05)))                    # приклад
    g.merge(box(stock, (-0.36, -0.05, 0), (0.06, 0.15, 0.055)))
    g.merge(box(body, (0.12, 0.02, 0), (0.34, 0.08, 0.055)))                    # ствольная коробка
    g.merge(box(stock, (0.26, 0.0, 0), (0.18, 0.06, 0.06)))                     # цевьё
    g.merge(cyl(body, (0.29, 0.03, 0), barrel_len + length, 0.013, R=rot_z(-math.pi / 2), seg=6))   # ствол
    g.merge(box(WOOD_D, (0.0, -0.08, 0), (0.05, 0.1, 0.04), R=rot_z(0.25)))    # пистолетная рукоятка
    if mag == "ak":
        g.merge(box(body, (0.08, -0.12, 0), (0.06, 0.16, 0.035), R=rot_z(-0.35)))
    elif mag == "box":
        g.merge(box(body, (0.08, -0.09, 0), (0.07, 0.1, 0.04)))
    elif mag == "drum":
        g.merge(cyl(body, (0.1, -0.13, -0.03), 0.06, 0.07, R=rot_x(math.pi / 2), seg=10))
    if scope:
        g.merge(cyl(METAL_D, (0.02, 0.11, 0), 0.24, 0.022, R=rot_z(-math.pi / 2), seg=8))
        g.merge(box(METAL_D, (0.14, 0.075, 0), (0.03, 0.04, 0.02)))
    if bayonet:
        g.merge(box(STEEL, (0.29 + barrel_len + 0.1, 0.0, 0), (0.22, 0.02, 0.01)))
    single(name, g)


def pistol(name, body=METAL_D, grip=WOOD_D, long_=False):
    g = Geo()
    g.merge(box(body, (0.08, 0.02, 0), (0.2 if not long_ else 0.26, 0.05, 0.03)))
    g.merge(box(grip, (0.0, -0.06, 0), (0.05, 0.11, 0.032), R=rot_z(0.3)))
    g.merge(box(body, (0.03, -0.015, 0), (0.06, 0.012, 0.02)))
    single(name, g)


def revolver(name):
    g = Geo()
    g.merge(cyl(METAL_D, (0.05, 0.03, 0), 0.2, 0.012, R=rot_z(-math.pi / 2), seg=6))
    g.merge(cyl(METAL, (0.04, 0.02, -0.025), 0.05, 0.028, R=rot_x(math.pi / 2), seg=8))
    g.merge(box(WOOD_D, (-0.01, -0.06, 0), (0.05, 0.1, 0.032), R=rot_z(0.4)))
    single(name, g)


def shotgun(name, barrels=1, pump=True, short=False, body=METAL_D, stock=WOOD):
    g = Geo()
    L = 0.32 if short else 0.62
    if not short:
        g.merge(box(stock, (-0.2, -0.03, 0), (0.34, 0.1, 0.05)))
    else:
        g.merge(box(stock, (-0.06, -0.05, 0), (0.1, 0.12, 0.045), R=rot_z(0.4)))
    g.merge(box(body, (0.08, 0.02, 0), (0.22, 0.08, 0.05)))
    for i in range(barrels):
        z = 0 if barrels == 1 else (-0.016 if i == 0 else 0.016)
        g.merge(cyl(body, (0.19, 0.035, z), L, 0.016, R=rot_z(-math.pi / 2), seg=6))
    if pump:
        g.merge(box(stock, (0.35, 0.0, 0), (0.16, 0.045, 0.05)))
    single(name, g)


def smg(name, mag="box", stock=WOOD):
    g = Geo()
    g.merge(box(stock, (-0.17, -0.02, 0), (0.24, 0.09, 0.05)))
    g.merge(box(METAL_D, (0.08, 0.02, 0), (0.26, 0.08, 0.05)))
    g.merge(cyl(METAL_D, (0.2, 0.03, 0), 0.18, 0.02, R=rot_z(-math.pi / 2), seg=8))   # кожух
    g.merge(box(WOOD_D, (0.0, -0.07, 0), (0.05, 0.09, 0.04), R=rot_z(0.25)))
    if mag == "drum":
        g.merge(cyl(METAL_D, (0.07, -0.12, -0.035), 0.07, 0.08, R=rot_x(math.pi / 2), seg=10))
    else:
        g.merge(box(METAL_D, (0.08, -0.13, 0), (0.045, 0.18, 0.03), R=rot_z(-0.1)))
    single(name, g)


def bow(name, cross=False):
    g = Geo()
    if cross:
        g.merge(box(WOOD, (0.05, 0, 0), (0.55, 0.06, 0.05)))
        pts = [(0.3, 0, -0.32), (0.34, 0, -0.16), (0.36, 0, 0), (0.34, 0, 0.16), (0.3, 0, 0.32)]
        for a, b in zip(pts, pts[1:]):
            g.merge(cyl_between(WOOD_D, a, b, 0.015, 5))
        g.merge(cyl_between(ROPE, (0.3, 0.01, -0.32), (0.12, 0.01, 0), 0.004, 3))
        g.merge(cyl_between(ROPE, (0.12, 0.01, 0), (0.3, 0.01, 0.32), 0.004, 3))
    else:
        pts = [(0.05, -0.62, 0), (0.12, -0.4, 0), (0.15, -0.15, 0), (0.15, 0.15, 0), (0.12, 0.4, 0), (0.05, 0.62, 0)]
        for a, b in zip(pts, pts[1:]):
            g.merge(cyl_between(WOOD, a, b, 0.018, 5))
        g.merge(box(WOOD_D, (0.15, 0, 0), (0.04, 0.12, 0.04)))
        g.merge(cyl_between(ROPE, (0.05, -0.62, 0), (0.05, 0.62, 0), 0.004, 3))
    single(name, g)


def launcher(name):
    g = Geo()
    g.merge(cyl(GREEN_M, (-0.45, 0.06, 0), 1.0, 0.045, R=rot_z(-math.pi / 2), seg=10))
    g.merge(box(WOOD_D, (0.0, -0.05, 0), (0.05, 0.1, 0.04)))
    g.merge(box(METAL_D, (0.12, 0.12, 0), (0.06, 0.06, 0.03)))
    single(name, g)


def melee(name, blade=STEEL, handle=WOOD, blade_len=0.42, width=0.05, spear=False, club=False, pipe=False):
    g = Geo()
    if spear:
        g.merge(cyl(handle, (-0.6, 0, 0), 1.5, 0.017, R=rot_z(-math.pi / 2), seg=6))
        g.merge(cyl(blade, (0.9, 0, 0), 0.22, 0.03, 0.0, R=rot_z(-math.pi / 2), seg=4))
    elif club:
        g.merge(cyl(handle, (-0.05, 0, 0), 0.65, 0.025, 0.045, R=rot_z(-math.pi / 2), seg=6))
        for i in range(4):
            g.merge(box(METAL, (0.45 + i * 0.05, 0.03 * (-1) ** i, 0), (0.02, 0.06, 0.02)))
    elif pipe:
        g.merge(cyl(RUST, (-0.08, 0, 0), 0.7, 0.02, R=rot_z(-math.pi / 2), seg=6))
    else:
        g.merge(box(handle, (0.02, 0, 0), (0.14, 0.035, 0.03)))
        g.merge(box(METAL, (0.1, 0, 0), (0.02, 0.07, 0.04)))
        g.merge(prism(blade, -0.006, 0.006, [(0.11, -width / 2), (0.11 + blade_len, -width / 3), (0.11 + blade_len + 0.06, 0), (0.11, width / 2)]))
        # prism строится в плоскости XZ — повернём лезвие вертикально
        last = g.tris[-1]
        v = last[1].reshape(-1, 3) @ rot_x(math.pi / 2).T
        g.tris[-1] = (last[0], v.reshape(-1, 3, 3))
    single(name, g)


def tool(name, head_mat=STONE, handle=WOOD, kind="hatchet"):
    g = Geo()
    g.merge(cyl(handle, (-0.1, 0, 0), 0.62, 0.016, R=rot_z(-math.pi / 2), seg=6))
    if kind == "hatchet":
        g.merge(box(head_mat, (0.48, 0.05, 0), (0.07, 0.14, 0.03)))
        g.merge(box(head_mat, (0.48, 0.13, 0), (0.1, 0.04, 0.02)))
    elif kind == "pickaxe":
        g.merge(cyl_between(head_mat, (0.49, -0.2, 0), (0.49, 0.2, 0), 0.025, 6, r2=0.008))
    elif kind == "hammer":
        g.merge(box(head_mat, (0.5, 0.0, 0), (0.07, 0.16, 0.06)))
    elif kind == "chainsaw":
        g = Geo()
        g.merge(box(RED_M, (0.05, 0, 0), (0.28, 0.16, 0.12)))
        g.merge(box(STEEL, (0.42, 0, 0), (0.5, 0.06, 0.02)))
        g.merge(cyl_between(BLACK, (-0.02, 0.1, -0.06), (-0.02, 0.1, 0.06), 0.015, 5))
    elif kind == "jackhammer":
        g = Geo()
        g.merge(box(YELLOW_M, (0.2, 0, 0), (0.4, 0.14, 0.12)))
        g.merge(cyl(STEEL, (0.4, 0, 0), 0.3, 0.015, R=rot_z(-math.pi / 2), seg=6))
        g.merge(cyl_between(BLACK, (0.0, -0.1, 0), (0.0, 0.1, 0), 0.02, 5))
    elif kind == "plan":
        g = Geo()
        g.merge(box(WHITE_M, (0.12, 0, 0), (0.26, 0.005, 0.2)))
        g.merge(box(BLUE_M, (0.12, 0.004, 0), (0.2, 0.004, 0.14)))
    elif kind == "torch":
        g = Geo()
        g.merge(cyl(WOOD_D, (-0.05, 0, 0), 0.45, 0.02, R=rot_z(-math.pi / 2), seg=6))
        g.merge(blob(GLOW_FIRE, (0.45, 0.02, 0), 0.06, sub=0))
    elif kind == "rock":
        g = Geo()
        g.merge(blob(STONE, (0.06, 0, 0), (0.08, 0.06, 0.07), sub=0, jitter=0.2, rng=np.random.default_rng(5)))
    single(name, g)


rifle("held_weapon_ak", mag="ak")
rifle("held_weapon_akm", mag="ak", stock=WOOD_L)
rifle("held_weapon_an94", mag="ak", stock=METAL_D, body=METAL)
rifle("held_weapon_svt40", mag="box", barrel_len=0.58)
rifle("held_weapon_mosin", mag=None, barrel_len=0.66, bayonet=True)
rifle("held_weapon_svd", mag="box", stock=WOOD_L, barrel_len=0.66, scope=True)
rifle("held_weapon", mag="ak")   # запасное «ружьё вообще»
smg("held_weapon_ppsh", mag="drum")
smg("held_weapon_pps43", mag="box", stock=METAL_D)
smg("held_weapon_homesmg", mag="box", stock=RUST)
smg("held_weapon_smg", mag="box")
pistol("held_weapon_aps", long_=True)
pistol("held_weapon_grach")
revolver("held_weapon_nagant")
revolver("held_weapon_revolver")
shotgun("held_weapon_mp133", pump=True)
shotgun("held_weapon_saiga12", pump=False, stock=METAL_D)
shotgun("held_weapon_doublebarrel", barrels=2, pump=False)
shotgun("held_weapon_sawnoff", barrels=2, pump=False, short=True)
shotgun("held_weapon_pipeshotgun", pump=False, body=RUST, stock=WOOD_D)
shotgun("held_weapon_waterpipe", pump=False, body=RUST, stock=WOOD_D, short=True)
bow("held_weapon_bow")
bow("held_weapon_crossbow", cross=True)
launcher("held_weapon_rocket_launcher")
melee("held_weapon_machete")
melee("held_weapon_cleaver", blade_len=0.2, width=0.1)
melee("held_weapon_salvaged_sword", blade=RUST, blade_len=0.6)
melee("held_weapon_bone_knife", blade=WHITE_M, blade_len=0.16)
melee("held_weapon_spear", spear=True)
melee("held_weapon_club", club=True)
melee("held_weapon_pipe", pipe=True)
tool("held_tool_hatchet", STONE, kind="hatchet")
tool("held_tool_metal_hatchet", STEEL, kind="hatchet")
tool("held_tool_pickaxe", STONE, kind="pickaxe")
tool("held_tool_metal_pickaxe", STEEL, kind="pickaxe")
tool("held_tool_hammer", METAL, kind="hammer")
tool("held_tool_chainsaw", kind="chainsaw")
tool("held_tool_jackhammer", kind="jackhammer")
tool("held_build_plan", kind="plan")
tool("held_weapon_torch", kind="torch")
tool("held_tool_rock", kind="rock")
tool("held_tool", METAL, kind="hammer")


# ================================================================ деревья (по биомам)

BARK = M("bark", "#5a4330", 0.95)
BARK_D = M("bark_d", "#3f3024", 0.95)
BIRCH = M("birch", "#dedad0", 0.9)
BIRCH_M = M("birch_m", "#2b2b2b", 0.9)
ASPEN = M("aspen", "#a4a88c", 0.9)
PINE = [M("pine1", "#2f4a33", 0.95), M("pine2", "#3a5a3a", 0.95), M("pine3", "#294030", 0.95)]
LEAF = [M("leaf1", "#4d7a36", 0.95), M("leaf2", "#5f8a3c", 0.95), M("leaf3", "#3f6a30", 0.95), M("leaf_autumn", "#9a8a3a", 0.95)]
SNOW = M("snow", "#e8ecef", 0.9)


def pine(name, seed, height=8.0, layers=4, snowy=False, spruce=False):
    rng = np.random.default_rng(seed)
    g = Geo()
    g.merge(cyl(BARK, (0, 0, 0), height * 0.55, 0.22, 0.12, seg=7))
    base = height * (0.22 if spruce else 0.3)
    for i in range(layers):
        t = i / max(1, layers - 1)
        r = (2.1 if spruce else 2.5) * (1 - 0.72 * t) * rng.uniform(0.9, 1.1)
        y = base + (height - base) * 0.82 * t
        h = (height - base) / layers * (1.9 if spruce else 1.7)
        mat = PINE[int(rng.integers(0, len(PINE)))]
        g.merge(cyl(mat, (rng.uniform(-0.08, 0.08), y, rng.uniform(-0.08, 0.08)), h, r, 0.0, seg=7 if spruce else 8, phase=rng.uniform(0, 1)))
        if snowy:
            g.merge(cyl(SNOW, (0, y + h * 0.55, 0), h * 0.45, r * 0.46, 0.0, seg=7))
    single(name, g)


def broadleaf(name, seed, trunk=BARK, height=7.0, crown=2.4, clusters=5, birch=False, autumn=False, squash=1.0):
    rng = np.random.default_rng(seed)
    g = Geo()
    th = height * 0.62
    g.merge(cyl(trunk, (0, 0, 0), th, 0.2 if not birch else 0.14, 0.12 if not birch else 0.09, seg=7))
    if birch:
        for i in range(7):
            y = rng.uniform(0.4, th)
            a = rng.uniform(0, math.tau)
            g.merge(box(BIRCH_M, (math.cos(a) * 0.12, y, math.sin(a) * 0.12), (0.06, 0.03, 0.06), R=rot_y(-a)))
    # ветки к кронам
    cy = th + crown * 0.4
    for i in range(clusters):
        a = math.tau * i / clusters + rng.uniform(-0.3, 0.3)
        d = crown * rng.uniform(0.35, 0.6)
        c = (math.cos(a) * d, cy + rng.uniform(-0.5, 0.8), math.sin(a) * d)
        g.merge(cyl_between(trunk, (0, th * 0.75, 0), (c[0] * 0.6, c[1] - 0.4, c[2] * 0.6), 0.06, 5))
        mat = LEAF[3] if autumn and rng.uniform() < 0.5 else LEAF[int(rng.integers(0, 3))]
        g.merge(blob(mat, c, crown * rng.uniform(0.45, 0.6), sub=1, jitter=0.18, rng=rng, squash=(1, 0.8 * squash, 1)))
    g.merge(blob(LEAF[int(rng.integers(0, 3))], (0, cy + crown * 0.35, 0), crown * 0.62, sub=1, jitter=0.15, rng=rng, squash=(1, 0.85 * squash, 1)))
    single(name, g)


def dead_tree(name, seed, height=6.0):
    rng = np.random.default_rng(seed)
    g = Geo()
    g.merge(cyl(BARK_D, (0, 0, 0), height, 0.2, 0.05, seg=6))
    for i in range(6):
        y = rng.uniform(height * 0.35, height * 0.9)
        a = rng.uniform(0, math.tau)
        L = rng.uniform(0.8, 1.8) * (1 - y / height * 0.5)
        g.merge(cyl_between(BARK_D, (0, y, 0), (math.cos(a) * L, y + L * 0.6, math.sin(a) * L), 0.05, 4, r2=0.015))
    single(name, g)


# node_tree_<биом>_<N>: клиент выбирает по биому места и Look узла
pine("node_tree_forest_0", 1, 8.5, 4)
pine("node_tree_forest_1", 2, 9.5, 5, spruce=True)
pine("node_tree_forest_2", 3, 7.5, 4)
broadleaf("node_tree_forest_3", 4, BIRCH, 7.5, 2.2, 4, birch=True)
broadleaf("node_tree_grassland_0", 11, BARK, 7.0, 2.6, 5)
broadleaf("node_tree_grassland_1", 12, BIRCH, 7.8, 2.1, 4, birch=True)
broadleaf("node_tree_grassland_2", 13, ASPEN, 8.5, 1.8, 4, squash=1.4)
broadleaf("node_tree_grassland_3", 14, BARK_D, 6.2, 3.0, 6, autumn=True)
pine("node_tree_tundra_0", 21, 7.0, 4, snowy=True, spruce=True)
pine("node_tree_tundra_1", 22, 6.0, 3, snowy=True)
dead_tree("node_tree_tundra_2", 23, 5.5)
dead_tree("node_tree_swamp_0", 31, 5.5)
broadleaf("node_tree_swamp_1", 32, BIRCH, 6.5, 1.8, 3, birch=True)
broadleaf("node_tree_swamp_2", 33, BARK_D, 5.5, 2.2, 4, autumn=True)
dead_tree("node_tree_arid_0", 41, 5.0)
pine("node_tree_arid_1", 42, 6.5, 3)
broadleaf("node_tree_arid_2", 43, BARK, 5.0, 2.0, 3, autumn=True)
pine("node_tree_0", 51, 8.0, 4)   # запасной (без биома)


# ================================================================ кусты, растения

def bush(name, seed, mat, berries=None, r=0.7):
    rng = np.random.default_rng(seed)
    g = Geo()
    for i in range(4):
        a = math.tau * i / 4 + rng.uniform(-0.4, 0.4)
        g.merge(blob(mat, (math.cos(a) * r * 0.4, r * 0.55, math.sin(a) * r * 0.4), r * rng.uniform(0.5, 0.65), sub=1, jitter=0.2, rng=rng, flat_bottom=0.02))
    if berries:
        for i in range(12):
            a = rng.uniform(0, math.tau); y = rng.uniform(0.3, 1.0) * r
            g.merge(blob(berries, (math.cos(a) * r * 0.75, y, math.sin(a) * r * 0.75), 0.05, sub=0))
    single(name, g)


def hemp(name, seed):
    rng = np.random.default_rng(seed)
    g = Geo()
    leaf = M("hemp", "#4f7d32", 0.9)
    g.merge(cyl(M("hemp_s", "#5a7a3a", 0.9), (0, 0, 0), 1.1, 0.02, 0.01, seg=5))
    for i in range(7):
        y = 0.3 + i * 0.12
        for k in range(5):
            a = math.tau * k / 5 + i * 0.7
            L = 0.35 * (1 - i / 9)
            g.merge(quad(leaf, (0, y, 0), (math.cos(a + 0.2) * L * 0.5, y + 0.05, math.sin(a + 0.2) * L * 0.5),
                         (math.cos(a) * L, y + 0.1, math.sin(a) * L), (math.cos(a - 0.2) * L * 0.5, y + 0.05, math.sin(a - 0.2) * L * 0.5)))
    single(name, g)


bush("plant_bush_0", 61, LEAF[0])
bush("plant_bush_1", 62, LEAF[2], r=0.9)
bush("pickup_berries", 63, LEAF[2], berries=M("berry", "#a01e35", 0.5))
hemp("pickup_hemp", 64)


# ================================================================ камни и руда

def rock(name, seed, vein=None, size=1.0, chunks=3):
    rng = np.random.default_rng(seed)
    g = Geo()
    for i in range(chunks):
        a = rng.uniform(0, math.tau); d = rng.uniform(0, 0.35) * size
        r = size * rng.uniform(0.55, 0.85) * (1 if i == 0 else 0.7)
        mat = STONE if rng.uniform() < 0.6 else STONE_D
        g.merge(blob(mat, (math.cos(a) * d, r * 0.45, math.sin(a) * d), r, sub=1, jitter=0.22, rng=rng, squash=(1.1, 0.75, 1.0), flat_bottom=0.0))
    if vein:
        for i in range(6):
            a = rng.uniform(0, math.tau); y = rng.uniform(0.15, 0.45) * size
            g.merge(blob(vein, (math.cos(a) * size * 0.6, y, math.sin(a) * size * 0.6), size * rng.uniform(0.15, 0.22), sub=0, jitter=0.2, rng=rng))
    single(name, g)


for i in range(3):
    rock(f"node_stone_{i}", 70 + i, None, 1.0 + 0.15 * i)
    rock(f"node_metal_{i}", 80 + i, M("ore_metal", "#8b5a3c", 0.5, 0.5), 1.0 + 0.1 * i)
    rock(f"node_sulfur_{i}", 90 + i, M("ore_sulfur", "#d9c13a", 0.6), 1.0 + 0.1 * i)


def junk_pile(name, seed):
    rng = np.random.default_rng(seed)
    g = Geo()
    g.merge(blob(M("dirt", "#4f4438", 0.95), (0, 0.1, 0), (1.0, 0.35, 0.9), sub=1, jitter=0.2, rng=rng, flat_bottom=0))
    for i in range(9):
        mat = [RUST, METAL, PLANK, BLUE_M, RED_M][int(rng.integers(0, 5))]
        s = rng.uniform(0.15, 0.5, size=3)
        g.merge(box(mat, (rng.uniform(-0.6, 0.6), rng.uniform(0.15, 0.45), rng.uniform(-0.5, 0.5)), s, R=rot_y(rng.uniform(0, 3)) @ rot_x(rng.uniform(-0.5, 0.5))))
    single(name, g)


junk_pile("node_junk", 100)
g = Geo()
g.merge(cyl(BLUE_M, (0, 0, 0), 0.9, 0.3, seg=10))
g.merge(cyl(M("ring", "#2d445a", 0.6, 0.4), (0, 0.3, 0), 0.04, 0.31, seg=10))
g.merge(cyl(M("ring", "#2d445a", 0.6, 0.4), (0, 0.62, 0), 0.04, 0.31, seg=10))
single("node_barrel", g)


def tires(name, n):
    g = Geo()
    rubber = M("rubber", "#1c1c1c", 0.9)
    seg = 14
    for i in range(n):
        y0, y1 = i * 0.22, i * 0.22 + 0.22
        g.merge(cyl(rubber, (0, y0, 0), 0.22, 0.42, seg=seg, caps=False))      # протектор
        g.merge(cyl(rubber, (0, y0, 0), 0.22, 0.2, seg=seg, caps=False))       # обод изнутри
        for y in (y0, y1):                                                    # боковины-кольца
            tris = []
            for k in range(seg):
                a0, a1 = math.tau * k / seg, math.tau * (k + 1) / seg
                o0 = (math.cos(a0) * 0.42, y, math.sin(a0) * 0.42); o1 = (math.cos(a1) * 0.42, y, math.sin(a1) * 0.42)
                i0 = (math.cos(a0) * 0.2, y, math.sin(a0) * 0.2); i1 = (math.cos(a1) * 0.2, y, math.sin(a1) * 0.2)
                tris += [(o0, o1, i1), (o0, i1, i0)]
            g.add(rubber, tris)
    single(name, g)


tires("node_tire", 1)
tires("node_tirestack", 3)


# ================================================================ установленное (размер подгоняется клиентом под хитбокс)

def workbench():
    g = Geo()
    g.merge(box(PLANK, (0, 0.88, 0), (1.6, 0.08, 0.8)))
    for x in (-0.72, 0.72):
        for z in (-0.33, 0.33):
            g.merge(box(WOOD_D, (x, 0.42, z), (0.08, 0.84, 0.08)))
    g.merge(box(WOOD_D, (0, 0.2, 0), (1.5, 0.05, 0.7)))
    g.merge(box(METAL, (0.6, 0.98, 0.25), (0.14, 0.12, 0.12)))           # тиски
    g.merge(box(METAL_D, (-0.4, 0.95, -0.1), (0.3, 0.04, 0.05)))
    g.merge(box(RED_M, (-0.1, 0.96, 0.2), (0.18, 0.08, 0.1)))            # ящик с инструментом
    g.merge(box(PLANK, (0, 1.4, -0.36), (1.5, 0.9, 0.04)))               # щит с инструментом
    for i in range(4):
        g.merge(box(METAL_D, (-0.5 + i * 0.32, 1.4, -0.32), (0.05, 0.3, 0.03)))
    single("deploy_workbench", g)


def furnace(name="deploy_furnace", metal=False):
    g = Geo()
    body = METAL_D if metal else STONE
    g.merge(prism(body, 0, 1.05, [(-0.55, -0.55), (0.55, -0.55), (0.55, 0.55), (-0.55, 0.55)], [(-0.42, -0.42), (0.42, -0.42), (0.42, 0.42), (-0.42, 0.42)]))
    g.merge(box(GLOW_FIRE, (0.5, 0.35, 0), (0.06, 0.25, 0.35)))            # жерло
    g.merge(box(BLACK, (0.47, 0.35, 0), (0.05, 0.31, 0.43)))
    g.merge(cyl(METAL_D if not metal else METAL, (0, 1.05, 0), 0.75, 0.13, seg=8))
    single(name, g)


def box_storage(name, large=False):
    g = Geo()
    w, h, d = (1.4, 0.75, 0.75) if large else (0.95, 0.55, 0.55)
    g.merge(box(PLANK, (0, h / 2, 0), (w, h, d)))
    g.merge(box(WOOD_D, (0, h + 0.03, 0), (w + 0.04, 0.06, d + 0.04)))
    for x in (-w / 2 + 0.05, w / 2 - 0.05):
        g.merge(box(METAL_D, (x, h / 2, 0), (0.05, h + 0.02, d + 0.02)))
    g.merge(box(METAL, (0, h * 0.75, d / 2 + 0.01), (0.12, 0.1, 0.03)))
    single(name, g)


def cupboard():
    g = Geo()
    g.merge(box(PLANK, (0, 0.9, 0), (0.6, 1.8, 0.5)))
    g.merge(box(WOOD_D, (0.31, 0.9, 0), (0.02, 1.7, 0.46)))
    g.merge(box(METAL, (0.33, 1.0, 0.15), (0.03, 0.12, 0.03)))
    g.merge(box(RED_M, (0.33, 1.6, 0), (0.02, 0.18, 0.3)))
    single("deploy_claim", g)


def sleepbag():
    g = Geo()
    g.merge(box(CLOTH_R, (0, 0.06, 0), (1.9, 0.12, 0.75)))
    g.merge(cyl(M("cloth_r2", "#6e241e", 0.95), (-0.75, 0.06, -0.38), 0.75, 0.14, R=rot_x(math.pi / 2), seg=8))
    single("deploy_sleepbag", g)


def campfire():
    rng = np.random.default_rng(7)
    g = Geo()
    for i in range(9):
        a = math.tau * i / 9
        g.merge(blob(STONE_D, (math.cos(a) * 0.55, 0.08, math.sin(a) * 0.55), 0.14, sub=0, jitter=0.2, rng=rng))
    for i in range(4):
        a = math.tau * i / 4 + 0.4
        g.merge(cyl_between(WOOD_D, (math.cos(a) * 0.4, 0.05, math.sin(a) * 0.4), (0, 0.4, 0), 0.05, 5))
    single("deploy_campfire", g)


def recycler():
    g = Geo()
    g.merge(box(GREEN_M, (0, 0.75, 0), (1.6, 1.5, 1.2)))
    g.merge(prism(M("funnel", "#5a7a5e", 0.6, 0.4), 1.5, 1.95, [(-0.4, -0.35), (0.4, -0.35), (0.4, 0.35), (-0.4, 0.35)], [(-0.6, -0.5), (0.6, -0.5), (0.6, 0.5), (-0.6, 0.5)]))
    g.merge(box(YELLOW_M, (0.81, 0.6, 0), (0.04, 0.4, 0.8)))
    g.merge(box(BLACK, (0.81, 1.15, 0.35), (0.04, 0.2, 0.2)))
    single("deploy_recycler", g)


def lathe():
    g = Geo()
    g.merge(box(BLUE_M, (0, 0.45, 0), (1.6, 0.9, 0.6)))
    g.merge(box(METAL, (0, 1.0, 0), (1.5, 0.18, 0.4)))
    g.merge(cyl(STEEL, (-0.6, 1.15, 0), 0.25, 0.12, R=rot_z(-math.pi / 2), seg=10))
    g.merge(cyl(STEEL, (-0.3, 1.15, 0), 0.8, 0.03, R=rot_z(-math.pi / 2), seg=6))
    single("deploy_lathe", g)


def generator():
    g = Geo()
    g.merge(box(METAL_D, (0, 0.1, 0), (1.2, 0.2, 0.7)))
    g.merge(box(YELLOW_M, (0, 0.55, 0), (1.0, 0.7, 0.6)))
    g.merge(cyl(METAL, (0.3, 0.9, 0), 0.2, 0.08, seg=8))
    g.merge(cyl_between(METAL_D, (-0.6, 0.3, -0.35), (-0.6, 0.9, -0.35), 0.03, 5))
    g.merge(cyl_between(METAL_D, (-0.6, 0.9, -0.35), (0.6, 0.9, -0.35), 0.03, 5))
    single("deploy_generator", g)


def lamp(name="deploy_lamp", flood=False):
    g = Geo()
    h = 2.2 if flood else 1.6
    g.merge(box(METAL_D, (0, 0.04, 0), (0.4, 0.08, 0.4)))
    g.merge(cyl(METAL, (0, 0, 0), h, 0.04, seg=6))
    if flood:
        g.merge(box(METAL_D, (0.1, h, 0), (0.15, 0.35, 0.55)))
        g.merge(box(GLOW_LAMP, (0.18, h, 0), (0.02, 0.3, 0.5)))
    else:
        g.merge(cyl(GLOW_LAMP, (0, h, 0), 0.25, 0.12, 0.09, seg=8))
        g.merge(cyl(METAL_D, (0, h + 0.25, 0), 0.08, 0.18, 0.04, seg=8))
    single(name, g)


def barrel_rain():
    g = Geo()
    g.merge(cyl(PLANK, (0, 0, 0), 1.05, 0.42, 0.46, seg=12))
    for y in (0.15, 0.5, 0.9):
        g.merge(cyl(METAL_D, (0, y, 0), 0.05, 0.445, seg=12))
    g.merge(cyl(M("water", "#3e6a85", 0.1, 0.1), (0, 0.95, 0), 0.02, 0.42, seg=12))
    single("deploy_barrel", g)


def drill():
    g = Geo()
    g.merge(box(METAL_D, (0, 0.15, 0), (1.8, 0.3, 1.8)))
    for x, z in ((-0.7, -0.7), (0.7, -0.7), (0.7, 0.7), (-0.7, 0.7)):
        g.merge(cyl_between(YELLOW_M, (x, 0.3, z), (0, 2.8, 0), 0.06, 5))
    g.merge(cyl(STEEL, (0, 0.3, 0), 2.4, 0.08, seg=8))
    g.merge(box(YELLOW_M, (0.6, 0.6, 0), (0.6, 0.6, 0.6)))
    single("deploy_drill", g)


def station(name, color, h=1.2, extra=None):
    g = Geo()
    g.merge(box(color, (0, h / 2, 0), (1.4, h, 0.8)))
    g.merge(box(METAL_D, (0, h + 0.03, 0), (1.45, 0.06, 0.85)))
    if extra:
        extra(g, h)
    single(name, g)


workbench()
furnace()
furnace("deploy_efurnace", metal=True)
box_storage("deploy_box")
box_storage("deploy_boxlarge", large=True)
cupboard()
sleepbag()
campfire()
recycler()
lathe()
generator()
lamp()
lamp("deploy_floodlight", flood=True)
barrel_rain()
drill()
station("deploy_still", M("copper", "#a8653a", 0.4, 0.7), 1.0,
        lambda g, h: (g.merge(cyl(M("copper", "#a8653a", 0.4, 0.7), (0, h, 0), 0.7, 0.28, 0.1, seg=10)),
                      g.merge(cyl_between(M("copper", "#a8653a", 0.4, 0.7), (0, h + 0.65, 0), (0.6, h + 0.3, 0.3), 0.03, 5))))
station("deploy_sewing", WOOD, 0.8, lambda g, h: g.merge(box(BLACK, (0, h + 0.15, 0), (0.4, 0.25, 0.15))))
station("deploy_chem", WHITE_M, 0.95, lambda g, h: [g.merge(cyl(M("glass", "#8fc3c8", 0.1, 0.0, alpha=0.6), (x, h + 0.06, 0), 0.25, 0.06, seg=8)) for x in (-0.4, 0, 0.4)])
station("deploy_guntable", METAL_D, 0.95, lambda g, h: g.merge(box(WOOD, (0, h + 0.08, 0), (0.9, 0.1, 0.12))))
station("deploy_medtable", WHITE_M, 0.9, lambda g, h: g.merge(box(RED_M, (0, h + 0.07, 0), (0.25, 0.08, 0.25))))
station("deploy_rectifier", BLUE_M, 1.6, lambda g, h: g.merge(box(GLOW_LAMP, (0.71, h * 0.7, 0), (0.02, 0.1, 0.3))))
station("deploy_conveyor", METAL, 0.6, lambda g, h: g.merge(box(BLACK, (0, h + 0.02, 0), (1.4, 0.04, 0.6))))


# ================================================================ ящики лута

def crate(name, body, band, mark=None, lid_open=False):
    g = Geo()
    g.merge(box(body, (0, 0.28, 0), (0.95, 0.56, 0.6)))
    g.merge(box(band, (0, 0.28, 0), (0.08, 0.58, 0.62)))
    g.merge(box(band, (-0.38, 0.28, 0), (0.06, 0.58, 0.62)))
    g.merge(box(band, (0.38, 0.28, 0), (0.06, 0.58, 0.62)))
    if mark is not None:
        g.merge(box(mark, (0.0, 0.4, 0.305), (0.2, 0.14, 0.01)))
        g.merge(box(mark, (0.0, 0.4, 0.305), (0.06, 0.24, 0.011)))
    if not lid_open:
        g.merge(box(band, (0, 0.575, 0), (0.97, 0.03, 0.62)))
    single(name, g)


crate("loot_crate_wood", PLANK, WOOD_D)
crate("loot_crate_army", OLIVE, M("olive_d", "#3f4730", 0.9))
crate("loot_crate_elite", BLUE_M, YELLOW_M)
crate("loot_crate_medical", WHITE_M, M("grey_l", "#b8b8b0", 0.7), mark=RED_M)
crate("loot_crate_safe", METAL_D, STEEL)
g = Geo()
g.merge(blob(M("sack", "#5e4a34", 0.95), (0, 0.2, 0), (0.45, 0.22, 0.32), sub=1, jitter=0.15, rng=np.random.default_rng(3), flat_bottom=0))
g.merge(cyl(ROPE, (0.35, 0.18, 0), 0.08, 0.06, seg=6))
single("loot_bag", g)


if __name__ == "__main__":
    print(f"готово: {len(made)} моделей в {os.path.normpath(OUT)}")
