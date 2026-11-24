"""Turns a Hunyuan3D building shape plus its four reference views into a textured game model.

Usage (Hunyuan Python):
    .tools/hunyuan3d-env/python.exe tools/build_hunyuan_model.py <model-name> <height-m> [faces] [game-name] [yaw-deg]

Reads assets-candidates/hunyuan3d/<model-name>/raw/side-as-{right,left}.glb and input/{front,right,back,left}.png.
Picks the raw shape whose outline matches the four views best, scales it to the given height with its base
centered on the origin (front = +Z), decimates it, unwraps UVs with xatlas and projects the views onto it.
Writes game/assets/models/<game-name>.dcmodel (int32 vertex count, int32 index count, float32 positions,
normals, UVs, int32 indices) and <game-name>.dctex (JPEG bytes), plus a review image in the model folder.
"""
import json
import sys
from pathlib import Path

import numpy as np
import pymeshlab
import trimesh
import xatlas
from PIL import Image
from scipy import ndimage

ROOT = Path(__file__).resolve().parent.parent
NAME = sys.argv[1]
HEIGHT = float(sys.argv[2])
FACES = int(sys.argv[3]) if len(sys.argv) > 3 else 4000
GAME_NAME = sys.argv[4] if len(sys.argv) > 4 else NAME.rsplit("-v", 1)[0]
YAW = float(sys.argv[5]) if len(sys.argv) > 5 else 0.0  # degrees around +Y applied to the written model only
BASE = ROOT / "assets-candidates" / "hunyuan3d" / NAME
OUT = ROOT / "game" / "assets" / "models"
TEXTURE = 2048
VIEW = 1024

# image-x axis, depth toward the camera and view direction for each reference view (front = +Z, right view sees -X).
SPECS = {
    "front": (lambda p: p[:, 0], lambda p: p[:, 2], np.array((0, 0, 1.0))),
    "right": (lambda p: p[:, 2], lambda p: -p[:, 0], np.array((-1.0, 0, 0))),
    "back": (lambda p: -p[:, 0], lambda p: -p[:, 2], np.array((0, 0, -1.0))),
    "left": (lambda p: -p[:, 2], lambda p: p[:, 0], np.array((1.0, 0, 0))),
}


def view_extent(view):
    """Opaque width and height of a reference view in pixels (all views share one scale)."""
    alpha = view[:, :, 3] > 0.5
    rows, cols = np.where(alpha.any(1))[0], np.where(alpha.any(0))[0]
    return cols[-1] - cols[0], rows[-1] - rows[0]


def load(path, views):
    scene = trimesh.load(path, force="scene")
    mesh = scene.to_geometry() if hasattr(scene, "to_geometry") else scene.dump(concatenate=True)
    parts = mesh.split(only_watertight=False)
    biggest = max(len(p.faces) for p in parts)
    mesh = trimesh.util.concatenate([p for p in parts if len(p.faces) > biggest * 0.02])
    v = np.asarray(mesh.vertices, float)
    v *= HEIGHT / (v[:, 1].max() - v[:, 1].min())
    v[:, 1] -= v[:, 1].min()
    # Hunyuan tends to squeeze long buildings; restore the width and depth measured on the views.
    (fw, fh), (sw, sh) = view_extent(views["front"]), view_extent(views["right"])
    for axis, ratio in ((0, fw / fh), (2, sw / sh)):
        extent = v[:, axis].max() - v[:, axis].min()
        v[:, axis] *= ratio * HEIGHT / extent
    base = v[v[:, 1] < HEIGHT * 0.2]
    v[:, 0] -= (base[:, 0].max() + base[:, 0].min()) / 2
    v[:, 2] -= (base[:, 2].max() + base[:, 2].min()) / 2
    return v, np.asarray(mesh.faces)


