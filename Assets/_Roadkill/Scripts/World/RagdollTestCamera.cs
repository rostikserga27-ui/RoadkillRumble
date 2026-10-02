using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Third-person view for the ragdoll test scene: orbits behind the player using the same yaw and
    /// pitch the PlayerMotor's mouse look produces (so WASD stays camera-relative) and keeps the
    /// floppy body's hips in frame. V switches to the game's own first-person camera pivot.
    /// </summary>
    public class RagdollTestCamera : MonoBehaviour
    {
        public PlayerMotor motor;
        public ActiveRagdollController body;
        public float distance = 4.2f;
        public float height = 0.5f;
        public float smoothing = 10f;
        [Tooltip("Orbit around the player: 0 behind, 90 from his left side, 180 in front.")]
        public float yawOffset;

        Vector3 focus;
        bool firstPerson;

        void Start()
        {
            if (body != null) focus = body.Hips.position;
        }

        void LateUpdate()
        {
            if (motor == null || body == null || body.Hips == null) return;
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.vKey.wasPressedThisFrame) firstPerson = !firstPerson;
#else
            if (Input.GetKeyDown(KeyCode.V)) firstPerson = !firstPerson;
#endif
            if (firstPerson)
            {
                transform.SetPositionAndRotation(motor.cameraPivot.position, motor.cameraPivot.rotation);
                return;
            }

            focus = Vector3.Lerp(focus, body.Hips.position + Vector3.up * 0.35f, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
            float pitch = motor.cameraPivot.localEulerAngles.x;
            if (pitch > 180f) pitch -= 360f;
            Quaternion view = Quaternion.Euler(Mathf.Clamp(pitch + 12f, -30f, 70f), motor.transform.eulerAngles.y + yawOffset, 0f);
            Vector3 desired = focus - view * Vector3.forward * distance + Vector3.up * height;
            if (Physics.Linecast(focus, desired, out var hit, ~0, QueryTriggerInteraction.Ignore) && hit.rigidbody == null)
                desired = hit.point + hit.normal * 0.2f;
            transform.position = desired;
            transform.rotation = Quaternion.LookRotation(focus - desired, Vector3.up);
        }
    }
}
