using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// A moving obstacle (spinner arm, hammer) that knocks down whoever it hits. Each player's own machine
    /// decides, as with falls: the hazard moves identically everywhere (KinematicMover), so the owner's
    /// capsule meets it exactly when that player sees it. Props are simply batted away by physics.
    /// </summary>
    public class Hazard : MonoBehaviour
    {
        [Tooltip("The hazard's own speed at the contact must exceed this to knock you down.")]
        public float minSpeed = 1.5f;
        public float kickScale = 1.1f;
        public float upKick = 3f;
        public float maxKick = 14f;
        public float downtime = 2f;
        public float damage = 5f;

        KinematicMover mover;

        void Awake() => mover = GetComponentInParent<KinematicMover>();

        void OnCollisionEnter(Collision collision) => Hit(collision);
        void OnCollisionStay(Collision collision) => Hit(collision);

        void Hit(Collision collision)
        {
            var body = collision.rigidbody;
            var motor = body != null ? body.GetComponent<PlayerMotor>() : null;
            if (motor == null || !motor.enabled || motor.IsRagdolled || collision.contactCount == 0) return;

            Vector3 point = collision.GetContact(0).point;
            Vector3 velocity = mover != null ? mover.VelocityAt(point) : Vector3.zero;
            if (velocity.magnitude < minSpeed) return;

            Vector3 flat = Vector3.ProjectOnPlane(velocity, Vector3.up) * kickScale;
            Vector3 kick = Vector3.ClampMagnitude(flat + Vector3.up * upKick, maxKick);
            motor.EnterRagdoll(downtime, kick);
            var health = motor.GetComponent<PlayerHealth>();
            if (health != null) health.Damage(damage, "smacked");
        }
    }
}
