"""
Сборка своих моделей: каждая — scripts/blender/models/<имя>.py с функцией build(k), где k — soviet_kit.
Результат: game/art3d/own/<имя>.glb и превью assets/3d-preview/<имя>.png.

Запуск (из корня репозитория):
  tools/blender/blender-5.2.4-windows-x64/blender.exe -b -P scripts/blender/build.py -- [имя …] [--nopreview]
"""
import importlib.util
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "models"))   # общие части моделей (_crate.py)
import soviet_kit as k  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
preview = "--nopreview" not in args
names = [a for a in args if not a.startswith("--")]
model_dir = os.path.join(HERE, "models")
if not names:
    names = sorted(f[:-3] for f in os.listdir(model_dir) if f.endswith(".py") and not f.startswith("_"))

out_dir = os.path.join(k.ROOT, "game", "art3d", "own")
prev_dir = os.path.join(k.ROOT, "assets", "3d-preview")
os.makedirs(prev_dir, exist_ok=True)
for name in names:
    spec = importlib.util.spec_from_file_location(name, os.path.join(model_dir, name + ".py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    k.reset()
    mod.build(k)
    path, tris = k.export(name, out_dir)
    print(f"МОДЕЛЬ {name}: {tris} тр., {os.path.getsize(path) // 1024} КБ → {path}")
    if preview:
        k.render_preview(os.path.join(prev_dir, name + ".png"))
        print(f"ПРЕВЬЮ {name}")
