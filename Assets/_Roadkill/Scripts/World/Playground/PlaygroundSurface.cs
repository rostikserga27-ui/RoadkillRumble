using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// What a player standing on this collider feels (read by PlayerMotor): grip (ice is low), the
    /// surface's own motion (a merry-go-round or conveyor carries you), trampoline bounce and soft
    /// landings (foam pits never knock you down). Props get the same effects from physics materials.
    /// </summary>
    public class PlaygroundSurface : MonoBehaviour
    {
        [Range(0f, 1f)] public float grip = 1f;
        [Tooltip("Belt speed in world space (m/s).")]
        public Vector3 conveyor;
        [Tooltip("Above 0: a trampoline that throws you up at this speed (at least).")]
        public float bounceSpeed;
        [Tooltip("Landing here never ragdolls you or hurts.")]
        public bool softLanding;

        KinematicMover mover;

        void Awake() => mover = GetComponentInParent<KinematicMover>();

        /// <summary>How fast the surface itself moves at a point on it.</summary>
        public Vector3 VelocityAt(Vector3 point) => conveyor + (mover != null ? mover.VelocityAt(point) : Vector3.zero);

        public static PlaygroundSurface Of(Collider collider) =>
            collider != null ? collider.GetComponentInParent<PlaygroundSurface>() : null;

        /// <summary>
        /// Whether playground forces should push this body here: anything this peer simulates (props on
        /// the server, local clutter, the local player's capsule), but a fisherman only while it leads
        /// (its owner's limp body); bodies that follow a capsule go where the capsule goes.
        /// </summary>
        public static bool Pushable(Rigidbody body)
        {
            if (body == null || body.isKinematic) return false;
            var part = body.GetComponent<RagdollBodyPart>();
            return part == null || (part.controller != null && part.controller.BodyLeads);
        }
    }
}
