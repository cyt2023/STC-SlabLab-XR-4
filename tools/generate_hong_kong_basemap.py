"""Cache and compose the small OSM tile set used by the Unity Hong Kong view."""

from __future__ import annotations

import io
import math
from pathlib import Path

import httpx
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "RenderingModule" / "Assets" / "Resources" / "HongKongOSM.png"
CACHE = ROOT / ".runtime" / "osm-hong-kong-z10"
ZOOM = 10
LON_MIN, LON_MAX = 113.650220, 114.659994
LAT_MIN, LAT_MAX = 22.0300455, 22.6998139
TILE_SIZE = 256


def tile_x(lon: float) -> float:
    return (lon + 180.0) / 360.0 * (1 << ZOOM)


def tile_y(lat: float) -> float:
    latitude = math.radians(lat)
    return (1.0 - math.asinh(math.tan(latitude)) / math.pi) / 2.0 * (1 << ZOOM)


def main() -> None:
    CACHE.mkdir(parents=True, exist_ok=True)
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    left, right = tile_x(LON_MIN), tile_x(LON_MAX)
    top, bottom = tile_y(LAT_MAX), tile_y(LAT_MIN)
    x0, x1 = math.floor(left), math.floor(right)
    y0, y1 = math.floor(top), math.floor(bottom)
    canvas = Image.new("RGB", ((x1 - x0 + 1) * TILE_SIZE, (y1 - y0 + 1) * TILE_SIZE))
    headers = {"User-Agent": "STC-SlabLab-XR/1.0 (local research visualization)"}
    with httpx.Client(timeout=30, headers=headers, follow_redirects=True) as client:
        for x in range(x0, x1 + 1):
            for y in range(y0, y1 + 1):
                cached = CACHE / f"{ZOOM}-{x}-{y}.png"
                if not cached.exists():
                    response = client.get(f"https://tile.openstreetmap.org/{ZOOM}/{x}/{y}.png")
                    response.raise_for_status()
                    cached.write_bytes(response.content)
                tile = Image.open(io.BytesIO(cached.read_bytes())).convert("RGB")
                canvas.paste(tile, ((x - x0) * TILE_SIZE, (y - y0) * TILE_SIZE))

    crop = (
        round((left - x0) * TILE_SIZE),
        round((top - y0) * TILE_SIZE),
        round((right - x0) * TILE_SIZE),
        round((bottom - y0) * TILE_SIZE),
    )
    result = canvas.crop(crop).resize((1024, 680), Image.Resampling.LANCZOS)
    draw = ImageDraw.Draw(result, "RGBA")
    draw.rounded_rectangle((660, 646, 1014, 674), radius=7, fill=(255, 255, 255, 220))
    draw.text((674, 653), "© OpenStreetMap contributors", fill=(30, 45, 55, 255))
    result.save(OUTPUT, optimize=True)
    print(f"Wrote {OUTPUT}")


if __name__ == "__main__":
    main()
