#!/usr/bin/env python3
"""Isolated, offline S4D server for Unity's Validate Full Workflow menu.

Run with the repository's .venv Python. The Unity check uses port 8031 and
stops when its six-step report is written. This server uses the real S4D routes
and RAW/cache/snapshot code, and MatPlotAgent's deterministic chart renderer.
Remote model generation/interpretation is replaced with reproducible fixtures.
No production service, credential, data file or job directory is changed.
"""
from __future__ import annotations

import argparse
import os
import sys
import threading
import uuid
from pathlib import Path
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=8031)
    args = parser.parse_args()
    runtime = ROOT / ".runtime" / "full-workflow"
    runtime.mkdir(parents=True, exist_ok=True)
    # Configuration is captured during import, so isolate it first.
    os.environ.update(S4D_OFFLINE="1", S4D_WAVE_CACHE=str(ROOT / "For_VR/WaveCache"),
                      S4D_JOB_ROOT=str(runtime / "jobs"),
                      S4D_SNAPSHOT_ROOT=str(runtime / "snapshots"),
                      S4D_WAVE_IMPORT_ROOT=str(runtime / "wave-imports"))
    from Services.MatPlotAgent.local_run import render_contract_fallback
    from Services.S4DAnalysisService.app import app
    from Services.S4DAnalysisService.digest import build_deterministic_digest
    from Services.S4DAnalysisService.routers import analysis, jobs
    import uvicorn

    rendered = {}
    lock = threading.Lock()

    class FixtureGateway:
        def __init__(self, *_args, **_kwargs):
            pass

        def submit(self, package):
            with lock:
                if not render_contract_fallback(package.data_csv.parent, "final.png"):
                    raise RuntimeError("The deterministic chart renderer failed")
                job_id = uuid.uuid4().hex
                rendered[job_id] = package.data_csv.parent
            return SimpleNamespace(job_id=job_id)

        def status(self, job_id):
            if job_id not in rendered:
                raise KeyError(job_id)
            return dict(status="completed", progress=1.0, stage="fixture chart ready", error="")

        def artifact(self, job_id, kind):
            name = "final.png" if kind == "image" else "chart_result.json"
            return (rendered[job_id] / name).read_bytes(), (
                "image/png" if kind == "image" else "application/json")

    analysis.MatPlotAgentGateway = FixtureGateway
    jobs.MatPlotAgentGateway = FixtureGateway
    jobs.build_llm_digest = lambda snapshot, _image: {
        **build_deterministic_digest(snapshot), "generatedBy": "offline-workflow-fixture"}
    uvicorn.run(app, host="127.0.0.1", port=args.port)


if __name__ == "__main__":
    main()
