"""Bakes CC0 Quaternius characters into compact mesh and bone-animation files for DeskCiv.

Usage: python -I tools/bake_characters.py
Input:  assets-source/quaternius-cube-world
Output: game/assets/characters
"""
import base64
import json
import math
import struct
from io import BytesIO
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "assets-source" / "quaternius-cube-world"
OUTPUT = ROOT / "game" / "assets" / "characters"
FPS = 24
COMPONENT = {5120: "b", 5121: "B", 5122: "h", 5123: "H", 5125: "I", 5126: "f"}
SIZES = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}
CHARACTERS = {"male1": "Character_Male_1", "male2": "Character_Male_2", "female1": "Character_Female_1", "female2": "Character_Female_2"}


class Gltf:
    def __init__(self, path):
        self.json = json.loads(path.read_text(encoding="utf-8"))
        self.buffers = [base64.b64decode(b["uri"].split(",", 1)[1]) for b in self.json["buffers"]]

    def view(self, index):
        v = self.json["bufferViews"][index]
        start = v.get("byteOffset", 0)
        return self.buffers[v["buffer"]][start:start + v["byteLength"]]

    def accessor(self, index):
        a = self.json["accessors"][index]
        view = self.json["bufferViews"][a["bufferView"]]
        size = SIZES[a["type"]]
        dtype = np.dtype("<" + COMPONENT[a["componentType"]])
        stride = view.get("byteStride", dtype.itemsize * size)
        offset = view.get("byteOffset", 0) + a.get("byteOffset", 0)
        data = self.buffers[view["buffer"]]
        if stride == dtype.itemsize * size:
            out = np.frombuffer(data, dtype, a["count"] * size, offset).copy()
        else:
            out = np.array([np.frombuffer(data, dtype, size, offset + i * stride) for i in range(a["count"])]).ravel()
        return out.reshape((a["count"], size)) if size > 1 else out

    def atlas(self):
        image = self.json["images"][0]
        return np.asarray(Image.open(BytesIO(self.view(image["bufferView"]))).convert("RGB"), dtype=np.float32) / 255.0


def quat_matrix(q):
    x, y, z, w = q
    return np.array([
        [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
        [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
        [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)],
    ])


def slerp(a, b, t):
    dot = float(np.dot(a, b))
    if dot < 0:
        b, dot = -b, -dot
    if dot > 0.9995:
        out = a + (b - a) * t
    else:
        theta = math.acos(min(1, dot))
        out = (math.sin((1 - t) * theta) * a + math.sin(t * theta) * b) / math.sin(theta)
    return out / np.linalg.norm(out)


def rot_x(degrees):
    r = math.radians(degrees)
    c, s = math.cos(r), math.sin(r)
    return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])


def smooth(v):
    v = max(0.0, min(1.0, v))
    return v * v * (3 - 2 * v)


def lerp(a, b, t):
    return a + (b - a) * t


