from __future__ import annotations

import struct
import zlib

import numpy as np

from .raw_reader import GridNumericResult


_VIRIDIS_STOPS = np.asarray(
    [
        [68, 1, 84],
        [59, 82, 139],
        [33, 145, 140],
        [94, 201, 98],
        [253, 231, 37],
    ],
    dtype=np.float32,
)


def render_preview_atlas(
    result: GridNumericResult,
    column_count: int,
    row_count: int,
) -> bytes:
    """Render aggregated cells into a transparent, shared-scale PNG atlas."""
    if column_count <= 0 or row_count <= 0:
        raise ValueError("preview atlas dimensions must be positive")
    if len(result.cells) != column_count * row_count:
        raise ValueError("preview cell count does not match the requested grid")

    height, width = result.cells[0].values.shape
    atlas = np.zeros((height * row_count, width * column_count, 4), dtype=np.uint8)
    value_range = max(1e-12, result.shared_maximum - result.shared_minimum)

    for index, cell in enumerate(result.cells):
        values = cell.values
        if values.shape != (height, width):
            raise ValueError("all preview cells must share one XY shape")
        finite = np.isfinite(values)
        normalized = np.clip(
            (values - result.shared_minimum) / value_range, 0.0, 1.0
        )
        normalized = np.where(finite, normalized, 0.0)
        scaled = normalized * (_VIRIDIS_STOPS.shape[0] - 1)
        lower = np.floor(scaled).astype(np.int32)
        upper = np.minimum(lower + 1, _VIRIDIS_STOPS.shape[0] - 1)
        fraction = (scaled - lower)[..., None]
        rgb = (
            _VIRIDIS_STOPS[lower] * (1.0 - fraction)
            + _VIRIDIS_STOPS[upper] * fraction
        ).astype(np.uint8)
        rgba = np.zeros((height, width, 4), dtype=np.uint8)
        rgba[..., :3] = rgb
        rgba[..., 3] = np.where(finite, 255, 0).astype(np.uint8)

        row = index // column_count
        column = index % column_count
        y0 = row * height
        x0 = column * width
        atlas[y0 : y0 + height, x0 : x0 + width] = rgba

    return _encode_rgba_png(atlas)


def _encode_rgba_png(pixels: np.ndarray) -> bytes:
    height, width, channels = pixels.shape
    if channels != 4 or pixels.dtype != np.uint8:
        raise ValueError("PNG input must be uint8 RGBA")
    scanlines = b"".join(
        b"\x00" + pixels[row].tobytes() for row in range(height)
    )

    def chunk(kind: bytes, data: bytes) -> bytes:
        payload = kind + data
        return (
            struct.pack(">I", len(data))
            + payload
            + struct.pack(">I", zlib.crc32(payload) & 0xFFFFFFFF)
        )

    return (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
        + chunk(b"IDAT", zlib.compress(scanlines, level=6))
        + chunk(b"IEND", b"")
    )
