"""Дополнительные синтезированные звуки (взрыв, фитиль, щелчок замка) — без перезаписи остальных.
Запуск: python scripts/synth-extra.py"""
import importlib.util
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


def main() -> None:
    explosion("explosion_small", 0.8)
    explosion("explosion_big", 1.3)
    fuse("fuse_hiss")
    beep("c4_beep", 1800, 0.07)
    beep("lock_click", 900, 0.04)


if __name__ == "__main__":
    main()
