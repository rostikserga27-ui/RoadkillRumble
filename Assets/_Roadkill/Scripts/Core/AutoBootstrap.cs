using UnityEngine;
using UnityEngine.SceneManagement;

namespace Roadkill
{
    /// <summary>
    /// Pressing Play in an empty scene (Unity's default camera and light only) builds the
    /// greybox test level, so the prototype runs without any authored scene.
    /// Real scenes, or scenes that already contain a GreyboxBuilder or player, are left alone.
    /// </summary>
    static class AutoBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindAnyObjectByType<GreyboxBuilder>() != null) return;
            if (Object.FindAnyObjectByType<RagdollTestBuilder>() != null) return;
            if (Object.FindAnyObjectByType<PlayerMotor>() != null) return;

            Scene scene = SceneManager.GetActiveScene();
            if (scene.rootCount > 3) return;

            // The player brings its own camera.
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<Camera>() != null) Object.Destroy(root);
            }

            new GameObject("Greybox").AddComponent<GreyboxBuilder>();
        }
    }
}