def raster(points, faces, depth, size):
    """Max-depth z-buffer of projected triangles."""
    image = np.full((size, size), -1e9)
    for tri in faces:
        p, z = points[tri], depth[tri]
        x0, y0 = np.maximum(np.floor(p.min(0)).astype(int), 0)
        x1, y1 = np.minimum(np.ceil(p.max(0)).astype(int), size - 1)
        if x1 < x0 or y1 < y0:
            continue
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1) + .5, np.arange(y0, y1 + 1) + .5)
        d = (p[1, 1] - p[2, 1]) * (p[0, 0] - p[2, 0]) + (p[2, 0] - p[1, 0]) * (p[0, 1] - p[2, 1])
        if abs(d) < 1e-9:
            continue
        w0 = ((p[1, 1] - p[2, 1]) * (xs - p[2, 0]) + (p[2, 0] - p[1, 0]) * (ys - p[2, 1])) / d
        w1 = ((p[2, 1] - p[0, 1]) * (xs - p[2, 0]) + (p[0, 0] - p[2, 0]) * (ys - p[2, 1])) / d
        w2 = 1 - w0 - w1
        inside = (w0 >= -.01) & (w1 >= -.01) & (w2 >= -.01)
        value = w0 * z[0] + w1 * z[1] + w2 * z[2]
        patch = image[y0:y1 + 1, x0:x1 + 1]
        update = inside & (value > patch)
        patch[update] = value[update]
    return image


def mapping(view, axis, y):
    """Pixel = a * world + b, fitted so the mesh extent fills the view's opaque bounding box."""
    alpha = view[:, :, 3] > 0.5
    rows, cols = np.where(alpha.any(1))[0], np.where(alpha.any(0))[0]
    ax = (cols[-1] - cols[0]) / (axis.max() - axis.min())
    ay = (rows[0] - rows[-1]) / (y.max() - y.min())
    return ax, cols[0] - ax * axis.min(), ay, rows[-1] - ay * y.min()


