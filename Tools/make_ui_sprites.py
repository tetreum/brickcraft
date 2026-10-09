"""
Draws the white UI sprites of the menus (icons, rounded corners, circles), which Unity tints.
Shapes are drawn 4x bigger and scaled down for smooth edges.

    python Tools/make_ui_sprites.py [output folder]

The output folder defaults to Assets/Textures/UI/Sprites. Needs Pillow (pip install pillow).
After changing a sprite, Unity reimports it keeping its import settings (sprite, 9-slice borders).
"""
import math, sys, os
from PIL import Image, ImageDraw

OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Textures", "UI", "Sprites")
os.makedirs(OUT, exist_ok=True)
S = 4

def save(mask, size, name):
    mask = mask.resize((size, size), Image.LANCZOS)
    img = Image.new("RGBA", (size, size), (255, 255, 255, 0))
    img.putalpha(mask)
    img.save(os.path.join(OUT, name + ".png"))

def canvas(size):
    m = Image.new("L", (size * S, size * S), 0)
    return m, ImageDraw.Draw(m)

def rounded(name, size, radius, stroke=None):
    m, d = canvas(size)
    d.rounded_rectangle([0, 0, size * S - 1, size * S - 1], radius * S, fill=255)
    if stroke:
        d.rounded_rectangle([stroke * S, stroke * S, size * S - 1 - stroke * S, size * S - 1 - stroke * S], (radius - stroke) * S, fill=0)
    save(m, size, name)

# 9-sliced shapes: rounded corners of 16 px, its outline, and a circle (a pill when sliced)
rounded("rounded16", 48, 16)
rounded("rounded16_outline", 48, 16, stroke=2)
m, d = canvas(64); d.ellipse([0, 0, 64 * S - 1, 64 * S - 1], fill=255); save(m, 64, "circle")
m, d = canvas(64); d.ellipse([0, 0, 64 * S - 1, 64 * S - 1], fill=255); d.ellipse([5 * S, 5 * S, 59 * S, 59 * S], fill=0); save(m, 64, "circle_outline")

# icons, on a 512 grid
N = 128
def icon():
    m = Image.new("L", (512, 512), 0)
    return m, ImageDraw.Draw(m)
def done(m, name):
    img = Image.new("RGBA", (N, N), (255, 255, 255, 0)); img.putalpha(m.resize((N, N), Image.LANCZOS)); img.save(os.path.join(OUT, name + ".png"))

# gear: body, 8 teeth, hole
m, d = icon()
c = 256
d.ellipse([c - 165, c - 165, c + 165, c + 165], fill=255)
for i in range(8):
    a = i * math.pi / 4
    w, r0, r1 = 46, 140, 232
    pts = []
    for (r, s) in ((r0, -1), (r1, -0.8), (r1, 0.8), (r0, 1)):
        ox, oy = math.cos(a) * r, math.sin(a) * r
        px, py = -math.sin(a) * w * s, math.cos(a) * w * s
        pts.append((c + ox + px, c + oy + py))
    d.polygon(pts, fill=255)
d.ellipse([c - 72, c - 72, c + 72, c + 72], fill=0)
done(m, "icon_gear")

# globe: outline, meridian, parallels
m, d = icon()
d.ellipse([46, 46, 466, 466], outline=255, width=34)
d.ellipse([156, 46, 356, 466], outline=255, width=30)
d.line([256, 50, 256, 462], fill=255, width=30)
d.line([50, 256, 462, 256], fill=255, width=30)
d.line([96, 160, 416, 160], fill=255, width=28)
d.line([96, 352, 416, 352], fill=255, width=28)
done(m, "icon_globe")

# difficulty: three bars
m, d = icon()
for i, h in enumerate((150, 250, 370)):
    x = 70 + i * 135
    d.rounded_rectangle([x, 450 - h, x + 105, 450], 22, fill=255)
done(m, "icon_difficulty")

