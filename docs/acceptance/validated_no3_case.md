# Validated NO3 Analysis Case

## Research question

Where and when does NO3 encoded intensity change most across the selected Time and Depth regions?

## Reproducible setup

- Dataset: `hong_kong_ocean_encoded_v1`
- Immutable snapshot: `67136421aff34ceb8e8c43689311b9f5`
- Variable: `NO3`
- Unit: `encoded_intensity`
- Time: Before 0-9, During 10-19, After 20-29
- Depth: Surface 0-29, Middle 30-60, Deep 61-90
- Aggregation: missing values excluded; equal-frame/equal-layer arithmetic mean

## Evidence-backed findings

- Surface mean changes from 11.9406 to 10.8613 (-9.0%).
- Middle mean changes from 6.7480 to 5.4813 (-18.8%).
- Deep mean changes from 12.5630 to 8.6376 (-31.2%).
- The highest absolute mean is `before__deep`; the lowest is `after__middle`; the widest range is `after__surface`.
- Deep cells have only about 3.37% valid coverage, so their large decline is a lead to inspect, not a strong standalone conclusion.
- Surface cells have about 38.92% coverage and therefore provide the most defensible temporal comparison in this selected footprint.

## Conclusion

The selected NO3 encoded signal declines from Before to After at every depth bucket. The strongest relative decline appears in Deep, but its sparse coverage makes it uncertain. The more defensible result is a smaller, consistent decline at Surface, while Surface also contains the widest local range and should be inspected spatially for concentrated anomalies.

## Ground verification

Ground reconstruction is covered by the automated synthetic closure test; run this command with `--verify-ground` for a real-source rebuild.

## MatPlot intent acceptance

- `Generate a heatmap` -> `horizontal_heatmap`: PASS
- `Generate a bar chart` -> `bar_chart`: PASS
- `Show a histogram` -> `histogram`: PASS
- `Generate a scatter plot` -> `scatter_plot`: PASS
- `Generate a line chart` -> `line_chart`: PASS
- `Generate a pie chart` -> `pie_chart`: PASS
- `Generate a box plot` -> `box_plot`: PASS
- `Generate a violin plot` -> `violin_plot`: PASS

## Interpretation boundary

These values are `encoded_intensity`, not calibrated chemical concentration. The system can support comparisons and anomaly localization, but it must not claim physical NO3 concentration without a calibration mapping and domain metadata.
