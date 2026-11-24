# DeskCiv

A Windows desktop-wallpaper civilisation simulation, built with Godot 4 and C#. A village on a river estuary grows by itself from the Old Stone Age to the 20th century: villagers farm, fish, cut wood, quarry stone, build windmills, water mills and dikes, and reclaim land from the sea. It runs behind the desktop icons and is not interactive.

## Run

1. Download Godot 4.7.2 .NET for Windows into `.tools/Godot_v4.7.2-stable_mono_win64/`.
2. Run `start-deskciv.cmd`. Stop it with `stop-deskciv.cmd` or from the tray icon.

Test runs in a window: `Godot_v4.7.2-stable_mono_win64.exe --path game -- --windowed --resolution 960x540`. Other test flags are documented in `game/src/Main.cs` (`--seed`, `--start-year`, `--hour`, `--weather`, `--shot`, `--look`).

The simulation tests are in `tests/SimTests` (`dotnet run -c Release`).

## Assets in this repository

- Buildings, props and scatter: Kenney Fantasy Town Kit and Nature Kit (CC0).
- Animation clips and skeleton: Quaternius Universal Animation Library (CC0).
- Ground textures: Poly Haven (CC0).
- Concept and reference images: generated with ChatGPT and Gemini (`assets-candidates/concepts`).

See `THIRD_PARTY_NOTICES.md`.

## Not included

The realistic buildings, characters, animals and boats are made with Tencent Hunyuan3D. Its licence does not allow distribution in the EU, the UK or South Korea, so these generated models are not published here. Without them, the game uses its low-poly fallback models. You can rebuild them locally from the reference images with the pipelines in `docs/building-pipeline.md` and `docs/character-pipeline.md`.

## Licence

The code has no open-source licence yet; all rights reserved by the author.
