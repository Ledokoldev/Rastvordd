# Полный набор анимаций нарисованного персонажа: 3D-каркас (Blender, манекен UAL CC0, камера 30°) → образец для Codex
# (поза + образец одежды) → manifest_hero_anims.json. Дальше: gen-assets.ps1 -Manifest assets/manifest_hero_anims.json,
# нарезка: python scripts/slice-hero.py assets/gen/charvar/hero_<anim>.png hero/<anim> 50 <кадров>
# Запуск: python scripts/hero-anims.py [--render]   (--render — перерисовать 3D-каркасы)
import json, os, subprocess, sys
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BLENDER = r"D:\Tools\blender-5.2.3-windows-x64\blender.exe"
TMP = "C:/Temp/claude/char/anim"
C = 128
D_FRONT, D_BACK = 1, 3      # направления каркаса: вперёд-вправо, назад-вправо (левые — зеркало в игре)

# анимация игры: (клип UAL, кадров, оружие в 3D-руке, что сказать художнику)
ANIMS = {
    "idle":         ("Idle_Loop", 4, "", "standing still with both feet planted firmly on the ground about shoulder-width apart, arms relaxed, only subtle breathing between frames — NOT walking, NO stride"),
    "walk":         ("Walk_Loop", 8, "", "walking at a calm natural pace, a full realistic walk cycle: heel strike, passing, push-off, arms swinging opposite to the legs"),
    "run":          ("Jog_Fwd_Loop", 8, "", "running, a full realistic run cycle with a clear flight phase, forearms bent, torso slightly forward"),
    "crouch_idle":  ("Crouch_Idle_Loop", 4, "", "crouching low and still on bent knees, weight on the balls of the feet, alert, subtle breathing"),
    "crouch_walk":  ("Crouch_Fwd_Loop", 8, "", "sneaking forward in a low crouch, a full realistic crouched walk cycle"),
    "aim_pistol":   ("Pistol_Aim_Neutral", 4, "pistol", "aiming a Makarov pistol forward with both hands at chest height, steady stance, tiny sway between frames"),
    "shoot_pistol": ("Pistol_Shoot", 4, "pistol", "firing a Makarov pistol held in both hands: recoil kick then return, small muzzle flash only in the 2nd frame"),
    "reload_pistol":("Pistol_Reload", 8, "pistol", "reloading a Makarov pistol: magazine out, new magazine in, rack the slide, back to ready"),
    "aim_rifle":    ("Pistol_Aim_Neutral", 4, "rifle", "aiming an AK assault rifle shouldered: right hand on the pistol grip, left hand on the handguard, stock tight to the right shoulder, cheek on the stock, steady stance, tiny sway"),
    "shoot_rifle":  ("Pistol_Shoot", 4, "rifle", "firing an AK assault rifle shouldered with both hands correctly placed: recoil pushes the shoulder back then returns, small muzzle flash only in the 2nd frame"),
    "reload_rifle": ("Pistol_Reload", 8, "rifle", "reloading an AK assault rifle: rifle tilted, left hand pulls the curved magazine out, inserts a new one, then racks the charging handle, back to ready"),
    "walk_rifle":   ("Walk_Loop", 8, "rifle", "walking while carrying an AK assault rifle at low ready with BOTH hands (right on the grip, left on the handguard, muzzle down-forward), a full realistic walk cycle"),
    "melee":        ("Punch_Cross", 6, "", "throwing a hard realistic right cross punch: wind-up, strike, recovery"),
    "melee_tool":   ("Sword_Attack", 8, "", "swinging a heavy hatchet with the right hand in an overhead chop: raise, strike down, recover"),
    "death":        ("Death01", 8, "", "being shot and collapsing realistically to the ground, ending lying motionless"),
}

STYLE = ("Gloomy post-Soviet survival, muted desaturated natural colours (olive, khaki, grey-brown), hand-painted like Project Zomboid "
         "and Rust, soft overcast light from the top-left, strong readable volume.")
HERO = ("the same muscular broad-shouldered man (athletic heavy build: wide shoulders and chest, thick neck and arms, narrow waist, "
        "strong legs, normal-sized boots), short dark hair, stubble, square jaw, wearing a worn quilted khaki telogreika jacket "
        "over a dark t-shirt, dark grey work trousers and brown boots")
REAL = ("REALISM IS THE PRIORITY: correct human anatomy and joint angles, believable weight and balance, feet actually touching "
        "the ground, natural hand grip on objects, consistent body size in every frame; upright confident posture (not hunched).")
FRAMING = ("A character animation sprite sheet for a 2D top-down survival game: a grid of {cols} columns and {rows} rows of equally "
           "spaced frames on a transparent background. Top row: the character facing the camera and to the right (front 3/4 view); "
           "bottom row: the same character facing away from the camera and to the right (back 3/4 view). Camera moderately above, "
           "about 30 degrees down (not top-down). Identical character, proportions, clothing and lighting in every frame, full body "
           "visible in every frame, feet on one baseline per row, frames in time order left to right. No text, no frame, no border, "
           "no ground, no cast shadow.")


def render(anim, clip, n, weapon):
    out = f"{TMP}/{anim}"
    if os.path.isdir(out) and "--render" not in sys.argv: return out
    env = dict(os.environ, CHAR_TILT="30")
    cmd = [BLENDER, "-b", "--python", os.path.join(ROOT, "scripts", "render-char3d.py"), "--", clip, str(n), out] + ([weapon] if weapon else [])
    subprocess.run(cmd, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True)
    return out


outfit_src = Image.open(os.path.join(ROOT, "assets/gen/charvar/hero30_idle.png")).convert("RGBA")
W, H = outfit_src.size
outfit = outfit_src.crop((0, 0, W // 4, H // 2))
outfit = outfit.crop(outfit.getchannel("A").point(lambda v: 255 if v > 60 else 0).getbbox())

assets = []
for anim, (clip, n, weapon, how) in ANIMS.items():
    d = render(anim, clip, n, weapon)
    gw, gh = n * C * 2, 2 * C * 2
    ref = Image.new("RGBA", (gw + 300, gh), (200, 200, 200, 255))
    for r, dd in enumerate((D_FRONT, D_BACK)):
        for i in range(n):
            f = Image.open(f"{d}/{clip}_d{dd}_f{i}.png").convert("RGBA").resize((C * 2, C * 2), Image.BOX)
            ref.alpha_composite(f, (i * C * 2, r * C * 2))
    k = (gh - 40) / outfit.height
    of = outfit.resize((int(outfit.width * k), gh - 40), Image.LANCZOS)
    ref.alpha_composite(of, (gw + (300 - of.width) // 2, 20))
    rp = f"assets/charref/anim_{anim}.png"
    ref.save(os.path.join(ROOT, rp))
    gun = {"pistol": " The grey box in the mannequin's hand marks where the pistol is.", "rifle": " The mannequin holds only a placeholder; paint a real AK held correctly with both hands as described."}.get(weapon, "")
    assets.append({"id": f"hero_{anim}", "cat": "charvar", "anim": [n, 2], "ref": rp,
                   "desc": f"{HERO}, {how}. The attached image has two parts. LEFT: a grey mannequin grid — use it ONLY as the pose, "
                           f"timing and camera reference, copy each frame's pose exactly.{gun} RIGHT: the painted character — copy his face, "
                           f"hair, clothes, boots and colours exactly; do not include the reference figure itself. {REAL} {STYLE}"})
    print("ok", anim, n)

json.dump({"anim_framing": FRAMING, "assets": assets}, open(os.path.join(ROOT, "assets/manifest_hero_anims.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print("manifest:", len(assets))
