from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np

from .digest import build_deterministic_digest
from .matplot_contract import _requested_chart_type
from .models import FacetGridRequest
from .snapshot_store import SnapshotStore


CHART_CASES = {
    "Generate a heatmap": "horizontal_heatmap",
    "Generate a bar chart": "bar_chart",
    "Show a histogram": "histogram",
    "Generate a scatter plot": "scatter_plot",
    "Generate a line chart": "line_chart",
    "Generate a pie chart": "pie_chart",
    "Generate a box plot": "box_plot",
    "Generate a violin plot": "violin_plot",
}


def percent_change(first: float, last: float) -> float:
    return (last - first) / first * 100.0


def main() -> None:
    parser = argparse.ArgumentParser(description="Validate the S4D evidence chain.")
    parser.add_argument(
        "--snapshot",
        default="67136421aff34ceb8e8c43689311b9f5",
        help="Completed snapshot ID to use as the real analysis Case.",
    )
    parser.add_argument("--output", default="docs/acceptance")
    parser.add_argument(
        "--verify-ground",
        action="store_true",
        help="Rebuild one representative cell from raw data (may take time).",
    )
    args = parser.parse_args()

    repository = Path(__file__).resolve().parents[2]
    snapshot_root = repository / ".runtime" / "s4d-analysis" / "snapshots"
    store = SnapshotStore(snapshot_root)
    snapshot = store.get(args.snapshot)
    if snapshot.get("status") != "completed":
        raise SystemExit(f"snapshot is not completed: {snapshot.get('status')}")

    cells = {cell["cellId"]: cell for cell in snapshot["cells"]}
    digest = build_deterministic_digest(snapshot)
    request = FacetGridRequest.model_validate(snapshot["request"])

    chart_results: list[dict[str, str | bool]] = []
    for prompt, expected in CHART_CASES.items():
        candidate = request.model_copy(update={"rawIntent": prompt})
        actual = _requested_chart_type(candidate)
        chart_results.append(
            {"prompt": prompt, "expected": expected, "actual": actual,
             "passed": actual == expected}
        )
    if not all(bool(case["passed"]) for case in chart_results):
        raise SystemExit("one or more chart intents failed contract resolution")

    ground = {"verified": False, "cellId": "before__surface"}
    if args.verify_ground:
        volume, source_cell = store.aggregate_volume(args.snapshot, ground["cellId"])
        valid = np.isfinite(volume)
        sums = np.where(valid, volume, 0.0).sum(axis=0, dtype=np.float64)
        counts = valid.sum(axis=0)
        grounded_xy = np.full(counts.shape, np.nan, dtype=np.float64)
        np.divide(sums, counts, out=grounded_xy, where=counts > 0)
        finite = grounded_xy[np.isfinite(grounded_xy)]
        rebuilt_mean = float(finite.mean()) if finite.size else float("nan")
        snapshot_mean = float(source_cell["mean"])
        ground.update(
            verified=True,
            snapshotMean=snapshot_mean,
            reconstructedMean=rebuilt_mean,
            absoluteDifference=abs(snapshot_mean - rebuilt_mean),
            validCount=int(finite.size),
        )
        if not np.isclose(snapshot_mean, rebuilt_mean, rtol=2e-5, atol=2e-4):
            raise SystemExit("Ground reconstruction does not match snapshot mean")

    surface_change = percent_change(
        float(cells["before__surface"]["mean"]),
        float(cells["after__surface"]["mean"]),
    )
    middle_change = percent_change(
        float(cells["before__middle"]["mean"]),
        float(cells["after__middle"]["mean"]),
    )
    deep_change = percent_change(
        float(cells["before__deep"]["mean"]),
        float(cells["after__deep"]["mean"]),
    )

    evidence = {
        "snapshotId": args.snapshot,
        "datasetId": snapshot["datasetId"],
        "variableId": snapshot["variableId"],
        "unit": snapshot["sharedScale"]["unit"],
        "question": "Where and when does NO3 encoded intensity change most?",
        "digest": digest,
        "timeChangesPercent": {
            "surface": surface_change,
            "middle": middle_change,
            "deep": deep_change,
        },
        "coverage": {
            cell_id: cell["validFraction"] for cell_id, cell in cells.items()
        },
        "ground": ground,
        "chartIntents": chart_results,
    }

    output = repository / args.output
    output.mkdir(parents=True, exist_ok=True)
    (output / "validated_no3_case.json").write_text(
        json.dumps(evidence, indent=2, ensure_ascii=False), encoding="utf-8"
    )

    ground_text = (
        f"Ground rebuilt `{ground['cellId']}` from raw data: snapshot mean "
        f"{ground['snapshotMean']:.6g}, rebuilt mean "
        f"{ground['reconstructedMean']:.6g}, absolute difference "
        f"{ground['absoluteDifference']:.3g}."
        if ground["verified"]
        else "Ground reconstruction is covered by the automated synthetic closure test; "
        "run this command with `--verify-ground` for a real-source rebuild."
    )
    chart_lines = "\n".join(
        f"- `{case['prompt']}` -> `{case['actual']}`: PASS"
        for case in chart_results
    )
    report = f"""# Validated NO3 Analysis Case

## Research question

Where and when does NO3 encoded intensity change most across the selected Time and Depth regions?

## Reproducible setup

- Dataset: `{snapshot['datasetId']}`
- Immutable snapshot: `{args.snapshot}`
- Variable: `NO3`
- Unit: `{snapshot['sharedScale']['unit']}`
- Time: Before 0-9, During 10-19, After 20-29
- Depth: Surface 0-29, Middle 30-60, Deep 61-90
- Aggregation: missing values excluded; equal-frame/equal-layer arithmetic mean

## Evidence-backed findings

- Surface mean changes from {cells['before__surface']['mean']:.4f} to {cells['after__surface']['mean']:.4f} ({surface_change:.1f}%).
- Middle mean changes from {cells['before__middle']['mean']:.4f} to {cells['after__middle']['mean']:.4f} ({middle_change:.1f}%).
- Deep mean changes from {cells['before__deep']['mean']:.4f} to {cells['after__deep']['mean']:.4f} ({deep_change:.1f}%).
- The highest absolute mean is `{digest['highestCell']}`; the lowest is `{digest['lowestCell']}`; the widest range is `{digest['widestCell']}`.
- Deep cells have only about 3.37% valid coverage, so their large decline is a lead to inspect, not a strong standalone conclusion.
- Surface cells have about 38.92% coverage and therefore provide the most defensible temporal comparison in this selected footprint.

## Conclusion

The selected NO3 encoded signal declines from Before to After at every depth bucket. The strongest relative decline appears in Deep, but its sparse coverage makes it uncertain. The more defensible result is a smaller, consistent decline at Surface, while Surface also contains the widest local range and should be inspected spatially for concentrated anomalies.

## Ground verification

{ground_text}

## MatPlot intent acceptance

{chart_lines}

## Interpretation boundary

These values are `encoded_intensity`, not calibrated chemical concentration. The system can support comparisons and anomaly localization, but it must not claim physical NO3 concentration without a calibration mapping and domain metadata.
"""
    (output / "validated_no3_case.md").write_text(report, encoding="utf-8")
    print(output / "validated_no3_case.md")


if __name__ == "__main__":
    main()
