# Персонаж из 3D (манекен Quaternius UAL, CC0) → спрайт-листы: 8 направлений × кадры анимации, ракурс игры 3/4,
# свет сверху-слева. Запуск (Blender без окна):
#   blender -b --python scripts/render-char3d.py -- <анимация> <кадров> <выход_png>
# Пример: ... -- Walk_Loop 8 C:/Temp/claude/char/walk.png
import bpy, math, sys, os
from mathutils import Vector

GLB = r"D:\Tools\ual\Animation Library[Standard]\Godot\AnimationLibrary_Godot_Standard.glb"
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ACTION = args[0] if args else "Walk_Loop"
FRAMES = int(args[1]) if len(args) > 1 else 8
OUT = args[2] if len(args) > 2 else "C:/Temp/claude/char/sheet.png"
CELL = 128           # кадр в игре
SS = 4               # суперсэмплинг: рендер 512 → уменьшение до 128
PPM = 32             # пикселей на метр, как у всех ассетов мира
TILT = 53.13         # камера с юга: cos/sin = 0.75 — как в игре (высота ×0,75 от глубины, Iso.HS); по Y лист потом ×1,25

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLB)
for o in list(bpy.data.objects):
    if o.name.startswith("Icosphere"): bpy.data.objects.remove(o)
rig = bpy.data.objects["Rig"]
body = bpy.data.objects["Mannequin"]

# ---- цвета: кожа, майка, штаны по весам костей (у манекена один серый материал) ----
def mat(name, rgb, rough=0.8):
    m = bpy.data.materials.new(name); m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*rgb, 1); bsdf.inputs["Roughness"].default_value = rough
    return m
skin, shirt, pants, hair = mat("skin", (0.62, 0.42, 0.32)), mat("shirt", (0.32, 0.33, 0.3)), mat("pants", (0.2, 0.22, 0.25)), mat("hair", (0.12, 0.09, 0.07))
me = body.data
me.materials.clear()
for m in (skin, shirt, pants, hair): me.materials.append(m)
groups = {g.index: g.name.lower() for g in body.vertex_groups}
def part(vi):
    best, bw = "", 0
    for g in me.vertices[vi].groups:
        if g.weight > bw: bw, best = g.weight, groups.get(g.group, "")
    return best
for poly in me.polygons:
    names = [part(v) for v in poly.vertices]
    n = max(set(names), key=names.count)
    z = sum(me.vertices[v].co.z for v in poly.vertices) / len(poly.vertices)
    if any(k in n for k in ("thigh", "shin", "hips", "foot", "toe")): poly.material_index = 2
    elif any(k in n for k in ("spine", "upper_arm", "shoulder")): poly.material_index = 1
    elif "head" in n and z > 1.68: poly.material_index = 3
    else: poly.material_index = 0

# ---- анимация ----
act = bpy.data.actions[ACTION]
rig.animation_data_create(); rig.animation_data.action = act
f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])

# ---- сцена: ортокамера с юга, наклон 50°, солнце сверху-слева ----
sc = bpy.context.scene
try: sc.render.engine = "BLENDER_EEVEE_NEXT"
except Exception: sc.render.engine = "BLENDER_EEVEE"
sc.render.film_transparent = True
sc.render.resolution_x = sc.render.resolution_y = CELL * SS
sc.view_settings.view_transform = "Standard"
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
CHAR_SCALE = 1.05   # ~1,25 роста нынешнего персонажа (тот ~36 px); люди чуть крупнее реального масштаба
cam.data.type = "ORTHO"; cam.data.ortho_scale = CELL / PPM / CHAR_SCALE
t = math.radians(TILT)
cam.rotation_euler = (math.pi / 2 - t, 0, 0)
target = Vector((0, 0, 1.15))   # ступни у нижней трети кадра, над головой — место для рук с оружием
cam.location = target + Vector((0, -math.cos(t), math.sin(t))) * 10
cam.data.shift_y = 0.0
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sc.collection.objects.link(sun)
sun.data.energy = 3.2; sun.data.angle = math.radians(8)
sun.rotation_euler = (math.radians(50), math.radians(-35), math.radians(-30))   # сверху-слева (с северо-запада, как у ассетов)
world = bpy.data.worlds.new("w"); sc.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.42, 0.45, 0.52, 1); world.node_tree.nodes["Background"].inputs[1].default_value = 0.55   # холодная подсветка теней

