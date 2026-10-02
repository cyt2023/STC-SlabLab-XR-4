namespace UnityVolumeRendering
{
    public sealed partial class VolumeSTCubeQuestSpatialWorkbench
    {
        /// <summary>
        /// Work that is currently in flight. This replaces seven separate bools
        /// with one bitmask so the whole "what is running" question is answered by
        /// a single field; each bit is the exact former boolean, and the code
        /// already combines them (e.g. manifest resolving OR intent resolving), so
        /// they stay independent bits rather than one exclusive state.
        /// </summary>
        [System.Flags]
        private enum PendingJob
        {
            None = 0,
            Analysis = 1 << 0,
            VariableLoad = 1 << 1,
            DatasetManifest = 1 << 2,
            Intent = 1 << 3,
            SourcePreview = 1 << 4,
            Digest = 1 << 5,
            GroundAggregate = 1 << 6
        }

        private PendingJob pendingJobs;

        private bool IsPending(PendingJob job)
        {
            return (pendingJobs & job) != 0;
        }

        private void SetPending(PendingJob job, bool pending)
        {
            if (pending)
                pendingJobs |= job;
            else
                pendingJobs &= ~job;
        }
    }
}
