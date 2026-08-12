from pathlib import Path
from PIL import Image


workspace = Path(__file__).resolve().parent.parent
source = workspace / "assets" / "csv-peek-icon.png"
target = workspace / "assets" / "csv-peek.ico"

with Image.open(source) as image:
    rgba = image.convert("RGBA")
    rgba.save(
        target,
        format="ICO",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )

print(target)

