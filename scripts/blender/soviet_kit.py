"""
Набор для своих 3D-моделей в стиле «советский постапок» (docs/design/28-3d-style.md).
Запускается внутри Blender (bpy). Модели — scripts/blender/models/<имя>.py, сборка — build.py.

Координаты Blender: Z вверх, X — «перед». glTF-экспорт сам переводит в Y вверх (+X остаётся +X).
1 ед. = 1 м, опора модели — на земле (z = 0) в центре.
"""
import math
import os
import shutil

import bmesh
import bpy
import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
TEX = os.path.join(ROOT, "tools", "textures")
TINTED = os.path.join(TEX, "_tinted")

# материал: текстура Poly Haven, оттенок (умножение на цвет — под палитру), метр на плитку, металличность
MATS = {
    "paint_green": ("green_metal_rust", (0.95, 1.00, 0.85), 1.2, 0.0),      # защитный зелёный с ржавчиной
    "paint_blue":  ("blue_metal_plate", (0.92, 0.97, 1.00), 1.5, 0.0),     # выцветший голубой (станки, шкафы)
    "rust":        ("rusty_metal_02", (0.62, 0.52, 0.46), 1.0, 0.25),       # приглушённая, не рыжая
    "rust_dark":   ("rust_coarse_01", (0.55, 0.50, 0.48), 0.8, 0.3),         # печной чёрный металл с ржавчиной
    "steel":       ("metal_plate", (0.80, 0.80, 0.78), 1.0, 0.6),

    "planks":      ("weathered_planks", (0.95, 0.92, 0.88), 1.6, 0.0),
    "planks_green":("green_rough_planks", (0.68, 0.78, 0.58), 1.2, 0.0),    # армейский ящик, в хаки
    "plywood":     ("plywood", (0.85, 0.80, 0.72), 1.2, 0.0),
    "concrete":    ("concrete", (0.95, 0.95, 0.92), 2.0, 0.0),
    "tin":         ("worn_corrugated_iron", (0.90, 0.90, 0.88), 1.5, 0.2),
}
# акцент — краска одного цвета (не больше одного на модель): цвет, шероховатость, металл, износ 0..1
# износ: пятна грязи и сколы до ржавчины (маска — свой бесшовный шум, см. _worn_image)
FLAT = {
    "red": ((0.30, 0.04, 0.03), 0.6, 0.0, 0.55),
    "white": ((0.40, 0.40, 0.36), 0.7, 0.0, 0.9),    # светлая краска (медицина, трафареты), грязная
    "black": ((0.04, 0.04, 0.04), 0.7, 0.2, 0.25),
    "yellow": ((0.55, 0.40, 0.06), 0.6, 0.0, 0.75),
    "clean_black": ((0.03, 0.03, 0.03), 0.5, 0.3, 0.0),   # воронёная сталь, пластик — без износа
    "rubber": ((0.025, 0.025, 0.025), 0.9, 0.0, 0.0),
    "wood_dark": ((0.16, 0.08, 0.035), 0.55, 0.0, 0.15),  # лакированное дерево (приклады)
    "glass": ((0.25, 0.32, 0.30), 0.1, 0.0, 0.0),
    "brass": ((0.55, 0.38, 0.12), 0.35, 1.0, 0.2),
    "copper": ((0.50, 0.20, 0.09), 0.4, 1.0, 0.35),      # самогонный куб, трубки (тёмная медь с патиной)
    "leaf": ((0.10, 0.17, 0.06), 0.85, 0.0, 0.0),
    "leaf_dry": ((0.22, 0.16, 0.08), 0.9, 0.0, 0.0),
    "leaf_dark": ((0.05, 0.10, 0.05), 0.85, 0.0, 0.0),        # хвоя
    "hemp": ((0.12, 0.22, 0.05), 0.8, 0.0, 0.0),
    "berry": ((0.06, 0.08, 0.30), 0.3, 0.0, 0.0),
    "bark": ((0.13, 0.10, 0.08), 0.95, 0.0, 0.0),
    "birch": ((0.55, 0.54, 0.50), 0.9, 0.0, 0.0),
    "bark_pine": ((0.26, 0.12, 0.06), 0.9, 0.0, 0.0),     # рыжеватая кора сосны
    "bark_dark": ((0.03, 0.03, 0.03), 0.9, 0.0, 0.0),     # чечевички берёзы
    "bark_dead": ((0.24, 0.21, 0.18), 0.95, 0.0, 0.0),    # выбеленная сухостоем
    "needle": ((0.04, 0.10, 0.05), 0.85, 0.0, 0.0),
    "needle_dark": ((0.025, 0.06, 0.035), 0.85, 0.0, 0.0),
    "leaf_light": ((0.20, 0.30, 0.07), 0.8, 0.0, 0.0),    # берёза, осина, тополь
    "leaf_willow": ((0.16, 0.22, 0.11), 0.85, 0.0, 0.0),
    "snow": ((0.80, 0.82, 0.85), 0.6, 0.0, 0.0),
    "apple": ((0.40, 0.05, 0.03), 0.4, 0.0, 0.0),
    # оружие и инструмент
    "gunmetal": ((0.035, 0.035, 0.04), 0.45, 0.7, 0.3),  # воронёная сталь, потёртая до ржавчины
    "wood_stock": ((0.11, 0.04, 0.016), 0.5, 0.0, 0.2),   # лакированная берёза/орех приклада (АК, ППШ)
    "wood_light": ((0.22, 0.10, 0.035), 0.6, 0.0, 0.2),    # рыжий ламинат АКМ, лук
    "polymer": ((0.07, 0.035, 0.035), 0.6, 0.0, 0.1),     # «сливовый» пластик АК-74, чёрный полимер
    "tape": ((0.03, 0.10, 0.28), 0.5, 0.0, 0.15),          # синяя изолента — акцент самоделок
    "leather": ((0.12, 0.06, 0.03), 0.8, 0.0, 0.2),
    "bone": ((0.55, 0.50, 0.40), 0.6, 0.0, 0.1),
    "blueprint": ((0.04, 0.12, 0.35), 0.8, 0.0, 0.1),
    # техника РТ: кузов светлый — клиент умножает на цвет спрайта (Decor3D), стёкла тёмные
    "car_paint": ((0.55, 0.55, 0.53), 0.55, 0.0, 0.6),
    "glass_dark": ((0.02, 0.025, 0.03), 0.08, 0.0, 0.0),
    # персонажи
    "skin": ((0.50, 0.32, 0.22), 0.6, 0.0, 0.0),
    "hair": ((0.05, 0.035, 0.025), 0.8, 0.0, 0.0),
    "under": ((0.20, 0.20, 0.19), 0.9, 0.0, 0.0),          # серое бельё
    "jacket": ((0.13, 0.13, 0.08), 0.95, 0.0, 0.5),         # ватник хаки
    "pants": ((0.06, 0.06, 0.065), 0.95, 0.0, 0.4),
    "track": ((0.03, 0.06, 0.20), 0.6, 0.0, 0.4),            # синий спортивный костюм
    "uniform": ((0.10, 0.12, 0.06), 0.9, 0.0, 0.4),          # армейская полевая форма
    "rowan_berry": ((0.55, 0.06, 0.02), 0.35, 0.0, 0.0),
    "cloth": ((0.17, 0.18, 0.12), 0.95, 0.0, 0.3),        # брезент, ткань хаки
    "cloth_grey": ((0.30, 0.29, 0.26), 0.95, 0.0, 0.3),
    "glow": ((1.0, 0.35, 0.05), 0.5, 0.0, 0.0),            # раскалённое (светится: emission)
}
SOFT = {"cloth", "cloth_grey", "wood_stock", "wood_light", "leather", "bone", "blueprint", "tape",
        "track", "uniform", "jacket", "pants"}     # износ без ржавчины (см. _worn_image)
