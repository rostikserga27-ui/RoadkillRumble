using Unity.Netcode;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// A big floor button the server watches: any player standing on it fires the action (with a cooldown).
    /// The server sees every player's capsule, so nobody has to send anything. The cap is visual only.
    /// </summary>
    public class PressurePlate : MonoBehaviour
    {
        public PlaygroundRules.Action action = PlaygroundRules.Action.ResetProps;
        public Vector3 size = new Vector3(1.6f, 1f, 1.6f);
        public float cooldown = 4f;
        public Transform cap;

        readonly Collider[] hits = new Collider[16];
        float ready;
        float pressed;
        Vector3 capRest;

        void Start()
        {
            if (cap != null) capRest = cap.localPosition;
        }

        void FixedUpdate()
        {
            var network = NetworkManager.Singleton;
            if (network == null || !network.IsServer || !network.IsListening || Time.time < ready) return;
            int n = Physics.OverlapBoxNonAlloc(transform.position + Vector3.up * size.y * 0.5f, size * 0.5f, hits,
                Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var player = hits[i].GetComponentInParent<PlayerNet>();
                if (player == null || PlaygroundRules.Instance == null) continue;
                ready = Time.time + cooldown;
                PlaygroundRules.Instance.ServerAction(action, player.OwnerClientId);
                return;
            }
        }

        void Update()
        {
            if (cap == null) return;
            // Everyone sees it go down while somebody stands on it.
            pressed = Mathf.MoveTowards(pressed, Occupied() ? 1f : 0f, Time.deltaTime * 6f);
            cap.localPosition = capRest - Vector3.up * (0.12f * pressed);
        }

        bool Occupied()
        {
            int n = Physics.OverlapBoxNonAlloc(transform.position + Vector3.up * 0.5f, new Vector3(size.x * 0.5f, 0.5f, size.z * 0.5f), hits,
                Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (hits[i].GetComponentInParent<PlayerMotor>() != null || hits[i].GetComponentInParent<RagdollBodyPart>() != null) return true;
            return false;
        }
    }
}
