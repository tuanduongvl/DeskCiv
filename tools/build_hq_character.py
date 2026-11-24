"""Turns the Hunyuan woodcutter shape into a textured, rigged 2,000-triangle test character.

Run with .tools/hunyuan3d-env/python.exe.
Output: assets-candidates/hunyuan3d/woodcutter-v1/game
"""
import json
import struct
import sys
from pathlib import Path

import numpy as np
import pymeshlab
import trimesh
import xatlas
from PIL import Image
from scipy import ndimage
from scipy.sparse import csr_matrix
from scipy.sparse.csgraph import dijkstra

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools"))
import bake_villagers as bv  # noqa: E402

BASE = ROOT / "assets-candidates" / "hunyuan3d" / "woodcutter-v1"
OUTPUT = BASE / "game"
TARGET_FACES = 2000
HEIGHT = 1.80
TEXTURE = 1024
FPS = 24
MASK_LEFT_PROPS = True  # v1 only: the side view shows a rope and pouch that belong on one side.
POST_COLOR = None  # Optional fix-up for surfaces that the three views cannot see.


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def load_mesh():
    scene = trimesh.load(BASE / "raw" / "side-as-right.glb", force="scene")
    mesh = scene.to_geometry() if hasattr(scene, "to_geometry") else scene.dump(concatenate=True)
    parts = mesh.split(only_watertight=False)
    mesh = max(parts, key=lambda p: len(p.faces))
    v = np.asarray(mesh.vertices, float)
    scale = HEIGHT / (v[:, 1].max() - v[:, 1].min())
    v = v * scale
    v[:, 1] -= v[:, 1].min()
    v[:, 0] -= (v[:, 0].max() + v[:, 0].min()) / 2
    v[:, 2] -= np.median(v[(v[:, 1] > 0.8) & (v[:, 1] < 1.4), 2])
    return v, np.asarray(mesh.faces)


def decimate(v, f):
    ms = pymeshlab.MeshSet()
    ms.add_mesh(pymeshlab.Mesh(vertex_matrix=v, face_matrix=f))
    for target in (60000, 12000, TARGET_FACES):
        ms.meshing_decimation_quadric_edge_collapse(targetfacenum=target, qualitythr=0.45, preserveboundary=True, preservenormal=True, preservetopology=True, optimalplacement=True, planarquadric=True, autoclean=True)
    out = ms.current_mesh()
    return np.asarray(out.vertex_matrix(), float), np.asarray(out.face_matrix(), np.int64)


def fit_pivots(v):
    p = bv.PIVOT.copy()
    scale = HEIGHT / 1.82
    p *= scale
    center_z = 0.0
    for side, arm, fore, hand in ((1, "armL", "foreL", "handL"), (-1, "armR", "foreR", "handR")):
        sx = v[:, 0] * side
        tip = sx.max()
        centers = []
        for x in np.linspace(0.31, tip - 0.08, 8):
            band = v[np.abs(sx - x) < 0.018]
            if len(band) >= 3:
                centers.append((x, np.median(band[:, 1]), np.median(band[:, 2])))
        centers = np.array(centers)
        ay, by = np.polyfit(centers[:, 0], centers[:, 1], 1)
        az, bz = np.polyfit(centers[:, 0], centers[:, 2], 1)
        line = lambda x: np.array((side * x, ay * x + by, az * x + bz))
        shoulder, wrist = line(0.215), line(tip - 0.095)
        p[bv.J[arm]] = shoulder
        p[bv.J[fore]] = shoulder + (wrist - shoulder) * 0.52
        p[bv.J[hand]] = wrist
    for side, leg, shin, foot in ((1, "legL", "shinL", "footL"), (-1, "legR", "shinR", "footR")):
        sx = v[:, 0] * side
        for name, y in ((leg, 0.88), (shin, 0.50), (foot, 0.10)):
            band = v[(np.abs(v[:, 1] - y) < 0.025) & (sx > 0.015)]
            x = np.median(band[:, 0]) if len(band) >= 3 and name != leg else side * 0.1
            z = np.median(band[:, 2]) if len(band) >= 3 and name != leg else center_z
            p[bv.J[name]] = (x, y, z)
    for name, y in (("hips", 0.93), ("spine", 1.06), ("chest", 1.24), ("neck", 1.45), ("head", 1.52)):
        band = v[(np.abs(v[:, 1] - y) < 0.025) & (np.abs(v[:, 0]) < 0.17)]
        p[bv.J[name]] = (0, y, np.median(band[:, 2]) if len(band) >= 3 else 0)
    return p


