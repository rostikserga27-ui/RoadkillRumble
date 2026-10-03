using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Every number behind fists in one asset (Resources/Combat/PunchConfig): the arm drive, the hit
    /// rule, damage and knockback, the victim's stagger, game feel and friendly fire. GDD phase 1 step 10:
    /// a tap is a jab, a charged haymaker deals 35; friendly fire is 50% damage and 100% knockback.
    /// Without the asset every peer falls back to these defaults (PunchConfig.Current).
    /// </summary>
    [CreateAssetMenu(menuName = "Roadkill/Punch Config", fileName = "PunchConfig")]
    public class PunchConfig : ScriptableObject
    {
        public const string ResourcePath = "Combat/PunchConfig";

        [Header("Timing")]
        [Tooltip("Shortest wind-up: a tap still pulls the fist back this long before it flies.")]
        public float minWindUpSeconds = 0.12f;
        [Tooltip("Holding the key this long after the minimum wind-up gives a full-charge haymaker.")]
        public float chargeSeconds = 0.65f;
        [Tooltip("Held longer than this, the haymaker lets go on its own.")]
        public float maxHoldSeconds = 2.5f;
        [Tooltip("The fist is live (counts hits) for this long: jab .. full charge.")]
        public float strikeSecondsJab = 0.14f;
        public float strikeSecondsHaymaker = 0.2f;
        public float recoverySeconds = 0.2f;
        [Tooltip("Shortest gap between two punches (the other fist may already wind up).")]
        public float cooldownSeconds = 0.22f;

        [Header("Arm drive (physics)")]
        [Tooltip("Fist speed the strike kicks the hand to along the aim (m/s): jab .. full charge.")]
        public float jabSpeed = 9f;
        public float haymakerSpeed = 14f;
        [Tooltip("The hand is never allowed faster than this relative to the body (m/s): no explosions.")]
        public float maxFistSpeed = 18f;
        [Tooltip("PD pull of the fist toward its target (1/s^2) and its damping (1/s): wind-up and recovery.")]
        public float windUpSpring = 450f;
        public float windUpDamping = 30f;
        [Tooltip("Strike: how hard the fist is held at strike speed along the aim (1/s), so it is still fast when it lands.")]
        public float strikeVelocityGain = 30f;
        public float recoverySpring = 120f;
        public float recoveryDamping = 16f;
        [Tooltip("Wrist joint spring while punching, so the hand stays a fist in line with the forearm instead of drooping.")]
        public float wristSpring = 250f;
        [Tooltip("Cap on the PD's acceleration (m/s^2), so the arm cannot fling the body around.")]
        public float maxArmAcceleration = 500f;
        [Tooltip("Punching range: a player, prop or NPC under the crosshair this close to the eyes (metres) is in reach, the crosshair shows it, and the punch steps in to land.")]
        public float punchRange = 1.7f;
        [Tooltip("With nothing in range the fist flies at a point this far in front of the eyes (metres).")]
        public float missAimDistance = 0.8f;
        [Tooltip("Stepping in to a target beyond arm's length: the player dashes at it this fast (m/s) and stops this far from the aim point (capsule centre, metres; the aim point is 0.1 m inside the target).")]
        public float approachSpeed = 5f;
        public float comfortableDistance = 0.5f;
        [Tooltip("The arm is thrown once the aim point is within arm's length plus this (metres), or after maxApproachSeconds of stepping in.")]
        public float throwReach = 0.35f;
        public float maxApproachSeconds = 0.25f;
        [Tooltip("How far the fist cocks back past the guard at full charge (metres).")]
        public float windUpPullBack = 0.1f;
        [Tooltip("Guard (where the fist rests while winding up and after a punch): this far in front of the shoulder, out to its side and up (metres). In front and beside, never by the chin.")]
        public float guardForward = 0.24f;
        public float guardSide = 0.06f;
        public float guardUp = -0.12f;
        [Tooltip("Chamber: where every punch is cocked, in front of the shoulder (metres forward, out to the side, down).")]
        public float chamberForward = 0.12f;
        public float chamberSide = 0.06f;
        public float chamberDown = 0.1f;
        [Tooltip("The fist is kept at least this far from its own head's centre (metres): no winding up through the face.")]
        public float headClearance = 0.25f;
        [Tooltip("How hard a fist that got inside that clearance is pushed back out (1/s^2).")]
        public float headClearanceSpring = 2000f;
        [Tooltip("Share of the strike's kick pushed back into the chest (recoil), so momentum stays sane.")]
        [Range(0f, 1f)] public float strikeRecoil = 0f;
        [Tooltip("Torso twist (degrees): the punching shoulder goes back on the wind-up and drives through on the strike.")]
        public float windUpTwist = 8f;
        public float strikeTwist = 12f;
        [Tooltip("The strike snaps the chest and hips round about the vertical (rad/s), the way hips lead a real punch; it buys reach.")]
        public float strikeTwistKick = 0f;
        [Tooltip("Lean (degrees): back on a charged wind-up, forward into the strike.")]
        public float windUpLean = 4f;
        public float strikeLean = 12f;
        [Tooltip("How far the body steps ahead of the gameplay capsule at full strike lean (metres).")]
        public float strikeStepForward = 0.4f;
        [Tooltip("A punch steps the player forward (m/s on the capsule): a jab a little, a full charge all of it.")]
        public float lungeSpeed = 2.4f;
        [Range(0f, 1f)] public float jabLungeShare = 0.45f;
        [Tooltip("A full-charge punch steps in for this long before the arm is thrown (seconds; part of the strike), so the fist is still fast when the target comes into reach.")]
        public float haymakerStepSeconds = 0.09f;
        [Tooltip("Aim pitch limits (degrees, up negative): you can punch a friend lying down, not the sky.")]
        public float minAimPitch = -35f;
        public float maxAimPitch = 50f;

        [Header("Hit rule")]
        [Tooltip("Fist speed into the target along the contact normal below this is a brush, not a punch (m/s).")]
        public float minImpactSpeed = 3f;
        [Tooltip("Body mass a full charge puts behind the fist, on top of the arm's own (kg).")]
        public float chargeBodyMass = 3f;
        [Tooltip("Momentum (kg m/s) treated as a full-power punch for feel and stagger.")]
        public float fullPowerMomentum = 70f;
        [Tooltip("One punch can hit at most this many different targets.")]
        public int maxTargetsPerStrike = 3;

        [Header("Damage")]
        public float damagePerMomentum = 0.55f;
        public float minDamage = 3f;
        public float maxDamage = 35f;
        [Tooltip("A full charge deals this much more damage on top of its extra speed and weight (0.5 = +50%).")]
        public float chargeDamageBonus = 0.5f;
        public float headDamageMultiplier = 1.5f;
        public float limbDamageMultiplier = 0.6f;

        [Header("Knockback")]
        [Tooltip("Impulse on the struck bone (N s per kg m/s of punch momentum), clamped.")]
        public float knockbackPerMomentum = 0.6f;
        public float minKnockback = 10f;
        public float maxKnockback = 35f;
        [Tooltip("The struck bone itself gets at most this much speed (m/s); the rest of the impulse moves the whole body. Light bones (head, hands) would otherwise snap off their joints.")]
        public float maxBoneKick = 3f;
        [Tooltip("Share of a hit's impulse applied at the contact point (the rest at the bone's centre): low = a push, not a spin.")]
        [Range(0f, 1f)] public float hitTorqueShare = 0.25f;
        [Tooltip("Hits push mostly sideways: at most this much of the knock goes up.")]
        [Range(0f, 1f)] public float hitLift = 0.15f;
        public float headKnockbackMultiplier = 1.25f;
        public float limbKnockbackMultiplier = 0.8f;
        [Tooltip("Speed added to the victim's capsule (m/s per kg m/s), clamped.")]
        public float shovePerMomentum = 0.06f;
        public float maxShove = 5f;
        [Tooltip("Share of the shove that goes upward, so hard hits lift the victim off their feet.")]
        public float shoveLift = 0.3f;
        [Tooltip("Upward share added to the knockback direction.")]
        public float knockUpBias = 0.25f;
        [Tooltip("Props: impulse per kg m/s of punch momentum (N s), clamped.")]
        public float propImpulsePerMomentum = 0.9f;
        public float maxPropImpulse = 60f;

        [Header("Victim reaction")]
        [Tooltip("Stagger: the victim's joints and balance soften for a moment (light .. full-power punch).")]
        public float staggerSecondsLight = 0.2f;
        public float staggerSecondsHeavy = 0.45f;
        [Tooltip("Joint stiffness while staggered (1 = normal).")]
        [Range(0f, 1f)] public float staggerStiffnessLight = 0.85f;
        [Range(0f, 1f)] public float staggerStiffnessHeavy = 0.6f;
        [Tooltip("The shove keeps the victim's footing loose this long (they slide instead of stopping dead).")]
        public float stumbleSeconds = 0.35f;
        [Tooltip("Momentum x zone multiplier at which a punch knocks the victim down (full ragdoll).")]
        public float knockdownMomentum = 45f;
        [Tooltip("A punch charged at least this much that lands on the head always knocks down (1 = off).")]
        [Range(0f, 1f)] public float headKnockdownCharge = 0.8f;
        public float knockdownSecondsMin = 1.2f;
        public float knockdownSecondsMax = 2.6f;
        [Tooltip("Upward pop on a knockdown (m/s at full power).")]
        public float knockdownPopUp = 2.5f;

        [Header("Game feel")]
        [Tooltip("Attacker's first-person fist freezes this long on contact (light .. full power).")]
        public float hitStopSecondsLight = 0.04f;
        public float hitStopSecondsHeavy = 0.11f;
        public float shakeLight = 0.012f;
        public float shakeHeavy = 0.05f;
        public float shakeSeconds = 0.18f;
        [Tooltip("Camera kick (degrees) on the attacker / on the victim at full power.")]
        public float attackerCameraKick = 3.5f;
        public float victimCameraKick = 7f;
        [Tooltip("Momentum x zone above which the hit plays the comedic sound and a big popup.")]
        public float comedicMomentum = 50f;
        public Color hitSparkColor = new Color(1f, 0.85f, 0.35f);
        public Color friendlySparkColor = new Color(1f, 0.35f, 0.75f);
        public Color propSparkColor = new Color(0.85f, 0.85f, 0.85f);

        [Header("Sound (empty = generated placeholder; no whoosh clip = silent swing)")]
        public AudioClip whooshClip;
        public AudioClip impactClip;
        public AudioClip comedicClip;
        public AudioClip harmlessClip;
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("Friendly fire")]
        [Tooltip("Lobby default for the host's toggle.")]
        public bool friendlyFireByDefault = true;
        [Range(0f, 2f)] public float friendlyDamageMultiplier = 0.5f;
        [Range(0f, 2f)] public float friendlyKnockbackMultiplier = 1f;
        [Tooltip("Anti-grief: no damage or knockdown for this long after (re)spawning or being gathered.")]
        public bool spawnProtectionByDefault = true;
        public float spawnProtectionSeconds = 3f;
        [Tooltip("Anti-grief: one teammate can deal at most this much punch damage to you per window.")]
        public bool teammateDamageCapByDefault = true;
        public float teammateDamageCap = 50f;
        public float teammateDamageWindowSeconds = 30f;

        [Header("Server validation")]
        [Tooltip("A hit claim is accepted this long after the strike's live window ends (latency).")]
        public float claimLatencyAllowance = 0.35f;
        [Tooltip("Contact point at most this far from the attacker's chest (metres).")]
        public float maxClaimDistanceFromAttacker = 2.6f;
        [Tooltip("Contact point at most this far from the victim (metres, beyond their body).")]
        public float maxClaimDistanceFromTarget = 1.2f;
        [Tooltip("Reported fist speeds above this are clamped; above 1.5x it the claim is thrown out (m/s).")]
        public float maxPlausibleFistSpeed = 24f;

        static PunchConfig current;

        /// <summary>The asset under Resources, or the defaults above when it is missing.</summary>
        public static PunchConfig Current
        {
            get
            {
                if (current != null) return current;
                current = Resources.Load<PunchConfig>(ResourcePath);
                if (current == null)
                {
                    current = CreateInstance<PunchConfig>();
                    current.name = "PunchConfig (defaults)";
                }
                return current;
            }
        }

        /// <summary>How long the fist is live once the arm is thrown.</summary>
        public float StrikeSeconds(float charge) => Mathf.Lerp(strikeSecondsJab, strikeSecondsHaymaker, charge);
        /// <summary>From the strike starting to its fist going dead, at the longest (step-in and approach included).</summary>
        public float LiveSeconds(float charge) => StrikeSeconds(charge) + StepIn(charge) + maxApproachSeconds;
        /// <summary>How long a punch of this charge steps in before the arm goes (none for a light jab).</summary>
        public float StepIn(float charge) => charge < 0.25f ? 0f : haymakerStepSeconds * charge;
        public float StrikeSpeed(float charge) => Mathf.Lerp(jabSpeed, haymakerSpeed, charge);
    }
}
