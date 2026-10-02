using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Roadkill.EditorTools
{
    public static class RoadkillMenu
    {
        const string GreyboxScenePath = "Assets/Scenes/Greybox.unity";

        [MenuItem("Roadkill/Create Greybox Test Scene")]
        static void CreateGreyboxScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Greybox").AddComponent<GreyboxBuilder>();

            Directory.CreateDirectory(Path.GetDirectoryName(GreyboxScenePath));
            EditorSceneManager.SaveScene(scene, GreyboxScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(GreyboxScenePath, true) };
            Debug.Log($"Roadkill: saved {GreyboxScenePath}. Press Play.");
        }
    }
}
