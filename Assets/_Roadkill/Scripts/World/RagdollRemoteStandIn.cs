using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// How other players see you, without a network: drives a kinematic capsule around a circle the way
    /// NetworkTransform moves a remote copy (MovePosition on a kinematic body, no PlayerMotor), and every
    /// few seconds plays an "owner got knocked down" episode (capsule tips over, PlayerNet's ragdolled
    /// flag goes up, then it stands back up). In between it carries a box, winds up and throws it, fed
    /// the way PlayerNet feeds a remote body (nearest point on the held object, shared throw charge).
    /// The fisherman on it runs the same code path as a real remote player's body.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class RagdollRemoteStandIn : MonoBehaviour
    {
        public ActiveRagdollController body;
        public Vector3 center = new Vector3(12f, 0.02f, -6f);
        public float radius = 4f;
        public float speed = 3.5f;
        public float knockdownEvery = 9f;
        public float knockdownSeconds = 3f;

        public bool KnockedDown { get; private set; }
        public bool Carrying => box != null && box.isKinematic;
        public int Throws { get; private set; }
        public Rigidbody CarriedBox => Carrying ? box : null;

        Rigidbody rb;
        Rigidbody box;
        float angle, clock;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            clock += dt;
            float cycle = clock % (knockdownEvery + knockdownSeconds);
            KnockedDown = cycle > knockdownEvery;

            Vector3 tangent = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            Quaternion facing = Quaternion.LookRotation(tangent, Vector3.up);
            if (KnockedDown)
            {
                // The owner's capsule lies where it fell; NetworkTransform shows it on its side.
                rb.MoveRotation(Quaternion.Slerp(rb.rotation, facing * Quaternion.Euler(-80f, 0f, 0f), dt * 6f));
            }
            else
            {
                angle += speed / radius * dt;
                rb.MovePosition(center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);
                rb.MoveRotation(Quaternion.Slerp(rb.rotation, facing, dt * 10f));
            }

            if (body != null)
            {
                body.FollowRootRagdoll(KnockedDown);       // what PlayerNet does with the networked flag
                body.SetLook(facing * Quaternion.Euler(Mathf.Sin(clock * 0.7f) * 15f, Mathf.Sin(clock * 0.4f) * 40f, 0f));
                CarryAndThrow(cycle, facing);
            }
        }

        /// <summary>From 1.5 s carry a box (held where the server's hand anchors would put it), wind up from 5.5 s, throw at 6.5 s.</summary>
        void CarryAndThrow(float cycle, Quaternion facing)
        {
            bool carryTime = !KnockedDown && cycle > 1.5f && cycle < 6.5f;
            if (carryTime && box == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Carried Box";
                go.transform.localScale = Vector3.one * 0.45f;
                go.GetComponent<Renderer>().sharedMaterial = RkMaterials.Get(new Color(0.66f, 0.5f, 0.3f));
                box = go.AddComponent<Rigidbody>();
                box.mass = 6f;
                box.isKinematic = true;
                box.interpolation = RigidbodyInterpolation.Interpolate;
                foreach (var c in body.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(c, go.GetComponent<Collider>(), true);
                Physics.IgnoreCollision(GetComponent<Collider>(), go.GetComponent<Collider>(), true);
                go.transform.position = transform.position + facing * new Vector3(0f, 1.1f, 0.7f);
            }
            float charge = carryTime ? Mathf.Clamp01(cycle - 5.5f) : 0f;
            if (Carrying)
            {
                box.MovePosition(transform.position + facing * new Vector3(0f, 1.1f + 0.25f * charge, 0.7f - 0.2f * charge));
                box.MoveRotation(facing);
                var boxCollider = box.GetComponent<Collider>();
                for (int i = 0; i < 2; i++) body.SetHandTarget(i, true, boxCollider.ClosestPoint(body.ShoulderPosition(i)));
                body.SetThrowCharge(charge);
                if (!carryTime)
                {
                    box.isKinematic = false;
                    if (KnockedDown) box.linearVelocity = Vector3.zero;
                    else
                    {
                        box.linearVelocity = facing * new Vector3(0f, 0.35f, 1f) * 9f;
                        body.Throw();
                        Throws++;
                    }
                    Destroy(box.gameObject, 3f);
                    box = null;
                }
            }
            if (!Carrying)
            {
                body.SetHandTarget(0, false, Vector3.zero);
                body.SetHandTarget(1, false, Vector3.zero);
                body.SetThrowCharge(0f);
            }
        }
    }
}
