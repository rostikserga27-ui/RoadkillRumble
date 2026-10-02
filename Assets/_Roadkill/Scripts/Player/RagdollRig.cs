using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Bone ragdoll on the character model (GDD section 3). While inactive every bone is kinematic and
    /// CharacterAnimator poses it. When the player goes down, the model leaves the player hierarchy and
    /// its bones go physical, so every limb collides with the world on its own.
    ///
    /// Owner: the body is free and PlayerMotor drags the (collider-less) capsule along behind the hips,
    /// which is what the NetworkTransform sends. Networked copies: the hips are pinned to that capsule,
    /// so the floppy body never drifts away from the owner's position.
    /// Built by the prefab generator.
    /// </summary>
    public class RagdollRig : MonoBehaviour
    {
        public Rigidbody root;        // the player's capsule body
        public Rigidbody hips;
        public Rigidbody[] bodies;    // parents before children
        public Collider[] colliders;
        [Tooltip("How far the hips may drift from the capsule while ragdolled and pinned (metres).")]
        public float pinSlack = 0.4f;
        [Tooltip("Bone colliders stay on while the ragdoll is off. The owner turns this off: their own body " +
                 "must not trip their ground check or block their grab ray.")]
        public bool collidersWhenIdle = true;

        public bool IsActive { get; private set; }
        public PlayerNet Player { get; private set; }
        /// <summary>Height of the hips above the player's feet in the rest pose.</summary>
        public float HipsHeight { get; private set; }
        public Vector3 Velocity => hips.linearVelocity;

        ConfigurableJoint pin;
        Transform home;
        Vector3 homePosition;
        Quaternion homeRotation;
        Vector3[] restPositions;
        Quaternion[] restRotations;
        Vector3[] savedPositions;
        Quaternion[] savedRotations;

        void Awake()
        {
            home = transform.parent;
            homePosition = transform.localPosition;
            homeRotation = transform.localRotation;
            Player = GetComponentInParent<PlayerNet>();
            HipsHeight = root.transform.InverseTransformPoint(hips.position).y;

            IgnoreOwnCollisions();   // in Awake the model is still in its rest pose

            restPositions = new Vector3[bodies.Length];
            restRotations = new Quaternion[bodies.Length];
            savedPositions = new Vector3[bodies.Length];
            savedRotations = new Quaternion[bodies.Length];
            for (int i = 0; i < bodies.Length; i++)
            {
                var b = bodies[i];
                restPositions[i] = b.transform.localPosition;
                restRotations[i] = b.transform.localRotation;
                b.isKinematic = true;
                // Interpolation would overwrite the animated pose every frame; only use it while physical.
                b.interpolation = RigidbodyInterpolation.None;
            }
        }

        public void SetCollidersEnabled(bool enabled)
        {
            foreach (var c in colliders) c.enabled = enabled;
            // Re-applied after enabling, in case toggling a collider dropped the ignore pairs.
            if (enabled) IgnoreOwnCollisions();
        }

        void IgnoreOwnCollisions()
        {
            var capsule = root.GetComponent<Collider>();
            foreach (var c in colliders) Physics.IgnoreCollision(capsule, c, true);
            // Limbs collide with the rest of the body, so arms cannot sink into the torso. Bones sharing a
            // joint are already exempt; pairs that overlap in the rest pose are too, or they would shove
            // each other apart the moment the body goes limp.
            if (restOverlaps == null) FindRestOverlaps();
            foreach (var (a, b) in restOverlaps) Physics.IgnoreCollision(a, b, true);
        }

        System.Collections.Generic.List<(Collider, Collider)> restOverlaps;

        void FindRestOverlaps()
        {
            restOverlaps = new System.Collections.Generic.List<(Collider, Collider)>();
            for (int i = 0; i < colliders.Length; i++)
            {
                for (int j = i + 1; j < colliders.Length; j++)
                {
                    Collider a = colliders[i], b = colliders[j];
                    if (Physics.ComputePenetration(a, a.transform.position, a.transform.rotation,
                            b, b.transform.position, b.transform.rotation, out _, out float depth) && depth > 0.005f)
                        restOverlaps.Add((a, b));
                }
            }
        }

        /// <param name="pinned">Leash the hips to the capsule (networked copies). The owner's body is free.</param>
        /// <param name="snapToRest">On deactivate: jump to the rest pose instead of blending from where it lies.</param>
        public void SetActive(bool active, Vector3 velocity = default, bool pinned = true, bool snapToRest = false)
        {
            if (active == IsActive) return;
            IsActive = active;

            if (active)
            {
                // Out of the player hierarchy: the capsule keeps moving while we are down, and moving a
                // parent transform would teleport the simulated bones along with it.
                transform.SetParent(null, true);
                SetCollidersEnabled(true);
                foreach (var b in bodies)
                {
                    b.isKinematic = false;
                    b.interpolation = RigidbodyInterpolation.Interpolate;
                    b.linearVelocity = velocity;
                    b.angularVelocity = Vector3.zero;
                }
                if (pinned) Pin();
                return;
            }

            if (pin != null)
            {
                Destroy(pin);
                pin = null;
            }
            for (int i = 0; i < bodies.Length; i++)
            {
                var b = bodies[i];
                b.isKinematic = true;
                b.interpolation = RigidbodyInterpolation.None;
                savedPositions[i] = b.transform.position;
                savedRotations[i] = b.transform.rotation;
            }

            // Back into the player. The bones keep lying where they fell and CharacterAnimator blends
            // them home, so standing up reads as getting up rather than a pop.
            transform.SetParent(home, false);
            transform.localPosition = homePosition;
            transform.localRotation = homeRotation;
            for (int i = 0; i < bodies.Length; i++)
            {
                if (snapToRest)
                {
                    bodies[i].transform.localPosition = restPositions[i];
                    bodies[i].transform.localRotation = restRotations[i];
                }
                else
                {
                    bodies[i].transform.SetPositionAndRotation(savedPositions[i], savedRotations[i]);
                }
            }
            SetCollidersEnabled(collidersWhenIdle);
        }

        /// <summary>Add the same velocity to every bone (a hit while already down).</summary>
        public void AddVelocity(Vector3 velocity)
        {
            if (!IsActive) return;
            foreach (var b in bodies) b.AddForce(velocity, ForceMode.VelocityChange);
        }

        /// <summary>Push the upper body over: bones get speed in proportion to their height above the hips.</summary>
        public void Topple(Vector3 direction, float speedPerMetre)
        {
            if (!IsActive) return;
            foreach (var b in bodies)
            {
                float height = Mathf.Max(0f, b.position.y - hips.position.y);
                b.AddForce(direction * (height * speedPerMetre), ForceMode.VelocityChange);
            }
        }

        void Pin()
        {
            // Hips ride along with the capsule; everything else hangs off them.
            pin = hips.gameObject.AddComponent<ConfigurableJoint>();
            pin.connectedBody = root;
            pin.autoConfigureConnectedAnchor = false;
            pin.anchor = Vector3.zero;
            // The owner keeps the capsule HipsHeight below the hips, so aim at that point.
            pin.connectedAnchor = Vector3.up * HipsHeight;
            // A leash, not a weld: the hips may sag up to pinSlack from it, so the body settles onto the
            // ground instead of hovering.
            pin.xMotion = pin.yMotion = pin.zMotion = ConfigurableJointMotion.Limited;
            pin.linearLimit = new SoftJointLimit { limit = pinSlack };
            pin.linearLimitSpring = new SoftJointLimitSpring { spring = 1200f, damper = 120f };
            pin.angularXMotion = pin.angularYMotion = pin.angularZMotion = ConfigurableJointMotion.Free;
            pin.enableCollision = false;
        }
    }
}
