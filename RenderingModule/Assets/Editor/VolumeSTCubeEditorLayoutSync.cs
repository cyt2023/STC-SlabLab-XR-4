using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityVolumeRendering
{
    [InitializeOnLoad]
    internal static class VolumeSTCubeEditorLayoutSync
    {
        private static int alignedSceneHandle = -1;

        static VolumeSTCubeEditorLayoutSync()
        {
            EditorApplication.hierarchyChanged += QueueApply;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            QueueApply();
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            alignedSceneHandle = -1;
            QueueApply();
        }

        private static void QueueApply()
        {
            EditorApplication.delayCall -= Apply;
            EditorApplication.delayCall += Apply;
        }

        private static void Apply()
        {
            if (Application.isPlaying || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            bool changed = VolumeSTCubeSceneIntegration.ApplyToolbarLayout();
            VolumeControllerObject controller = Object.FindObjectOfType<VolumeControllerObject>();
            if (VolumeSTCubeOriginalSceneAdapter.NeedsGeographicStackLayout(controller))
            {
                VolumeSTCubeRenderMode renderMode = controller.GetRenderMode() == RenderMode.IsosurfaceRendering
                    ? VolumeSTCubeRenderMode.Surface
                    : VolumeSTCubeRenderMode.Volume;
                VolumeSTCubeOriginalSceneAdapter.RefreshController(controller, renderMode, VolumeSTCubeTimeAxis.Z);
                changed = true;
            }

            if (controller != null && controller.meshRenderers != null && controller.meshRenderers.Length > 0)
            {
                changed |= VolumeSTCubeOriginalSceneAdapter.AlignGeographicMapBelowStack(controller);
                bool cameraChanged = VolumeSTCubeOriginalSceneAdapter.SetPresentationCamera();
                changed |= cameraChanged;

                Scene activeScene = SceneManager.GetActiveScene();
                if (cameraChanged || alignedSceneHandle != activeScene.handle)
                {
                    AlignSceneViewToMainCamera();
                    alignedSceneHandle = activeScene.handle;
                }
            }

            if (!changed)
                return;

            Scene scene = SceneManager.GetActiveScene();
            if (scene.IsValid() && scene.isLoaded)
                EditorSceneManager.MarkSceneDirty(scene);
            SceneView.RepaintAll();
        }

        private static void AlignSceneViewToMainCamera()
        {
            Camera camera = Camera.main;
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (camera == null || sceneView == null)
                return;

            sceneView.AlignViewToObject(camera.transform);
            sceneView.Repaint();
        }
    }
}
