# Превью уровня −1 «Объекта 42» (казармы) из игровых пиксельных спрайтов — на движке макетов scripts/mock-tier2.py.
# Только картинка (в игру не экспортируется): docs/design/img/o42-l1-preview.png (+ -half, -lit без подписей).
# Запуск: python scripts/mock-o42.py
import importlib.util, os, random
from PIL import Image, ImageDraw, ImageFilter

_spec = importlib.util.spec_from_file_location('mt2', os.path.join(os.path.dirname(os.path.abspath(__file__)), 'mock-tier2.py'))
mt = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(mt)
Scene, PPM, OUT, font = mt.Scene, mt.PPM, mt.OUT, mt.font

W, H, M = 96, 72, 4
s = Scene('«Объект 42», уровень −1: казармы', W, H, M=M, seed=4242)
WALL, HW = 'obj_wall_bunker', 3.0       # толстые бетонные стены бункера, потолок 3 м
INNER = 'obj_wall_concrete_raw'
lights = []                                    # (x, y, радиус м, цвет, сила)


def red(x, y, r=7):
    s.prop('props/obj_m_red_lamp_ceiling', x, y, shadow=False)
    lights.append((x, y - 0.4, r, (255, 60, 40), 0.85))


def warm(x, y, r=9):
    s.prop('props/obj_lamp_cage', x, y, shadow=False)
    lights.append((x, y - 0.3, r, (255, 214, 150), 0.75))


def box(x0, y0, x1, y1, gaps=(), tex=INNER, h=HW):
    s.walls([(x0, y0), (x1, y0), (x1, y1), (x0, y1), (x0, y0)], h, tex, t=0.4, gaps=gaps)


# ---------- скала и полы ----------
s.ground('ground_rock')
s.ground('obj_floor_bunker', s.rect(0, 0, W, H), blur=0)
s.ground('obj_floor_painted_green', s.rect(4, 30, 92, 40), blur=0)          # главный коридор запад—восток
s.ground('obj_floor_painted_green', s.rect(44, 4, 52, 68), blur=0)          # коридор север—юг
s.ground('floor_linoleum_green', s.rect(4, 4, 40, 28), blur=0)              # казарма
s.ground('obj_floor_tile_white', s.rect(4, 42, 22, 68), blur=0)             # душевые
s.ground('floor_concrete_painted', s.rect(56, 4, 92, 28), blur=0)           # столовая
s.ground('obj_floor_grating', s.rect(56, 42, 92, 68), blur=0)               # дизельная / вентиляция
s.ground('obj_floor_hazard_edge', s.rect(24, 42, 42, 46), blur=0)           # кромка у оружейки
for (x0, y0, x1, y1) in [(0, 0, 10, 10), (86, 0, 96, 10), (43, 62, 53, 72)]:
    s.ground('obj_floor_hazard_edge', s.rect(x0 + 1, y0 + 1, x1 - 1, y1 - 1), blur=0)   # шлюзы под лифтами

# ---------- внешний контур ----------
s.walls([(0, 0), (W, 0), (W, H), (0, H), (0, 0)], HW, WALL, t=0.8)

# ---------- три шлюза сквадов (под лифтовыми оголовками) ----------
for (x0, y0, x1, y1, door) in [(0, 0, 10, 10, (10, 4, 10, 6.5)), (86, 0, 96, 10, (86, 4, 86, 6.5)), (43, 62, 53, 72, (46.8, 62, 49.2, 62))]:
    box(x0, y0, x1, y1, gaps=(door,), tex=WALL)
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    s.prop('props/obj_m_elevator_cabin', cx, cy + 1.6)
    s.prop('props/obj_lockers_bunker', x0 + 2.2, y1 - 0.8 if y0 == 0 else y0 + 2.2)
    s.prop('props/obj_decon_shower', x1 - 2.2, y1 - 0.8 if y0 == 0 else y0 + 2.6)
    red(cx, y0 + 1.2)
    s.mark(cx, cy, (255, 200, 60), 'ШЛЮЗ')
s.prop('props/obj_blast_door', 11.6, 6.6)
s.prop('props/obj_blast_door_open', 84.2, 7.0, flip=True)
s.prop('props/obj_blast_door', 48, 61.4)

# ---------- казарма (северо-запад) ----------
box(4, 12, 40, 28, gaps=((18, 28, 21, 28), (40, 18, 40, 21)))
s.walls([(10.5, 0), (10.5, 12)], HW, WALL, t=0.4)                              # отделить шлюз от казармы
for row, y in enumerate((15.8, 20.6, 25.4)):
    for k, x in enumerate((7.5, 12.5, 26.5, 31.5, 36.5)):
        s.prop('props/obj_l1_dorm_bunks', x, y, flip=(k + row) % 2 == 1)
        s.prop('props/obj_l1_footlocker', x + 1.4, y + 0.9, shadow=False)
