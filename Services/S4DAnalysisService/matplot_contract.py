from __future__ import annotations

import csv
import json
from dataclasses import dataclass
from pathlib import Path

import numpy as np

from .models import FacetGridRequest
from .raw_reader import GridNumericResult


@dataclass(frozen=True)
class MatPlotAgentGridPackage:
    data_csv: Path
    contract_json: Path
    prompt_txt: Path


def _requested_chart_type(request: FacetGridRequest) -> str:
    """Resolve a spoken chart form to a bounded, testable rendering contract."""
    text = (request.rawIntent or "").casefold()
    aliases = (
        ("pie_chart", ("\u997c\u56fe", "\u997c\u72b6", "pie chart", "donut chart", "doughnut chart", "pie")),
        ("violin_plot", ("\u5c0f\u63d0\u7434", "violin plot", "violin chart", "violin")),
        ("box_plot", ("\u7bb1\u7ebf", "\u76d2\u987b", "box plot", "boxplot", "box-and-whisker")),
        ("scatter_plot", ("\u6563\u70b9", "scatter plot", "scatter chart", "scatter")),
        ("line_chart", ("\u6298\u7ebf", "\u66f2\u7ebf", "line chart", "line plot")),
        ("histogram", ("\u76f4\u65b9", "histogram")),
        ("bar_chart", ("\u67f1\u72b6", "\u67f1\u5f62", "bar chart", "bar graph")),
        ("horizontal_heatmap", ("\u70ed\u529b", "\u70ed\u56fe", "heatmap", "heat map")),
    )
    for chart_type, tokens in aliases:
        if any(token in text for token in tokens):
            return chart_type
    return request.chartType


def build_matplotagent_cell_packages(
    output_directory: str | Path,
    request: FacetGridRequest,
    result: GridNumericResult,
) -> dict[str, MatPlotAgentGridPackage]:
    """Build one mandatory MatPlotAgent package per Facet Grid cell.

    Every package receives the grid-wide min/max, so independently generated
    panels still have identical encoding and remain visually comparable.
    """
    output = Path(output_directory)
    packages: dict[str, MatPlotAgentGridPackage] = {}
    cells = {cell.cell_id: cell for cell in result.cells}
    for depth_bucket in request.depthBuckets:
        for time_bucket in request.timeBuckets:
            cell_id = f"{time_bucket.id}__{depth_bucket.id}"
            if cell_id not in cells:
                continue
            cell = cells[cell_id]
            cell_request = request.model_copy(
                update={
                    "variableId": depth_bucket.variableId or request.variableId,
                    "timeBuckets": [time_bucket],
                    "depthBuckets": [depth_bucket],
                }
            )
            cell_result = GridNumericResult(
                cells=(cell,),
                shared_minimum=result.shared_minimum,
                shared_maximum=result.shared_maximum,
                unit=result.unit,
                coordinate_reference=result.coordinate_reference,
                render_projection=result.render_projection,
                x_axis=result.x_axis,
                y_axis=result.y_axis,
            )
            packages[cell_id] = build_matplotagent_grid_package(
                output / "cells" / cell_id,
                cell_request,
                cell_result,
                cell_panel=True,
            )
    return packages


