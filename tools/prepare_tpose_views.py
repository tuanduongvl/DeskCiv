"""Cuts the T-pose concept sheet into same-scale front, right and back views for Hunyuan3D.

Usage: python -I tools/prepare_tpose_views.py <role> <role>-vN
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools"))
from prepare_concept_views import background_mask, column_groups  # noqa: E402

ROLE = sys.argv[1] if len(sys.argv) > 1 else "woodcutter"
VERSION = sys.argv[2] if len(sys.argv) > 2 else "woodcutter-v2"
SOURCE = ROOT / "assets-candidates" / "concepts" / f"{ROLE}-tpose" / "01-orthographic-tpose.png"
OUTPUT = ROOT / "assets-candidates" / "hunyuan3d" / VERSION / "input"
SIZE = 1024
MARGIN = 0.05


def body_axis(foreground, left, right, top, bottom):
    """Centers each view on the torso, so arms, toes and hair cannot shift it."""
    height = bottom - top
    band = foreground[top + int(height * 0.32):top + int(height * 0.55), left:right]
    columns = np.where(band.mean(axis=0) > 0.5)[0]
    return left + (columns[0] + columns[-1] + 1) / 2


def main():
    rgb = np.asarray(Image.open(SOURCE).convert("RGB"))
    foreground = ~background_mask(rgb)
    groups = column_groups(foreground)
    if len(groups) != 3:
        raise SystemExit(f"Expected 3 figures, found {len(groups)} groups: {groups}")
    rows = np.where(foreground.any(axis=1))[0]
    top, bottom = int(rows[0]), int(rows[-1]) + 1
    axes = [body_axis(foreground, left, right, top, bottom) for left, right in groups]
    half_width = max(max(axis - left, right - axis) for axis, (left, right) in zip(axes, groups))
    extent = max(bottom - top, half_width * 2)
    scale = SIZE * (1 - 2 * MARGIN) / extent
    alpha = Image.fromarray((foreground * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(0.6))
    rgba = Image.fromarray(rgb).convert("RGBA")
    rgba.putalpha(alpha)
    OUTPUT.mkdir(parents=True, exist_ok=True)
    y = round((SIZE - (bottom - top) * scale) / 2)
    for name, axis, (left, right) in zip(("front", "right", "back"), axes, groups):
        crop = rgba.crop((left, top, right, bottom))
        crop = crop.resize((round(crop.width * scale), round(crop.height * scale)), Image.Resampling.LANCZOS)
        canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        canvas.alpha_composite(crop, (round(SIZE / 2 - (axis - left) * scale), y))
        canvas.save(OUTPUT / f"{name}.png")
        print(name, "source_x", left, right, "axis", axis, "output", crop.size)
    preview = Image.new("RGB", (SIZE * 3 // 2, SIZE // 2), (92, 132, 62))
    for i, name in enumerate(("front", "right", "back")):
        view = Image.open(OUTPUT / f"{name}.png").resize((SIZE // 2, SIZE // 2), Image.Resampling.LANCZOS)
        preview.paste(view, (i * SIZE // 2, 0), view)
    preview.save(OUTPUT / "check.png")
    print("scale", round(scale, 3), "figure_height", bottom - top)


if __name__ == "__main__":
    main()
