"""Procedural device SFX (camera raise / power-on / lower, terminal dive + CRT on/off).

Usage: python Tools/Claude/gen_device_sfx.py <output folder>
Reuses the helpers from gen_locker_sfx.py. Deterministic (fixed seeds).
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from gen_locker_sfx import SR, write_wav, one_pole_lp, envelope, mix, impact  # noqa: E402


def noise(duration, seed):
    rnd = random.Random(seed)
    return [rnd.uniform(-1, 1) for _ in range(int(duration * SR))]


def band(samples, low, high):
    """Crude band-pass: low-pass at `high` minus low-pass at `low`."""
    hi = one_pole_lp(samples, high)
    lo = one_pole_lp(samples, low)
    return [a - b for a, b in zip(hi, lo)]


def swept_lp(samples, start_cutoff, end_cutoff):
    out, y = [], 0.0
    n = len(samples)
    for i, s in enumerate(samples):
        cutoff = start_cutoff * (end_cutoff / start_cutoff) ** (i / max(1, n - 1))
        a = math.exp(-2 * math.pi * cutoff / SR)
        y = (1 - a) * s + a * y
        out.append(y)
    return out


def tone(duration, f0, f1, shape="sine", amp=1.0, attack=0.005, release=0.03):
    n = int(duration * SR)
    out, phase = [], 0.0
    for i in range(n):
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        phase += 2 * math.pi * f / SR
        if shape == "square":
            v = 1.0 if math.sin(phase) >= 0 else -1.0
            v = 0.6 * v + 0.4 * math.sin(phase)
        else:
            v = math.sin(phase)
        out.append(v * amp)
    env = envelope(n, attack, release, duration)
    return [s * e for s, e in zip(out, env)]


def shaped(samples, attack, release):
    return [s * e for s, e in zip(samples, envelope(len(samples), attack, release, len(samples) / SR))]


def rustle(duration, seed, gain=1.0):
    rnd = random.Random(seed)
    base = band(noise(duration, seed), 700, 5000)
    n = len(base)
    out = []
    grain = 0.0
    for i, s in enumerate(base):
        if rnd.random() < 0.004:
            grain = rnd.uniform(0.4, 1.0)
        grain *= 0.9993
        swell = math.sin(math.pi * i / n) ** 1.5
        out.append(s * (0.35 + grain) * swell * gain)
    return out


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else "."
    os.makedirs(out_dir, exist_ok=True)
    p = lambda name: os.path.join(out_dir, name)

    tick = impact(0.12, [(3100, 0.012, 0.6), (5200, 0.008, 0.4)], 0.004, seed=8, noise_gain=0.3)
    write_wav(p("Camera_Raise.wav"), mix(rustle(0.42, 3), [s * 0.5 for s in tick], [s * 0.35 for s in tick],
                                         offsets=[0.0, 0.08, 0.3]), peak=0.7)

    whir = shaped(tone(0.26, 280, 920, "square", 0.35), 0.02, 0.05)
    whir = one_pole_lp(whir, 2500)
    beep1 = tone(0.06, 1320, 1320, "square", 0.5)
    beep2 = tone(0.09, 1980, 1980, "square", 0.5)
    write_wav(p("Camera_PowerOn.wav"), mix(whir, beep1, beep2, offsets=[0.0, 0.3, 0.38]), peak=0.6)

    blip = tone(0.09, 1500, 650, "square", 0.45)
    write_wav(p("Camera_Lower.wav"), mix(rustle(0.36, 5, 0.9), blip, offsets=[0.1, 0.0]), peak=0.65)

    whoosh = shaped(swept_lp(noise(0.62, 11), 250, 9000), 0.3, 0.06)
    chirp = shaped(tone(0.6, 180, 1700, "sine", 0.35), 0.2, 0.05)
    write_wav(p("Terminal_DiveIn.wav"), mix(whoosh, chirp), peak=0.75)

    thump = [math.sin(2 * math.pi * 62 * (i / SR)) * math.exp(-(i / SR) / 0.09) for i in range(int(0.4 * SR))]
    static = shaped(band(noise(0.32, 21), 1500, 12000), 0.005, 0.2)
    whine = shaped(tone(0.4, 15734, 15734, "sine", 0.06), 0.08, 0.15)
    write_wav(p("Terminal_CrtOn.wav"), mix(thump, [s * 0.7 for s in static], whine), peak=0.75)

    zap = shaped(tone(0.26, 2200, 70, "square", 0.45), 0.003, 0.08)
    click = impact(0.1, [(2600, 0.01, 0.5)], 0.003, seed=31, noise_gain=0.4)
    static_off = shaped(band(noise(0.12, 33), 1500, 11000), 0.003, 0.08)
    write_wav(p("Terminal_CrtOff.wav"), mix(zap, click, [s * 0.5 for s in static_off], offsets=[0.0, 0.0, 0.2]), peak=0.75)


if __name__ == "__main__":
    main()
