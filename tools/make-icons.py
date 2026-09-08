#!/usr/bin/env python3
"""
Generates every Windows icon asset in GitHalls.App/Assets from one description
of the mark, so the icon is reproducible instead of a binary nobody can edit.

The mark is the one the macOS GitHalls uses (~/XProjects/GitHalls): a branch
splitting off a trunk, white on a blue-to-violet plate. The geometry below was
measured off that icon's 1024px render, but it is redrawn rather than resized:
the trunk is 4.9% of the canvas, and downsampling a 1024px PNG to 16px turns
that into a grey smudge.

Two things differ from macOS on purpose:
  - the plate is a Windows 11 round-rect (18% radius), not a macOS squircle (~22%)
  - below 48px the mark is drawn heavier, because a stroke that thin disappears

    python3 tools/make-icons.py

Needs Pillow. Writes only into GitHalls.App/Assets.
"""

from __future__ import annotations

import struct
from pathlib import Path

from PIL import Image, ImageDraw

ASSETS = Path(__file__).resolve().parent.parent / "GitHalls.App" / "Assets"

# MARK: - Brand

# Sampled from the macOS icon_512x512.png at (60,60) and (452,452), then
# extrapolated out to the true corners of the 512px canvas.
BRAND_START = (0x4A, 0x8C, 0xFF)
BRAND_END = (0x7E, 0x3C, 0xEC)

PLATE_RADIUS = 0.18  # Windows 11 round-rect. macOS uses ~0.22 with a squircle.

# MARK: - Mark geometry
#
# Normalized to the plate. From the 1024px macOS render: trunk x=380, nodes at
# y=280 and y=750 with r=60, branch node at (680,600), stroke width 50.

TRUNK_X = 380 / 1024
NODE_TOP_Y = 280 / 1024
NODE_BOTTOM_Y = 750 / 1024
BRANCH_NODE = (680 / 1024, 600 / 1024)
NODE_R = 60 / 1024
STROKE_W = 50 / 1024

# The branch leaves the trunk heading down-right and arrives at the node
# heading right: a cubic whose last control point sits level with the node.
BRANCH_CURVE = [
    (380 / 1024, 450 / 1024),
    (490 / 1024, 514 / 1024),
    (600 / 1024, 600 / 1024),
    (680 / 1024, 600 / 1024),
]

SS = 4  # supersampling; the plate corners and the node edges need it


def weight_for(size: int) -> tuple[float, float]:
    """Stroke and node multipliers. A 4.9% trunk is 0.8px at 16px: invisible."""
    if size >= 48:
        return 1.0, 1.0
    if size >= 24:
        return 1.5, 1.15
    return 1.9, 1.3


# MARK: - Drawing


def _bezier(points, steps=96):
    (x0, y0), (x1, y1), (x2, y2), (x3, y3) = points
    out = []
    for i in range(steps + 1):
        t = i / steps
        u = 1 - t
        x = u**3 * x0 + 3 * u**2 * t * x1 + 3 * u * t**2 * x2 + t**3 * x3
        y = u**3 * y0 + 3 * u**2 * t * y1 + 3 * u * t**2 * y2 + t**3 * y3
        out.append((x, y))
    return out


def _gradient(size: int) -> Image.Image:
    """135 degrees, start at the top-left corner. Built small and scaled up:
    a linear ramp survives interpolation exactly, and 4096x4096 in a Python
    loop does not finish."""
    n = 256
    ramp = Image.new("RGB", (n, n))
    px = ramp.load()
    for y in range(n):
        for x in range(n):
            t = (x + y) / (2 * (n - 1))
            px[x, y] = tuple(
                round(BRAND_START[c] + (BRAND_END[c] - BRAND_START[c]) * t)
                for c in range(3)
            )
    return ramp.resize((size, size), Image.Resampling.BICUBIC)


def _mark_mask(w: int, h: int, plate: float, ox: float, oy: float,
               stroke_mul: float, node_mul: float) -> Image.Image:
    """The branch, as an alpha mask. Drawn at SS and returned at SS."""
    mask = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(mask)

    sw = max(1, round(STROKE_W * plate * stroke_mul))
    r = NODE_R * plate * node_mul

    def pt(nx, ny):
        return (ox + nx * plate, oy + ny * plate)

    trunk_top = pt(TRUNK_X, NODE_TOP_Y)
    trunk_bottom = pt(TRUNK_X, NODE_BOTTOM_Y)
    d.line([trunk_top, trunk_bottom], fill=255, width=sw)

    # Stamped, not stroked: PIL's joint="curve" leaves hairline notches
    # between segments, and they survive the downsample as a dotted edge.
    br = sw / 2
    for cx, cy in _bezier(BRANCH_CURVE, steps=max(96, int(plate / 2))):
        x, y = ox + cx * plate, oy + cy * plate
        d.ellipse([x - br, y - br, x + br, y + br], fill=255)

    for cx, cy in (trunk_top, trunk_bottom, pt(*BRANCH_NODE)):
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=255)

    return mask


