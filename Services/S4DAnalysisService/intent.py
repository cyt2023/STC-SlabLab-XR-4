"""Keyword intent resolution used by the spoken-analysis endpoint."""

from __future__ import annotations

from .models import IntentResolution, IntentResolutionRequest

_INTENT_RULES: tuple[tuple[str, str, tuple[str, ...]], ...] = (
    (
        "find_anomalies",
        "FIND ANOMALIES",
        (
            "anomaly", "anomalies", "outlier", "outliers", "extreme", "hotspot",
            "异常", "离群", "极值", "热点",
        ),
    ),
    (
        "determine_range",
        "DETERMINE RANGE",
        (
            "range", "minimum", "maximum", "min ", "max ", "bounds",
            "范围", "区间", "最大", "最小", "上下限",
        ),
    ),
    (
        "characterize_trend",
        "CHARACTERIZE TREND",
        (
            "trend", "change over time", "increase", "decrease", "evolution",
            "趋势", "随时间", "变化", "上升", "下降", "演变",
        ),
    ),
    (
        "correlate",
        "CORRELATE",
        (
            "correlation", "correlate", "relationship", "association",
            "相关", "关系", "关联",
        ),
    ),
    (
        "cluster",
        "CLUSTER",
        (
            "cluster", "group similar", "pattern groups",
            "聚类", "分组", "相似模式",
        ),
    ),
    (
        "characterize_distribution",
        "CHARACTERIZE DISTRIBUTION",
        (
            "distribution", "compare", "spatial pattern", "where", "spread",
            "分布", "比较", "空间格局", "哪里", "扩散",
        ),
    ),
)


def resolve_intent_text(request: IntentResolutionRequest) -> IntentResolution:
    """Resolve free text to one validated Amar-style analytic task.

    This deterministic resolver is deliberately kept behind the service API so
    an LLM resolver can replace it later without changing the Unity contract.
    Ambiguous text falls back to distribution comparison and is explicitly
    marked as a fallback for user confirmation.
    """
    text = " ".join(request.text.strip().split())
    empty_input = not text
    if empty_input:
        text = "Compare the distribution across all cells."
    folded = text.casefold()
    matches: list[tuple[int, int, str, str]] = []
    for priority, (task, label, keywords) in enumerate(_INTENT_RULES):
        score = sum(1 for keyword in keywords if keyword.casefold() in folded)
        if score:
            matches.append((score, -priority, task, label))
    if matches:
        # Distribution words such as "compare" and "where" are generic. A
        # sentence such as "compare where the hotspots occur" must retain its
        # more specific anomaly intent even when it contains several generic
        # comparison words.
        specific_matches = [
            match for match in matches
            if match[2] != "characterize_distribution"
        ]
        score, _, task, label = max(specific_matches or matches)
        used_fallback = empty_input
        confidence = 0.45 if empty_input else min(
            0.98, 0.68 + 0.10 * (score - 1)
        )
    else:
        task = "characterize_distribution"
        label = "CHARACTERIZE DISTRIBUTION"
        used_fallback = True
        confidence = 0.45

    variable = request.variableDisplayName or request.variableId or "the selected variable"
    unit_suffix = f" ({request.unit})" if request.unit else ""
    focus = f"{variable}{unit_suffix} across every Time x Depth cell"
    normalized = (
        f"{label.title()}: {text}. Compare all cells with identical spatial "
        "encoding, missing-value handling, and one shared color scale."
    )
    return IntentResolution(
        rawText=text,
        analyticTask=task,
        displayLabel=label,
        focus=focus,
        confidence=confidence,
        usedFallback=used_fallback,
        normalizedInstruction=normalized,
    )


# Manifests that could not be read during the last registry scan. A dataset
# silently disappearing is impossible to act on, so /health reports them.