class Rig:
    def __init__(self, gltf):
        self.g = gltf
        j = gltf.json
        self.nodes = j["nodes"]
        self.skin = j["skins"][0]
        self.joints = self.skin["joints"]
        self.names = {self.nodes[n]["name"]: n for n in self.joints}
        self.inverse = gltf.accessor(self.skin["inverseBindMatrices"]).reshape(-1, 4, 4).transpose(0, 2, 1)
        self.parent = {}
        for i, n in enumerate(self.nodes):
            for c in n.get("children", []):
                self.parent[c] = i
        self.order = []
        stack = list(j["scenes"][0]["nodes"])
        while stack:
            n = stack.pop()
            self.order.append(n)
            stack.extend(self.nodes[n].get("children", []))
        self.anims = {a["name"]: a for a in j["animations"]}
        self.cache = {}

    def channels(self, name):
        if name not in self.cache:
            a = self.anims[name]
            out = []
            for c in a["channels"]:
                s = a["samplers"][c["sampler"]]
                out.append((c["target"]["node"], c["target"]["path"], self.g.accessor(s["input"]), self.g.accessor(s["output"])))
            length = max(float(ch[2][-1]) for ch in out)
            self.cache[name] = (out, length)
        return self.cache[name]

    def globals(self, name, t, offsets=None):
        channels, length = self.channels(name)
        t = t % length if length > 0 else 0
        trs = {}
        for i, n in enumerate(self.nodes):
            trs[i] = [np.array(n.get("translation", [0, 0, 0]), float), np.array(n.get("rotation", [0, 0, 0, 1]), float), np.array(n.get("scale", [1, 1, 1]), float)]
        for node, path, times, values in channels:
            k = int(np.searchsorted(times, t, side="right") - 1)
            k = max(0, min(k, len(times) - 1))
            if k >= len(times) - 1:
                value = values[-1].astype(float)
            else:
                u = (t - times[k]) / max(1e-6, times[k + 1] - times[k])
                value = slerp(values[k].astype(float), values[k + 1].astype(float), u) if path == "rotation" else lerp(values[k].astype(float), values[k + 1].astype(float), u)
            trs[node][0 if path == "translation" else 1] = value
        result = {}
        for n in self.order:
            T, R, S = trs[n]
            local = np.eye(4)
            local[:3, :3] = quat_matrix(R) @ np.diag(S)
            local[:3, 3] = T
            world = result[self.parent[n]] @ local if n in self.parent else local
            name_ = self.nodes[n].get("name")
            if offsets and name_ in offsets:
                world = world.copy()
                world[:3, :3] = rot_x(offsets[name_]) @ world[:3, :3]
            aim = offsets.get("_aim", {}).get(name_) if offsets else None
            if aim is not None:
                side_name = name_[-1]
                child = self.names[("LowerArm." if name_.startswith("UpperArm") else "Fist.") + side_name]
                current = world[:3, :3] @ trs[child][0]
                side = 1.0 if world[0, 3] >= 0 else -1.0
                radians = math.radians(aim)
                target = np.array([side * 0.17, -math.cos(radians), math.sin(radians)])
                world = world.copy()
                world[:3, :3] = align(current, target) @ world[:3, :3]
            result[n] = world
        return result

    def skin_frame(self, name, t, offsets=None):
        world = self.globals(name, t, offsets)
        return np.array([world[j] @ self.inverse[i] for i, j in enumerate(self.joints)])


def swing(u):
    if u < 0.58:
        return smooth(u / 0.58)
    if u < 0.7:
        return 1 - smooth((u - 0.58) / 0.12)
    return 0.0


def arms(angle_l, angle_r, torso=0.0, fore_l=0.0, fore_r=0.0):
    """Angles are measured from hanging down (0) through forward (90) to overhead (180)."""
    return {
        "Abdomen": torso * 0.45, "Torso": torso * 0.55,
        "_aim": {
            "UpperArm.L": angle_l, "UpperArm.R": angle_r,
            "LowerArm.L": angle_l + fore_l, "LowerArm.R": angle_r + fore_r,
        },
    }


def align(source, target):
    a = source / np.linalg.norm(source)
    b = target / np.linalg.norm(target)
    v = np.cross(a, b)
    c = float(np.dot(a, b))
    if c < -0.9999:
        return rot_x(180)
    k = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]])
    return np.eye(3) + k + k @ k / (1 + c)


def clip_chop(u):
    s = swing(u)
    return arms(lerp(38, 172, s), lerp(38, 172, s), lerp(30, -10, s), 8, 8)


def clip_hoe(u):
    s = swing(u)
    return arms(lerp(30, 118, s), lerp(30, 118, s), lerp(38, 4, s), 16, 16)


def clip_mine(u):
    s = swing((u * 1.0) % 1)
    return arms(lerp(48, 178, s), lerp(48, 178, s), lerp(34, -6, s), 6, 6)


def clip_sow(u):
    w = math.sin(u * math.tau)
    return arms(42, 58 + 34 * w, 15 + 5 * w, 62, 18)


def clip_gather(u):
    w = math.sin(u * math.tau)
    return arms(74 + 13 * w, 74 - 13 * w, 58 + 5 * math.sin(u * math.tau * 2), 18, 18)


def clip_fish(u):
    jerk = max(0.0, math.sin(u * math.tau * 2 - 1.1)) ** 6
    return arms(58, 62 + 20 * jerk, 5 + 2 * math.sin(u * math.tau), 36, 26)


def clip_build(u):
    s = swing((u * 2) % 1)
    return arms(46, lerp(46, 142, s), lerp(22, 6, s), 38, 12)


def clip_knead(u):
    w = math.sin(u * math.tau * 2)
    return arms(62 + 14 * w, 62 - 14 * w, 26 + 5 * abs(w), 34, 34)


