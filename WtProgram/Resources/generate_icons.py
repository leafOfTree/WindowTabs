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


def layer(draw, commands, fill, outline, width, scale, offset):
    points = []
    for command in commands:
        if len(command) == 2:
            points.append(command)
        else:
            x, y = points[-1]
            cx, cy, end_x, end_y = command
            for step in range(1, 25):
                t = step / 24
                points.append(((1-t)**2*x + 2*(1-t)*t*cx + t*t*end_x,
                               (1-t)**2*y + 2*(1-t)*t*cy + t*t*end_y))
    ox, oy = offset
    xy = [(round(x * scale + ox), round(y * scale + oy)) for x, y in points]
    draw.polygon(xy, fill=fill)
    draw.line(xy + [xy[0]], fill=outline, width=round(width * scale), joint="curve")
    radius = width * scale / 2
    for x, y in xy:
        draw.ellipse((x-radius, y-radius, x+radius, y+radius), fill=outline)


def frame(size):
    # Draw at high resolution, then sample once. The simplified silhouette
    # survives the 16-pixel notification icon without hairline highlights.
    scale = max(4, 1024 // size)
    im = Image.new("RGBA", (size * scale, size * scale))
    d = ImageDraw.Draw(im)
    # Small shell icons need optical sizing rather than scaled logo clear space.
    k = (size - 2) / 201 if size <= 48 else size / 256
    s = scale * k
    offset = ((size/2 - 126*k)*scale, (size/2 - 127.5*k)*scale) if size <= 48 else (0, 0)
    # A light front tab with a dark outline reads on light and dark taskbars alike.
    back, front, edge = "#858585", "#F5F5F5", "#202020"

    # Match docs/images/logo.svg: equal tab widths/heights and rounded corners.
    layer(d, [(54,31), (83,31), (107,31,107,55), (107,65),
              (190,65), (214,65,214,89), (214,151), (214,175,190,175),
              (54,175), (30,175,30,151), (30,55), (30,31,54,31)], back, edge, 8, s, offset)
    layer(d, [(72,111), (145,111), (145,101), (145,77,169,77),
              (198,77), (222,77,222,101), (222,200), (222,224,198,224),
              (72,224), (48,224,48,200), (48,135), (48,111,72,111)], front, edge, 8, s, offset)
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


write_icon("Bemo.ico", [frame(size) for size in SIZES])
