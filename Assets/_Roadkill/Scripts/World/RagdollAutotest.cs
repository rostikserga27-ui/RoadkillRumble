using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace Roadkill
{
    /// <summary>
    /// Scripted check of the active ragdoll in the RagdollTest scene: presses the real keys (through the
    /// Input System, so PlayerMotor moves exactly as when you play) to idle, walk, sprint-jump, walk off
    /// the ramp ledge, run down the slope, drop on the bouncy pad, get hit by the box cannon, get
    /// launched and play possum. Every physics step it measures joint stretch, NaNs, floor clipping and
    /// knees/elbows folding the wrong way; it logs "RK-RAGDOLL" lines and a PASS/FAIL summary.
    /// </summary>
    public class RagdollAutotest : MonoBehaviour
    {
        public RagdollTestBuilder builder;

        public bool Running { get; private set; }
        public string Summary { get; private set; } = "";

        ActiveRagdollController body;
        PlayerMotor motor;
        Transform root;
        Dictionary<string, Rigidbody> bones;
        readonly List<string> failures = new List<string>();

        // Running measurements (reset per phase).
        float maxStretch, minKnee, minElbow, maxSpin, jitterSum, hipHeightSum, tiltSum, maxTilt, legLagSum, feetTrailSum;
        int groundedSamples;
        int samples, nanFrames, clipFrames, airborneFrames, activeSamples;
        string spinBone = "";
        // The remote-player stand-in, measured over the whole run.
        float remoteTiltSum, remoteLagSum, remoteMaxStretch;
        int remoteActive, remoteLimpEpisodes, remoteRecoveries;
        bool remoteWasLimp;

        public void Run()
        {
            if (!Running && builder != null && builder.Body != null) StartCoroutine(Sequence());
        }

        IEnumerator Sequence()
        {
            Running = true;
            failures.Clear();
            body = builder.Body;
            motor = builder.Motor;
            root = motor.transform;
            bones = new Dictionary<string, Rigidbody>();
            foreach (var p in body.parts) bones[p.body.name] = p.body;
            StartCoroutine(Sample());
            Log("start");

            Place(new Vector3(0f, 0.05f, -1f), Vector3.forward);
            yield return Hold(1f);
            Begin(); yield return Hold(3f);
            End("idle", checkJitter: true);

            Begin(); yield return Hold(3f, Key.W);
            End("walk", checkGait: true);

            Begin();
            yield return Hold(0.8f, Key.W, Key.LeftShift);
            yield return Hold(0f, Key.W, Key.LeftShift, Key.Space);   // a tap: a few frames
            yield return Hold(1.4f, Key.W);
            yield return Hold(1.5f);
            End("sprint + jump", expectAirborne: true);

            Place(new Vector3(-7f, 2.55f, 10f), Vector3.forward);
            yield return Hold(1f);
            Begin(); yield return Hold(1.6f, Key.W); yield return Hold(2.5f);
            End("walk off the 2.5 m ledge");

            Place(new Vector3(7f, 5.05f, 11f), Vector3.back);
            yield return Hold(1f);
            Begin(); yield return Hold(2.2f, Key.W); yield return Hold(2f);
            End("down the 32 degree slope");

            Place(new Vector3(0f, 3f, -6f), Vector3.forward);
            Begin(); yield return Hold(4f);
            End("drop on the bouncy pad");

            Place(new Vector3(-4f, 0.05f, -4.5f), Vector3.back);
            yield return Hold(1f);
            Begin();
            builder.FireBox();
            yield return Hold(0.6f);
            bool knocked = body.IsLimp;
            yield return Recover(6f);
            End("hit by the box cannon", expectRecovered: true);
            Log($"box hit knocked him out: {knocked}");

            Place(new Vector3(0f, 0.05f, -1f), Vector3.forward);
            yield return Hold(1f);
            Begin();
            body.Launch(root.forward * 7f + Vector3.up * 8f);
            yield return Hold(1f);
            bool flying = motor.IsRagdolled && body.IsLimp;
            yield return Recover(8f);
            End("launched", expectRecovered: true);
            if (!flying) failures.Add("launch: did not go limp with the player");

            Begin(); yield return Hold(2f, Key.C); yield return Recover(6f);
            End("possum", expectRecovered: true);

            Begin(); body.SetLimp(true); yield return Hold(2f); body.SetLimp(false); yield return Recover(4f);
            End("ragdoll toggle", expectRecovered: true);

            RemoteVerdict();
            Summary = failures.Count == 0 ? "PASS" : "FAIL: " + string.Join("; ", failures);
            Log("summary " + Summary);
            Running = false;
        }

        void Place(Vector3 position, Vector3 facing)
        {
            motor.Respawn();
            motor.DebugPlace(position, position + facing * 5f + Vector3.up * 1.6f);
            body.ResetPose();
        }

        IEnumerator Hold(float seconds, params Key[] keys)
        {
            int frames = 0;
            for (float t = 0f; t < seconds || frames < 3; t += Time.deltaTime, frames++)
            {
                Press(keys);
                yield return null;
            }
            Press();
        }

        /// <summary>Wait (no keys) until he is back on his feet and fully active, up to maxSeconds; logs how long it took.</summary>
        IEnumerator Recover(float maxSeconds)
        {
            float t = 0f;
            yield return Hold(0.5f);
            while (t < maxSeconds && (body.IsLimp || motor.IsRagdolled || body.ActiveWeight < 0.99f || !body.IsUpright))
            {
                Press();
                t += Time.deltaTime;
                yield return null;
            }
            Log($"back on his feet {t + 0.5f:0.0} s after the knock");
            yield return Hold(1f);   // and steady for a second
        }

        static void Press(params Key[] keys)
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys));
#endif
        }

        void Begin()
        {
            maxStretch = maxSpin = jitterSum = hipHeightSum = tiltSum = maxTilt = 0f;
            minKnee = minElbow = float.MaxValue;
            samples = nanFrames = clipFrames = airborneFrames = activeSamples = groundedSamples = 0;
            legLagSum = feetTrailSum = 0f;
            spinBone = "";
        }

        void End(string phase, bool checkJitter = false, bool checkGait = false, bool expectAirborne = false, bool expectRecovered = false)
        {
            float jitter = samples > 0 ? jitterSum / samples : 0f;
            float hips = samples > 0 ? hipHeightSum / samples : 0f;
            float tilt = activeSamples > 0 ? tiltSum / activeSamples : 0f;
            float legLag = activeSamples > 0 ? legLagSum / activeSamples : 0f;
            float feetTrail = activeSamples > 0 ? feetTrailSum / activeSamples : 0f;
            if (checkGait) Log($"{phase}: legs trail their gait targets by {legLag:0} deg on average, feet {feetTrail * 100f:0} cm behind the hips, grounded {100f * groundedSamples / Mathf.Max(1, samples):0}%");
            Log($"{phase}: torso tilt avg {tilt:0} max {maxTilt:0} deg, stretch {maxStretch * 100f:0.0} cm, min knee {minKnee:0} deg, min elbow {minElbow:0} deg, " +
                $"max spin {maxSpin:0.0} rad/s ({spinBone}), hips {hips:0.00} m, jitter {jitter:0.00} rad/s, airborne {airborneFrames}, " +
                $"clipping {clipFrames}, NaN {nanFrames}, speed {body.Speed:0.0}");
            if (nanFrames > 0) failures.Add($"{phase}: NaN");
            if (maxStretch > 0.05f) failures.Add($"{phase}: joints stretched {maxStretch * 100f:0.0} cm");
            if (clipFrames > 3) failures.Add($"{phase}: clipped through the floor ({clipFrames} steps)");
            if (minKnee < -12f) failures.Add($"{phase}: knee bent forward ({minKnee:0} deg)");
            if (minElbow < -12f) failures.Add($"{phase}: elbow bent backward ({minElbow:0} deg)");
            // Flailing hands reach a few tens of rad/s; an exploding joint goes to hundreds and stretches.
            if (maxSpin > 150f) failures.Add($"{phase}: {spinBone} spun at {maxSpin:0} rad/s (exploding)");
            if (checkJitter && jitter > 0.8f) failures.Add($"idle jitter {jitter:0.00} rad/s");
            if (checkJitter && Mathf.Abs(hips - body.hipHeight) > 0.12f) failures.Add($"idle hips at {hips:0.00} m, want ~{body.hipHeight:0.00}");
            if (checkGait && body.Speed < 2f) failures.Add($"walk: body only reached {body.Speed:0.0} m/s");
            if (checkGait && legLag > 15f) failures.Add($"walk: legs dragged behind the gait by {legLag:0} deg");
            if ((checkGait || checkJitter) && feetTrail > 0.2f) failures.Add($"{phase}: feet trail {feetTrail * 100f:0} cm behind the hips (towed, not walking)");
            if ((checkGait || checkJitter) && (tilt > 25f || maxTilt > 60f)) failures.Add($"{phase}: torso tipped (avg {tilt:0}, max {maxTilt:0} deg)");
            if (expectAirborne && airborneFrames == 0) failures.Add($"{phase}: never left the ground");
            if (expectRecovered && (body.IsLimp || motor.IsRagdolled || body.ActiveWeight < 0.99f || !body.IsUpright))
                failures.Add($"{phase}: had not recovered");
            if (body.LastImpact.Length > 0) Log($"{phase}: last impact {body.LastImpact}");
        }

        void RemoteVerdict()
        {
            if (builder.Remote == null) return;
            float tilt = remoteActive > 0 ? remoteTiltSum / remoteActive : 0f;
            float lag = remoteActive > 0 ? remoteLagSum / remoteActive : 0f;
            Log($"remote player view: torso tilt avg {tilt:0} deg, hips lag {lag * 100f:0} cm, stretch {remoteMaxStretch * 100f:0.0} cm, " +
                $"knockdowns {remoteLimpEpisodes}, recovered {remoteRecoveries}");
            if (tilt > 25f) failures.Add($"remote: torso tipped (avg {tilt:0} deg)");
            if (lag > 0.5f) failures.Add($"remote: body trails its capsule by {lag:0.00} m");
            if (remoteMaxStretch > 0.05f) failures.Add($"remote: joints stretched {remoteMaxStretch * 100f:0.0} cm");
            if (remoteLimpEpisodes == 0 || remoteRecoveries < remoteLimpEpisodes - 1) failures.Add("remote: did not go limp and recover with the owner");
        }

        void SampleRemote()
        {
            var remote = builder.Remote;
            if (remote == null || remote.body == null) return;
            var r = remote.body;
            foreach (var p in r.parts)
            {
                if (p.joint == null) continue;
                Vector3 a = p.body.transform.TransformPoint(p.joint.anchor);
                Vector3 b = p.joint.connectedBody.transform.TransformPoint(p.joint.connectedAnchor);
                remoteMaxStretch = Mathf.Max(remoteMaxStretch, (a - b).magnitude);
            }
            if (r.IsLimp && !remoteWasLimp) remoteLimpEpisodes++;
            if (!r.IsLimp && remoteWasLimp) remoteRecoveries++;
            remoteWasLimp = r.IsLimp;
            if (r.IsLimp || r.ActiveWeight < 0.99f) return;
            foreach (var p in r.parts)
                if (p.role == ActiveRagdollController.Role.Chest)
                    remoteTiltSum += Vector3.Angle(p.body.rotation * Quaternion.Inverse(p.restRotation) * Vector3.up, Vector3.up);
            Vector3 lag = r.Hips.position - remote.transform.position;
            remoteLagSum += new Vector2(lag.x, lag.z).magnitude;
            remoteActive++;
        }

        IEnumerator Sample()
        {
            var wait = new WaitForFixedUpdate();
            remoteTiltSum = remoteLagSum = remoteMaxStretch = 0f;
            remoteActive = remoteLimpEpisodes = remoteRecoveries = 0;
            remoteWasLimp = false;
            while (Running)
            {
                yield return wait;
                if (body == null) yield break;
                SampleRemote();
                samples++;
                bool nan = false;
                foreach (var p in body.parts)
                {
                    Vector3 position = p.body.position;
                    if (float.IsNaN(position.x) || float.IsNaN(position.y)) nan = true;
                    float spin = p.body.angularVelocity.magnitude;
                    if (spin > maxSpin) { maxSpin = spin; spinBone = p.body.name; }
                    if (p.joint != null)
                    {
                        Vector3 a = p.body.transform.TransformPoint(p.joint.anchor);
                        Vector3 b = p.joint.connectedBody.transform.TransformPoint(p.joint.connectedAnchor);
                        maxStretch = Mathf.Max(maxStretch, (a - b).magnitude);
                    }
                }
                if (nan) nanFrames++;
                if (BelowFloor()) clipFrames++;
                if (!body.IsGrounded) airborneFrames++;
                else groundedSamples++;
                jitterSum += body.Hips.angularVelocity.magnitude;
                hipHeightSum += body.Hips.position.y - root.position.y;

                if (!body.IsLimp && body.ActiveWeight > 0.99f)
                {
                    float tilt = Vector3.Angle(BodyAxis(bones["Chest"], Vector3.up), Vector3.up);
                    tiltSum += tilt;
                    maxTilt = Mathf.Max(maxTilt, tilt);
                    activeSamples++;
                    foreach (var p in body.parts)
                    {
                        if (p.role == ActiveRagdollController.Role.UpperLeg) legLagSum += 0.5f * (SwingAngle(p, true) - SwingAngle(p, false));
                        if (p.role == ActiveRagdollController.Role.Foot)
                            feetTrailSum += 0.5f * Vector3.Dot(body.Hips.position - p.body.position, Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized);
                    }
                    minKnee = Mathf.Min(minKnee, Knee("Left"), Knee("Right"));
                    minElbow = Mathf.Min(minElbow, Elbow("Left"), Elbow("Right"));
                }
            }
        }

        /// <summary>Any body collider sunk into the flat ground (y = 0) where there is nothing else underneath.</summary>
        bool BelowFloor()
        {
            foreach (var p in body.parts)
            foreach (var c in p.body.GetComponentsInChildren<Collider>())
            {
                if (c.attachedRigidbody != p.body) continue;
                if (c.bounds.min.y < -0.08f) return true;
            }
            return false;
        }

        /// <summary>Knee flexion about the knee's hinge axis: positive folds the shin backward (correct).</summary>
        float Knee(string side) => Flexion(side + "UpperLeg", side + "LowerLeg", side + "Foot");

        /// <summary>Elbow flexion about the elbow's hinge axis: positive folds the forearm forward (correct).</summary>
        float Elbow(string side) => Flexion(side + "UpperArm", side + "LowerArm", side + "Hand");

        /// <summary>The builder points each hinge axis so that a positive rotation about it is flexion.</summary>
        float Flexion(string upper, string lower, string tip)
        {
            var hinge = bones[lower].GetComponent<ConfigurableJoint>();
            Vector3 axis = bones[lower].transform.TransformDirection(hinge.axis);
            Vector3 a = bones[lower].position - bones[upper].position;
            Vector3 b = bones[tip].position - bones[lower].position;
            return Vector3.SignedAngle(Vector3.ProjectOnPlane(a, axis), Vector3.ProjectOnPlane(b, axis), axis);
        }

        /// <summary>How far a limb is swung back (positive) about the character's right, from its rest pose: now or as targeted.</summary>
        static float SwingAngle(ActiveRagdollController.Part p, bool actual)
        {
            Quaternion local;
            if (actual) local = Quaternion.Inverse(p.joint.connectedBody.rotation) * p.body.rotation;
            else local = p.startLocal * Quaternion.Inverse(p.jointFrame * p.joint.targetRotation * Quaternion.Inverse(p.jointFrame));
            (local * Quaternion.Inverse(p.startLocal)).ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            return angle * Vector3.Dot(axis, p.right);
        }

        /// <summary>A character axis (in root space at rest) as a bone carries it now.</summary>
        Vector3 BodyAxis(Rigidbody bone, Vector3 characterAxis)
        {
            foreach (var p in body.parts)
                if (p.body == bone) return bone.rotation * Quaternion.Inverse(p.restRotation) * characterAxis;
            return root.rotation * characterAxis;
        }

        static void Log(string message) => Debug.Log($"RK-RAGDOLL {message}");

#if !ENABLE_INPUT_SYSTEM
        enum Key { W, LeftShift, Space, C }
#endif
    }
}
