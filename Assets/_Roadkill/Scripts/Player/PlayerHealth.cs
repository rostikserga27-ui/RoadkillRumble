using System.Collections;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// 100 HP, shown as the fuel gauge. Phase 0 placeholder for going down: at 0 HP the player is
    /// knocked out and respawns. Downed state, bleed-out and revives arrive in phase 1 (GDD section 11).
    /// </summary>
    public class PlayerHealth : MonoBehaviour
    {
        public float maxHealth = 100f;
        public float respawnSeconds = 4f;

        public float Health { get; private set; }
        public bool IsDown { get; private set; }
        public string LastDamageText { get; private set; } = "";
        public float LastDamageTime { get; private set; } = -99f;

        PlayerMotor motor;

        void Awake() => Health = maxHealth;
        void Start() => motor = GetComponent<PlayerMotor>();

        public void Damage(float amount, string source)
        {
            if (IsDown || amount <= 0f || PlaygroundRules.Has(PlaygroundRules.Rule.NoDamage)) return;
            Health = Mathf.Max(0f, Health - amount);
            LastDamageText = $"-{Mathf.RoundToInt(amount)} ({source})";
            LastDamageTime = Time.time;
            if (Health <= 0f) StartCoroutine(KnockOut());
        }

        IEnumerator KnockOut()
        {
            IsDown = true;
            if (motor != null) motor.EnterRagdoll(respawnSeconds, Vector3.zero);
            yield return new WaitForSeconds(respawnSeconds);
            Health = maxHealth;
            IsDown = false;
            if (motor != null) motor.Respawn();
        }
    }
}