def align(source, target):
    a = source / np.linalg.norm(source)
    b = target / np.linalg.norm(target)
    axis = np.cross(a, b)
    c = float(np.dot(a, b))
    k = np.array([[0, -axis[2], axis[1]], [axis[2], 0, -axis[0]], [-axis[1], axis[0], 0]])
    return np.eye(3) + k + k @ k / (1 + c)


def surface_distances(v, f, sources):
    edges = np.vstack((f[:, [0, 1]], f[:, [1, 2]], f[:, [2, 0]]))
    edges = np.unique(np.sort(edges, axis=1), axis=0)
    lengths = np.linalg.norm(v[edges[:, 0]] - v[edges[:, 1]], axis=1)
    graph = csr_matrix((lengths, (edges[:, 0], edges[:, 1])), shape=(len(v), len(v)))
    return dijkstra(graph, directed=False, indices=sources, min_only=True)


def skin_weights(v, f, pivots):
    """Uses distance along the surface, so a hand cannot capture a nearby hip or rope."""
    segments = {
        "hips": ("hips", "spine"), "spine": ("spine", "chest"), "chest": ("chest", "neck"), "neck": ("neck", "head"),
        "armL": ("armL", "foreL"), "foreL": ("foreL", "handL"), "armR": ("armR", "foreR"), "foreR": ("foreR", "handR"),
        "legL": ("legL", "shinL"), "shinL": ("shinL", "footL"), "legR": ("legR", "shinR"), "shinR": ("shinR", "footR"),
    }
    ends = {name: (pivots[bv.J[x]], pivots[bv.J[y]]) for name, (x, y) in segments.items()}
    for hand, fore in (("handL", "foreL"), ("handR", "foreR")):
        h = pivots[bv.J[hand]]
        direction = h - pivots[bv.J[fore]]
        ends[hand] = (h, h + direction / np.linalg.norm(direction) * 0.12)
    for foot in ("footL", "footR"):
        p = pivots[bv.J[foot]]
        ends[foot] = (p, p + np.array((0, -0.06, 0.16)))
    scores = np.zeros((len(v), len(bv.NAMES)))
    for joint, (start, end) in ends.items():
        ab = end - start
        t = np.clip(((v - start) @ ab) / (ab @ ab), 0, 1)
        straight = np.linalg.norm(v - (start + t[:, None] * ab), axis=1)
        sources = np.where(straight <= straight.min() * 1.3 + 0.012)[0]
        distance = surface_distances(v, f, sources)
        distance[~np.isfinite(distance)] = 10
        scores[:, bv.J[joint]] = 1 / (distance * distance + 0.0009) ** 2

    head_y = pivots[bv.J["head"]][1]
    head = smoothstep(head_y - 0.015, head_y + 0.035, v[:, 1])
    scores[:, bv.J["head"]] = head * scores.max(axis=1) * 8
    scores[:, [bv.J[n] for n in ("hips", "spine", "chest", "armL", "foreL", "handL", "armR", "foreR", "handR")]] *= (1 - head)[:, None]
    for side, names in ((1, ("armL", "foreL", "handL")), (-1, ("armR", "foreR", "handR"))):
        mask = smoothstep(0.15, 0.205, v[:, 0] * side)
        scores[:, [bv.J[n] for n in names]] *= mask[:, None]
    for side, names in ((1, ("legL", "shinL", "footL")), (-1, ("legR", "shinR", "footR"))):
        lower = smoothstep(0.74, 0.60, v[:, 1])
        same = smoothstep(-0.015, 0.015, v[:, 0] * side)
        scores[:, [bv.J[n] for n in names]] *= (lower * same)[:, None]
    upper = smoothstep(0.58, 0.72, v[:, 1])
    scores[:, bv.J["hips"]] *= upper
    scores[:, bv.J["hips"]] += 1e-9

    order = np.argsort(-scores, axis=1)[:, :4]
    weights = np.take_along_axis(scores, order, axis=1)
    weights /= np.maximum(weights.sum(axis=1, keepdims=True), 1e-12)
    weights[weights < 0.05] = 0
    weights /= weights.sum(axis=1, keepdims=True)
    return order.astype(float), weights