# ---- пропорции: у манекена тонкие ноги и большие ступни — толще бёдра/голени/руки, ступни меньше
# (ширина кости — её локальные X и Z; дети наследуют масштаб, поэтому ступню компенсируем) ----
# Телосложение «гигачад»: плечи и грудь шире, шея и руки толще, талия уже, ноги мощные, ступни маленькие.
# (ширина, длина, ширина) в осях кости. Масштаб из анимации перебивал pose.scale — поэтому ограничитель LIMIT_SCALE
# (применяется после действия), без наследования от родителя (иначе ступня брала ширину бедра → «ласты»).
SHAPE = {"DEF-thigh": (1.5, 1, 1.5), "DEF-shin": (1.4, 1, 1.4), "DEF-foot": (0.7, 0.62, 0.7), "DEF-toe": (0.7, 0.5, 0.7),
         "DEF-hips": (1.05, 1, 1.05), "DEF-spine.001": (0.95, 1, 0.95), "DEF-spine.002": (1.2, 1, 1.15), "DEF-spine.003": (1.4, 1, 1.25),
         "DEF-shoulder": (1.25, 1.35, 1.25), "DEF-upper_arm": (1.55, 1, 1.55), "DEF-forearm": (1.4, 1, 1.4), "DEF-hand": (1.05, 1, 1.05),
         "DEF-neck": (1.5, 0.9, 1.5), "DEF-head": (0.95, 0.95, 0.95)}
for bn in rig.data.bones:
    if any(bn.name.startswith(k) for k in SHAPE): bn.inherit_scale = "NONE"
for pb in rig.pose.bones:
    for k, v in SHAPE.items():
        if pb.name.startswith(k + ".") or pb.name == k:
            c = pb.constraints.new("LIMIT_SCALE"); c.owner_space = "LOCAL"
            for ax, val in zip("xyz", v):
                setattr(c, f"use_min_{ax}", True); setattr(c, f"use_max_{ax}", True)
                setattr(c, f"min_{ax}", val); setattr(c, f"max_{ax}", val)
def shape(): pass

# ---- оружие в правой руке (простая болванка, сцеплена с костью кисти): WEAPON=pistol ----
WEAPON = args[3] if len(args) > 3 else ""
if WEAPON:
    gunmat = mat("gun", (0.08, 0.08, 0.09), 0.45)
    def box(name, size, loc):
        bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 0))
        o = bpy.context.active_object; o.name = name; o.scale = size; o.location = loc
        o.data.materials.append(gunmat)
        return o
    parts = [box("slide", (0.035, 0.2, 0.045), (0, 0.11, 0.035)), box("grip", (0.03, 0.05, 0.11), (0, 0.06, -0.02))] if WEAPON == "pistol" else             [box("body", (0.05, 0.75, 0.08), (0, 0.25, 0.04)), box("mag", (0.03, 0.06, 0.16), (0, 0.22, -0.07)), box("stock", (0.045, 0.22, 0.1), (0, -0.2, 0.0))]
    for o in parts:
        o.parent = rig; o.parent_type = "BONE"; o.parent_bone = "DEF-hand.R"
        o.location = o.location + rig.pose.bones["DEF-hand.R"].bone.vector * 0 - __import__("mathutils").Vector((0, rig.pose.bones["DEF-hand.R"].length, 0))
        bpy.context.view_layer.update()

# ---- рендер: 8 направлений (0 — лицом к камере, далее через 45° против часовой) × FRAMES кадров → отдельные png;
# склейка и пиксель-обработка — системным Python (scripts/pack-char3d.py), в Blender нет PIL ----
os.makedirs(OUT, exist_ok=True)
for d in range(8):
    rig.rotation_mode = "XYZ"; rig.rotation_euler = (0, 0, math.radians(d * 45))
    for i in range(FRAMES):
        sc.frame_set(f0 + round(i * (f1 - f0) / FRAMES))
        shape()
        sc.render.filepath = os.path.join(OUT, f"{ACTION}_d{d}_f{i}.png")
        bpy.ops.render.render(write_still=True)
print("RENDER_OK", ACTION, FRAMES)
