"""Cuts a 2x2 orthographic building sheet (front, right / back, left) into same-scale views for Hunyuan3D.

Usage: python -I tools/prepare_grid_views.py <sheet.png> <model-name>
Writes assets-candidates/hunyuan3d/<model-name>/input/{front,right,back,left}.png and check.png.
All views share one scale and one ground line, and are centered on the lower half of the building
so that parts sticking out higher up (an axle, a chimney) cannot shift them.
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage

ROOT = Path(__file__).resolve().parent.parent
SIZE = 1024
MARGIN = 0.06
ORDER = (("front", 0, 0), ("right", 0, 1), ("back", 1, 0), ("left", 1, 1))
# "right" means the view whose image-right side is the building's front (a turntable turned to the right).
# Pass --swap-sides when the sheet's top-right view instead looks at the building's right wall.


def despill(rgb):
    """Removes the key colour's tint from object edges (green or magenta fringe)."""
    border = np.concatenate((rgb[0], rgb[-1], rgb[:, 0], rgb[:, -1])).astype(np.int16)
    color = np.median(border, axis=0)
    if color.max() - color.min() <= 80:
        return rgb
    out = rgb.astype(np.int16)
    r, g, b = out[..., 0], out[..., 1], out[..., 2]
    if color[1] > color[0]:
        out[..., 1] = np.minimum(g, np.maximum(r, b) + 8)
    else:
        limit = g + 8
        out[..., 0] = np.minimum(r, np.maximum(limit, b - 10))
        out[..., 2] = np.minimum(b, np.maximum(limit, r - 10))
    return np.clip(out, 0, 255).astype(np.uint8)


def foreground_mask(rgb, holes=False):
    """Everything that is not the flat, neutral studio background connected to the cell border."""
    border = np.concatenate((rgb[0], rgb[-1], rgb[:, 0], rgb[:, -1])).astype(np.int16)
    color = np.median(border, axis=0)
    signed = rgb.astype(np.int16)
    if color.max() - color.min() > 80:
        # Chroma-key sheet (pure green or magenta background): key on the dominant channels,
        # which keeps white walls and greyish stone that a grey background would swallow.
        r, g, b = signed[..., 0], signed[..., 1], signed[..., 2]
        if color[1] > color[0] and color[1] > color[2]:
            near = (g - np.maximum(r, b)) > 60
        else:
            near = (np.minimum(r, b) - g) > 60
        holes = True  # an unmistakable key colour can safely be cut out of enclosed gaps too
    else:
        neutral = signed.max(axis=2) - signed.min(axis=2) <= 10
        # Generated backgrounds are flat, so a tight tolerance keeps white walls that are only slightly brighter.
        near = (np.abs(signed - color).max(axis=2) <= 9) & neutral
    labels, count = ndimage.label(near)
    sizes = ndimage.sum(near, labels, range(1, count + 1))
    edge = set(np.unique(np.concatenate((labels[0], labels[-1], labels[:, 0], labels[:, -1])))) - {0}
    # Enclosed background patches (gaps between spokes or laths) are cut out only on request.
    background = np.isin(labels, [i + 1 for i in range(count) if (i + 1) in edge or (holes and sizes[i] >= 60)])
    foreground = ~background
    # Keep the largest object only, dropping stray specks.
    parts, n = ndimage.label(foreground)
    if n > 1:
        keep = np.argmax(ndimage.sum(foreground, parts, range(1, n + 1))) + 1
        big = ndimage.binary_dilation(parts == keep, iterations=12)
        foreground &= big
    return foreground


def split_line(sheet, axis):
    """Where the sheet divides into two cells along an axis: a drawn divider line if there is one, else the emptiest band."""
    signed = sheet.astype(np.int16)
    border = np.concatenate((signed[0], signed[-1], signed[:, 0], signed[:, -1]))
    bg = np.median(border, axis=0)
    background = np.abs(signed - bg).max(axis=2) <= 20
    size = sheet.shape[1 - axis]
    lo, hi = int(size * 0.3), int(size * 0.75)
    filled = (~background).mean(axis=axis)  # per column (axis 0) or per row (axis 1)
    luma = signed.mean(axis=2)
    spread, level = luma.std(axis=axis), luma.mean(axis=axis)
    # A divider is a straight, evenly colored line clearly darker or lighter than the background.
    lines = [i for i in range(lo, hi) if spread[i] < 14 and abs(level[i] - bg.mean()) > 25]
    if lines:
        return int(np.median(lines))
    smooth = np.convolve(filled, np.ones(9) / 9, mode="same")
    return lo + int(np.argmin(smooth[lo:hi]))


def main():
    sheet = np.asarray(Image.open(sys.argv[1]).convert("RGB"))
    out = ROOT / "assets-candidates" / "hunyuan3d" / sys.argv[2] / "input"
    out.mkdir(parents=True, exist_ok=True)
    h, w, _ = sheet.shape
    cells = {}
    order = ORDER if "--swap-sides" not in sys.argv else (("front", 0, 0), ("left", 0, 1), ("back", 1, 0), ("right", 1, 1))
    sx, sy = split_line(sheet, 0), split_line(sheet, 1)
    print("split at", sx, sy)
    for name, row, col in order:
        t = h // 80  # trim cell edges, where some generators draw divider lines
        y0, y1 = (0, sy) if row == 0 else (sy, h)
        x0, x1 = (0, sx) if col == 0 else (sx, w)
        rgb = sheet[y0 + t:y1 - t, x0 + t:x1 - t]
        fg = foreground_mask(rgb, "--holes" in sys.argv)
        rows, cols = np.where(fg.any(1))[0], np.where(fg.any(0))[0]
        top, bottom = rows[0], rows[-1] + 1
        lower = fg[top + (bottom - top) // 2:bottom]
        lc = np.where(lower.any(0))[0]
        axis = (lc[0] + lc[-1] + 1) / 2
        cells[name] = (rgb, fg, top, bottom, cols[0], cols[-1] + 1, axis)
    height = max(c[3] - c[2] for c in cells.values())
    half = max(max(c[6] - c[4], c[5] - c[6]) for c in cells.values())
    scale = SIZE * (1 - 2 * MARGIN) / max(height, 2 * half)
    ground = SIZE - SIZE * MARGIN - (SIZE * (1 - 2 * MARGIN) - height * scale) / 2
    preview = Image.new("RGB", (SIZE * 2, SIZE // 2), (92, 132, 62))
    for i, (name, (rgb, fg, top, bottom, left, right, axis)) in enumerate(cells.items()):
        alpha = Image.fromarray((fg * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(0.6))
        rgb = despill(rgb)
        image = Image.fromarray(rgb).convert("RGBA")
        image.putalpha(alpha)
        crop = image.crop((left, top, right, bottom))
        crop = crop.resize((max(1, round(crop.width * scale)), max(1, round(crop.height * scale))), Image.Resampling.LANCZOS)
        canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        canvas.alpha_composite(crop, (round(SIZE / 2 - (axis - left) * scale), round(ground - crop.height)))
        canvas.save(out / f"{name}.png")
        small = canvas.resize((SIZE // 2, SIZE // 2), Image.Resampling.LANCZOS)
        preview.paste(small, (i * SIZE // 2, 0), small)
        print(name, "bbox", (int(left), int(top), int(right), int(bottom)), "axis", float(axis))
    preview.save(out / "check.png")
    print("scale", round(scale, 3))


if __name__ == "__main__":
    main()
