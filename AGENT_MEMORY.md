# AGENT_MEMORY — DeskCiv

Hand-off record shared between agents. Newest entries first. Do not overwrite prior entries or record secrets.

## 2026-11-24 — Green-screen sheets, second house shape, chicken, roundhouse, harbour warehouse, quarry

- **Purpose:** Continue realistic models after ChatGPT's limit reset; the user suggested asking for green backgrounds so cut-outs are easier.
- **Changed:** `tools/prepare_grid_views.py` keys saturated green or magenta backgrounds on the dominant channel, clears enclosed gaps and despills the fringe; grey sheets cut byte-identically to before. `MeshKit.RealisticVariant` loads `<type>_t<tier>b`; `WorldView` has a second multimesh per type and sends every other building (position hash) to it, with the same recolouring material. New models: `animal_chicken`, `hut_t1` (Bronze/Iron Age roundhouse), `storehouse_t3` (stepped-gable harbour warehouse, also used for tier 4), `quarry_t0` (cut limestone outcrop, used for all tiers), `hut_t4b` (second 20th-century house, first green-screen sheet). `docs/building-pipeline.md` updated.
- **Verified:** Game built with 0 errors; the green-screen sheet cut cleanly (white walls kept, no fringe); shots of a 1962 street with both house shapes and of a Bronze Age village with roundhouses and the quarry, 0 logged errors. Outline match: chicken 0.89, roundhouse 0.94, warehouse 0.92, quarry 0.89, second house 0.91.
- **Limitations:** Only tier 4 homes have a second shape so far. Roundhouse side views lack the porch. Performance not re-measured (two extra small multimeshes per type, mostly empty).

## 2026-11-24 — Live reload of generated models

- **Purpose:** The user asked whether models can be updated while the app runs, without a restart.
- **Changed:** `WorldView` watches `assets/models` with a `FileSystemWatcher`; 2 s after the last file event, `ReloadModelsIfChanged` (called from `SyncWorld`) clears `MeshKit`'s model cache and re-applies buildings for the current era, moving parts (`ApplyMovingPart`), trees, hedges and felled trees (`ApplyTree`), and animals and boats (`Wildlife.ReloadModels`, which now also picks up `animal_chicken`). New `tools/publish_models.ps1` copies new or changed model files to `dist/DeskCiv/app/assets/models`; `tools/make_building.ps1` runs it at the end, so a running portable app shows a new model about 2 s after it is built.
- **Verified:** Game built with 0 errors. Live test: started the game, waited until frames were rendering, replaced `hut_t2.dctex` with a colour-swapped copy; the log printed "generated models reloaded" and the last frame showed the medieval houses with the new texture, 0 logged errors. The original texture was restored afterwards.
- **Limitations:** Characters (`assets/characters`), Kenney kit models and ground textures are not watched. A model type that did not exist at start (for example a first moving part for a building that had none) needs a restart. Cut-out cards and textures each allocate new GPU memory on reload; old ones are freed by Godot reference counting.

## 2026-11-24 — House colour variety and weather

- **Purpose:** The user agreed to more house variety and weather, and suggested reusing one house model with per-part colours ("dùng chung mẫu nhà chỉ đổi màu từng part").
- **Changed:** `WorldView.BuildingShader` replaces the realistic models' surface material (not mills, sluice or quarry): roof = upward faces in the upper half of the model, recoloured from 5 roof tints; walls get one of 6 tints, strongest on bright, low-chroma plaster texels, mild on stone, brick and timber; plus a small brightness jitter. The choice is a hash of the building position, so no extra instance data. Weather (`Main.Weather`): cloud cover drifts with the wall clock (periods 7-53 min), rain above about 0.8 cover; `--weather=clear|cloudy|rain` fixes it. Global shader parameters `cloud_cover`, `rain`, `daylight` drive drifting cloud shadows and wet darkening in the terrain and water shaders. Sun and sky turn orange at dawn and dusk, light and shadows soften under cloud, fog thickens for morning mist (5-9 h) and rain, and 3,000 GPU rain streaks fall in a 90 m box over the camera focus. The sun update now runs every 3 s.
- **User-visible behavior:** Neighbouring houses of one model have different roof and wall colours; the wallpaper has clear, cloudy and rainy spells, cloud shadows crossing land and river, golden mornings and evenings with mist.
- **Verified:** Game built with 0 errors; shots of a 1962 and a 1598 town (varied colours), rain, cloud shadows, dawn mist and a cloudy night, 0 logged errors after removing an editor-only call (`GlobalShaderParameterGetList`). 2560x1440 off-screen: 30 FPS, GPU 3.67 ms in rain and in clear weather, CPU 0.51-0.57%.
- **Limitations:** Clouds are shadows only; the sky behind is still a flat colour (rarely visible from this camera). No snow particles; rain shows only near the camera focus. Recolour masks are heuristic, so some timber or stone picks up a tint.

## 2026-11-24 — Photo textures on the ground

- **Purpose:** The user asked for ground textures ("Làm texture cho mặt đất đi").
- **Changed:** Downloaded seven CC0 1K diffuse textures from Poly Haven (source copies in ignored `assets-source/polyhaven/`, game copies in `game/assets/ground/*.dctex`, JPEG bytes loaded at runtime). `WorldView.TerrainShader`: grass and sand keep the biome and season colour but take detail from `leafy_grass` and `coast_sand_01` (photo divided by its own average, read from the smallest mip so it works in the Compatibility renderer's colour space); wear, yards and dirt roads use `dirt_aerial_02`; cobbled roads and town paving `cobblestone_floor_01`; 20th-century roads `asphalt_02`; quarry spoil `rocks_ground_02`. Each photo is sampled at two scales, the second rotated, blended by noise to hide tiling. Fields and vineyards use a new `FieldShader` with `dry_mud_field_001` detail. Tree crown cards are hidden when seen edge-on. `THIRD_PARTY_NOTICES.md` lists the textures.
- **User-visible behavior:** Grass, sand, earth, cobbles and asphalt show real photographic detail instead of flat colour.
- **Verified:** Game built with 0 errors; shots of a Neolithic village, a Renaissance field and town, a 1962 street and a wide view, 0 logged errors. 2560x1440 off-screen: 30 FPS, 0.52% CPU, 404 MB RAM, GPU 3.6 ms (was 3.45 ms).
- **Pitfall found:** dividing by a linear-space mean computed in C# made the ground twice as bright, because the Compatibility renderer samples `source_color` textures without the expected conversion; the shader now uses `textureLod(tex, uv, 12.0)` as the mean.
- **Limitations:** Fields keep their low-poly furrow shape. Snow, polder land and slopes use the same grass detail. Tiling can still be seen on large asphalt areas.

## 2026-11-24 — Walkers face their path, animal walk cycle, more human marks on the land

- **Purpose:** The user asked for animation, said villagers walked backwards ("nhân vật đi giật lùi") and wanted more human impact on the terrain.
- **Cause of backward walking:** `SimHost` aimed each walker at `TX/TZ`, the final destination, while people follow road paths through waypoints, so they often faced sideways or back. Checked that character meshes, rigs and the walk/carry clips face +Z and play forward (planted foot moves backward).
- **Changed:** `SimHost` turns each walker toward its actual movement since the last published frame, smoothed (teleports over 5 m fall back to the destination). `Wildlife` herd shader: diagonal leg pairs swing about the hips (lowest 45% of the mesh) while moving, a small body bob, and a slow grazing head nod when still. Terrain (`WorldView`): the road texture is now RGB: roads, worked ground around buildings, quarry spoil. Roads are dirt, then cobbles from era 4, then asphalt in era 7; yards are trampled earth, paved between later town houses; quarries leave a widening patch of broken stone. Felled trees leave a stump on open ground until they regrow (`MeshKit.Stump`). From era 4 fields get hedgerows of small oaks on two sides. Tree models gain a level crown card (top-view crown made from the side image) in an atlas texture, so trees no longer look like stars from above.
- **Verified:** Game built with 0 errors; shots of Bronze Age, Renaissance and 20th-century towns show yards, cobbles, asphalt, hedgerows, stumps and round crowns from above, 0 logged errors. 2560x1440 off-screen: 30 FPS, 0.57% CPU, 381 MB RAM, GPU 3.45 ms. Simulation code unchanged in this step.
- **Motion check:** New test flag `--burst=N` saves N extra frames 4 frames apart. A 15-frame burst at a Neolithic storehouse shows a baker walking toward the camera facing it, a woman climbing the steps with her back turned, and a carrier facing her direction of travel.
- **Limitations:** The animal leg walk was not watched in motion. Leg swing also bends any wool or belly below 45% height. Quarry rocks are still Kenney low-poly. Hedges skip only road and building tiles.

## 2026-11-24 — Realistic trees, animals, boats, sluice door, gatherer and industrial workshop

- **Purpose:** Continue the user's request for realistic generated models (Gemini sheets while ChatGPT's free limit was used up).
- **Changed:** Trees: `tree_pine`, `tree_broad`, `tree_birch` are three crossed cut-out cards each (`build_cutout_model.py --cross 3`, which now crops to trunk base and crown top) drawn with `WorldView.TexturedTreeShader` (sway, near dither, seasonal leaf colour); felled trees use the same pine. Animals `animal_cow`, `animal_sheep`, `animal_deer` and `boat` from Hunyuan; `Wildlife` shader multiplies a texture, and real boats sink 0.55 m. `tidegate_door` cut-out slides in the realistic sluice. `gatherer_t0`. One brick `workshop_t3` is shared by every craft workshop from the industrial era (`RealisticModel`). `build_hunyuan_model.py` gives upward faces that no view sees (boat deck, flat roofs) the median colour of well-seen surfaces at the same height.
- **User-visible behavior:** Realistic conifers, oaks and birches change colour with the seasons; Holstein cows, sheep, red deer and clinker-built sailing boats; industrial and 20th-century workshops are brick instead of thatch.
- **Verified:** Game built with 0 errors; in-game shots of the town, fields, shore and tide gate with 0 logged errors; review renders of all new models. 2560x1440 off-screen: 30 FPS, 0.52% CPU, 392 MB RAM, GPU 3.4 ms.
- **Limitations:** Chickens and gulls are still low-poly (Gemini limit reached before the hen). Deer outline match 0.76, boat 0.66 (thin rigging). Gravestones, fields, crops, dikes and quarries remain simple. Era tier 1 homes and later storehouses borrow other tiers. Converted house blocks repeat one model.

