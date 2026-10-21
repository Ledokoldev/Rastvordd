"""Дополнительные синтезированные звуки (взрыв, фитиль, щелчок замка) — без перезаписи остальных.
Запуск: python scripts/synth-extra.py"""
import importlib.util
import math
import pathlib

spec = importlib.util.spec_from_file_location("synth", pathlib.Path(__file__).with_name("synth-sfx.py"))
s = importlib.util.module_from_spec(spec)
spec.loader.exec_module(s)


def explosion(name: str, size: float) -> None:
    c = s.n(1.6 * size)
    boom = [x * e for x, e in zip(s.lowpass(s.noise(c), 380), s.env(c, 0.002, 0.45 * size))]
    body = [x * e for x, e in zip(s.lowpass(s.noise(c), 1400), s.env(c, 0.001, 0.12 * size))]
    crack = [x * e for x, e in zip(s.highpass(s.noise(s.n(0.08)), 2000), s.env(s.n(0.08), 0.0005, 0.02))]
    thump = s.sweep(s.n(0.6), 90, 28, 0.18 * size)
    # обломки: редкие щелчки после основного удара
    rattle = [0.0] * c
    for _ in range(int(40 * size)):
        at = s.rng.randint(s.n(0.15), c - s.n(0.03))
        ln = s.n(0.012)
        amp = s.rng.uniform(0.05, 0.25) * (1 - at / c)
        for i, x in enumerate(s.highpass(s.noise(ln), 1500)):
            rattle[at + i] += x * amp * (1 - i / ln)
    echo = [0.0] * s.n(0.14) + [x * 0.3 for x in s.lowpass(boom, 500)]
    s.save(name, s.mix([x * 1.3 for x in boom], body, [x * 0.8 for x in crack], [x * 1.4 for x in thump], rattle, echo))


def beep(name: str, freq: float, length: float) -> None:
    c = s.n(length)
    s.save(name, [((1 if (i * freq * 2 // s.SR) % 2 else -1) * 0.5) * s.env(c, 0.002, length / 3)[i] for i in range(c)], 0.5)


def fuse(name: str) -> None:
    # шипение фитиля: шум с «потрескиванием», кольцуемый
    c = s.n(1.0)
    hiss = s.highpass(s.noise(c), 3000)
    crackle = [0.0] * c
    for _ in range(60):
        at = s.rng.randint(0, c - 200)
        for i in range(200):
            crackle[at + i] += s.rng.uniform(-1, 1) * (1 - i / 200) * 0.8
    s.save(name, s.mix([x * 0.5 for x in hiss], crackle), 0.5)


def glass(name: str) -> None:
    # бьётся бутылка: звонкие высокие осколки + глухой удар
    c = s.n(0.5)
    shards = [0.0] * c
    for _ in range(26):
        at = s.rng.randint(0, s.n(0.25))
        f = s.rng.uniform(2500, 6500)
        ln = s.n(s.rng.uniform(0.03, 0.12))
        for i in range(min(ln, c - at)):
            shards[at + i] += s.math.sin(2 * s.math.pi * f * i / s.SR) * s.math.exp(-i / (ln * 0.3)) * s.rng.uniform(0.1, 0.4)
    thud = [x * e for x, e in zip(s.lowpass(s.noise(c), 600), s.env(c, 0.001, 0.04))]
    s.save(name, s.mix(shards, thud))


def whoosh(name: str) -> None:
    # вспыхнувшая горючка: нарастающий шум с низом
    c = s.n(1.2)
    w = [x * (min(1.0, i / s.n(0.15)) * s.math.exp(-max(0, i - s.n(0.15)) / s.n(0.5))) for i, x in enumerate(s.lowpass(s.noise(c), 1800))]
    s.save(name, w)


def water(name: str, length: float, bright: float, body: float) -> None:
    # вода: шум с «пузырями» — короткие синусные всплески с падающей частотой
    c = s.n(length)
    hiss = [x * e for x, e in zip(s.lowpass(s.highpass(s.noise(c), 300), bright), s.env(c, 0.02 * length, length / 3))]
    bubbles = [0.0] * c
    for _ in range(int(30 * length)):
        at = s.rng.randint(0, max(1, c - s.n(0.05)))
        f0 = s.rng.uniform(400, 1400)
        ln = s.n(s.rng.uniform(0.01, 0.04))
        for i in range(min(ln, c - at)):
            bubbles[at + i] += s.math.sin(2 * s.math.pi * f0 * (1 + i / ln) * i / s.SR) * (1 - i / ln) * 0.3
    thud = [x * e for x, e in zip(s.lowpass(s.noise(c), 250), s.env(c, 0.003, 0.08))]
    s.save(name, s.mix(hiss, bubbles, [x * body for x in thud]), 0.7)


def chime(name: str, notes: list, length: float, bright: float) -> None:
    # раскрытие ячейки: звоночек, чем реже вещь — тем больше нот и ярче
    c = s.n(length)
    out = [0.0] * c
    for k, f in enumerate(notes):
        start = s.n(0.06 * k)
        for i in range(c - start):
            e = math.exp(-i / (s.SR * length * 0.35))
            out[start + i] += (s.math.sin(2 * s.math.pi * f * i / s.SR) + bright * 0.3 * s.math.sin(2 * s.math.pi * f * 2.01 * i / s.SR)) * e * 0.4
    s.save(name, out, 0.6)


def rummage(name: str) -> None:
    # шорох обыска: мягкий шум с «хрустом»
    c = s.n(0.45)
    base = [x * e for x, e in zip(s.lowpass(s.highpass(s.noise(c), 400), 2500), s.env(c, 0.05, 0.15))]
    s.save(name, base, 0.45)


def main() -> None:
    glass("glass_break")
    whoosh("fire_whoosh")
    explosion("explosion_small", 0.8)
    explosion("explosion_big", 1.3)
    fuse("fuse_hiss")
    beep("c4_beep", 1800, 0.07)
    beep("lock_click", 900, 0.04)


if __name__ == "__main__":
    main()
