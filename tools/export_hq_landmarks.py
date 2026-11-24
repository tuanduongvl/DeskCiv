"""Writes body landmarks from the 2,000-triangle woodcutter for the Blender rigging step.

Run with .tools/hunyuan3d-env/python.exe.
"""
import json
import struct
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools"))
import bake_villagers as bv  # noqa: E402
from build_hq_character import fit_pivots  # noqa: E402

GAME = ROOT / "assets-candidates" / "hunyuan3d" / "woodcutter-v1" / "game"
data = (GAME / "woodcutter.dchq").read_bytes()
count = struct.unpack("<i", data[:4])[0]
positions = np.frombuffer(data, "<f4", count * 3, 8).reshape(-1, 3).astype(float)
pivots = fit_pivots(positions)
out = {name: pivots[i].round(5).tolist() for i, name in enumerate(bv.NAMES)}
(GAME / "landmarks.json").write_text(json.dumps(out, indent=1), encoding="utf-8")
print("landmarks", len(out), "height", round(float(positions[:, 1].max()), 3))
