"""Procedural locker / analog-TV SFX for DEFRAG (no external Python packages).

Usage: python Tools/Claude/gen_locker_sfx.py <output folder>
Writes 44.1 kHz mono 16-bit WAV files. Deterministic (fixed seeds) so re-runs match.
"""
import math
import os
import random
import struct
import sys
import wave

SR = 44100


def write_wav(path, samples, peak=0.89):
    m = max(1e-9, max(abs(s) for s in samples))
    gain = peak / m
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1.0, min(1.0, s * gain)) * 32767)) for s in samples))
    print("wrote", path, f"{len(samples) / SR:.2f}s")


class Resonator:
    """Two-pole resonant band-pass (modal mode)."""

    def __init__(self, freq, decay_s, gain=1.0):
        r = math.exp(-1.0 / (decay_s * SR))
        self.a1 = -2 * r * math.cos(2 * math.pi * freq / SR)
        self.a2 = r * r
        self.g = gain * (1 - r)
        self.y1 = self.y2 = 0.0

    def tick(self, x):
        y = self.g * x - self.a1 * self.y1 - self.a2 * self.y2
        self.y2, self.y1 = self.y1, y
        return y


def one_pole_lp(samples, cutoff):
    a = math.exp(-2 * math.pi * cutoff / SR)
    y, out = 0.0, []
    for s in samples:
        y = (1 - a) * s + a * y
        out.append(y)
    return out


def envelope(n, attack, release, total):
    out = []
    for i in range(n):
        t = i / SR
        e = min(1.0, t / max(1e-4, attack))
        if t > total - release:
            e *= max(0.0, (total - t) / max(1e-4, release))
        out.append(e)
    return out


def creak(duration, rate_start, rate_end, modes, seed, pitch_jitter=0.35, roughness=0.5):
    """Stick-slip hinge: irregular impulse train exciting metallic modes."""
    rnd = random.Random(seed)
    n = int(duration * SR)
    res = [Resonator(f * (1 + rnd.uniform(-0.01, 0.01)), d, g) for f, d, g in modes]
    out = [0.0] * n
    next_slip = 0.0
    t = 0.0
    for i in range(n):
        t = i / SR
        p = t / duration
        rate = rate_start + (rate_end - rate_start) * (p * p * (3 - 2 * p))
        rate *= 1 + 0.25 * math.sin(2 * math.pi * 3.1 * t + 1.3) + 0.15 * math.sin(2 * math.pi * 7.7 * t)
        x = 0.0
        if t >= next_slip:
            x = rnd.uniform(0.6, 1.0) * (1 if rnd.random() > 0.15 else -0.6)
            next_slip = t + (1.0 / max(5.0, rate)) * (1 + rnd.uniform(-pitch_jitter, pitch_jitter))
        x += rnd.uniform(-1, 1) * 0.02 * roughness
        out[i] = sum(r.tick(x) for r in res)
    env = envelope(n, 0.03, 0.12, duration)
    return [s * e for s, e in zip(out, env)]


def impact(duration, modes, noise_decay, seed, noise_gain=0.8, lp=6000):
    rnd = random.Random(seed)
    n = int(duration * SR)
    res = [Resonator(f, d, g) for f, d, g in modes]
    noise = [rnd.uniform(-1, 1) * math.exp(-(i / SR) / noise_decay) for i in range(n)]
    noise = one_pole_lp(noise, lp)
    out = []
    for i in range(n):
        exc = 1.0 if i < 24 else 0.0
        out.append(sum(r.tick(exc * 40) for r in res) + noise[i] * noise_gain)
    return out


def mix(*tracks, offsets=None):
    offsets = offsets or [0.0] * len(tracks)
    n = max(int(o * SR) + len(t) for t, o in zip(tracks, offsets))
    out = [0.0] * n
    for t, o in zip(tracks, offsets):
        k = int(o * SR)
        for i, s in enumerate(t):
            out[k + i] += s
    return out


LOCKER_MODES = [(412, 0.05, 1.0), (1030, 0.04, 0.8), (1870, 0.03, 0.6), (2960, 0.02, 0.45), (4410, 0.015, 0.3)]
DOOR_BANG_MODES = [(92, 0.55, 1.0), (151, 0.45, 0.8), (233, 0.35, 0.6), (517, 0.25, 0.45),
                   (873, 0.2, 0.35), (1402, 0.12, 0.3), (2210, 0.08, 0.2), (3650, 0.05, 0.12)]


