"""Run: blender -b --factory-startup --python blender_ogg.py -- <wav dir> <out dir>
Encodes NAME__KBPS.wav files to NAME.dcsnd (Ogg Vorbis) with Blender's audaspace."""
import sys
from pathlib import Path

import aud

wav_dir, out_dir = (Path(p) for p in sys.argv[sys.argv.index("--") + 1:][:2])
for wav in sorted(wav_dir.glob("*.wav")):
    name, kbps = wav.stem.rsplit("__", 1)
    sound = aud.Sound(str(wav))
    target = out_dir / f"{name}.dcsnd"
    sound.write(str(target), rate=44100, channels=aud.CHANNELS_STEREO, format=aud.FORMAT_FLOAT32,
                container=aud.CONTAINER_OGG, codec=aud.CODEC_VORBIS, bitrate=int(kbps) * 1000)
    print(f"{name}: {target.stat().st_size // 1024} KB")