def outline_score(v, f, views):
    """Mean silhouette IoU against the reference views."""
    scores = []
    for name, (axis, depth, _) in SPECS.items():
        if name not in views:
            continue
        view = views[name]
        ax, bx, ay, by = mapping(view, axis(v), v[:, 1])
        screen = np.stack((ax * axis(v) + bx, ay * v[:, 1] + by), 1)
        sil = raster(screen / 4, f, depth(v), VIEW // 4) > -1e8
        ref = view[::4, ::4, 3] > 0.5
        scores.append((sil & ref).sum() / max((sil | ref).sum(), 1))
    return float(np.mean(scores)), [round(float(s), 3) for s in scores]


def decimate(v, f):
    ms = pymeshlab.MeshSet()
    ms.add_mesh(pymeshlab.Mesh(vertex_matrix=v, face_matrix=f))
    for target in (80000, 20000, FACES):
        if target < len(f):
            ms.meshing_decimation_quadric_edge_collapse(targetfacenum=target, qualitythr=0.4, preserveboundary=True, preservenormal=True,
                                                        preservetopology=False, optimalplacement=True, planarquadric=True, autoclean=True)
    out = ms.current_mesh()
    return np.asarray(out.vertex_matrix(), float), np.asarray(out.face_matrix(), np.int64)


def bake(v, normals, faces, uv, views):
    uv_px = uv * (TEXTURE - 1)
    covered = np.zeros((TEXTURE, TEXTURE), bool)
    positions, normal_values, indices = [], [], []
    for tri in faces:
        p = uv_px[tri]
        x0, y0 = np.maximum(np.floor(p.min(0)).astype(int) - 1, 0)
        x1, y1 = np.minimum(np.ceil(p.max(0)).astype(int) + 1, TEXTURE - 1)
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
        d = (p[1, 1] - p[2, 1]) * (p[0, 0] - p[2, 0]) + (p[2, 0] - p[1, 0]) * (p[0, 1] - p[2, 1])
        if abs(d) < 1e-12:
            continue
        w0 = ((p[1, 1] - p[2, 1]) * (xs - p[2, 0]) + (p[2, 0] - p[1, 0]) * (ys - p[2, 1])) / d
        w1 = ((p[2, 1] - p[0, 1]) * (xs - p[2, 0]) + (p[0, 0] - p[2, 0]) * (ys - p[2, 1])) / d
        w2 = 1 - w0 - w1
        inside = (w0 >= -.03) & (w1 >= -.03) & (w2 >= -.03) & ~covered[y0:y1 + 1, x0:x1 + 1]
        if not inside.any():
            continue
        covered[y0:y1 + 1, x0:x1 + 1] |= inside
        b = np.stack((w0[inside], w1[inside], w2[inside]), 1)
        positions.append(b @ v[tri])
        normal_values.append(b @ normals[tri])
        indices.append(np.stack((ys[inside], xs[inside]), 1))
    position, normal, index = np.vstack(positions), np.vstack(normal_values), np.vstack(indices)
    normal /= np.maximum(np.linalg.norm(normal, axis=1, keepdims=True), 1e-9)

    color = np.zeros((len(position), 3))
    total = np.zeros(len(position))
    for name, (axis, depth, direction) in SPECS.items():
        view = views[name]
        ax, bx, ay, by = mapping(view, axis(v), v[:, 1])
        screen = np.stack((ax * axis(v) + bx, ay * v[:, 1] + by), 1)
        zbuf = ndimage.maximum_filter(raster(screen, faces, depth(v), VIEW), 3)
        sx, sy = ax * axis(position) + bx, ay * position[:, 1] + by
        visible = depth(position) >= ndimage.map_coordinates(zbuf, [sy, sx], order=0, mode="nearest") - HEIGHT * 0.012
        rgba = np.stack([ndimage.map_coordinates(view[:, :, c], [sy, sx], order=1, mode="nearest") for c in range(4)], 1)
        facing = np.clip(normal @ direction, 0, 1)
        weight = (facing ** 3 + 0.004) * visible * (rgba[:, 3] > 0.6)
        color += rgba[:, :3] * weight[:, None]
        total += weight
    valid = total > 1e-6
    color[valid] /= total[valid, None]
    # Upward faces that no side view sees well (a boat's deck, a flat roof) take the colour of
    # well-seen surfaces at the same height instead of stray background or sail pixels.
    hidden = (normal[:, 1] > 0.55) & (total < 0.03)
    seen = valid & (total > 0.2)
    if hidden.any() and seen.sum() > 50:
        bands = np.clip((position[:, 1] / HEIGHT * 8).astype(int), 0, 7)
        for band in np.unique(bands[hidden]):
            source = seen & (np.abs(bands - band) <= 1)
            if source.sum() < 20:
                source = seen
            color[hidden & (bands == band)] = np.median(color[source], axis=0)
        valid = valid | hidden
    texture = np.zeros((TEXTURE, TEXTURE, 3))
    mask = np.zeros((TEXTURE, TEXTURE), bool)
    texture[index[valid, 0], index[valid, 1]] = color[valid]
    mask[index[valid, 0], index[valid, 1]] = True
    nearest = ndimage.distance_transform_edt(~mask, return_distances=False, return_indices=True)
    return np.clip(texture[nearest[0], nearest[1]], 0, 1), float(valid.mean())


def main():
    views = {n: np.asarray(Image.open(BASE / "input" / f"{n}.png").convert("RGBA"), float) / 255 for n in SPECS}
    best = None
    for label in ("side-as-right", "side-as-left"):
        path = BASE / "raw" / f"{label}.glb"
        if not path.exists():
            continue
        v, f = load(path, views)
        lv, lf = decimate(v, f) if len(f) > 30000 else (v, f)
        score, per_view = outline_score(lv, lf, views)
        print(label, "outline IoU", round(score, 3), per_view)
        if best is None or score > best[0]:
            best = (score, label, v, f)
    score, label, v, f = best
    v, f = decimate(v, f)
    mesh = trimesh.Trimesh(v, f, process=False)
    vmap, idx, uvs = xatlas.parametrize(v.astype(np.float32), f.astype(np.uint32))
    av, an = v[vmap], np.asarray(mesh.vertex_normals)[vmap]
    texture, coverage = bake(av, an, idx, uvs, views)
    OUT.mkdir(parents=True, exist_ok=True)
    c, s_ = np.cos(np.radians(YAW)), np.sin(np.radians(YAW))
    turn = np.array([[c, 0, s_], [0, 1, 0], [-s_, 0, c]])
    av, an = av @ turn.T, an @ turn.T
    with (OUT / f"{GAME_NAME}.dcmodel").open("wb") as out:
        out.write(np.array([len(av), idx.size], "<i4").tobytes())
        for array in (av, an, uvs):
            out.write(np.asarray(array, "<f4").tobytes())
        out.write(np.asarray(idx[:, ::-1].ravel(), "<i4").tobytes())  # Godot treats clockwise triangles as front faces
    image = Image.fromarray((texture * 255).astype(np.uint8))
    image.save(OUT / f"{GAME_NAME}.dctex", format="JPEG", quality=90)
    image.save(BASE / "texture.png")
    report = {"source": label, "outline_iou": round(score, 3), "triangles": int(len(idx)), "vertices": int(len(av)),
              "texture_coverage": round(coverage, 4), "size_m": np.round(v.max(0) - v.min(0), 2).tolist()}
    (BASE / "build-report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print("BUILD_OK", json.dumps(report))


if __name__ == "__main__":
    main()
