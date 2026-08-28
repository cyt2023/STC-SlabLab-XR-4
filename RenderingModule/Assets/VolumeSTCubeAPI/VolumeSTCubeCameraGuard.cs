using UnityEngine;

namespace UnityVolumeRendering
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    public sealed class VolumeSTCubeCameraGuard : MonoBehaviour
    {
        private Camera targetCamera;
        private CameraController legacyController;

        private void Awake()
        {
            targetCamera = GetComponent<Camera>();
            legacyController = GetComponent<CameraController>();
            TakeCameraOwnership();
        }

        private void LateUpdate()
        {
            TakeCameraOwnership();
        }

        private void TakeCameraOwnership()
        {
            if (legacyController != null)
                legacyController.debug_unlock_rotation = true;

            if (targetCamera != null)
                targetCamera.rect = new Rect(0.0f, 0.0f, 1.0f, 1.0f);
        }
    }
}