FONT = r"C:\Windows\Fonts\bahnschrift.ttf"   # DIN-подобный, есть кириллица; жирность — смещением контура

_tile = {}   # имя материала → метр на плитку (для развёртки)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    _tile.clear()


def _tinted_image(tex_id, tint):
    """Цвет текстуры, умноженный на оттенок, — отдельным jpg (glTF не переносит узлы смешивания)."""
    os.makedirs(TINTED, exist_ok=True)
    out = os.path.join(TINTED, f"{tex_id}_{'_'.join(f'{c:.2f}' for c in tint)}.jpg")
    if not os.path.exists(out):
        img = bpy.data.images.load(os.path.join(TEX, tex_id, "diff.jpg"))
        px = np.array(img.pixels[:], dtype=np.float32).reshape(-1, 4)
        px[:, :3] *= np.array(tint, dtype=np.float32)
        img.pixels[:] = np.clip(px, 0, 1).ravel()
        img.filepath_raw = out
        img.file_format = "JPEG"
        img.save()
        bpy.data.images.remove(img)
    return _img(out)


def _img(path):
    """Картинка с уникальным именем (green_metal_rust_rough, worn_red_0.55_v1): экспорт кладёт её в общую
    папку own/tex под этим именем — одна копия на все модели, без затирания разных rough.jpg друг другом."""
    stem = os.path.splitext(os.path.basename(path))[0]
    if stem in ("diff", "rough", "nor"):                 # экспорт называет файл по имени исходника — копия с уникальным
        uniq = os.path.join(TINTED, f"{os.path.basename(os.path.dirname(path))}_{stem}.jpg")
        if not os.path.exists(uniq):
            os.makedirs(TINTED, exist_ok=True)
            shutil.copyfile(path, uniq)
        path, stem = uniq, os.path.splitext(os.path.basename(uniq))[0]
    img = bpy.data.images.load(path, check_existing=True)
    img.name = stem
    return img


