using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Stores the camera-to-avatar arrangement exactly as placed in the Editor, so the runtime
/// never has to guess it after XROrigin has already rewritten the Camera Offset.
/// </summary>
[InitializeOnLoad]
internal static class LocalAvatarAuthoredPoseCapture
{
    static LocalAvatarAuthoredPoseCapture()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorSceneManager.sceneSaving += OnSceneSaving;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode)
            return;

        for (int i = 0; i < SceneManager.sceneCount; i++)
            CaptureScene(SceneManager.GetSceneAt(i));
    }

    private static void OnSceneSaving(Scene scene, string path)
    {
        CaptureScene(scene);
    }

    internal static void CaptureScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            LocalAvatarIKDriver[] drivers =
                roots[i].GetComponentsInChildren<LocalAvatarIKDriver>(true);

            for (int j = 0; j < drivers.Length; j++)
            {
                drivers[j].CaptureAuthoredPose();
                EditorUtility.SetDirty(drivers[j]);
            }
        }
    }

    private sealed class BuildSceneCapture : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null)
                return;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                LocalAvatarIKDriver[] drivers =
                    roots[i].GetComponentsInChildren<LocalAvatarIKDriver>(true);

                for (int j = 0; j < drivers.Length; j++)
                    drivers[j].CaptureAuthoredPose();
            }
        }
    }
}
