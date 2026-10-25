# Персонаж «по частям» в 3D: на скелет Quaternius UAL (CC0) вместо гладкого манекена — жёсткие блоки на каждую кость
# (голова, шея, грудь, пресс, таз, плечо, предплечье, кисть, бедро, голень, ступня). Пиксельная покраска: 3 тона без
# градиента (toon), тёмный контур, рендер сразу в игровом разрешении без сглаживания. Телосложение «гигачад».
# Запуск: blender -b --python scripts/render-char-blocky.py -- <анимация> <кадров> <папка_кадров> [pistol|rifle]
# Склейка: python scripts/pack-char3d.py <папка> <анимация> <кадров> <лист.png>
import bpy, math, sys, os
from mathutils import Vector

GLB = r"D:\Tools\ual\Animation Library[Standard]\Godot\AnimationLibrary_Godot_Standard.glb"
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ACTION = args[0] if args else "Walk_Loop"
FRAMES = int(args[1]) if len(args) > 1 else 8
OUT = args[2] if len(args) > 2 else "C:/Temp/claude/char/blocky"
WEAPON = args[3] if len(args) > 3 else ""
CELL, SS, PPM, TILT = 128, 4, 32, 53.13
CHAR_SCALE = 1.05

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLB)
for o in list(bpy.data.objects):
    if o.type == "MESH": bpy.data.objects.remove(o)       # гладкий манекен и сфера не нужны — только скелет
rig = bpy.data.objects["Rig"]

# ---- материалы: toon в 3 тона (свет / полутень / тень), лёгкий пиксельный шум ткани ----
def toon(name, rgb, noise=0.0):
    # обычная матовая заливка; «пиксельность» (палитра, контур 1 px) — в pack-char3d.py --pixel
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*rgb, 1); b.inputs["Roughness"].default_value = 0.95
    return m

SKIN = toon("skin", (0.62, 0.43, 0.33), 0.04)
JACKET = toon("jacket", (0.27, 0.29, 0.21))         # ватник цвета хаки
PANTS = toon("pants", (0.2, 0.21, 0.24))
BOOTS = toon("boots", (0.16, 0.12, 0.09))
HAIR = toon("hair", (0.12, 0.09, 0.07))
GUN = toon("gun", (0.12, 0.12, 0.13), 0.03)

# ---- блоки на кости: (кость, ширина, глубина, доля длины, материал, смещение вдоль кости) ----
PARTS = [
    ("DEF-hips", 0.40, 0.25, 1.0, PANTS, 0), ("DEF-spine.001", 0.36, 0.22, 1.0, JACKET, 0),
    ("DEF-spine.002", 0.48, 0.28, 1.0, JACKET, 0), ("DEF-spine.003", 0.58, 0.31, 1.0, JACKET, 0),
    ("DEF-neck", 0.17, 0.17, 1.0, SKIN, 0), ("DEF-head", 0.23, 0.25, 0.95, SKIN, 0),
    ("DEF-thigh.L", 0.2, 0.21, 1.0, PANTS, 0), ("DEF-thigh.R", 0.2, 0.21, 1.0, PANTS, 0),
    ("DEF-shin.L", 0.16, 0.17, 1.0, PANTS, 0), ("DEF-shin.R", 0.16, 0.17, 1.0, PANTS, 0),
    ("DEF-foot.L", 0.11, 0.09, 1.25, BOOTS, 0), ("DEF-foot.R", 0.11, 0.09, 1.25, BOOTS, 0),
    ("DEF-shoulder.L", 0.17, 0.17, 0.9, JACKET, 0), ("DEF-shoulder.R", 0.17, 0.17, 0.9, JACKET, 0),
    ("DEF-upper_arm.L", 0.16, 0.16, 1.0, JACKET, 0), ("DEF-upper_arm.R", 0.16, 0.16, 1.0, JACKET, 0),
    ("DEF-forearm.L", 0.13, 0.13, 1.0, SKIN, 0), ("DEF-forearm.R", 0.13, 0.13, 1.0, SKIN, 0),
    ("DEF-hand.L", 0.09, 0.05, 1.6, SKIN, 0), ("DEF-hand.R", 0.09, 0.05, 1.6, SKIN, 0),
]
def block(bone, w, d, k, m, off):
    b = rig.data.bones[bone]
    L = b.length * k
    bpy.ops.mesh.primitive_cube_add(size=1)
    o = bpy.context.active_object; o.name = "blk_" + bone
    o.scale = (w, L, d)
    bpy.ops.object.transform_apply(scale=True)
    mod = o.modifiers.new("bev", "BEVEL"); mod.width = min(w, d) * 0.22; mod.segments = 1     # фаска — не «кирпич»
    o.data.materials.append(m)
    o.parent = rig; o.parent_type = "BONE"; o.parent_bone = bone
    o.location = (0, -b.length + L / 2 + off, 0)           # от головы кости к хвосту (родитель-кость: начало в хвосте)
    return o
