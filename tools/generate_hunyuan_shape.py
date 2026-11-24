"""Generates untextured shapes from the prepared woodcutter views with Hunyuan3D-2mv Turbo.

Run with .tools/hunyuan3d-env/python.exe. Tests both side-view labels because the source view's convention must be checked visually.
"""
import os
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
os.environ.setdefault("HF_HOME", str(ROOT / ".tools" / "hf"))
os.environ.setdefault("HF_HUB_OFFLINE", "1")
os.environ.setdefault("HF_HUB_DISABLE_TELEMETRY", "1")
sys.path.insert(0, str(ROOT / ".tools" / "Hunyuan3D-2"))

import torch  # noqa: E402
from PIL import Image  # noqa: E402
from hy3dgen.shapegen import Hunyuan3DDiTFlowMatchingPipeline  # noqa: E402

BASE = ROOT / "assets-candidates" / "hunyuan3d" / (sys.argv[1] if len(sys.argv) > 1 else "woodcutter-v1")
SEED = 12345


def main():
    views = {name: Image.open(BASE / "input" / f"{name}.png").convert("RGBA") for name in ("front", "right", "back", "left")
             if (BASE / "input" / f"{name}.png").exists()}
    pipeline = Hunyuan3DDiTFlowMatchingPipeline.from_pretrained("tencent/Hunyuan3D-2mv", subfolder="hunyuan3d-dit-v2-mv-turbo", variant="fp16")
    pipeline.enable_flashvdm()
    output = BASE / "raw"
    output.mkdir(parents=True, exist_ok=True)
    for label, side_key in (("side-as-right", "right"), ("side-as-left", "left")):
        torch.cuda.reset_peak_memory_stats()
        started = time.time()
        mesh = pipeline(
            image={"front": views["front"], side_key: views["right"], "back": views["back"],
                   **({("left" if side_key == "right" else "right"): views["left"]} if "left" in views else {})},
            num_inference_steps=8,
            octree_resolution=380,
            num_chunks=20000,
            generator=torch.manual_seed(SEED),
            output_type="trimesh",
        )[0]
        path = output / f"{label}.glb"
        mesh.export(path)
        print(f"{label}: seconds={time.time() - started:.1f} peak_vram_gb={torch.cuda.max_memory_allocated() / 1e9:.2f} "
              f"vertices={len(mesh.vertices)} faces={len(mesh.faces)} bounds={mesh.bounds.round(3).tolist()} watertight={mesh.is_watertight}", flush=True)


if __name__ == "__main__":
    main()
