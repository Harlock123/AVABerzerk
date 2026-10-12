#!/usr/bin/env python3
"""Turns a clean recording into a Berzerk robot: flat, buzzy and lo-fi.

The cabinet's speech board synthesised its words from a few parameters, which gave the robots their
droning, electrical voice. This pitches the voice down a little, multiplies it by a buzzing sawtooth
(ring modulation, the classic robot sound), cuts it down to 8 kHz and 5 bits, band-limits it like a
cabinet speaker and writes it at the game's 22050 Hz.
Usage: robot_voice.py in.wav out.wav
"""
import math
import struct
import sys
import wave

src, dst = sys.argv[1], sys.argv[2]
with wave.open(src, "rb") as w:
    rate = w.getframerate()
    raw = w.readframes(w.getnframes())
samples = [s / 32768 for s in struct.unpack("<%dh" % (len(raw) // 2), raw)]

OUT_RATE = 22050
PITCH = 0.9


def resample(data, ratio):
    out = []
    for k in range(int(len(data) / ratio)):
        x = k * ratio
        i = int(x)
        f = x - i
        a = data[i]
        b = data[i + 1] if i + 1 < len(data) else a
        out.append(a + (b - a) * f)
    return out


# Slightly deeper, at the game's rate.
voice = resample(samples, rate * PITCH / OUT_RATE)

# Ring-modulate with a 110 Hz buzz, mixed with some of the dry voice so the words stay clear.
buzzed = []
for i, s in enumerate(voice):
    saw = 2 * ((i * 110 / OUT_RATE) % 1) - 1
    buzzed.append(0.55 * s * saw * 2 + 0.45 * s)

# Down to 8 kHz and 5 bits, held, for the crunch.
step = OUT_RATE / 8000
held, nxt, crunched = 0.0, 0.0, []
peak = max(1e-9, max(abs(s) for s in buzzed))
for i, s in enumerate(buzzed):
    if i >= nxt:
        held = round(s / peak * 16) / 16
        nxt += step
    crunched.append(held)

# A small speaker: no deep bass, no sparkle.
hp, lp, prev, out = 0.0, 0.0, 0.0, []
for s in crunched:
    hp = 0.97 * (hp + s - prev)
    prev = s
    lp += 0.5 * (hp - lp)
    out.append(lp)

peak = max(1e-9, max(abs(s) for s in out))
start = next((i for i, s in enumerate(out) if abs(s) > peak * 0.03), 0)
end = len(out) - next((i for i, s in enumerate(reversed(out)) if abs(s) > peak * 0.03), 0)
out = out[max(0, start - 200):min(len(out), end + 500)]

with wave.open(dst, "wb") as w:
    w.setnchannels(1)
    w.setsampwidth(2)
    w.setframerate(OUT_RATE)
    w.writeframes(b"".join(struct.pack("<h", int(max(-1, min(1, s / peak * 0.9)) * 32767)) for s in out))
