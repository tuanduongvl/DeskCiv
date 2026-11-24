"""Composes DeskCiv buildings, rotors, trees and props from Kenney's CC0 Fantasy Town Kit and Nature Kit.

Run: .tools\\hunyuan3d-env\\python.exe -I tools\\build_kit_buildings.py
Input: assets-source/kenney/fantasy-town-kit (https://kenney.nl/assets/fantasy-town-kit)
       assets-source/kenney/nature-kit (https://kenney.nl/assets/nature-kit)
Output: game/assets/buildings/*.dckit, flat-shaded triangles with vertex colors.
Format: int32 vertex count, then positions, normals and RGB colors as float32 triples.
"""
import struct
from pathlib import Path

import numpy as np
import trimesh
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
KIT = ROOT / "assets-source" / "kenney" / "fantasy-town-kit" / "Models" / "GLB format"
OUT = ROOT / "game" / "assets" / "buildings"
NATURE = ROOT / "assets-source" / "kenney" / "nature-kit" / "Models" / "GLTF format"
COLORMAP = np.asarray(Image.open(KIT / "Textures" / "colormap.png").convert("RGB"), dtype=np.float32) / 255
CELL = 2.25

THATCH, TILE, SLATE, ZINC = (0.76, 0.61, 0.33), (0.66, 0.31, 0.2), (0.31, 0.35, 0.41), (0.47, 0.49, 0.5)
WOOD, PLASTER, STONE, BRICK, CONCRETE = (0.5, 0.36, 0.23), (0.82, 0.75, 0.62), (0.63, 0.61, 0.56), (0.6, 0.29, 0.22), (0.75, 0.75, 0.73)
TIERS = {
    0: dict(walls="wall-wood", roof=THATCH, tint=WOOD),
    1: dict(walls="wall-wood", roof=TILE, tint=PLASTER),
    2: dict(walls="wall", roof=SLATE, tint=STONE),
    3: dict(walls="wall", roof=SLATE, tint=BRICK),
    4: dict(walls="wall", roof=ZINC, tint=CONCRETE),
}
_cache = {}

# Nature Kit pieces are colored by material. These map its bright palette to the game's muted one.
NATURE_COLORS = {
    "grass": (0.38, 0.53, 0.25), "dirt": (0.46, 0.37, 0.27), "_defaultMat": (0.56, 0.55, 0.51), "stone": (0.6, 0.59, 0.55),
    "wood": (0.55, 0.39, 0.24), "woodDark": (0.4, 0.28, 0.18), "woodBark": (0.42, 0.3, 0.19), "woodBarkDark": (0.33, 0.23, 0.15),
    "woodInner": (0.78, 0.65, 0.46), "colorRed": (0.76, 0.24, 0.2), "colorRedDark": (0.58, 0.19, 0.16), "colorYellow": (0.94, 0.8, 0.32),
    "colorPurple": (0.66, 0.46, 0.74), "colorTan": (0.8, 0.66, 0.48), "leafsGreen": (0.36, 0.52, 0.24), "leafsDark": (0.27, 0.42, 0.2),
    "leafsFall": (0.55, 0.39, 0.24), "corn": (0.9, 0.78, 0.4),
}


def load_nature(name):
    scene = trimesh.load(NATURE / f"{name}.glb", force="scene")
    corners, colors = [], []
    for node in scene.graph.nodes_geometry:
        transform, geometry_name = scene.graph[node]
        mesh = scene.geometry[geometry_name]
        material = getattr(getattr(mesh.visual, "material", None), "name", None)
        corners.append(trimesh.transform_points(mesh.vertices, transform)[mesh.faces])
        colors.append(np.full((len(mesh.faces), 3, 3), NATURE_COLORS.get(material, (0.6, 0.6, 0.6)), dtype=np.float32))
    return np.concatenate(corners), np.concatenate(colors)


def load(name):
    """Returns triangle corners (n, 3, 3) and their colors (n, 3, 3) for one kit piece."""
    if name in _cache:
        return _cache[name]
    if name.startswith("nature/"):
        _cache[name] = load_nature(name[7:])
        return _cache[name]
    scene = trimesh.load(KIT / f"{name}.glb", force="scene")
    corners, colors = [], []
    for node in scene.graph.nodes_geometry:
        transform, geometry_name = scene.graph[node]
        mesh = scene.geometry[geometry_name]
        points = trimesh.transform_points(mesh.vertices, transform)
        corners.append(points[mesh.faces])
        uv = getattr(mesh.visual, "uv", None)
        if uv is None:
            colors.append(np.full((len(mesh.faces), 3, 3), 0.6, dtype=np.float32))
            continue
        height, width = COLORMAP.shape[:2]
        # Each triangle takes one palette color, so edges stay crisp.
        center = uv[mesh.faces].mean(axis=1)
        px = np.clip((center[:, 0] % 1) * width, 0, width - 1).astype(int)
        py = np.clip((1 - center[:, 1] % 1) * height, 0, height - 1).astype(int)
        colors.append(np.repeat(COLORMAP[py, px][:, None, :], 3, axis=1))
    _cache[name] = (np.concatenate(corners), np.concatenate(colors))
    return _cache[name]


