# Галерея поз рига: каждое состояние × 4 ракурса (к камере, вправо, от камеры, влево) — один лист для оценки.
# Каждый кадр — отдельный запуск игры вне экрана в полигоне стрельбы (в руках АКМ), крупный план персонажа.
# Запуск: python scripts/rig-gallery.py [--rigset=hero] [--out=C:/Temp/claude/gallery.png] [--only=walk,aim]
import os, subprocess, sys
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GODOT = r"C:\Users\Xbaza\Desktop\Rastvordd\tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe"
args = dict(a.lstrip("-").split("=", 1) if "=" in a else (a.lstrip("-"), "1") for a in sys.argv[1:])
RIG = args.get("rigset", "hero")
OUT = args.get("out", "C:/Temp/claude/gallery.png")
TMP = os.path.join(os.path.dirname(OUT), "gallery_tmp")
os.makedirs(TMP, exist_ok=True)

DIRS = [("к камере", "0;4"), ("вправо", "4;0"), ("от камеры", "0;-4"), ("влево", "-4;0")]
# состояние: (подпись, доп. аргументы, действия --do (после поворота), кадров)
STATES = {
    "idle":        ("стоит", [], "", 240),
    "walk":        ("идёт", [], "go{d}", 260),
    "run":         ("бежит", [], "sprint,go{d}", 260),
    "crouch":      ("присед", [], "crouch", 260),
    "crouch_walk": ("крадётся", [], "crouch,go{d}", 280),
    "aim":         ("прицел", ["--peek=1"], "", 240),
    "crouch_aim":  ("прицел в приседе", ["--peek=1"], "crouch", 260),
}
only = set(args["only"].split(",")) if "only" in args else None
font = ImageFont.truetype("arialbd.ttf", 16)
rows = []
for key, (label, extra, act, frames) in STATES.items():
    if only and key not in only: continue
    cells = []
    for dname, d in DIRS:
        shot = os.path.join(TMP, f"{key}_{d.replace(';', '_').replace('-', 'm')}.png").replace("\\", "/")
        do = f"20:at{d}" + (f",40:{act.format(d=d).replace(',', ',40:')}" if act else "")
        cmd = [GODOT, "--position", "-32000,-32000", "--path", os.path.join(ROOT, "game"), "--", "--sandbox=aim",
               f"--rigset={RIG}", "--zoom=2.6", f"--frames={frames}", f"--do={do}", f"--shot={shot}"] + extra
        subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=200)
        im = Image.open(shot).convert("RGB"); W, H = im.size
        c = im.crop((W // 2 - 80, H // 2 - 110, W // 2 + 80, H // 2 + 50)).resize((240, 240), Image.NEAREST)
        ImageDraw.Draw(c).text((4, 2), f"{label} · {dname}", font=font, fill=(255, 225, 150))
        cells.append(c)
    row = Image.new("RGB", (4 * 244, 244), (12, 12, 12))
    for k, c in enumerate(cells): row.paste(c, (k * 244, 2))
    rows.append(row)
    print("ok", key)
sheet = Image.new("RGB", (4 * 244, 244 * len(rows)), (12, 12, 12))
for k, r in enumerate(rows): sheet.paste(r, (0, k * 244))
sheet.save(OUT); print("лист:", OUT)
