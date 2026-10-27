# Поставить карты нормалей от Codex (assets/gen/normals/<id>_n.png) в игру: подогнать под рамку спрайта, обрезать по его
# прозрачности, старую нормаль — в D:/Archive/normals_before_codex. Запуск: python scripts/install-normals.py assets/manifest_normals2.json
import json, os, shutil, sys
from PIL import Image
m = json.load(open(sys.argv[1], encoding="utf-8"))
os.makedirs("D:/Archive/normals_before_codex", exist_ok=True)
n = 0
for a in m["assets"]:
    b = a["id"][:-2]; d = a.get("dir")
    src = f"assets/gen/normals/{a['id']}.png"
    if not d or not os.path.exists(src): continue
    spr = Image.open(f"game/art/{d}/{b}.png").convert("RGBA")
    dst = f"game/art/{d}/{b}_n.png"
    bak = f"D:/Archive/normals_before_codex/{b}_n.png"
    if os.path.exists(dst) and not os.path.exists(bak): shutil.copy(dst, bak)
    gen = Image.open(src).convert("RGBA")
    gb = gen.getchannel("A").point(lambda v: 255 if v > 40 else 0).getbbox()
    sb = spr.getchannel("A").getbbox()
    if not gb or not sb: continue
    nm = gen.crop(gb).resize((sb[2] - sb[0], sb[3] - sb[1]), Image.LANCZOS)
    out = Image.new("RGBA", spr.size, (128, 128, 255, 0)); out.paste(nm, (sb[0], sb[1]))
    px = out.load(); al = spr.getchannel("A").load()
    for y in range(spr.height):
        for x in range(spr.width):
            r, g, bb, a2 = px[x, y]
            px[x, y] = (128, 128, 255, 0) if al[x, y] == 0 else ((128, 128, 255, al[x, y]) if a2 < 128 else (r, g, bb, al[x, y]))
    out.save(dst); n += 1
print("installed", n)
