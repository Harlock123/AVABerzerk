#!/usr/bin/env python3
"""Synthesizes the game's sound effects into ../Sounds as 16-bit mono WAV files.

Nothing is sampled from the original machine: every effect is built here from oscillators and noise,
so the sounds can be tweaked and regenerated. Run it from anywhere: python3 tools/make_sounds.py
(The robots' voices come from tools/make_voice.sh.)
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Sounds")
random.seed(1980)


def write(name, samples, volume=0.6):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def n(seconds):
    return int(RATE * seconds)


def env(i, total, attack=0.005, release=1.0):
    """Linear attack, then a decay shaped by `release` (higher = faster fade)."""
    t = i / RATE
    a = min(1.0, t / attack) if attack > 0 else 1.0
    return a * (1 - i / total) ** release


def sweep(seconds, f0, f1, wave_fn, release=1.0, attack=0.005):
    total, phase, out = n(seconds), 0.0, []
    for i in range(total):
        f = f0 + (f1 - f0) * i / total
        phase += f / RATE
        out.append(wave_fn(phase) * env(i, total, attack=attack, release=release))
    return out


def square(p):
    return 1.0 if (p % 1) < 0.5 else -1.0


def tri(p):
    return 4 * abs((p % 1) - 0.5) - 1


def saw(p):
    return 2 * (p % 1) - 1


def sine(p):
    return math.sin(2 * math.pi * p)


def noise(seconds, release=1.0, smooth=0.0, attack=0.002):
    total, last, out = n(seconds), 0.0, []
    for i in range(total):
        last = last * smooth + random.uniform(-1, 1) * (1 - smooth)
        out.append(last * env(i, total, attack=attack, release=release))
    return out


def mix(*tracks):
    length = max(len(t) for t in tracks)
    return [sum(t[i] for t in tracks if i < len(t)) for i in range(length)]


def scale(track, k):
    return [s * k for s in track]


def silence(seconds):
    return [0.0] * n(seconds)


os.makedirs(OUT, exist_ok=True)

# The humanoid's gun: a sharp, rising-then-falling zap.
write("Shot.wav", mix(sweep(0.06, 600, 1800, square, release=0.3), sweep(0.12, 1800, 400, square, release=1.4)), 0.25)

# A robot's shot: a lower, harsher buzz.
write("RobotShot.wav", sweep(0.18, 500, 220, saw, release=1.2), 0.25)

# A robot blowing apart: a crackling burst.
write("RobotBoom.wav", mix(noise(0.45, release=2.2, smooth=0.2), scale(sweep(0.45, 400, 80, square, release=2), 0.5)), 0.55)

# Electrocution: a crackling, buzzing jolt that fades.
total = n(1.5)
zap = []
for i in range(total):
    t = i / RATE
    buzz = square(120 * t) * (0.6 + 0.4 * math.sin(2 * math.pi * 23 * t))
    crackle = random.uniform(-1, 1) if random.random() < 0.3 else 0.0
    zap.append((buzz * 0.7 + crackle * 0.6) * env(i, total, attack=0.005, release=0.9))
write("Zap.wav", zap, 0.6)

# Walking out of a room: a quick descending swoop.
write("Exit.wav", sweep(0.35, 900, 200, tri, release=1.0), 0.3)

# The room cleared: a cheerful arpeggio.
notes = []
for f in (523, 659, 784, 1047):
    notes += sweep(0.07, f, f, square, release=0.3)
write("Bonus.wav", notes, 0.28)

# Evil Otto appears: a falling "boing".
write("Otto.wav", sweep(0.5, 600, 150, tri, release=0.8), 0.4)

# Otto bouncing: a springy boing, one bounce per loop.
total = n(0.52)
boing, phase = [], 0.0
for i in range(total):
    t = i / total
    f = 180 + 260 * (1 - t) ** 3
    phase += f / RATE
    boing.append(sine(phase) * (1 - t) ** 1.5)
write("OttoBounce.wav", boing, 0.35)

write("ExtraLife.wav", sweep(0.08, 784, 784, square, release=0.3) + sweep(0.08, 1047, 1047, square, release=0.3)
      + sweep(0.08, 1319, 1319, square, release=0.3) + sweep(0.25, 1568, 1568, square, release=1), 0.3)

write("WallHit.wav", noise(0.08, release=3, smooth=0.5), 0.3)

print("Wrote sounds to", os.path.normpath(OUT))
