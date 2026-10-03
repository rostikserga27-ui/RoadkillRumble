using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// 100 HP, shown as the fuel gauge. At 0 HP the player is out cold (GDD section 11): limp on the ground
    /// for unconsciousSeconds, while friends can drag the body or revive them (PlayerKnockout); then they
    /// respawn. Owner only, like the rest of the player's own state.
    /// </summary>
    public class PlayerHealth : MonoBehaviour
    {
        public float maxHealth = 100f;
        [Tooltip("Out cold this long before respawning, unless a friend gets you up first.")]
        public float unconsciousSeconds = 45f;

        public float Health { get; private set; }
        /// <summary>Out cold.</summary>
        public bool IsDown { get; private set; }
        public float DownSecondsLeft { get; private set; }
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
            if (Health <= 0f) KnockOut();
        }

        void KnockOut()
        {
            IsDown = true;
            DownSecondsLeft = unconsciousSeconds;
            if (motor != null) motor.EnterRagdoll(Mathf.Infinity, Vector3.zero);   // until revived or respawned
        }

        void Update()
        {
            if (!IsDown) return;
            DownSecondsLeft -= Time.deltaTime;
            if (DownSecondsLeft <= 0f) RespawnNow();
        }

        /// <summary>A friend got you up: back on your feet where you lie, with a little health.</summary>
        public void Revive(float health)
        {
            if (!IsDown) return;
            IsDown = false;
            Health = Mathf.Clamp(health, 1f, maxHealth);
            if (motor != null) motor.WakeUp();
        }

        /// <summary>Out of time, or gave up: full health at the spawn point (later the car's back seat).</summary>
        public void RespawnNow()
        {
            IsDown = false;
            Health = maxHealth;
            DownSecondsLeft = 0f;
            if (motor != null) motor.Respawn();
        }
    }
}
