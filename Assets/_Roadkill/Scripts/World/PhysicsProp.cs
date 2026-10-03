using UnityEngine;

namespace Roadkill
{
    /// <summary>Anything grabbable with a name for the HUD. Fragile value and part quirks hang off this later.</summary>
    public class PhysicsProp : MonoBehaviour
    {
        public string displayName = "Junk";
        [Tooltip("Above 0: the prop's colliders get a bouncy material at runtime (dodgeballs).")]
        [Range(0f, 1f)] public float bounciness;

        void Awake()
        {
            if (bounciness <= 0f) return;
            var material = new PhysicsMaterial($"{displayName} Bounce")
            {
                bounciness = bounciness,
                dynamicFriction = 0.4f,
                staticFriction = 0.5f,
                bounceCombine = PhysicsMaterialCombine.Maximum
            };
            foreach (var c in GetComponentsInChildren<Collider>()) c.sharedMaterial = material;
        }
    }
}