def pose_frame(pose, pivots, rest):
    world = [np.eye(4) for _ in bv.NAMES]
    for i, parent in enumerate(bv.PARENT):
        name = bv.NAMES[i]
        x, z = pose.get(name, (0, 0))
        rotation = bv.rz(z) @ bv.rx(x)
        if name == "chest" and "_turn" in pose:
            rotation = bv.ry(pose["_turn"]) @ rotation
        if name == "head" and "_turn_head" in pose:
            rotation = bv.ry(pose["_turn_head"]) @ rotation
        if name in rest:
            rotation = rotation @ rest[name]
        local = np.eye(4)
        local[:3, :3] = rotation
        if parent < 0:
            local[:3, 3] = pivots[i] + (0, pose.get("_dy", 0), 0)
            world[i] = local
        else:
            local[:3, 3] = pivots[i] - pivots[parent]
            world[i] = world[parent] @ local
    return world


def skin_matrices(world, pivots):
    out = []
    for i, w in enumerate(world):
        inverse = np.eye(4)
        inverse[:3, 3] = -pivots[i]
        out.append(w @ inverse)
    return np.array(out)[:, :3, :]


def rasterize_depth(v2, faces, depth):
    image = np.full((TEXTURE, TEXTURE), -1e9, float)
    for tri in faces:
        p, z = v2[tri], depth[tri]
        x0, y0 = np.maximum(np.floor(p.min(0)).astype(int), 0)
        x1, y1 = np.minimum(np.ceil(p.max(0)).astype(int), TEXTURE - 1)
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


def view_mapping(view, axis_values, y_values):
    alpha = view[:, :, 3] > 0.5
    rows, cols = np.where(alpha.any(axis=1))[0], np.where(alpha.any(axis=0))[0]
    ax = (cols[-1] - cols[0]) / (axis_values.max() - axis_values.min())
    bx = cols[0] - ax * axis_values.min()
    ay = (rows[0] - rows[-1]) / (y_values.max() - y_values.min())
    by = rows[-1] - ay * y_values.min()
    return ax, bx, ay, by


def sample(view, x, y):
    return np.stack([ndimage.map_coordinates(view[:, :, c], [y, x], order=1, mode="nearest") for c in range(4)], axis=1)


