namespace UnityVolumeRendering
{
    public sealed partial class VolumeSTCubeQuestSpatialWorkbench
    {
        /// <summary>
        /// Where the workflow currently stands, as a single value instead of a
        /// combination of a stage plus a dozen flags.
        ///
        /// This is a *read-only view*: it is computed from the fields that already
        /// drive the flow (stage, spatialWorkflowStep, and the pending/pending-…
        /// gates). Nothing assigns to it, so it cannot change behaviour — it exists
        /// so readers, diagnostics and tests can talk about "which step are we on"
        /// without re-deriving it, and so the transition table below is written
        /// down in one place:
        ///
        ///   DatasetImport : ImportConnecting -> ImportReady
        ///   Field         : FieldLoading -> FieldReady -> FieldBoundaryAuthoring
        ///   Slab          : SlabAxisBinding -> SlabIntent -> SlabMaterializing
        ///                   -> SlabPreviewReady
        ///   Matrix        : MatrixBuilding -> MatrixCellPicked
        ///   Analyze       : AnalyzeRunning -> AnalyzeReady
        ///   Result        : ResultReady
        /// </summary>
        public enum Phase
        {
            ImportConnecting,
            ImportReady,
            FieldLoading,
            FieldReady,
            FieldBoundaryAuthoring,
            SlabAxisBinding,
            SlabIntent,
            SlabMaterializing,
            SlabPreviewReady,
            MatrixBuilding,
            MatrixCellPicked,
            AnalyzeRunning,
            AnalyzeReady,
            ResultReady
        }

        public Phase CurrentPhase
        {
            get
            {
                switch (stage)
                {
                    case Stage.DatasetImport:
                        return datasets.Count > 0 || datasetImportConfirmed
                            ? Phase.ImportReady
                            : Phase.ImportConnecting;
                    case Stage.Field:
                        if (boundaryEditActive || initialBoundarySetupActive ||
                            spatialWorkflowStep == SpatialWorkflowStep.BoundaryAuthoring)
                            return Phase.FieldBoundaryAuthoring;
                        if (authorBoundaryConfirmed)
                            return Phase.FieldReady;
                        return Phase.FieldLoading;
                    case Stage.Slab:
                        switch (spatialWorkflowStep)
                        {
                            case SpatialWorkflowStep.Intent:
                                return Phase.SlabIntent;
                            case SpatialWorkflowStep.Materializing:
                                return Phase.SlabMaterializing;
                            case SpatialWorkflowStep.SourcePreviewReady:
                                return Phase.SlabPreviewReady;
                            default:
                                return Phase.SlabAxisBinding;
                        }
                    case Stage.Matrix:
                        return gridCellSelected
                            ? Phase.MatrixCellPicked : Phase.MatrixBuilding;
                    case Stage.Analyze:
                        return IsPending(PendingJob.Analysis) || IsPending(PendingJob.Digest) || IsPending(PendingJob.Intent)
                            ? Phase.AnalyzeRunning : Phase.AnalyzeReady;
                    default:
                        return Phase.ResultReady;
                }
            }
        }
    }
}
