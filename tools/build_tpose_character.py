"""Builds the 2,000-triangle textured T-pose woodcutter and symmetric joint landmarks.

Run with .tools/hunyuan3d-env/python.exe. Rigging happens afterwards in tools/rig_ual_character.py.
"""
import json
import sys
from pathlib import Path

import numpy as np
import trimesh
import xatlas
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools"))
import build_hq_character as hq  # noqa: E402

BASE = ROOT / "assets-candidates" / "hunyuan3d" / (sys.argv[1] if len(sys.argv) > 1 else "woodcutter-v2")
OUTPUT = BASE / "game"
hq.BASE = BASE
hq.MASK_LEFT_PROPS = False
ARM_HEIGHT = 1.386  # The approved woodcutter's arm line. Hats must not shrink the body.
HAT_BOTTOM = 1.60
HAT_STRAW_BOTTOM = 1.76


def middle(values):
    return float((values.min() + values.max()) / 2)


def band(v, y, half=0.02, x_low=-9.0, x_high=9.0):
    return v[(np.abs(v[:, 1] - y) < half) & (v[:, 0] > x_low) & (v[:, 0] < x_high)]


def center_on_torso(v):
    torso = band(v, 1.05, 0.08, -0.35, 0.35)
    v = v.copy()
    v[:, 0] -= middle(torso[:, 0])
    v[:, 2] -= middle(torso[:, 2])
    return v


def scale_to_arm_height(v):
    """Scales from the T-pose arm line, because total height changes with hats and hair."""
    tip = (v[:, 0].max() - v[:, 0].min()) / 2
    arm = v[(np.abs(v[:, 0]) > tip * 0.55) & (np.abs(v[:, 0]) < tip * 0.75)]
    return v * (ARM_HEIGHT / middle(arm[:, 1]))


def landmarks(v):
    """Keeps the spine straight and mirrors both limbs, so the rest pose cannot lean."""
    out = {}
    pelvis_z = middle(band(v, 0.93, 0.03, -0.2, 0.2)[:, 2])
    neck_z = middle(band(v, 1.47, 0.015, -0.1, 0.1)[:, 2])
    head_z = middle(band(v, 1.56, 0.02, -0.12, 0.12)[:, 2])
    out["hips"] = (0.0, 0.93, pelvis_z)
    out["neck"] = (0.0, 1.45, neck_z)
    out["head"] = (0.0, 1.53, head_z)

    tip = (v[:, 0].max() - v[:, 0].min()) / 2
    wrist_x = tip - 0.205
    shoulder_x = 0.205
    def arm_point(x):
        samples = []
        for side in (1, -1):
            part = v[np.abs(v[:, 0] * side - x) < 0.018]
            samples.append((middle(part[:, 1]), middle(part[:, 2])))
        return np.mean(samples, axis=0)
    wrist = arm_point(wrist_x)
    elbow_x = (shoulder_x + wrist_x) / 2
    elbow = arm_point(elbow_x)
    shoulder_y = float(elbow[0] + (elbow[0] - wrist[0]))
    shoulder_z = float(elbow[1] + (elbow[1] - wrist[1]))
    for side, a, f, h in ((1, "armL", "foreL", "handL"), (-1, "armR", "foreR", "handR")):
        out[a] = (side * shoulder_x, shoulder_y, shoulder_z)
        out[f] = (side * elbow_x, float(elbow[0]), float(elbow[1]))
        out[h] = (side * wrist_x, float(wrist[0]), float(wrist[1]))

    knee, ankle = [], []
    for side in (1, -1):
        for y, store in ((0.49, knee), (0.11, ankle)):
            part = band(v, y, 0.02)
            part = part[part[:, 0] * side > 0.01]
            store.append((abs(middle(part[:, 0])), middle(part[:, 2])))
    knee, ankle = np.mean(knee, axis=0), np.mean(ankle, axis=0)
    hip_x = max(0.09, float(knee[0]) * 0.72)
    for side, leg, shin, foot in ((1, "legL", "shinL", "footL"), (-1, "legR", "shinR", "footR")):
        out[leg] = (side * hip_x, 0.88, pelvis_z)
        out[shin] = (side * float(knee[0]), 0.49, float(knee[1]))
        out[foot] = (side * float(ankle[0]), 0.10, float(ankle[1]))
    return {k: [round(float(c), 5) for c in p] for k, p in out.items()}


def revolve_hat(position, normal, color, images, v):
    """Colors a round hat's top from its front silhouette, since no view shows the upper brim."""
    view = images["front"]
    ax, bx, ay, by = hq.view_mapping(view, v[:, 0], v[:, 1])
    hat = (position[:, 1] > HAT_BOTTOM) & (normal[:, 1] > 0.15)
    center_z = float(np.median(position[hat, 2]))
    radius = np.hypot(position[hat, 0], position[hat, 2] - center_z)
    side = np.where(position[hat, 0] >= 0, 1.0, -1.0)
    # Read only the visible straw above the front brim edge, because lower pixels show the face and hair.
    top = float(position[:, 1].max())
    straw_y = top - (radius / radius.max()) * (top - HAT_STRAW_BOTTOM)
    rgba = hq.sample(view, ax * side * radius * 0.5 + bx, ay * straw_y + by)
    solid = rgba[:, 3] > 0.6
    result = color.copy()
    indices = np.nonzero(hat)[0][solid]
    result[indices] = rgba[solid, :3]
    print("HAT_TEXELS", int(hat.sum()), "recolored", int(solid.sum()))
    return result


def main():
    if BASE.name.startswith("farmer"):
        hq.POST_COLOR = revolve_hat
    OUTPUT.mkdir(parents=True, exist_ok=True)
    v, f = hq.load_mesh()
    source_faces = len(f)
    v = center_on_torso(scale_to_arm_height(v))
    marks = landmarks(v)  # Measured on the dense mesh: 2,000 triangles leave narrow bands empty.
    v, f = hq.decimate(v, f)
    mesh = trimesh.Trimesh(v, f, process=False)
    vmapping, indices, uvs = xatlas.parametrize(v, f)
    atlas_v, atlas_n = v[vmapping], np.asarray(mesh.vertex_normals)[vmapping]
    texture, coverage = hq.bake_texture(atlas_v, atlas_n, indices, uvs)
    Image.fromarray((texture * 255).astype(np.uint8)).save(OUTPUT / "woodcutter_albedo.png")
    count = len(atlas_v)
    weights = np.zeros((count, 4))
    weights[:, 0] = 1
    hq.write_mesh(OUTPUT / "woodcutter.dchq", atlas_v, atlas_n, uvs, np.ones((count, 3)), np.zeros((count, 4)), weights, indices.ravel())
    (OUTPUT / "landmarks.json").write_text(json.dumps(marks, indent=1), encoding="utf-8")
    report = {"source_triangles": int(source_faces), "body_triangles": int(len(f)), "uv_vertices": count, "texture_coverage": round(coverage, 4),
              "height": round(float(v[:, 1].max() - v[:, 1].min()), 3), "arm_span": round(float(v[:, 0].max() - v[:, 0].min()), 3)}
    (OUTPUT / "build-report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print("BUILD_OK", json.dumps(report))
    print("LANDMARKS", json.dumps(marks))


if __name__ == "__main__":
    main()
