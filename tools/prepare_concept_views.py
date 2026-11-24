"""Cuts the approved turnaround sheet into aligned transparent front, right and back views.

Usage: python -I tools/prepare_concept_views.py
"""
from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "assets-candidates" / "concepts" / "woodcutter-approved" / "01-orthographic-turnaround.png"
OUTPUT = ROOT / "assets-candidates" / "hunyuan3d" / "woodcutter-v1" / "input"
SIZE = 1024
MARGIN = 0.07


def background_mask(rgb):
    """Finds the neutral studio background, including enclosed gaps such as between the legs."""
    border = np.concatenate((rgb[0], rgb[-1], rgb[:, 0], rgb[:, -1]))
    color = np.median(border, axis=0)
    signed = rgb.astype(np.int16)
    neutral = signed.max(axis=2) - signed.min(axis=2) <= 16
    near = (np.abs(signed - color).max(axis=2) <= 34) & neutral
    height, width = near.shape
    seen = np.zeros_like(near)
    background = np.zeros_like(near)
    for start_y, start_x in zip(*np.nonzero(near)):
        if seen[start_y, start_x]:
            continue
        queue = deque([(int(start_y), int(start_x))])
        seen[start_y, start_x] = True
        region = []
        while queue:
            y, x = queue.popleft()
            region.append((y, x))
            for ny, nx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
                if 0 <= ny < height and 0 <= nx < width and near[ny, nx] and not seen[ny, nx]:
                    seen[ny, nx] = True
                    queue.append((ny, nx))
        touches_border = any(y in (0, height - 1) or x in (0, width - 1) for y, x in region)
        if touches_border or len(region) >= 90:
            ys, xs = zip(*region)
            background[list(ys), list(xs)] = True
    return background


def column_groups(foreground):
    """Finds horizontally separated figures, ignoring tiny specks."""
    columns = foreground.sum(axis=0) > 2
    groups, start = [], None
    for x, used in enumerate(columns):
        if used and start is None:
            start = x
        if not used and start is not None:
            if x - start > 40:
                groups.append((start, x))
            start = None
    if start is not None:
        groups.append((start, len(columns)))
    return groups


def main():
    rgb = np.asarray(Image.open(SOURCE).convert("RGB"))
    background = background_mask(rgb)
    foreground = ~background
    groups = column_groups(foreground)
    if len(groups) != 4:
        raise SystemExit(f"Expected 3 figures and an axe, found {len(groups)} groups: {groups}")
    figures = groups[:3]
    rows = np.where(foreground[:, figures[0][0]:figures[2][1]].any(axis=1))[0]
    top, bottom = int(rows[0]), int(rows[-1]) + 1
    figure_height = bottom - top
    scale = SIZE * (1 - 2 * MARGIN) / figure_height
    alpha = Image.fromarray((foreground * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(5)).filter(ImageFilter.GaussianBlur(0.7))
    rgba = Image.fromarray(rgb).convert("RGBA")
    rgba.putalpha(alpha)
    OUTPUT.mkdir(parents=True, exist_ok=True)
    for name, (left, right) in zip(("front", "right", "back"), figures):
        crop = rgba.crop((left, top, right, bottom))
        crop = crop.resize((round(crop.width * scale), round(crop.height * scale)), Image.Resampling.LANCZOS)
        canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        canvas.alpha_composite(crop, ((SIZE - crop.width) // 2, round(SIZE * MARGIN)))
        canvas.save(OUTPUT / f"{name}.png")
        print(name, "source_x", left, right, "output", crop.size)
    preview = Image.new("RGB", (SIZE * 3 // 2, SIZE // 2), (92, 132, 62))
    for i, name in enumerate(("front", "right", "back")):
        view = Image.open(OUTPUT / f"{name}.png").resize((SIZE // 2, SIZE // 2), Image.Resampling.LANCZOS)
        preview.paste(view, (i * SIZE // 2, 0), view)
    preview.save(OUTPUT / "check.png")


if __name__ == "__main__":
    main()
