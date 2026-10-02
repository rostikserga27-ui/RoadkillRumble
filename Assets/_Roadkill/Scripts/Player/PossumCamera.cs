using UnityEngine;

namespace Roadkill
{
    /// <summary>Owner-only camera pullback while playing possum, including recovery.</summary>
    [DefaultExecutionOrder(200)]
    public class PossumCamera : MonoBehaviour
    {
        public float distance = 3f;
        public float height = 1.2f;
        public float transitionSeconds = 0.18f;
        public bool IsTransitioning { get; private set; }

        PlayerNet player;
        Transform view;
        Vector3 restPosition;
        Quaternion restRotation;
        Vector3 position;
        Quaternion rotation;
        Vector3 positionVelocity;
        Quaternion orbit;
        bool followingFall;
        float returnProgress;
        Vector3 returnOffset;
        Quaternion returnRotation;
        int resetVersion;

        public void Initialize(PlayerNet owner)
        {
            player = owner;
            view = owner.playerCamera.transform;
            restPosition = view.localPosition;
            restRotation = view.localRotation;
            ResetView();
        }

        void ResetView()
        {
            view.localPosition = restPosition;
            view.localRotation = restRotation;
            position = view.position;
            rotation = view.rotation;
            positionVelocity = Vector3.zero;
            followingFall = IsTransitioning = false;
            resetVersion = player.Motor.CameraResetVersion;
            SetVisuals(false);
        }

        void LateUpdate()
        {
            if (player == null || !player.IsSpawned || !player.IsOwner) return;
            if (resetVersion != player.Motor.CameraResetVersion) ResetView();

            if (player.Motor.IsPossum && !followingFall)
            {
                followingFall = IsTransitioning = true;
                // Capture the viewing direction once; capsule tumbling must not roll the camera.
                orbit = Quaternion.Euler(0f, rotation.eulerAngles.y, 0f);
            }
            Vector3 firstPosition = view.parent.TransformPoint(restPosition);
            Quaternion firstRotation = view.parent.rotation * restRotation;
            if (followingFall && !player.Motor.IsRagdolled)
            {
                followingFall = false;
                returnProgress = 0f;
                returnOffset = position - firstPosition;
                returnRotation = rotation;
            }
            if (!IsTransitioning)
            {
                view.SetPositionAndRotation(firstPosition, firstRotation);
                position = firstPosition;
                rotation = firstRotation;
                return;
            }

            var ragdoll = player.ragdoll;
            Vector3 focus = ragdoll != null && ragdoll.IsActive
                ? ragdoll.hips.position + Vector3.up * 0.2f
                : transform.position + Vector3.up * 0.65f;
            Vector3 targetPosition = followingFall
                ? focus + Vector3.up * height - orbit * Vector3.forward * distance
                : firstPosition;
            Quaternion targetRotation = followingFall
                ? Quaternion.LookRotation(focus - targetPosition, Vector3.up)
                : firstRotation;
            if (followingFall)
            {
                position = Vector3.SmoothDamp(position, targetPosition, ref positionVelocity,
                    Mathf.Max(0.01f, transitionSeconds));
                rotation = Quaternion.Slerp(rotation, targetRotation,
                    1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, transitionSeconds)));
            }
            else
            {
                // A finite blend follows the moving eyes, so walking or turning cannot keep
                // the camera (and grab controls) stuck in the return transition indefinitely.
                returnProgress = Mathf.Clamp01(returnProgress + Time.deltaTime /
                    Mathf.Max(0.01f, transitionSeconds * 3f));
                float blend = Mathf.SmoothStep(0f, 1f, returnProgress);
                position = firstPosition + returnOffset * (1f - blend);
                rotation = Quaternion.Slerp(returnRotation, firstRotation, blend);
                positionVelocity = Vector3.zero;
            }

            // Resolve after smoothing as well, so easing cannot leave the camera behind a wall.
            Vector3 safePosition = AvoidObstacles(focus, position);
            view.SetPositionAndRotation(safePosition, rotation);
            if (!followingFall && returnProgress >= 1f)
            {
                ResetView();
                return;
            }

            Vector3 headPosition = player.head != null ? player.head.position : firstPosition;
            bool showBody = Vector3.Distance(safePosition, headPosition) > 0.6f
                && Vector3.Distance(safePosition, firstPosition) > 0.6f;
            SetVisuals(showBody);
        }

        Vector3 AvoidObstacles(Vector3 origin, Vector3 desired)
        {
            Vector3 offset = desired - origin;
            float length = offset.magnitude;
            if (length < 0.001f) return desired;
            float allowed = length;
            foreach (var hit in Physics.SphereCastAll(origin, 0.15f, offset / length,
                length, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform)) continue;
                // While down, the body lives outside the player hierarchy.
                if (player.ragdoll != null && hit.collider.transform.IsChildOf(player.ragdoll.transform)) continue;
                allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - 0.05f));
            }
            return origin + offset / length * allowed;
        }

        void SetVisuals(bool showBody)
        {
            foreach (var renderer in player.bodyRenderers) renderer.enabled = showBody;
            if (player.Hands.Left.Visual != null)
                player.Hands.Left.Visual.gameObject.SetActive(!IsTransitioning);
            if (player.Hands.Right.Visual != null)
                player.Hands.Right.Visual.gameObject.SetActive(!IsTransitioning);
        }
    }
}
