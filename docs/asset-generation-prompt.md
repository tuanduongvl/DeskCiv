# DeskCiv asset test-pack prompt

Replace `<TOOL_NAME>` with `codex` or `gemini`, then give everything below the line to that tool.

---

You are creating a small 3D asset test pack for DeskCiv, a Windows live wallpaper that simulates a fictional settlement. The game uses Godot 4.7 with the Compatibility renderer. We will compare your pack with the current procedural models, so follow the specification exactly.

## Output and boundaries

- Write only inside `assets-candidates/<TOOL_NAME>/`.
- Do not edit `game/`, `tools/`, `tests/`, or any existing file.
- Read `tools/bake_villagers.py` and `game/src/MeshKit.cs` only to understand the current scale and content.
- Do not commit, push, or install software globally. If you need a Python package, use a virtual environment inside your output folder and exclude it from the final file list.
- Build the pack with a reproducible script in `assets-candidates/<TOOL_NAME>/source/`. Blender in background mode is allowed if it is already installed. Otherwise generate valid glTF directly with Python.

## Art direction

Create an original, hand-crafted low-poly look for a Neolithic river village.

- The viewer sees the assets from about 30 to 60 meters away, so silhouettes and activities must read clearly at that distance.
- Use believable human proportions, warm natural materials, chunky readable details, and restrained colors.
- Make each job recognizable from clothing, posture, and tool.
- Show age and handmade construction through uneven thatch, irregular timber, stones, lashings, and small props.
- Do not use metal, glass, sawn planks, or medieval architecture in the Neolithic buildings. Use stone, flint, wood, bark, reed, hide, clay, and rope.
- Do not copy or closely imitate any existing game's models, textures, characters, layouts, logos, or art. This includes Knights and Merchants and Banished.
- Do not use AI-generated images that imitate a named artist or game. Do not download third-party assets unless they are CC0 and you record the source URL.

## Required assets

| File | Asset | Size limit in meters (X x Z x height) | Triangle limit |
|---|---|---:|---:|
| `buildings/hut.glb` | Round family house with thatched roof | 7.6 x 7.6 x 5.5 | 2,500 |
| `buildings/storehouse.glb` | Raised communal storehouse with baskets and sacks | 11.5 x 11.5 x 7 | 4,000 |
| `buildings/bakery.glb` | Clay-oven bakehouse with firewood and a smoke opening | 11.5 x 11.5 x 6.5 | 4,000 |
| `nature/tree.glb` | Natural broadleaf or conifer tree | 7 x 7 x 10 | 900 |
| `nature/wheat_patch.glb` | Tileable crop patch at harvest stage | 4 x 4 x 1.1 | 700 |
| `tools/axe.glb` | Stone axe | 0.9 long | 120 |
| `tools/sickle.glb` | Flint or bone sickle | 0.6 long | 120 |
| `characters/farmer.glb` | Rigged and animated adult farmer | about 1.8 tall | 2,500 |
| `characters/woodcutter.glb` | Rigged and animated adult woodcutter | about 1.8 tall | 2,500 |

## Technical rules

- Export glTF 2.0 binary (`.glb`) with embedded data.
- Units are meters. Y is up. The front faces +Z.
- Place each asset's origin at its ground center.
- For buildings, center the main door on the +Z side. The doorway must be at least 1.9 meters high and 1.0 meter wide.
- Apply all transforms. Use outward-facing normals and no non-manifold geometry where it is visible.
- Use one material per asset and one shared 512 x 512 PNG palette atlas embedded in the GLB. Use base color only.
- Do not use normal maps, transparency, lights, cameras, or physics data.
- Keep every asset within its size and triangle limit.

## Character rig

Both characters must use one skin and exactly these 17 joints, in this order. Positions are bind-pose world positions in meters, with the character facing +Z.

| Joint | Parent | Position (X, Y, Z) |
|---|---|---|
| `hips` | none | 0, 0.95, 0 |
| `spine` | `hips` | 0, 1.08, 0 |
| `chest` | `spine` | 0, 1.26, 0 |
| `neck` | `chest` | 0, 1.47, 0 |
| `head` | `neck` | 0, 1.54, 0 |
| `armL` | `chest` | 0.215, 1.42, 0 |
| `foreL` | `armL` | 0.245, 1.14, 0 |
| `handL` | `foreL` | 0.26, 0.89, 0.01 |
| `armR` | `chest` | -0.215, 1.42, 0 |
| `foreR` | `armR` | -0.245, 1.14, 0 |
| `handR` | `foreR` | -0.26, 0.89, 0.01 |
| `legL` | `hips` | 0.1, 0.93, 0 |
| `shinL` | `legL` | 0.105, 0.52, 0 |
| `footL` | `shinL` | 0.105, 0.1, 0 |
| `legR` | `hips` | -0.1, 0.93, 0 |
| `shinR` | `legR` | -0.105, 0.52, 0 |
| `footR` | `shinR` | -0.105, 0.1, 0 |

- Use at most 4 weights per vertex, normalized to 1.
- Bake looping animation clips at 24 FPS with these exact names:
  - Both characters: `idle` (2.4 s), `walk` (0.92 s), `carry` (1.05 s).
  - Farmer: `reap` (1.15 s), `sow` (1.5 s).
  - Woodcutter: `chop` (1.2 s).
- Work clips need clear anticipation, impact, and recovery. A viewer must be able to tell the job from the silhouette alone.
- Keep feet planted during work clips. The last frame must loop cleanly into the first.
- Attach the matching tool to `handR` in the animated preview, but keep it as a separate GLB.

## Required previews

Render PNG previews into `assets-candidates/<TOOL_NAME>/previews/`:

- `buildings.png`: all three buildings at 1920 x 1080 from a 35-degree elevated three-quarter view.
- `characters.png`: both characters at 1920 x 1080, showing the front, side, and the key work pose.
- `distance.png`: all assets at 1920 x 1080 from about 45 meters away, on a plain grass-colored ground.

## Validation and report

Write a script that opens every GLB after export and checks its file, triangle count, bounds, up-axis orientation, material count, joint names and order, animation names and durations, and vertex-weight limits.

Write `assets-candidates/<TOOL_NAME>/REPORT.md` containing:

1. The generation method and how to rerun it.
2. A table with each asset's triangle count, bounds, and pass or fail result.
3. Sources and licenses for anything you did not create from scratch.
4. Known defects. Report them honestly and do not claim success for a check you did not run.

Stop after producing the pack and report. Do not integrate it into the game.