CLIPS = [
    ("idle", "Idle", None, 1.667),
    ("walk", "Walk", None, 1.0),
    ("carry", "Walk_Hold", None, 1.0),
    ("chop", "Idle", clip_chop, 1.15),
    ("hoe", "Idle", clip_hoe, 1.35),
    ("mine", "Idle", clip_mine, 1.05),
    ("sow", "Walk", clip_sow, 1.6),
    ("gather", "Idle", clip_gather, 1.7),
    ("fish", "Idle", clip_fish, 3.6),
    ("build", "Idle", clip_build, 1.25),
    ("knead", "Idle", clip_knead, 1.6),
]


def box(center, size, color, rotation=None):
    c = np.array(center, float)
    h = np.array(size, float) / 2
    R = rotation if rotation is not None else np.eye(3)
    faces = [
        ([0, 1, 0], [[-1, 1, -1], [-1, 1, 1], [1, 1, 1], [1, 1, -1]]),
        ([0, -1, 0], [[-1, -1, 1], [-1, -1, -1], [1, -1, -1], [1, -1, 1]]),
        ([0, 0, 1], [[-1, -1, 1], [1, -1, 1], [1, 1, 1], [-1, 1, 1]]),
        ([0, 0, -1], [[1, -1, -1], [-1, -1, -1], [-1, 1, -1], [1, 1, -1]]),
        ([1, 0, 0], [[1, -1, 1], [1, -1, -1], [1, 1, -1], [1, 1, 1]]),
        ([-1, 0, 0], [[-1, -1, -1], [-1, -1, 1], [-1, 1, 1], [-1, 1, -1]]),
    ]
    p, n, col, idx = [], [], [], []
    for normal, corners in faces:
        base = len(p)
        for q in corners:
            p.append(c + R @ (np.array(q) * h))
            n.append(R @ np.array(normal, float))
            col.append(color)
        idx += [base, base + 2, base + 1, base, base + 3, base + 2]
    return np.array(p), np.array(n), np.array(col, float), np.array(idx)


def merge(parts):
    p, n, c, idx, base = [], [], [], [], 0
    for a, b, d, e in parts:
        p.append(a); n.append(b); c.append(d); idx.append(e + base); base += len(a)
    return np.vstack(p), np.vstack(n), np.vstack(c), np.concatenate(idx)


def gltf_tool(name):
    g = Gltf(SOURCE / f"{name}.gltf")
    atlas = g.atlas()
    prim = g.json["meshes"][0]["primitives"][0]
    p = g.accessor(prim["attributes"]["POSITION"]).astype(float)
    n = g.accessor(prim["attributes"]["NORMAL"]).astype(float)
    uv = g.accessor(prim["attributes"]["TEXCOORD_0"])
    color = sample(atlas, uv)
    index = g.accessor(prim["indices"]).astype(np.int64)
    axis = int(np.argmax(p.max(0) - p.min(0)))
    if axis != 1:
        swap = np.eye(3)[[axis, 1 if axis != 1 else 0, 3 - axis - 1]] if False else np.eye(3)
        order = [0, 1, 2]
        order[axis], order[1] = order[1], order[axis]
        swap = np.eye(3)[order]
        p, n = p @ swap.T, n @ swap.T
        if np.linalg.det(swap) < 0:
            index = index[:, ] if index.ndim > 1 else index.reshape(-1, 3)[:, [0, 2, 1]].ravel()
    low = p.min(0)
    high = p.max(0)
    center = (low + high) / 2
    p = p - np.array([center[0], low[1] + (high[1] - low[1]) * 0.18, center[2]])
    p = p / (high[1] - low[1])
    return p, n, color, index


def sample(atlas, uv):
    h, w = atlas.shape[:2]
    x = np.clip((uv[:, 0] * w).astype(int), 0, w - 1)
    y = np.clip((uv[:, 1] * h).astype(int), 0, h - 1)
    return atlas[y, x]


def tools():
    wood = (0.47, 0.32, 0.2)
    stone = (0.56, 0.56, 0.54)
    axe = gltf_tool("Axe_Wood")
    hoe = gltf_tool("Shovel_Wood")
    pick = merge([
        box((0, 0.36, 0), (0.05, 1.0, 0.05), wood),
        box((0, 0.82, 0.0), (0.09, 0.09, 0.62), stone),
        box((0, 0.8, 0.34), (0.06, 0.07, 0.12), stone),
        box((0, 0.8, -0.34), (0.06, 0.07, 0.12), stone),
    ])
    hammer = merge([box((0, 0.3, 0), (0.07, 0.86, 0.07), wood), box((0, 0.72, 0), (0.2, 0.2, 0.42), stone)])
    rod = merge([box((0, 0.42, 0), (0.035, 1.2, 0.035), wood), box((0, 1.02, 0), (0.018, 0.22, 0.018), (0.7, 0.68, 0.6))])
    return {"axe": (axe, 1.75), "hoe": (hoe, 2.25), "pick": (pick, 2.05), "hammer": (hammer, 1.55), "rod": (rod, 4.2)}


