using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace UnityVolumeRenderingEditorTools
{
    [InitializeOnLoad]
    internal static class VolumeSTCubeDefaultScene
    {
        private const string MainScenePath = "Assets/Scenes/mainScene.unity";

        static VolumeSTCubeDefaultScene()
        {
            EditorApplication.delayCall += OpenMainSceneForEmptyWorkspace;
        }

        private static void OpenMainSceneForEmptyWorkspace()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            Scene activeScene = SceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(activeScene.path))
                return;

            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        }
    }
}
