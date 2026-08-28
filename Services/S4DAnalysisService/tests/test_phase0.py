from __future__ import annotations

import json
import shutil
import unittest
import uuid
from pathlib import Path

import numpy as np
from PIL import Image

from Services.MatPlotAgent.local_run import (
    contract_execution_warnings,
    preflight_code_warnings,
    render_contract_fallback,
)
from Services.S4DAnalysisService.matplot_contract import (
    build_matplotagent_cell_packages,
    build_matplotagent_grid_package,
)
from Services.S4DAnalysisService.digest import build_deterministic_digest
from Services.S4DAnalysisService.models import FacetGridRequest, VolumeManifest
from Services.S4DAnalysisService.app import _compose_cell_atlas, resolve_intent_text
from Services.S4DAnalysisService.models import IntentResolutionRequest
from Services.S4DAnalysisService.raw_reader import (
    RawVolumeReader,
    validate_manifest_files,
)
from Services.S4DAnalysisService.preview_renderer import render_preview_atlas
from Services.S4DAnalysisService.snapshot_store import SnapshotStore


class PhaseZeroTests(unittest.TestCase):
    def setUp(self) -> None:
        runtime_test_root = (
            Path(__file__).resolve().parents[3] / ".runtime" / "s4d-analysis-tests"
        )
        runtime_test_root.mkdir(parents=True, exist_ok=True)
        self.root = runtime_test_root / uuid.uuid4().hex
        self.root.mkdir()
        frames = []
        arrays = [
            np.array(
                [
                    [[0, 10], [20, 30]],
                    [[0, 30], [40, 50]],
                ],
                dtype=np.uint8,
            ),
            np.array(
                [
                    [[0, 20], [30, 40]],
                    [[0, 40], [50, 60]],
                ],
                dtype=np.uint8,
            ),
        ]
        for index, array in enumerate(arrays):
            path = self.root / f"v_t{index}.raw"
            array.tofile(path)
            frames.append(
                {
                    "frameId": f"v_t{index}",
                    "timeIndex": index,
                    "temporalMeaning": "instantaneous",
                    "path": path.name,
                    "expectedBytes": 8,
                    "sha256": None,
                }
            )
        self.manifest_path = self.root / "manifest.json"
        self.manifest_path.write_text(
            json.dumps(
                {
                    "schemaVersion": "1.0",
                    "datasetId": "synthetic",
                    "datasetVersion": "1",
                    "dimensions": {"x": 2, "y": 2, "z": 2},
                    "storageOrder": "ZYX",
                    "defaultVoxelType": "uint8",
                    "coordinates": {
                        "x": {"kind": "ordinal_index", "unit": "x_index"},
                        "y": {"kind": "ordinal_index", "unit": "y_index"},
                        "depth": {
                            "kind": "ordinal_index",
                            "unit": "depth_index",
                            "positive": "down",
                            "excludedIndices": [],
                        },
                    },
                    "variables": {
                        "v": {
                            "displayName": "V",
                            "unit": "encoded_intensity",
                            "valueSemantics": "encoded_intensity",
                            "voxelType": "uint8",
                            "scale": 1.0,
                            "offset": 0.0,
                            "missingRawValues": [0],
                            "frames": frames,
                        },
                        "w": {
                            "displayName": "W",
                            "unit": "encoded_intensity",
                            "valueSemantics": "encoded_intensity",
                            "voxelType": "uint8",
                            "scale": 2.0,
                            "offset": 0.0,
                            "missingRawValues": [0],
                            "frames": frames,
                        }
                    },
                    "assumptions": [],
                }
            ),
            encoding="utf-8",
        )

    def tearDown(self) -> None:
        shutil.rmtree(self.root, ignore_errors=True)

    def request(self) -> FacetGridRequest:
        return FacetGridRequest.model_validate(
            {
                "datasetId": "synthetic",
                "variableId": "v",
                "timeBuckets": [
                    {"id": "both", "label": "Both", "indices": [0, 1]}
                ],
                "depthBuckets": [
                    {"id": "all", "label": "All", "indices": [0, 1]}
                ],
            }
        )

    def test_manifest_and_exact_file_sizes_validate(self) -> None:
        manifest = VolumeManifest.load(self.manifest_path)
        self.assertEqual("synthetic", manifest.datasetId)
        report = validate_manifest_files(self.manifest_path)
        self.assertTrue(report.valid, report.errors)
        self.assertEqual(4, report.checkedFiles)

        with (self.root / "v_t1.raw").open("ab") as stream:
            stream.write(b"\0")
        report = validate_manifest_files(self.manifest_path)
        self.assertFalse(report.valid)
        self.assertIn("expected 8 bytes, found 9", report.errors[0])

    def test_at_scale_request_accepts_nine_by_nine_drill_grid(self) -> None:
        time_buckets = [
            {"id": f"time_{index}", "label": f"Time {index}", "indices": [0]}
            for index in range(9)
        ]
        depth_buckets = [
            {"id": f"depth_{index}", "label": f"Depth {index}", "indices": [0]}
            for index in range(9)
        ]
        request = FacetGridRequest.model_validate(
            {
                "datasetId": "synthetic",
                "variableId": "v",
                "timeBuckets": time_buckets,
                "depthBuckets": depth_buckets,
                "requestedCellIds": [
                    f"time_{time}__depth_{depth}"
                    for depth in range(9)
                    for time in range(9)
                ],
            }
        )
        self.assertEqual(9, len(request.timeBuckets))
        self.assertEqual(9, len(request.depthBuckets))
        self.assertEqual(81, len(request.requestedCellIds))

    def test_grid_aggregation_excludes_missing_and_shares_scale(self) -> None:
        result = RawVolumeReader(self.manifest_path).materialize_grid(self.request())
        self.assertEqual(1, len(result.cells))
        np.testing.assert_allclose(
            result.cells[0].values,
            np.array([[np.nan, 25], [35, 45]], dtype=np.float32),
            equal_nan=True,
        )
        self.assertAlmostEqual(0.75, result.cells[0].valid_fraction)
        self.assertEqual(25, result.shared_minimum)
        self.assertEqual(45, result.shared_maximum)

    def test_variable_faceting_uses_each_row_bucket_variable(self) -> None:
        request = FacetGridRequest.model_validate(
            {
                "datasetId": "synthetic",
                "variableId": "v",
                "timeBuckets": [
                    {"id": "t0", "label": "T0", "indices": [0]}
                ],
                "depthBuckets": [
                    {
                        "id": "variable_v",
                        "label": "V",
                        "indices": [1],
                        "variableId": "v",
                    },
                    {
                        "id": "variable_w",
                        "label": "W",
                        "indices": [1],
                        "variableId": "w",
                    },
                ],
                "dimensionRoles": [
                    {"dimension": "time", "role": "faceted"},
                    {"dimension": "depth", "role": "fixed"},
                    {"dimension": "horizontal", "role": "mapped"},
                    {"dimension": "variable", "role": "faceted"},
                ],
            }
        )
        result = RawVolumeReader(self.manifest_path).materialize_grid(request)
        self.assertEqual(["v", "w"], [cell.variable_id for cell in result.cells])
        np.testing.assert_allclose(
            result.cells[1].values,
            result.cells[0].values * 2.0,
            equal_nan=True,
        )

    def test_digest_compares_immutable_cell_statistics(self) -> None:
        result = RawVolumeReader(self.manifest_path).materialize_grid(self.request())
        metadata = SnapshotStore(self.root / "digest-snapshots").create(
            "digest-snapshot",
            self.manifest_path,
            self.request(),
            result,
            "s4d-job",
            "matplot-job",
        )
        digest = build_deterministic_digest(metadata)
        self.assertEqual("DISTRIBUTION DIGEST", digest["headline"])
        self.assertEqual("both__all", digest["highestCell"])
        self.assertIn("highest mean", digest["summary"])
        self.assertEqual(4, len(digest["findings"]))
        self.assertEqual(1, digest["validCellCount"])
        self.assertEqual(0, digest["excludedCellCount"])

    def test_digest_excludes_empty_zero_placeholders(self) -> None:
        snapshot = {
            "request": {"analyticTask": "find_anomalies"},
            "cells": [
                {
                    "cellId": "empty",
                    "minimum": 0.0,
                    "mean": 0.0,
                    "maximum": 0.0,
                    "validFraction": 0.0,
                    "validCount": 0,
                    "hasData": False,
                },
                {
                    "cellId": "supported",
                    "minimum": 4.0,
                    "mean": 5.0,
                    "maximum": 7.0,
                    "validFraction": 0.75,
                    "validCount": 3,
                    "hasData": True,
                },
            ],
        }
        digest = build_deterministic_digest(snapshot)
        self.assertEqual("supported", digest["highestCell"])
        self.assertEqual("supported", digest["lowestCell"])
        self.assertEqual(1, digest["validCellCount"])
        self.assertEqual(1, digest["excludedCellCount"])

    def test_preview_atlas_uses_materialized_grid(self) -> None:
        result = RawVolumeReader(self.manifest_path).materialize_grid(self.request())
        png = render_preview_atlas(result, column_count=1, row_count=1)
        self.assertTrue(png.startswith(b"\x89PNG\r\n\x1a\n"))
        self.assertIn(b"IHDR", png)
        self.assertTrue(png.endswith(b"IEND\xaeB`\x82"))

    def test_matplot_atlas_preserves_mixed_cell_aspect_ratios(self) -> None:
        images = [
            Image.new("RGB", (1200, 180), "red"),
            Image.new("RGB", (180, 1200), "green"),
            Image.new("RGB", (640, 420), "blue"),
        ] * 3
        atlas = _compose_cell_atlas(
            images,
            columns=3,
            rows=3,
            cell_size=(200, 140),
            gutter=4,
        )
        self.assertEqual((616, 436), atlas.size)
        self.assertEqual((0, 214, 230), atlas.getpixel((104, 4)))
        self.assertEqual((0, 214, 230), atlas.getpixel((308, 4)))

    def test_ground_volume_preserves_depth_and_aggregates_time(self) -> None:
        reader = RawVolumeReader(self.manifest_path)
        volume = reader.materialize_aggregate_volume(
            self.request(), "both__all"
        )
        self.assertEqual((2, 2, 2), volume.values.shape)
        np.testing.assert_allclose(
            volume.values,
            np.array(
                [
                    [[np.nan, 15], [25, 35]],
                    [[np.nan, 35], [45, 55]],
                ],
                dtype=np.float32,
            ),
            equal_nan=True,
        )
        self.assertEqual((0, 1), volume.frames_used)
        self.assertEqual((0, 1), volume.depth_indices)

    def test_snapshot_is_durable_and_ground_uses_its_request(self) -> None:
        request = self.request()
        result = RawVolumeReader(self.manifest_path).materialize_grid(request)
        store = SnapshotStore(self.root / "snapshots")
        metadata = store.create(
            "snapshot-1",
            self.manifest_path,
            request,
            result,
            "local-job",
            "remote-job",
        )
        self.assertEqual("pending", metadata["status"])
        self.assertEqual("synthetic", metadata["datasetId"])
        self.assertEqual(1, len(metadata["cells"]))
        self.assertTrue(metadata["cells"][0]["hasData"])
        self.assertGreater(metadata["cells"][0]["validCount"], 0)
        store.set_status("snapshot-1", "completed")
        volume, cell = store.aggregate_volume("snapshot-1", "both__all")
        self.assertEqual((2, 2, 2), volume.shape)
        self.assertEqual([0, 1], cell["framesUsed"])
        self.assertTrue(
            (self.root / "snapshots" / "snapshot-1" / "ground" /
             "both__all.npy").is_file()
        )
        # The Ground volume preserves depth, while its valid-value mean over
        # depth must reproduce the immutable 2D interval-mean cell exactly.
        expected_cell = result.cells[0].values
        grounded_valid = np.isfinite(volume)
        grounded_sum = np.where(grounded_valid, volume, 0.0).sum(axis=0)
        grounded_count = grounded_valid.sum(axis=0)
        grounded_cell = np.full(grounded_count.shape, np.nan, dtype=np.float64)
        np.divide(
            grounded_sum,
            grounded_count,
            out=grounded_cell,
            where=grounded_count > 0,
        )
        np.testing.assert_allclose(grounded_cell, expected_cell, equal_nan=True)
        finite = grounded_cell[np.isfinite(grounded_cell)]
        self.assertAlmostEqual(cell["minimum"], float(finite.min()))
        self.assertAlmostEqual(cell["mean"], float(finite.mean()))
        self.assertAlmostEqual(cell["maximum"], float(finite.max()))

    def test_matplotagent_package_is_grid_level_and_fixed_scale(self) -> None:
        request = FacetGridRequest.model_validate(
            {
                **self.request().model_dump(),
                "rawIntent": "Find unusual hot spots.",
                "analyticTask": "find_anomalies",
                "dimensionRoles": [
                    {"dimension": "time", "role": "faceted"},
                    {"dimension": "depth", "role": "faceted"},
                    {"dimension": "horizontal", "role": "mapped"},
                    {"dimension": "variable", "role": "fixed"},
                ],
            }
        )
        result = RawVolumeReader(self.manifest_path).materialize_grid(request)
        package = build_matplotagent_grid_package(
            self.root / "job", request, result
        )
        contract = json.loads(package.contract_json.read_text(encoding="utf-8"))
        self.assertEqual("shared_across_grid", contract["encoding"]["scalePolicy"])
        self.assertEqual(25, contract["encoding"]["minimum"])
        self.assertEqual({"width": 2, "height": 2, "xMinimum": 0,
                          "xMaximum": 1, "yMinimum": 0, "yMaximum": 1},
                         contract["spatialGrid"])
        self.assertEqual(18, contract["layout"]["figureWidthInches"])
        self.assertEqual(9, contract["layout"]["figureHeightInches"])
        self.assertEqual("find_anomalies", contract["intent"]["analyticTask"])
        self.assertEqual("mapped", contract["dimensionRoles"][2]["role"])
        prompt = package.prompt_txt.read_text()
        self.assertIn("Find unusual hot spots.", prompt)
        self.assertIn("one grid-level MatPlotAgent job", prompt)
        self.assertIn("Never use\n  iterrows", prompt)
        self.assertIn("pivoted.reindex(index=full_y", prompt)
        self.assertIn("np.asarray(full_x)[x_ticks]", prompt)
        self.assertIn("width=2, height=2", prompt)
        self.assertIn("spatialWidth=2", prompt)
        self.assertIn("wide 18x9 inch figure", prompt)
        self.assertIn("fig.add_axes([0.91, 0.15, 0.015, 0.7])", prompt)
        self.assertIn("one visible shared colorbar", prompt)
        self.assertTrue(package.data_csv.is_file())

    def test_cell_packages_keep_one_shared_scale(self) -> None:
        request = FacetGridRequest.model_validate(
            {
                "datasetId": "synthetic",
                "variableId": "v",
                "timeBuckets": [
                    {"id": "t0", "label": "T0", "indices": [0]},
                    {"id": "t1", "label": "T1", "indices": [1]},
                ],
                "depthBuckets": [
                    {"id": "z0", "label": "Z0", "indices": [0]},
                    {"id": "z1", "label": "Z1", "indices": [1]},
                ],
            }
        )
        result = RawVolumeReader(self.manifest_path).materialize_grid(request)
        packages = build_matplotagent_cell_packages(
            self.root / "cell-job", request, result
        )
        self.assertEqual(
            {"t0__z0", "t1__z0", "t0__z1", "t1__z1"}, set(packages)
        )
        for package in packages.values():
            contract = json.loads(
                package.contract_json.read_text(encoding="utf-8")
            )
            self.assertEqual(1, len(contract["grid"]["columns"]))
            self.assertEqual(1, len(contract["grid"]["rows"]))
            self.assertFalse(contract["layout"]["showColorbar"])
            self.assertEqual(6, contract["layout"]["figureWidthInches"])
            self.assertEqual(result.shared_minimum, contract["encoding"]["minimum"])
            self.assertEqual(result.shared_maximum, contract["encoding"]["maximum"])
            self.assertIn(
                "Do not draw a per-cell colorbar",
                package.prompt_txt.read_text(encoding="utf-8"),
            )

    def test_bar_chart_intent_changes_the_matplot_contract(self) -> None:
        request = FacetGridRequest.model_validate(
            {
                **self.request().model_dump(),
                "rawIntent": "Generate a bar chart for each Time x Depth cell.",
            }
        )
        result = RawVolumeReader(self.manifest_path).materialize_grid(request)
        packages = build_matplotagent_cell_packages(
            self.root / "bar-cell-job", request, result
        )
        self.assertTrue(packages)
        for package in packages.values():
            contract = json.loads(package.contract_json.read_text(encoding="utf-8"))
            prompt = package.prompt_txt.read_text(encoding="utf-8")
            self.assertEqual("bar_chart", contract["chartType"])
            self.assertIn("Draw a BAR CHART", prompt)
            self.assertIn("exactly 12 equal-width", prompt)
            self.assertNotIn("Use imshow", prompt)

    def test_spoken_chart_types_receive_distinct_safe_contracts(self) -> None:
        cases = {
            "Show a histogram for every cell.": ("histogram", "HISTOGRAM"),
            "生成散点图": ("scatter_plot", "SCATTER PLOT"),
            "生成折线图": ("line_chart", "LINE CHART"),
            "生成饼状图": ("pie_chart", "PIE CHART"),
            "生成箱线图": ("box_plot", "BOX PLOT"),
            "生成小提琴图": ("violin_plot", "VIOLIN PLOT"),
            "生成热力图": ("horizontal_heatmap", "imshow"),
        }
        result = RawVolumeReader(self.manifest_path).materialize_grid(self.request())
        for index, (spoken, expected) in enumerate(cases.items()):
            chart_type, required_prompt = expected
            request = FacetGridRequest.model_validate(
                {**self.request().model_dump(), "rawIntent": spoken}
            )
            packages = build_matplotagent_cell_packages(
                self.root / f"spoken-chart-{index}", request, result
            )
            package = next(iter(packages.values()))
            contract = json.loads(package.contract_json.read_text(encoding="utf-8"))
            prompt = package.prompt_txt.read_text(encoding="utf-8")
            self.assertEqual(chart_type, contract["chartType"])
            self.assertIn(required_prompt, prompt)

    def test_supported_spoken_charts_have_a_non_llm_failure_fallback(self) -> None:
        spoken_requests = (
            "Generate a heatmap", "Generate a bar chart", "Generate a histogram",
            "Generate a scatter plot", "Generate a line chart", "Generate a pie chart",
            "Generate a box plot", "Generate a violin plot",
        )
        result = RawVolumeReader(self.manifest_path).materialize_grid(self.request())
        for index, spoken in enumerate(spoken_requests):
            request = FacetGridRequest.model_validate(
                {**self.request().model_dump(), "rawIntent": spoken}
            )
            packages = build_matplotagent_cell_packages(
                self.root / f"fallback-chart-{index}", request, result
            )
            package = next(iter(packages.values()))
            output = f"fallback-{index}.png"
            self.assertTrue(render_contract_fallback(package.data_csv.parent, output))
            with Image.open(package.data_csv.parent / output) as image:
                self.assertGreater(image.width, 100)
                self.assertGreater(image.height, 100)

    def test_partial_grid_keeps_requested_shared_scale(self) -> None:
        request = FacetGridRequest.model_validate(
            {
                "datasetId": "synthetic",
                "variableId": "v",
                "timeBuckets": [
                    {"id": "t0", "label": "T0", "indices": [0]},
                    {"id": "t1", "label": "T1", "indices": [1]},
                ],
                "depthBuckets": [
                    {"id": "z0", "label": "Z0", "indices": [0]},
                    {"id": "z1", "label": "Z1", "indices": [1]},
                ],
                "requestedCellIds": ["t1__z0"],
                "hasSharedScaleOverride": True,
                "sharedScaleMinimum": -10.0,
                "sharedScaleMaximum": 100.0,
            }
        )
        result = RawVolumeReader(self.manifest_path).materialize_grid(request)
        self.assertEqual(["t1__z0"], [cell.cell_id for cell in result.cells])
        self.assertEqual(-10.0, result.shared_minimum)
        self.assertEqual(100.0, result.shared_maximum)
        packages = build_matplotagent_cell_packages(
            self.root / "partial-job", request, result
        )
        self.assertEqual(["t1__z0"], list(packages))

    def test_matplotagent_preflight_rejects_pixel_loops(self) -> None:
        unsafe = """
for y_idx in pivoted.index:
    for x_idx in pivoted.columns:
        grid[y_idx, x_idx] = pivoted.loc[y_idx, x_idx]
"""
        self.assertTrue(preflight_code_warnings(unsafe, "initial.png"))
        self.assertTrue(
            preflight_code_warnings(
                "for y_idx in y_indices:\n    for x_idx in x_indices:\n        pass",
                "initial.png",
            )
        )
        self.assertTrue(
            preflight_code_warnings(
                "arr[y_indices, x_indices] = pivoted.values",
                "initial.png",
            )
        )
        self.assertTrue(
            preflight_code_warnings(
                "labels = full_x_indices[x_ticks]",
                "initial.png",
            )
        )
        self.assertTrue(
            preflight_code_warnings(
                "fig, axes = plt.subplots(len(rows), len(columns), "
                "squeeze=False)\n"
                "if len(rows) == 1 and len(columns) == 1:\n"
                "    axes = np.array([[axes]])\n",
                "initial.png",
            )
        )
        self.assertTrue(
            preflight_code_warnings(
                "fig.suptitle(contract['intent']['rawText'])\n"
                "fig.savefig('initial.png', bbox_inches='tight')",
                "initial.png",
            )
        )
        self.assertTrue(
            preflight_code_warnings(
                "fig.colorbar(image, cax=colorbar_axis)",
                "initial.png",
            )
        )
        self.assertTrue(
            preflight_code_warnings(
                "with open('grid_contract.json', 'r') as stream:\n"
                "    contract = json.load(stream)",
                "initial.png",
            )
        )
        self.assertTrue(
            preflight_code_warnings(
                "counts, _ = pd.cut(values, bins=edges).value_counts(sort=False)",
                "initial.png",
            )
        )
        self.assertTrue(
            preflight_code_warnings(
                "ax.bar(centers, counts, color=colormap)",
                "initial.png",
            )
        )
        safe = """
grid = pivoted.reindex(index=y_indices, columns=x_indices).to_numpy()
fig.savefig('initial.png')
"""
        self.assertFalse(preflight_code_warnings(safe, "initial.png"))

    def test_contract_execution_rejects_wrong_cell_filter(self) -> None:
        workspace = self.root / "contract-execution-wrong-cell"
        workspace.mkdir(parents=True, exist_ok=True)
        (workspace / "grid_contract.json").write_text(
            json.dumps({
                "expectedCellOrder": ["before__depth_fixed_2"],
                "grid": {
                    "columns": [{"id": "before"}],
                    "rows": [{"id": "depth_fixed_2"}],
                },
            }),
            encoding="utf-8",
        )
        (workspace / "grid_data.csv").write_text(
            "cell_id,x_index,y_index,value\n"
            "before__depth_fixed_2,0,0,1.25\n",
            encoding="utf-8",
        )
        (workspace / "chart_result.json").write_text(
            json.dumps({"usedCellOrder": ["before__depth_fixed_2_2"]}),
            encoding="utf-8",
        )
        warnings = contract_execution_warnings(workspace)
        self.assertTrue(any("filtered cell_id incorrectly" in item
                            for item in warnings))

    def test_contract_execution_accepts_exact_nonempty_cells(self) -> None:
        workspace = self.root / "contract-execution-valid"
        workspace.mkdir(parents=True, exist_ok=True)
        expected = ["before__surface", "during__surface", "after__surface"]
        (workspace / "grid_contract.json").write_text(
            json.dumps({
                "expectedCellOrder": expected,
                "grid": {
                    "columns": [{"id": item.split("__")[0]}
                                for item in expected],
                    "rows": [{"id": "surface"}],
                },
            }),
            encoding="utf-8",
        )
        (workspace / "grid_data.csv").write_text(
            "cell_id,x_index,y_index,value\n" +
            "\n".join(f"{cell_id},0,0,1.0" for cell_id in expected) + "\n",
            encoding="utf-8",
        )
        (workspace / "chart_result.json").write_text(
            json.dumps({"usedCellOrder": expected}),
            encoding="utf-8",
        )
        self.assertEqual([], contract_execution_warnings(workspace))

    def test_intent_resolution_is_structured_and_marks_fallback(self) -> None:
        anomaly = resolve_intent_text(
            IntentResolutionRequest(
                text="找出每个区域里的异常热点",
                variableId="v",
                variableDisplayName="V",
                unit="encoded_intensity",
            )
        )
        self.assertEqual("find_anomalies", anomaly.analyticTask)
        self.assertFalse(anomaly.usedFallback)
        self.assertIn("V", anomaly.focus)

        mixed_anomaly = resolve_intent_text(
            IntentResolutionRequest(
                text=(
                    "Show where the strongest late-period hotspots occur "
                    "and compare them across depth."
                )
            )
        )
        self.assertEqual("find_anomalies", mixed_anomaly.analyticTask)
        self.assertFalse(mixed_anomaly.usedFallback)

        chinese_trend = resolve_intent_text(
            IntentResolutionRequest(
                text="比较前中后三段随时间的变化趋势，并说明表层和深层是否不同"
            )
        )
        self.assertEqual("characterize_trend", chinese_trend.analyticTask)
        self.assertFalse(chinese_trend.usedFallback)

        fallback = resolve_intent_text(
            IntentResolutionRequest(text="请帮我看看这些九宫格")
        )
        self.assertEqual(
            "characterize_distribution", fallback.analyticTask
        )
        self.assertTrue(fallback.usedFallback)

        empty = resolve_intent_text(IntentResolutionRequest())
        self.assertEqual("characterize_distribution", empty.analyticTask)
        self.assertTrue(empty.usedFallback)


if __name__ == "__main__":
    unittest.main()
