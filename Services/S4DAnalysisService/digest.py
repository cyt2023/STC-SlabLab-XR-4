from __future__ import annotations

import base64
import json
import math
import os
import re
from typing import Any

from openai import OpenAI


def build_deterministic_digest(snapshot: dict[str, Any]) -> dict[str, Any]:
    """Build an evidence-only Grid digest from immutable snapshot metadata."""
    request = snapshot.get("request") or {}
    all_cells = list(snapshot.get("cells", []))
    cells = _evidence_cells(all_cells)
    task = request.get("analyticTask", "characterize_distribution")
    raw_intent = request.get("rawIntent", "")
    if not cells:
        return {
            "headline": "GRID DIGEST",
            "summary": "No completed cell statistics are available yet.",
            "findings": [],
            "highestCell": "",
            "lowestCell": "",
            "widestCell": "",
            "analyticTask": task,
            "rawIntent": raw_intent,
            "generatedBy": "deterministic",
            "validCellCount": 0,
            "excludedCellCount": len(all_cells),
        }

    highest = max(cells, key=lambda cell: float(cell["mean"]))
    lowest = min(cells, key=lambda cell: float(cell["mean"]))
    widest = max(
        cells,
        key=lambda cell: float(cell["maximum"]) - float(cell["minimum"]),
    )
    validest = max(cells, key=lambda cell: float(cell.get("validFraction", 0.0)))
    variable = str(snapshot.get("variableId") or request.get("variableId") or
                   cells[0].get("variableId") or "selected variable")
    if variable.lower() in {"no3", "salt"}:
        variable = variable.upper()
    elif variable.lower() == "chlorophyll":
        variable = "Chlorophyll"
    unit = str((snapshot.get("sharedScale") or {}).get("unit") or "").strip()
    unit_suffix = f" {unit}" if unit else ""
    cell_labels = _cell_labels(request)
    highest_label = cell_labels.get(highest["cellId"], _human_cell(highest["cellId"]))
    lowest_label = cell_labels.get(lowest["cellId"], _human_cell(lowest["cellId"]))
    widest_label = cell_labels.get(widest["cellId"], _human_cell(widest["cellId"]))
    time_pattern = _axis_pattern(cells, request.get("timeBuckets", []), 0)
    depth_pattern = _axis_pattern(cells, request.get("depthBuckets", []), 1)
    summary = (
        f"For {variable}, {highest_label} has the highest mean "
        f"({highest['mean']:.4g}{unit_suffix}), while {lowest_label} has the "
        f"lowest ({lowest['mean']:.4g}{unit_suffix}). "
        f"{time_pattern} {depth_pattern}"
    )
    directional_finding = " ".join(
        part for part in (time_pattern, depth_pattern) if part
    ) or f"{variable} does not show a clear directional change across the selected regions."
    findings = [
        f"{variable}: {directional_finding}",
        f"{variable} is highest on average in {highest_label} "
        f"({highest['mean']:.4g}{unit_suffix}).",
        f"{variable} is lowest on average in {lowest_label} "
        f"({lowest['mean']:.4g}{unit_suffix}).",
        (
            f"{variable} varies most within {widest_label}; its observed range is "
            f"{float(widest['maximum']) - float(widest['minimum']):.4g}{unit_suffix}."
        ),
    ]
    return {
        "headline": _headline_for_task(task),
        "summary": summary,
        "findings": findings,
        "highestCell": highest["cellId"],
        "lowestCell": lowest["cellId"],
        "widestCell": widest["cellId"],
        "analyticTask": task,
        "rawIntent": raw_intent,
        "generatedBy": "deterministic",
        "validCellCount": len(cells),
        "excludedCellCount": len(all_cells) - len(cells),
    }


