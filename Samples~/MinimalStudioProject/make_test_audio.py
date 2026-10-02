#!/usr/bin/env python3
"""Writes the placeholder WAV files used by CreateMinimalProject.js (Python 3 standard library only).

Usage: python3 make_test_audio.py <FMOD Studio project folder>
Creates <folder>/SourceAudio/*.wav (short synthetic tones and noise; replace them with real sounds later).
"""
import math
import os
import random
import struct
import sys
import wave


def write(path, seconds, fn, rate=48000):
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        frames = bytearray()
        for i in range(int(seconds * rate)):
            v = max(-1.0, min(1.0, fn(i / rate, seconds)))
            frames += struct.pack("<h", int(v * 20000))
        w.writeframes(bytes(frames))


def main():
    folder = os.path.join(sys.argv[1] if len(sys.argv) > 1 else ".", "SourceAudio")
    os.makedirs(folder, exist_ok=True)
    random.seed(1)
    write(os.path.join(folder, "click.wav"), 0.08, lambda t, d: math.sin(2 * math.pi * 1800 * t) * (1 - t / d))
    write(os.path.join(folder, "explosion.wav"), 0.8, lambda t, d: (random.random() * 2 - 1) * math.exp(-5 * t))
    write(os.path.join(folder, "footstep.wav"), 0.12, lambda t, d: (random.random() * 2 - 1) * math.exp(-30 * t))
    write(os.path.join(folder, "engine.wav"), 1.0, lambda t, d: 0.6 * math.sin(2 * math.pi * 110 * t) + 0.3 * math.sin(2 * math.pi * 220 * t))
    write(os.path.join(folder, "music.wav"), 4.0, lambda t, d: 0.3 * sum(math.sin(2 * math.pi * f * t) for f in (261.6, 329.6, 392.0)))
    write(os.path.join(folder, "wind.wav"), 2.0, lambda t, d: (random.random() * 2 - 1) * 0.3)
    print("Wrote test audio to " + folder)


if __name__ == "__main__":
    main()
