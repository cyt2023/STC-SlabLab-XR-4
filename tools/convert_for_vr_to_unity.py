"""Convert the For_VR unstructured HDF5 fields into Unity XYZ+T RAW datasets."""

from __future__ import annotations

import json
from pathlib import Path

import h5py
import matplotlib.tri as mtri
import numpy as np


ROOT = Path(__file__).resolve().parents[1]
SOURCE_ROOT = ROOT / "For_VR"
OUTPUT_ROOT = SOURCE_ROOT / "UnityRaw"
GRID_X, GRID_Y, GRID_Z = 96, 64, 4
CHANNELS = (("HS", 0), ("Water_Level", 1))
SOURCES = (("Prediction", "prediction"), ("GroundTruth", "ground_truth"))


def source_files(folder: str) -> list[Path]:
    return sorted((SOURCE_ROOT / folder).glob("event_*.h5"))


def build_spatial_mapping(first_file: Path):
    with h5py.File(first_file, "r") as handle:
        lon = handle["lon"][:]
        lat = handle["lat"][:]
        faces = handle["faces_0based"][:].astype(np.int64)

    grid_lon = np.linspace(float(lon.min()), float(lon.max()), GRID_X)
    grid_lat = np.linspace(float(lat.min()), float(lat.max()), GRID_Y)
    mesh_lon, mesh_lat = np.meshgrid(grid_lon, grid_lat)
    triangulation = mtri.Triangulation(lon, lat, triangles=faces)
    triangle_index = triangulation.get_trifinder()(mesh_lon, mesh_lat).reshape(-1)
    valid = triangle_index >= 0
    vertices = np.zeros((triangle_index.size, 3), dtype=np.int64)
    weights = np.zeros((triangle_index.size, 3), dtype=np.float64)
    vertices[valid] = faces[triangle_index[valid]]

    px = mesh_lon.reshape(-1)[valid]
    py = mesh_lat.reshape(-1)[valid]
    selected = vertices[valid]
    x1, x2, x3 = lon[selected[:, 0]], lon[selected[:, 1]], lon[selected[:, 2]]
    y1, y2, y3 = lat[selected[:, 0]], lat[selected[:, 1]], lat[selected[:, 2]]
    denominator = (y2 - y3) * (x1 - x3) + (x3 - x2) * (y1 - y3)
    w1 = ((y2 - y3) * (px - x3) + (x3 - x2) * (py - y3)) / denominator
    w2 = ((y3 - y1) * (px - x3) + (x1 - x3) * (py - y3)) / denominator
    weights[valid, 0] = w1
    weights[valid, 1] = w2
    weights[valid, 2] = 1.0 - w1 - w2
    return lon, lat, valid, vertices, weights


def value_range(files: list[Path], dataset_key: str, channel_index: int) -> tuple[float, float]:
    minimum, maximum = np.inf, -np.inf
    for path in files:
        with h5py.File(path, "r") as handle:
            values = handle[dataset_key][:, :, channel_index]
            minimum = min(minimum, float(np.nanmin(values)))
            maximum = max(maximum, float(np.nanmax(values)))
    return minimum, maximum


def encode_frame(values, valid, vertices, weights, minimum, maximum) -> np.ndarray:
    raster = np.zeros(valid.size, dtype=np.float64)
    raster[valid] = np.sum(values[vertices[valid]] * weights[valid], axis=1)
    encoded = np.zeros(valid.size, dtype=np.uint8)
    scale = 254.0 / max(maximum - minimum, np.finfo(float).eps)
    encoded[valid] = np.clip(np.rint((raster[valid] - minimum) * scale + 1.0), 1, 255)
    surface = encoded.reshape(GRID_Y, GRID_X)
    return np.repeat(surface[np.newaxis, :, :], GRID_Z, axis=0)