for p in PARTS: block(*p)
# волосы — шапка на голове, подбородок квадратный
hb = rig.data.bones["DEF-head"]
bpy.ops.mesh.primitive_cube_add(size=1); hair = bpy.context.active_object
hair.scale = (0.245, hb.length * 0.35, 0.265); bpy.ops.object.transform_apply(scale=True); hair.data.materials.append(HAIR)
hair.parent = rig; hair.parent_type = "BONE"; hair.parent_bone = "DEF-head"; hair.location = (0, -hb.length * 0.12, -0.01)

if WEAPON:
    hb = rig.data.bones["DEF-hand.R"]
    def gunbox(size, loc):
        bpy.ops.mesh.primitive_cube_add(size=1); o = bpy.context.active_object
        o.scale = size; bpy.ops.object.transform_apply(scale=True); o.data.materials.append(GUN)
        o.parent = rig; o.parent_type = "BONE"; o.parent_bone = "DEF-hand.R"; o.location = loc
    if WEAPON == "pistol":
        gunbox((0.04, 0.2, 0.05), (0, 0.04, 0.04)); gunbox((0.035, 0.05, 0.12), (0, -0.04, -0.03))
    else:
        gunbox((0.05, 0.8, 0.08), (0, 0.18, 0.05)); gunbox((0.035, 0.07, 0.17), (0, 0.12, -0.07)); gunbox((0.05, 0.25, 0.11), (0, -0.33, 0.02))

act = bpy.data.actions[ACTION]
rig.animation_data_create(); rig.animation_data.action = act
f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])

sc = bpy.context.scene
try: sc.render.engine = "BLENDER_EEVEE_NEXT"
except Exception: sc.render.engine = "BLENDER_EEVEE"
sc.render.film_transparent = True
sc.render.resolution_x = sc.render.resolution_y = CELL * SS
sc.render.filter_size = 0.5
sc.view_settings.view_transform = "Standard"
# контур (Freestyle): тёмная линия по силуэту и стыкам блоков
sc.render.use_freestyle = False; sc.render.line_thickness_mode = "ABSOLUTE"; sc.render.line_thickness = 1.6 * SS
ls = sc.view_layers[0].freestyle_settings.linesets.new("outline") if not sc.view_layers[0].freestyle_settings.linesets else sc.view_layers[0].freestyle_settings.linesets[0]
ls.select_by_visibility = True; ls.select_by_edge_types = True
ls.select_silhouette = True; ls.select_border = True; ls.select_crease = False
if ls.linestyle is None: ls.linestyle = bpy.data.linestyles.new("ln")
ls.linestyle.color = (0.05, 0.05, 0.06); ls.linestyle.thickness = 1.6 * SS
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = "ORTHO"; cam.data.ortho_scale = CELL / PPM / CHAR_SCALE
t = math.radians(TILT)
cam.rotation_euler = (math.pi / 2 - t, 0, 0)
cam.location = Vector((0, 0, 1.15)) + Vector((0, -math.cos(t), math.sin(t))) * 10
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sc.collection.objects.link(sun)
sun.data.energy = 3.0; sun.rotation_euler = (math.radians(50), math.radians(-35), math.radians(-30))
world = bpy.data.worlds.new("w"); sc.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.42, 0.45, 0.52, 1); world.node_tree.nodes["Background"].inputs[1].default_value = 0.5

os.makedirs(OUT, exist_ok=True)
for d in range(8):
    rig.rotation_mode = "XYZ"; rig.rotation_euler = (0, 0, math.radians(d * 45))
    for i in range(FRAMES):
        sc.frame_set(f0 + round(i * (f1 - f0) / FRAMES))
        sc.render.filepath = os.path.join(OUT, f"{ACTION}_d{d}_f{i}.png")
        bpy.ops.render.render(write_still=True)
print("RENDER_OK", ACTION, FRAMES)
