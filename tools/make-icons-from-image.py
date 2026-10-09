#!/usr/bin/env python3
"""
Generates Windows icon assets in GitHalls.App/Assets from a generated master image.
Takes a square master image, masks it with Windows 11 squircle curvature,
and outputs all required scale variants, tile assets, and GitHalls.ico.
"""

from __future__ import annotations

import argparse
import io
import struct
import sys
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "GitHalls.App" / "Assets"

# Windows 11 rounded-rect radius fraction (18%)
PLATE_RADIUS = 0.18

SCALES = {
    "scale-100": 1.0,
    "scale-125": 1.25,
    "scale-150": 1.5,
    "scale-200": 2.0,
    "scale-400": 4.0,
}


def create_plated_master(src_img: Image.Image, crop_box: tuple[int, int, int, int] | None = None) -> Image.Image:
    """Crops the central plaque from the master image and applies a supersampled squircle alpha mask."""
    if crop_box:
        img = src_img.crop(crop_box)
    else:
        img = src_img

    w, h = img.size
    side = min(w, h)
    img = img.crop(((w - side) // 2, (h - side) // 2, (w + side) // 2, (h + side) // 2)).convert("RGBA")

    # Supersampled rounded rectangle mask (SS=4)
    ss = 4
    mask = Image.new("L", (side * ss, side * ss), 0)
    d = ImageDraw.Draw(mask)
    d.rounded_rectangle(
        [0, 0, side * ss - 1, side * ss - 1],
        radius=round(side * ss * PLATE_RADIUS),
        fill=255,
    )
    mask = mask.resize((side, side), Image.Resampling.LANCZOS)
    img.putalpha(mask)
    return img


def render_asset(master: Image.Image, w: int, h: int, plate_fraction: float = 1.0) -> Image.Image:
    """Renders one asset canvas of size (w, h), with the plate sized to plate_fraction."""
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    plate_size = max(1, round(min(w, h) * plate_fraction))
    resized_plate = master.resize((plate_size, plate_size), Image.Resampling.LANCZOS)
    ox = (w - plate_size) // 2
    oy = (h - plate_size) // 2
    out.paste(resized_plate, (ox, oy), resized_plate)
    return out


def write_ico(images_by_size: dict[int, Image.Image], path: Path) -> None:
    """Writes a standard Windows .ico file containing all specified square sizes."""
    sizes = sorted(images_by_size.keys())
    blobs = []
    for s in sizes:
        buf = io.BytesIO()
        images_by_size[s].save(buf, format="PNG")
        blobs.append((s, buf.getvalue()))

    header = struct.pack("<HHH", 0, 1, len(blobs))
    offset = 6 + 16 * len(blobs)
    entries = []
    for s, blob in blobs:
        b_w = s if s < 256 else 0
        b_h = s if s < 256 else 0
        entries.append(struct.pack("<BBBBHHII", b_w, b_h, 0, 0, 1, 32, len(blob), offset))
        offset += len(blob)

    path.write_bytes(header + b"".join(entries) + b"".join(b for _, b in blobs))


def process_image(src_path: Path, output_dir: Path) -> list[str]:
    output_dir.mkdir(parents=True, exist_ok=True)
    src_img = Image.open(src_path)
    w, h = src_img.size

    # In our 2048x2048 generated image, the plaque is located at (340, 340, 1708, 1708)
    if w == 2048 and h == 2048:
        crop_box = (340, 340, 1708, 1708)
    elif w == 1024 and h == 1024:
        crop_box = (170, 170, 854, 854)
    else:
        crop_box = None

    master = create_plated_master(src_img, crop_box)
    written = []

    def save(img: Image.Image, name: str) -> None:
        p = output_dir / name
        img.save(p)
        written.append(f"{name} ({img.width}x{img.height})")

    # 1. App list, Taskbar, Start Menu (Square44x44Logo)
    for tag, mul in SCALES.items():
        s = round(44 * mul)
        save(render_asset(master, s, s, plate_fraction=1.0), f"Square44x44Logo.{tag}.png")

    for s in (16, 24, 32, 48, 256):
        img = render_asset(master, s, s, plate_fraction=1.0)
        save(img, f"Square44x44Logo.targetsize-{s}.png")
        save(img, f"Square44x44Logo.targetsize-{s}_altform-unplated.png")

    # 2. Windows Tiles (Centered plate with padding)
    for base, mul_map in ((71, SCALES), (150, SCALES), (310, SCALES)):
        for tag, mul in mul_map.items():
            s = round(base * mul)
            save(render_asset(master, s, s, plate_fraction=0.70), f"Square{base}x{base}Logo.{tag}.png")

    # 3. Wide Tiles
    for tag, mul in (("scale-100", 1.0), ("scale-200", 2.0)):
        w_tile, h_tile = round(310 * mul), round(150 * mul)
        save(render_asset(master, w_tile, h_tile, plate_fraction=0.70), f"Wide310x150Logo.{tag}.png")

    # 4. Splash Screen
    for tag, mul in SCALES.items():
        w_splash, h_splash = round(620 * mul), round(300 * mul)
        save(render_asset(master, w_splash, h_splash, plate_fraction=0.60), f"SplashScreen.{tag}.png")

    # 5. Store Logos
    for tag, mul in (("", 1.0), (".scale-200", 2.0)):
        s = round(50 * mul)
        save(render_asset(master, s, s, plate_fraction=1.0), f"StoreLogo{tag}.png")

    # 6. Windows Executable Multi-resolution ICO
    ico_sizes = [16, 20, 24, 32, 48, 64, 128, 256]
    ico_dict = {s: render_asset(master, s, s, plate_fraction=1.0) for s in ico_sizes}
    write_ico(ico_dict, output_dir / "GitHalls.ico")
    written.append(f"GitHalls.ico (sizes: {', '.join(str(s) for s in ico_sizes)})")

    return written


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate Windows icon assets from an image.")
    parser.add_argument("image", nargs="?", default=r"C:\Users\Lucas\Downloads\Generated Image October 07, 2026 - 11_51PM.jpg",
                        help="Path to source master image")
    parser.add_argument("--out", default=str(ASSETS), help="Output directory for assets")
    args = parser.parse_args()

    src_path = Path(args.image)
    if not src_path.exists():
        print(f"Error: image not found at {src_path}", file=sys.stderr)
        sys.exit(1)

    out_dir = Path(args.out)
    print(f"Processing {src_path} -> {out_dir} ...")
    written = process_image(src_path, out_dir)
    print(f"Successfully generated {len(written)} assets:")
    for item in written:
        print(f"  + {item}")


if __name__ == "__main__":
    main()
