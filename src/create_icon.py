"""Generate the local notebook-and-moon icon without external image assets."""

from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parent
scale = 4
image = Image.new("RGBA", (256 * scale, 256 * scale), (0, 0, 0, 0))
draw = ImageDraw.Draw(image)


def bounds(box):
    return tuple(int(value * scale) for value in box)


def rounded(box, radius, color):
    draw.rounded_rectangle(bounds(box), radius=int(radius * scale), fill=color)


background = "#89ABC2"
rounded((8, 8, 248, 248), 54, background)

# A small warm moon above an ivory notebook.
draw.ellipse(bounds((156, 33, 216, 93)), fill="#FFF1C9")
draw.ellipse(bounds((172, 26, 223, 78)), fill=background)
draw.polygon(
    [(80 * scale, 37 * scale), (83 * scale, 44 * scale),
     (90 * scale, 47 * scale), (83 * scale, 50 * scale),
     (80 * scale, 57 * scale), (77 * scale, 50 * scale),
     (70 * scale, 47 * scale), (77 * scale, 44 * scale)],
    fill="#EAF3F8",
)
draw.ellipse(bounds((123, 46, 129, 52)), fill="#EAF3F8")

rounded((54, 89, 202, 222), 18, "#779CB7")
rounded((51, 83, 199, 215), 17, "#FFFDF5")
rounded((51, 83, 74, 215), 10, "#D1E2EB")
draw.line(bounds((74, 88, 74, 210)), fill="#B9D0DE", width=2 * scale)
for y, end in ((119, 172), (148, 172), (177, 150)):
    rounded((92, y, end, y + 7), 3.5, "#7191A7")

image = image.resize((256, 256), Image.Resampling.LANCZOS)
image.save(root / "app.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
image.save(root / "app.png")