def _fbm(n, beta, seed, low=0):
    """Бесшовный фрактальный шум (спектральный синтез: период = размер картинки)."""
    rng = np.random.default_rng(seed)
    f = np.fft.fft2(rng.standard_normal((n, n)))
    k = np.fft.fftfreq(n) * n
    kk = np.sqrt(k[:, None] ** 2 + k[None, :] ** 2)
    kk[0, 0] = 1
    amp = 1.0 / kk ** beta
    amp[kk < low] = 0           # без самых крупных пятен — сколы равномерно по поверхности
    x = np.real(np.fft.ifft2(f * amp))
    return (x - x.mean()) / x.std()


def _worn_image(name, col, wear, n=1024):
    """Краска с износом: цвет × грязь, сколы до ржавчины по краям пятен, потёки потемнее."""
    os.makedirs(TINTED, exist_ok=True)
    soft = name in SOFT
    out = os.path.join(TINTED, f"worn_{name}_{wear:.2f}_{'soft' if soft else 'v1'}.jpg")
    if not os.path.exists(out):
        chips = _fbm(n, 1.15, 11, low=3) * 0.75 + _fbm(n, 0.7, 12) * 0.45
        dirt = _fbm(n, 1.4, 13)
        thr = np.percentile(chips, 100 - 12 * wear)            # доля сколов ≈ 12 % при полном износе
        m = np.clip((chips - thr) * 6, 0, 1)
        rim = np.clip(1 - np.abs(chips - thr + 0.12) * 5, 0, 1) * wear * 0.35   # тёмный край у скола
        img = bpy.data.images.load(os.path.join(TEX, "rusty_metal_02", "diff.jpg"))
        rust = np.array(img.pixels[:], dtype=np.float32).reshape(n, n, 4)[:, :, :3] * 0.8
        bpy.data.images.remove(img)
        base = np.array(col, dtype=np.float32) ** (1 / 2.2)                    # цвет задан линейным
        shade = 1 - wear * 0.22 * np.clip(dirt * 0.5 + 0.5, 0, 1) - rim        # грязь пятнами
        px = base[None, None, :] * shade[:, :, None]
        if soft:                                   # ткань, дерево, кожа: не ржавеют — затёртые грязные пятна
            rust = base[None, None, :] * 0.55 * (0.85 + 0.15 * np.clip(dirt[:, :, None], -1, 1))
            m = m * 0.6
        px = px * (1 - m[:, :, None]) + rust * m[:, :, None]
        res = bpy.data.images.new("worn", n, n)
        res.pixels[:] = np.dstack([np.clip(px, 0, 1), np.ones((n, n, 1), np.float32)]).ravel()
        res.filepath_raw = out
        res.file_format = "JPEG"
        res.save()
        bpy.data.images.remove(res)
    return _img(out)


