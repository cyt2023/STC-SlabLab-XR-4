#!/usr/bin/env python3
"""Bundle a hydrated Wave window so the packaged app works without the tailnet.

Run this once with network + WAVE_API_KEY available: it registers the window
through the normal gateway, hydrates it, and copies the hydrated dataset into
``For_VR/WaveCache/<datasetId>/`` which ``Packaging/macOS/package-macos.sh``
ships with the app. The service then answers from that copy whenever the live
gateway is unreachable (or when started with ``--offline``).
"""

from __future__ import annotations

import argparse
import json
import shutil
import sys
import time
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT))

from Services.S4DAnalysisService.wave_importer import (  # noqa: E402
    WaveDatasetImporter,
    WaveImportError,
)

DEFAULT_START = "2019-11-30T00:00:00Z"
DEFAULT_END = "2019-12-01T00:00:00Z"
DEFAULT_FIELDS = "hs,elev"


def directory_size(path: Path) -> int:
    total = 0
    for item in path.rglob("*"):
        if item.is_file() and not item.is_symlink():
            total += item.stat().st_size
    return total


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--start", default=DEFAULT_START)
    parser.add_argument("--end", default=DEFAULT_END)
    parser.add_argument("--fields", default=DEFAULT_FIELDS)
    parser.add_argument("--force", action="store_true",
                        help="replace an existing bundle for the same window")
    arguments = parser.parse_args()

    fields = [field.strip().lower() for field in arguments.fields.split(",")
              if field.strip()]
    cache_root = REPO_ROOT / "For_VR" / "WaveCache"
    importer = WaveDatasetImporter(
        REPO_ROOT, REPO_ROOT / ".runtime" / "wave-imports",
        cache_root=cache_root,
    )

    print("Registering %s .. %s (%s)" %
          (arguments.start, arguments.end, ",".join(fields)))
    live = importer.prepare_live_dataset(arguments.start, arguments.end, fields)
    dataset_root = Path(str(live["datasetRoot"])).parent
    print("  dataset: %s" % dataset_root)
    print("  source : %s (hydrated=%s)" %
          (live.get("source", "gateway"), live.get("hydrated")))

    if not live.get("hydrated"):
        print("Hydrating frames from the Wave gateway ...")
        started = time.time()
        result = importer.hydrate_dataset(dataset_root)
        print("  %s (%.1fs)" % (result, time.time() - started))
        if not (dataset_root / ".hydrated").is_file():
            raise SystemExit(
                "Hydration did not complete; run this again with the gateway "
                "reachable (needs WAVE_API_KEY in .env)."
            )

    target = cache_root / dataset_root.name
    if target.is_dir() and not arguments.force:
        print("Bundle already present: %s" % target)
    else:
        cache_root.mkdir(parents=True, exist_ok=True)
        if target.exists():
            shutil.rmtree(target)
        shutil.copytree(dataset_root, target)
        print("Wrote bundle: %s" % target)

    bundle = {
        "datasetId": target.name,
        "start": str(live.get("start", arguments.start)),
        "end": str(live.get("end", arguments.end)),
        "fields": fields,
        "hours": live.get("hours"),
        "datasetVersion": live.get("datasetVersion"),
        "createdAt": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "bytes": directory_size(target),
    }
    (target / "bundle.json").write_text(
        json.dumps(bundle, indent=2) + "\n", encoding="utf-8"
    )
    print("Bundle size: %.1f MB" % (bundle["bytes"] / 1024 / 1024))
    print("Done. The packaged app will use this when the gateway is unreachable.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except WaveImportError as error:
        raise SystemExit("Wave import failed: %s" % error)