def build_llm_digest(
    snapshot: dict[str, Any],
    grid_image: bytes | None = None,
) -> dict[str, Any]:
    """Ask the same configured multimodal model as MatPlotAgent to compare the Grid.

    Numeric snapshot evidence is always supplied, while the optional 3x3 montage lets
    the model inspect spatial shapes that min/mean/max alone cannot describe.
    """
    request = snapshot.get("request") or {}
    all_cells = list(snapshot.get("cells") or [])
    cells = _evidence_cells(all_cells)
    if not cells:
        return build_deterministic_digest(snapshot)

    provider = os.getenv("MATPLOT_PROVIDER", "").lower()
    use_qwen = provider == "qwen" or (
        not provider and bool(os.getenv("DASHSCOPE_API_KEY"))
    )
    key = os.getenv("DASHSCOPE_API_KEY") if use_qwen else os.getenv("OPENAI_API_KEY")
    if not key:
        fallback = build_deterministic_digest(snapshot)
        fallback["generatedBy"] = "deterministic-fallback (LLM key unavailable)"
        return fallback
    base_url = os.getenv(
        "OPENAI_BASE_URL",
        "https://dashscope.aliyuncs.com/compatible-mode/v1"
        if use_qwen
        else "https://api.openai.com/v1",
    )
    model = os.getenv(
        "MATPLOT_SUMMARY_MODEL",
        os.getenv("MATPLOT_MODEL", "qwen-vl-max" if use_qwen else "gpt-4.1-mini"),
    )
    evidence = {
        "variableId": snapshot.get("variableId") or request.get("variableId"),
        "analyticTask": request.get("analyticTask", "characterize_distribution"),
        "userIntent": request.get("rawIntent", ""),
        "timeBuckets": request.get("timeBuckets", []),
        "depthBuckets": request.get("depthBuckets", []),
        "sharedScale": snapshot.get("sharedScale", {}),
        "aggregation": snapshot.get("transform", {}),
        "cells": cells,
    }
    panel_count = len(cells)
    time_count = len(request.get("timeBuckets", []))
    depth_count = len(request.get("depthBuckets", []))
    prompt = (
        f"Analyze this complete {time_count} x {depth_count} Time x Depth "
        f"Facet Grid ({panel_count} valid evidence panels) as one scientific result. "
        "The image panels share one scale and the JSON statistics are authoritative. "
        "Compare only the supplied valid panels; never assume a nine-panel grid or "
        "invent a time/depth region that is absent from the evidence. Identify the "
        "strongest supported time trend, depth trend, "
        "interaction or anomaly, and mention missing/coverage limitations. Every "
        "finding must name the measured variable and the exact human-readable time "
        "and depth region it refers to. Explain the direction of change (for example, "
        "higher later or lower at depth), rather than merely listing extrema. Do not "
        "claim causality and do not invent values. If no physical unit or explicit "
        "measurement semantics are supplied, call the quantity 'dataset value' or "
        "'encoded intensity'; never call it a concentration. Return JSON only with keys: "
        "headline (short uppercase), summary (2 concise sentences), findings "
        "(3-5 concise strings), highestCell, lowestCell, widestCell. Cell IDs must "
        "exactly match the supplied JSON.\n\nEVIDENCE JSON:\n" +
        json.dumps(evidence, ensure_ascii=False, separators=(",", ":"))
    )
    content: list[dict[str, Any]] = [{"type": "text", "text": prompt}]
    if grid_image:
        content.append(
            {
                "type": "image_url",
                "image_url": {
                    "url": "data:image/jpeg;base64," +
                    base64.b64encode(grid_image).decode("ascii")
                },
            }
        )
    client = OpenAI(api_key=key, base_url=base_url)
    response = client.chat.completions.create(
        model=model,
        messages=[
            {
                "role": "system",
                "content": (
                    "You are a cautious scientific visualization analyst. Base every "
                    "statement on the supplied image panels and numeric evidence. "
                    "The actual panel count and axis buckets vary by request."
                ),
            },
            {"role": "user", "content": content},
        ],
        temperature=0,
        max_completion_tokens=900,
    )
    raw = response.choices[0].message.content or ""
    fenced = re.search(r"```(?:json)?\s*([\s\S]*?)```", raw, re.I)
    parsed = json.loads(fenced.group(1) if fenced else raw)
    fallback = build_deterministic_digest(snapshot)
    variable = str(evidence.get("variableId") or "selected variable")
    if variable.lower() in {"no3", "salt"}:
        variable = variable.upper()
    elif variable.lower() == "chlorophyll":
        variable = "Chlorophyll"
    llm_summary = str(parsed.get("summary") or "").strip()
    if variable.lower() not in llm_summary.lower():
        llm_summary = fallback["summary"]
    llm_findings = [
        str(item).strip() for item in parsed.get("findings", [])
        if str(item).strip() and variable.lower() in str(item).lower()
    ]
    # Lead with the scientifically useful directional comparison.  Model
    # interpretation comes next, while numeric extrema remain supporting
    # evidence instead of masquerading as the conclusion.
    findings = [fallback["findings"][0]]
    for item in llm_findings:
        if item not in findings:
            findings.append(item)
        if len(findings) >= 3:
            break
    for item in fallback["findings"][1:]:
        if item not in findings:
            findings.append(item)
        if len(findings) >= 5:
            break
    result = {
        "headline": str(parsed.get("headline") or "AI GRID SUMMARY"),
        "summary": llm_summary,
        "findings": findings,
        # The model writes the narrative, but navigation targets are numeric
        # facts.  Keep these deterministic so Highest, Lowest and Widest can
        # never be routed to a plausible-sounding but incorrect panel.
        "highestCell": fallback["highestCell"],
        "lowestCell": fallback["lowestCell"],
        "widestCell": fallback["widestCell"],
        "analyticTask": request.get("analyticTask", "characterize_distribution"),
        "rawIntent": request.get("rawIntent", ""),
        "generatedBy": "llm:" + model,
        "validCellCount": len(cells),
        "excludedCellCount": len(all_cells) - len(cells),
    }
    return result


