# Blender-помощник для scripts/cut-rig-hero.py: манекен (Quaternius UAL, CC0) в A-позе с пропорциями render-char3d.py,
# ракурс игры (CHAR_TILT), направления d1 (спереди-справа) и d3 (сзади-справа). На каждое направление:
#   apose_d<N>.png  — затенённый рендер (образец позы для генерации),
#   label_d<N>.png  — карта частей: плоские цвета по группам костей, без сглаживания,
#   joints_d<N>.json — суставы (голова/хвост костей) в пикселях кадра.
# Запуск: blender -b --python scripts/cut-rig-render.py -- <папка_вывода> [разрешение=1024]
import bpy, math, sys, os, json
from mathutils import Vector, Matrix
from bpy_extras.object_utils import world_to_camera_view

GLB = r"D:\Tools\ual\Animation Library[Standard]\Godot\AnimationLibrary_Godot_Standard.glb"
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = args[0] if args else "C:/Temp/claude/rigcut/blend"
RES = int(args[1]) if len(args) > 1 else 1024
CELL, PPM, CHAR_SCALE = 128, 32, 1.05
TILT = float(os.environ.get("CHAR_TILT", "30"))
ZOOM = float(os.environ.get("CUT_ZOOM", "2"))   # крупнее кадр игры: фигура почти во весь рендер
ARM_DOWN = float(os.environ.get("ARM_DOWN", "40"))   # руки ниже горизонтали, градусов
LEG_OUT = float(os.environ.get("LEG_OUT", "7"))      # ноги врозь, градусов
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLB)
for o in list(bpy.data.objects):
    if o.name.startswith("Icosphere"): bpy.data.objects.remove(o)
rig = bpy.data.objects["Rig"]
body = bpy.data.objects["Mannequin"]

# ---- группы частей рига (имя метки, цвет карты) ----
LABELS = {"head": (1, 0, 0), "torso": (0, 1, 0), "pelvis": (0, 0, 1),
          "upperarm_L": (1, 1, 0), "upperarm_R": (1, 0, 1), "forearm_L": (0, 1, 1), "forearm_R": (1, 0.5, 0),
          "thigh_L": (0.5, 0, 1), "thigh_R": (0, 0.5, 0), "shin_L": (0.5, 0.5, 1), "shin_R": (0.5, 0.25, 0)}
def label_of(g):
    s = "_L" if g.endswith(".L") else "_R"
    if "neck" in g or "head" in g: return "head"
    if "spine.002" in g or "spine.003" in g or "shoulder" in g: return "torso"
    if "hips" in g or "spine.001" in g: return "pelvis"
    if "upper_arm" in g: return "upperarm" + s
    if any(k in g for k in ("forearm", "hand", "f_", "thumb")): return "forearm" + s
    if "thigh" in g: return "thigh" + s
    if any(k in g for k in ("shin", "foot", "toe")): return "shin" + s
    return "torso"

me = body.data
groups = {g.index: g.name for g in body.vertex_groups}
def vgroup(vi):
    best, bw = "", 0
    for g in me.vertices[vi].groups:
        if g.weight > bw: bw, best = g.weight, groups.get(g.group, "")
    return best
face_label, face_shade = [], []
for poly in me.polygons:
    names = [vgroup(v) for v in poly.vertices]
    n = max(set(names), key=names.count)
    face_label.append(label_of(n))
    nl = n.lower(); z = sum(me.vertices[v].co.z for v in poly.vertices) / len(poly.vertices)
    if any(k in nl for k in ("thigh", "shin", "hips", "foot", "toe")): face_shade.append(2)
    elif any(k in nl for k in ("spine", "upper_arm", "shoulder")): face_shade.append(1)
    elif "head" in nl and z > 1.68: face_shade.append(3)
    else: face_shade.append(0)

def mat(name, rgb, rough=0.8):
    m = bpy.data.materials.new(name); m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*rgb, 1); bsdf.inputs["Roughness"].default_value = rough
    m.diffuse_color = (*rgb, 1)
    return m
shade_mats = [mat("skin", (0.62, 0.42, 0.32)), mat("shirt", (0.32, 0.33, 0.3)), mat("pants", (0.2, 0.22, 0.25)), mat("hair", (0.12, 0.09, 0.07))]
label_names = list(LABELS)
label_mats = []
for k in label_names:
    m = bpy.data.materials.new("L_" + k); m.diffuse_color = (*LABELS[k], 1); label_mats.append(m)

def use_shade():
    me.materials.clear()
    for m in shade_mats: me.materials.append(m)
    for p, i in zip(me.polygons, face_shade): p.material_index = i
def use_label():
    me.materials.clear()
    for m in label_mats: me.materials.append(m)
    for p, k in zip(me.polygons, face_label): p.material_index = label_names.index(k)

# ---- пропорции «гигачад», как в render-char3d.py ----
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

