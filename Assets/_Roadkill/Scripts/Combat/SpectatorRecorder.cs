using System.Collections;
using System.IO;
using Unity.Netcode;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Test tool: films two players from the side with its own camera (off screen) and writes JPEG frames to
    /// a folder, to be put together into a clip (ffmpeg). Shows this peer's own body too, which is normally
    /// hidden from its owner, and hides the first-person hands so they do not float in the shot.
    /// Add it in Play mode once two players are in; it removes itself when done.
    /// </summary>
    public class SpectatorRecorder : MonoBehaviour
    {
        public string folder;
        public float seconds = 25f;
        public float framesPerSecond = 20f;
        public int width = 960, height = 540;
        public float distance = 3.4f;
        public float heightAbove = 1.3f;

        /// <summary>Frames written and the real time they took, for the clip's frame rate.</summary>
        public int Frames { get; private set; }
        public float Elapsed { get; private set; }
        public bool Done { get; private set; }

        IEnumerator Start()
        {
            PlayerNet a = null, b = null;
            for (float t = 0f; t < 60f && b == null; t += 0.5f)
            {
                a = b = null;
                foreach (var p in FindObjectsByType<PlayerNet>(FindObjectsInactive.Exclude))
                {
                    if (!p.IsSpawned || p.body == null) continue;
                    if (a == null) a = p; else if (b == null) b = p;
                }
                if (b == null) yield return new WaitForSeconds(0.5f);
            }
            if (b == null) { Done = true; yield break; }

            Directory.CreateDirectory(folder);
            var target = new RenderTexture(width, height, 24);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var cam = new GameObject("SpectatorCamera").AddComponent<Camera>();
            cam.targetTexture = target;
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.05f;

            Vector3 side = Vector3.right;
            Vector3 position = Vector3.zero;
            bool placed = false;
            float start = Time.realtimeSinceStartup, next = start;
            while (Time.realtimeSinceStartup - start < seconds)
            {
                yield return new WaitForEndOfFrame();
                ShowBodies(a, b);

                // Frame both fighters from the side of the line between them, easing so the camera does not jerk.
                Vector3 pa = a.body.Hips.position, pb = b.body.Hips.position;
                Vector3 mid = (pa + pb) * 0.5f;
                Vector3 across = Vector3.ProjectOnPlane(pb - pa, Vector3.up);
                if (across.sqrMagnitude > 0.04f)
                {
                    Vector3 wanted = Vector3.Cross(Vector3.up, across.normalized);
                    if (Vector3.Dot(wanted, side) < 0f) wanted = -wanted;   // stay on the same side
                    side = Vector3.Slerp(side, wanted, 0.05f).normalized;
                }
                float spread = Mathf.Clamp(across.magnitude, 1f, 6f);
                Vector3 wantedPosition = mid + side * (distance + spread * 0.6f) + Vector3.up * heightAbove;
                position = placed ? Vector3.Lerp(position, wantedPosition, 0.1f) : wantedPosition;
                placed = true;
                cam.transform.position = position;
                cam.transform.rotation = Quaternion.LookRotation(mid + Vector3.up * 0.2f - position, Vector3.up);

                if (Time.realtimeSinceStartup < next) continue;
                next += 1f / framesPerSecond;
                // The camera renders into its texture on its own every frame; read the latest one.
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                RenderTexture.active = previous;
                File.WriteAllBytes(Path.Combine(folder, $"frame_{Frames:0000}.jpg"), texture.EncodeToJPG(88));
                Frames++;
            }
            Elapsed = Time.realtimeSinceStartup - start;
            Destroy(cam.gameObject);
            target.Release();
            Done = true;
            Debug.Log($"RK-REC {Frames} frames in {Elapsed:0.0} s to {folder}");
        }

        static void ShowBodies(params PlayerNet[] players)
        {
            foreach (var p in players)
            {
                if (p == null) continue;
                foreach (var r in p.bodyRenderers) r.enabled = true;
                if (p.IsOwner && p.Hands != null)
                {
                    if (p.Hands.Left.Visual != null) p.Hands.Left.Visual.gameObject.SetActive(false);
                    if (p.Hands.Right.Visual != null) p.Hands.Right.Visual.gameObject.SetActive(false);
                }
            }
        }
    }
}