s.prop('props/obj_bunk_triple', 19.5, 16.6)
s.prop('props/obj_lockers_bunker', 21.5, 25.8)
s.prop('props/obj_l1_gasmask_rack', 33.5, 12.9)
warm(13, 13.2); warm(30, 13.2); red(22, 22)
s.room(22, 20, 'barracks')
# дежурка у казармы — тут спит часть охраны
box(12, 4, 40, 12, gaps=((26, 12, 29, 12),))
s.prop('props/obj_l1_guard_desk', 18, 8.6)
s.prop('props/int_lockers', 14.2, 6.0)
s.prop('props/obj_map_table', 33, 8.8)
s.prop('props/obj_l2_phone_bank', 24.5, 5.4)
warm(24, 5.0)
s.room(26, 8, 'office')

# ---------- столовая (северо-восток) ----------
box(56, 4, 85.5, 28, gaps=((56, 30, 56, 30), (66, 28, 70, 28), (56, 14, 56, 18)))
for y in (11.5, 16.5, 21.5):
    for x in (61, 68, 75):
        s.prop('props/obj_l1_mess_tables', x, y)
s.prop('props/obj_l1_serving_line', 81, 9.5)
s.prop('props/kit_pots_shelf', 82.8, 14.8)
s.prop('props/kit_samovar_table', 81.5, 22)
s.prop('props/obj_water_tanks', 60, 26.6)
warm(64, 5.3); warm(76, 5.3); red(70, 24)
s.room(70, 16, 'kitchen')

# ---------- главный коридор и пост на перекрёстке ----------
for x in (8, 20, 32, 62, 74, 86):
    red(x, 30.8, 6)
for y in (12, 22, 46, 56):
    red(48, y, 6)
s.prop('outdoor/sandbags_wall', 48, 31.6)
s.prop('props/obj_l1_guard_desk', 51.5, 36.2)
s.prop('props/obj_m_cable_tray', 30, 30.4, shadow=False)
s.prop('props/obj_m_pipes_wall', 66, 30.4, shadow=False)
s.prop('props/obj_m_pipes_wall', 80, 30.4, shadow=False)
s.prop('props/obj_m_debris_pile', 37, 37.4)
s.prop('props/obj_collapsed_ceiling', 72, 38.6)                                 # завал — коридор сужен
s.prop('props/obj_m_sign_blank', 46, 30.6, shadow=False)
mt.guard(s, 47, 34, 'weapon_akm', 'пост «Перекрёсток»')
mt.guard(s, 20, 34.5, 'weapon_pps43', 'патруль')
warm(48, 35.2, 10)

# ---------- душевые и умывальня (юго-запад) ----------
box(4, 42, 22, 68, gaps=((22, 52, 22, 55),))
s.prop('props/obj_l1_shower_row', 9, 46.5)
s.prop('props/obj_l1_shower_row', 17, 46.5)
s.prop('props/obj_l1_shower_row', 9, 56)
s.prop('props/obj_lockers_bunker', 17.5, 60)
s.prop('furniture/bench_indoor', 13, 64)
red(13, 43.4)
s.room(13, 55, 'washroom')

# ---------- оружейка (лут) ----------
box(24, 46, 42, 60, gaps=((32, 46, 35, 46),), tex=WALL)
s.prop('props/obj_l1_armory_cage', 29, 52)
s.prop('props/obj_l1_armory_cage', 37, 52, flip=True)
s.prop('props/ars_shell_rack', 27.5, 58.4)
s.prop('props/crate_military', 33.5, 58.6)
s.prop('props/ars_ammo_bunker', 39, 58.4)
s.prop('props/obj_l1_gasmask_rack', 33.5, 47.4, shadow=False)
red(33.5, 49)
s.mark(33.5, 55, (255, 120, 80), 'ОРУЖЕЙКА: крейты')
mt.guard(s, 33.5, 44.2, 'weapon_akm', 'часовой оружейки')
# лестница вниз, на −2 (запертая гермодверь)
box(24, 62, 40, 68, gaps=((30, 62, 33, 62),))
s.prop('props/obj_m_stairwell', 34, 67.4)
s.prop('props/obj_rad_detector', 27, 64.4)
red(31.5, 63.2)
s.mark(34, 64.5, (120, 200, 255), 'ЛЕСТНИЦА НА −2')

