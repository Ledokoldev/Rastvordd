"""Синтез звуков-заглушек (без внешних библиотек) -> game/audio/*.wav (22050 Гц, моно, 16 бит).

Позже заменятся звуками из бесплатных паков (см. docs/roadmap.md). Запуск: python scripts/synth-sfx.py
"""
import math
import pathlib
import random
import struct
import wave

SR = 22050
OUT = pathlib.Path(__file__).resolve().parent.parent / "game" / "audio"
rng = random.Random(7)


def save(name: str, samples: list[float], gain: float = 0.9) -> None:
    peak = max(1e-6, max(abs(s) for s in samples))
    OUT.mkdir(parents=True, exist_ok=True)
    with wave.open(str(OUT / f"{name}.wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1, min(1, s / peak * gain)) * 32767)) for s in samples))
    print(f"{name:<16} {len(samples) / SR:.2f} c")


def n(sec: float) -> int:
    return int(sec * SR)


def lowpass(x: list[float], cutoff: float) -> list[float]:
    a = 1 - math.exp(-2 * math.pi * cutoff / SR)
    y, out = 0.0, []
    for s in x:
        y += a * (s - y)
        out.append(y)
    return out


def highpass(x: list[float], cutoff: float) -> list[float]:
    lp = lowpass(x, cutoff)
    return [s - l for s, l in zip(x, lp)]


def noise(count: int) -> list[float]:
    return [rng.uniform(-1, 1) for _ in range(count)]


def env(count: int, attack: float, decay: float) -> list[float]:
    a = max(1, n(attack))
    return [(i / a if i < a else math.exp(-(i - a) / (decay * SR))) for i in range(count)]


def mix(*tracks: list[float]) -> list[float]:
    length = max(len(t) for t in tracks)
    return [sum(t[i] for t in tracks if i < len(t)) for i in range(length)]


def sweep(count: int, f0: float, f1: float, decay: float) -> list[float]:
    out, ph = [], 0.0
    for i in range(count):
        t = i / count
        f = f0 + (f1 - f0) * t
        ph += 2 * math.pi * f / SR
        out.append(math.sin(ph) * math.exp(-i / (decay * SR)))
    return out


def gunshot(name: str, body: float, crack: float, tail: float) -> None:
    c = n(tail)
    bang = [s * e for s, e in zip(lowpass(noise(c), body), env(c, 0.001, tail / 5))]
    snap = [s * e for s, e in zip(highpass(noise(n(0.05)), 2500), env(n(0.05), 0.0005, 0.012))]
    thump = sweep(n(0.25), 140, 45, 0.06)
    # «эхо»: приглушённая копия с задержкой
    echo = [0.0] * n(0.09) + [s * 0.25 for s in lowpass(bang, 900)]
    save(name, mix(bang, [s * crack for s in snap], [s * 0.9 for s in thump], echo))


def thunk(name: str, freq: float, ring: float, bright: float) -> None:
    c = n(0.35)
    tone = [math.sin(2 * math.pi * freq * i / SR) * math.exp(-i / (ring * SR)) for i in range(c)]
    tone2 = [math.sin(2 * math.pi * freq * 2.7 * i / SR) * math.exp(-i / (ring * 0.4 * SR)) * 0.4 for i in range(c)]
    hit = [s * e for s, e in zip(lowpass(noise(c), bright), env(c, 0.0005, 0.03))]
    save(name, mix(tone, tone2, [s * 1.2 for s in hit]))


def step(name: str, bright: float, length: float) -> None:
    c = n(length)
    x = [s * e for s, e in zip(lowpass(highpass(noise(c), 200), bright), env(c, 0.004, length / 4))]
    save(name, x, 0.6)


def main() -> None:
    gunshot("shot_pipe", body=1800, crack=0.6, tail=0.45)
    gunshot("shot_ak", body=2600, crack=1.0, tail=0.6)

    # лук: короткий щипок струны (Karplus–Strong) + шорох
    period = int(SR / 110)
    buf = noise(period)
    out = []
    for i in range(n(0.4)):
        v = buf[i % period]
        buf[i % period] = 0.5 * (v + buf[(i + 1) % period]) * 0.993
        out.append(v)
    save("shot_bow", mix(out, [s * 0.3 * e for s, e in zip(highpass(noise(n(0.12)), 1500), env(n(0.12), 0.002, 0.03))]))

    thunk("hit_wood", freq=210, ring=0.08, bright=1800)
    thunk("hit_stone", freq=900, ring=0.05, bright=6000)
    thunk("hit_metal_ore", freq=1300, ring=0.12, bright=7000)

    # взмах: полоса шума с плавным подъёмом и спадом
    c = n(0.22)
    sw = lowpass(highpass(noise(c), 400), 2200)
    save("swing", [s * math.sin(math.pi * i / c) ** 2 for i, s in enumerate(sw)], 0.5)

    # попадание по человеку: глухой удар
    c = n(0.18)
    save("hit_flesh", mix(sweep(c, 180, 60, 0.04), [s * e * 0.6 for s, e in zip(lowpass(noise(c), 700), env(c, 0.001, 0.03))]))

    step("step_grass", bright=1800, length=0.09)
    step("step_gravel", bright=5000, length=0.11)

    # сбор/лут: шорох ткани
    c = n(0.3)
    save("loot", [s * (0.5 + 0.5 * math.sin(i / SR * 60)) * math.sin(math.pi * i / c) for i, s in enumerate(lowpass(highpass(noise(c), 900), 3500))], 0.5)

    # конец крафта: короткий двухнотный «дзинь»
    c = n(0.35)
    save("craft_done", mix(
        [math.sin(2 * math.pi * 660 * i / SR) * math.exp(-i / (0.08 * SR)) for i in range(c)],
        [0.0] * n(0.08) + [math.sin(2 * math.pi * 990 * i / SR) * math.exp(-i / (0.12 * SR)) for i in range(c)],
    ), 0.45)

    # печь: потрескивание огня, петля 3 с
    c = n(3.0)
    base = [s * 0.18 for s in lowpass(noise(c), 500)]
    for _ in range(70):
        at = rng.randrange(0, c - n(0.02))
        amp = rng.uniform(0.3, 1.0)
        for i in range(n(0.012)):
            base[at + i] += rng.uniform(-1, 1) * amp * math.exp(-i / (0.003 * SR))
    save("furnace_loop", base, 0.5)

    # ---------- фон (петли по 8 с) ----------
    L = n(8.0)

    def loopable(x: list[float], fade: int) -> list[float]:
        # склеиваем конец с началом, чтобы петля не щёлкала
        out = x[:len(x) - fade]
        for i in range(fade):
            k = i / fade
            out[i] = out[i] * k + x[len(x) - fade + i] * (1 - k)
        return out

    # ветер: выше и тоньше прибоя (полоса 500–1600 Гц) + еле слышный плавающий свист, порывы медленные и неровные
    LL = L + n(1)
    band = lowpass(highpass(noise(LL), 500), 1600)
    gust = [0.35 + 0.65 * (0.5 + 0.5 * math.sin(2 * math.pi * (i / SR) / 6.3)) * (0.6 + 0.4 * math.sin(2 * math.pi * (i / SR) / 2.1 + 2)) for i in range(LL)]
    whistle, ph = [], 0.0
    for i in range(LL):
        t = i / SR
        f = 780 + 160 * math.sin(2 * math.pi * t / 5.1) + 60 * math.sin(2 * math.pi * t / 1.7)
        ph += 2 * math.pi * f / SR
        whistle.append(math.sin(ph) * 0.08 * gust[i])
    save("amb_wind", loopable([b * g + w for b, g, w in zip(band, gust, whistle)], n(1)), 0.45)

    surf = lowpass(noise(L + n(1)), 900)
    waves = [max(0.0, math.sin(2 * math.pi * (i / SR) / 4.0)) ** 2 * 0.9 + 0.1 for i in range(L + n(1))]
    save("amb_surf", loopable([s * w for s, w in zip(surf, waves)], n(1)), 0.55)

    birds = [0.0] * L
    for _ in range(14):
        at = rng.randrange(0, L - n(0.4))
        f0 = rng.uniform(2200, 4200)
        for k in range(rng.randint(2, 5)):
            st = at + k * n(0.09)
            for i in range(n(0.07)):
                if st + i >= L:
                    break
                t = i / n(0.07)
                f = f0 * (1 + 0.3 * math.sin(math.pi * t))
                birds[st + i] += math.sin(2 * math.pi * f * i / SR) * math.sin(math.pi * t) * 0.6
    save("amb_birds", birds, 0.35)

    crick = [0.0] * L
    for i in range(L):
        t = i / SR
        pulse = 1.0 if (t * 14) % 1 < 0.45 and (t * 0.6) % 1 < 0.7 else 0.0
        crick[i] = math.sin(2 * math.pi * 4800 * t) * pulse
    save("amb_crickets", lowpass(crick, 6000), 0.25)


if __name__ == "__main__":    main()
