using UnityEngine;

namespace UnityVolumeRendering
{
    /// <summary>
    /// Temporarily moves the proven animated Field aside while an independent
    /// XYT Field occupies its former presentation position. Reference counting
    /// keeps dataset switches from applying the offset more than once.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    internal sealed class VolumeSTCubeForVrFieldSwapLayout : MonoBehaviour
    {
        public const float Separation = 2.15f;
        public const float DesktopSeparation = 1.72f;

        public static float ActiveSeparation =>
            VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled
                ? DesktopSeparation : Separation;

        private int users;
        private Vector3 expectedShiftedPosition;
        private Transform datasetSelector;
        private Vector3 datasetSelectorOriginalLocalPosition;
        private bool datasetSelectorPositionCaptured;

        public void Acquire()
        {
            users++;
            if (users != 1)
                return;
            ApplyShift();
            PositionSharedDatasetSelector();
        }

        public void Release()
        {
            users = Mathf.Max(0, users - 1);
            if (users != 0)
                return;
            RestoreDatasetSelectorPosition();
            transform.position += transform.right * ActiveSeparation;
            expectedShiftedPosition = transform.position;
        }

        public void KeepCurrentShiftedPosition()
        {
            expectedShiftedPosition = transform.position;
        }

        private void LateUpdate()
        {
            if (users <= 0)
                return;
            // Workspace re-anchoring writes the original centre position.
            // Reapply the presentation offset exactly once after such a write.
            if ((transform.position - expectedShiftedPosition).sqrMagnitude >
                0.0004f)
                ApplyShift();
            PositionSharedDatasetSelector();
        }

        private void ApplyShift()
        {
            transform.position -= transform.right * ActiveSeparation;
            expectedShiftedPosition = transform.position;
        }

        private void PositionSharedDatasetSelector()
        {
            if (datasetSelector == null)
            {
                datasetSelector = transform.Find("Field display dataset selector");
                if (datasetSelector == null)
                    return;
            }
            if (!datasetSelectorPositionCaptured)
            {
                datasetSelectorOriginalLocalPosition =
                    datasetSelector.localPosition;
                datasetSelectorPositionCaptured = true;
            }
            // The animated Field is at local 0 and the XYT Field is one
            // Separation to its right. Put the shared variable selector at the
            // midpoint of their upper edges so it visually belongs to both.
            datasetSelector.localPosition = datasetSelectorOriginalLocalPosition +
                Vector3.right * (ActiveSeparation * 0.5f);
        }

        private void RestoreDatasetSelectorPosition()
        {
            if (datasetSelector != null && datasetSelectorPositionCaptured)
                datasetSelector.localPosition = datasetSelectorOriginalLocalPosition;
            datasetSelectorPositionCaptured = false;
        }
    }
}