# ---- поза: T-поза из действия, затем руки вниз на ARM_DOWN, ноги врозь на LEG_OUT ----
sc = bpy.context.scene
rig.animation_data_create(); rig.animation_data.action = bpy.data.actions["A_TPose"]
sc.frame_set(0)
rig.animation_data.action = None   # поза осталась в свойствах костей, дальше правим её руками
bpy.context.view_layer.update()
up = (rig.matrix_world.inverted().to_3x3() @ Vector((0, 0, 1))).normalized()
def swing(name, down_deg):
    """Поворачивает кость вокруг её головы в вертикальной плоскости: down_deg > 0 — кончик ниже."""
    pb = rig.pose.bones[name]
    d = (pb.tail - pb.head).normalized()
    axis = d.cross(up)
    if axis.length < 1e-4: axis = Vector((0, 1, 0))
    axis.normalize()
    h = pb.head.copy()
    pb.matrix = Matrix.Translation(h) @ Matrix.Rotation(math.radians(-down_deg), 4, axis) @ Matrix.Translation(-h) @ pb.matrix
    bpy.context.view_layer.update()
for s in ("L", "R"):
    swing(f"DEF-upper_arm.{s}", ARM_DOWN)
    # бедро: наружу — вокруг оси «вперёд»: кончик (колено) уходит в сторону
    pb = rig.pose.bones[f"DEF-thigh.{s}"]
    side = 1 if pb.head.x > 0 else -1
    h = pb.head.copy(); fwd = up.cross(Vector((1, 0, 0))).normalized()
    pb.matrix = Matrix.Translation(h) @ Matrix.Rotation(math.radians(LEG_OUT * side), 4, fwd) @ Matrix.Translation(-h) @ pb.matrix
    bpy.context.view_layer.update()
    # проверка направления: колено должно уйти наружу, иначе крутим обратно в два раза
    if (pb.tail.x - pb.head.x) * side < 0:
        pb.matrix = Matrix.Translation(h) @ Matrix.Rotation(math.radians(-2 * LEG_OUT * side), 4, fwd) @ Matrix.Translation(-h) @ pb.matrix
        bpy.context.view_layer.update()

# ---- камера и свет, как в render-char3d.py ----
sc.render.film_transparent = True
sc.render.resolution_x = sc.render.resolution_y = RES
sc.view_settings.view_transform = "Standard"
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = "ORTHO"; cam.data.ortho_scale = CELL / PPM / CHAR_SCALE / ZOOM
t = math.radians(TILT)
cam.rotation_euler = (math.pi / 2 - t, 0, 0)
cam.location = Vector((0, 0, 0.98)) + Vector((0, -math.cos(t), math.sin(t))) * 10
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sc.collection.objects.link(sun)
sun.data.energy = 3.2; sun.data.angle = math.radians(8)
sun.rotation_euler = (math.radians(50), math.radians(-35), math.radians(-30))
world = bpy.data.worlds.new("w"); sc.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.42, 0.45, 0.52, 1); world.node_tree.nodes["Background"].inputs[1].default_value = 0.55

JOINT_BONES = ["DEF-hips", "DEF-spine.001", "DEF-spine.002", "DEF-spine.003", "DEF-neck", "DEF-head"] + \
    [f"DEF-{b}.{s}" for s in "LR" for b in ("shoulder", "upper_arm", "forearm", "hand", "thigh", "shin", "foot", "toe")]

for d in (1, 3):
    rig.rotation_mode = "XYZ"; rig.rotation_euler = (0, 0, math.radians(d * 45))
    bpy.context.view_layer.update()
    joints = {}
    for bn in JOINT_BONES:
        pb = rig.pose.bones[bn]
        pts = []
        for p in (pb.head, pb.tail):
            w = rig.matrix_world @ p
            v = world_to_camera_view(sc, cam, w)
            pts.append([v.x * RES, (1 - v.y) * RES, (w - cam.location).length])
        joints[bn] = {"head": pts[0], "tail": pts[1]}
    json.dump(joints, open(os.path.join(OUT, f"joints_d{d}.json"), "w"), indent=1)
    # затенённый рендер
    use_shade()
    try: sc.render.engine = "BLENDER_EEVEE_NEXT"
    except Exception: sc.render.engine = "BLENDER_EEVEE"
    sc.render.filter_size = 1.5
    sc.render.filepath = os.path.join(OUT, f"apose_d{d}.png"); bpy.ops.render.render(write_still=True)
    # карта частей: плоско, без сглаживания
    use_label()
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "FLAT"; sc.display.shading.color_type = "MATERIAL"
    sc.display.render_aa = "OFF"; sc.render.filter_size = 0.0
    sc.display.shading.show_object_outline = False; sc.display.shading.show_cavity = False
    sc.render.filepath = os.path.join(OUT, f"label_d{d}.png"); bpy.ops.render.render(write_still=True)
json.dump({k: list(v) for k, v in LABELS.items()}, open(os.path.join(OUT, "labels.json"), "w"))
print("CUTRENDER_OK")