def render(w: int, h: int, plate_fraction: float = 1.0,
           plated: bool = True, weight_size: int | None = None) -> Image.Image:
    """One asset. `plate_fraction` insets the plate inside the canvas, which is
    what the tiles want; `plated=False` gives the white mark alone."""
    stroke_mul, node_mul = weight_for(weight_size if weight_size else min(w, h))

    W, H = w * SS, h * SS
    plate = min(W, H) * plate_fraction
    ox, oy = (W - plate) / 2, (H - plate) / 2

    out = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    mark = _mark_mask(W, H, plate, ox, oy, stroke_mul, node_mul)

    if plated:
        shape = Image.new("L", (W, H), 0)
        ImageDraw.Draw(shape).rounded_rectangle(
            [ox, oy, ox + plate - 1, oy + plate - 1],
            radius=plate * PLATE_RADIUS, fill=255,
        )
        # The ramp spans the plate, not the canvas: on an inset tile or the
        # wide logo, a canvas-wide ramp would leave the plate showing only the
        # middle of the gradient, and the icon would lose its blue and violet.
        plate_img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        plate_img.paste(_gradient(round(plate)), (round(ox), round(oy)))
        plate_img.putalpha(shape)
        out = Image.alpha_composite(out, plate_img)
        white = Image.new("RGBA", (W, H), (255, 255, 255, 0))
        white.putalpha(mark)
        out = Image.alpha_composite(out, white)
    else:
        out = Image.new("RGBA", (W, H), (255, 255, 255, 0))
        out.putalpha(mark)

    return out.resize((w, h), Image.Resampling.LANCZOS)


# MARK: - ICO
#
# Written by hand rather than through Pillow's ICO save, which resamples one
# image down to every size and so loses the heavier small-size geometry.


def write_ico(path: Path, sizes: list[int]) -> None:
    import io

    images = []
    for s in sizes:
        buf = io.BytesIO()
        render(s, s).save(buf, format="PNG")
        images.append(buf.getvalue())

    out = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    for s, blob in zip(sizes, images):
        out += struct.pack("<BBBBHHII", s if s < 256 else 0, s if s < 256 else 0,
                           0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
    path.write_bytes(out + b"".join(images))


# MARK: - Assets

SCALES = {"scale-100": 1.0, "scale-125": 1.25, "scale-150": 1.5,
          "scale-200": 2.0, "scale-400": 4.0}


def main() -> None:
    ASSETS.mkdir(parents=True, exist_ok=True)
    written = []

    def save(img: Image.Image, name: str) -> None:
        img.save(ASSETS / name)
        written.append(f"{name}  {img.width}x{img.height}")

    # App list, taskbar, Start: the plate fills the asset.
    for tag, mul in SCALES.items():
        s = round(44 * mul)
        save(render(s, s), f"Square44x44Logo.{tag}.png")
    for s in (16, 24, 32, 48, 256):
        img = render(s, s)
        save(img, f"Square44x44Logo.targetsize-{s}.png")
        save(img, f"Square44x44Logo.targetsize-{s}_altform-unplated.png")

    # Tiles: the plate is centred at 66%, the rest transparent, so Windows can
    # put its own tile colour behind it.
    for base, mul_map in ((71, SCALES), (150, SCALES), (310, SCALES)):
        for tag, mul in mul_map.items():
            s = round(base * mul)
            save(render(s, s, plate_fraction=0.66, weight_size=round(s * 0.66)),
                 f"Square{base}x{base}Logo.{tag}.png")

    for tag, mul in (("scale-100", 1.0), ("scale-200", 2.0)):
        w, h = round(310 * mul), round(150 * mul)
        save(render(w, h, plate_fraction=0.66, weight_size=round(h * 0.66)),
             f"Wide310x150Logo.{tag}.png")

    for tag, mul in SCALES.items():
        w, h = round(620 * mul), round(300 * mul)
        save(render(w, h, plate_fraction=0.60, weight_size=round(h * 0.60)),
             f"SplashScreen.{tag}.png")

    for tag, mul in (("", 1.0), (".scale-200", 2.0)):
        s = round(50 * mul)
        save(render(s, s), f"StoreLogo{tag}.png")

    # The lock screen badge has to be a white silhouette on transparent; a
    # gradient here renders as a grey block.
    save(render(48, 48, plated=False), "LockScreenLogo.scale-200.png")

    write_ico(ASSETS / "GitHalls.ico", [16, 20, 24, 32, 48, 64, 128, 256])
    written.append("GitHalls.ico  16/20/24/32/48/64/128/256")

    for line in written:
        print(line)
    print(f"\n{len(written)} assets -> {ASSETS}")


if __name__ == "__main__":
    main()
