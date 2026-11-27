# DeskCiv realistic building pipeline

Realistic buildings are made from AI-generated reference sheets and local image-to-3D. The user asked for realistic models made with ChatGPT and Gemini (2026-11-24).

## Steps

1. **Reference sheet.** In ChatGPT or Gemini, ask for a square 2x2 sheet of four orthographic views at one scale: top-left FRONT, top-right RIGHT SIDE, bottom-left BACK, bottom-right LEFT SIDE, photorealistic game-asset texture, flat light, no shadows, plain light grey background. Leave out moving parts (sails, wheel, sluice door) and ask for an axle stub or groove where they attach. Save the sheet as `assets-candidates/concepts/buildings/<type>-t<tier>.png`.
2. **Build.** Run:

   ```powershell
   tools\make_building.ps1 -Sheet assets-candidates\concepts\buildings\<type>-t<tier>.png -Name <type>_t<tier> -Height <m> [-SwapSides] [-Yaw <deg>]
   ```

   This cuts the views (`tools/prepare_grid_views.py`, which finds uneven grid dividers itself), runs Hunyuan3D-2mv Turbo with both side labels (`tools/generate_hunyuan_shape.py`), keeps the shape whose outline matches the views best, restores width and depth measured on the views, decimates to 4,000 triangles, unwraps with xatlas and projects the four views (`tools/build_hunyuan_model.py`). Output: `game/assets/models/<name>.dcmodel` and `.dctex` (JPEG).
3. **Check** the outline IoU in the output (above 0.85 is good) and render a review: `Godot_console.exe --path tools/character-review --resolution 400x300 res://model.tscn -- <abs .dcmodel> <out.png>`.
4. **In game.** `MeshKit.RealisticModel` picks `<type>_t<tier>` for the era tier (0 Neolithic, 1 Bronze/Iron, 2 Medieval/Renaissance, 3 Industrial, 4 20th century). A tier without a model borrows the nearest tier, preferring the older one.

## Conventions and pitfalls

- **Front is +Z.** "RIGHT SIDE" means the view in which the building's front is on the image's right (a turntable turned right). When the generator instead shows the building's right wall in the top-right cell (an axle stub pointing at the viewer), pass `-SwapSides`.
- **Height** is the full height in metres; width and depth follow from the views.
- **Use a green screen.** Ask for "a flat, pure chroma-key green (#00FF00) background, no shadows or gradients" and "no green anywhere on the object" (the user's suggestion, 2026-11-24). `prepare_grid_views.py` detects a saturated background, keys on the dominant channel, also clears enclosed gaps, and removes green fringe. For green objects (trees, plants) use magenta (#FF00FF) instead. Older grey-background sheets still work unchanged.
- **Shape variants.** A second model named `<type>_t<tier>b` is used for every other building of that type and era (chosen by position), on top of the per-house roof and wall colours.
- **White walls** close to the grey background: the mask uses a tight tolerance and keeps enclosed regions. Pass `--holes` to `prepare_grid_views.py` only for open objects such as wheels.
- **Thin moving parts** (sails, sluice door) are flat cut-out cards: `python -I tools/build_cutout_model.py <image on white> <name> <size-m>`. Their texture is a PNG with alpha and they are drawn two-sided.
- **Moving parts** are declared in `MeshKit.Moving`; pivots for the realistic windmill (2.62 m forward, 9.1 m up), watermill wheel (0, 3.1, -4.55) and sluice door (0, 2.2, 0) were measured on the models.
- **Free-plan limits.** ChatGPT and Gemini each allow a handful of images, then pause for hours. Downloads go through Chrome's Save As dialog; `finish-save-dialog.ps1` in the session scratchpad fills it.
- **Licence.** Hunyuan3D's licence excludes use in the EU, UK and South Korea. Resolve this before any public release.

## Models made so far

| Type | Tiers with own model | Notes |
|---|---|---|
| Windmill | all | body + cut-out sails |
| Watermill | all | body (rotated 90°) + Hunyuan wheel |
| Hut (home) | 0, 2, 3, 4 | tier 1 borrows tier 0 |
| Storehouse | 0, 2 | |
| Bakery | 1 | |
| Smithy | 1 | |
| Winery | 1 | |
| Mill | 0 | |
| Woodcutter, Fisher | 0 | fisher's net rack was lost by Hunyuan |
| TideGate | 2 | door still procedural until `tidegate_door` exists |
| Hillside cave (`landmark_cave`) | wild years | Gemini sheet; its side views repeat the front, but the rock still built at IoU 0.909. The mouth is always open (the dig no longer shows on the rock) |
