#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace B2U
{
    [InitializeOnLoad]
    public static class B2UEditorSceneBootstrap
    {
        static B2UEditorSceneBootstrap()
        {
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorApplication.delayCall += TryBindFromActiveScene;
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            TryBindFromActiveScene();
        }

        private static void TryBindFromActiveScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            B2URuntimeManager manager = Object.FindAnyObjectByType<B2URuntimeManager>();
            if (manager == null || manager.defaultProfile == null) return;

            if (B2URuntimeUtility.ActiveProfile != manager.defaultProfile)
            {
                B2URuntimeUtility.SetB2UShaderPropertyValuesFromProfile(manager.defaultProfile);
                B2UShaderProfileEditor.SyncRenderFeatureStates(manager.defaultProfile);
                EditorPrefs.SetString("B2U_ActiveProfilePath", AssetDatabase.GetAssetPath(manager.defaultProfile));
                SceneView.RepaintAll();
            }
        }
    }
}
#endif