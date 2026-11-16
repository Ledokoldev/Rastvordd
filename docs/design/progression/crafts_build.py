# Справочник крафтов «Раствордд» → crafts.html (2026-11-16).
# Данные — живые, из кода игры: --progdump (game/src/Sim/ProgDump.cs) пишет prog.json и prog.icons.json.
# Запуск (из корня репозитория):
#   <движок>_console.exe --headless --path game -- --progdump=<папка>/prog.json
#   python docs/design/progression/crafts_build.py <папка>/prog.json
# Иконки ужимаются до 48 px и вшиваются в страницу — файл самодостаточный, открывается без интернета.
import base64, io, json, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
src = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, 'prog.json')
d = json.load(open(src, encoding='utf-8'))
icons_path = src.replace('.json', '.icons.json')
icons = json.load(open(icons_path, encoding='utf-8')) if os.path.exists(icons_path) else {}

try:
    from PIL import Image
except ImportError:
    Image = None

def icon_b64(res_path):
    p = os.path.join(ROOT, 'game', res_path.replace('res://', ''))
    if not os.path.exists(p):
        return None
    if Image is None:
        return 'data:image/png;base64,' + base64.b64encode(open(p, 'rb').read()).decode()
    im = Image.open(p).convert('RGBA')
    im.thumbnail((48, 48), Image.LANCZOS)
    buf = io.BytesIO()
    im.save(buf, 'PNG', optimize=True)
    return 'data:image/png;base64,' + base64.b64encode(buf.getvalue()).decode()

ic = {}
for iid, rp in icons.items():
    if iid in d['items']:
        b = icon_b64(rp)
        if b:
            ic[iid] = b

data = {k: d[k] for k in ('items', 'recipes', 'tech', 'loot', 'recycle', 'gather', 'smelt', 'notes', 'build')}
data['icons'] = ic
page = open(os.path.join(HERE, 'crafts_page.html'), encoding='utf-8').read()
html = page.replace('/*__DATA__*/null', json.dumps(data, ensure_ascii=False, separators=(',', ':')))
out = os.path.join(HERE, 'crafts.html')
open(out, 'w', encoding='utf-8').write(html)
print(f'crafts.html: {len(html) // 1024} КБ, предметов {len(d["items"])}, рецептов {len(d["recipes"])}, иконок {len(ic)}')
