# DeskCiv character pipeline

Use this process for every new villager. The woodcutter (`woodcutter-v2`) is the approved reference result.

**Status:** The woodcutter runs in the game through `CharacterKit.Textured`. LOD or VAT and a 300-person performance test are not done yet.

When testing, keep Godot windows small (for example 960x540). A window the size of the monitor looks like the wallpaper has taken over the screen.

## Rules

- Use existing tools. Do not hand-write rigs, skin weights or animation when a tool or CC0 clip exists.
- Start from an approved concept image, never from Python-generated blocks.
- Always generate a strict T-pose. The Quaternius skeleton rests in a T-pose with arms at 0 degrees.
- Keep props off the body. Rope, pouches and tools are generated or built separately and attached to a bone.
- Budget: about 2,000 triangles for the body, about 200 for a tool, one 1024 texture.
- Characters must be original. Do not copy a specific game's characters or art.
- Hunyuan3D's license excludes use, distribution and display in the EU, UK and South Korea. Resolve this before any public release.

## Steps

Replace `<role>` with the character name, for example `farmer`, and `vN` with its version.

### 1. Approve the design, then make the T-pose sheet

Generate the design in ChatGPT web. Once it is approved, attach it with the prompt below. **Use a 3:1 canvas**, because 3:2 cannot fit three T-pose figures.

```text
Use the attached character as the exact design reference. Keep the same person, face, hair, clothing, materials and colors.

Create one clean 3D-modeling reference sheet on a 3:1 landscape canvas, with exactly three full-body orthographic views side by side: FRONT, RIGHT SIDE (character faces image-right), BACK. No perspective.

POSE, strict T-pose in all views:
- Arms straight out to the sides, perfectly horizontal at shoulder height, 90 degrees from the torso
- Elbows and wrists straight, palms facing down, fingers straight and slightly apart, thumbs relaxed
- Spine perfectly vertical, shoulders level, head facing straight ahead
- Legs straight, feet shoulder-width apart, toes pointing straight forward
- Body perfectly left-right symmetric

CLOTHING CHANGES for rigging:
- Remove all tools, ropes, pouches and anything hanging from the belt
- Any cape or capelet ends at the top of the shoulders, leaving armpits and upper arms clearly visible
- Sleeves are fitted, with no large rolled cuffs. Hands and wrists fully visible
- Any tunic hem is even and ends at mid-thigh, with a clear gap between the legs
- Keep clear separation between arms and torso, and between both legs

ALIGNMENT: same scale in all three views. Top of head, shoulders, waist, knees and soles line up on the same horizontal lines. Leave empty space around each figure, and make sure the fingertips are not cropped or touching another view.

STYLE: same painterly realistic game-asset texture as the reference, flat even lighting, no cast shadows, plain light gray background. No text, labels, grid, props, or extra characters.
```

Save it as `assets-candidates/concepts/<role>-tpose/01-orthographic-tpose.png`.

### 2. Cut the views

Run the script with the Hunyuan Python. Check `input/check.png`: three figures, same height, nothing cropped.

```powershell
.tools\hunyuan3d-env\python.exe -I tools\prepare_tpose_views.py <role> <role>-vN
```

### 3. Generate the shape

Takes about 20 seconds per variant and 5.9 GB of VRAM on the RTX 3060. It writes `raw/side-as-right.glb` and `raw/side-as-left.glb`. Inspect both and normally use `side-as-right`.

```powershell
.tools\hunyuan3d-env\python.exe tools\generate_hunyuan_shape.py <role>-vN
```

### 4. Decimate, texture and place joints

The script produces the 2,000-triangle mesh, the texture and `game/landmarks.json`. It scales from the T-pose arm line, so a hat does not shrink the body.

```powershell
.tools\hunyuan3d-env\python.exe tools\build_tpose_character.py <role>-vN
```

Joint heights are fixed for an adult man (hip 0.88, knee 0.49, ankle 0.10, neck 1.45, head 1.53). **Change them for women, children or different proportions.**

For a wide hat, add the role to `RIGID_HAT_ROLES` in the rig script so the brim follows only the head. No view shows the top of the brim, so `revolve_hat` recolors it from the visible straw.

### 5. Rig and attach the tool

```powershell
& ".tools\blender\Blender Foundation\Blender 5.3\blender.exe" -b --factory-startup --python tools\rig_ual_character.py -- <role>-vN
```

The script fits the Quaternius skeleton, binds with Blender Automatic Weights, and exports `rigged/woodcutter.glb` and `rig-report.json`.

For a new role:

- Add its clips to `ROLE_CLIPS` in the rig script.
- Add a tool only if needed. Only the woodcutter's axe is implemented (`build_axe`, `analyse_axe`, `add_left_grip`).
- Register it in `WorldView._Ready` with `AddTexturedRole`, listing clips in the baked order.

Check all clips off-screen, without opening a large window:

```powershell
& ".tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" --path tools\character-review --resolution 480x270 --audio-driver Dummy res://clips.tscn -- <role>-vN
```

Useful clips in `assets-source/quaternius-ual`:

- UAL1: `Idle_Loop`, `Walk_Loop`, `Jog_Fwd_Loop`, `Interact`, `PickUp_Table`, `Fixing_Kneeling`, `Push_Loop`, `Crouch_*`.
- UAL2: `TreeChopping_Loop`, `Farm_Harvest`, `Farm_PlantSeed`, `Farm_Watering`, `Walk_Carry_Loop`, `Idle_FoldArms_Loop`, `Consume`.

### 5b. Flatten the texture to the game style

The world uses flat-colored low-poly Kenney models, so the painted texture is repainted as one solid color per triangle. The painted original is saved to `art/characters-painted/` the first time.

```powershell
python -I tools\stylize_character_textures.py            # add --restore to undo
```

### 6. Review before approval

Set `BASE` in `tools/character-review/review.gd` and `make_chop_gif.py`, then render frames and build the GIF and contact sheet.

```powershell
& ".tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" --path tools\character-review --resolution 1920x1080 --audio-driver Dummy
python -I tools\character-review\make_chop_gif.py
```

Accept only if:

- The body stays upright and symmetric, with clothing not torn or warped.
- Both hands hold a two-handed tool, and the left wrist error in `rig-report.json` is near 0.
- The work motion reads clearly and hits its target. For an axe, the blade is level at contact.
- The user has seen the result.

## Known pitfalls

- **Warped body:** caused by joints fitted from noisy cross-sections and by a lowered-arm source pose. Keep the spine straight and mirror the limbs.
- **Empty measurement bands:** measure landmarks on the dense mesh, before decimation.
- **Strike frame:** use the last frame before the hand reverses. Later frames are recoil and point the tool the wrong way.
- **IK chain:** use 3 bones for the left arm, so the clavicle helps reach the handle.
- **Make-It-Animatable:** rejected because it depends on an Auto-Rig Pro fork and a gated Mixamo-derived dataset.
- **Itch.io downloads:** scripted download was denied. Ask the user to download packs manually.

## Known limits of the woodcutter result

- The chop comes from overhead and is diagonal until the final approach.
- Fingers do not curl around the handle.
- The tool is not repositioned for walk and carry.
- About 15% of the texture is filled from neighboring pixels.