## 2026-11-24 — Denser town: homes cluster and inner fields become house blocks

- **Purpose:** The user approved making the town look dense: homes grouped around the centre, fields pushed outward.
- **Changed:** `Simulation.FindSite` (from era 4): homes score for being near the centre and near other homes (`HomesNear`, 20 m); fields and vineyards score away from homes. New `UrbanizeField` runs each new year from era 4: the inner field nearest the centre with 3+ homes within 32 m, no grain in stock, no reservations, nobody working on it right now, and only while grain is in surplus (`HasSurplus(Field)`) and at least 4 fields exist, becomes four 2x2 houses facing a new middle lane. The field object becomes the first house so ids stay valid; its idle farmers are released. Diagnostics `Urbanized`/`UrbanSkips` are printed by `tests/SimTests` as `urban=`.
- **User-visible behavior:** Medieval and later towns grow rows of houses where the oldest inner fields were; farmland moves to the edge.
- **Verified:** `tests/SimTests` ALL PASSED (including determinism, save/load and grain conservation). Era-end populations stay close to before (e.g. seed 72271: 28 55 119 144 199 192 255 278); buildings 163-222 versus 151-164; 6-16 fields urbanized per run. In-game wide shot of seed 1000 at 1962 shows new four-house blocks in the centre.
- **Rejected tries:** applying clustering from era 1 broke the early bread chain and polder tests; clearing field stock broke grain conservation; converting without a grain surplus dropped some seeds' population to 127.
- **Limitations:** Converted blocks look regular (grid of identical homes). Seed 1000 converts only 6 fields, so its outskirts still look sparse.

## 2026-11-24 — More, smaller homes in late eras

- **Purpose:** The user looked at the town and said there were too few houses ("Trông ít nhà quá nhỉ"). Homes held 7 people in the Middle Ages, 9 in the industrial era and 12 in the 20th century.
- **Changed:** `Simulation.HomeSize` is now 8 in era 0, 6 in eras 1-6 and 7 in era 7. Also: textured tree shader, wildlife texture support (earlier commits today).
- **User-visible behavior:** Later towns build more houses; the 20th-century town has roughly 40 homes for about 285 people instead of about 24.
- **Verified:** `tests/SimTests` ALL PASSED. Era-end populations (4 seeds) stay close to before, e.g. seed 72271: 28 55 119 144 190 198 248 288 (before 28 55 119 144 192 208 257 283); total buildings 165-175 versus 151-164. A first try with 5 people per medieval home failed the bread-chain test (6 in era 1) and later cut Renaissance population to 110 in seed 1000, so it was rejected.
- **Limitations:** The town still looks sparse from far away because homes are spread between 18 m fields and a wide road grid; the layout itself was not changed.

## 2026-11-24 — Realistic image-generated buildings with animated parts; painted villagers restored