# floppy disk
m, d = icon()
d.rounded_rectangle([60, 60, 452, 452], 50, fill=255)
d.polygon([(380, 56), (456, 56), (456, 132)], fill=0)
d.rounded_rectangle([140, 60, 340, 175], 14, fill=0)
d.rectangle([270, 82, 312, 160], fill=255)
d.rounded_rectangle([118, 270, 394, 452], 22, fill=0)
d.rectangle([118, 380, 394, 452], fill=0)
done(m, "icon_save")

# map pin
m, d = icon()
d.ellipse([106, 40, 406, 340], fill=255)
d.polygon([(124, 250), (388, 250), (256, 478)], fill=255)
d.ellipse([196, 130, 316, 250], fill=0)
done(m, "icon_pin")

# crosshair
m, d = icon()
d.ellipse([96, 96, 416, 416], outline=255, width=36)
for (x0, y0, x1, y1) in ((256, 30, 256, 170), (256, 342, 256, 482), (30, 256, 170, 256), (342, 256, 482, 256)):
    d.line([x0, y0, x1, y1], fill=255, width=40)
d.ellipse([232, 232, 280, 280], fill=255)
done(m, "icon_crosshair")

# light bulb
m, d = icon()
d.ellipse([106, 30, 406, 330], fill=255)
d.polygon([(150, 260), (362, 260), (330, 360), (182, 360)], fill=255)
d.rounded_rectangle([176, 380, 336, 418], 16, fill=255)
d.rounded_rectangle([196, 430, 316, 466], 16, fill=255)
d.ellipse([226, 456, 286, 490], fill=255)
done(m, "icon_bulb")

# dropdown chevron
m, d = icon()
d.line([(120, 190), (256, 326), (392, 190)], fill=255, width=48, joint="curve")
for (x, y) in ((120, 190), (392, 190), (256, 326)):
    d.ellipse([x - 24, y - 24, x + 24, y + 24], fill=255)
done(m, "icon_chevron")

# main menu: play triangle
m, d = icon()
d.polygon([(150, 96), (150, 416), (430, 256)], fill=255)
d.line([(150, 96), (150, 416), (430, 256), (150, 96)], fill=255, width=60, joint="curve")
for (x, y) in ((150, 96), (150, 416), (430, 256)):
    d.ellipse([x - 30, y - 30, x + 30, y + 30], fill=255)
done(m, "icon_play")

# main menu: a cube (the world), its faces in different shades
m, d = icon()
top = [(256, 40), (456, 150), (256, 260), (56, 150)]
left = [(56, 150), (256, 260), (256, 480), (56, 370)]
right = [(256, 260), (456, 150), (456, 370), (256, 480)]
d.polygon(top, fill=255); d.polygon(left, fill=200); d.polygon(right, fill=150)
d.line(top + [top[0]], fill=0, width=14, joint="curve"); d.line([(256, 260), (256, 480)], fill=0, width=14)
done(m, "icon_cube")

# main menu: two people
m, d = icon()
def person(d, cx, top, scale, fill):
    r = 70 * scale
    d.ellipse([cx - r, top, cx + r, top + 2 * r], fill=fill)
    w, h = 130 * scale, 150 * scale
    y = top + 2 * r + 20 * scale
    d.rounded_rectangle([cx - w, y, cx + w, y + h], 60 * scale, fill=fill)
person(d, 330, 70, 0.95, 255)
person(d, 200, 100, 1.12, 0)  # gap around the front person
person(d, 200, 120, 1.0, 255)
done(m, "icon_people")

# main menu: a server (hosting)
m, d = icon()
for y in (70, 290):
    d.rounded_rectangle([70, y, 442, y + 160], 34, fill=255)
    d.ellipse([110, y + 58, 154, y + 102], fill=0)
    d.rounded_rectangle([300, y + 66, 400, y + 94], 14, fill=0)
done(m, "icon_server")

# main menu: exit, a door and an arrow out of it
m, d = icon()
d.line([(300, 150), (300, 70), (80, 70), (80, 442), (300, 442), (300, 362)], fill=255, width=48, joint="curve")
d.line([(200, 256), (430, 256)], fill=255, width=52)
d.polygon([(470, 256), (370, 160), (370, 352)], fill=255)
done(m, "icon_exit")
print("ok")