def mat(name):
    """Материал из библиотеки (создаётся один раз на сцену)."""
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    if name in FLAT:
        col, rough, metal, wear = FLAT[name]
        bsdf.inputs["Roughness"].default_value = rough
        bsdf.inputs["Metallic"].default_value = metal
        _tile[name] = 1.2
        if wear <= 0:
            bsdf.inputs["Base Color"].default_value = (*col, 1)
            if name == "glow":
                bsdf.inputs["Emission Color"].default_value = (*col, 1)
                bsdf.inputs["Emission Strength"].default_value = 4.0
            return m
        diff = nt.nodes.new("ShaderNodeTexImage")
        diff.image = _worn_image(name, col, wear)
        nt.links.new(diff.outputs["Color"], bsdf.inputs["Base Color"])
        nor = nt.nodes.new("ShaderNodeTexImage")       # рельеф сколов — от ржавого металла
        nor.image = _img(os.path.join(TEX, "rusty_metal_02", "nor.jpg"))
        nor.image.colorspace_settings.name = "Non-Color"
        nmap = nt.nodes.new("ShaderNodeNormalMap")
        nmap.inputs["Strength"].default_value = 0.5
        nt.links.new(nor.outputs["Color"], nmap.inputs["Color"])
        nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
        return m
    tex_id, tint, tile, metal = MATS[name]
    _tile[name] = tile
    diff = nt.nodes.new("ShaderNodeTexImage")
    diff.image = _tinted_image(tex_id, tint)
    nt.links.new(diff.outputs["Color"], bsdf.inputs["Base Color"])
    rough = nt.nodes.new("ShaderNodeTexImage")
    rough.image = _img(os.path.join(TEX, tex_id, "rough.jpg"))
    rough.image.colorspace_settings.name = "Non-Color"
    nt.links.new(rough.outputs["Color"], bsdf.inputs["Roughness"])
    nor = nt.nodes.new("ShaderNodeTexImage")
    nor.image = _img(os.path.join(TEX, tex_id, "nor.jpg"))
    nor.image.colorspace_settings.name = "Non-Color"
    nmap = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(nor.outputs["Color"], nmap.inputs["Color"])
    nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
    bsdf.inputs["Metallic"].default_value = metal
    return m


def _obj_from_bm(bm, name, material):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.data.materials.append(mat(material))
    return ob