def rotate_y(points, degrees):
    a = np.radians(degrees)
    c, s = np.cos(a), np.sin(a)
    out = points.copy()
    out[..., 0] = points[..., 0] * c + points[..., 2] * s
    out[..., 2] = -points[..., 0] * s + points[..., 2] * c
    return out


def recolor(colors, target):
    """Moves a piece to a material color and keeps its light and dark detail."""
    light = colors @ np.array([0.3, 0.59, 0.11])
    mean = max(float(light.mean()), 0.05)
    return np.clip(np.asarray(target) * (0.72 + 0.28 * light[..., None] / mean), 0, 1)


class Model:
    def __init__(self):
        self.tris, self.cols = [], []

    def add(self, name, at=(0, 0, 0), yaw=0, scale=CELL, stretch=(1, 1, 1), color=None):
        tris, cols = load(name)
        points = tris * np.asarray(stretch) * scale
        points = rotate_y(points, yaw) + np.asarray(at)
        self.tris.append(points)
        self.cols.append(recolor(cols, color) if color is not None else cols)
        return self

    def prism(self, a, b, c, color):
        self.tris.append(np.array([[a, b, c]], dtype=np.float64))
        self.cols.append(np.full((1, 3, 3), color, dtype=np.float32))

    def write(self, name, occlusion=True):
        tris = np.concatenate(self.tris).astype(np.float32)
        cols = np.concatenate(self.cols).astype(np.float32)
        if occlusion:
            # Darken surfaces near the ground, a cheap stand-in for ambient occlusion.
            t = np.clip(tris[..., 1] / 1.6, 0, 1)
            cols = cols * (0.66 + 0.34 * t * t * (3 - 2 * t))[..., None]
        # Godot's front faces wind clockwise, the opposite of glTF.
        tris = tris[:, [0, 2, 1]]
        cols = cols[:, [0, 2, 1]]
        normal = np.cross(tris[:, 2] - tris[:, 0], tris[:, 1] - tris[:, 0])
        length = np.linalg.norm(normal, axis=1, keepdims=True)
        keep = length[:, 0] > 1e-8
        tris, cols, normal = tris[keep], cols[keep], (normal[keep] / length[keep]).astype(np.float32)
        normals = np.repeat(normal[:, None, :], 3, axis=1)
        OUT.mkdir(parents=True, exist_ok=True)
        with (OUT / f"{name}.dckit").open("wb") as f:
            f.write(struct.pack("<i", tris.shape[0] * 3))
            f.write(tris.reshape(-1, 3).tobytes())
            f.write(normals.reshape(-1, 3).tobytes())
            f.write(cols.reshape(-1, 3).tobytes())
        print(f"{name:20} tris={tris.shape[0]:5} size={np.round(tris.reshape(-1, 3).max(0) - tris.reshape(-1, 3).min(0), 1).tolist()}")


def house(m, wide, deep, floors, tier, at=(0, 0, 0), door=True, roof_rise=1.0):
    """Walls on a grid with a gabled roof. The door faces +Z and the ridge runs along X."""
    style = TIERS[tier]
    base, tint = style["walls"], style["tint"]
    ox, oy, oz = at
    half_w, half_d = wide * CELL / 2, deep * CELL / 2
    window = f"{base}-window-shutters"
    for floor in range(floors):
        y = oy + floor * CELL
        for i in range(wide):
            x = ox + (i - (wide - 1) / 2) * CELL
            front = f"{base}-door" if door and floor == 0 and i == wide // 2 else window if (i + floor) % 2 == 0 or floor else base
            m.add(front, (x, y, oz + (deep - 1) / 2 * CELL), -90, color=tint if tier == 0 or not front.endswith("door") else None)
            m.add(window if i % 2 == 0 else base, (x, y, oz - (deep - 1) / 2 * CELL), 90, color=tint)
        for k in range(deep):
            z = oz + (k - (deep - 1) / 2) * CELL
            m.add(window if k % 2 == 0 and floor else base, (ox + (wide - 1) / 2 * CELL, y, z), 0, color=tint)
            m.add(window if k % 2 == 1 and floor else base, (ox - (wide - 1) / 2 * CELL, y, z), 180, color=tint)
    top = oy + floors * CELL
    tris, _ = load("roof-gable")
    extent = tris.reshape(-1, 3)
    peak = extent[extent[:, 1] > extent[:, 1].max() - 0.02]
    # Turn the piece so that its ridge runs along X.
    yaw = 0 if np.ptp(peak[:, 0]) >= np.ptp(peak[:, 2]) else 90
    across = (deep + 0.12)
    for i in range(wide):
        x = ox + (i - (wide - 1) / 2) * CELL
        stretch = (1.0, roof_rise * (1 + deep * 0.28), across) if yaw == 0 else (across, roof_rise * (1 + deep * 0.28), 1.0)
        m.add("roof-gable", (x, top, oz), yaw, stretch=stretch, color=style["roof"])
    rise = float(extent[:, 1].max()) * CELL * roof_rise * (1 + deep * 0.28)
    gable = np.asarray(tint if tint is not None else WOOD, dtype=np.float32) * 0.92
    for side in (-1, 1):
        x = ox + side * (half_w - 0.14)
        m.prism((x, top, oz - half_d), (x, top + rise * 0.96, oz), (x, top, oz + half_d), gable)
        m.prism((x, top, oz - half_d), (x, top, oz + half_d), (x, top + rise * 0.96, oz), gable)
    return top + rise


