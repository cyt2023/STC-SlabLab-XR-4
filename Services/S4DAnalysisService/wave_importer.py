from __future__ import annotations

import json
import math
import os
import shutil
import time
import uuid
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Callable

import httpx
import numpy as np


WAVE_API_URL = os.getenv(
    "WAVE_API_URL", "https://zxcpu6-wave.tail6e1034.ts.net"
).rstrip("/")
# Hybrid data source: the live gateway is preferred, a bundled hydrated dataset
# is the fallback so a packaged app still opens without the tailnet. The mode is
# reported by /health so an operator can see which one answered.
WAVE_MODE = "unknown"
S4D_OFFLINE = os.getenv("S4D_OFFLINE", "").strip().lower() in {
    "1", "true", "yes", "on",
}


def wave_mode() -> str:
    """live | cache | offline — what the last resolution used."""
    return WAVE_MODE


def _redact(text: str, secret: str) -> str:
    """
    Keep the Wave key out of anything the app or a log can read back.

    Upstream error bodies are quoted straight into our own messages, and a
    gateway that echoes the request would echo the key with it. The key ships
    inside the packaged app, so it must never be able to travel back out through
    a status payload or an exception string.
    """
    if secret and secret in text:
        return text.replace(secret, "<redacted>")
    return text


GRID_X, GRID_Y, GRID_Z = 96, 64, 4
FIELD_METADATA = {
    "hs": ("Wave_HS", "HS", "source unit (unverified)"),
    "elev": ("Wave_Water_Level", "Water_Level", "source unit (unverified)"),
}
FIELD_DISPLAY_RANGES = {"hs": (0.0, 5.0), "elev": (-3.0, 3.0)}


class WaveImportError(RuntimeError):
    pass


def parse_utc_hour(value: str) -> datetime:
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as exc:
        raise WaveImportError(f"Invalid UTC timestamp: {value}") from exc
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=timezone.utc)
    parsed = parsed.astimezone(timezone.utc)
    if parsed.minute or parsed.second or parsed.microsecond:
        raise WaveImportError("Wave imports must start and end on an exact UTC hour.")
    return parsed


def _iso_utc(value: datetime) -> str:
    return value.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def _chunks(values: np.ndarray, size: int):
    for start in range(0, len(values), size):
        yield values[start : start + size]


