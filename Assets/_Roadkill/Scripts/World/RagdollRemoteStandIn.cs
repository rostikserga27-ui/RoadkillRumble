using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// How other players see you, without a network: drives a kinematic capsule around a circle the way
    /// NetworkTransform moves a remote copy (MovePosition on a kinematic body, no PlayerMotor), and every
    /// few seconds plays an "owner got knocked down" episode (capsule tips over, PlayerNet's ragdolled
    /// flag goes up, then it stands back up). The fisherman on it runs the same code path as a real
    /// remote player's body.
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

        Rigidbody rb;
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
            }
        }
    }
}
