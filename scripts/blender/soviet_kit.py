"""
Набор для своих 3D-моделей в стиле «советский постапок» (docs/design/28-3d-style.md).
Запускается внутри Blender (bpy). Модели — scripts/blender/models/<имя>.py, сборка — build.py.

Координаты Blender: Z вверх, X — «перед». glTF-экспорт сам переводит в Y вверх (+X остаётся +X).
1 ед. = 1 м, опора модели — на земле (z = 0) в центре.
"""
import math
import os

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
# акцент — гладкая краска без текстуры (не больше одного на модель)
FLAT = {
    "red": ((0.30, 0.04, 0.03), 0.6, 0.0),
    "white": ((0.40, 0.40, 0.36), 0.7, 0.0),    # светлая краска (медицина), грязная
    "black": ((0.04, 0.04, 0.04), 0.7, 0.2),
    "yellow": ((0.55, 0.40, 0.06), 0.6, 0.0),
}

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
    return bpy.data.images.load(out, check_existing=True)


def mat(name):
    """Материал из библиотеки (создаётся один раз на сцену)."""
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    if name in FLAT:
        col, rough, metal = FLAT[name]
        bsdf.inputs["Base Color"].default_value = (*col, 1)
        bsdf.inputs["Roughness"].default_value = rough
        bsdf.inputs["Metallic"].default_value = metal
        _tile[name] = 1.0
        return m
    tex_id, tint, tile, metal = MATS[name]
    _tile[name] = tile
    diff = nt.nodes.new("ShaderNodeTexImage")
    diff.image = _tinted_image(tex_id, tint)
    nt.links.new(diff.outputs["Color"], bsdf.inputs["Base Color"])
    rough = nt.nodes.new("ShaderNodeTexImage")
    rough.image = bpy.data.images.load(os.path.join(TEX, tex_id, "rough.jpg"), check_existing=True)
    rough.image.colorspace_settings.name = "Non-Color"
    nt.links.new(rough.outputs["Color"], bsdf.inputs["Roughness"])
    nor = nt.nodes.new("ShaderNodeTexImage")
    nor.image = bpy.data.images.load(os.path.join(TEX, tex_id, "nor.jpg"), check_existing=True)
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


def cyl(radius, height, at=(0, 0, 0), material="steel", segments=16, axis="Z", bevel=0.008, name="cyl", radius_top=None):
    """Цилиндр (или усечённый конус): at — центр нижнего торца; axis — вдоль какой оси стоит."""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=True, segments=segments, radius1=radius,   # крышка веером: n-угольник с фаской даёт чёрные пятна
                          radius2=radius if radius_top is None else radius_top, depth=height)
    bmesh.ops.translate(bm, vec=(0, 0, height / 2), verts=bm.verts)
    ob = _obj_from_bm(bm, name, material)
    if axis == "X":
        ob.rotation_euler.y = math.pi / 2
    elif axis == "Y":
        ob.rotation_euler.x = -math.pi / 2
    ob.location = at
    _finish(ob, bevel)
    return ob


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


def tris(objects):
    return sum(sum(len(p.vertices) - 2 for p in ob.data.polygons) for ob in objects)


def export(name, out_dir):
    objs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    uv_world(objs)
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, name + ".glb")
    for o in bpy.context.scene.objects:
        o.select_set(o.type == "MESH")
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_apply=True,
                              export_yup=True, export_image_format="JPEG", export_jpeg_quality=85)
    return path, tris(objs)


def render_preview(path_png, size=(720, 540)):
    """Превью под углом камеры игры (наклон 58°, почти изометрия), Cycles на процессоре."""
    sc = bpy.context.scene
    objs = [o for o in sc.objects if o.type == "MESH"]
    lo = np.min([np.min([o.matrix_world @ v.co for v in o.data.vertices], axis=0) for o in objs], axis=0)
    hi = np.max([np.max([o.matrix_world @ v.co for v in o.data.vertices], axis=0) for o in objs], axis=0)
    c = (lo + hi) / 2
    ext = float(np.max(hi - lo))
    # пол
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=ext * 3)
    floor = _obj_from_bm(bm, "floor", "concrete")
    uv_world([floor])
    # камера: с юго-востока, наклон 58°
    cam_d = bpy.data.cameras.new("cam")
    cam_d.type = "ORTHO"
    cam_d.ortho_scale = ext * 1.6
    cam = bpy.data.objects.new("cam", cam_d)
    sc.collection.objects.link(cam)
    tilt, yaw = math.radians(58), math.radians(-35)
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
