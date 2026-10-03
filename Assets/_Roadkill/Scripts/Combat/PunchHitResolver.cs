using Unity.Netcode;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Turns a fist's collision into a target, and on the server a validated hit into its consequences:
    /// players (damage, stagger or knockdown, friendly-fire rules), props (pushed at the contact point in
    /// the server's physics, where props live) and anything implementing IPunchable (NPCs later).
    /// Server-side punched props that fly fast enough still knock people over through ImpactReporter.
    /// </summary>
    public static class PunchHitResolver
    {
        /// <summary>
        /// What a fist touched: a player (through a bone of their body, or their capsule), a networked prop
        /// or a punchable object. False for the world (walls, floor).
        /// </summary>
        public static bool FindTarget(Collider collider, Rigidbody body, Vector3 point,
            out NetworkObject target, out PlayerNet victim, out int part, out HitZone zone)
        {
            target = null;
            part = -1;
            zone = HitZone.Other;
            victim = null;

            // The active-ragdoll body is detached from its player at runtime: find the player through the bone.
            var bone = collider.GetComponentInParent<RagdollBodyPart>();
            victim = bone != null ? bone.Player : collider.GetComponentInParent<PlayerNet>();
            if (victim != null)
            {
                target = victim.NetworkObject;
                var ragdoll = victim.body;
                if (ragdoll != null)
                {
                    part = body != null ? ragdoll.PartIndexOf(body) : -1;
                    if (part < 0) part = NearestPart(ragdoll, point);
                    zone = part >= 0 ? PunchMath.ZoneOf(ragdoll.PartRole(part)) : HitZone.Torso;
                }
                else zone = HitZone.Torso;
                return target != null && target.IsSpawned;
            }

            var networkObject = collider.GetComponentInParent<NetworkObject>();
            if (networkObject == null || !networkObject.IsSpawned) return false;
            if (networkObject.GetComponent<PhysicsProp>() != null) zone = HitZone.Prop;
            else if (networkObject.GetComponentInChildren<IPunchable>() == null) return false;
            target = networkObject;
            return true;
        }

        static int NearestPart(ActiveRagdollController ragdoll, Vector3 point)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < ragdoll.PartCount; i++)
            {
                float d = (ragdoll.PartBody(i).worldCenterOfMass - point).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            return best;
        }

        /// <summary>Server: is a claimed contact somewhere both the attacker's fist and the target could have been?</summary>
        public static bool ServerPlausible(PlayerFists attacker, NetworkObject target, Vector3 point)
        {
            var c = PunchConfig.Current;
            Vector3 chest = attacker.transform.position + Vector3.up * 1.3f;
            if (Vector3.Distance(point, chest) > c.maxClaimDistanceFromAttacker) return false;

            var victim = target.GetComponent<PlayerNet>();
            if (victim != null)
            {
                // Distance to the player's standing line, feet to head (a downed body's capsule trails its hips).
                Vector3 feet = victim.transform.position;
                Vector3 closest = ClosestOnSegment(feet + Vector3.up * 0.2f, feet + Vector3.up * 1.7f, point);
                return Vector3.Distance(point, closest) <= c.maxClaimDistanceFromTarget + 0.35f;
            }
            float nearest = float.MaxValue;
            foreach (var collider in target.GetComponentsInChildren<Collider>())
                nearest = Mathf.Min(nearest, Vector3.Distance(point, collider.bounds.ClosestPoint(point)));
            return nearest <= c.maxClaimDistanceFromTarget;
        }

        static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            return a + ab * t;
        }

        /// <summary>Server: apply a validated hit and tell everyone.</summary>
        public static void ServerResolve(PlayerFists attacker, NetworkObject target, int part, Vector3 point,
            Vector3 fistVelocity, float speed, float charge, int seq, float armMass)
        {
            var c = PunchConfig.Current;
            Vector3 direction = PunchMath.KnockDirection(c, fistVelocity, attacker.transform.forward);
            var flags = PlayerFists.HitFlags.None;

            var victim = target.GetComponent<PlayerNet>();
            if (victim != null)
            {
                ResolvePlayer(attacker, victim, target, part, point, direction, speed, charge, seq, armMass);
                return;
            }

            var outcome = PunchMath.Evaluate(c, speed, armMass, charge, HitZone.Prop, false);
            var punchable = target.GetComponentInChildren<IPunchable>();
            if (punchable != null)
            {
                punchable.OnPunched(new PunchHitInfo { Attacker = attacker.Player, Point = point, Direction = direction, Outcome = outcome });
            }
            else
            {
                var body = target.GetComponent<Rigidbody>();
                if (body != null && !body.isKinematic)
                {
                    if (attacker.Player.Hands.IsHolding(body)) return;   // punching your own load does nothing
                    body.AddForceAtPosition(direction * outcome.Impulse, point, ForceMode.Impulse);
                    Debug.Log($"Roadkill: {PlayerNet.NameOf(attacker.OwnerClientId)} punched prop {target.name} ({speed:0.0} m/s): {outcome.Impulse:0} N s");
                }
                flags |= PlayerFists.HitFlags.Prop;
            }
            if (outcome.Comedic) flags |= PlayerFists.HitFlags.Comedic;
            attacker.ServerBroadcastHit(target, -1, point, direction * outcome.Impulse, 0f, 1f, outcome.Power, flags, seq);
        }

        static void ResolvePlayer(PlayerFists attacker, PlayerNet victim, NetworkObject target, int part, Vector3 point,
            Vector3 direction, float speed, float charge, int seq, float armMass)
        {
            var c = PunchConfig.Current;
            var ragdoll = victim.body;
            if (ragdoll != null && (part < 0 || part >= ragdoll.PartCount)) part = -1;
            HitZone zone = ragdoll != null && part >= 0 ? PunchMath.ZoneOf(ragdoll.PartRole(part)) : HitZone.Torso;

            bool friendly = FriendlyFire.AreTeammates(attacker.Player, victim);
            var outcome = PunchMath.Evaluate(c, speed, armMass, charge, zone, friendly);
            var flags = PlayerFists.HitFlags.None;
            if (friendly) flags |= PlayerFists.HitFlags.Friendly;

            if (friendly && !FriendlyFire.Enabled)
            {
                // Friendly fire off: the fists still bump (physics), but nothing comes of it.
                flags |= PlayerFists.HitFlags.Harmless;
                attacker.ServerBroadcastHit(target, part, point, Vector3.zero, 0f, 1f, 0f, flags, seq);
                return;
            }
            if (friendly && FriendlyFire.IsProtected(victim.OwnerClientId))
            {
                // Spawn protection: a little push, no damage, no knockdown.
                flags |= PlayerFists.HitFlags.Protected;
                outcome.Damage = 0f;
                outcome.KnockdownSeconds = 0f;
                outcome.Impulse *= 0.25f;
                outcome.Shove *= 0.25f;
                outcome.StaggerSeconds = 0f;
                outcome.Comedic = false;
            }
            else if (friendly)
            {
                float allowed = FriendlyFire.ServerCapDamage(attacker.OwnerClientId, victim.OwnerClientId, outcome.Damage);
                if (allowed < outcome.Damage - 0.01f) flags |= PlayerFists.HitFlags.Capped;
                outcome.Damage = allowed;
            }
            if (outcome.Comedic) flags |= PlayerFists.HitFlags.Comedic;
            if (outcome.KnockdownSeconds > 0f) flags |= PlayerFists.HitFlags.Knockdown;

            Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
            flat = flat.sqrMagnitude > 0.0001f ? flat.normalized : Vector3.zero;
            Vector3 shove = flat * outcome.Shove + Vector3.up * (outcome.Shove * c.shoveLift);
            Vector3 kick = flat * Mathf.Max(outcome.Shove, 1.5f) + Vector3.up * (c.knockdownPopUp * outcome.Power);

            var reaction = victim.GetComponent<PunchReaction>();
            if (reaction != null)
                reaction.ServerReact(outcome.Damage, outcome.KnockdownSeconds, kick, shove, attacker.OwnerClientId, outcome.Power);
            attacker.ServerBroadcastHit(target, part, point, direction * outcome.Impulse,
                outcome.StaggerSeconds, outcome.StaggerStiffness, outcome.Power, flags, seq);

            Debug.Log($"Roadkill: {PlayerNet.NameOf(attacker.OwnerClientId)} punched {PlayerNet.NameOf(victim.OwnerClientId)} " +
                      $"({zone}, {speed:0.0} m/s, charge {charge:0.00}): {outcome.Damage:0} dmg" +
                      $"{(outcome.KnockdownSeconds > 0f ? $", down {outcome.KnockdownSeconds:0.0} s" : "")} [{flags}]");
        }
    }
}
