"""Makes a flat cut-out model (one square card, drawn two-sided) from a frontal image on a white background.

Used for thin moving parts such as windmill sails, which image-to-3D cannot shape well.
Usage: python -I tools/build_cutout_model.py <image.png> <game-name> <size-m> [--cross N]
The card lies in the XY plane centered on the origin, so the renderer can spin it around local Z.
With --cross N (for trees) it writes N upright cards rotated evenly around Y, standing on the ground (y = 0).
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "game" / "assets" / "models"
SIZE = 1024


def tree_atlas(side):
    """Left half: the side view squeezed to half width. Top right: a crown seen from above, made from crown texels."""
    alpha = side[..., 3] > 127
    widths = alpha.sum(1)
    rows = np.where(widths > widths.max() * 0.45)[0]
    c0, c1 = rows[0], rows[-1]
    cols = np.where(alpha[c0:c1 + 1].any(0))[0]
    cx, half = (cols[0] + cols[-1]) / 2, (cols[-1] - cols[0]) / 2
    n = SIZE // 2
    v, u = np.mgrid[0:n, 0:n] / (n - 1) * 2 - 1
    r, theta = np.hypot(u, v), np.arctan2(v, u)
    rng = np.random.default_rng(5)
    bumps = sum(rng.uniform(0.03, 0.07) * np.sin(f * theta + rng.uniform(0, 6.3)) for f in (3, 5, 7, 11))
    inside = r < 0.86 + bumps
    sx = np.clip(cx + u * half, 0, SIZE - 1).astype(int)
    sy = np.clip(c0 + (c1 - c0) * (0.2 + 0.6 * (v + 1) / 2), 0, SIZE - 1).astype(int)
    top = side[sy, sx].copy()
    shade = (1.08 - 0.3 * r)[..., None]  # sunlit middle, darker rim
    top[..., :3] = np.clip(top[..., :3] * shade, 0, 255)
    top[..., 3] = np.where(inside & (alpha[sy, sx] | (r < 0.55)), 255, 0)
    atlas = np.zeros((SIZE, SIZE, 4), np.uint8)
    atlas[:, :n] = np.asarray(Image.fromarray(side).resize((n, SIZE), Image.Resampling.LANCZOS))
    atlas[:n, n:] = top
    atlas[n:, n:] = top  # padding so mipmaps of the top card do not bleed transparent texels
    # Level cards at three heights down the crown, each as wide as the crown is there, so a pine reads as
    # tiers of branches from above instead of one wide disc cutting through it at mid-height.
    first = np.where(widths > 0)[0][0]
    levels = []
    for f in (0.35, 0.62, 0.88):
        row = int(first + (c1 - first) * f)
        levels.append((1 - row / SIZE, 0.92 * widths[row] / SIZE))
    return atlas, levels


def main():
    source, name, size = sys.argv[1], sys.argv[2], float(sys.argv[3])
    picture = Image.open(source).convert("RGB")
    if "--cross" in sys.argv:
        # Trees: crop a square whose bottom is the trunk base and whose top is the crown, so size = tree height.
        a = np.asarray(picture).astype(np.int16)
        content = ~((a.min(2) >= 236) & (a.max(2) - a.min(2) <= 12))
        rows, cols = np.where(content.sum(1) > 2)[0], np.where(content.sum(0) > 2)[0]
        top, bottom = rows[0], rows[-1] + 1
        side = bottom - top
        center = (cols[0] + cols[-1] + 1) // 2
        canvas = Image.new("RGB", (side, side), (255, 255, 255))
        left = center - side // 2
        x0, x1 = max(left, 0), min(left + side, picture.width)  # areas outside the picture stay white
        canvas.paste(picture.crop((x0, top, x1, bottom)), (x0 - left, 0))
        picture = canvas
    rgb = np.asarray(picture.resize((SIZE, SIZE), Image.Resampling.LANCZOS)).astype(np.int16)
    white = (rgb.min(2) >= 236) & (rgb.max(2) - rgb.min(2) <= 12)
    # Remove white specks inside wood, keep every larger white gap transparent.
    labels, count = ndimage.label(white)
    sizes = ndimage.sum(white, labels, range(1, count + 1))
    clear = np.isin(labels, np.nonzero(sizes >= 12)[0] + 1)
    alpha = Image.fromarray(((~clear) * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(3))
    image = Image.fromarray(rgb.astype(np.uint8)).convert("RGBA")
    # Bleed edge colors outward so mipmaps do not show a white fringe.
    solid = np.asarray(alpha) > 0
    nearest = ndimage.distance_transform_edt(~solid, return_distances=False, return_indices=True)
    bled = np.asarray(image)[nearest[0], nearest[1]].copy()
    bled[..., 3] = np.asarray(alpha)
    OUT.mkdir(parents=True, exist_ok=True)
    h = size / 2
    cross = int(sys.argv[sys.argv.index("--cross") + 1]) if "--cross" in sys.argv else 0
    crown = None
    if cross:
        bled, crown = tree_atlas(bled)
    Image.fromarray(bled).save(OUT / f"{name}.dctex", format="PNG", optimize=True)
    card = np.array([(-h, -h, 0), (h, -h, 0), (h, h, 0), (-h, h, 0)], float)
    pos, nor, uv, idx = [], [], [], []
    for k in range(max(cross, 1)):
        a = np.pi * k / max(cross, 1)
        turn = np.array([[np.cos(a), 0, np.sin(a)], [0, 1, 0], [-np.sin(a), 0, np.cos(a)]])
        corners = card + (np.array([0, h, 0]) if cross else 0)
        pos += list(corners @ turn.T)
        # Upright tree cards get upward-tilted normals so both faces light like a leafy crown.
        nor += [turn @ np.array([0, 0.5, 1]) / np.linalg.norm([0, 0.5, 1]) if cross else (0, 0, 1)] * 4
        uv += [(0, 1), (0.5, 1), (0.5, 0), (0, 0)] if cross else [(0, 1), (1, 1), (1, 0), (0, 0)]
        idx += [4 * k + i for i in (0, 2, 1, 0, 3, 2)]
    for level, (height, width) in enumerate(crown or []):
        # Level cards with the crown seen from above, so the tree does not look like a star from a high camera.
        # Each is turned a little so the tiers do not line up.
        height, width = height * size, width * size / 2
        a = 0.7 * level
        c, s = np.cos(a), np.sin(a)
        k = len(pos)
        for x, z in ((-width, -width), (width, -width), (width, width), (-width, width)):
            pos.append((x * c - z * s, height, x * s + z * c))
        nor += [(0, 1, 0)] * 4
        uv += [(0.5, 0), (1, 0), (1, 0.5), (0.5, 0.5)]
        idx += [k + i for i in (0, 1, 2, 0, 2, 3)]
    pos, nor, uv, idx = (np.asarray(pos, "<f4"), np.asarray(nor, "<f4"), np.asarray(uv, "<f4"), np.asarray(idx, "<i4"))
    with (OUT / f"{name}.dcmodel").open("wb") as f:
        f.write(np.array([len(pos), len(idx)], "<i4").tobytes())
        for a in (pos, nor, uv):
            f.write(a.tobytes())
        f.write(idx.tobytes())
    print("CUTOUT_OK", name, "opaque", round(float(solid.mean()), 3))


if __name__ == "__main__":
    main()
