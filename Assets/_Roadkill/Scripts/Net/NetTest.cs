using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Scripted check for the phase 0 gate, started with -rktest on a client build:
    /// stand at the 75 kg crate, grab it with both hands, send a test tone over voice, and log
    /// what this peer sees once a second. Pair it with a host doing the same from the other side.
    /// </summary>
    public class NetTest : MonoBehaviour
    {
        public Vector3 standAt = new Vector3(-20.45f, 1.55f, -0.7f);
        public float runSeconds = 12f;
        [Tooltip("Stand in the swinging log's path instead of carrying the crate.")]
        public bool logMode;
        [Tooltip("Walk in a circle at walking speed, for checking the walk animation from outside.")]
        public bool walkMode;
        [Tooltip("Get launched limp, then crouch, logging the body's pose to compare with what the host sees.")]
        public bool flyMode;

        IEnumerator Start()
        {
            var manager = NetworkManager.Singleton;
            while (manager == null || !manager.IsConnectedClient || manager.LocalClient?.PlayerObject == null)
            {
                yield return null;
                manager = NetworkManager.Singleton;
            }
            yield return new WaitForSeconds(2f);

            var player = manager.LocalClient.PlayerObject;
            if (logMode)
            {
                yield return StandInLogPath(player);
                yield break;
            }
            if (walkMode)
            {
                yield return WalkInCircles(player);
                yield break;
            }
            if (flyMode)
            {
                yield return FlyAndCrouch(player);
                yield break;
            }
            var crate = FindCrate();
            Log($"connected as client {manager.LocalClientId}; players={HandsController.PlayerCount}; " +
                $"spawned objects={manager.SpawnManager.SpawnedObjectsList.Count}; crate found={crate != null}");
            if (crate == null) yield break;

            var motor = player.GetComponent<PlayerMotor>();
            motor.DebugPlace(standAt, crate.position + Vector3.up * 0.1f);
            yield return new WaitForSeconds(0.5f);
            var hands = player.GetComponent<HandsController>();
            hands.DebugGrab(0);
            hands.DebugGrab(1);
            yield return new WaitForSeconds(0.5f);
            // Look up to eye level: the hands now try to raise the crate to chest height.
            Vector3 eye = standAt + Vector3.up * motor.standingEyeHeight;
            motor.DebugPlace(standAt, new Vector3(crate.position.x, eye.y, crate.position.z));
            var voice = player.GetComponent<VoiceChat>();
            StartCoroutine(voice.DebugSendTone(3f));

            for (float t = 0f; t < runSeconds; t += 1f)
            {
                yield return new WaitForSeconds(1f);
                Log($"t={t + 1:0}s crate y={crate.position.y:0.00} hands on crate={HandsController.HolderCount(crate.GetComponent<Rigidbody>())} " +
                    $"my speed x{hands.SpeedMultiplier:0.00} voice packets received={VoiceChat.PacketsReceived}");
            }
            Log("done");
        }

        IEnumerator WalkInCircles(NetworkObject player)
        {
            var motor = player.GetComponent<PlayerMotor>();
            Vector3 center = new Vector3(0f, 0.02f, -5f);
            const float radius = 3f, speed = 3f;
            Log("walking in circles");
            for (float t = 0f; t < runSeconds * 3f; t += Time.fixedDeltaTime)
            {
                float a = t * speed / radius;
                Vector3 position = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                Vector3 ahead = center + new Vector3(Mathf.Cos(a + 0.3f), 0f, Mathf.Sin(a + 0.3f)) * radius;
                motor.DebugPlace(position, ahead + Vector3.up * 1.6f);
                yield return new WaitForFixedUpdate();
            }
            Log("done walking");
        }

        IEnumerator StandInLogPath(NetworkObject player)
        {
            var motor = player.GetComponent<PlayerMotor>();
            var health = player.GetComponent<PlayerHealth>();
            Vector3 spot = PropLayout.SwingPivot + new Vector3(0f, -PropLayout.SwingPivot.y + 0.02f, -1.5f);
            motor.DebugPlace(spot, spot + new Vector3(5f, 1.6f, 0f));
            Log($"standing in the log's path at {spot}");
            bool wasDown = false;
            for (float t = 0f; t < runSeconds; t += 0.25f)
            {
                yield return new WaitForSeconds(0.25f);
                if (motor.IsRagdolled && !wasDown) Log($"t={t:0.00}s KNOCKED DOWN, hp={health.Health:0}");
                wasDown = motor.IsRagdolled;
            }
            Log($"done, hp={health.Health:0}");
        }

        /// <summary>Launch limp, lie still, get up and crouch; log the bones each time (the host logs its copy to compare).</summary>
        IEnumerator FlyAndCrouch(NetworkObject player)
        {
            var motor = player.GetComponent<PlayerMotor>();
            var body = player.GetComponent<PlayerNet>().body;
            Vector3 spot = new Vector3(12f, 0.05f, -18f);
            motor.DebugPlace(spot, spot + new Vector3(0f, 1.6f, -5f));
            yield return new WaitForSeconds(1f);
            Log("launching");
            motor.EnterRagdoll(7f, new Vector3(0f, 8f, -5f));
            yield return new WaitForSeconds(4.5f);
            LogPose("resting", body);
            while (motor.IsRagdolled) yield return null;
            yield return new WaitForSeconds(1.5f);
            motor.debugCrouch = true;
            yield return new WaitForSeconds(2f);
            LogPose("crouching", body);
            yield return new WaitForSeconds(2f);
            motor.debugCrouch = false;
            Log("done flying");
        }

        public static string PoseText(ActiveRagdollController body)
        {
            var text = new System.Text.StringBuilder($"hips {body.Hips.position:F2}");
            for (int i = 0; i < body.PartCount; i++)
            {
                var part = body.PartBody(i);
                text.Append($" | {part.name} up {part.rotation * Vector3.up:F2} fwd {part.rotation * Vector3.forward:F2}");
            }
            return text.ToString();
        }

        static void LogPose(string label, ActiveRagdollController body) => Log($"POSE {label}: {PoseText(body)}");

        static Transform FindCrate()
        {
            foreach (var prop in FindObjectsByType<PhysicsProp>(FindObjectsInactive.Exclude))
                if (prop.displayName.StartsWith("Crate")) return prop.transform;
            return null;
        }

        static void Log(string message) => Debug.Log($"RK-TEST {message}");
    }
}
