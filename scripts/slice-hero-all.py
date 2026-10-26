# Нарезка всех листов анимаций героя (assets/gen/charvar/hero_<anim>.png) в game/art/chars/hero/<anim>_<f|b><i>.png.
# Рост кадра: стоя — TALL; остальные анимации — пропорционально 3D-каркасу (присед ниже, смерть лёжа), иначе нарезка
# растянула бы присевшего до роста стоящего. Запуск: python scripts/slice-hero-all.py [рост=50]
import os, subprocess, sys
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "scripts"))
TALL = int(sys.argv[1]) if len(sys.argv) > 1 else 50
TMP = "C:/Temp/claude/char/anim"
spec = __import__("importlib.util").util.spec_from_file_location("ha", os.path.join(ROOT, "scripts", "hero-anims.py"))
src = open(os.path.join(ROOT, "scripts", "hero-anims.py"), encoding="utf-8").read().split("STYLE = (")[0]   # только таблица ANIMS
ns = {"__file__": os.path.join(ROOT, "scripts", "hero-anims.py")}; exec(src, ns)
ANIMS, D_FRONT = ns["ANIMS"], ns["D_FRONT"]


def h3d(anim):
    clip, n = ANIMS[anim][0], ANIMS[anim][1]
    hs = []
    for i in range(n):
        im = Image.open(f"{TMP}/{anim}/{clip}_d{D_FRONT}_f{i}.png").convert("RGBA")
        b = im.getchannel("A").point(lambda v: 255 if v > 60 else 0).getbbox()
        if b: hs.append(b[3] - b[1])
    return max(hs)


base = h3d("idle")
for anim, (clip, n, _, _) in ANIMS.items():
    sheet = os.path.join(ROOT, "assets", "gen", "charvar", f"hero_{anim}.png")
    if not os.path.exists(sheet): print("нет листа", anim); continue
    tall = round(TALL * h3d(anim) / base)
    r = subprocess.run([sys.executable, os.path.join(ROOT, "scripts", "slice-hero.py"), sheet, f"hero/{anim}", str(tall), str(n)], capture_output=True, text=True, encoding="utf-8", errors="replace")
    print(anim, "рост", tall, r.stdout.strip().splitlines()[0] if r.stdout else r.stderr[-200:])
