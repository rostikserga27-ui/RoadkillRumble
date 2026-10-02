using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Cosmetic bone ragdoll on the character model (GDD section 3). Gameplay physics stays on the
    /// player's capsule; this only makes the body flop. While inactive every bone is kinematic and
    /// CharacterAnimator poses it. When the player ragdolls, the bones go physical and the hips are
    /// pinned to the capsule, so the floppy body never drifts away from the networked position.
    /// Built by the prefab generator; not used on the owner, who does not see their own body.
    /// </summary>
    public class RagdollRig : MonoBehaviour
    {
        public Rigidbody root;        // the player's capsule body
        public Rigidbody hips;
        public Rigidbody[] bodies;
        public Collider[] colliders;
        [Tooltip("How far the hips may drift from the capsule while ragdolled (metres).")]
        public float pinSlack = 0.4f;

        public bool IsActive { get; private set; }

        ConfigurableJoint pin;

        void Awake()
        {
            var capsule = root.GetComponent<Collider>();
            foreach (var c in colliders) Physics.IgnoreCollision(capsule, c, true);
            foreach (var b in bodies)
            {
                b.isKinematic = true;
                // Interpolation would overwrite the animated pose every frame; only use it while physical.
                b.interpolation = RigidbodyInterpolation.None;
            }
        }

        public void SetCollidersEnabled(bool enabled)
        {
            foreach (var c in colliders) c.enabled = enabled;
        }

        public void SetActive(bool active, Vector3 velocity = default)
        {
            if (active == IsActive) return;
            IsActive = active;

            foreach (var b in bodies)
            {
                b.isKinematic = !active;
                b.interpolation = active ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
                if (active) b.linearVelocity = velocity;
            }

            if (active)
            {
                // Hips ride along with the capsule; everything else hangs off them.
                pin = hips.gameObject.AddComponent<ConfigurableJoint>();
                pin.connectedBody = root;
                pin.autoConfigureConnectedAnchor = true;
                // A leash, not a weld: the hips may sag up to pinSlack from the capsule's centre, so the
                // body settles onto the ground instead of hovering at the capsule's radius.
                pin.xMotion = pin.yMotion = pin.zMotion = ConfigurableJointMotion.Limited;
                pin.linearLimit = new SoftJointLimit { limit = pinSlack };
                pin.linearLimitSpring = new SoftJointLimitSpring { spring = 400f, damper = 40f };
                pin.angularXMotion = pin.angularYMotion = pin.angularZMotion = ConfigurableJointMotion.Free;
                pin.enableCollision = false;
            }
            else if (pin != null)
            {
                Destroy(pin);
                pin = null;
            }
        }
    }
}
