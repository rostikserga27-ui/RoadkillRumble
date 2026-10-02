using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Keeps the swinging log moving: pushes it along its motion whenever it slows near the bottom.
    /// Runs only where the log is simulated (the server); client copies are kinematic.
    /// </summary>
    public class KeepSwinging : MonoBehaviour
    {
        public float ropeLength = 4.7f;
        public float bottomSpeed = 6f;
        Rigidbody body;
        Vector3 pivot;
        bool kicked;

        void Start()
        {
            body = GetComponent<Rigidbody>();
            pivot = body.position + Vector3.up * ropeLength;
        }

        void FixedUpdate()
        {
            if (body == null || body.isKinematic) return;
            if (!kicked)
            {
                body.linearVelocity = Vector3.forward * bottomSpeed;
                kicked = true;
            }
            Vector3 v = body.linearVelocity;
            bool nearBottom = pivot.y - body.position.y > ropeLength * 0.95f;
            if (!nearBottom || v.magnitude >= bottomSpeed) return;
            Vector3 direction = v.sqrMagnitude > 0.04f ? v.normalized : Vector3.forward;
            body.AddForce(direction * 8f, ForceMode.Acceleration);
        }
    }
}