def build_matplotagent_grid_package(
    output_directory: str | Path,
    request: FacetGridRequest,
    result: GridNumericResult,
    *,
    cell_panel: bool = False,
) -> MatPlotAgentGridPackage:
    """Create one grid-level job package; never dispatch cells independently."""
    output = Path(output_directory)
    output.mkdir(parents=True, exist_ok=True)
    data_path = output / "grid_data.csv"
    contract_path = output / "grid_contract.json"
    prompt_path = output / "grid_prompt.txt"

    height, width = result.cells[0].values.shape
    with data_path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.writer(stream)
        writer.writerow(["cell_id", "x_index", "y_index", "value"])
        for cell in result.cells:
            for y_index in range(height):
                for x_index in range(width):
                    value = cell.values[y_index, x_index]
                    if np.isfinite(value):
                        writer.writerow(
                            [cell.cell_id, x_index, y_index, format(float(value), ".9g")]
                        )

    figure_width = 6 if cell_panel else 18
    figure_height = 4 if cell_panel else 9
    chart_type = _requested_chart_type(request)
    spatial_grid: dict[str, object] = {
        "width": width,
        "height": height,
        "xMinimum": 0,
        "xMaximum": width - 1,
        "yMinimum": 0,
        "yMaximum": height - 1,
    }
    if result.x_axis is not None and result.y_axis is not None:
        x_start = float(result.x_axis["start"])
        x_step = float(result.x_axis["step"])
        y_start = float(result.y_axis["start"])
        y_step = float(result.y_axis["step"])
        spatial_grid["georeference"] = {
            "coordinateReference": result.coordinate_reference,
            "renderProjection": result.render_projection,
            "x": {
                **result.x_axis,
                "minimum": x_start,
                "maximum": x_start + x_step * (width - 1),
            },
            "y": {
                **result.y_axis,
                "minimum": y_start,
                "maximum": y_start + y_step * (height - 1),
            },
        }

    contract = {
        "contractVersion": "1.0",
        "datasetId": request.datasetId,
        "variableId": request.variableId,
        "chartType": chart_type,
        "intent": {
            "rawText": request.rawIntent,
            "analyticTask": request.analyticTask,
        },
        "dimensionRoles": [
            assignment.model_dump() for assignment in request.dimensionRoles
        ],
        "grid": {
            "columns": [bucket.model_dump() for bucket in request.timeBuckets],
            "rows": [bucket.model_dump() for bucket in request.depthBuckets],
        },
        "expectedCellOrder": [cell.cell_id for cell in result.cells],
        "spatialGrid": spatial_grid,
        "layout": {
            "figureWidthInches": figure_width,
            "figureHeightInches": figure_height,
            "rightMargin": 0.98 if cell_panel else 0.88,
            "colorbarAxes": [0.91, 0.15, 0.015, 0.7],
            "showColorbar": not cell_panel,
        },
        "encoding": {
            "colorMap": request.colorMap,
            "scalePolicy": request.scalePolicy,
            "minimum": result.shared_minimum,
            "maximum": result.shared_maximum,
            "unit": result.unit,
            "missing": "transparent",
        },
        "requiredOutputs": {
            "image": "filename supplied by the outer MatPlotAgent generation prompt",
            "metadata": "chart_result.json",
            "layout": "one figure containing the complete facet grid",
        },
        "cellStatistics": [
            {
                "cellId": cell.cell_id,
                "variableId": cell.variable_id or request.variableId,
                "validFraction": cell.valid_fraction,
                "framesUsed": list(cell.frames_used),
                "depthIndices": list(cell.depth_indices),
            }
            for cell in result.cells
        ],
    }
    contract_path.write_text(
        json.dumps(contract, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )

    distribution_chart = chart_type in {
        "bar_chart", "histogram", "line_chart", "pie_chart",
        "box_plot", "violin_plot",
    }
    colorbar_requirement = (
        "- Do not draw a colorbar. Use the declared shared value range and a "
        "consistent style for every cell."
        if distribution_chart
        else
        "- Do not draw a per-cell colorbar. The Unity Grid owns one shared "
        "colorbar for all streamed cells."
        if cell_panel
        else (
            "- Draw one visible shared colorbar for the complete grid and label it\n"
            f"  {result.unit}. Do not create independent per-cell colorbars."
        )
    )
    if chart_type in {"bar_chart", "histogram"}:
        chart_name = "BAR CHART" if chart_type == "bar_chart" else "HISTOGRAM"
        chart_requirement = f"""- Draw a {chart_name}, not a heatmap or scatter plot.
- The requested chart type is {chart_name}; do not substitute another chart form.
- For each cell, exclude missing values and count `value` in exactly 12 equal-width
  bins spanning the shared range [{result.shared_minimum}, {result.shared_maximum}].
- Read JSON only with `open('grid_contract.json', encoding='utf-8')`.
- Use this reliable NumPy pattern, not `pd.cut`:
  `edges = np.linspace(shared_min, shared_max, 13)`
  `counts, _ = np.histogram(values, bins=edges)`
  `centers = (edges[:-1] + edges[1:]) / 2`
  `bar_colors = plt.get_cmap(contract['encoding']['colorMap'])(np.linspace(0.2, 0.85, 12))`
  `ax.bar(centers, counts, width=np.diff(edges) * 0.88, color=bar_colors)`
  A colormap name such as `viridis` is not itself a valid Matplotlib color.
- Set the x-axis to the exact shared range, label it `{result.unit}`, label the
  y-axis `Count`, and keep labels readable.
- Histogram bin counts are the requested aggregation; do not otherwise normalize,
  smooth, clip, interpolate, or alter values."""
        spatial_requirement = """- The CSV columns are cell_id, x_index, y_index, and value.
  For this distribution chart, x_index and y_index identify provenance but are not
  chart axes. Read every valid `value` row belonging to the requested cell."""
    elif chart_type == "scatter_plot":
        chart_requirement = f"""- Draw a SCATTER PLOT, not a heatmap or line chart.
- Plot x_index on x and y_index on y; encode `value` with point color using
  contract['encoding']['colorMap'], vmin={result.shared_minimum}, and
  vmax={result.shared_maximum}. Use small rasterized marks with no outlines.
- Keep the spatial aspect equal, label both index axes, and exclude missing values.
- Do not aggregate x/y positions or replace missing values with zero."""
        spatial_requirement = """- The CSV columns are cell_id, x_index, y_index, and value.
  Pass the complete valid columns to `ax.scatter`; do not use Python row loops."""
    elif chart_type == "line_chart":
        chart_requirement = f"""- Draw a LINE CHART spatial profile, not a heatmap.
- Exclude missing values, group by x_index, and compute the arithmetic mean of
  `value` across y_index for each x_index. Sort by x_index before plotting.
- Plot x_index on x and mean {result.unit} on y. Use the exact shared value range
  [{result.shared_minimum}, {result.shared_maximum}] on the y-axis.
- Draw one readable line with restrained markers; do not smooth or interpolate."""
        spatial_requirement = """- The CSV columns are cell_id, x_index, y_index, and value.
  Use vectorized pandas groupby; do not loop over individual pixels."""
    elif chart_type == "pie_chart":
        chart_requirement = f"""- Draw a PIE CHART of the value distribution, not a heatmap.
- Exclude missing values and use exactly 6 equal-width bins across the shared range
  [{result.shared_minimum}, {result.shared_maximum}]. Compute counts with
  `counts, edges = np.histogram(values, bins=np.linspace(shared_min, shared_max, 7))`.
- Remove zero-count wedges only after computing all six bins. Label remaining wedges
  with concise numeric ranges in a right-side legend and use colors sampled from
  contract['encoding']['colorMap']. Do not place range labels around the pie.
- Show a percentage inside a wedge only when it is at least 3 percent. Use an
  `autopct` callable that returns an empty string below that threshold. Reserve the
  right side with `fig.subplots_adjust(right=0.72)`, keep the legend font compact,
  and keep the pie circular.
- Never pass raw scientific values directly as pie wedge sizes."""
        spatial_requirement = """- The CSV columns are cell_id, x_index, y_index, and value.
  x_index and y_index are provenance only for this distribution chart."""
    elif chart_type == "box_plot":
        chart_requirement = f"""- Draw one vertical BOX PLOT of all valid `value` samples.
- Use `ax.boxplot(values, orientation='vertical', showfliers=True)` and apply the exact shared
  y-axis range [{result.shared_minimum}, {result.shared_maximum}].
- Label the y-axis `{result.unit}` and the x tick `Distribution`. Do not normalize,
  aggregate, smooth, or fabricate comparison groups."""
        spatial_requirement = """- The CSV columns are cell_id, x_index, y_index, and value.
  x_index and y_index are provenance only for this distribution chart."""
    elif chart_type == "violin_plot":
        chart_requirement = f"""- Draw one vertical VIOLIN PLOT of all valid `value` samples.
- Use `ax.violinplot(values, showmeans=True, showmedians=True, showextrema=True)`
  and apply the exact shared y-axis range [{result.shared_minimum}, {result.shared_maximum}].
- Label the y-axis `{result.unit}` and the x tick `Distribution`. Do not create
  artificial groups or change the values."""
        spatial_requirement = """- The CSV columns are cell_id, x_index, y_index, and value.
  x_index and y_index are provenance only for this distribution chart."""
    else:
        chart_requirement = """- Draw the requested horizontal spatial heatmap with `imshow`.
- Do not normalize, clip, aggregate, smooth, interpolate, or alter values.
- Missing coordinates stay transparent; do not replace them with zero."""
        reshape_requirement = f"""- The spatial grid is exactly width={width}, height={height}. Read these values
  from grid_contract.json `spatialGrid`; do not infer X/Y dimensions from time
  or depth bucket indices. Use the full array ranges x=0..{width - 1} and
  y=0..{height - 1} for every cell.
- Use vectorized pandas/numpy reshaping such as pivot/pivot_table. Never use
  iterrows, list.index, or Python loops over individual pixels.
- After pivoting a cell, construct its complete array exactly by reindexing both
  axes and converting once, e.g.
  `pivoted.reindex(index=full_y, columns=full_x).to_numpy(dtype=float)`.
  Never scatter-assign a 2D pivot with `arr[y_indices, x_indices] = ...`;
  differently sized 1D index arrays do not form the required Cartesian grid."""
        if result.x_axis is not None and result.y_axis is not None:
            coordinate_requirement = """
- This grid is georeferenced. Read `spatialGrid.georeference` from the contract.
  Keep the source array unchanged, but render it with `imshow(..., origin='lower',
  extent=[x.minimum-x.step/2, x.maximum+x.step/2,
          y.minimum-y.step/2, y.maximum+y.step/2])`.
- Label the axes `Longitude (degrees east)` and `Latitude (degrees north)`.
- Axis tick positions must also be geographic values. Use at most six positions
  from `np.linspace(x.minimum, x.maximum, 6)` and the equivalent y range.
  Never pass x_index/y_index values (0..width-1 / 0..height-1) to `set_xticks`
  or `set_yticks` after applying the geographic extent; doing so collapses the
  map into a tiny point. State the coordinateReference in a compact subtitle.
- Do not reproject, resample, transpose, flip, or invent coordinates."""
        else:
            coordinate_requirement = """
- Use x_index and y_index as ordinal coordinates and label them as indices.
- Use imshow with array coordinates. Show no more than 6 readable ticks per axis;
  never create one tick label for every x_index or y_index.
- Build tick positions as an integer NumPy array and index coordinate labels
  through `np.asarray(full_x)[x_ticks]` / `np.asarray(full_y)[y_ticks]`.
  A Python range or list cannot be indexed by a NumPy array."""
        spatial_requirement = f"""{reshape_requirement}
{coordinate_requirement}"""
    layout_requirement = (
        "- Use a compact 6x4 inch figure and let the chart fill the panel. "
        "Do not reserve a colorbar margin."
        if cell_panel
        else (
            "- Use a wide 18x9 inch figure for the VR panel. Reserve the right "
            "margin with\n"
            "  `fig.subplots_adjust(right=0.88, wspace=0.25, hspace=0.40)`, "
            "create the\n"
            "  shared colorbar axes with "
            "`fig.add_axes([0.91, 0.15, 0.015, 0.7])`, and do\n"
            "  not call tight_layout/constrained_layout after adding that colorbar."
        )
    )
    prompt = f"""Create the complete S4D Facet Grid from grid_data.csv.

This is one grid-level MatPlotAgent job, not independent cell jobs.
Read grid_contract.json and obey it exactly.
The user's requested analytic task is `{request.analyticTask}`.
Their original instruction is:
{request.rawIntent or "Compare the distribution across all cells."}

Hard requirements:
- Open every JSON/text file explicitly with `encoding='utf-8'`; never rely on the
  Windows default encoding.
- Use one readable typography scale throughout the figure: subplot titles at
  least 14 pt, axis labels at least 13 pt, tick labels at least 11 pt, and
  legends/colorbar tick labels at least 11 pt. Use semibold subplot titles and
  `Poppins-Bold.ttf` when supplied in the workspace (register it through
  Matplotlib's font manager); save the final PNG at 200 dpi. Do not shrink text
  merely to fit decoration.
- Render every requested cell in one figure using the declared row/column order.
- Filter `grid_data.csv` only with these exact complete cell_id values:
  {json.dumps([cell.cell_id for cell in result.cells], ensure_ascii=False)}.
  Do not reconstruct, append to, shorten, or otherwise modify these IDs. After
  filtering, fail explicitly if any requested cell has zero valid rows.
- Use the exact shared range
  [{result.shared_minimum}, {result.shared_maximum}] {result.unit}.
{colorbar_requirement}
{layout_requirement}
{chart_requirement}
{spatial_requirement}
- Save the image to the exact filename requested by the outer MatPlotAgent
  generation prompt. Do not hardcode final.png, initial.png, or a candidate name.
- Save chart_result.json containing the used cell order, colormap, minimum,
  maximum, unit, figureWidth={figure_width}, figureHeight={figure_height}, spatialWidth={width},
  spatialHeight={height}, and the requested output filename.
- Do not claim physical units, dates, significance, causality, or verification.
"""
    prompt_path.write_text(prompt, encoding="utf-8")
    return MatPlotAgentGridPackage(data_path, contract_path, prompt_path)
