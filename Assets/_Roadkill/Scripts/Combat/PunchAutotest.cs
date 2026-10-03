using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Scripted check for fists, writing "RK-PUNCH ... PASS/FAIL" lines. Host side (-rktestpunch, or added in
    /// Play mode): jab and haymaker boxes in the box wall (chest and head height), then, if another player is
    /// connected, jab their chest, haymaker their head and jab them with friendly fire off. Measures the fist's
    /// speed, whether the hit registered, what it did to the target and that no body blew up.
    /// Victim side (-rktestpunchvictim on a client): stand still at spawn and log health and ragdoll state.
    /// Duel (-rktestduel, on both peers): walk up to the other player, circle and trade punches for a while,
    /// logging every sudden jump of any body or capsule this peer sees (the "teleporting ragdoll" check).
    /// </summary>
    public class PunchAutotest : MonoBehaviour
    {
        public bool victimMode;
        public bool duelMode;
        public float duelSeconds = 40f;
        [Tooltip("Seconds to wait for a second player before skipping the player tests.")]
        public float waitForPlayerSeconds = 25f;

        readonly List<string> hitLines = new List<string>();
        int passed, failed;

        IEnumerator Start()
        {
            var manager = NetworkManager.Singleton;
            while (manager == null || !manager.IsListening || manager.LocalClient?.PlayerObject == null)
            {
                yield return null;
                manager = NetworkManager.Singleton;
            }
            yield return new WaitForSeconds(2f);
            var me = manager.LocalClient.PlayerObject.GetComponent<PlayerNet>();
            if (duelMode)
            {
                yield return Duel(me);
                yield break;
            }
            if (victimMode)
            {
                yield return StandAndReport(me);
                yield break;
            }

            Application.logMessageReceived += Capture;
            PunchArmDriver.LogContacts = true;
            var fists = me.GetComponent<PlayerFists>();
            Check("fists ready", fists != null && fists.Driver != null, fists == null ? "no PlayerFists" : fists.Driver == null ? "no ragdoll driver" :
                $"arm {fists.Driver.Body.ArmLength(1):0.00} m, shoulder {Vector3.Dot(fists.Driver.Body.UpperArmBody(1).position - me.transform.position, me.transform.forward):0.00} m ahead of the capsule centre");
            if (fists == null || fists.Driver == null) yield break;

            var session = FindAnyObjectByType<NetSession>();
            if (session != null) session.ResetProps();
            yield return new WaitForSeconds(1f);

            Vector3 wall = PropLayout.BoxWall;
            yield return PunchProp(me, "jab box (5 kg)", "Box5", wall + new Vector3(-0.26f, 1.25f, 0f), 0f);
            yield return PunchProp(me, "haymaker box (5 kg)", "Box5", wall + new Vector3(0.78f, 1.25f, 0f), 1.2f);
            yield return PunchProp(me, "jab box at head height", "Box5", wall + new Vector3(0.26f, 1.75f, 0f), 0f);

            PlayerNet other = null;
            for (float t = 0f; t < waitForPlayerSeconds && other == null; t += 0.5f)
            {
                foreach (var p in FindObjectsByType<PlayerNet>(FindObjectsInactive.Exclude))
                    if (p.IsSpawned && p != me) other = p;
                if (other == null) yield return new WaitForSeconds(0.5f);
            }
            if (other == null) Log("no second player: player tests skipped");
            else
            {
                yield return PunchPlayer(me, other, "jab chest", 1.3f, 0f, expectHit: true);
                yield return PunchPlayer(me, other, "jab from 1.3 m (steps in)", 1.3f, 0f, expectHit: true, distance: 1.3f);
                yield return PunchPlayer(me, other, "haymaker head", 1.6f, 1.2f, expectHit: true);
                if (FriendlyFire.Instance != null) FriendlyFire.Instance.ServerSet(FriendlyFire.Switch.FriendlyFire, false);
                yield return PunchPlayer(me, other, "jab, friendly fire off", 1.3f, 0f, expectHit: false);
                if (FriendlyFire.Instance != null) FriendlyFire.Instance.ServerSet(FriendlyFire.Switch.FriendlyFire, true);
            }

            Application.logMessageReceived -= Capture;
            PunchArmDriver.LogContacts = false;
            Log($"done: {passed} passed, {failed} failed");
        }

        void Capture(string message, string stackTrace, LogType type)
        {
            if (message.StartsWith("Roadkill: fist hit") || message.Contains(" punched ")) hitLines.Add(message);
        }

        IEnumerator PunchProp(PlayerNet me, string label, string prefab, Vector3 near, float hold)
        {
            var session = FindAnyObjectByType<NetSession>();
            if (session != null) session.ResetProps();
            yield return new WaitForSeconds(1f);
            var prop = FindProp(prefab, near);
            if (prop == null)
            {
                Check(label, false, $"no {prefab} near {near}");
                yield break;
            }
            var body = prop.GetComponent<Rigidbody>();
            Vector3 target = body.worldCenterOfMass;
            // Stand south of it, a short arm's length from its face, and aim at its middle.
            float half = prop.GetComponentInChildren<Collider>().bounds.extents.z;
            Vector3 feet = new Vector3(target.x, 0.02f, target.z - half - 0.6f);   // the capsule (0.35) nearly touching
            if (target.y > 2.2f) feet.y = target.y - 1.3f;
            reachTarget = prop.GetComponentInChildren<Collider>();
            yield return Punch(me, feet, target, hold, null);
            reachTarget = null;
            float moved = Vector3.Distance(body.worldCenterOfMass, target);
            Check(label, moved > 0.05f && lastHit, $"fist {lastFistSpeed:0.0} m/s, hit={lastHit}, {prop.name} moved {moved:0.00} m" + LastLine());
        }

        IEnumerator PunchPlayer(PlayerNet me, PlayerNet other, string label, float height, float hold, bool expectHit, float distance = 0.75f)
        {
            // Let the victim get up and settle first.
            for (float t = 0f; t < 6f && other.IsRagdolledShared; t += 0.2f) yield return new WaitForSeconds(0.2f);
            yield return new WaitForSeconds(1.5f);   // and let a head knocked back come upright
            Vector3 them = other.transform.position;
            Vector3 feet = them + new Vector3(0f, 0f, distance);   // 0.75: face to face, capsules (0.35 each) almost touching
            int before = CountLines(" punched ");
            yield return Punch(me, feet, them + Vector3.up * height, hold, other.body);
            bool registered = CountLines(" punched ") > before;
            bool ok = expectHit ? registered && lastHit : !registered;
            Check(label, ok, $"fist {lastFistSpeed:0.0} m/s, contact={lastHit}, server hit={registered}, " +
                             $"victim body max {lastVictimSpeed:0.0} m/s, down={other.IsRagdolledShared}" + LastLine());
        }

        float lastFistSpeed, lastVictimSpeed;
        Collider reachTarget;
        bool lastHit;

        /// <summary>Place, aim, punch, watch for 1.2 s: fist speed, whether it touched, and that both bodies stay sane.</summary>
        IEnumerator Punch(PlayerNet me, Vector3 feet, Vector3 aimAt, float hold, ActiveRagdollController victim)
        {
            var fists = me.GetComponent<PlayerFists>();
            var body = fists.Driver.Body;
            for (int i = 0; i < 25; i++)
            {
                me.Motor.DebugPlace(feet, aimAt);
                yield return new WaitForFixedUpdate();
            }
            int linesBefore = CountLines("Roadkill: fist hit");
            fists.DebugPunch(hold);
            lastFistSpeed = lastVictimSpeed = 0f;
            float maxOwnBone = 0f;
            var trace = new System.Text.StringBuilder();
            for (float t = 0f; t < 1.2f + hold; t += Time.fixedDeltaTime)
            {
                // Held in place through the wind-up, free once the strike starts (it steps into the punch).
                if (t < hold + 0.05f) me.Motor.DebugPlace(feet, aimAt);
                yield return new WaitForFixedUpdate();
                for (int h = 0; h < 2; h++)
                {
                    if (fists.Driver.PhaseOf(h) != PunchArmDriver.Phase.Strike) continue;
                    Vector3 aim = (aimAt - body.UpperArmBody(h).position).normalized;
                    float reach = Vector3.Dot(body.HandBody(h).position - body.UpperArmBody(h).position, aim);
                    float along = Vector3.Dot(body.HandBody(h).linearVelocity - body.RootVelocity, aim);
                    string gap = "";
                    if (reachTarget != null)
                    {
                        Vector3 hand = body.HandBody(h).position;
                        gap = $" gap {Vector3.Distance(hand, reachTarget.ClosestPoint(hand)):0.00}";
                    }
                    trace.Append($"{reach:0.00}/{along:0}{gap}  ");
                }
                for (int h = 0; h < 2; h++)
                    lastFistSpeed = Mathf.Max(lastFistSpeed, (body.HandBody(h).linearVelocity - body.RootVelocity).magnitude);
                if (t > hold + 0.5f)
                    for (int i = 0; i < body.PartCount; i++) maxOwnBone = Mathf.Max(maxOwnBone, body.PartBody(i).linearVelocity.magnitude);
                if (victim != null)
                    for (int i = 0; i < victim.PartCount; i++) lastVictimSpeed = Mathf.Max(lastVictimSpeed, victim.PartBody(i).linearVelocity.magnitude);
            }
            lastHit = CountLines("Roadkill: fist hit") > linesBefore;
            Log($"  trace (phase reach m / speed along aim m/s): {trace}");
            float hipsOff = Vector3.Distance(body.Hips.position, me.transform.position + Vector3.up * body.hipHeight);
            Check("  attacker body stable", maxOwnBone < 8f && hipsOff < 0.6f, $"bones up to {maxOwnBone:0.0} m/s after the punch, hips {hipsOff:0.00} m off");
            if (victim != null) Check("  victim body sane", lastVictimSpeed < 25f, $"victim bones up to {lastVictimSpeed:0.0} m/s");
        }

        static NetworkObject FindProp(string prefab, Vector3 near)
        {
            NetworkObject best = null;
            float bestDistance = 1.5f;
            foreach (var networkObject in NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
            {
                if (!networkObject.name.StartsWith(prefab) || networkObject.GetComponent<PhysicsProp>() == null) continue;
                float d = Vector3.Distance(networkObject.transform.position, near);
                if (d < bestDistance) { bestDistance = d; best = networkObject; }
            }
            return best;
        }

        int CountLines(string fragment)
        {
            int n = 0;
            foreach (var line in hitLines) if (line.Contains(fragment)) n++;
            return n;
        }

        string LastLine() => hitLines.Count > 0 ? $" | last: {hitLines[hitLines.Count - 1].Replace("Roadkill: ", "")}" : "";

        void Check(string label, bool ok, string detail)
        {
            if (ok) passed++; else failed++;
            Log($"{(ok ? "PASS" : "FAIL")} {label}: {detail}");
        }

        IEnumerator StandAndReport(PlayerNet me)
        {
            var health = me.Health;
            float lastHealth = health.Health;
            bool lastDown = false;
            Log($"victim ready at {me.transform.position}");
            for (float t = 0f; t < 120f; t += 0.25f)
            {
                yield return new WaitForSeconds(0.25f);
                bool down = me.Motor.IsRagdolled;
                if (!Mathf.Approximately(health.Health, lastHealth) || down != lastDown)
                {
                    Log($"t={t:0.00}s health {lastHealth:0} -> {health.Health:0} ({health.LastDamageText}), down={down}, " +
                        $"speed {me.GetComponent<Rigidbody>().linearVelocity.magnitude:0.0} m/s");
                    lastHealth = health.Health;
                    lastDown = down;
                }
            }
        }

        // ---- duel ----------------------------------------------------------------------------------------

        class Track
        {
            public PlayerNet Player;
            public Vector3 Hips, Root;
            public bool Has, Puppet;
            public int Snaps, HipsJumps, RelativeJumps, RootJumps, PuppetSwitches;
            public float MaxHipsStep, MaxRelative, MaxRootStep;
        }

        IEnumerator Duel(PlayerNet me)
        {
            PlayerNet other = null;
            for (float t = 0f; t < 60f && other == null; t += 0.5f)
            {
                foreach (var p in FindObjectsByType<PlayerNet>(FindObjectsInactive.Exclude))
                    if (p.IsSpawned && p != me) other = p;
                if (other == null) yield return new WaitForSeconds(0.5f);
            }
            if (other == null)
            {
                Log("duel: nobody to fight");
                yield break;
            }
            Log($"duel: {PlayerNet.NameOf(me.OwnerClientId)} vs {PlayerNet.NameOf(other.OwnerClientId)}");
            var fists = me.GetComponent<PlayerFists>();
            var tracks = new List<Track>();
            foreach (var p in new[] { me, other })
                if (p.body != null) tracks.Add(new Track { Player = p, Snaps = p.body.SnapCount });
            StartCoroutine(Watch(tracks));

            float nextPunch = 0f, end = Time.time + duelSeconds, nextReport = Time.time + 10f;
            int punches = 0;
            while (Time.time < end)
            {
                if (other == null || !other.IsSpawned) break;
                if (!me.Motor.IsRagdolled)
                {
                    Vector3 to = Vector3.ProjectOnPlane(other.transform.position - me.transform.position, Vector3.up);
                    float d = to.magnitude;
                    me.Motor.DebugLook(other.transform.position + Vector3.up * 1.35f);
                    Vector2 move = d > 1.4f ? new Vector2(0f, 1f) : d < 0.8f ? new Vector2(0f, -0.6f) : new Vector2(Mathf.Sin(Time.time * 1.3f) * 0.8f, 0.2f);
                    me.Motor.DebugMove(move, 0.25f);
                    if (fists.TargetInRange && Time.time > nextPunch)
                    {
                        fists.DebugPunch(Random.value < 0.35f ? Random.Range(0.4f, 1f) : 0f);
                        nextPunch = Time.time + Random.Range(0.5f, 1.1f);
                        punches++;
                    }
                }
                if (Time.time > nextReport)
                {
                    nextReport += 10f;
                    Report(tracks, $"after {duelSeconds - (end - Time.time):0}s, {punches} punches thrown");
                }
                yield return new WaitForSeconds(0.1f);
            }
            Report(tracks, $"end, {punches} punches thrown");
            Log("duel done");
        }

        /// <summary>Every physics step: how far each body's hips and each capsule moved, flagging jumps.</summary>
        IEnumerator Watch(List<Track> tracks)
        {
            int logged = 0;
            while (true)
            {
                yield return new WaitForFixedUpdate();
                foreach (var t in tracks)
                {
                    if (t.Player == null || t.Player.body == null) continue;
                    var body = t.Player.body;
                    Vector3 hips = body.Hips.position, root = t.Player.transform.position;
                    bool puppet = body.IsPuppet;
                    if (t.Has)
                    {
                        float hipsStep = (hips - t.Hips).magnitude;
                        float relative = ((hips - t.Hips) - (root - t.Root)).magnitude;
                        float rootStep = (root - t.Root).magnitude;
                        t.MaxHipsStep = Mathf.Max(t.MaxHipsStep, hipsStep);
                        t.MaxRelative = Mathf.Max(t.MaxRelative, relative);
                        t.MaxRootStep = Mathf.Max(t.MaxRootStep, rootStep);
                        bool jump = false;
                        if (hipsStep > 0.25f) { t.HipsJumps++; jump = true; }
                        if (relative > 0.2f) { t.RelativeJumps++; jump = true; }
                        if (rootStep > 0.3f) { t.RootJumps++; jump = true; }
                        if (puppet != t.Puppet) t.PuppetSwitches++;
                        if (jump && logged++ < 25)
                            Log($"  jump {Who(t)}: hips {hipsStep:0.00} m, vs capsule {relative:0.00} m, capsule {rootStep:0.00} m in one step; " +
                                $"puppet {t.Puppet}->{puppet}, down={t.Player.IsRagdolledShared}, snaps {body.SnapCount - t.Snaps}");
                    }
                    t.Hips = hips;
                    t.Root = root;
                    t.Puppet = puppet;
                    t.Has = true;
                }
            }
        }

        static string Who(Track t) => $"{(t.Player.IsOwner ? "own" : "remote")} {PlayerNet.NameOf(t.Player.OwnerClientId)}";

        void Report(List<Track> tracks, string when)
        {
            foreach (var t in tracks)
            {
                if (t.Player == null || t.Player.body == null) continue;
                Log($"duel {when}: {Who(t)} hips max {t.MaxHipsStep:0.00} m/step, jumps {t.HipsJumps}, vs capsule max {t.MaxRelative:0.00} " +
                    $"({t.RelativeJumps} jumps), capsule max {t.MaxRootStep:0.00} ({t.RootJumps} jumps), puppet switches {t.PuppetSwitches}, " +
                    $"snaps {t.Player.body.SnapCount - t.Snaps}, health {t.Player.Health.Health:0}");
            }
        }

        static void Log(string message) => Debug.Log($"RK-PUNCH {message}");
    }
}
