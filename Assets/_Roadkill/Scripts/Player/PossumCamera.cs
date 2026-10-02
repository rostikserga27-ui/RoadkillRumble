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
            if (!player.Motor.IsRagdolled) followingFall = false;

            Vector3 firstPosition = view.parent.TransformPoint(restPosition);
            Quaternion firstRotation = view.parent.rotation * restRotation;
            if (!IsTransitioning)
            {
                view.SetPositionAndRotation(firstPosition, firstRotation);
                position = firstPosition;
                rotation = firstRotation;
                return;
            }

            Vector3 focus = transform.position + Vector3.up * 0.65f;
            Vector3 targetPosition = followingFall
                ? focus + Vector3.up * height - orbit * Vector3.forward * distance
                : firstPosition;
            Quaternion targetRotation = followingFall
                ? Quaternion.LookRotation(focus - targetPosition, Vector3.up)
                : firstRotation;
            position = Vector3.SmoothDamp(position, targetPosition, ref positionVelocity,
                Mathf.Max(0.01f, transitionSeconds));
            rotation = Quaternion.Slerp(rotation, targetRotation,
                1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, transitionSeconds)));

            // Resolve after smoothing as well, so easing cannot leave the camera behind a wall.
            Vector3 safePosition = AvoidObstacles(focus, position);
            view.SetPositionAndRotation(safePosition, rotation);
            if (!followingFall && Vector3.Distance(position, firstPosition) < 0.015f
                && Quaternion.Angle(rotation, firstRotation) < 0.5f)
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
