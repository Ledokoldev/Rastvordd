"""Анимация ходьбы через Wan 2.2 I2V (ComfyUI): кадр персонажа → видео «идёт на месте» → кадры → пиксельный цикл.

python scripts/anim-walk.py char_naked            — сгенерировать видео лицом и спиной и сделать кадры
python scripts/anim-walk.py char_naked --only-process   — только переобработать уже скачанные кадры

Результат: game/art/chars/<id>_walk_f0..N.png (лицом) и _walk_b0..N.png (спиной), 64×64, как остальные кадры.
Настройки скорости/качества — STEPS, LENGTH, W×H ниже (на RTX 4070 Ti ~2–3 мин на ролик при 20 шагах).
"""
import pathlib
import random
import sys
import time

from PIL import Image, ImageChops, ImageFilter

sys.path.insert(0, str(pathlib.Path(__file__).parent))
import comfy  # noqa: E402

ROOT = pathlib.Path(__file__).resolve().parent.parent
W, H = 384, 512
LENGTH = 29          # кадров (шаг 4): ~1,8 с при 16 к/с — хватает на 1–2 шага цикла
STEPS = 20           # 10 high-noise + 10 low-noise (в исходном воркфлоу пользователя 40)
GREEN = (0, 177, 64)
FRAME = 64
PX_PER_M = 32

POSITIVE = ("The same person walks in place on a treadmill with a steady natural walking cycle, legs stepping and arms swinging slightly, "
            "the body keeps exactly the same diagonal orientation as in the first frame for the whole video, the torso and head never rotate toward the camera, "
            "full body always visible, the person stays exactly in the center of the frame and does not turn, the camera is completely static, "
            "plain flat solid green background, no shadow, even lighting.")
NEGATIVE = ("camera movement, zoom, pan, walking out of frame, turning around, changing clothes, extra people, extra limbs, deformed, blurry, "
            "background details, text, watermark, 色调艳丽, 过曝, 静态, 细节模糊不清, 字幕, 风格, 作品, 画作, 画面, 静止, 整体发灰, 最差质量, 低质量, "
            "JPEG压缩残留, 丑陋的, 残缺的, 多余的手指, 画得不好的手部, 画得不好的脸部, 畸形的, 毁容的, 形态畸形的肢体, 手指融合, 静止不动的画面, 杂乱的背景, 三条腿, 背景人很多, 倒着走")


