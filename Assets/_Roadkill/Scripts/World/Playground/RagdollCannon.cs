using System.Collections.Generic;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Stand on the pad, wait for the count, and you are fired limp along an arc that lands on the target.
    /// The local player is launched by their own machine (the network then carries where the body flies);
    /// props dropped on the pad are fired by the server the same way.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class RagdollCannon : MonoBehaviour
    {
        public Vector3 target;
        [Tooltip("How far above the higher end the arc peaks (m).")]
        public float apexHeight = 7f;
        public float fuseSeconds = 1.2f;
        public float propFuseSeconds = 0.6f;
        [Tooltip("Limbs drag a little in flight; aim this much further.")]
        public float bodyCarry = 1.08f;
        public float downtime = 3f;
        public TextMesh countdown;
        public Transform barrel;

        PlayerMotor seenPlayer;
        PlayerMotor loaded;
        float fuse;
        float recoil;
        readonly HashSet<Rigidbody> seenProps = new HashSet<Rigidbody>();
        readonly Dictionary<Rigidbody, float> props = new Dictionary<Rigidbody, float>();
        readonly List<Rigidbody> fired = new List<Rigidbody>();
        Vector3 barrelRest;
        BoxCollider box;
        readonly Collider[] hits = new Collider[32];

        void Start()
        {
            box = GetComponent<BoxCollider>();
            box.isTrigger = true;
            if (barrel != null) barrelRest = barrel.localPosition;
        }

        /// <summary>Who is on the pad. A query, not trigger callbacks: those stop for a player standing still (asleep).</summary>
        void Look()
        {
            int n = Physics.OverlapBoxNonAlloc(transform.TransformPoint(box.center), Vector3.Scale(box.size, transform.lossyScale) * 0.5f,
                hits, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var body = hits[i].attachedRigidbody;
                if (body == null) continue;
                var motor = body.GetComponent<PlayerMotor>();
                if (motor != null)
                {
                    if (motor.enabled && !motor.IsRagdolled) seenPlayer = motor;
                    continue;
                }
                if (body.GetComponent<RagdollBodyPart>() == null && PlaygroundSurface.Pushable(body)) seenProps.Add(body);
            }
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            Look();

            // The local player: count down while they stay on the pad.
            if (seenPlayer != null && seenPlayer == loaded) fuse += dt;
            else fuse = 0f;
            loaded = seenPlayer;
            seenPlayer = null;
            if (loaded != null && fuse >= fuseSeconds)
            {
                Fire(loaded);
                loaded = null;
                fuse = 0f;
            }

            // Props: each fires after its own short fuse.
            fired.Clear();
            foreach (var body in seenProps)
            {
                props.TryGetValue(body, out float time);
                props[body] = time + dt;
                if (time + dt >= propFuseSeconds) fired.Add(body);
            }
            foreach (var body in new List<Rigidbody>(props.Keys))
                if (!seenProps.Contains(body)) props.Remove(body);
            seenProps.Clear();
            foreach (var body in fired)
            {
                props.Remove(body);
                body.linearVelocity = Ballistic(body.worldCenterOfMass, target, -Physics.gravity.y, apexHeight);
                body.angularVelocity = Random.insideUnitSphere * 4f;
                recoil = 1f;
            }
        }

        void Fire(PlayerMotor motor)
        {
            var body = motor.body != null && motor.body.enabled ? motor.body : null;
            float gravity = -Physics.gravity.y * (body != null ? body.gravityMultiplier : 1f);
            Vector3 from = body != null ? body.Hips.position : motor.transform.position + Vector3.up;
            Vector3 aim = target + Vector3.up * (body != null ? body.hipHeight : 1f);
            Vector3 velocity = Ballistic(from, aim, gravity, apexHeight);
            velocity = new Vector3(velocity.x * bodyCarry, velocity.y, velocity.z * bodyCarry);
            Vector3 current = body != null ? body.Hips.linearVelocity : motor.GetComponent<Rigidbody>().linearVelocity;
            motor.EnterRagdoll(downtime, velocity - current);
            recoil = 1f;
        }

        /// <summary>Launch velocity from `from` that lands on `to` after peaking `apex` above the higher of the two.</summary>
        public static Vector3 Ballistic(Vector3 from, Vector3 to, float gravity, float apex)
        {
            float top = Mathf.Max(from.y, to.y) + apex;
            float up = Mathf.Sqrt(2f * gravity * (top - from.y));
            float time = up / gravity + Mathf.Sqrt(2f * (top - to.y) / gravity);
            Vector3 flat = Vector3.ProjectOnPlane(to - from, Vector3.up);
            return flat / time + Vector3.up * up;
        }

        void Update()
        {
            if (countdown != null)
                countdown.text = loaded != null ? Mathf.CeilToInt(Mathf.Max(0.01f, fuseSeconds - fuse)).ToString() : "";
            if (barrel != null)
            {
                recoil = Mathf.MoveTowards(recoil, 0f, Time.deltaTime * 3f);
                barrel.localPosition = barrelRest - barrel.localRotation * Vector3.up * (0.4f * recoil);
            }
        }
    }
}
