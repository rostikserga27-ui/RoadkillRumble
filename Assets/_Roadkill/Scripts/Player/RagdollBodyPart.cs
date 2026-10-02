using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Sits on each ragdoll bone: forwards its collisions to the ActiveRagdollController (knockouts,
    /// staggers) and lets world code find the player a body part belongs to, since the active ragdoll
    /// is detached from the player's hierarchy at runtime.
    /// </summary>
    public class RagdollBodyPart : MonoBehaviour
    {
        public ActiveRagdollController controller;

        public PlayerNet Player => controller != null ? controller.Player : null;

        void OnCollisionEnter(Collision collision)
        {
            if (controller != null) controller.OnPartCollision(this, collision);
        }
    }
}