def write_mesh(path, positions, normals, colors, joints, weights, indices):
    with path.open("wb") as f:
        f.write(struct.pack("<ii", len(positions), len(indices)))
        for array in (positions, normals, colors, joints, weights):
            f.write(np.asarray(array, dtype="<f4").tobytes())
        f.write(np.asarray(indices, dtype="<i4").tobytes())


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    reference = None
    for short, name in CHARACTERS.items():
        g = Gltf(SOURCE / f"{name}.gltf")
        rig = Rig(g)
        if reference is None:
            reference = rig
        difference = float(np.abs(rig.inverse - reference.inverse).max())
        if difference > 1e-3:
            raise SystemExit(f"{name} has a different bind pose ({difference})")
        prim = g.json["meshes"][0]["primitives"][0]
        a = prim["attributes"]
        weights = g.accessor(a["WEIGHTS_0"]).astype(float)
        weights = weights / np.maximum(weights.sum(1, keepdims=True), 1e-6)
        color = sample(g.atlas(), g.accessor(a["TEXCOORD_0"]))
        write_mesh(OUTPUT / f"{short}.dcmesh", g.accessor(a["POSITION"]), g.accessor(a["NORMAL"]), color,
                   g.accessor(a["JOINTS_0"]).astype(float), weights, g.accessor(prim["indices"]).astype(np.int64))

    rig = reference
    frames, table = [], []
    for clip, base, offsets, seconds in CLIPS:
        count = max(2, int(round(seconds * FPS)))
        table.append((clip, len(frames), count))
        for i in range(count):
            u = i / count
            frames.append(rig.skin_frame(base, u * (rig.channels(base)[1] if offsets is None else seconds), offsets(u) if offsets else None))
    matrices = np.array(frames)[:, :, :3, :]
    with (OUTPUT / "rig.bin").open("wb") as f:
        f.write(struct.pack("<iiii", len(rig.joints), len(frames), len(table), FPS))
        for clip, start, count in table:
            f.write(struct.pack("<ii", start, count))
        f.write(matrices.astype("<f4").tobytes())

    fist = rig.joints.index(rig.names["Fist.R"])
    hold = rig.globals("Idle", 0.0, arms(90, 90))[rig.names["Fist.R"]]
    bind = np.linalg.inv(rig.inverse[fist])
    for short, ((p, n, c, idx), length) in tools().items():
        world = np.eye(4)
        world[:3, :3] = np.eye(3) * length
        world[:3, 3] = hold[:3, 3]
        local = np.linalg.inv(hold) @ world
        to_bind = bind @ local
        ph = np.c_[p, np.ones(len(p))]
        pos = (to_bind @ ph.T).T[:, :3]
        rotation = to_bind[:3, :3]
        nor = (np.linalg.inv(rotation).T @ n.T).T
        nor /= np.maximum(np.linalg.norm(nor, axis=1, keepdims=True), 1e-6)
        joints = np.zeros((len(p), 4)); joints[:, 0] = fist
        weights = np.zeros((len(p), 4)); weights[:, 0] = 1
        write_mesh(OUTPUT / f"tool_{short}.dcmesh", pos, nor, c, joints, weights, idx)

    (OUTPUT / "clips.txt").write_text("\n".join(f"{c} {s} {n}" for c, s, n in table) + "\n", encoding="utf-8")
    (OUTPUT / "SOURCE.txt").write_text(
        "Characters, axe and shovel: Quaternius Cube World Kit, CC0 1.0\n"
        "https://quaternius.com/packs/cubeworldkit.html\n"
        "Modified by tools/bake_characters.py: mesh and animation baked; work clips and simple tools generated.\n", encoding="utf-8")
    size = sum(p.stat().st_size for p in OUTPUT.iterdir())
    print(f"joints={len(rig.joints)} frames={len(frames)} clips={len(table)} output_kb={size // 1024}")
    print("clips", [(c, s, n) for c, s, n in table])


if __name__ == "__main__":
    main()