# ---------- дизельная и вентиляция (юго-восток) ----------
box(56, 42, 92, 68, gaps=((56, 46, 56, 49), (70, 42, 73, 42)))
s.prop('props/obj_generator_big', 64, 50)
s.prop('props/obj_generator_big', 64, 58)
s.prop('props/obj_filter_unit', 76, 47)
s.prop('props/obj_vent_fan', 84, 47)
s.prop('props/obj_battery_room', 79, 57)
s.prop('props/obj_l4_fuel_day_tank', 87, 59)
s.prop('props/obj_vent_shaft_floor', 75, 64.6, shadow=False)
s.prop('props/barrel_mazut', 59.2, 65); s.prop('props/barrel_mazut', 60.6, 65.4)
s.prop('props/obj_cable_wall', 70, 42.6, shadow=False)
warm(64, 44, 8); red(84, 52); red(70, 64)
s.mark(64, 54, (120, 255, 160), 'ДИЗЕЛЬ: запуск → свет на уровне')
mt.guard(s, 74, 53, 'weapon_ppsh', 'дизелист')
s.room(74, 55, 'electrical')

# ---------- мусор и следы ----------
mt.mess(s, (5, 31, 43, 39), 10)
mt.mess(s, (53, 31, 91, 39), 10)
mt.mess(s, (5, 13, 39, 27), 8)

# ---------- сборка картинки (без крыш — уровень под землёй) ----------
sh = s.shadow.filter(ImageFilter.GaussianBlur(1.2)).point(lambda v: int(v * 0.42))
dark = Image.new('RGBA', s.img.size, (12, 14, 10, 255)); dark.putalpha(sh)
s.img.alpha_composite(dark)
for _, f in sorted(s.objs, key=lambda o: o[0]):
    f()
base = s.img.convert('RGB')

# свет: тьма бункера + пятна аварийных красных и тёплых ламп
light = Image.new('RGB', base.size, (34, 36, 44))                               # «темно, но читается» — для превью
glow = Image.new('RGB', base.size, (0, 0, 0))
gd = ImageDraw.Draw(glow)
for (x, y, r, col, k) in lights:
    cx, cy = s.px(x, y)
    R = int(r * PPM)
    for i in range(10, 0, -1):
        t = i / 10
        c = tuple(int(v * k * (1 - t) ** 1.4) for v in col)
        gd.ellipse([cx - R * t, cy - R * t * 0.8, cx + R * t, cy + R * t * 0.8], fill=c)
glow = glow.filter(ImageFilter.GaussianBlur(PPM * 0.6))
from PIL import ImageChops
light = ImageChops.add(light, glow)
lit = ImageChops.multiply(base, light.point(lambda v: min(255, int(v * 2.1))))
lit.save(OUT + 'o42-l1-lit.png')

# подписи помещений и меток — поверх, как на плане
lab = lit.copy()
d = ImageDraw.Draw(lab, 'RGBA')
F, Fs = font(54, True), font(34, True)
for (x, y, name) in [(22, 18.4, 'КАЗАРМА'), (26, 10.2, 'ДЕЖУРКА'), (70, 13.6, 'СТОЛОВАЯ'), (13, 51, 'ДУШЕВЫЕ'),
                     (64, 61.5, 'ДИЗЕЛЬНАЯ И ВЕНТИЛЯЦИЯ'), (12, 38.6, 'ГЛАВНЫЙ КОРИДОР'), (48, 26, 'КОРИДОР С—Ю')]:
    a, b = s.px(x, y)
    tw = d.textlength(name, font=F)
    d.rounded_rectangle([a - tw / 2 - 14, b - 36, a + tw / 2 + 14, b + 30], 10, fill=(0, 0, 0, 150))
    d.text((a - tw / 2, b - 34), name, font=F, fill=(235, 232, 220, 255))
GUNS = {'weapon_akm': 'АКМ', 'weapon_pps43': 'ППС-43', 'weapon_ppsh': 'ППШ'}
for (x, y, col, label) in s.marks:
    if label.startswith('MECH:guard:'):
        label = 'охрана · ' + GUNS.get(label.split(':')[-1], label.split(':')[-1])
        y += 1.2
    a, b = s.px(x, y)
    tw = d.textlength(label, font=Fs)
    d.rounded_rectangle([a - tw / 2 - 10, b - 24, a + tw / 2 + 10, b + 22], 8, fill=(0, 0, 0, 170), outline=col + (255,), width=3)
    d.text((a - tw / 2, b - 23), label, font=Fs, fill=col + (255,))
title = '«Объект 42» · уровень −1: казармы · 96 × 72 м · 3 шлюза сквадов, оружейка, дизель, лестница на −2'
d.rectangle([0, 0, lab.width, 92], fill=(0, 0, 0, 200))
d.text((40, 16), title, font=font(52, True), fill=(240, 236, 224, 255))
lab.save(OUT + 'o42-l1-preview.png')
lab.resize((lab.width // 2, lab.height // 2), Image.LANCZOS).save(OUT + 'o42-l1-preview-half.png')
print('ok', lab.size)
