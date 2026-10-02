# Open decisions

The automated desktop checks and the source-preserving refactor are recorded in
[REFACTOR-VERIFICATION.zh-CN.md](REFACTOR-VERIFICATION.zh-CN.md). Device-only and
external-service checks remain separate. The following three choices change
behaviour or delivery shape and are outside the refactor's unchanged-behaviour
scope. Each one lists its impact, options and verification.

## 1. Should a drag snap to different values?

**Already true:** a drag reports where it lands — `Time interval grounded: day X to
day Y.` and `Depth band grounded: <band>.`, plus a live preview while moving. Time
already lands on whole day cuts, depth rounds to its layer lattice on release.
Those two messages are frozen in the contract suite.

**The open part:** changing *which* values a drag can produce.

* It would change the time ranges operators get, and therefore the S4D results —
  the one thing the standing constraint forbids doing unasked.
* Options: (a) leave as is (feedback only); (b) snap time to whole days *and*
  re-label the readout so the two agree; (c) add a modifier key for fine
  dragging.
* Recommendation: (a). The current behaviour is already deterministic and
  reported; a change would invalidate the recorded baselines for every future
  comparison.
* If chosen: the verification is the recorded Field-pair/tri-axis bounds plus a
  new baseline phase that drags to a known cut and asserts the resulting range.

## 2. What should a typed depth number mean?

Depth has two grids in the current code: a drag snaps `low`/`high` to
`round(x * (DimZ - 1)) / (DimZ - 1)`, while `DepthRangeSummary()` derives its cuts
from `round(x * DimZ)`. So "type 1,3" has two defensible readings, and picking one
silently would either put typed values off the drag's lattice or make the readout
disagree with what was typed.

* Options: (a) type the two layers *as the readout shows them* and clamp to the
  nearest drag-reachable value, saying so in the prompt; (b) unify the two grids
  first (a behaviour change to what dragging reports); (c) skip depth entry — for
  the For_VR surface dataset DEPTH is a fixed role, so the demo never needs it.
* Recommendation: (c) for now, (a) if depth ranges are actually wanted, with the
  prompt spelling out which layer numbers are meant.
* If chosen: `SlabLabBoundaryEntry` gains a depth parser beside the time one, and
  the edit-mode guard asserts that typed values round-trip through the same
  readout a drag produces.

## 3. How should a Quest headset learn where the backend is?

The workspace starts from `s4dUrl = "http://127.0.0.1:8020"` and **nothing in the
runtime writes it** (the settings class reads or clears the override; it provides
no runtime setter), while the MatPlot URL does have an
on-device field. On a headset `127.0.0.1` is the headset, so the analysis steps
cannot reach a backend on the PC.

* Options: (a) add an on-device field beside the existing MatPlot one; (b) bake the
  PC's Tailscale address in as the build default; (c) leave the Quest path as
  editor-preview-only.
* Recommendation: (a) if the headset is meant to drive real analyses, (c)
  otherwise — (b) hides a network detail inside the build.
* If chosen: the field writes through `SlabLabSettings`, `VolumeSTCubeVrFlowTests`
  asserts it exists on the VR panel, and
  [QUEST-HEADSET-CHECKLIST.md](QUEST-HEADSET-CHECKLIST.md) step 7 becomes runnable.

## Conditional gaps (not decisions, just conditions)

* **Quest device session** — needs the headset; the checklist is ready.
* **`--stall` / `--vr` in batch mode** — the interactive editor must stay open
  today, because those checks drive its menu. A batch-mode entry point is easy to
  write but can only be *verified* with the editor closed, and shipping an
  unverified check is worse than not having it.