def box(size, at=(0, 0, 0), material="steel", rot_z=0.0, bevel=0.012, name="box"):
    """Коробка: size (x, y, z), at — центр НИЗА коробки."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=size, verts=bm.verts)
    bmesh.ops.translate(bm, vec=(0, 0, size[2] / 2), verts=bm.verts)
    ob = _obj_from_bm(bm, name, material)
    ob.rotation_euler.z = rot_z
    ob.location = at
    _finish(ob, bevel)
    return ob


def cyl(radius, height, at=(0, 0, 0), material="steel", segments=16, axis="Z", bevel=0.008, name="cyl", radius_top=None,
        shell=0.0):
    """Цилиндр (или усечённый конус): at — центр нижнего торца; axis — вдоль какой оси стоит.
    shell > 0 — полая труба/воронка без торцов со стенкой такой толщины (бункер, раструб)."""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=shell <= 0, cap_tris=True, segments=segments, radius1=radius,   # крышка веером: n-угольник с фаской даёт чёрные пятна
                          radius2=radius if radius_top is None else radius_top, depth=height)
    bmesh.ops.translate(bm, vec=(0, 0, height / 2), verts=bm.verts)
    ob = _obj_from_bm(bm, name, material)
    if shell > 0:
        sm = ob.modifiers.new("shell", "SOLIDIFY")
        sm.thickness = shell
        sm.offset = 1.0
    if axis == "X":
        ob.rotation_euler.y = math.pi / 2
    elif axis == "Y":
        ob.rotation_euler.x = -math.pi / 2
    ob.location = at
    _finish(ob, bevel)
    return ob


def torus(R, r, at=(0, 0, 0), material="rubber", seg=20, ring=8, axis="Z", tread=0.0, name="torus", rot=(0, 0, 0)):
    """Тор (покрышка, обод): R — радиус до оси трубки, r — радиус трубки; tread — сплющить в шину (0..1)."""
    bm = bmesh.new()
    rows = []
    for i in range(seg):
        a = 2 * math.pi * i / seg
        row = []
        for j in range(ring):
            b = 2 * math.pi * j / ring
            rr, zz = r * math.cos(b), r * math.sin(b)
            if tread:                                  # квадратнее: протектор плоский
                rr = math.copysign(abs(rr) ** (1 - tread * 0.6), rr) * r ** (tread * 0.6)
                zz = math.copysign(abs(zz) ** (1 - tread * 0.6), zz) * r ** (tread * 0.6)
            row.append(bm.verts.new(((R + rr) * math.cos(a), (R + rr) * math.sin(a), zz)))
        rows.append(row)
    for i in range(seg):
        for j in range(ring):
            bm.faces.new((rows[i][j], rows[(i + 1) % seg][j], rows[(i + 1) % seg][(j + 1) % ring], rows[i][(j + 1) % ring]))
    ob = _obj_from_bm(bm, name, material)
    if axis == "X":
        ob.rotation_euler.y = math.pi / 2
    elif axis == "Y":
        ob.rotation_euler.x = math.pi / 2
    else:
        ob.rotation_euler = rot
    ob.location = at
    _finish(ob, 0)
    return ob


def blob(radius, at=(0, 0, 0), material="leaf", subdiv=2, scale=(1, 1, 1), jitter=0.25, seed=0, name="blob", flat=False,
         smooth=False):
    """Комок неправильной формы (крона, куст, камень, куча): икосфера, сдвинутая шумом по нормали."""
    rng = np.random.default_rng(seed)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=radius)
    for v in bm.verts:
        n = v.co.normalized()
        v.co = v.co + n * radius * jitter * float(rng.uniform(-1, 1))
        v.co.x *= scale[0]
        v.co.y *= scale[1]
        v.co.z *= scale[2]
    if flat:                                            # срезать низ — стоит на земле
        for v in bm.verts:
            v.co.z = max(v.co.z, -radius * scale[2] * 0.3)
    ob = _obj_from_bm(bm, name, material)
    ob.location = at
    _finish(ob, 0)
    if smooth:
        smooth_all(ob)
    return ob


def smooth_all(ob):
    """Гладкие нормали целиком (без «острых» рёбер от shade_smooth_by_angle) — для листвы, коры, снега."""
    for p in ob.data.polygons:
        p.use_smooth = True
    for a in ("sharp_edge", "sharp_face"):
        if a in ob.data.attributes:
            ob.data.attributes.remove(ob.data.attributes[a])


def foliage(center, radii, n=24, clump=0.16, mats=("leaf", "leaf_dark"), seed=0, droop=0.0):
    """Крона/куст: много мелких сглаженных комков листвы по поверхности эллипсоида — рваный живой силуэт
    вместо одного шара. radii — полуоси (x, y, z); droop — опустить нижние комки (плакучесть)."""
    rng = np.random.default_rng(seed)
    out = []
    n = max(5, int(n * 0.7))          # комки по 80 граней: меньше, но крупнее — бюджет ~2–3 тыс. на крону
    clump *= 1.18
    for i in range(n):
        u, v = rng.uniform(0, 2 * math.pi), math.acos(rng.uniform(-0.55, 1))
        sx, sy, sz = math.sin(v) * math.cos(u), math.sin(v) * math.sin(u), math.cos(v)
        sh = rng.uniform(0.75, 1.0)
        p = (center[0] + radii[0] * sx * sh, center[1] + radii[1] * sy * sh, center[2] + radii[2] * sz * sh - droop * max(0, -sz))
        r = clump * rng.uniform(0.7, 1.3)
        out.append(blob(r, p, mats[i % len(mats)], subdiv=2, scale=(1, 1, 0.8), jitter=0.16, seed=int(seed * 100 + i), smooth=True))
    out.append(blob(min(radii) * 0.85, center, mats[-1], subdiv=2, jitter=0.15, seed=seed, smooth=True))   # сердцевина без просветов
    return out


_FACING = {   # куда смотрит надпись: (вправо, вверх, нормаль) в координатах Blender
    "-Y": ((1, 0, 0), (0, 0, 1), (0, -1, 0)),    # перед
    "+Y": ((-1, 0, 0), (0, 0, 1), (0, 1, 0)),
    "+X": ((0, 1, 0), (0, 0, 1), (1, 0, 0)),
    "-X": ((0, -1, 0), (0, 0, 1), (-1, 0, 0)),
    "+Z": ((1, 0, 0), (0, 1, 0), (0, 0, 1)),     # крышка, читается с юга
}


def stencil(text, at, size=0.06, facing="-Y", material="white", wrap=None, bold=0.06, spacing=1.08,
            lift=0.0025, rot=0.0, name="stencil"):
    """Трафаретная надпись тонкой плоской сеткой поверх поверхности (без текстуры — чёткая на любом расстоянии).
    at — центр надписи на поверхности; size — высота заглавной; wrap — радиус цилиндра (обернуть вокруг
    вертикальной оси, at — точка на боку). Износ — от материала (сколы совпадают с краской под ней)."""
    cu = bpy.data.curves.new(name, "FONT")
    cu.body = text
    cu.font = bpy.data.fonts.load(FONT, check_existing=True)
    cu.size = size * 1.38            # size шрифта ≈ кегль; заглавная ≈ 0,72 кегля
    cu.align_x, cu.align_y = "CENTER", "CENTER"
    cu.offset = bold * size
    cu.space_character = spacing
    cu.resolution_u = 2
    tmp = bpy.data.objects.new(name, cu)
    bpy.context.scene.collection.objects.link(tmp)
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(tmp.evaluated_get(dg))
    bpy.data.objects.remove(tmp)
    bm = bmesh.new()
    bm.from_mesh(me)
    bpy.data.meshes.remove(me)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    r_, u_, n_ = (np.array(v, dtype=float) for v in _FACING[facing])
    c, s = math.cos(rot), math.sin(rot)
    r_, u_ = r_ * c + u_ * s, u_ * c - r_ * s
    base = np.array(at, dtype=float) + n_ * lift
    for v in bm.verts:
        x, y = v.co.x, v.co.y
        z = 0.0
        if wrap:                      # на бок цилиндра: дуга вместо прямой
            a = x / wrap
            x, z = (wrap + lift) * math.sin(a), (wrap + lift) * math.cos(a) - (wrap + lift)
        v.co = tuple(base + r_ * x + u_ * y + n_ * z)
    for f in bm.faces:
        f.smooth = False
    return _obj_from_bm(bm, name, material)


def _finish(ob, bevel):
    """Фаска по рёбрам (главная «дорогая» деталь), мягкие нормали по углу, применить всё."""
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    if bevel > 0:
        b = ob.modifiers.new("bevel", "BEVEL")
        b.width = bevel
        b.segments = 2
        b.limit_method = "ANGLE"
        b.angle_limit = math.radians(40)
        b.harden_normals = True
    with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob], selected_editable_objects=[ob]):
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        for md in list(ob.modifiers):
            bpy.ops.object.modifier_apply(modifier=md.name)
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
    ob.select_set(False)


def uv_world(objects):
    """Развёртка «кубом» в мировых метрах: у всех моделей одинаковая плотность текстуры."""
    for ob in objects:
        tile = _tile.get(ob.data.materials[0].name, 1.0) if ob.data.materials else 1.0
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        uv = bm.loops.layers.uv.verify()
        mw = ob.matrix_world
        rot = mw.to_3x3()
        for f in bm.faces:
            n = rot @ f.normal
            ax = max(range(3), key=lambda i: abs(n[i]))
            for loop in f.loops:
                p = mw @ loop.vert.co
                u, v = {0: (p.y, p.z), 1: (p.x, p.z), 2: (p.x, p.y)}[ax]
                loop[uv].uv = (u / tile, v / tile)
        bm.to_mesh(ob.data)
        bm.free()


def joint(name, loc, parent=None):
    """Сустав персонажа — пустой узел с именем (hips, torso, head, arm_l…); в glTF станет Node3D с этим именем."""
    e = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(e)
    e.location = loc
    if parent is not None:
        e.parent = parent
        e.location = tuple(loc[i] - parent.matrix_world.translation[i] for i in range(3))
    bpy.context.view_layer.update()
    return e


def attach(objs, j):
    """Прицепить готовые (в мировых координатах позы покоя) детали к суставу, не сдвигая их."""
    bpy.context.view_layer.update()
    inv = j.matrix_world.inverted()
    for o in objs:
        mw = o.matrix_world.copy()
        o.parent = j
        o.matrix_parent_inverse.identity()
        o.matrix_world = mw
        o.matrix_basis = inv @ mw


def tris(objects):
    return sum(sum(len(p.vertices) - 2 for p in ob.data.polygons) for ob in objects)


def export(name, out_dir):
    objs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    uv_world(objs)
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, name + ".gltf")   # текстуры — общие в own/tex (одна копия на всю игру)
    for o in bpy.context.scene.objects:
        o.select_set(o.type in ("MESH", "EMPTY"))         # пустые — суставы персонажей
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLTF_SEPARATE", export_texture_dir=os.path.relpath(os.path.join(ROOT, "game", "art3d", "own", "tex"), out_dir).replace(os.sep, "/"), use_selection=True, export_apply=True,
                              export_yup=True, export_image_format="JPEG", export_jpeg_quality=85)
    return path, tris(objs)


def render_preview(path_png, size=(720, 540), side=False):
    """Превью под углом камеры игры (наклон 58°, почти изометрия), Cycles на процессоре."""
    sc = bpy.context.scene
    if "car_paint" in bpy.data.materials:                     # как в игре: кузов умножается на цвет спрайта
        nt = bpy.data.materials["car_paint"].node_tree
        tex = next(n for n in nt.nodes if n.type == "TEX_IMAGE" and n.image and "car_paint" in n.image.name)
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type, mix.blend_type = "RGBA", "MULTIPLY"
        mix.inputs["Factor"].default_value = 1.0
        mix.inputs[7].default_value = (0.75, 0.22, 0.12, 1)
        nt.links.new(tex.outputs["Color"], mix.inputs[6])
        nt.links.new(mix.outputs[2], nt.nodes["Principled BSDF"].inputs["Base Color"])
    objs = [o for o in sc.objects if o.type == "MESH"]
    lo = np.min([np.min([o.matrix_world @ v.co for v in o.data.vertices], axis=0) for o in objs], axis=0)
    hi = np.max([np.max([o.matrix_world @ v.co for v in o.data.vertices], axis=0) for o in objs], axis=0)
    c = (lo + hi) / 2
    ext = float(np.max(hi - lo))
    # пол
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=ext * 3)
    floor = _obj_from_bm(bm, "floor", "concrete")
    if side:                                                   # предмет в руке: сбоку, пол под нижней точкой
        floor.location.z = float(lo[2]) - 0.002
    uv_world([floor])
    # камера: с юго-востока, наклон 58°
    cam_d = bpy.data.cameras.new("cam")
    cam_d.type = "ORTHO"
    cam_d.ortho_scale = ext * 1.6
    cam = bpy.data.objects.new("cam", cam_d)
    sc.collection.objects.link(cam)
    tilt, yaw = (math.radians(12), math.radians(-90)) if side else (math.radians(58), math.radians(-35))
    d = np.array([math.cos(tilt) * math.cos(yaw), math.cos(tilt) * math.sin(yaw), math.sin(tilt)])
    cam.location = tuple(c + d * ext * 4)
    cam.rotation_euler = (math.pi / 2 - tilt, 0, yaw + math.pi / 2)
    sc.camera = cam
    sun_d = bpy.data.lights.new("sun", "SUN")
    sun_d.energy = 3.2
    sun_d.angle = math.radians(8)
    sun = bpy.data.objects.new("sun", sun_d)
    sun.rotation_euler = (math.radians(45), math.radians(-20), math.radians(30))
    sc.collection.objects.link(sun)
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.42, 0.45, 0.48, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.8
    sc.world = world
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 48
    sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = size
    sc.render.filepath = path_png
    sc.view_settings.view_transform = "AgX"
    bpy.ops.render.render(write_still=True)