def main() -> None:
    first_file = source_files("Prediction")[0]
    lon, lat, valid, vertices, weights = build_spatial_mapping(first_file)
    manifest = {
        "schemaVersion": "1.0",
        "source": "For_VR HDF5",
        "grid": {"x": GRID_X, "y": GRID_Y, "z": GRID_Z},
        "boundsEPSG4326": {
            "lonMin": float(lon.min()), "lonMax": float(lon.max()),
            "latMin": float(lat.min()), "latMax": float(lat.max()),
        },
        "zSemantics": "The 2D surface field is repeated across four layers for volume rendering.",
        "geographicSurface": {
            "coordinateReference": "EPSG:4326",
            "projection": "WebMercator",
            "longitudeOrigin": "Greenwich",
            "wrapPolicy": "none; fixed Hong Kong extent",
            "nodeCount": int(lon.size),
            "faceCount": 0,
            "coordinateFile": "GeoSurface/lon_lat_f32.bin",
            "faceFile": "GeoSurface/faces_u32.bin",
            "coordinateEncoding": "interleaved little-endian float32 longitude,latitude",
            "faceEncoding": "little-endian uint32 triplets",
        },
        "datasets": [],
    }
    with h5py.File(first_file, "r") as handle:
        faces = handle["faces_0based"][:].astype("<u4")
    manifest["geographicSurface"]["faceCount"] = int(faces.shape[0])
    geographic_root = OUTPUT_ROOT / "GeoSurface"
    geographic_root.mkdir(parents=True, exist_ok=True)
    np.column_stack((lon, lat)).astype("<f4").tofile(
        geographic_root / "lon_lat_f32.bin")
    faces.tofile(geographic_root / "faces_u32.bin")
    s4d_variables = {}
    OUTPUT_ROOT.mkdir(parents=True, exist_ok=True)

    for source_folder, dataset_key in SOURCES:
        files = source_files(source_folder)
        for channel_name, channel_index in CHANNELS:
            output_dir = OUTPUT_ROOT / f"{source_folder}_{channel_name}"
            output_dir.mkdir(parents=True, exist_ok=True)
            minimum, maximum = value_range(files, dataset_key, channel_index)
            frame_index = 0
            timestamps = []
            geographic_values_path = geographic_root / f"{output_dir.name}_nodes_f32.bin"
            with geographic_values_path.open("wb") as geographic_values:
                for event_index, path in enumerate(files, start=1):
                    with h5py.File(path, "r") as handle:
                        fields = handle[dataset_key]
                        times_hkt = handle["time_hkt"][:]
                        for local_index in range(fields.shape[0]):
                            node_values = fields[local_index, :, channel_index]
                            volume = encode_frame(
                                node_values, valid, vertices, weights,
                                minimum, maximum)
                            name = f"event_{event_index:02d}_time_{frame_index:04d}.raw"
                            (output_dir / name).write_bytes(volume.tobytes(order="C"))
                            (output_dir / f"{name}.ini").write_text(
                                f"dimx:{GRID_X}\ndimy:{GRID_Y}\ndimz:{GRID_Z}\n"
                                "skip:0\nformat:uint8\nendianness:littleendian\n",
                                encoding="utf-8",
                            )
                            geographic_values.write(
                                np.asarray(node_values, dtype="<f4").tobytes())
                            timestamp = times_hkt[local_index]
                            timestamps.append(timestamp.decode() if isinstance(timestamp, bytes) else str(timestamp))
                            frame_index += 1
            manifest["datasets"].append({
                "name": output_dir.name,
                "sourceField": dataset_key,
                "channel": channel_name,
                "unit": "m",
                "physicalMinimum": minimum,
                "physicalMaximum": maximum,
                "encoding": "0=outside mesh; 1..255 maps linearly to physical range",
                "frameCount": frame_index,
                "geographicValuesFile": f"GeoSurface/{output_dir.name}_nodes_f32.bin",
                "geographicValuesEncoding": "little-endian float32 physical values",
                "geographicFrameStrideBytes": int(lon.size * 4),
                "timeHKT": timestamps,
            })
            scale = (maximum - minimum) / 254.0
            raw_files = sorted(output_dir.glob("*.raw"))
            s4d_variables[output_dir.name] = {
                "displayName": output_dir.name,
                "unit": "m",
                "valueSemantics": "physical",
                "voxelType": "uint8",
                "scale": scale,
                "offset": minimum - scale,
                "missingRawValues": [0],
                "frames": [
                    {
                        "frameId": f"{output_dir.name}_t{index:04d}",
                        "timeIndex": index,
                        "temporalMeaning": "instantaneous",
                        "path": f"../../For_VR/UnityRaw/{output_dir.name}/{path.name}",
                        "expectedBytes": GRID_X * GRID_Y * GRID_Z,
                        "sha256": None,
                    }
                    for index, path in enumerate(raw_files)
                ],
            }
            print(f"{output_dir.name}: {frame_index} frames")

    (OUTPUT_ROOT / "conversion_manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    s4d_manifest = {
        "schemaVersion": "1.0",
        "datasetId": "for_vr_hong_kong_events_v1",
        "datasetVersion": "for-vr-2026-08-14",
        "dimensions": {"x": GRID_X, "y": GRID_Y, "z": GRID_Z},
        "storageOrder": "ZYX",
        "defaultVoxelType": "uint8",
        "coordinates": {
            "coordinateReference": "EPSG:4326",
            "renderProjection": "WebMercator",
            "x": {
                "kind": "regular_geographic_grid", "axis": "longitude",
                "unit": "degree_east", "start": float(lon.min()),
                "step": float((lon.max() - lon.min()) / (GRID_X - 1)),
            },
            "y": {
                "kind": "regular_geographic_grid", "axis": "latitude",
                "unit": "degree_north", "start": float(lat.min()),
                "step": float((lat.max() - lat.min()) / (GRID_Y - 1)),
            },
            "depth": {
                "kind": "ordinal_index", "unit": "display_extrusion_layer",
                "start": 0, "step": 1, "positive": "down", "excludedIndices": [],
            },
        },
        "variables": s4d_variables,
        "assumptions": [
            {
                "id": "mesh-rasterization",
                "statement": "The supplied triangular Hong Kong mesh is linearly rasterized to a 96 by 64 longitude-latitude grid.",
                "evidence": "Interpolation uses faces_0based and preserves the supplied mesh boundary; raw zero marks cells outside it.",
                "status": "measured",
            },
            {
                "id": "exact-geographic-surface",
                "statement": "The primary Unity surface uses all supplied EPSG:4326 nodes and faces without spatial interpolation.",
                "evidence": "GeoSurface stores 7,364 interleaved lon/lat nodes, 13,445 source faces and one encoded value per original node and hour.",
                "status": "measured",
            },
            {
                "id": "surface-extrusion",
                "statement": "Each physical 2D surface is repeated across four display layers; these layers are not physical depth.",
                "evidence": "For_VR supplies time, node and channel dimensions but no depth dimension.",
                "status": "measured",
            },
            {
                "id": "event-time-order",
                "statement": "Frames are ordered by event number and then hourly HKT timestamp; gaps between events remain discontinuities.",
                "evidence": "The six source HDF5 files provide time_hkt arrays and paired filenames.",
                "status": "measured",
            },
        ],
    }
    s4d_path = ROOT / "datasets" / "for_vr" / "manifest.json"
    s4d_path.parent.mkdir(parents=True, exist_ok=True)
    s4d_path.write_text(json.dumps(s4d_manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Converted data written to {OUTPUT_ROOT}")


if __name__ == "__main__":
    main()
