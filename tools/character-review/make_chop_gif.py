import json
from pathlib import Path

from PIL import Image

BASE = Path(r"D:\2026\101.DeskCiv\assets-candidates\hunyuan3d\woodcutter-v2")
frames = [Image.open(p).convert("RGB") for p in sorted((BASE / "review" / "frames").glob("*.png"))]
report = json.loads((BASE / "rigged" / "rig-report.json").read_text(encoding="utf-8"))["axe"]
contact = report["contact_index"]
box = (360, 40, 1560, 1040)
small = [f.crop(box).resize((600, 500), Image.Resampling.LANCZOS) for f in frames]
palette = small[contact].quantize(colors=128, method=Image.Quantize.MEDIANCUT)
gif = [f.quantize(palette=palette, dither=Image.Dither.NONE) for f in small]
gif[0].save(BASE / "review" / "woodcutter-chop.gif", save_all=True, append_images=gif[1:], duration=33, loop=0, optimize=True)
offsets = [-6, -4, -2, -1, 0, 3]
strip = Image.new("RGB", (1800, 500))
for i, offset in enumerate(offsets):
    strip.paste(small[(contact + offset) % len(small)].resize((300, 250), Image.Resampling.LANCZOS), (i * 300, 0))
sheet = Image.new("RGB", (1800, 1000))
for i, offset in enumerate(offsets):
    tile = small[(contact + offset) % len(small)]
    sheet.paste(tile, ((i % 3) * 600, (i // 3) * 500))
sheet.save(BASE / "review" / "woodcutter-chop-sheet.png")
print("frames", len(frames), "contact", contact, "gif_kb", (BASE / "review" / "woodcutter-chop.gif").stat().st_size // 1024)
