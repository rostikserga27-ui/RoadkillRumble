using UnityEngine;

namespace Roadkill
{
    /// <summary>Where a fist landed: decides the damage and knockback multipliers.</summary>
    public enum HitZone : byte { Head, Torso, Limb, Prop, Other }

    /// <summary>What one punch does, from its physics: worked out the same way by the attacker's prediction and the server.</summary>
    public struct PunchOutcome
    {
        public float Momentum;          // fist speed x effective mass (kg m/s)
        public float Power;             // 0..1 of a full-power punch
        public float Damage;
        public float Impulse;           // N s on the struck bone or prop
        public float Shove;             // m/s added to the victim's capsule
        public float KnockdownSeconds;  // > 0: full ragdoll
        public float StaggerSeconds;
        public float StaggerStiffness;
        public bool Comedic;
    }

    /// <summary>
    /// Anything an NPC or special object can implement to be punched. Called on the server only, after
    /// the hit was validated; the attacker's FX (sparks, sound) are already handled by the fists.
    /// </summary>
    public interface IPunchable
    {
        void OnPunched(PunchHitInfo hit);
    }

    public struct PunchHitInfo
    {
        public PlayerNet Attacker;
        public Vector3 Point;
        public Vector3 Direction;   // unit, the way the fist was going
        public PunchOutcome Outcome;
    }

    public static class PunchMath
    {
        public static HitZone ZoneOf(ActiveRagdollController.Role role)
        {
            switch (role)
            {
                case ActiveRagdollController.Role.Head: return HitZone.Head;
                case ActiveRagdollController.Role.Hips:
                case ActiveRagdollController.Role.Spine:
                case ActiveRagdollController.Role.Chest: return HitZone.Torso;
                default: return HitZone.Limb;
            }
        }

        /// <summary>
        /// Damage, knockback and the victim's reaction for a fist arriving at `speed` (m/s along the contact
        /// normal) with an arm of `armMass` kg and `charge` 0..1 of body weight behind it.
        /// Friendly multipliers are applied here; the server's anti-grief rules come after.
        /// </summary>
        public static PunchOutcome Evaluate(PunchConfig c, float speed, float armMass, float charge, HitZone zone, bool friendly)
        {
            var o = new PunchOutcome();
            o.Momentum = speed * (armMass + c.chargeBodyMass * Mathf.Clamp01(charge));
            o.Power = Mathf.Clamp01(o.Momentum / Mathf.Max(1f, c.fullPowerMomentum));

            if (zone == HitZone.Prop || zone == HitZone.Other)
            {
                o.Impulse = Mathf.Min(o.Momentum * c.propImpulsePerMomentum, c.maxPropImpulse);
                o.Comedic = o.Momentum >= c.comedicMomentum;
                return o;
            }

            float zoneDamage = zone == HitZone.Head ? c.headDamageMultiplier : zone == HitZone.Limb ? c.limbDamageMultiplier : 1f;
            float zoneKnock = zone == HitZone.Head ? c.headKnockbackMultiplier : zone == HitZone.Limb ? c.limbKnockbackMultiplier : 1f;
            float friendlyDamage = friendly ? c.friendlyDamageMultiplier : 1f;
            float friendlyKnock = friendly ? c.friendlyKnockbackMultiplier : 1f;

            o.Damage = Mathf.Clamp(o.Momentum * c.damagePerMomentum, c.minDamage, c.maxDamage) * zoneDamage * friendlyDamage;
            o.Impulse = Mathf.Clamp(o.Momentum * c.knockbackPerMomentum, c.minKnockback, c.maxKnockback) * zoneKnock * friendlyKnock;
            o.Shove = Mathf.Min(o.Momentum * c.shovePerMomentum, c.maxShove) * zoneKnock * friendlyKnock;

            float knock = o.Momentum * zoneKnock * friendlyKnock;
            if (knock >= c.knockdownMomentum)
            {
                float over = Mathf.InverseLerp(c.knockdownMomentum, c.knockdownMomentum * 1.6f, knock);
                o.KnockdownSeconds = Mathf.Lerp(c.knockdownSecondsMin, c.knockdownSecondsMax, over);
            }
            o.StaggerSeconds = Mathf.Lerp(c.staggerSecondsLight, c.staggerSecondsHeavy, o.Power);
            o.StaggerStiffness = Mathf.Lerp(c.staggerStiffnessLight, c.staggerStiffnessHeavy, o.Power);
            o.Comedic = knock >= c.comedicMomentum;
            return o;
        }

        /// <summary>The way a hit pushes: along the fist's travel, tipped up a little.</summary>
        public static Vector3 KnockDirection(PunchConfig c, Vector3 fistVelocity, Vector3 fallback)
        {
            Vector3 d = fistVelocity.sqrMagnitude > 0.01f ? fistVelocity.normalized : fallback.normalized;
            return (d + Vector3.up * c.knockUpBias).normalized;
        }
    }
}
