using System.Collections;
using System.IO;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Test tool: films what the local player sees, one JPEG per frame at a fixed game frame rate
    /// (Time.captureFramerate), so the clip plays smoothly however slow the editor is. With revolverDemo it
    /// also fans the whole cylinder and lets the reload run, for showing the gun off. Put the frames
    /// together with ffmpeg. Add it in Play mode; it removes itself when done.
    /// </summary>
    public class FirstPersonRecorder : MonoBehaviour
    {
        public string folder;
        public int frames = 150;
        public int framesPerSecond = 30;
        public bool revolverDemo = true;

        public bool Done { get; private set; }

        IEnumerator Start()
        {
            Directory.CreateDirectory(folder);
            Time.captureFramerate = framesPerSecond;
            RevolverTool.IgnoreInput = true;
            var player = Unity.Netcode.NetworkManager.Singleton.LocalClient.PlayerObject;
            var gun = player != null ? player.GetComponent<PlayerTools>().Revolver : null;
            for (int i = 0; i < frames; i++)
            {
                // Fan the cylinder dry after a moment; the empty click starts the reload.
                if (revolverDemo && gun != null && i > 12 && i < 60 && !gun.Reloading) gun.DebugFire(true);
                yield return new WaitForEndOfFrame();
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(folder, $"frame_{i:0000}.jpg"), shot.EncodeToJPG(88));
                Destroy(shot);
            }
            Time.captureFramerate = 0;
            RevolverTool.IgnoreInput = false;
            Done = true;
            Debug.Log($"RK-REC {frames} frames to {folder}");
            Destroy(this);
        }
    }
}
