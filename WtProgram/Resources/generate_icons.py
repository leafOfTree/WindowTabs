"""Generate the WindowTabs application and tray icons from one scalable design.

Run with the bundled Python runtime (requires Pillow). Keep the individual ICO
frames: Windows uses the small frames for the notification area.
"""

from pathlib import Path
from io import BytesIO
import struct
from PIL import Image, ImageDraw


HERE = Path(__file__).resolve().parent
SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)


def layer(draw, points, fill, outline, width, scale):
    xy = [(round(x * scale), round(y * scale)) for x, y in points]
    draw.polygon(xy, fill=fill)
    draw.line(xy + [xy[0]], fill=outline, width=round(width * scale), joint="curve")
    radius = width * scale / 2
    for x, y in xy:
        draw.ellipse((x-radius, y-radius, x+radius, y+radius), fill=outline)


def frame(size, light):
    # Draw at high resolution, then sample once. The simplified silhouette
    # survives the 16-pixel notification icon without hairline highlights.
    scale = max(4, 1024 // size)
    im = Image.new("RGBA", (size * scale, size * scale))
    d = ImageDraw.Draw(im)
    k = size / 256
    s = scale * k
    if light:
        back, front, edge = "#929292", "#252525", "#F4F4F4"
    else:
        back, front, edge = "#737373", "#F5F5F5", "#202020"

    # Two overlapping window tabs, with consistent clear space around them.
    layer(d, [(30, 31), (107, 31), (107, 65), (214, 65),
              (214, 175), (30, 175)], back, edge, 9, s)
    layer(d, [(48, 111), (139, 111), (139, 89), (222, 89),
              (222, 224), (48, 224)], front, edge, 10, s)
    return im.resize((size, size), Image.Resampling.LANCZOS)


def pencil(size, light):
    # Title-bar glyph for edit dialogs: a solid pencil with a separate cap,
    # drawn along the diagonal. "light" is for a light title bar.
    scale = max(4, 1024 // size)
    im = Image.new("RGBA", (size * scale, size * scale))
    d = ImageDraw.Draw(im)
    s = scale * size / 256
    ink = "#252525" if light else "#F5F5F5"
    tip = (44, 212)
    u = (0.7071, -0.7071)
    n = (0.7071, 0.7071)
    length, half = 236, 30

    def at(t, w):
        return (round((tip[0] + u[0] * t + n[0] * w) * s),
                round((tip[1] + u[1] * t + n[1] * w) * s))

    d.polygon([at(0, 0), at(52, -half), at(length - 58, -half),
               at(length - 58, half), at(52, half)], fill=ink)
    d.polygon([at(length - 40, -half), at(length, -half),
               at(length, half), at(length - 40, half)], fill=ink)
    return im.resize((size, size), Image.Resampling.LANCZOS)


def write_icon(name, images):
    payloads = []
    for image in images:
        stream = BytesIO()
        image.save(stream, format="PNG")
        payloads.append(stream.getvalue())
    offset = 6 + 16 * len(SIZES)
    with (HERE / name).open("wb") as icon:
        icon.write(struct.pack("<HHH", 0, 1, len(SIZES)))
        for size, payload in zip(SIZES, payloads):
            icon.write(struct.pack("<BBBBHHII", size % 256, size % 256,
                                   0, 0, 1, 32, len(payload), offset))
            offset += len(payload)
        for payload in payloads:
            icon.write(payload)


for name, light in (("Bemo.ico", False), ("BemoLight.ico", True)):
    write_icon(name, [frame(size, light) for size in SIZES])
for name, light in (("edit.ico", False), ("editLight.ico", True)):
    write_icon(name, [pencil(size, light) for size in SIZES])