def _time_chunks(start: datetime, end: datetime, node_count: int):
    max_hours = min(31 * 24, max(1, 50_000 // max(1, node_count)))
    cursor = start
    while cursor < end:
        next_cursor = min(end, cursor + timedelta(hours=max_hours))
        yield cursor, next_cursor
        cursor = next_cursor


def _retry_delay(response: httpx.Response | None, attempt: int) -> float:
    """Honour the gateway's retry hint, else back off 5s, 20s, 45s."""
    if response is not None:
        header = response.headers.get("Retry-After", "").strip()
        if header.isdigit():
            return min(float(header), 90.0)
        text = response.text[:200].lower()
        if "retry in one minute" in text or "one minute" in text:
            return 62.0
    return (5.0, 20.0, 45.0, 60.0)[min(attempt, 3)]


class WaveDatasetImporter:
    def __init__(
        self,
        workspace_root: Path,
        import_root: Path,
        *,
        cache_root: Path | None = None,
        transport: httpx.BaseTransport | None = None,
    ):
        self.workspace_root = workspace_root.resolve()
        self.import_root = import_root.resolve()
        configured_cache = cache_root or os.getenv("S4D_WAVE_CACHE")
        self.cache_root = Path(
            configured_cache
            if configured_cache
            else self.workspace_root / "For_VR" / "WaveCache"
        ).resolve()
        self.transport = transport
        self.static_root = self.workspace_root / "For_VR" / "UnityRaw" / "GeoSurface"

    # ---------------------------------------------------------------- cache
    def live_roots(self) -> list[Path]:
        """Hydrated dataset stores, already-registered ones first."""
        roots: list[Path] = []
        seen: set[str] = set()
        for root in (self.import_root, self.cache_root):
            if not root.is_dir():
                continue
            for path in sorted(
                (item for item in root.iterdir() if item.is_dir()),
                key=lambda item: item.name,
            ):
                # The seeded copy and the bundled original are the same window;
                # report it once.
                if path.name in seen:
                    continue
                seen.add(path.name)
                roots.append(path)
        return roots

    def _find_covering(
        self, start: datetime, end: datetime, fields: list[str],
    ) -> Path | None:
        """A hydrated dataset that already contains the requested window."""
        wanted = {field.lower() for field in fields}
        for root in self.live_roots():
            if not (root / ".hydrated").is_file():
                continue
            identity = self._live_identity(root)
            if identity is None:
                continue
            cached_start, cached_end, cached_fields = identity
            if (
                cached_start <= start
                and start < cached_end
                and cached_start < end
                and end <= cached_end
                and wanted <= set(cached_fields)
            ):
                return root
        return None

    def bundled_windows(self) -> list[dict[str, object]]:
        """What the bundled cache can serve, for /health and diagnostics."""
        result = []
        for root in self.live_roots():
            if not (root / ".hydrated").is_file():
                continue
            identity = self._live_identity(root)
            if identity is None:
                continue
            start, end, fields = identity
            result.append(
                {
                    "name": root.name,
                    "start": _iso_utc(start),
                    "end": _iso_utc(end),
                    "fields": list(fields),
                }
            )
        return result

    def _seed_from_cache(self, source: Path, dataset_id: str) -> Path:
        """Register a bundled dataset into the live store the app reads."""
        target = self.import_root / dataset_id
        if target.is_dir():
            return target
        self.import_root.mkdir(parents=True, exist_ok=True)
        temporary = self.import_root / ("." + dataset_id + "-" + uuid.uuid4().hex)
        shutil.copytree(source, temporary)
        temporary.replace(target)
        return target

    def _offline_detail(self, field: str, start: datetime) -> str:
        """Say what is missing instead of a bare "key missing"."""
        windows = ", ".join(
            "%s..%s" % (entry["start"], entry["end"])
            for entry in self.bundled_windows()
        )
        detail = (
            "Wave API key is missing and no bundled dataset covers "
            + _iso_utc(start) + " (field " + field + ")."
        )
        if windows:
            detail += " Bundled windows: " + windows + "."
        else:
            detail += " No bundled datasets found in " + str(self.cache_root) + "."
        return detail

    def _local_items(
        self, root: Path, field: str, node_ids: list[int],
        start: datetime, hours: int,
    ) -> list[dict[str, object]] | None:
        """Read one node/time patch from a hydrated dataset, no gateway needed."""
        directory_name = FIELD_METADATA[field][0]
        geo_root = root / "UnityRaw" / "GeoSurface"
        values_path = geo_root / f"{directory_name}_nodes_f32.bin"
        nodes_path = geo_root / "node_ids_u32.bin"
        if not values_path.is_file() or not nodes_path.is_file():
            return None
        identity = self._live_identity(root)
        if identity is None:
            return None
        cached_start, cached_end, _ = identity
        cached_hours = int((cached_end - cached_start).total_seconds() // 3600)
        nodes = np.fromfile(nodes_path, dtype="<u4")
        values = np.fromfile(values_path, dtype="<f4")
        if nodes.size == 0 or values.size != nodes.size * cached_hours:
            return None
        offset = int((start - cached_start).total_seconds() // 3600)
        if offset < 0 or offset + hours > cached_hours:
            return None
        table = values.reshape(cached_hours, nodes.size)
        lookup = {int(node): index for index, node in enumerate(nodes)}
        items: list[dict[str, object]] = []
        for hour in range(hours):
            row = table[offset + hour]
            for node_id in node_ids:
                index = lookup.get(int(node_id))
                if index is None:
                    continue
                value = float(row[index])
                if not math.isfinite(value):
                    continue          # still a placeholder for this node
                items.append(
                    {"frameIndex": hour, "nodeId": int(node_id), "value": value}
                )
        return items

    def status(self, api_key: str | None = None) -> dict[str, object]:
        global WAVE_MODE
        key = (api_key or os.getenv("WAVE_API_KEY", "")).strip()
        result: dict[str, object] = {
            "configured": bool(key),
            "baseUrl": WAVE_API_URL,
            "meshNodes": self._load_static_arrays()[0].size,
            "supportedFields": list(FIELD_METADATA),
            "mode": WAVE_MODE,
            "offline": S4D_OFFLINE,
            "cacheRoot": str(self.cache_root),
            "bundledWindows": self.bundled_windows(),
        }
        if not key:
            # Without a key the bundled dataset is what will answer, unless
            # nothing is bundled at all.
            WAVE_MODE = "cache" if self.bundled_windows() else "offline"
            result["mode"] = WAVE_MODE
            return result
        try:
            # A readiness probe must answer quickly. Reusing the batch retry
            # policy here made the status call take over a minute when the host
            # was unreachable, which stalled the app's opening screen.
            result["metadata"] = self._get_json(
                "/v1/metadata", key, attempts=1, timeout_seconds=6.0
            )
            result["reachable"] = True
            WAVE_MODE = "live"
        except Exception as exc:
            result["reachable"] = False
            result["error"] = _redact(str(exc), key)[:240]
            WAVE_MODE = "offline"
        result["mode"] = WAVE_MODE
        return result

    def import_dataset(
        self,
        start_text: str,
        end_text: str,
        fields: list[str],
        *,
        api_key: str | None = None,
        progress: Callable[[str], None] | None = None,
    ) -> dict[str, object]:
        key = (api_key or os.getenv("WAVE_API_KEY", "")).strip()
        if not key:
            raise WaveImportError(
                "Wave API key is missing. Set WAVE_API_KEY in the repository .env file."
            )
        start = parse_utc_hour(start_text)
        end = parse_utc_hour(end_text)
        if end <= start:
            raise WaveImportError("end must be later than start")
        hours = int((end - start).total_seconds() // 3600)
        if hours > 31 * 24:
            raise WaveImportError("One import may contain at most 31 days.")
        normalized_fields = list(dict.fromkeys(field.strip().lower() for field in fields))
        unsupported = sorted(set(normalized_fields) - set(FIELD_METADATA))
        if not normalized_fields or unsupported:
            raise WaveImportError(
                "Supported fields are hs and elev. Unsupported: " + ", ".join(unsupported)
            )

        node_ids, valid, vertices, weights = self._load_static_arrays()
        values = {
            field: np.full((hours, node_ids.size), np.nan, dtype=np.float32)
            for field in normalized_fields
        }
        node_lookup = {int(node_id): index for index, node_id in enumerate(node_ids)}
        report = progress or (lambda _: None)
        request_count = 0
        for node_batch in _chunks(node_ids, 100):
            for chunk_start, chunk_end in _time_chunks(start, end, len(node_batch)):
                report(
                    f"Querying nodes {int(node_batch[0])}-{int(node_batch[-1])} "
                    f"from {_iso_utc(chunk_start)}"
                )
                rows = self._get_json(
                    "/v1/wave",
                    key,
                    {
                        "start": _iso_utc(chunk_start),
                        "end": _iso_utc(chunk_end),
                        "node_ids": ",".join(str(int(value)) for value in node_batch),
                        "fields": ",".join(normalized_fields),
                    },
                )
                request_count += 1
                if not isinstance(rows, list):
                    raise WaveImportError("Wave API returned a non-list response.")
                for row in rows:
                    self._store_row(row, start, hours, node_lookup, values)
                # Keep the burst gentle: a full window is one call per
                # 100-node batch and the gateway throttles aggressive clients.
                time.sleep(0.15)

        dataset_id = (
            "wave_" + start.strftime("%Y%m%dT%H%MZ") + "_" +
            end.strftime("%Y%m%dT%H%MZ") + "_" + "_".join(normalized_fields)
        )
        final_root = self.import_root / dataset_id
        temporary_root = self.import_root / ("." + dataset_id + "-" + uuid.uuid4().hex)
        temporary_root.mkdir(parents=True, exist_ok=False)
        try:
            result = self._write_dataset(
                temporary_root, dataset_id, start, end, values,
                node_ids, valid, vertices, weights,
            )
            if final_root.exists():
                shutil.rmtree(final_root)
            temporary_root.replace(final_root)
        except Exception:
            shutil.rmtree(temporary_root, ignore_errors=True)
            raise
        result["datasetRoot"] = str(final_root / "UnityRaw")
        result["manifestPath"] = str(final_root / "manifest.json")
        result["requests"] = request_count
        result["hours"] = hours
        return result

    def prepare_live_dataset(
        self, start_text: str, end_text: str, fields: list[str],
        *, api_key: str | None = None,
    ) -> dict[str, object]:
        """Create a lightweight remote dataset without downloading field values."""
        start = parse_utc_hour(start_text)
        end = parse_utc_hour(end_text)
        if end <= start:
            raise WaveImportError("end must be later than start")
        hours = int((end - start).total_seconds() // 3600)
        if hours > 31 * 24:
            raise WaveImportError("One live dataset may contain at most 31 days.")
        normalized_fields = list(dict.fromkeys(field.strip().lower() for field in fields))
        unsupported = sorted(set(normalized_fields) - set(FIELD_METADATA))
        if not normalized_fields or unsupported:
            raise WaveImportError(
                "Supported fields are hs and elev. Unsupported: " + ", ".join(unsupported)
            )
        dataset_id = (
            "wave_live_" + start.strftime("%Y%m%dT%H%MZ") + "_" +
            end.strftime("%Y%m%dT%H%MZ") + "_" + "_".join(normalized_fields)
        )
        # Hybrid source: a bundled hydrated dataset answers first, so a package
        # opens with no tailnet, no API key and no remote call at all.
        cached = self._find_covering(start, end, normalized_fields)
        if cached is not None:
            global WAVE_MODE
            WAVE_MODE = "cache"
            seeded_root = self._seed_from_cache(cached, dataset_id)
            return {
                "datasetId": dataset_id,
                "datasetVersion": self._dataset_version(seeded_root),
                "variables": [
                    FIELD_METADATA[field][0] for field in normalized_fields
                ],
                "start": _iso_utc(start),
                "end": _iso_utc(end),
                "hours": hours,
                "live": True,
                "reused": True,
                "hydrated": True,
                "source": "cache",
                "datasetRoot": str(seeded_root / "UnityRaw"),
                "manifestPath": str(seeded_root / "manifest.json"),
            }
        # Validate credentials and connectivity now, while the user is still on
        # the opening screen. Field values remain completely lazy.
        status = self.status(api_key)
        if not status.get("configured"):
            # Say both halves: the key is missing *and* nothing bundled covers
            # this window. The bare "Wave API key is missing." sent the operator
            # looking for the wrong problem — on a packaged install the useful
            # answer is usually "this machine is not on the tailnet and the
            # bundle only has other windows".
            raise WaveImportError(
                self._offline_detail(normalized_fields[0], start)
            )
        if not status.get("reachable"):
            raise WaveImportError(str(status.get("error", "Wave API is unreachable.")))

        final_root = self.import_root / dataset_id
        # Every launch re-registers the same window. Rebuilding it would discard
        # the hydrated frames and download the whole range again, which trips
        # the gateway rate limit, so an existing registration is reused as is.
        # Reuse only a dataset that already holds real frames. An unhydrated
        # registration is placeholder-only, so it is rebuilt to keep its
        # metadata fresh (this costs one metadata call, no field download).
        if (
            final_root.is_dir()
            and (final_root / ".hydrated").is_file()
            and self._live_identity(final_root)
            == (start, end, tuple(sorted(normalized_fields)))
        ):
            return {
                "datasetId": dataset_id,
                "datasetVersion": self._dataset_version(final_root),
                "variables": [
                    FIELD_METADATA[field][0] for field in normalized_fields
                ],
                "start": _iso_utc(start),
                "end": _iso_utc(end),
                "hours": hours,
                "live": True,
                "reused": True,
                "hydrated": (final_root / ".hydrated").is_file(),
                "source": "live",
                "datasetRoot": str(final_root / "UnityRaw"),
                "manifestPath": str(final_root / "manifest.json"),
            }
        temporary_root = self.import_root / ("." + dataset_id + "-" + uuid.uuid4().hex)
        temporary_root.mkdir(parents=True, exist_ok=False)
        try:
            result = self._write_live_dataset(
                temporary_root, dataset_id, start, end, normalized_fields
            )
            if final_root.exists():
                shutil.rmtree(final_root)
            temporary_root.replace(final_root)
        except Exception:
            shutil.rmtree(temporary_root, ignore_errors=True)
            raise
        result["datasetRoot"] = str(final_root / "UnityRaw")
        result["manifestPath"] = str(final_root / "manifest.json")
        result.setdefault("source", "live")
        return result

    @staticmethod
    def _live_identity(root: Path):
        """Return (start, end, fields) for an existing live registration."""
        try:
            conversion = json.loads(
                (root / "UnityRaw" / "conversion_manifest.json").read_text(
                    encoding="utf-8"
                )
            )
            entries = [
                entry
                for entry in conversion.get("datasets", [])
                if entry.get("liveWave") and entry.get("timeUTC")
            ]
            if not entries:
                return None
            windows = [
                parse_utc_hour(str(value)) for value in entries[0]["timeUTC"]
            ]
            fields = tuple(
                sorted(str(entry.get("sourceField", "")).lower() for entry in entries)
            )
            return (windows[0], windows[-1] + timedelta(hours=1), fields)
        except (OSError, ValueError, KeyError, WaveImportError):
            return None

    @staticmethod
    def _dataset_version(root: Path) -> str:
        try:
            manifest = json.loads(
                (root / "manifest.json").read_text(encoding="utf-8")
            )
            return str(manifest.get("datasetVersion", ""))
        except (OSError, ValueError):
            return ""

    def hydrate_dataset(
        self,
        dataset_root: Path,
        *,
        api_key: str | None = None,
        progress: Callable[[str], None] | None = None,
    ) -> dict[str, object]:
        """Replace a live dataset's placeholder frames with real Wave values.

        Live registration writes zero-filled RAW frames so the Unity surface can
        open instantly and stay lazy. The analysis service reads RAW from disk,
        so placeholders have to become real frames before a Grid can be built.
        """
        root = Path(dataset_root).resolve()
        conversion_path = root / "UnityRaw" / "conversion_manifest.json"
        if not conversion_path.is_file():
            return {"hydrated": False, "reason": "dataset has no conversion manifest"}
        conversion = json.loads(conversion_path.read_text(encoding="utf-8"))
        entries = [
            entry
            for entry in conversion.get("datasets", [])
            if entry.get("liveWave") and entry.get("timeUTC")
        ]
        if not entries:
            return {"hydrated": False, "reason": "dataset is not a live Wave import"}
        marker = root / ".hydrated"
        if marker.is_file():
            return {"hydrated": False, "reason": "dataset is already hydrated"}

        unity_root = root / "UnityRaw"
        pending: list[tuple[dict[str, object], str, list[Path]]] = []
        for entry in entries:
            field = str(entry.get("sourceField") or "").strip().lower()
            if field not in FIELD_METADATA:
                raise WaveImportError(f"Unsupported live Wave field: {field!r}")
            directory = unity_root / str(entry["name"])
            frames = sorted(directory.glob("event_01_time_*.raw"))
            if not frames:
                raise WaveImportError(f"Live dataset has no frames under {directory}")
            if any(
                not np.any(np.fromfile(frame, dtype=np.uint8)) for frame in frames
            ):
                pending.append((entry, field, frames))
        if not pending:
            return {"hydrated": False, "reason": "frames already contain values"}

        key = (api_key or os.getenv("WAVE_API_KEY", "")).strip()
        if not key:
            raise WaveImportError(
                "Wave API key is missing. Set WAVE_API_KEY in the repository .env file."
            )
        windows = [parse_utc_hour(str(value)) for value in entries[0]["timeUTC"]]
        start = windows[0]
        hours = len(windows)
        node_ids, valid, vertices, weights = self._load_static_arrays()
        node_lookup = {int(value): index for index, value in enumerate(node_ids)}
        values = {
            field: np.full((hours, node_ids.size), np.nan, dtype=np.float32)
            for _, field, _ in pending
        }
        report = progress or (lambda _: None)
        end = start + timedelta(hours=hours)
        request_count = 0
        failed_batches = 0
        total_batches = 0
        for node_batch in _chunks(node_ids, 100):
            for chunk_start, chunk_end in _time_chunks(
                start, end, len(node_batch)
            ):
                total_batches += 1
                report(
                    f"Hydrating nodes {int(node_batch[0])}-{int(node_batch[-1])} "
                    f"from {_iso_utc(chunk_start)}"
                )
                try:
                    rows = self._get_json(
                        "/v1/wave",
                        key,
                        {
                            "start": _iso_utc(chunk_start),
                            "end": _iso_utc(chunk_end),
                            "node_ids": ",".join(
                                str(int(value)) for value in node_batch
                            ),
                            "fields": ",".join(sorted(values)),
                        },
                    )
                except WaveImportError as exc:
                    # A single failing batch must not throw away the whole
                    # window: keep the nodes that answered and judge coverage
                    # once every batch has been attempted.
                    failed_batches += 1
                    report(f"Batch {total_batches} failed after retries: {exc}")
                    continue
                request_count += 1
                if not isinstance(rows, list):
                    failed_batches += 1
                    continue
                for row in rows:
                    self._store_row(row, start, hours, node_lookup, values)
                # Keep the burst gentle: a full window is one call per
                # 100-node batch and the gateway throttles aggressive clients.
                time.sleep(0.15)

        if total_batches and failed_batches:
            coverage = 1.0 - (failed_batches / float(total_batches))
            if coverage < 0.5:
                raise WaveImportError(
                    "Wave API answered only "
                    f"{int(round(coverage * 100.0))}% of this window "
                    f"({failed_batches}/{total_batches} batches failed). "
                    "Retry once the gateway is reachable again."
                )
            report(
                f"Hydrating with partial coverage: {failed_batches} of "
                f"{total_batches} batches are missing."
            )
        partial = failed_batches > 0

        written: dict[str, object] = {}
        for entry, field, frames in pending:
            frame_values = values[field]
            finite = frame_values[np.isfinite(frame_values)]
            if finite.size == 0:
                raise WaveImportError(
                    f"Wave API returned no usable {field} values for this window."
                )
            minimum, maximum = FIELD_DISPLAY_RANGES[field]
            node_file = unity_root / "GeoSurface" / f"{entry['name']}_nodes_f32.bin"
            # A read-only or full volume must surface as a clear failure: the
            # marker is only written after every frame lands, and the caller
            # turns this into an actionable message instead of a bare 500.
            try:
                with node_file.open("wb") as stream:
                    for row in frame_values:
                        stream.write(np.asarray(row, dtype="<f4").tobytes())
                for index, frame_path in enumerate(frames):
                    volume = self._encode_frame(
                        frame_values[min(index, hours - 1)],
                        valid,
                        vertices,
                        weights,
                        minimum,
                        maximum,
                    )
                    frame_path.write_bytes(volume.tobytes(order="C"))
            except OSError as exc:
                raise WaveImportError(
                    "Could not write the hydrated frames into "
                    f"{unity_root}: {exc}"
                ) from exc
            written[str(entry["name"])] = {
                "frames": len(frames),
                "minimum": float(finite.min()),
                "maximum": float(finite.max()),
            }
        # Only a complete fetch is sealed. A partial one stays retryable so the
        # next analysis can fill the gaps instead of trusting them forever.
        if not partial:
            marker.write_text(
                _iso_utc(datetime.now(timezone.utc)), encoding="utf-8"
            )
        return {
            "hydrated": True,
            "partial": partial,
            "failedBatches": failed_batches,
            "totalBatches": total_batches,
            "datasetId": root.name,
            "hours": hours,
            "requests": request_count,
            "variables": written,
        }

    def fetch_frame_batch(
        self, time_text: str, field: str, node_ids: list[int],
        *, api_key: str | None = None,
    ) -> dict[str, object]:
        """Proxy one visible-frame patch; callers cap concurrency at three."""
        normalized_field = field.strip().lower()
        if normalized_field not in FIELD_METADATA:
            raise WaveImportError(f"Unsupported Wave field: {field}")
        if not node_ids or len(node_ids) > 100:
            raise WaveImportError("A frame batch must contain 1 to 100 node IDs.")
        if any(node_id < 1 or node_id > 15523 for node_id in node_ids):
            raise WaveImportError("A frame batch contains an invalid node ID.")
        start = parse_utc_hour(time_text)
        end = start + timedelta(hours=1)
        cached = self._find_covering(start, end, [normalized_field])
        if cached is not None:
            local_items = self._local_items(
                cached, normalized_field, node_ids, start, 1
            )
            if local_items is not None:
                global WAVE_MODE
                WAVE_MODE = "cache"
                return {
                    "items": [
                        {
                            "nodeId": int(item["nodeId"]),
                            "value": float(item["value"]),
                        }
                        for item in local_items
                    ],
                    "time": _iso_utc(start),
                    "field": normalized_field,
                    "source": "cache",
                }
        key = (api_key or os.getenv("WAVE_API_KEY", "")).strip()
        if not key:
            raise WaveImportError(self._offline_detail(normalized_field, start))
        rows = self._get_json(
            "/v1/wave",
            key,
            {
                "start": _iso_utc(start),
                "end": _iso_utc(end),
                "node_ids": ",".join(str(value) for value in node_ids),
                "fields": normalized_field,
            },
        )
        if not isinstance(rows, list):
            raise WaveImportError("Wave API returned a non-list response.")
        items = []
        for row in rows:
            if not isinstance(row, dict) or row.get(normalized_field) is None:
                continue
            items.append({
                "nodeId": int(row["node_id"]),
                "value": float(row[normalized_field]),
            })
        return {"items": items, "time": _iso_utc(start), "field": normalized_field}

    def fetch_timeline_batch(
        self, start_text: str, end_text: str, field: str, node_ids: list[int],
        *, api_key: str | None = None,
    ) -> dict[str, object]:
        """Proxy one node patch for every hour currently shown by the app."""
        normalized_field = field.strip().lower()
        if normalized_field not in FIELD_METADATA:
            raise WaveImportError(f"Unsupported Wave field: {field}")
        if not node_ids or len(node_ids) > 100:
            raise WaveImportError("A timeline batch must contain 1 to 100 node IDs.")
        if any(node_id < 1 or node_id > 15523 for node_id in node_ids):
            raise WaveImportError("A timeline batch contains an invalid node ID.")
        start = parse_utc_hour(start_text)
        end = parse_utc_hour(end_text)
        if end <= start:
            raise WaveImportError("end must be later than start")
        hours = int((end - start).total_seconds() // 3600)
        if hours > 31 * 24 or hours * len(node_ids) > 50_000:
            raise WaveImportError("The requested timeline batch is too large.")
        cached = self._find_covering(start, end, [normalized_field])
        if cached is not None:
            local_items = self._local_items(
                cached, normalized_field, node_ids, start, hours
            )
            if local_items is not None:
                global WAVE_MODE
                WAVE_MODE = "cache"
                return {
                    "items": local_items,
                    "start": _iso_utc(start),
                    "end": _iso_utc(end),
                    "field": normalized_field,
                    "hours": hours,
                    "source": "cache",
                }
        key = (api_key or os.getenv("WAVE_API_KEY", "")).strip()
        if not key:
            raise WaveImportError(self._offline_detail(normalized_field, start))
        rows = self._get_json(
            "/v1/wave",
            key,
            {
                "start": _iso_utc(start),
                "end": _iso_utc(end),
                "node_ids": ",".join(str(value) for value in node_ids),
                "fields": normalized_field,
            },
        )
        if not isinstance(rows, list):
            raise WaveImportError("Wave API returned a non-list response.")
        items = []
        for row in rows:
            if not isinstance(row, dict) or row.get(normalized_field) is None:
                continue
            timestamp = parse_utc_hour(str(row["time"]))
            frame_index = int((timestamp - start).total_seconds() // 3600)
            if frame_index < 0 or frame_index >= hours:
                continue
            items.append({
                "frameIndex": frame_index,
                "nodeId": int(row["node_id"]),
                "value": float(row[normalized_field]),
            })
        return {
            "items": items,
            "start": _iso_utc(start),
            "end": _iso_utc(end),
            "field": normalized_field,
            "hours": hours,
        }

    def _get_json(
        self,
        path: str,
        api_key: str,
        params: dict[str, str] | None = None,
        *,
        attempts: int = 4,
        timeout_seconds: float = 90.0,
    ) -> object:
        # A full window needs one request per 100-node batch, so the gateway
        # rate limiter answers with 429 long before the range is complete.
        # Back off and retry instead of failing the whole Grid.
        for attempt in range(attempts):
            try:
                with httpx.Client(
                    transport=self.transport,
                    timeout=httpx.Timeout(timeout_seconds),
                    headers={"X-API-Key": api_key},
                ) as client:
                    response = client.get(WAVE_API_URL + path, params=params)
                    response.raise_for_status()
                    return response.json()
            except httpx.HTTPStatusError as exc:
                status = exc.response.status_code
                retryable = status == 429 or 500 <= status < 600
                if retryable and attempt + 1 < attempts:
                    delay = _retry_delay(exc.response, attempt)
                    time.sleep(delay)
                    continue
                detail = _redact(exc.response.text[:300], api_key)
                raise WaveImportError(
                    f"Wave API returned {status}: {detail}"
                ) from exc
            except (httpx.HTTPError, ValueError) as exc:
                if attempt + 1 < attempts:
                    time.sleep(_retry_delay(None, attempt))
                    continue
                raise WaveImportError(
                    "Wave API request failed: " + _redact(str(exc), api_key)
                ) from exc
        raise WaveImportError("Wave API request failed: retries exhausted")

    def _write_live_dataset(
        self, root: Path, dataset_id: str, start: datetime, end: datetime,
        fields: list[str],
    ) -> dict[str, object]:
        node_ids, _, _, _ = self._load_static_arrays()
        hours = int((end - start).total_seconds() // 3600)
        unity_root = root / "UnityRaw"
        geo_root = unity_root / "GeoSurface"
        geo_root.mkdir(parents=True)
        for name in ("lon_lat_f32.bin", "faces_u32.bin", "node_ids_u32.bin"):
            shutil.copy2(self.static_root / name, geo_root / name)
        source_manifest = json.loads(
            (self.workspace_root / "For_VR" / "UnityRaw" /
             "conversion_manifest.json").read_text(encoding="utf-8")
        )
        timestamps_utc = [_iso_utc(start + timedelta(hours=index)) for index in range(hours)]
        timestamps_hkt = [
            (start + timedelta(hours=index)).astimezone(
                timezone(timedelta(hours=8))
            ).isoformat()
            for index in range(hours)
        ]
        conversion = {
            "schemaVersion": "1.0",
            "source": "Wave subset API (live, on demand)",
            "grid": {"x": GRID_X, "y": GRID_Y, "z": GRID_Z},
            "boundsEPSG4326": source_manifest["boundsEPSG4326"],
            "geographicSurface": source_manifest["geographicSurface"],
            "datasets": [],
        }
        variables = {}
        empty_volume = bytes(GRID_X * GRID_Y * GRID_Z)
        empty_nodes = np.full(node_ids.size, np.nan, dtype="<f4").tobytes()
        for field in fields:
            directory_name, channel, unit = FIELD_METADATA[field]
            minimum, maximum = FIELD_DISPLAY_RANGES[field]
            output_dir = unity_root / directory_name
            output_dir.mkdir(parents=True)
            exact_path = geo_root / f"{directory_name}_nodes_f32.bin"
            with exact_path.open("wb") as stream:
                for _ in range(hours):
                    stream.write(empty_nodes)
            frames = []
            for index in range(hours):
                raw_name = f"event_01_time_{index:04d}.raw"
                raw_path = output_dir / raw_name
                raw_path.write_bytes(empty_volume)
                (output_dir / f"{raw_name}.ini").write_text(
                    f"dimx:{GRID_X}\ndimy:{GRID_Y}\ndimz:{GRID_Z}\n"
                    "skip:0\nformat:uint8\nendianness:littleendian\n",
                    encoding="utf-8",
                )
                frames.append({
                    "frameId": f"{directory_name}_t{index:04d}",
                    "timeIndex": index,
                    "temporalMeaning": "instantaneous",
                    "path": f"UnityRaw/{directory_name}/{raw_name}",
                    "expectedBytes": GRID_X * GRID_Y * GRID_Z,
                    "sha256": None,
                })
            scale = (maximum - minimum) / 254.0
            conversion["datasets"].append({
                "name": directory_name,
                "sourceField": field,
                "channel": channel,
                "unit": unit,
                "physicalMinimum": minimum,
                "physicalMaximum": maximum,
                "frameCount": hours,
                "geographicValuesFile": f"GeoSurface/{directory_name}_nodes_f32.bin",
                "geographicValuesEncoding": "little-endian float32 physical values",
                "geographicFrameStrideBytes": int(node_ids.size * 4),
                "timeHKT": timestamps_hkt,
                "liveWave": True,
                "waveField": field,
                "timeUTC": timestamps_utc,
            })
            variables[directory_name] = {
                "displayName": directory_name,
                "unit": unit,
                "valueSemantics": "physical",
                "voxelType": "uint8",
                "scale": scale,
                "offset": minimum - scale,
                "missingRawValues": [0],
                "frames": frames,
            }
        (unity_root / "conversion_manifest.json").write_text(
            json.dumps(conversion, ensure_ascii=False, indent=2), encoding="utf-8"
        )
        bounds = conversion["boundsEPSG4326"]
        dataset_version = _iso_utc(datetime.now(timezone.utc))
        manifest = {
            "schemaVersion": "1.0",
            "datasetId": dataset_id,
            "datasetVersion": dataset_version,
            "dimensions": {"x": GRID_X, "y": GRID_Y, "z": GRID_Z},
            "storageOrder": "ZYX",
            "defaultVoxelType": "uint8",
            "coordinates": {
                "coordinateReference": "EPSG:4326", "renderProjection": "WebMercator",
                "x": {"kind": "regular_geographic_grid", "axis": "longitude",
                      "unit": "degree_east", "start": bounds["lonMin"],
                      "step": (bounds["lonMax"] - bounds["lonMin"]) / (GRID_X - 1)},
                "y": {"kind": "regular_geographic_grid", "axis": "latitude",
                      "unit": "degree_north", "start": bounds["latMin"],
                      "step": (bounds["latMax"] - bounds["latMin"]) / (GRID_Y - 1)},
                "depth": {"kind": "ordinal_index", "unit": "display_extrusion_layer",
                          "start": 0, "step": 1, "positive": "down",
                          "excludedIndices": []},
            },
            "variables": variables,
            "assumptions": [{
                "id": "live-wave-source",
                "statement": "Frames are fetched from the Wave API only when displayed.",
                "evidence": "The Unity surface requests 100-node patches with at most three concurrent calls.",
                "status": "measured",
            }],
        }
        (root / "manifest.json").write_text(
            json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
        )
        return {
            "datasetId": dataset_id,
            "datasetVersion": dataset_version,
            "variables": list(variables),
            "start": _iso_utc(start),
            "end": _iso_utc(end),
            "hours": hours,
            "live": True,
        }

    def _load_static_arrays(self):
        required = {
            "nodes": self.static_root / "node_ids_u32.bin",
            "valid": self.static_root / "raster_valid_u8.bin",
            "vertices": self.static_root / "raster_vertices_u32.bin",
            "weights": self.static_root / "raster_weights_f32.bin",
        }
        missing = [str(path) for path in required.values() if not path.is_file()]
        if missing:
            raise WaveImportError("Wave mesh mapping files are missing: " + ", ".join(missing))
        nodes = np.fromfile(required["nodes"], dtype="<u4")
        valid = np.fromfile(required["valid"], dtype=np.uint8).astype(bool)
        vertices = np.fromfile(required["vertices"], dtype="<u4").reshape(-1, 3)
        weights = np.fromfile(required["weights"], dtype="<f4").reshape(-1, 3)
        if valid.size != GRID_X * GRID_Y or vertices.shape[0] != valid.size:
            raise WaveImportError("Wave mesh mapping files have invalid dimensions.")
        return nodes, valid, vertices, weights

    @staticmethod
    def _store_row(row, start, hours, node_lookup, values) -> None:
        if not isinstance(row, dict):
            return
        try:
            node_index = node_lookup[int(row["node_id"])]
            timestamp = parse_utc_hour(str(row["time"]))
        except (KeyError, TypeError, ValueError, WaveImportError):
            return
        time_index = int((timestamp - start).total_seconds() // 3600)
        if time_index < 0 or time_index >= hours:
            return
        for field, target in values.items():
            value = row.get(field)
            if value is not None:
                target[time_index, node_index] = float(value)

    def _write_dataset(
        self, root, dataset_id, start, end, values,
        node_ids, valid, vertices, weights,
    ) -> dict[str, object]:
        unity_root = root / "UnityRaw"
        geo_root = unity_root / "GeoSurface"
        geo_root.mkdir(parents=True)
        for name in ("lon_lat_f32.bin", "faces_u32.bin", "node_ids_u32.bin"):
            shutil.copy2(self.static_root / name, geo_root / name)

        source_manifest = json.loads(
            (self.workspace_root / "For_VR" / "UnityRaw" /
             "conversion_manifest.json").read_text(encoding="utf-8")
        )
        conversion = {
            "schemaVersion": "1.0",
            "source": "Wave subset API",
            "grid": {"x": GRID_X, "y": GRID_Y, "z": GRID_Z},
            "boundsEPSG4326": source_manifest["boundsEPSG4326"],
            "geographicSurface": source_manifest["geographicSurface"],
            "datasets": [],
        }
        conversion["geographicSurface"]["nodeIdFile"] = "GeoSurface/node_ids_u32.bin"
        manifest_variables: dict[str, object] = {}
        timestamps = [
            (start + timedelta(hours=index)).astimezone(
                timezone(timedelta(hours=8))
            ).isoformat()
            for index in range(next(iter(values.values())).shape[0])
        ]

        for field, frames in values.items():
            directory_name, channel, unit = FIELD_METADATA[field]
            finite = frames[np.isfinite(frames)]
            if finite.size == 0:
                raise WaveImportError(f"Wave API returned no usable {field} values.")
            minimum = float(finite.min())
            maximum = float(finite.max())
            scale = (maximum - minimum) / 254.0
            if not math.isfinite(scale) or scale <= 0:
                scale = 1.0
            output_dir = unity_root / directory_name
            output_dir.mkdir(parents=True)
            exact_path = geo_root / f"{directory_name}_nodes_f32.bin"
            raw_frames = []
            with exact_path.open("wb") as exact_file:
                for index, frame in enumerate(frames):
                    exact_file.write(np.asarray(frame, dtype="<f4").tobytes())
                    volume = self._encode_frame(
                        frame, valid, vertices, weights, minimum, maximum
                    )
                    raw_name = f"event_01_time_{index:04d}.raw"
                    raw_path = output_dir / raw_name
                    raw_path.write_bytes(volume.tobytes(order="C"))
                    (output_dir / f"{raw_name}.ini").write_text(
                        f"dimx:{GRID_X}\ndimy:{GRID_Y}\ndimz:{GRID_Z}\n"
                        "skip:0\nformat:uint8\nendianness:littleendian\n",
                        encoding="utf-8",
                    )
                    raw_frames.append(raw_path)
            conversion["datasets"].append({
                "name": directory_name,
                "sourceField": field,
                "channel": channel,
                "unit": unit,
                "physicalMinimum": minimum,
                "physicalMaximum": maximum,
                "frameCount": len(raw_frames),
                "geographicValuesFile": f"GeoSurface/{directory_name}_nodes_f32.bin",
                "geographicValuesEncoding": "little-endian float32 physical values",
                "geographicFrameStrideBytes": int(node_ids.size * 4),
                "timeHKT": timestamps,
            })
            manifest_variables[directory_name] = {
                "displayName": directory_name,
                "unit": unit,
                "valueSemantics": "physical",
                "voxelType": "uint8",
                "scale": scale,
                "offset": minimum - scale,
                "missingRawValues": [0],
                "frames": [
                    {
                        "frameId": f"{directory_name}_t{index:04d}",
                        "timeIndex": index,
                        "temporalMeaning": "instantaneous",
                        "path": f"UnityRaw/{directory_name}/{path.name}",
                        "expectedBytes": GRID_X * GRID_Y * GRID_Z,
                        "sha256": None,
                    }
                    for index, path in enumerate(raw_frames)
                ],
            }

        (unity_root / "conversion_manifest.json").write_text(
            json.dumps(conversion, ensure_ascii=False, indent=2), encoding="utf-8"
        )
        bounds = conversion["boundsEPSG4326"]
        manifest = {
            "schemaVersion": "1.0",
            "datasetId": dataset_id,
            "datasetVersion": _iso_utc(datetime.now(timezone.utc)),
            "dimensions": {"x": GRID_X, "y": GRID_Y, "z": GRID_Z},
            "storageOrder": "ZYX",
            "defaultVoxelType": "uint8",
            "coordinates": {
                "coordinateReference": "EPSG:4326",
                "renderProjection": "WebMercator",
                "x": {"kind": "regular_geographic_grid", "axis": "longitude",
                      "unit": "degree_east", "start": bounds["lonMin"],
                      "step": (bounds["lonMax"] - bounds["lonMin"]) / (GRID_X - 1)},
                "y": {"kind": "regular_geographic_grid", "axis": "latitude",
                      "unit": "degree_north", "start": bounds["latMin"],
                      "step": (bounds["latMax"] - bounds["latMin"]) / (GRID_Y - 1)},
                "depth": {"kind": "ordinal_index", "unit": "display_extrusion_layer",
                          "start": 0, "step": 1, "positive": "down",
                          "excludedIndices": []},
            },
            "variables": manifest_variables,
            "assumptions": [{
                "id": "wave-api-source-units",
                "statement": "The Wave API has not yet published final units or water-level datum.",
                "evidence": "GET /v1/metadata marks these labels for confirmation.",
                "status": "unverified",
            }],
        }
        (root / "manifest.json").write_text(
            json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )
        return {
            "datasetId": dataset_id,
            "variables": list(manifest_variables),
            "start": _iso_utc(start),
            "end": _iso_utc(end),
        }

    @staticmethod
    def _encode_frame(frame, valid, vertices, weights, minimum, maximum):
        raster = np.full(valid.size, np.nan, dtype=np.float32)
        selected = vertices[valid].astype(np.int64)
        samples = frame[selected]
        usable = np.all(np.isfinite(samples), axis=1)
        interpolated = np.sum(samples * weights[valid], axis=1)
        valid_indices = np.flatnonzero(valid)
        raster[valid_indices[usable]] = interpolated[usable]
        encoded = np.zeros(valid.size, dtype=np.uint8)
        finite = np.isfinite(raster)
        value_range = max(maximum - minimum, np.finfo(np.float32).eps)
        encoded[finite] = np.clip(
            np.rint((raster[finite] - minimum) * (254.0 / value_range) + 1.0),
            1,
            255,
        ).astype(np.uint8)
        surface = encoded.reshape(GRID_Y, GRID_X)
        return np.repeat(surface[np.newaxis, :, :], GRID_Z, axis=0)