def bake_texture(v, normals, faces, uv):
    px, py, pz, nx = [], [], [], []
    uv_px = uv * (TEXTURE - 1)
    covered = np.zeros((TEXTURE, TEXTURE), bool)
    positions, normal_values, indices = [], [], []
    for tri in faces:
        p = uv_px[tri]
        x0, y0 = np.maximum(np.floor(p.min(0)).astype(int) - 1, 0)
        x1, y1 = np.minimum(np.ceil(p.max(0)).astype(int) + 1, TEXTURE - 1)
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
        d = (p[1, 1] - p[2, 1]) * (p[0, 0] - p[2, 0]) + (p[2, 0] - p[1, 0]) * (p[0, 1] - p[2, 1])
        if abs(d) < 1e-9:
            continue
        w0 = ((p[1, 1] - p[2, 1]) * (xs - p[2, 0]) + (p[2, 0] - p[1, 0]) * (ys - p[2, 1])) / d
        w1 = ((p[2, 1] - p[0, 1]) * (xs - p[2, 0]) + (p[0, 0] - p[2, 0]) * (ys - p[2, 1])) / d
        w2 = 1 - w0 - w1
        inside = (w0 >= -.02) & (w1 >= -.02) & (w2 >= -.02) & ~covered[y0:y1 + 1, x0:x1 + 1]
        if not inside.any():
            continue
        covered[y0:y1 + 1, x0:x1 + 1] |= inside
        b = np.stack((w0[inside], w1[inside], w2[inside]), axis=1)
        positions.append(b @ v[tri])
        normal_values.append(b @ normals[tri])
        indices.append(np.stack((ys[inside], xs[inside]), axis=1))
    position = np.vstack(positions)
    normal = np.vstack(normal_values)
    normal /= np.maximum(np.linalg.norm(normal, axis=1, keepdims=True), 1e-9)
    index = np.vstack(indices)

    images = {n: np.asarray(Image.open(BASE / "input" / f"{n}.png").convert("RGBA"), float) / 255 for n in ("front", "right", "back")}
    specs = [
        ("front", "front", v[:, 0], position[:, 0], v[:, 2], position[:, 2], np.array((0, 0, 1.)), 1.0),
        ("right", "right", v[:, 2], position[:, 2], -v[:, 0], -position[:, 0], np.array((-1., 0, 0)), 1.0),
        ("back", "back", -v[:, 0], -position[:, 0], -v[:, 2], -position[:, 2], np.array((0, 0, -1.)), 1.0),
        ("left", "right", v[:, 2], position[:, 2], v[:, 0], position[:, 0], np.array((1., 0, 0)), 0.34),
    ]
    color = np.zeros((len(position), 3))
    total = np.zeros(len(position))
    for label, image_name, axis_vertices, axis_points, depth_vertices, depth_points, direction, strength in specs:
        view = images[image_name]
        ax, bx, ay, by = view_mapping(view, axis_vertices, v[:, 1])
        screen = np.stack((ax * axis_vertices + bx, ay * v[:, 1] + by), axis=1)
        depth_map = rasterize_depth(screen, faces, depth_vertices)
        sx, sy = ax * axis_points + bx, ay * position[:, 1] + by
        visible = depth_points >= ndimage.map_coordinates(ndimage.maximum_filter(depth_map, 3), [sy, sx], order=0, mode="nearest") - 0.035
        rgba = sample(view, sx, sy)
        weight = np.clip(normal @ direction, 0, 1) ** 3 * visible * (rgba[:, 3] > 0.6) * strength
        if label == "left" and MASK_LEFT_PROPS:
            weight *= 1 - smoothstep(0.80, 0.86, position[:, 1]) * smoothstep(1.12, 1.06, position[:, 1])
        color += rgba[:, :3] * weight[:, None]
        total += weight
    valid = total > 1e-5
    color[valid] /= total[valid, None]
    if POST_COLOR:
        color = POST_COLOR(position, normal, color, images, v)
    texture = np.zeros((TEXTURE, TEXTURE, 3))
    mask = np.zeros((TEXTURE, TEXTURE), bool)
    texture[index[valid, 0], index[valid, 1]] = color[valid]
    mask[index[valid, 0], index[valid, 1]] = True
    nearest = ndimage.distance_transform_edt(~mask, return_distances=False, return_indices=True)
    filled = texture[nearest[0], nearest[1]]
    coverage = float(valid.mean())
    return np.clip(filled, 0, 1), coverage