def prepare_input(sheet_id: str, row: int) -> pathlib.Path:
    """Кадр 0 нужного ряда листа персонажа на ровном зелёном фоне, с запасом сверху/снизу."""
    sheet = Image.open(ROOT / "assets" / "gen" / "chars" / f"{sheet_id}.png").convert("RGBA")
    cw, ch = sheet.width // 4, sheet.height // 2
    cell = sheet.crop((0, row * ch, cw, (row + 1) * ch))
    a = cell.getchannel("A").point(lambda v: 0 if v < 70 else 255)
    cell.putalpha(a)
    bbox = a.getbbox()
    fig = cell.crop(bbox)
    scale = (H * 0.78) / fig.height
    fig = fig.resize((round(fig.width * scale), round(fig.height * scale)), Image.LANCZOS)
    canvas = Image.new("RGB", (W, H), GREEN)
    canvas.paste(fig, ((W - fig.width) // 2, H - fig.height - round(H * 0.08)), fig)
    out = ROOT / "assets" / "gen" / "anim" / f"{sheet_id}_{'fb'[row]}_input.png"
    out.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(out)
    return out


def build_prompt(image_name: str, prefix: str) -> dict:
    seed = random.randint(0, 2**48)
    half = STEPS // 2
    return {
        "1": {"class_type": "LoadImage", "inputs": {"image": image_name}},
        "2": {"class_type": "UnetLoaderGGUF", "inputs": {"unet_name": "Wan2.2-I2V-A14B-HighNoise-Q4_K_S.gguf"}},
        "3": {"class_type": "UnetLoaderGGUF", "inputs": {"unet_name": "Wan2.2-I2V-A14B-LowNoise-Q4_0.gguf"}},
        "4": {"class_type": "CLIPLoader", "inputs": {"clip_name": "umt5_xxl_fp8_e4m3fn_scaled.safetensors", "type": "wan", "device": "default"}},
        "5": {"class_type": "VAELoader", "inputs": {"vae_name": "wan_2.1_vae.safetensors"}},
        "6": {"class_type": "CLIPTextEncode", "inputs": {"text": POSITIVE, "clip": ["4", 0]}},
        "7": {"class_type": "CLIPTextEncode", "inputs": {"text": NEGATIVE, "clip": ["4", 0]}},
        "8": {"class_type": "WanImageToVideo", "inputs": {"positive": ["6", 0], "negative": ["7", 0], "vae": ["5", 0], "start_image": ["1", 0],
                                                         "width": W, "height": H, "length": LENGTH, "batch_size": 1}},
        "10": {"class_type": "ModelSamplingSD3", "inputs": {"model": ["2", 0], "shift": 4}},
        "21": {"class_type": "ModelSamplingSD3", "inputs": {"model": ["3", 0], "shift": 4}},
        "11": {"class_type": "KSamplerAdvanced", "inputs": {"model": ["10", 0], "add_noise": "enable", "noise_seed": seed, "steps": STEPS, "cfg": 4,
                                                           "sampler_name": "euler", "scheduler": "simple", "positive": ["8", 0], "negative": ["8", 1],
                                                           "latent_image": ["8", 2], "start_at_step": 0, "end_at_step": half,
                                                           "return_with_leftover_noise": "enable"}},
        "12": {"class_type": "KSamplerAdvanced", "inputs": {"model": ["21", 0], "add_noise": "disable", "noise_seed": seed, "steps": STEPS, "cfg": 4,
                                                           "sampler_name": "euler", "scheduler": "simple", "positive": ["8", 0], "negative": ["8", 1],
                                                           "latent_image": ["11", 0], "start_at_step": half, "end_at_step": 10000,
                                                           "return_with_leftover_noise": "disable"}},
        "19": {"class_type": "VAEDecode", "inputs": {"samples": ["12", 0], "vae": ["5", 0]}},
        "30": {"class_type": "SaveImage", "inputs": {"images": ["19", 0], "filename_prefix": prefix}},
    }


def key_green(im: Image.Image) -> Image.Image:
    """Убираем зелёный фон: пиксели, где зелёный заметно сильнее красного и синего, — прозрачные."""
    im = im.convert("RGB")
    px = im.load()
    alpha = Image.new("L", im.size, 255)
    ap = alpha.load()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b = px[x, y]
            if g > 90 and g > r * 1.35 and g > b * 1.25:
                ap[x, y] = 0
            elif g > r * 1.12 and g > b * 1.08:          # кайма: снимаем зелёный оттенок
                px[x, y] = (r, min(g, max(r, b) + 8), b)
    alpha = alpha.filter(ImageFilter.MinFilter(3))
    out = im.convert("RGBA")
    out.putalpha(alpha)
    return out


def pick_cycle(frames: list, want: int = 6) -> list:
    """Один цикл шага: ищем кадр, наиболее похожий на первый (после хотя бы 6 кадров), и равномерно берём want кадров."""
    small = [f.convert("L").resize((48, 64)) for f in frames]

    def diff(a, b):
        h = ImageChops.difference(a, b).histogram()
        return sum(i * v for i, v in enumerate(h))

    start = 2   # первые кадры часто «разгоняются»
    # отбрасываем кадры, где верх тела (голова/плечи) сильно отличается от первого — значит, развернулся
    top = [f.convert("L").resize((48, 64)).crop((0, 0, 48, 28)) for f in frames]
    base = sum(i * v for i, v in enumerate(ImageChops.difference(top[start], top[start + 1]).histogram()))
    ok = [k for k in range(len(frames)) if k <= start or sum(i * v for i, v in enumerate(ImageChops.difference(top[start], top[k]).histogram())) < base * 6]
    if len(ok) >= want + 4:
        frames = [frames[k] for k in ok]
        small = [small[k] for k in ok]
    best, best_d = len(frames) - 1, float("inf")
    for j in range(start + 6, len(frames)):
        d = diff(small[start], small[j])
        if d < best_d:
            best, best_d = j, d
    span = best - start
    return [frames[start + round(i * span / want)] for i in range(want)]


def to_sprite(frames: list, h_m: float = 1.8) -> list:
    """Общая рамка на все кадры (без дрожи), масштаб 32 px/м, кадр 64×64, ступни на одной линии."""
    sys.path.insert(0, str(ROOT / "scripts"))
    import importlib.util
    spec = importlib.util.spec_from_file_location("pa", ROOT / "scripts" / "process-assets.py")
    pa = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(pa)
    boxes = [f.getchannel("A").point(lambda v: 255 if v > 8 else 0).getbbox() for f in frames]
    boxes = [b for b in boxes if b]
    union = (min(b[0] for b in boxes), min(b[1] for b in boxes), max(b[2] for b in boxes), max(b[3] for b in boxes))
    target_h = round(h_m * PX_PER_M)
    scale = target_h / (union[3] - union[1])
    out = []
    for f in frames:
        crop = f.crop(union)
        px = pa.pixelize(crop, (max(1, round(crop.width * scale)), target_h))
        frame = Image.new("RGBA", (FRAME, FRAME), (0, 0, 0, 0))
        frame.paste(px, ((FRAME - px.width) // 2, FRAME - target_h - 2), px)
        out.append(frame)
    return out


def main() -> None:
    sheet_id = sys.argv[1] if len(sys.argv) > 1 else "char_naked"
    only_process = "--only-process" in sys.argv
    rows = (0,) if "--front" in sys.argv else (1,) if "--back" in sys.argv else (0, 1)
    for row in rows:
        tag = "fb"[row]
        raw_dir = ROOT / "assets" / "gen" / "anim" / f"{sheet_id}_walk_{tag}"
        if not only_process:
            src = prepare_input(sheet_id, row)
            name = comfy.upload_image(str(src), src.name)
            t0 = time.time()
            pid = comfy.queue(build_prompt(name, f"rastvordd/{sheet_id}_walk_{tag}"))
            print(f"[{tag}] queued {pid}")
            entry = comfy.wait(pid)
            print(f"[{tag}] done in {time.time() - t0:.0f} s")
            for old in raw_dir.glob("*.png"):
                old.unlink()
            comfy.download_images(entry, raw_dir)
        files = sorted(raw_dir.glob("*.png"))
        frames = [key_green(Image.open(p)) for p in files]
        cycle = pick_cycle(frames)
        sprites = to_sprite(cycle)
        dst = ROOT / "game" / "art" / "chars"
        dst.mkdir(parents=True, exist_ok=True)
        for i, s in enumerate(sprites):
            s.save(dst / f"{sheet_id}_walk_{tag}{i}.png")
        strip = Image.new("RGBA", (FRAME * len(sprites), FRAME), (60, 64, 50, 255))
        for i, s in enumerate(sprites):
            strip.paste(s, (i * FRAME, 0), s)
        strip.resize((strip.width * 4, strip.height * 4), Image.NEAREST).save(ROOT / "assets" / f"preview_{sheet_id}_walk_{tag}.png")
        print(f"[{tag}] {len(frames)} кадров видео -> цикл {len(sprites)} кадров")


if __name__ == "__main__":
    main()
