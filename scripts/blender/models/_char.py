"""Персонажи (char_*): фигура из частей на суставах hips/torso/head/arm_l/arm_r/leg_l/leg_r — их двигает
Actor3D (game/src/Client3D/Ents3D.cs), там же надеваются «оболочки» одежды поверх. Поза покоя — руки вниз.
Позиции суставов — как в заглушке Ents3D.Stub (в Blender: X — вперёд, Y = −Z Godot, Z — вверх):
hips (0,0,0.88) → torso +0.06 → head +0.62; arm_l/arm_r (0,±0.25,1.44) от torso; leg_l/leg_r (0,±0.11,0.84) от hips.
Левая сторона Godot (z −0.25) — в Blender y = +0.25."""
import math


def limb(k, top, bottom, r0, r1, mat, seg=10):
    """Сегмент конечности (сужающийся цилиндр) от top вниз к bottom + «сустав»-сфера сверху."""
    h = top[2] - bottom[2]
    ob = k.cyl(r0, h, bottom, mat, segments=seg, bevel=0, radius_top=r0)
    for v in ob.data.vertices:                          # сужение книзу
        t = v.co.z / h
        f = (r1 + (r0 - r1) * t) / r0
        v.co.x *= f
        v.co.y *= f
    k.smooth_all(ob)
    j = k.blob(r0 * 1.02, top, mat, subdiv=2, jitter=0.0, smooth=True)
    return [ob, j]


def figure(k, top="skin", sleeves=None, legs="under", boots="skin", hands="skin", head_extra=None, body_extra=None):
    """Тело с одеждой материалами. sleeves — материал рукавов (None = как top)."""
    sleeves = sleeves or top
    hips = k.joint("hips", (0, 0, 0.88))
    torso = k.joint("torso", (0, 0, 0.94), hips)
    head = k.joint("head", (0, 0, 1.56), torso)
    parts = {"hips": [], "torso": [], "head": [], "arm_l": [], "arm_r": [], "leg_l": [], "leg_r": []}

    # таз и торс: скруглённые блоки, грудь шире талии
    parts["hips"].append(k.box((0.22, 0.34, 0.18), (0, 0, 0.8), legs, bevel=0.06))
    parts["torso"].append(k.box((0.2, 0.32, 0.2), (0, 0, 0.94), top, bevel=0.07))          # живот
    parts["torso"].append(k.box((0.24, 0.42, 0.32), (0, 0, 1.12), top, bevel=0.09))        # грудь и плечи
    parts["torso"].append(k.cyl(0.05, 0.12, (0, 0, 1.44), "skin", segments=10, bevel=0))    # шея
    for o in parts["torso"]:
        k.smooth_all(o)
    # голова: череп, челюсть, нос, уши, волосы
    hd = k.blob(0.115, (0.0, 0, 1.69), "skin", subdiv=3, scale=(1.0, 0.86, 1.08), jitter=0.0, smooth=True)
    jaw = k.blob(0.08, (0.03, 0, 1.6), "skin", subdiv=2, scale=(1.0, 1.0, 0.8), jitter=0.0, smooth=True)
    nose = k.box((0.03, 0.025, 0.045), (0.115, 0, 1.64), "skin", bevel=0.01)
    ears = [k.blob(0.025, (0.0, s * 0.1, 1.68), "skin", subdiv=1, scale=(1, 0.5, 1.3), jitter=0.0, smooth=True) for s in (-1, 1)]
    hair = k.blob(0.118, (-0.012, 0, 1.72), "hair", subdiv=3, scale=(1.0, 0.88, 0.95), jitter=0.0, smooth=True)
    for v in hair.data.vertices:                                     # срезать спереди и снизу — короткая стрижка
        v.co.z = max(v.co.z, 0.0)
        if v.co.x > 0.06:
            v.co.x = 0.06 + (v.co.x - 0.06) * 0.2
    eyes = [k.box((0.012, 0.022, 0.012), (0.108, s * 0.04, 1.68), "hair", bevel=0.004) for s in (-1, 1)]
    parts["head"] += [hd, jaw, nose, hair] + ears + eyes
    if head_extra:
        parts["head"] += head_extra(k)
    # руки
    for side, y in (("l", 0.25), ("r", -0.25)):
        a = parts["arm_" + side]
        a += limb(k, (0, y, 1.42), (0, y, 1.15), 0.058, 0.05, sleeves)
        a += limb(k, (0, y, 1.15), (0, y, 0.93), 0.05, 0.04, sleeves)
        a.append(k.box((0.075, 0.05, 0.1), (0.0, y, 0.83), hands, bevel=0.02))                  # кисть
        a.append(k.box((0.03, 0.03, 0.06), (0.04, y - (0.03 if y > 0 else -0.03) * 0, 0.87), hands, bevel=0.01))
    # ноги
    for side, y in (("l", 0.11), ("r", -0.11)):
        g = parts["leg_" + side]
        g += limb(k, (0, y, 0.84), (0, y, 0.47), 0.085, 0.062, legs)
        g += limb(k, (0, y, 0.47), (0, y, 0.1), 0.062, 0.048, legs)
        g.append(k.box((0.26, 0.11, 0.1), (0.05, y, 0.0), boots, bevel=0.03))                   # стопа/ботинок
        if boots != "skin":
            g += limb(k, (0, y, 0.3), (0, y, 0.08), 0.066, 0.06, boots)                         # голенище
    if body_extra:
        for jn, objs in body_extra(k).items():
            parts[jn] += objs
    joints = {"hips": hips, "torso": torso, "head": head}
    joints["arm_l"] = k.joint("arm_l", (0, 0.25, 1.44), torso)
    joints["arm_r"] = k.joint("arm_r", (0, -0.25, 1.44), torso)
    joints["leg_l"] = k.joint("leg_l", (0, 0.11, 0.84), hips)
    joints["leg_r"] = k.joint("leg_r", (0, -0.11, 0.84), hips)
    for jn, objs in parts.items():
        k.attach(objs, joints[jn])


def stripes(k, mat="white"):
    """Две белые лампасы по рукавам и штанинам — спортивный костюм."""
    out = {"arm_l": [], "arm_r": [], "leg_l": [], "leg_r": []}
    for side, y in (("l", 0.25), ("r", -0.25)):
        for dy in (-0.012, 0.012):
            out["arm_" + side].append(k.box((0.006, 0.008, 0.5), (0, y + (0.058 if y > 0 else -0.058) + dy * 0, 0.93), mat, bevel=0))
    for side, y in (("l", 0.11), ("r", -0.11)):
        out["leg_" + side].append(k.box((0.01, 0.008, 0.72), (0, y + (0.08 if y > 0 else -0.08), 0.12), mat, bevel=0))
    return out