def tv_static(duration, seed, flutter=11.0, whine=True):
    rnd = random.Random(seed)
    n = int(duration * SR)
    hp_prev_x = hp_y = 0.0
    out = []
    for i in range(n):
        t = i / SR
        x = rnd.uniform(-1, 1)
        # crude high-pass so the hiss sits above the voice
        hp_y = 0.97 * (hp_y + x - hp_prev_x)
        hp_prev_x = x
        am = 0.55 + 0.45 * math.sin(2 * math.pi * flutter * t + 2 * math.sin(2 * math.pi * 0.7 * t))
        crackle = (rnd.uniform(-1, 1) * 3.0) if rnd.random() < 0.0009 else 0.0
        s = hp_y * am * 0.6 + crackle
        if whine:
            s += 0.05 * math.sin(2 * math.pi * 15734 * t)
        out.append(s)
    env = envelope(n, 0.01, 0.15, duration)
    return [s * e for s, e in zip(out, env)]


def heartbeat(seed):
    """Single lub-dub, 1.0 s long, so playback pitch/rate can follow tension."""
    n = int(1.0 * SR)
    out = [0.0] * n
    for start, amp, f0 in ((0.0, 1.0, 58.0), (0.23, 0.7, 64.0)):
        k = int(start * SR)
        for i in range(int(0.22 * SR)):
            t = i / SR
            f = f0 * (1.4 - 0.4 * min(1.0, t / 0.05))
            s = math.sin(2 * math.pi * f * t) * math.exp(-t / 0.055) * amp
            s += math.sin(2 * math.pi * f * 2.02 * t) * math.exp(-t / 0.03) * amp * 0.25
            out[k + i] += s
    return one_pole_lp(out, 900)


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else "."
    os.makedirs(out_dir, exist_ok=True)
    p = lambda name: os.path.join(out_dir, name)

    # Opening: slow rising hinge squeal, then the door swings free.
    write_wav(p("Locker_CreakOpen_01.wav"), creak(1.15, 70, 430, LOCKER_MODES, seed=11, pitch_jitter=0.12))
    write_wav(p("Locker_CreakOpen_02.wav"), creak(0.95, 190, 310, LOCKER_MODES, seed=23, pitch_jitter=0.1))
    # Closing: short falling creak + latch click and thud.
    close_creak = creak(0.7, 360, 70, LOCKER_MODES, seed=37, pitch_jitter=0.12)
    latch = impact(0.35, [(2400, 0.02, 0.6), (3900, 0.015, 0.4), (180, 0.08, 0.5)], 0.01, seed=5, noise_gain=0.4)
    write_wav(p("Locker_CreakClose_01.wav"), mix(close_creak, latch, offsets=[0.0, 0.66]))
    close_creak2 = creak(0.55, 280, 60, LOCKER_MODES, seed=41, pitch_jitter=0.15)
    write_wav(p("Locker_CreakClose_02.wav"), mix(close_creak2, latch, offsets=[0.0, 0.5]))
    # Forced open: violent bang, fast shriek of the hinge, door hitting the side panel.
    bang = impact(1.6, DOOR_BANG_MODES, 0.08, seed=77, noise_gain=1.4)
    shriek = creak(0.35, 620, 380, LOCKER_MODES, seed=91, pitch_jitter=0.08)
    rebound = impact(0.9, DOOR_BANG_MODES[2:], 0.05, seed=78, noise_gain=0.6)
    write_wav(p("Locker_ForcedOpen.wav"), mix(bang, shriek, [s * 0.6 for s in rebound], offsets=[0.0, 0.02, 0.24]))
    # Monster claws / knocks on the locker while lurking.
    knock = impact(0.6, DOOR_BANG_MODES[1:6], 0.03, seed=55, noise_gain=0.5, lp=3500)
    write_wav(p("Locker_Knock.wav"), mix(knock, [s * 0.7 for s in knock], offsets=[0.0, 0.32]))
    # Analog TV presentation.
    write_wav(p("TvStatic_Burst_Short.wav"), tv_static(0.55, seed=3), peak=0.7)
    write_wav(p("TvStatic_Burst_Long.wav"), tv_static(1.4, seed=4, flutter=6.0), peak=0.7)
    write_wav(p("Heartbeat_Single.wav"), heartbeat(seed=1), peak=0.95)


if __name__ == "__main__":
    main()
