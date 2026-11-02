"""Generates synthetic test audio for the offline harness (no copyrighted music involved).

usage: python3 make_fixtures.py <output dir>
Needs numpy and ffmpeg (with libvorbis + libmp3lame).
"""
import os
import subprocess
import sys
import wave

import numpy as np


def write_wav(path, data, rate, bits=16):
    data = np.clip(data, -1, 1)
    if data.ndim == 1:
        data = data[:, None]
    with wave.open(path, "wb") as w:
        w.setnchannels(data.shape[1])
        w.setsampwidth(bits // 8)
        w.setframerate(rate)
        if bits == 16:
            w.writeframes((data * 32767).astype("<i2").tobytes())
        elif bits == 24:
            ints = (data * 8388607).astype("<i4").reshape(-1)
            b = ints.view(np.uint8).reshape(-1, 4)[:, :3]
            w.writeframes(b.tobytes())


def song(rate=44100, seconds=8.0):
    """Kick + saw bass + minor chord pad at 120 BPM. First second is near silent (start-tag test)."""
    t = np.arange(int(rate * seconds)) / rate
    out = np.zeros_like(t)
    beat = 0.5
    for k in np.arange(1.0, seconds, beat):
        tt = t - k
        m = (tt >= 0) & (tt < 0.4)
        f = 45 + 110 * np.exp(-tt[m] / 0.03)
        ph = np.cumsum(f) / rate
        out[m] += np.sin(2 * np.pi * ph) * np.exp(-tt[m] / 0.18) * 0.6
    bass = 2 * ((55 * t) % 1.0) - 1
    out += bass * 0.15 * (t >= 1.0)
    for f in (220.0, 261.63, 329.63):
        out += np.sin(2 * np.pi * f * t) * 0.07 * (t >= 1.0)
    left = out + np.sin(2 * np.pi * 880 * t) * 0.02
    right = out
    return np.stack([left, right], axis=1) * 0.8


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)

    s = song()
    base = os.path.join(out, "base.wav")
    write_wav(base, s, 44100)

    ogg = os.path.join(out, "Test Artist - Ogg Song [start=0:01] [gain=-2].ogg")
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", base, "-c:a", "libvorbis", "-q:a", "5",
                    "-metadata", "artist=Tagged Artist", "-metadata", "title=Tagged Title", ogg], check=True)

    mp3 = os.path.join(out, "untitled_track (Official Audio).mp3")
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", base, "-ac", "1", "-ar", "22050",
                    "-c:a", "libmp3lame", "-b:a", "128k", "-id3v2_version", "3",
                    "-metadata", "artist=DM DOKURO", "-metadata", "title=Mp3 Tag Title", mp3], check=True)

    # 96 kHz / 24-bit: 1 kHz wanted tone + 30 kHz tone that must NOT alias into the audible band.
    t = np.arange(96000 * 2) / 96000
    tones = 0.4 * np.sin(2 * np.pi * 1000 * t) + 0.4 * np.sin(2 * np.pi * 30000 * t)
    write_wav(os.path.join(out, "hires_96k.wav"), np.stack([tones, tones], 1), 96000, bits=24)

    # Pure 1 kHz sine at 44.1 kHz for resampler SNR.
    t = np.arange(44100 * 2) / 44100
    sine = 0.5 * np.sin(2 * np.pi * 1000 * t)
    write_wav(os.path.join(out, "sine_44k.wav"), np.stack([sine, sine], 1), 44100)
    print("fixtures written to", out)


if __name__ == "__main__":
    main()