- **Purpose:** The user reversed the low-poly restyle: "Dùng chatgpt và gemini để generate ra các model theo phong cách thực tế. Ngoài ra các model cối xay gió, bánh xe nước... phải có animation".
- **Changed:** Painted villager textures restored (`stylize_character_textures.py --restore`) and faceted shading removed. New pipeline (see `docs/building-pipeline.md`): `tools/prepare_grid_views.py`, `tools/build_hunyuan_model.py`, `tools/build_cutout_model.py`, `tools/make_building.ps1`; `generate_hunyuan_shape.py` takes four views. `MeshKit.LoadModel`/`RealisticModel` load `game/assets/models/*.dcmodel` + `.dctex` with a textured material; `WorldView` clears the vertex-colour override for those meshes and uses a textured, alpha-cut moving-part shader. The windmill body now faces the same fixed sea-wind yaw as its sails. Test flag `--look=<BType>` aims the camera at a built building of that type. Review scene `tools/character-review/model.tscn`. Sheets saved in `assets-candidates/concepts/buildings/`.
- **Models:** windmill body + cut-out sails, watermill body + Hunyuan wheel (both turning), homes for tiers 0, 2, 3, 4, storehouse 0 and 2, bakery, smithy, winery, grain mill, woodcutter, fisher, tide gate piers (door still the procedural sliding panel). Workplaces without later-era models borrow the nearest tier.
- **Verified:** Game built with 0 errors; in-game shots of the windmill (sails turning, shadows), watermill (wheel on the water side), Neolithic village and medieval town with 0 logged errors; review renders of every model. `tests/SimTests` ALL PASSED. 2560x1440 off-screen, 259 people: 30 FPS, 0.55% CPU, 377 MB RAM, GPU 3.6 ms per frame (was 3.3 ms).
- **Limitations:** Trees, animals, boats, fields, dikes, quarries, the gatherer hut and the sluice door are still low-poly. Hunyuan dropped thin parts (fisher's net rack) and the tide gate outline match is only 0.76. Small moving parts of the mill, winery and smithy are still procedural boxes. Side textures may be mirrored where the generator's side views disagree. ChatGPT (until 20:59) and Gemini (until 17:44) free image limits were reached. Hunyuan3D licence excludes EU/UK/South Korea.

## 2026-11-24 — Villagers restyled as flat-colored low-poly to match the Kenney world

- **Purpose:** The user asked to unify the model style. Villagers had painterly, semi-realistic textures (faces, fabric detail) while buildings, trees and props are flat-colored Kenney low-poly.
- **Changed:** New `tools/stylize_character_textures.py`: for every `hq_*` character it samples the painted texture under each triangle (median of 24 interior points), blends touching triangles of similar color, snaps all triangle colors to a 10-color palette (slightly brighter and more saturated), and repaints the texture as one solid color per triangle. The painted originals are kept byte-identical in `art/characters-painted/`; `--restore` puts them back. `CharacterKit.SkinShader` now uses faceted normals from screen derivatives, for both bodies and tools. `docs/character-pipeline.md` gained the step.
- **User-visible behavior:** Villagers look like faceted toy figures with clean color patches (skin, hair, tunic, trousers, boots) instead of painted faces and fabric. Era clothing dye and the Stone Age hide look still apply.
- **Verified:** Game built with 0 errors. 960x540 close-ups of a medieval woodcutter and a carrier before and after, with 0 logged errors. Overviews of the industrial (1820) and 20th-century (1962) towns showed procedural brick and plain houses already in the same flat style.
- **Limitations:** Faces no longer show eyes or beards up close. Performance was not re-measured (same texture size and triangle count; the shader adds two derivative calls). `tests/SimTests` not rerun (no simulation change). Animals and boats are still procedural boxes, consistent in style but simple. Not every role was viewed close up.

## 2026-11-24 — Sun shadows, ground-contact shading, ground noise and swaying grass

- **Purpose:** The user said the scene did not look real and asked whether shaders or ray tracing would help. Agreed plan: cheap lighting and shader work on the existing GL Compatibility renderer, no ray tracing.
- **Changed:** `Main.cs`: the sun casts an orthogonal shadow map whose range follows the zoom (`_distance * 1.7`, 90-400 m); shadows turn off at auto-quality level 2+; fog slightly denser with aerial perspective; MSAA 2x in `project.godot`. Test flags `--no-shadows`, `--no-msaa`, `--shadow-splits=1|2`, `--shadow-size=N`; metrics CSV gains `gpu_ms` and `cpu_render_ms`. `WorldView.cs`: buildings, trees, falling trees, windmill and wheel parts and villagers cast shadows; terrain uses a FastNoiseLite texture for damp and dry patches and clumps (the old fine sine pattern is gone); grass, flowers, pebbles and reeds use a swaying `ScatterShader` that keeps the seasonal tint. `tools/build_kit_buildings.py` darkens kit surfaces within 1.6 m of the ground (not rotors or scatter); all kit models rebaked.
- **Important finding:** In the Compatibility renderer a sun with shadows enabled lights several times brighter than the same sun without shadows. `UpdateSun` scales sun energy by 0.35 when shadows are on and lowers ambient more by day, so the lit grass matches the old brightness while shadows read clearly. AgX and ACES tone mapping washed the image out in this renderer and were removed.
- **User-visible behavior:** Buildings, trees and people have sun shadows; grass and flowers move in the wind; meadows show broad colour variation; building bases look grounded.
- **Verified:** Game built with 0 errors. Before and after 960x540 shots of the era-0 camp, an era-1 village and an era-4 town, plus a night town shot, all with 0 logged errors. At 2560x1440 off-screen with 259 people: 30 FPS, 0.5-0.65% CPU, 382 MB RAM, GPU frame time 3.3 ms (about 10% of the 33 ms frame) versus 2.5 ms without shadows; 4-split shadows cost 5.4 ms and were rejected. `nvidia-smi` and Windows GPU counters could not attribute load to the game, so the in-engine GPU timer was used. `tests/SimTests` not rerun because no simulation code changed.
- **Limitations:** A single shadow map is soft-edged when zoomed far out. When the sun is behind the camera, shadows hide behind objects. Shadow brightness compensation was tuned by pixel samples at noon and 4 pm only.

## 2026-11-24 — Props, rocks and ground scatter from Kenney's CC0 Nature Kit

- **Purpose:** Continue replacing primitive models with open-source assets.
- **Changed:** `tools/build_kit_buildings.py` also reads `assets-source/kenney/nature-kit` (colors mapped by material name to the game palette) and writes `prop_*.dckit` and `scatter_*.dckit`. `MeshKit.TryKit` places a baked prop inside a procedural model, with the old geometry as fallback. Camp hut fire ring, camp storehouse bowl, camp fisher dugout canoe, camp woodcutter log stack and stump, camp and later quarries (natural rock outcrop), dike stone revetment, and grass, flower and pebble scatter now use Nature Kit pieces. Added `LICENSE-Kenney-Nature-Kit.txt` and a notice row.
- **User-visible behavior:** Stone Age camps and quarries look natural; meadows have Kenney grass, flowers and pebbles.
- **Verified:** Game built with 0 errors. In-game 960x540 shots reviewed the era-0 camp, shore, an era-1 village quarry and an era-4 town. `tests/SimTests` ALL PASSED. Grass was shrunk after the first shot showed it too large.
- **Limitations:** Performance was not re-measured; scatter triangles are similar to before except flowers (146 vs about 84 per cluster, 2,200 clusters). The dike was not viewed close up. The earlier "Chrome blocked downloads" diagnosis was wrong: kenney.nl's download endpoint returns HTTP 503 after one download (rate limit), still failing 10+ minutes later. At the user's request a Chrome user policy `HKCU\Software\Policies\Google\Chrome\AutomaticDownloadsAllowedForUrls\1 = [*.]kenney.nl` was added; it may make Chrome show "managed by your organization". Survival, Castle, City, Watercraft and graveyard kits are still missing.

## 2026-11-24 — Buildings, rotors and trees from Kenney's CC0 Fantasy Town Kit

- **Purpose:** The user asked to improve the primitive 3D models using open-source assets.
- **Changed:** Added `tools/build_kit_buildings.py`, which composes kit pieces into `game/assets/buildings/*.dckit`. `MeshKit.LoadKit` loads them. Work buildings and storehouses use kit models for tiers 0-4, homes for tiers 1-3, and windmills, water wheels and all three tree kinds use kit parts. Added `THIRD_PARTY_NOTICES.md`. The source zip and extraction are in the git-ignored `assets-source/kenney/`.
- **User-visible behavior:** Buildings have real walls, doors, shutters, gabled roofs and chimneys, changing material by era. Windmill sails, water wheels and trees are more detailed.
- **Verified:** Game built with 0 errors. In-game shots reviewed eras 1, 3 and 5 plus a polder. With 281 people at 2560x1440 off-screen: 30 FPS, about 0.5% CPU, 317 MB RAM, and 3-7% GPU in 10 samples. See the commit for the `tests/SimTests` result.
- **Limitations:** Animals, boats, tents, dikes, the tide gate, the windmill tower, round Neolithic huts, year-2000 homes and storehouses, and the factory are still procedural. Chrome stopped further automatic downloads from kenney.nl, so Nature, Survival, Castle, City, Watercraft and Cube Pets kits were not obtained. OpenGameArt refuses automated fetches, and Quaternius downloads go through itch.io, which was not used.

## 2026-11-24 — Stone Age camp and land reclamation from the year 500

- **Purpose:** The user asked for no clothes or houses in the first era, only a few tents, and Dutch-style sea reclamation with windmills from about the 5th-6th century.
- **Changed:** `MeshKit.CampBuilding` adds hide tents and open work spots. Era 0 is capped at 32 people, hides laid roads, farm animals and sailing boats, and draws villagers with bare arms and legs under a simple animal-hide wrap. `Polder` in `Model.cs` and the polder code in `Simulation.cs` add ring dikes, wind pumps, draining and new farmland from era 4. `WorldMap` keeps base and reclaimed heights. The terrain shader reads a height texture so land rises live. Added `--view=X,Z,DISTANCE`. Windmills keep their classic look and sails face the south-east. Rebaked `hq_laborer` with a harvest clip. Save format is now 9, so older saves are set aside and a new world starts.
- **User-visible behavior:** A run opens with a small tent camp. From about year 500 villagers build dikes into the sea, windmills drain the enclosed water, and the land turns into fields.
- **Verified:** `tests/SimTests` ALL PASSED before the final hide-wrap and animal-gating edits, which are render-only: four full runs each reclaimed 4-8 polders and ended at 277-294 people. In-game 1120x630 shots reviewed the camp, close-up villagers, a medieval polder and a later polder landscape, with 0 logged errors.
- **Limitations:** The polder is raised land rather than ground below sea level. The landward edge of a polder has a jagged mud patch. Draining was seen at finished stages, not watched continuously. The portable build with 275 people at 1120x630 ran at 30 FPS, 0.5-0.9% CPU and 356 MB RAM with 0 logged errors; the test stopped it during its run, so a clean exit was not rechecked. Performance was not re-measured at 2560x1440.

## 2026-11-24 — Hand tools for miner, fisher, farmer and builder

- **Purpose:** Make more jobs readable on screen.
- **Changed:** `tools/rig_ual_character.py` builds a pickaxe for the miner and one-handed rod, sickle and hammer props. The fisher's work clip is now `Farm_Watering`. Rebaked `hq_miner`, `hq_fisher`, `hq_farmer` and `hq_builder`, and rebuilt the portable folder.
- **User-visible behavior:** These workers show their own tool while working.
- **Verified:** Clip sheets for all four roles show the tool in the hand. The game and portable build ran with 0 logged errors.
- **Limitations:** Fingers do not close around one-handed tools. The fishing line is not modeled. Children are still scaled adults. Bakers have no tool.

## 2026-11-24 — Era looks, generated audio, auto quality, Windows startup and portable folder

- **Purpose:** Finish the remaining product items from the agreed requirements.
- **Changed:** `MeshKit.Retint` recolors all work buildings by era, and `CharacterKit` dyes clothing by era. `Soundscape.cs` now generates sea, wind, birds and one music piece per era. New `AudioYield.cs` fades DeskCiv out while another app makes sound. `Main` lowers render scale when FPS stays low and adds `--restart-every=SEC` for testing. The tray has a "Chạy cùng Windows" toggle, off by default. `tools/package_portable.ps1` builds `dist/DeskCiv` (ignored by git) with the engine and .NET runtime; its saves stay in its `data` folder.
- **User-visible behavior:** Towns and clothes change through the eras, sound is richer and yields to other apps, and the app can run from a portable folder through `DeskCiv.cmd`.
- **Verified:** Game built with 0 errors. A year-1986 shot shows modern buildings and dyed clothes. 8 forced world restarts plateaued near 385 MB. With 274 people at 2560x1440 off-screen: 30 FPS, 0.6% CPU, 322 MB RAM, about 5% GPU from few samples. The portable build ran from its bundled runtime with no errors. In real wallpaper mode its window is a child of Progman, not topmost, at 30 FPS and 0.5% CPU.
- **Limitations:** Nobody has listened to the audio. Audio yielding and the startup toggle were compiled but not exercised, so the registry was not changed. Auto quality was not triggered under real load. The portable folder is not a single exe and was not tried on a second PC.

## 2026-11-24 — Stable population through the year 2000

- **Purpose:** Remove the boom-and-famine waves in eras 6-8.
- **Changed:** `game/src/Sim/Simulation.cs`. Harvests, mill and bakery batches and carry loads grow by era. Trades have worker caps, so processors no longer drain farmers. Births slow near the era's limit and need a larger food reserve. Homes hold more people from era 4. Streets and sites may use more land. A tide gate can replace a dike on a crowded bank.
- **User-visible behavior:** Towns grow steadily to nearly 300 people instead of collapsing late in a run.
- **Verified:** `tests/SimTests` ALL PASSED. Four full runs with a day-night cycle ended at 282-296 people and built every building type.
- **Limitations:** Only four seeds were run for the full 336 years.

## 2026-11-24 — Era architecture, visible surges, history replay and new world

- **Purpose:** Make era changes visible and complete the run loop.
- **Changed:** `MeshKit.EraBuilding` adds timber, stone, brick and concrete versions of homes and storehouses, plus a factory for the smithy from the Industrial era. `WorldView` swaps these models and raises the water during a surge. `Simulation.History` records each era and is saved. `Main` plays a history replay, default 120 seconds, then starts a new world. `--replay-seconds=N` shortens it for testing. Save format is now 8, so older saves are set aside and a new world starts.
- **User-visible behavior:** Houses change with the era. Water rises during storm surges. After the year 2000, the camera circles the town with one card per era, then a new world begins.
- **Verified:** `tests/SimTests` ALL PASSED, including history for all eight eras in four full runs. A 1120x630 run from year 336 showed the modern town and an era card, then restarted at 20 people and year 0 with no logged errors, at 30 FPS and about 0.5% CPU.
- **Limitations:** Only homes, storehouses and the smithy change by era, so other buildings keep thatch in the year 2000. People do not change clothes by era. The surge rise was not reviewed on screen. RAM rose from about 389 MB to 456 MB after the world restart and was not tested over several restarts. Population still dips in eras 6-8. Real audio and audio yielding, automatic quality, Windows startup, portable export, a 300-person performance test and a direct wallpaper-mode check remain.

## 2026-11-24 — Eight-era progression through the year 2000

- **Purpose:** Replace the single Neolithic era with the agreed eight eras and keep villages alive for a whole run.
- **Changed:** `Eras` in `game/src/Sim/Model.cs` holds names, calendar (10,000 BCE to 2000), population caps (45 to 300) and building unlocks. `Simulation.cs` advances eras every 42 years, keeps street corridors clear so roads can grow, bounds site-search pathfinding, staffs each core trade first, moves workers from surplus trades to shortages, and raises field yields by era. `SimHost` and `Main` show the era and calendar. `tests/SimTests` adds four full runs and `diag <seed> <years> [mixed]`. Save format is now 7, so older saves are set aside and a new world starts.
- **User-visible behavior:** New worlds start in the Old Stone Age with only basic buildings, then unlock farming, crafts, water works, windmills and the tide gate. The overlay shows the current era and year.
- **Verified:** `tests/SimTests` ALL PASSED: 12 worlds for 50 years and 4 worlds for all 336 years with a day-night cycle. Each full run built every building type. Era-end population reached 200-240 by era 5-6, and final population was 89-230.
- **Limitations:** Later eras do not change how buildings or people look yet. Population still dips in eras 6-8 and does not reliably reach 300. No end-of-run replay or new-world restart exists yet.

## 2026-11-24 — Economy recovery, craft effects and storm surges

- **Purpose:** Fix the population collapse before adding later eras, and give wine, tools, dikes and the tide gate a real effect. Also records commit `17298b1`, which lays vineyards along the ground slope.
- **Changed:** `game/src/Sim/Simulation.cs`. Workers stop when their output pile is full and help carry goods. Mills and bakeries hold larger input buffers. Trees last longer and woodcutters settle near forest. One in five villagers rests at night instead of two in five. Wander pathfinding is bounded, and paths to blocked tiles are skipped. Tools speed work, wine raises births, and yearly storm surges damage riverside fields unless dikes and a tide gate hold them. Save format is now 5, so older saves are set aside and a new world starts.
- **User-visible behavior:** Villages grow larger and rarely starve. Event text reports storm surges and whether the dikes held.
- **Verified:** `tests/SimTests` ALL PASSED on 12 worlds for 50 years, with final population 68-155 (previously 43-132) and no world out of food. The game project built with 0 errors.
- **Limitations:** The surge has no visual yet, because the water level does not rise on screen. The change was not viewed in the running game. Population is still below the 200-300 target at year 50. Seven later eras, history replay, real audio, automatic quality, Windows startup and portable export remain.

## 2026-11-23 — Coastal water, trees, reeds, animals and boats

- **Purpose:** Complete the requested order of models, then ground, trees and animals.
- **Changed:** Added `game/src/Wildlife.cs` for sheep, cows, chickens, deer, gulls and sailing boats. `WorldView` now has a wave and shoreline-foam water shader, salt-grass and beach coloring, reeds, and three wind-swayed tree kinds. `MeshKit` has the new meshes.
- **User-visible behavior:** The sea moves and foams at the shore, trees sway, animals wander near the village, gulls circle, and boats sail the estuary.
- **Verified:** The game built, and wide and close in-game shots were reviewed at 1120x630. See the commit's test result for `tests/SimTests`.
- **Limitations:** Animals and boats are decorative and do not affect the simulation. Still not done: the seven later eras, history replay, real recorded audio and audio yielding, automatic quality, Windows startup, portable export, LOD or VAT, flood gameplay for dikes and the tide gate, uses for wine and tools, and a 300-person performance test.

## 2026-11-23 — All villager roles use textured characters

- **Purpose:** Finish the character models before ground, trees and animals.
- **Changed:** Generated T-pose concepts in ChatGPT for woman, laborer, builder, baker, fisher, miner and elder. Added `tools/build_roles.ps1`, baked `game/assets/characters/hq_*`, and routed every adult, woman, elder and child to a textured model. Workers now stand on field plates.
- **User-visible behavior:** No old block villagers remain among people drawn near the camera. Children use smaller laborer and woman models.
- **Verified:** All seven clip sheets were reviewed. The game built and ran at 1120x630 with 56 people: 30 FPS, 0.70% CPU, 304 MB RAM.
- **Limitations:** Children are scaled adults. Fisher and elder work clips are a folded-arms idle. Miners use the axe mesh rather than a pickaxe. Farmers, bakers and fishers have no hand tools. Not measured at 300 people.

## 2026-11-23 — Estuary map and animated working buildings

- **Purpose:** The user asked for a finished app with animated works, an estuary so windmills make sense, then ground, trees and animals, without further approvals.
- **Changed:** `WorldMap` now has a sea to the east and a widening river mouth. Added Windmill, Watermill, Vineyard, Winery, Smithy, Dike and TideGate, plus Grapes, Wine and Tools. Added bank placement, moving parts (`MeshKit.Moving`, `MovingShader`) and `game/debug/BuildingPreview`. Save format is now 4, so older saves are set aside and a new world starts.
- **User-visible behavior:** New worlds start at a river mouth. Sails, water wheel, millstone, wine press, smith's hammer and tide gate move once built.
- **Verified:** `tests/SimTests` passed on 12 worlds for 50 years, and the game built. An in-game wide shot shows the estuary with dikes. The off-screen preview shows all new building models.
- **Limitations:** Buildings are procedural low-poly, not image-generated. Average population is lower than before the estuary change (roughly 45-110 at year 50 versus up to 160). Wine and tools are produced but not yet used by villagers. Dikes and the tide gate have no flood gameplay yet. Only the Neolithic era exists.

## 2026-11-23 — Farmer added through the character pipeline

- **Purpose:** The user asked the agent to generate images through ChatGPT in Chrome and continue on its own. The agent chose the farmer as the next role. Woodcutter integration is commit `1845b7e`.
- **Changed:** Added `assets-candidates/concepts/farmer-tpose/` and `game/assets/characters/hq_farmer.*`. The prepare, build and rig scripts now take role and version. Hats use rigid head weights and a recolored brim. Added `tools/character-review/clips.*` for off-screen clip sheets. `WorldView` now supports several textured roles through `TexturedRole`. Updated `docs/character-pipeline.md`.
- **User-visible behavior:** Adult male field workers and gatherers use the straw-hat farmer. Hoe and gather work use the bent-over harvest clip. Their old hoe, sickle and basket are no longer drawn.
- **Verified:** Hunyuan shape is watertight with 361,588 faces. Body is 2,000 triangles. The off-screen sheet shows idle, walk, carry, sow and reap. Game built with 0 errors, `tests/SimTests` passed, and a 1120x630 in-game shot shows the farmer walking in the village.
- **Limitations:** Sowing and reaping were not captured inside the game, only on the clip sheet. The farmer has no hand tools. A small dark patch remains on one side of the hat brim. Performance was not re-measured. Women, children, elders and other jobs still use old models. The user has not reviewed the farmer.

## 2026-11-23 — Woodcutter v2 integrated into the game

- **Purpose:** The user asked to commit, then put the approved woodcutter in the game. Checkpoint commit is `2273ad3`; the user then asked to commit this entry's work as well.
- **Changed:** `tools/rig_ual_character.py` exports `game/assets/characters/hq_woodcutter.*`. `CharacterKit.Textured` loads its own mesh, texture and 22-joint rig. `WorldView` draws adult male woodcutters with it and shows the axe only while chopping. `MeshKit.Tree` has a taller trunk and higher canopy so the swing is visible. `Main` waits longer for `--watch` shots. Added `game/debug/WoodcutterPreview`.
- **User-visible behavior:** Adult male woodcutters use the new textured model for idle, walk, carry and chop. All trees look taller. Other villagers, including female woodcutters, keep the old models.
- **Verified:** Game built with 0 errors and `tests/SimTests` passed before the tree mesh edit; the tree edit is render-only and tests were not re-run after it. A real in-game screenshot shows the woodcutter chopping at a trunk. With 88 people in a 2560x1440 window: 30 FPS, 0.55% average CPU, 291 MB RAM.
- **Limitations:** GPU use and 300 people were not measured. No LOD yet. Carried goods still use the old load offset and were not reviewed on the new carry clip.
- **Incident:** The user saw the screen taken over during this work. No Godot process remained when checked. The likely cause is the agent's own 2560x1440 `--windowed` measurement, which covers a same-size monitor for 75 seconds. This does not confirm or disprove the earlier wallpaper always-on-top report; the user should test by launching `start-deskciv.cmd` directly.

## 2026-11-23 — Commit checkpoint before woodcutter integration

- **Purpose:** The user asked to commit the current work before integrating the woodcutter.
- **Changed:** `.gitignore` now excludes the downloaded Quaternius packs (`assets-source/quaternius-ual/`, 166 MB), generated Hunyuan outputs (`assets-candidates/hunyuan3d/`, about 50 MB) and `__pycache__`. Concept images, tools, docs and the logistics work are tracked.
- **User-visible behavior:** No change from this entry.
- **Verified:** `tests/SimTests` printed ALL PASSED and the game project built with 0 errors before the commit.
- **Limitations:** Ignored assets exist only on this machine. The packs can be downloaded again from the Quaternius itch.io page, and Hunyuan shapes can be regenerated from the tracked concepts with a fixed seed.

## 2026-11-23 — Documented the approved character pipeline

- **Purpose:** The user approved woodcutter-v2 and asked to record the process for future characters.
- **Changed:** Added `docs/character-pipeline.md` with steps, ChatGPT T-pose prompt, commands, acceptance checks and pitfalls. Moved the review scene and GIF script from the temporary scratchpad to `tools/character-review/`. Added a pointer in Claude's project memory.
- **User-visible behavior:** None in the app.
- **Verified:** Files were written and copied. The copied review project was not re-run from its new location, and the documented process has not yet been repeated on a second character.
- **Limitations:** Scripts still hardcode woodcutter paths, the axe and adult-male joint heights, so the next role needs them parameterized. Nothing was committed.

## 2026-11-23 — Regenerated woodcutter in T-pose and re-rigged (v2)

- **Purpose:** The user saw the v1 body as warped and asked to regenerate in T-pose. Measurement showed two causes: v1 auto-fitted joints were asymmetric (hips 17 cm forward, zigzag spine), and its lowered arms did not match the skeleton's horizontal T-pose.
- **Changed:** Saved the user's ChatGPT sheet to `assets-candidates/concepts/woodcutter-tpose/`. Added `tools/prepare_tpose_views.py` and `tools/build_tpose_character.py` (straight spine, mirrored limbs, measured on the dense mesh). `tools/generate_hunyuan_shape.py` and `tools/rig_ual_character.py` now take a version name. `build_hq_character.py` gained `MASK_LEFT_PROPS`. Strike contact is now the last frame before the hand reverses. Output is in `assets-candidates/hunyuan3d/woodcutter-v2/`, using `raw/side-as-right.glb`.
- **User-visible behavior:** None in the app. The running game still uses the old villagers.
- **Verified:** Hunyuan produced a watertight 344,930-face shape. Final body is 2,000 triangles, 1.80 m tall, axe 178 triangles. Blender export succeeded, and Godot rendered idle, walk, carry and all 30 chop frames. Body stays upright in the reviewed frames, both hands are on the handle, left wrist error is 0.003, and the blade lands flat against a test trunk.
- **Limitations:** The hand path at contact is still diagonal (0.766 horizontal, 0.643 vertical) because the source clip swings from overhead; only the final approach and blade are level. Fingers do not curl around the handle. The axe is held but not tuned for walk and carry. Texture coverage is 85.4%, with the rest filled from neighboring pixels. Not tested at game camera distance, not integrated, no LOD or VAT, no 300-person measurement. The Hunyuan regional license limit still applies. The always-on-top mitigation is still unconfirmed by the user.

## 2026-11-23 — Woodcutter rigged to CC0 skeleton with horizontal two-handed chop

- **Purpose:** Replace the rejected custom rig with existing tools and meet the user's horizontal-chop requirement.
- **Changed:** Extracted the user-downloaded Quaternius UAL1 and UAL2 packs into `assets-source/quaternius-ual/`. Added `tools/export_hq_landmarks.py` and `tools/rig_ual_character.py`. The Blender script fits the UAL skeleton to the 2,000-triangle woodcutter, binds with Automatic Weights, uses `Idle_Loop`, `Walk_Loop`, `Walk_Carry_Loop` and `TreeChopping_Loop`, attaches a 178-triangle axe to `hand_r`, and bakes the left arm onto the handle with Blender IK and Damped Track. Output: `assets-candidates/hunyuan3d/woodcutter-v1/rigged/woodcutter.glb` and `rig-report.json`; review images in `review/woodcutter-chop.gif` and `review/woodcutter-chop-sheet.png`.
- **User-visible behavior:** None in the app. The running game still uses the old villagers.
- **Verified:** Blender export succeeded and Godot loaded and rendered all 30 chop frames from the GLB. At contact the swing direction is 0.991 horizontal and 0.136 vertical, and the blade edge reaches a test trunk. Both hands are on the handle in the reviewed frames.
- **Limitations:** The left wrist stays 0.115 units from its solver target on every frame, and fingers do not curl because finger bones are excluded from deformation. Idle, walk and carry were only checked before the final axe orientation, and the axe is not yet tuned for those clips. Not tested at game camera distance, not integrated, no LOD or VAT, and no 300-person performance measurement. The always-on-top mitigation is still unconfirmed by the user.

## 2026-11-23 — Blender ready, waiting for CC0 animation packs

- **Purpose:** Replace the custom rig with existing tools.
- **Changed:** Extracted Blender 5.3.0 to ignored `.tools/blender` after matching the winget SHA-256 from an official mirror. Cloned Make-It-Animatable v2 to `.tools` for inspection only and did not install it. Created `assets-source/quaternius-ual/download/`.
- **User-visible behavior:** None in the app.
- **Decision:** Do not use Make-It-Animatable here because its pipeline references an Auto-Rig Pro fork and a gated Mixamo-derived dataset with no stated license. Planned path: Quaternius CC0 skeleton and clips, Blender automatic weights, then a horizontal axe swing authored in Blender.
- **Blocked:** Scripted download from itch.io did not work and a further attempt was denied by the permission system. Do not retry it. The user must download `Universal Animation Library[Standard].zip` and `Universal Animation Library 2[Standard].zip` into that folder.
- **Verified:** `blender.exe --version` reports 5.3.0. Rigging and animation have not been tested.

## 2026-11-23 — 2,000-triangle woodcutter test, custom rig stopped

- **Purpose:** Test the Hunyuan woodcutter at game budget beside the current model.
- **Changed:** Added `tools/build_hq_character.py` and review scene `game/debug/CharacterCompare`. Output is in `assets-candidates/hunyuan3d/woodcutter-v1/game` and `review`. The running app does not use it.
- **User-visible behavior:** None in the app.
- **Verified:** Body has 2,000 triangles, a 1024 texture and 12 test clips, and looks much closer to the concept than the current villager at close and game distance.
- **Limitations:** The custom rig and chop animation are not acceptable: arms do not grip the axe, and the swing is vertical while the user requires a horizontal felling swing. At the user's direction, stop hand-writing rigging and animation. Next candidate pipeline: Blender, Make-It-Animatable or AccuRIG, Quaternius Universal Animation Library 2, Godot retargeting, OpenVAT or an MIT VAT plugin, and meshoptimizer. None has been installed or tested. Model-weight licenses still need checking.

## 2026-11-23 — Hunyuan3D shape test for approved woodcutter

- **Purpose:** Test local image-to-3D from the approved ChatGPT concept.
- **Changed:** Saved concepts in `assets-candidates/concepts/woodcutter-approved/`. Added `tools/prepare_concept_views.py` and `tools/generate_hunyuan_shape.py`. Installed pinned Hunyuan3D-2 commit `f8db630`, an isolated Python environment and model cache under ignored `.tools/`. Outputs are in `assets-candidates/hunyuan3d/woodcutter-v1/`.
- **User-visible behavior:** None in the app.
- **Verified:** Hunyuan3D-2mv Turbo ran on the RTX 3060 in 11-20 seconds with 5.91 GB peak VRAM. `raw/side-as-right.glb` is the better candidate: watertight, 418,168 faces, correct rope and pouch sides. Shaded preview reviewed.
- **Limitations:** Shape only, with no texture, retopology, rig or game test. Hands and face are soft, the rope and pouch are fused to the clothing, and the 270-degree view shows a duplicated pouch shape. The Hunyuan license excludes use, distribution and display in the EU, UK and South Korea, which matters before any public release.

## 2026-11-23 — Start image-first character concepts with Codex

- **Purpose:** The user rejected the Python-generated Codex blocks and wants orthographic and isometric character images first, at a polished city-builder level.
- **Changed:** Added `docs/character-concept-prompt.md`. Started Codex CLI image generation for one original woodcutter in `assets-candidates/codex-concepts/woodcutter-v1/`.
- **User-visible behavior:** None in the app.
- **Verified:** No images were produced. The PATH Codex CLI 0.130 failed because `gpt-5.5` is rejected. The desktop app's Codex 0.171 ran with `gpt-6-luna` and `gpt-5.6-terra`, and both reported no built-in image tool in `codex exec`, although the feature flag is enabled. Codex wrote a failure `REPORT.md` and a local `AGENT_MEMORY.md` in the concept folder.
- **Limitation:** The next attempt must use an interactive Codex or ChatGPT session with image generation. No API key was used. Concepts are images only; no 3D conversion or integration should start until the user approves a design.

## 2026-11-23 — Mitigate wallpaper covering the primary monitor

- **Purpose:** The user reported DeskCiv as always on top and taking over one monitor.
- **Evidence:** While it happened, the window was still a child of Progman without a topmost style. Z-order tests with normal windows could not reproduce it. The suspected cause is GPU-driver direct scanout of an OpenGL window that exactly covers one monitor; this is unconfirmed because Win32 window-order checks cannot see it.
- **Changed:** `DesktopHost` makes the window one pixel shorter than the monitor. `TrayIcon` treats an external close as a graceful quit. Added `stop-deskciv.cmd`, `tools/stop-deskciv.ps1` and `tools/zorder-test.ps1`.
- **User-visible behavior:** DeskCiv should remain behind normal windows. If it does not, `stop-deskciv.cmd` saves and quits without using the tray.
- **Verified:** Window is 2560x1439 at 30 FPS. Z-order, functional and desktop probes passed. The stop command exited gracefully. Mouse test passed twice after one earlier run gave inconsistent wheel results that did not recur. The app was deliberately left closed so the user can test it.
- **Limitation:** Only the user's physical display can confirm the fix. If it fails, try the ANGLE renderer or another presentation path next.

## 2026-11-23 — Delivery dispatcher, worker roles, asset prompt

- **Purpose:** Commit the road/villager/mouse version, build the next logistics step, and prepare a fair external asset comparison.
- **Changed:** Commit `c3df393` is on branch `feature/village-logistics`. After it, uncommitted changes add `Role` (porter, builder, worker), a priority-based delivery dispatcher, role assignment, open-land building sites with road links, save format 3 with version 2 loading, test checks, and `docs/asset-generation-prompt.md`.
- **User-visible behavior:** Porters wait near storehouses and carry goods by urgency and distance. Builders handle construction and roads. Workers stay at their workplaces unless urgent deliveries need help. The village can open new streets when roadside land runs out. The existing world continues.
- **Verified:** Simulation tests pass for 12 worlds. The real version 2 save loaded at year 0.37 with 21 people. At 2560x1440 with 104 people over 60 seconds: 30 FPS, 0.56% CPU, 3.8% average and 3.97% peak GPU, 275 MB RAM. Functional, mouse and desktop probes passed.
- **Limitations:** Logistics changes are not committed. Performance is unmeasured at 300 people. Schools, inns, visible stock at buildings and longer production chains are not built. The asset prompt has not been run in Codex or Gemini.

## 2026-11-23 — Mouse pan and zoom on the wallpaper

- **Purpose:** The user asked to pan and zoom with the mouse.
- **Changed:** `TrayIcon` adds a pass-through low-level mouse hook; `Main` adds manual camera handling and extra metric columns; `DesktopHost`, `Win32` and `WorldView` support it. Added `tools/mouse-test.ps1`. Raw Input was tried first but delivered no events in the Godot process.
- **User-visible behavior:** With the pointer on the primary monitor's visible desktop, the wheel zooms toward the pointer and middle-button drag moves the map. Ctrl+wheel stays with Windows icon sizing. Mouse events are never blocked. The automatic camera resumes after 60 seconds without wheel or middle-drag input.
- **Verified:** `tools/mouse-test.ps1` passed with injected input: zoom in/out, pan, input ignored over another window, director resume, clean exit.
- **Limitations:** Not tested with a physical mouse, touchpad gestures or remote-desktop tools. An earlier test run showed unexplained zoom values once; the instrumented rerun was correct. Changes are not committed.

## 2026-11-23 — Tile roads, 300-person cap, original villager style

- **Purpose:** Start the logistics-style rebuild and test a clearer visual style. The user set the population target to about 200-300.
- **Changed:** `Simulation` and `Model` use a 4-unit tile grid, building footprints, doors, A* paths, planned and built roads, road labor, and save format 2. `MaxPeople` is 300. Added `tools/bake_villagers.py`, which generates original villagers, tools and 12 clips. Rebuilt building models and carried goods in `MeshKit`; updated `SimHost`, `WorldView`, `CharacterKit`, `Main`, `PoseSheet` and tests. The camera now moves closer and searches for an unobstructed angle.
- **User-visible behavior:** The village grows along dirt roads, buildings face roads, workers wear job-specific clothes and use visible tools and loads. A previous version 1 world is set aside as `world.sav.bad` and a new world starts.
- **Verified:** Simulation tests pass, including no overlapping buildings, doors on roads and the population cap. At 2560x1440 with 110 people over 60 seconds: 30 FPS, 0.48% CPU, 4.56% average and 9.53% peak GPU, 275 MB RAM. Functional test and desktop probe passed. The app was relaunched, started a new world and kept the old one as `world.sav.bad`.
- **Limitations:** Offer/demand logistics, specialist training, inns, visible building stock and longer production chains are not built. Work animations were reviewed in stills. The camera can still choose a poor woodcutter shot. These changes are not committed.

## 2026-11-23 — First local version committed

- **Purpose:** The user asked to save the current state as a version.
- **Changed:** Added `.gitignore`, initialized a local Git repository, and created the first commit with tag `v0.2-neolithic`. `.tools/`, generated build files and `measurements/` are not tracked. Nothing was pushed.
- **User-visible behavior:** None. The running app is unchanged.
- **Verified:** Git status and tracked-file list checked after commit. No new app tests were needed because code did not change.
- **Limitation:** A fresh clone must download Godot 4.7.2 .NET into `.tools/` before `start-deskciv.cmd` works.

## 2026-11-23 — One-monitor mode and animated visual test scene

- **Purpose:** The user asked for nicer low-poly models that show activity, and for the app to use one monitor because the seam hid the center. This replaces the earlier two-monitor decision.
- **Changed:** `DesktopHost` now covers and evaluates only the primary monitor. Added `tools/bake_characters.py`, `assets-source/quaternius-cube-world` (CC0), baked files in `game/assets/characters`, `CharacterKit`, and review scene `game/debug/PoseSheet`. Updated `WorldView`, `MeshKit`, `SimHost`, `Main`, `Simulation`, and test scripts.
- **User-visible behavior:** Four animated character models with tools and work clips for chopping, hoeing, mining, sowing, gathering, fishing, building and kneading. New thatched huts, wheat, grass, flowers, stones, chimney smoke, falling trees, and a closer camera that visits work areas with periodic overviews.
- **Verified:** Simulation tests pass. At 2560x1440 with about 100 people: 30 FPS, 0.48% CPU, 3.82% average and 4.05% peak GPU, 267 MB RAM over 60 seconds. Functional test passed. Desktop probe had 0 hung samples. Blank-desktop click worked in 3 of 4 final runs; in the other, Chrome stayed foreground and the cause is not established.
- **Limitations:** Work clips are generated from the rig and reviewed mostly as still poses, not in motion. A close-up of chopping beside a tree was not captured because the camera fell back to the overview. Up to 320 nearby people use full models; the rest use simple shapes. Medieval MegaKit and Universal Animation Library are not integrated. Portable export must include the custom `.dcmesh` and `rig.bin` files.

## 2026-11-23 — Brighter night and test speed slider

- **Purpose:** The user wanted visible nights and a way to speed up the world for testing.
- **Changed:** `Main` lighting, `TrayIcon` speed slider, `SimHost` adjustable speed with a 70 ms step budget per tick, `tools/functional-test.ps1`.
- **User-visible behavior:** Night follows the real clock but stays dimly lit. The tray volume popup has a Tốc độ slider from x1 to x500. Speed is not saved and returns to x1 on each start. The overlay shows speed above x1.
- **Verified:** Night screenshot reviewed. Functional test passed, with x100 measured at about x88 using 5-second metric samples while holding 30 FPS. Desktop probe: 0 hung samples in 16, blank click handled in 12 ms.
- **Limitations:** High speeds are capped by the step budget, so a large city may not reach the selected speed. x500 was not measured. The user's own DeskCiv instance (started 18:49) was left running and must be restarted to be sure it has this build.

## 2026-11-23 — Fix desktop hang and busy cursor

- **Purpose:** The user could not click desktop icons and saw a busy cursor while DeskCiv ran.
- **Evidence:** Windows flagged the DeskCiv window as hung from about 5 seconds after launch, and in some launches it held the foreground. Message pings still answered, so the earlier hit test missed the fault.
- **Changed:** `DesktopHost` detaches from Explorer's input queue, disables window input, hands foreground back whenever it receives it, and signals message-loop liveness each frame. `project.godot` sets `no_focus`. Added `tools/desktop-input-probe.ps1`.
- **User-visible behavior:** DeskCiv should no longer block the desktop or show the busy cursor.
- **Verified:** Five launches had 0 hung samples and 0 foreground samples, with a real click on blank desktop handled in 30 ms. A 2-minute run had 0 hung samples in 113 and no busy cursor. The functional test passed.
- **Limitations:** The fixes were applied together, so the exact contribution of each is unproven. Automated clicks targeted blank desktop only, not opening an icon; the user must confirm that.

## 2026-11-23 — Milestone 2 Neolithic vertical slice built

- **Purpose:** Replace dummy characters with a complete Neolithic settlement. Stop for user review before milestone 3.
- **Changed:** Added engine-free simulation in `game/src/Sim/` (`WorldMap`, `Model`, `Simulation`), `SimHost`, `MeshKit`, `WorldView`, rewrote `Main`, extended tray and settings. Removed prototype `Crowd.cs` and `Terrain.cs`. Added `tests/SimTests`, `tools/screenshots.ps1`, `start-deskciv.cmd`. Measurement scripts now use `--fresh --no-save`.
- **User-visible behavior:** A tribe of 20 founds a riverside village. People age, marry, have children, eat, keep huts warm and die. Goods are physically carried through berries, fish, wood, stone and grain to flour to bread. Four seasons, real-clock day/night, worn paths, graves, drought, harsh winter, sickness, bumper harvest, migrants, a slow event camera, a Vietnamese overlay with tray toggle, autosave every 5 minutes and on exit.
- **Verified:** `dotnet run -c Release` in `tests/SimTests` passes: determinism, save/load continuation, goods conservation, and 12 worlds for 50 simulated years in day, night and mixed patterns, none extinct and all completing the era. Desktop at 5120x1440, year 40, about 75 people: 30 FPS, 0.47% CPU, 2.71% average and 5.02% peak GPU, 252 MB RAM over 60 seconds. Functional test passed. Save and resume passed end to end; the test save was removed.
- **Limitations:** No real-time 42-year run. Performance not re-measured with a large late-era city. Windows-shutdown save path is implemented but not exercised. Audio remains placeholder and not heard; yielding to other apps' audio is not built. Models are procedural; CC0 assets are not used yet. Food surplus can grow large, so balance needs tuning. After the Neolithic the world stays in that era. Auto quality, Windows startup, portable export, NVIDIA overlay toast and history replay remain open.

## 2026-11-23 — 1-hour soak cancelled by user

- **Purpose:** The user observed no RAM growth and said the measurement is not needed.
- **Changed:** Stopped the soak with a normal window close. No code changed.
- **Verified:** The run stopped at about 222 seconds at 30 FPS, RAM 227 MB to 233 MB (peak 234 MB).
- **Limitation:** The 1-hour criterion is waived by the user and was not measured. Run a longer soak once real simulation exists. Milestone 1 is ready for user review; do not start milestone 2 without go-ahead.

## 2026-11-23 — Milestone 1 prototype built, 1-hour soak in progress

- **Purpose:** Prove the desktop-wallpaper host and performance budget before building the simulation.
- **Changed:** Added Godot 4.7.2 .NET in `.tools/` (SHA-512 verified), the project in `game/`, and `tools/measure.ps1` and `tools/functional-test.ps1`. Results go to `measurements/`.
- **User-visible behavior:** Renders a generated valley with 2,000 dummy characters behind desktop icons across both monitors. Tray left-click opens volume sliders and mute; right-click offers pause and quit. Generated wind and sparse chord are placeholders.
- **Verified:** 60-second run at 5120x1440: 30 FPS, 0.49% CPU, 3.07% average and 6.7% peak GPU 3D, 228 MB RAM. Functional test passed: icon hit test, tray popup, volume save and bus level, mute, stop at fullscreen and recovery, clean exit.
- **Limitations:** 1-hour soak not finished when this entry was written. Audio was tested through bus levels with a dummy driver, not by ear. Lighting is currently simple with no shadows, so budgets will need re-measuring as content grows. Auto quality, audio yielding to other apps, Windows startup, overlay and portable export are not built.
- **Note for agents:** In PowerShell, passing `$null` to a .NET string parameter sends `""`; use `[NullString]::Value`, especially with the wallpaper API.

## 2026-11-23 — Requirements round 4 answered, clarification complete

- **Purpose:** Record the final decisions. All design branches have an answer.
- **Changed:** This file only. No application code exists and Godot is not installed.
- **Audio:** Ambient sound varies by era, season, hour and camera position, plus sparse soft music, from CC0 or self-made sources. Left-clicking the tray icon opens Master, Ambient and Music sliders plus mute; right-click opens the menu. Levels persist. First run is 15%, with a 10-second fade-in at each start. Audio fades out for other apps' sound, fullscreen apps and lock screen, with a tray option to disable this.
- **Runtime:** Re-layout on monitor or resolution change. Auto quality lowers shadows, render resolution, then visible characters, without affecting simulation; presets are Low, Medium, High, Auto (default).
- **Data and packaging:** Keep the current world plus short chronicles and screenshots of past worlds in `%LOCALAPPDATA%\DeskCiv`. Ship as a portable DeskCiv folder needing no .NET or Godot install.
- **Prototype pass criteria (all required):** Runs behind icons on both monitors with icons still clickable; terrain and 2,000 dummy characters stable at 30 FPS at 5120x1440; average under 5% CPU, 10% GPU, 1.5 GB RAM; 1-hour run without abnormal RAM growth; tray volume works; rendering stops for fullscreen apps. On a miss, report real measurements and options; do not lower the criteria.
- **Verification:** None needed beyond recording the decisions. Feasibility of the targets is unproven.
- **Next:** Wait for the user's go-ahead, then build milestone 1 and stop for review.

## 2026-11-23 — Requirements round 3 answered

- **Purpose:** Record the user's round 3 decisions.
- **Changed:** This file only. No application code exists.
- **Accepted:** Godot 4 + C#, reconsider custom C#/DirectX only if the prototype misses targets. Standalone exe behind desktop icons, no mouse/keyboard input, tray menu for pause, overlay, settings, quit and Windows startup. Stop rendering when both monitors are covered or a fullscreen app runs; 15 FPS when one is covered. World freezes while PC is off or asleep, autosaves every 5 minutes and on shutdown. Physically hauled goods, age, families, housing and winter stockpiles. Director acts only indirectly, population never reaches 0, support rises if an era runs past 21 hours. Organic city growth that keeps historic traces. Conflict comes from outside the valley with no detailed battles. CC0 assets with a unified palette, procedural modular buildings for gaps. Milestones: technical prototype (stop for user review), Neolithic vertical slice, then all eras and replay.
- **Changed from recommendation:** The app must have sound, with volume adjustable from the system tray.
- **Verification:** None needed beyond recording the decisions. Godot is not installed yet.
- **Unresolved:** Audio details, display changes, quality scaling, world history, packaging and prototype pass criteria.

## 2026-11-23 — Requirements round 2 answered

- **Purpose:** Record the user's round 2 decisions.
- **Changed:** This file only. No application code exists.
- **Accepted:** 3D low-poly replaces 2D pixel art, and a performance prototype comes first. New project that learns from Latticefolk's architecture and may reuse its CC0 assets; copy a module only if clearly worthwhile, keeping MIT credit. One river valley growing from about 20 people to a city, up to about 2,000 individually simulated people, with aggregate population beyond that. Slow high-angle director camera with no shake or hard cuts. One season cycle = 30 real minutes; displayed calendar is non-linear from about 10,000 BCE to 2000. Day/night follows the real PC clock. Eight eras at about 21 real hours each: Paleolithic, Neolithic, Bronze, Iron/Classical, Medieval, Renaissance/Colonial sail, Industrial, 20th century. Fully offline, rule-based decisions.
- **Verification:** None needed beyond recording the decisions. The 10% GPU target at 5120x1440 is unproven until the prototype is measured.
- **Unresolved:** Engine, Windows integration, off-time behavior, simulation loop, director limits, city growth, conflict, assets, audio and first milestone.

## 2026-11-23 — Requirements round 1 answered

- **Purpose:** Record the user's round 1 decisions and the Latticefolk review.
- **Changed:** This file only. No application code exists.
- **Accepted:** Personal use first, with only self-made or clearly licensed assets. Hybrid simulation with a hidden director. About 7 real days per run, Stone Age to 2000. After a run, about 2 minutes of history replay, then a new world. Fictional civilization. Non-graphic hardship. One world across both monitors (5120x1440). Max 30 FPS, under 5% CPU and 10% GPU, render pause for fullscreen apps, persistent progress. Small, optional Vietnamese overlay.
- **Needs reconfirmation:** The user accepted 2D isometric pixel art before the Latticefolk link was reviewed. Latticefolk is 3D, so style is reopened in round 2.
- **Latticefolk facts (`ranzevor/Latticefolk`, read through GitHub only, not run):** MIT, TypeScript, Three.js, Node/Express/SQLite, CC0 asset manifest. Uses deterministic simulation, coarse/fine chunks, production chains, ecology and pluggable decision engines. `src/main.ts` is about 284 KB. It is a first-person/God View sandbox.
- **Unresolved:** Visual style, reuse of Latticefolk, engine, world scale, camera, calendar and era list.

## 2026-11-23 — Install grill-me, start requirements clarification

- **Purpose:** Prepare to clarify requirements for DeskCiv, a non-interactive desktop-background simulation of a civilization from the Stone Age to the 20th century (references: Knights and Merchants, Banished, Latticefolk).
- **Changed:** Created this file. Installed user-level skills `~/.claude/skills/grill-me` and `~/.claude/skills/grilling` from `mattpocock/skills` (MIT) at commit `abeeaf95660ba9e8b7c753eb38f441ca707a8823`. Only `SKILL.md` was installed; the OpenAI agent metadata was omitted.
- **User-visible behavior:** `/grill-me` and `grilling` are available in Claude Code. No application code exists yet.
- **Verified:** Read both skill files before install; they contain instructions only. `grilling` loaded in the session.
- **Machine facts:** RTX 3060, i5-12400F (6C/12T), 32 GB RAM, two 2560x1440 monitors at 59 Hz, Windows 11, .NET SDK 9.0.310, Node 22.22.1, Python via miniconda. Rust and Godot are not installed.
- **Unresolved:** All product requirements are still open.

