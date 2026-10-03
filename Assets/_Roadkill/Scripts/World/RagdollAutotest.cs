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
    /// Input System, so PlayerMotor moves exactly as when you play) to idle, walk, strafe, walk
    /// diagonally and backwards, sprint-jump, walk off the ramp ledge, run down the slope, drop on the
    /// bouncy pad, grab and throw a crate, get hit by the box cannon, get launched and play possum. Every physics step it measures joint stretch, NaNs, floor clipping and
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
        int groundedSamples, crossedSamples, walkingSamples;
        float remoteReachSum;
        int remoteReachSamples;
        // Side-to-side wobble and rolling while down.
        float maxSideSway, limpTime, rollTravel, rollSpin;
        int rollSamples;
        Vector3 lastLimpHips;
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

            Place(new Vector3(-12f, 0.05f, -16f), Vector3.forward);
            yield return Hold(0.5f);
            Begin(); yield return Hold(2.5f, Key.D);
            End("strafe right", checkTravel: true);

            Begin(); yield return Hold(2.5f, Key.W, Key.A);
            End("walk diagonally", checkTravel: true);

            Begin(); yield return Hold(2f, Key.S);
            End("walk backwards", checkTravel: true);

            Place(new Vector3(-12f, 0.05f, -16f), Vector3.forward);
            yield return Hold(0.5f);
            Begin();
            for (int k = 0; k < 6; k++) yield return Hold(0.6f, k % 2 == 0 ? Key.D : Key.A);
            yield return Hold(0.8f);
            End("side to side", checkSway: true);

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

            Place(new Vector3(-12f, 0.05f, -3f), Vector3.forward);
            var crate = builder.SpawnCrate(new Vector3(-12f, 0.3f, -2.1f));
            yield return Hold(1f);
            Begin();
            bool grabbed = builder.TestHands.Grab(0) & builder.TestHands.Grab(1);
            yield return Hold(1.2f);
            // The game holds loads 0.9 m+ in front of the eyes, often beyond the fisherman's arms: then the
            // arm must be stretched out and pointing at the grip; within reach the hand must be on it.
            float reachError = 0f, aimError = 0f;
            for (int i = 0; i < 2; i++)
            {
                Vector3 shoulder = body.ShoulderPosition(i), grip = builder.TestHands.Grip(i);
                Vector3 palm = bones[i == 0 ? "LeftHand" : "RightHand"].position;
                float reachable = body.ReachLength(i);
                if (Vector3.Distance(shoulder, grip) <= reachable) reachError = Mathf.Max(reachError, Vector3.Distance(palm, grip) - 0.08f);
                else
                {
                    aimError = Mathf.Max(aimError, Vector3.Angle(palm - shoulder, grip - shoulder));
                    reachError = Mathf.Max(reachError, reachable * 0.85f - Vector3.Distance(palm, shoulder));
                }
            }
            yield return Hold(0.9f, Key.G);
            float charge = builder.TestHands.ThrowCharge;
            yield return Hold(0f);                                   // G comes up: the throw
            bool flung = body.IsThrowing;
            float crateSpeed = 0f;
            for (float t = 0f; t < 0.5f; t += Time.fixedDeltaTime)
            {
                yield return new WaitForFixedUpdate();
                flung |= body.IsThrowing;
                crateSpeed = Mathf.Max(crateSpeed, crate.linearVelocity.magnitude);
            }
            yield return Hold(1f);
            End("grab and throw");
            Log($"grab and throw: grabbed {grabbed}, reach short by {Mathf.Max(0f, reachError) * 100f:0} cm, arms off the grip by {aimError:0} deg, charge {charge:0.00}, flung {flung}, crate left at {crateSpeed:0.0} m/s");
            if (!grabbed) failures.Add("grab: nothing grabbed");
            if (reachError > 0.15f) failures.Add($"grab: hands fell {reachError * 100f:0} cm short of what they hold");
            if (aimError > 30f) failures.Add($"grab: arms pointed {aimError:0} deg away from the grip");
            if (!flung) failures.Add("throw: the body did not fling");
            if (crateSpeed < 3f) failures.Add($"throw: crate only left at {crateSpeed:0.0} m/s");
            Destroy(crate.gameObject, 3f);

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

            Place(new Vector3(0f, 0.05f, -1f), Vector3.forward);
            yield return Hold(1f);
            Begin(); yield return Hold(3f, Key.C); yield return Recover(6f);
            End("possum", expectRecovered: true, checkRoll: true);

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
            crossedSamples = walkingSamples = 0;
            maxSideSway = limpTime = rollTravel = rollSpin = 0f;
            rollSamples = 0;
            spinBone = "";
        }

        void End(string phase, bool checkJitter = false, bool checkGait = false, bool expectAirborne = false, bool expectRecovered = false,
            bool checkTravel = false, bool checkSway = false, bool checkRoll = false)
        {
            if (checkSway)
            {
                float swayTilt = activeSamples > 0 ? tiltSum / activeSamples : 0f;
                Log($"{phase}: torso tilt avg {swayTilt:0} max {maxTilt:0} deg, hips swing up to {maxSideSway * 100f:0} cm sideways off the capsule, jitter {(samples > 0 ? jitterSum / samples : 0f):0.00} rad/s");
                if (swayTilt > 12f || maxTilt > 30f) failures.Add($"{phase}: torso thrown about (avg {swayTilt:0}, max {maxTilt:0} deg)");
                if (maxSideSway > 0.3f) failures.Add($"{phase}: hips flung {maxSideSway * 100f:0} cm sideways");
            }
            if (checkRoll)
            {
                float spin = rollSamples > 0 ? rollSpin / rollSamples : 0f;
                Log($"{phase}: while lying limp the hips moved {rollTravel * 100f:0} cm and turned {spin:0.00} rad/s on average");
                if (rollTravel > 0.5f) failures.Add($"{phase}: body rolled {rollTravel:0.00} m while lying down");
                if (spin > 1.5f) failures.Add($"{phase}: body kept spinning while down ({spin:0.00} rad/s)");
            }
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
            if ((checkGait || checkJitter || checkTravel) && (tilt > 25f || maxTilt > 60f)) failures.Add($"{phase}: torso tipped (avg {tilt:0}, max {maxTilt:0} deg)");
            if (checkTravel)
            {
                float crossed = walkingSamples > 0 ? (float)crossedSamples / walkingSamples : 0f;
                Log($"{phase}: feet {feetTrail * 100f:0} cm behind the hips along the travel, crossed {crossed * 100f:0}% of steps, speed {body.Speed:0.0}");
                if (feetTrail > 0.2f) failures.Add($"{phase}: feet trail {feetTrail * 100f:0} cm behind the travel (towed, not stepping)");
                if (crossed > 0.15f) failures.Add($"{phase}: feet crossed {crossed * 100f:0}% of the time");
                if (body.Speed < 1.5f) failures.Add($"{phase}: body only reached {body.Speed:0.0} m/s");
            }
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
            float reach = remoteReachSamples > 0 ? remoteReachSum / remoteReachSamples : 0f;
            Log($"remote player view: torso tilt avg {tilt:0} deg, hips lag {lag * 100f:0} cm, stretch {remoteMaxStretch * 100f:0.0} cm, " +
                $"knockdowns {remoteLimpEpisodes}, recovered {remoteRecoveries}, hands {reach * 100f:0} cm from the carried box, throws {builder.Remote.Throws}");
            if (remoteReachSamples > 0 && reach > 0.25f) failures.Add($"remote: hands {reach * 100f:0} cm away from the box it carries");
            if (builder.Remote.Throws == 0) failures.Add("remote: never threw its box");
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
            var box = remote.CarriedBox;
            if (box != null && r.ActiveWeight > 0.99f)
            {
                var boxCollider = box.GetComponent<Collider>();
                foreach (var p in r.parts)
                    if (p.role == ActiveRagdollController.Role.Hand)
                    {
                        remoteReachSum += Vector3.Distance(p.body.position, boxCollider.ClosestPoint(p.body.position));
                        remoteReachSamples++;
                    }
            }
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
            remoteReachSum = 0f;
            remoteReachSamples = 0;
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
                if (!body.IsLimp)
                {
                    Vector3 off = body.Hips.position - root.position;
                    maxSideSway = Mathf.Max(maxSideSway, Mathf.Abs(Vector3.Dot(off, root.right)));
                }
                if (body.IsLimp && body.ActiveWeight < 0.01f)
                {
                    limpTime += Time.fixedDeltaTime;
                    if (limpTime > 1f)   // settled on the ground: from here a limp body should lie still
                    {
                        Vector3 delta = body.Hips.position - lastLimpHips;
                        rollTravel += new Vector2(delta.x, delta.z).magnitude;
                        rollSpin += body.Hips.angularVelocity.magnitude;
                        rollSamples++;
                    }
                    lastLimpHips = body.Hips.position;
                }
                hipHeightSum += body.Hips.position.y - root.position.y;

                if (!body.IsLimp && body.ActiveWeight > 0.99f)
                {
                    float tilt = Vector3.Angle(BodyAxis(bones["Chest"], Vector3.up), Vector3.up);
                    tiltSum += tilt;
                    maxTilt = Mathf.Max(maxTilt, tilt);
                    activeSamples++;
                    if (body.IsGrounded && body.Speed > 1f)
                    {
                        // Feet crossing: the left foot ends up right of the right foot (in his own frame).
                        Quaternion inverse = Quaternion.Inverse(Quaternion.LookRotation(Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized));
                        float leftX = (inverse * bones["LeftFoot"].position).x, rightX = (inverse * bones["RightFoot"].position).x;
                        walkingSamples++;
                        if (leftX > rightX - 0.02f) crossedSamples++;
                    }
                    foreach (var p in body.parts)
                    {
                        if (p.role == ActiveRagdollController.Role.UpperLeg) legLagSum += 0.5f * (SwingAngle(p, true) - SwingAngle(p, false));
                        if (p.role == ActiveRagdollController.Role.Foot)
                            feetTrailSum += 0.5f * Vector3.Dot(body.Hips.position - p.body.position, Travel());
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

        /// <summary>The body's direction of travel in the world.</summary>
        Vector3 Travel() => Quaternion.LookRotation(Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized) * body.MoveDirection;

        /// <summary>A character axis (in root space at rest) as a bone carries it now.</summary>
        Vector3 BodyAxis(Rigidbody bone, Vector3 characterAxis)
        {
            foreach (var p in body.parts)
                if (p.body == bone) return bone.rotation * Quaternion.Inverse(p.restRotation) * characterAxis;
            return root.rotation * characterAxis;
        }

        static void Log(string message) => Debug.Log($"RK-RAGDOLL {message}");

#if !ENABLE_INPUT_SYSTEM
        enum Key { W, A, S, D, G, LeftShift, Space, C }
#endif
    }
}