def build_axe(world_hand, bind_hand):
    h = world_hand[:3, 3]
    m = bv.Mesh()
    j = bv.J["handR"]
    wood, dark, flint, edge = (0.56, 0.37, 0.19), (0.3, 0.19, 0.1), (0.72, 0.72, 0.68), (0.93, 0.93, 0.88)
    m.tube(j, h + (0, 0, -0.2), h + (0, 0, 0.9), 0.03, 0.026, wood, 7)
    for z in (0.69, 0.75, 0.81, 0.87):
        m.tube(j, h + (0, 0, z - 0.012), h + (0, 0, z + 0.012), 0.039, 0.039, dark, 7)
    m.ball(j, h + (0, -0.15, 0.78), (0.035, 0.17, 0.125), flint, 6, 4)
    m.box(j, h + (0, -0.285, 0.78), (0.018, 0.075, 0.22), edge)
    m.box(j, h + (0.024, -0.13, 0.75), (0.012, 0.11, 0.09), (0.56, 0.56, 0.53), 0.5)
    m.box(j, h + (-0.024, -0.17, 0.82), (0.012, 0.09, 0.07), (0.84, 0.84, 0.79), -0.4)
    transform = bind_hand @ np.linalg.inv(world_hand)
    p = (transform[:3, :3] @ np.array(m.p).T).T + transform[:3, 3]
    n = (transform[:3, :3] @ np.array(m.n).T).T
    return p, n, np.array(m.c), j


def write_mesh(path, p, n, uv, c, joints, weights, indices):
    with path.open("wb") as f:
        f.write(struct.pack("<ii", len(p), len(indices)))
        for array in (p, n, uv, c, joints, weights):
            f.write(np.asarray(array, dtype="<f4").tobytes())
        f.write(np.asarray(indices, dtype="<i4").tobytes())


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    v, f = load_mesh()
    source_faces = len(f)
    v, f = decimate(v, f)
    mesh = trimesh.Trimesh(v, f, process=False)
    pivots = fit_pivots(v)
    rest = {}
    for name, child, side in (("armL", "foreL", 1), ("armR", "foreR", -1)):
        rest[name] = align(pivots[bv.J[child]] - pivots[bv.J[name]], np.array((side * 0.12, -1, 0)))
    joints, weights = skin_weights(v, f, pivots)
    vmapping, indices, uvs = xatlas.parametrize(v, f)
    atlas_v, atlas_n = v[vmapping], np.asarray(mesh.vertex_normals)[vmapping]
    texture, coverage = bake_texture(atlas_v, atlas_n, indices, uvs)
    Image.fromarray((texture * 255).astype(np.uint8)).save(OUTPUT / "woodcutter_albedo.png")
    write_mesh(OUTPUT / "woodcutter.dchq", atlas_v, atlas_n, uvs, np.ones((len(atlas_v), 3)), joints[vmapping], weights[vmapping], indices.ravel())

    frames, table = [], []
    for name, clip, seconds in bv.CLIPS:
        count = max(2, round(seconds * FPS))
        table.append((name, len(frames), count))
        frames.extend(skin_matrices(pose_frame(clip(i / count), pivots, rest), pivots) for i in range(count))
    with (OUTPUT / "rig.bin").open("wb") as out:
        out.write(struct.pack("<iiii", len(bv.NAMES), len(frames), len(table), FPS))
        for _, start, count in table:
            out.write(struct.pack("<ii", start, count))
        out.write(np.array(frames, dtype="<f4").tobytes())

    rest_world = pose_frame({}, pivots, rest)[bv.J["handR"]]
    bind_hand = np.eye(4)
    bind_hand[:3, 3] = pivots[bv.J["handR"]]
    p, n, c, joint = build_axe(rest_world, bind_hand)
    tool_joints = np.zeros((len(p), 4)); tool_joints[:, 0] = joint
    tool_weights = np.zeros((len(p), 4)); tool_weights[:, 0] = 1
    write_mesh(OUTPUT / "axe.dchq", p, n, np.zeros((len(p), 2)), c, tool_joints, tool_weights, np.arange(len(p)))
    report = {
        "source_triangles": int(source_faces), "body_triangles": int(len(f)), "uv_vertices": int(len(atlas_v)), "axe_triangles": int(len(p) // 3),
        "texture": TEXTURE, "texture_coverage": round(coverage, 4), "height": round(float(v[:, 1].max() - v[:, 1].min()), 3),
        "max_weights_per_vertex": int((weights > 0).sum(axis=1).max()), "clips": [c[0] for c in table],
    }
    (OUTPUT / "build-report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report))


if __name__ == "__main__":
    main()