def _evidence_cells(cells: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """Return only cells with real finite evidence.

    ``hasData``/``validCount`` are emitted by current snapshots.  The
    validFraction and finite-number checks retain compatibility with older
    completed snapshots while excluding legacy empty 0/0/0 placeholders.
    """
    evidence: list[dict[str, Any]] = []
    for cell in cells:
        if cell.get("hasData") is False:
            continue
        if "validCount" in cell and int(cell.get("validCount") or 0) <= 0:
            continue
        if float(cell.get("validFraction") or 0.0) <= 0.0:
            continue
        try:
            statistics = (
                float(cell["minimum"]),
                float(cell["mean"]),
                float(cell["maximum"]),
            )
        except (KeyError, TypeError, ValueError):
            continue
        if not all(math.isfinite(value) for value in statistics):
            continue
        evidence.append(cell)
    return evidence


def _headline_for_task(task: str) -> str:
    return {
        "find_anomalies": "ANOMALY DIGEST",
        "determine_range": "RANGE DIGEST",
        "characterize_trend": "TREND DIGEST",
        "correlate": "RELATIONSHIP DIGEST",
        "cluster": "PATTERN GROUP DIGEST",
    }.get(task, "DISTRIBUTION DIGEST")


def _bucket_name(bucket: dict[str, Any]) -> str:
    return str(bucket.get("label") or bucket.get("name") or bucket.get("id") or "region")


def _cell_labels(request: dict[str, Any]) -> dict[str, str]:
    result: dict[str, str] = {}
    for time_bucket in request.get("timeBuckets", []):
        for depth_bucket in request.get("depthBuckets", []):
            cell_id = f"{time_bucket.get('id')}__{depth_bucket.get('id')}"
            result[cell_id] = (
                f"{_bucket_name(time_bucket)} time at "
                f"{_bucket_name(depth_bucket)} depth"
            )
    return result


def _human_cell(cell_id: str) -> str:
    parts = str(cell_id).split("__", 1)
    if len(parts) == 2:
        return f"{parts[0].replace('_', ' ')} time at {parts[1].replace('_', ' ')} depth"
    return str(cell_id).replace("_", " ")


def _axis_pattern(
    cells: list[dict[str, Any]], buckets: list[dict[str, Any]], axis: int
) -> str:
    if len(buckets) < 2:
        return ""
    scores: list[tuple[str, float]] = []
    for bucket in buckets:
        bucket_id = str(bucket.get("id"))
        matching = []
        for cell in cells:
            parts = str(cell.get("cellId", "")).split("__", 1)
            if len(parts) == 2 and parts[axis] == bucket_id:
                matching.append(float(cell["mean"]))
        if matching:
            scores.append((_bucket_name(bucket), sum(matching) / len(matching)))
    if len(scores) < 2:
        return ""
    low = min(scores, key=lambda item: item[1])
    high = max(scores, key=lambda item: item[1])
    dimension = "time period" if axis == 0 else "depth region"
    if math.isclose(low[1], high[1], rel_tol=1e-6, abs_tol=1e-12):
        return f"Average values are similar across the selected {dimension}s."
    return f"Across {dimension}s, {high[0]} is higher on average than {low[0]}."
