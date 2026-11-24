"""Repaint villager textures as one solid color per triangle, so characters match the low-poly Kenney world.

Each triangle gets the average of the painted texture under it, then all triangle colors are snapped to a
small palette. With flat shading in the game this gives the faceted look of the Kenney buildings and trees.

The painted originals are kept in art/characters-painted so the step can be re-run or undone:
    python -I tools/stylize_character_textures.py            # all hq_* characters
    python -I tools/stylize_character_textures.py --restore  # put the painted textures back
"""
import shutil
import struct
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw
from scipy.cluster.vq import kmeans2

ROOT = Path(__file__).resolve().parent.parent
GAME = ROOT / "game" / "assets" / "characters"
PAINTED = ROOT / "art" / "characters-painted"
COLORS = 10  # palette size per character
SAMPLES = 24  # texture samples per triangle


def read_mesh(path: Path):
    """Positions, UVs and triangle indices from a textured .dcmesh (see CharacterKit.LoadMesh)."""
    d = path.read_bytes()
    v, n = struct.unpack_from("<ii", d, 0)
    at = 8
    pos = np.frombuffer(d, np.float32, v * 3, at).reshape(v, 3)
    at += v * 12 * 2  # positions, normals
    uv = np.frombuffer(d, np.float32, v * 2, at).reshape(v, 2)
    at += v * 8 + v * 12 + v * 16 * 2  # uvs, colors, joints, weights
    tris = np.frombuffer(d, np.int32, n, at).reshape(-1, 3)
    return pos, uv, tris


def smooth_neighbors(pos, tris, colors, rounds=3, edge=0.12):
    """Blend each triangle with touching triangles of a similar color, so cloth and skin become clean patches."""
    _, weld = np.unique(np.round(pos, 4), axis=0, return_inverse=True)
    corners = weld.reshape(-1)[tris] if weld.ndim > 1 else weld[tris]
    owners = {}
    for t, vs in enumerate(corners):
        for v in vs:
            owners.setdefault(int(v), []).append(t)
    pairs = np.array([(a, b) for ts in owners.values() for a in ts for b in ts if a != b])
    for _ in range(rounds):
        diff = np.linalg.norm(colors[pairs[:, 0]] - colors[pairs[:, 1]], axis=1)
        w = np.exp(-(diff / edge) ** 2)
        total = np.ones(len(colors))
        acc = colors.copy()
        np.add.at(acc, pairs[:, 0], colors[pairs[:, 1]] * w[:, None])
        np.add.at(total, pairs[:, 0], w)
        colors = acc / total[:, None]
    return colors


def stylize(mesh: Path, src: Path, dst: Path):
    image = np.asarray(Image.open(src).convert("RGB"), np.float32) / 255.0
    h, w, _ = image.shape
    pos, uv, tris = read_mesh(mesh)
    rng = np.random.default_rng(3)
    bary = rng.dirichlet((1.5, 1.5, 1.5), SAMPLES)  # interior points, away from neighboring islands
    corners = uv[tris] * (w, h)  # (t, 3, 2) pixels
    points = np.einsum("sk,tkd->tsd", bary, corners)
    px = np.clip(points[..., 0].astype(int), 0, w - 1)
    py = np.clip(points[..., 1].astype(int), 0, h - 1)
    samples = image[py, px]  # (t, s, 3)
    # Median rejects stray seam pixels better than the mean.
    colors = np.median(samples, 1)
    colors = smooth_neighbors(pos, tris, colors)

    centers, labels = kmeans2(colors, COLORS, minit="++", seed=3)
    palette = np.stack([colors[labels == i].mean(0) if np.any(labels == i) else centers[i] for i in range(COLORS)])
    # Kenney colors are a little brighter and more saturated than the painted concept art.
    grey = palette.mean(1, keepdims=True)
    palette = np.clip(grey + (palette - grey) * 1.2, 0, 1) ** 0.9
    flat = palette[labels]

    # Fill the background with the most common color so mipmaps do not bleed black into the edges.
    base = tuple(int(c * 255) for c in palette[np.bincount(labels, minlength=COLORS).argmax()])
    out = Image.new("RGB", (w, h), base)
    draw = ImageDraw.Draw(out)
    # Larger triangles first, so small details such as eyes and belts stay on top.
    a, b = corners[:, 1] - corners[:, 0], corners[:, 2] - corners[:, 0]
    area = np.abs(a[:, 0] * b[:, 1] - a[:, 1] * b[:, 0])
    for t in np.argsort(-area):
        color = tuple(int(c * 255) for c in flat[t])
        poly = [tuple(p) for p in corners[t]]
        draw.polygon(poly, fill=color, outline=color, width=3)
    out.save(dst, format="PNG", optimize=True)


def main():
    PAINTED.mkdir(parents=True, exist_ok=True)
    restore = "--restore" in sys.argv
    for tex in sorted(GAME.glob("hq_*.dctex")):
        original = PAINTED / (tex.stem + ".png")
        if not original.exists():
            shutil.copyfile(tex, original)
        if restore:
            shutil.copyfile(original, tex)
        else:
            stylize(GAME / (tex.stem + ".dcmesh"), original, tex)
        print(("restored " if restore else "stylized ") + tex.name)


if __name__ == "__main__":
    main()
