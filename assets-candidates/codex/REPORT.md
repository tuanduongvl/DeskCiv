# DeskCiv asset candidate pack — Codex

## Generation and rerun

The assets were generated from scratch with a deterministic Python script using `trimesh`, NumPy, Pillow and Matplotlib. No third-party models or textures were used. Blender was unavailable. From the repository root, run:

```powershell
python assets-candidates/codex/source/generate.py
python assets-candidates/codex/source/validate.py
```

The generator also writes a standalone 512×512 palette swatch image and the three PNG previews. It does not embed that image in the GLBs.

## Read-back measurements

Bounds are listed as X × Z × height (meters), reordered from the GLB's X/Y/Z bounds. Triangle counts and material counts were measured after reopening each exported GLB with `trimesh`. “Pass” covers only the listed dimension and triangle limits.

| Asset | Triangles | Bounds X × Z × height (m) | Limit result |
|---|---:|---:|---|
| `buildings/hut.glb` | 700 | 5.10 × 5.10 × 7.45 | Fail — height exceeds 5.5 m |
| `buildings/storehouse.glb` | 1,148 | 7.03 × 7.02 × 6.77 | Pass |
| `buildings/bakery.glb` | 1,188 | 7.41 × 5.38 × 6.85 | Fail — height exceeds 6.5 m |
| `nature/tree.glb` | 368 | 5.70 × 5.00 × 8.60 | Pass |
| `nature/wheat_patch.glb` | 200 | 1.96 × 1.64 × 0.89 | Pass dimensions/triangles; patch is undersized |
| `tools/axe.glb` | 184 | 0.58 × 0.24 × 0.88 | Fail — 64 triangles over limit |
| `tools/sickle.glb` | 46 | 0.25 × 0.10 × 0.41 | Pass dimensions/triangles; shorter than target length |
| `characters/farmer.glb` | 656 | 0.72 × 0.77 × 2.06 | Fail — taller than about 1.8 m |
| `characters/woodcutter.glb` | 760 | 0.73 × 0.79 × 1.90 | Pass triangle limit; slightly taller than target |

Each GLB reopened successfully and reports one material. The `source/validate.py` script currently prints file readability, triangle count, bounds, and material count. It does **not** validate doorway dimensions, ground-centered origins, outward normals, manifoldness, glTF up-axis, skin joints, animation clips/durations, weight limits, or embedded texture data.

## Sources and licenses

All geometry and color choices were created from scratch for this pack. No external assets or sources were used.

## Known defects and unfulfilled requirements

- Both character GLBs are static meshes. They do not contain the required 17-joint skin, weights, animations, or hand-attached preview tools.
- The per-asset materials use flat base colors; the palette PNG is separate and is not embedded or used as a texture atlas. GLBs do not meet the required shared embedded-atlas material setup.
- Hut and bakery exceed their height limits. The axe exceeds its triangle limit. Farmer exceeds the approximate height target. The wheat patch is much smaller than its 4 × 4 m target, and the sickle is shorter than its 0.6 m target.
- The geometry and preview renderer are a rough procedural candidate, not a polished hand-crafted asset pack. In particular, the preview does not show three character views or distinct key work poses. The distance preview omits the axe and sickle and does not demonstrate all assets together at a calibrated 45 m camera distance.
- `previews/buildings.png`, `previews/characters.png`, and `previews/distance.png` are 1920 × 1080 PNGs, but their visuals have not been independently inspected for clipping or presentation quality.