def chimney(m, at, floors, tier):
    color = BRICK if tier != 3 else STONE
    m.add("chimney", (at[0], (floors - 0.55) * CELL, at[1]), 0, stretch=(1.25, 1.05, 1.25), color=color)


def plinth(m, wide, deep):
    m.tris.append(_box(wide * CELL + 0.5, 0.18, deep * CELL + 0.5))
    m.cols.append(np.full((12, 3, 3), (0.46, 0.45, 0.41), dtype=np.float32))


def box(m, center, size, color):
    tris = _box(size[0], size[1], size[2]) + np.array([center[0], center[1] - size[1] / 2, center[2]])
    m.tris.append(tris)
    m.cols.append(np.full((12, 3, 3), color, dtype=np.float32))


def _box(w, h, d):
    x, z = w / 2, d / 2
    v = np.array([[-x, 0, -z], [x, 0, -z], [x, 0, z], [-x, 0, z], [-x, h, -z], [x, h, -z], [x, h, z], [-x, h, z]])
    f = [(4, 6, 5), (4, 7, 6), (0, 1, 2), (0, 2, 3), (3, 2, 6), (3, 6, 7), (1, 0, 4), (1, 4, 5), (2, 1, 5), (2, 5, 6), (0, 3, 7), (0, 7, 4)]
    return v[np.array(f)]


def build(kind, tier):
    m = Model()
    if kind == "Hut":
        size = {1: (3, 2, 1), 2: (3, 3, 2), 3: (3, 3, 2)}[tier]
        plinth(m, size[0], size[1])
        house(m, *size, tier)
        if tier >= 2:
            chimney(m, (CELL * 0.8, -CELL * 0.45), size[2] + 0.4, tier)
    elif kind == "Storehouse":
        wide, deep, floors = 4, 3, 1 if tier < 2 else 2
        plinth(m, wide, deep)
        house(m, wide, deep, floors, tier)
        m.add("cart", (-5.2, 0, 3.0), 25, scale=1.7)
    elif kind == "Gatherer":
        plinth(m, 2, 2)
        house(m, 2, 2, 1, tier, (0, 0, -0.5))
        m.add("stall", (2.9, 0, 1.2), 0, scale=1.9)
    elif kind == "Fisher":
        plinth(m, 2, 2)
        house(m, 2, 2, 1, tier, (0, 0, -0.5))
        m.add("poles", (2.1, 0, 1.0), 0, scale=2.0).add("planks", (3.0, 0.08, 1.0), 0, scale=1.6)
    elif kind == "Woodcutter":
        plinth(m, 2, 2)
        house(m, 2, 2, 1, tier, (0, 0, -0.5))
        m.add("planks", (2.9, 0.06, 1.2), 15, scale=1.7).add("planks", (2.9, 0.2, 1.2), 40, scale=1.5)
    elif kind == "Mill":
        plinth(m, 3, 3)
        house(m, 3, 3, 2 if tier >= 2 else 1, tier)
    elif kind == "Bakery":
        plinth(m, 3, 3)
        house(m, 3, 3, 1, tier)
        chimney(m, (CELL * 0.9, -CELL * 0.5), 1.4, tier)
        m.add("stall-red", (-4.6, 0, 3.0), 90, scale=1.9)
    elif kind == "Winery":
        plinth(m, 4, 3)
        house(m, 4, 3, 1, tier)
        m.add("banner-red", (0, 1.1, 3.6), -90, scale=1.8)
    elif kind == "Smithy":
        plinth(m, 3, 3)
        house(m, 3, 3, 1, tier)
        chimney(m, (CELL * 0.9, -CELL * 0.6), 1.6, tier)
        m.add("stall-green", (-4.6, 0, 2.8), 90, scale=1.9)
    elif kind == "Watermill":
        plinth(m, 3, 2)
        house(m, 3, 2, 1, tier, (0, 0, 1.2))
        # Stone race and axle that carry the water wheel behind the house.
        box(m, (0, 0.45, -3.3), (9.4, 1.3, 1.2), (0.55, 0.54, 0.5))
        for side in (-1, 1):
            box(m, (side * 1.5, 1.2, -4.6), (0.5, 2.6, 3.4), (0.5, 0.49, 0.45))
        box(m, (0, 2.05, -2.6), (3.4, 0.28, 0.28), (0.37, 0.26, 0.17))
    m.write(f"{kind.lower()}_{tier}")


