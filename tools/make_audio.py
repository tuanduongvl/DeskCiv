"""Turns downloaded recordings into DeskCiv audio assets (.dcsnd = Ogg Vorbis).
Usage: make_audio.py <download dir> <game/assets/audio dir> <ffmpeg>"""
import subprocess
import sys
from pathlib import Path

import numpy as np

src, out, ffmpeg = Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3]
out.mkdir(parents=True, exist_ok=True)
RATE = 44100


def decode(path, start=0.0, length=None, channels=1):
    cmd = [ffmpeg, "-hide_banner", "-loglevel", "error", "-ss", str(start), "-i", str(path)]
    if length:
        cmd += ["-t", str(length)]
    cmd += ["-ac", str(channels), "-ar", str(RATE), "-f", "f32le", "-"]
    data = np.frombuffer(subprocess.run(cmd, capture_output=True, check=True).stdout, dtype=np.float32)
    return data.reshape(-1, channels)


WAVS = src / "wav"
WAVS.mkdir(exist_ok=True)


def encode(samples, name, lufs, kbps):
    """Writes a loudness-normalised WAV; blender_ogg.py turns it into Ogg Vorbis at kbps."""
    raw = samples.astype(np.float32).tobytes()
    channels = samples.shape[1]
    subprocess.run([ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-f", "f32le", "-ar", str(RATE), "-ac", str(channels), "-i", "-",
                    "-af", f"loudnorm=I={lufs}:TP=-3:LRA=11", "-ar", str(RATE), "-c:a", "pcm_s16le",
                    str(WAVS / f"{name}__{kbps}.wav")], input=raw, check=True)
    print(f"{name}: {len(samples) / RATE:.1f}s")


def seamless(samples, fade_seconds):
    """Crossfades the tail into the head so the clip loops without a click."""
    fade = int(fade_seconds * RATE)
    body = samples[:-fade].copy()
    t = np.linspace(0, 1, fade, dtype=np.float32)[:, None]
    body[:fade] = samples[-fade:] * np.cos(t * np.pi / 2) + samples[:fade] * np.sin(t * np.pi / 2)
    return body


def one_shot(samples, seconds):
    """The loudest stretch of a recording, with soft edges."""
    window = int(seconds * RATE)
    if len(samples) > window:
        energy = np.convolve((samples[:, 0] ** 2), np.ones(RATE) / RATE, mode="valid")
        steps = range(0, len(samples) - window, RATE // 2)
        best = max(steps, key=lambda s: energy[s:s + window - RATE].mean())
        samples = samples[best:best + window]
    edge = int(0.8 * RATE)
    ramp = np.linspace(0, 1, edge, dtype=np.float32)[:, None]
    samples = samples.copy()
    samples[:edge] *= ramp
    samples[-edge:] *= ramp[::-1]
    return samples


amb = src / "amb"
encode(seamless(decode(amb / "surf_a.wav", 0.2, channels=2), 4), "surf", -27, 96)
encode(seamless(decode(amb / "wind_a.wav", 0.5, channels=2), 5), "wind", -31, 80)
encode(seamless(decode(amb / "rain_a.mp3", 3.0, channels=2), 5), "rain", -27, 96)
for i, name in enumerate(["bird_blackbird", "bird_robin", "bird_skylark"], 1):
    encode(one_shot(decode(amb / f"{name}.mp3"), 16), f"bird_{i}", -32, 64)
for i, name in enumerate(["gull_herring", "gull_blackheaded"], 1):
    encode(one_shot(decode(amb / f"{name}.mp3"), 10), f"gull_{i}", -33, 64)

music = ["The Sky of our Ancestors", "Truth in the Stones", "Ascending the Vale", "Teller of the Tales", "Angevin",
         "Canon in D for Two Renaissance Harps", "Devonshire Waltz Andante", "Local Forecast - Slower"]
for era, title in enumerate(music):
    track = decode(src / "music" / f"{title}.mp3", channels=2)
    limit = 5 * 60 * RATE  # at most five minutes, faded out
    if len(track) > limit:
        track = track[:limit].copy()
        fade = 8 * RATE
        track[-fade:] *= np.linspace(1, 0, fade, dtype=np.float32)[:, None]
    encode(track, f"music_{era}", -24, 96)
