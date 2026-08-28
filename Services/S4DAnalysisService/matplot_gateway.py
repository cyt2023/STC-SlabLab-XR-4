from __future__ import annotations

from dataclasses import dataclass

import httpx

from .matplot_contract import MatPlotAgentGridPackage


@dataclass(frozen=True)
class SubmittedMatPlotJob:
    job_id: str
    status_url: str


class MatPlotAgentGateway:
    def __init__(self, base_url: str, timeout_seconds: float = 30.0):
        self.base_url = base_url.rstrip("/")
        self.timeout_seconds = timeout_seconds

    def submit(self, package: MatPlotAgentGridPackage) -> SubmittedMatPlotJob:
        prompt = package.prompt_txt.read_text(encoding="utf-8")
        with (
            package.data_csv.open("rb") as data_stream,
            package.contract_json.open("rb") as contract_stream,
            httpx.Client(timeout=self.timeout_seconds, trust_env=False) as client,
        ):
            health = client.get(f"{self.base_url}/health")
            health.raise_for_status()
            response = client.post(
                f"{self.base_url}/jobs",
                data={"prompt": prompt},
                files={
                    "data": ("grid_data.csv", data_stream, "text/csv"),
                    "contract": (
                        "grid_contract.json",
                        contract_stream,
                        "application/json",
                    ),
                },
            )
            response.raise_for_status()
            payload = response.json()
        job_id = str(payload.get("job_id", "")).strip()
        if not job_id:
            raise RuntimeError("MatPlotAgent returned no job_id")
        return SubmittedMatPlotJob(
            job_id=job_id,
            status_url=str(payload.get("status_url", f"/jobs/{job_id}")),
        )

    def status(self, job_id: str) -> dict[str, object]:
        with httpx.Client(timeout=self.timeout_seconds, trust_env=False) as client:
            response = client.get(f"{self.base_url}/jobs/{job_id}")
            response.raise_for_status()
            return response.json()

    def artifact(self, job_id: str, name: str) -> tuple[bytes, str]:
        routes = {
            "image": ("image", "image/png"),
            "metadata": ("metadata", "application/json"),
        }
        if name not in routes:
            raise ValueError(f"unsupported MatPlotAgent artifact: {name}")
        route, content_type = routes[name]
        with httpx.Client(timeout=self.timeout_seconds, trust_env=False) as client:
            response = client.get(f"{self.base_url}/jobs/{job_id}/{route}")
            response.raise_for_status()
            return response.content, response.headers.get("content-type", content_type)