def rotor(name, piece, radius):
    """Kit rotors spin around X. The game's moving shader spins them around Z."""
    m = Model()
    tris, _ = load(piece)
    span = float(np.abs(tris[..., 1]).max())
    m.add(piece, yaw=-90, scale=radius / span)
    m.write(name, occlusion=False)


def tree(name, piece, height):
    """The tree shader tints near-white leaf colors by season, so leaves are stored as light values."""
    m = Model()
    tris, cols = load(piece)
    scale = height / float(tris[..., 1].max())
    leaf = (cols[..., 1] >= cols[..., 0]) & (cols[..., 1] > cols[..., 2])
    light = cols @ np.array([0.3, 0.59, 0.11])
    mean = float(light[leaf].mean()) if leaf.any() else 0.5
    shaded = cols.copy()
    shaded[leaf] = np.clip(0.9 * (light[leaf] / mean), 0.74, 1.0)[:, None]
    m.tris.append(tris * scale)
    m.cols.append(shaded)
    m.write(name)


def prop(name, pieces):
    """A prop from Nature Kit pieces, given as (piece, position, yaw, scale), with its base at y = 0."""
    m = Model()
    for piece, at, yaw, scale in pieces:
        tris, _ = load(f"nature/{piece}")
        # Nature pieces sit 0.05 units below their origin, so lift them onto the ground.
        m.add(f"nature/{piece}", (at[0], at[1] - float(tris[..., 1].min()) * scale, at[2]), yaw, scale=scale)
    m.write(name, occlusion=not name.startswith("scatter_"))


def props():
    prop("prop_logs", [("log_stack", (0, 0, 0), 90, 4.2)])
    prop("prop_stump", [("stump_round", (0, 0, 0), 0, 3.6)])
    prop("prop_canoe", [("canoe", (0, 0, 0), 90, 4.4), ("canoe_paddle", (0.2, 0.5, 0.35), 80, 3.2)])
    prop("prop_campfire", [("campfire_stones", (0, 0, 0), 0, 3.0)])
    prop("prop_pot", [("pot_large", (0, 0, 0), 0, 2.6)])
    prop("prop_rock_tall", [("rock_tallA", (0, 0, 0), 0, 3.8)])
    prop("prop_rock_mid", [("rock_tallC", (0, 0, 0), 30, 3.4)])
    prop("prop_rock_large", [("rock_largeD", (0, 0, 0), 0, 2.6)])
    prop("prop_rubble", [("stone_smallFlatB", (0, 0, 0), 0, 3), ("stone_smallA", (0.7, 0, 0.3), 40, 2.4), ("stone_smallC", (-0.5, 0, 0.5), 10, 2.6)])
    prop("prop_revetment", [("rock_largeB", (0, 0, 0), 90, 2.4)])
    prop("scatter_pebbles", [("rock_smallA", (0, 0, 0), 0, 1.7), ("rock_smallC", (0.52, 0, 0.22), 50, 1.3)])
    prop("scatter_flowers", [("flower_yellowA", (0, 0, 0), 0, 2.2), ("flower_purpleA", (0.3, 0, 0.2), 70, 2.0)])
    prop("scatter_grass", [("grass_leafs", (0, 0, 0), 0, 2.4)])


def main():
    props()
    for kind in ("Storehouse", "Gatherer", "Fisher", "Woodcutter", "Mill", "Bakery", "Winery", "Smithy", "Watermill"):
        for tier in range(5):
            build(kind, tier)
    for tier in (1, 2, 3):
        build("Hut", tier)
    rotor("rotor_windmill", "windmill", 5.5)
    rotor("rotor_watermill", "watermill", 2.5)
    tree("tree_pine", "tree-high", 8.8)
    tree("tree_broad", "tree", 7.6)
    tree("tree_crooked", "tree-high-crooked", 8.4)


if __name__ == "__main__":
    main()
