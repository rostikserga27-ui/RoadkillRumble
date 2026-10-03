using Unity.Netcode;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// How a player takes a punch: the stagger / knockout side of fists.
    ///
    /// Every peer: the impulse lands on the struck bone of its copy of the body, at the contact point
    /// (so a head, a chest and a leg each tumble differently), and the body staggers with its joints
    /// softened for a moment. Owner: the capsule is shoved (feet slide for a moment) or, on a hard
    /// enough hit, knocked down into a full ragdoll; damage is taken and the camera jolts.
    /// The server decides all of it (PunchHitResolver) and only sends the result.
    /// Also tells the server when this player (re)spawns, for friendly fire's spawn protection.
    /// </summary>
    public class PunchReaction : NetworkBehaviour
    {
        PlayerNet player;
        int resetVersion;
        float lastProtectRequest = -99f;

        /// <summary>Owner only: the camera shake and kick for hits given and taken.</summary>
        public PunchCameraFx CameraFx { get; private set; }

        void Awake() => player = GetComponent<PlayerNet>();

        public override void OnNetworkSpawn()
        {
            if (IsServer) FriendlyFire.ServerProtect(OwnerClientId);
            if (!IsOwner || player.playerCamera == null) return;
            CameraFx = player.playerCamera.gameObject.AddComponent<PunchCameraFx>();
            CameraFx.view = player.playerCamera.transform;
            resetVersion = player.Motor.CameraResetVersion;
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner) return;
            // Respawned or gathered: ask for a moment of spawn protection.
            if (player.Motor.CameraResetVersion != resetVersion)
            {
                resetVersion = player.Motor.CameraResetVersion;
                if (Time.time - lastProtectRequest > 1f)
                {
                    lastProtectRequest = Time.time;
                    ProtectRpc();
                }
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void ProtectRpc() => FriendlyFire.ServerProtect(OwnerClientId);

        /// <summary>
        /// Every peer: knock this peer's copy of the body at `point` on bone `part` and make it stagger. The
        /// struck bone takes as much of the impulse as it can without snapping (PunchConfig.maxBoneKick);
        /// the rest moves the whole body, so a haymaker to the head whips the head and rocks the man.
        /// </summary>
        public void ApplyToBody(int part, Vector3 point, Vector3 impulse, float staggerSeconds, float staggerStiffness)
        {
            var body = player.body;
            if (body == null || !body.IsSimulated || body.IsPuppet) return;   // a puppet replays its owner's pose
            float total = impulse.magnitude;
            if (part >= 0 && part < body.PartCount && total > 0f)
            {
                var bone = body.PartBody(part);
                if (!bone.isKinematic)
                {
                    float onBone = Mathf.Min(total, bone.mass * PunchConfig.Current.maxBoneKick);
                    Vector3 direction = impulse / total;
                    bone.AddForceAtPosition(direction * onBone, point, ForceMode.Impulse);
                    if (total > onBone) body.AddVelocity(direction * ((total - onBone) / Mathf.Max(1f, body.TotalMass)));
                }
            }
            body.Stagger(staggerSeconds, staggerStiffness);
        }

        /// <summary>Server: what the punch does to this player on their own machine.</summary>
        public void ServerReact(float damage, float knockdownSeconds, Vector3 kick, Vector3 shove, ulong attacker, float power)
        {
            if (!IsServer) return;
            ReactRpc(damage, knockdownSeconds, kick, shove, attacker, power);
        }

        [Rpc(SendTo.Owner)]
        void ReactRpc(float damage, float knockdownSeconds, Vector3 kick, Vector3 shove, ulong attacker, float power)
        {
            var c = PunchConfig.Current;
            if (knockdownSeconds > 0f) player.Motor.EnterRagdoll(knockdownSeconds, kick);
            else if (shove.sqrMagnitude > 0.0001f) player.Motor.Shove(shove, c.stumbleSeconds);
            if (damage > 0f) player.Health.Damage(damage, $"punched by {PlayerNet.NameOf(attacker)}");

            if (CameraFx == null) return;
            // Snap the view the way the blow pushes (rotating forward toward the push; straight on, the head snaps back).
            Transform view = player.playerCamera.transform;
            Vector3 push = view.InverseTransformDirection(kick.sqrMagnitude > 0.0001f ? kick : shove);
            Vector3 axis = Vector3.Cross(Vector3.forward, push);
            if (axis.sqrMagnitude < 0.001f) axis = Vector3.left;
            CameraFx.Hit(Mathf.Lerp(c.shakeLight, c.shakeHeavy, power) * 1.5f, c.shakeSeconds * 1.5f, c.victimCameraKick * Mathf.Max(0.3f, power), axis);
        }
    }
}
