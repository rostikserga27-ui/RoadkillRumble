using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Scripted check of the revolver (-rktestgun), logged as "RK-GUN". The client stands still; the host
    /// takes a revolver and shoots it in the chest, then the head (should knock it down), shoots a box, fans
    /// the rest of the drum and lets it reload. The client logs the damage it took, whether it went down
    /// and how many of the host's shots it saw.
    /// </summary>
    public class GunAutotest : MonoBehaviour
    {
        static readonly Vector3 HostSpot = new Vector3(0f, 0.1f, -6f);
        static readonly Vector3 ClientSpot = new Vector3(4f, 0.1f, -6f);
        static readonly Vector3 BoxSpot = new Vector3(0f, 0.3f, -9.5f);

        NetworkManager net;
        PlayerNet me;
        int passed, failed;

        static void Log(string text) => Debug.Log("RK-GUN " + text);

        void Check(string what, bool ok)
        {
            if (ok) passed++;
            else failed++;
            Log($"{(ok ? "PASS" : "FAIL")} {what}");
        }

        IEnumerator Start()
        {
            float until = Time.time + 60f;
            while (Time.time < until && me == null)
            {
                net = NetworkManager.Singleton;
                var player = net != null && net.LocalClient != null ? net.LocalClient.PlayerObject : null;
                if (player != null) me = player.GetComponent<PlayerNet>();
                yield return null;
            }
            if (me == null) { Log("FAIL no local player"); yield break; }
            yield return new WaitForSeconds(2f);
            RevolverTool.IgnoreInput = true;   // only the test pulls the trigger
            if (net.IsServer) yield return Host();
            else yield return Client();
            RevolverTool.IgnoreInput = false;
            Log($"DONE {(net.IsServer ? "host" : "client")}: {passed} passed, {failed} failed");
        }

        PlayerNet Other()
        {
            foreach (var hands in HandsController.Players)
            {
                var player = hands.GetComponent<PlayerNet>();
                if (player != null && player != me) return player;
            }
            return null;
        }

        // ---- host --------------------------------------------------------------------------------------

        IEnumerator Host()
        {
            float until = Time.time + 60f;
            while (net.ConnectedClientsIds.Count < 2 && Time.time < until) yield return null;
            yield return new WaitForSeconds(3f);
            var victim = Other();
            if (victim == null) { Log("FAIL no client"); yield break; }

            me.Motor.TeleportTo(HostSpot, Quaternion.LookRotation(Vector3.right));
            var catalog = Resources.Load<ShopCatalog>(ShopCatalog.ResourcePath);
            ShopItemData gunData = null;
            foreach (var item in catalog.Items)
                if (item.HeldPrefab != null && item.HeldPrefab.GetComponent<RevolverTool>() != null) gunData = item;
            var hotbar = me.GetComponent<HotbarInventory>();
            hotbar.TryAdd(gunData);
            for (int i = 0; i < hotbar.SlotCount; i++)
                if (hotbar.Get(i) == gunData) hotbar.Select(i);
            yield return new WaitForSeconds(6f);   // spawn protection after the teleports
            var tools = me.GetComponent<PlayerTools>();
            var gun = tools.Revolver;
            Check("host holds the revolver", gun != null);
            if (gun == null) yield break;

            // Chest, then head.
            yield return Aim(victim.body.Chest.position);
            gun.DebugFire(false);
            Log($"shot 1 at the chest, rounds {gun.Rounds}");
            yield return new WaitForSeconds(2.5f);
            gun.spreadDegrees = 0f;   // the test wants the head, not the luck of the spread
            string aimedAt = "nothing";
            for (int i = 0; i < 30; i++)
            {
                yield return Aim(victim.body.Head.position + Vector3.up * 0.03f);
                if (tools.FindShotTarget(tools.ViewOrigin, tools.ViewForward, gun.range, out RaycastHit onHead))
                {
                    aimedAt = onHead.collider.name;
                    if (onHead.collider.attachedRigidbody == victim.body.Head) break;
                }
            }
            Log($"head shot aimed at {aimedAt}");
            gun.DebugFire(false);
            Log($"shot 2 at the head, rounds {gun.Rounds}");
            yield return new WaitForSeconds(4f);

            // A box: it should be knocked away.
            Rigidbody box = null;
            foreach (var prop in FindObjectsByType<PhysicsProp>())
                if (prop.name.StartsWith("Box5") && (box == null || prop.transform.position.z < box.position.z)) box = prop.GetComponent<Rigidbody>();
            box.linearVelocity = Vector3.zero;
            box.position = BoxSpot;
            box.transform.position = BoxSpot;
            yield return new WaitForSeconds(1f);
            yield return Aim(box.worldCenterOfMass);
            bool sees = tools.FindShotTarget(tools.ViewOrigin, tools.ViewForward, gun.range, out RaycastHit seen);
            var struck = sees ? seen.collider.attachedRigidbody : null;
            Vector3 before = struck != null ? struck.position : Vector3.zero;
            float speedBefore = struck != null ? struck.linearVelocity.magnitude : 0f;
            gun.DebugFire(false);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            float speedAfter = struck != null ? struck.linearVelocity.magnitude : 0f;
            yield return new WaitForSeconds(1f);
            float moved = struck != null ? Vector3.Distance(before, struck.position) : 0f;
            Check($"shot {(struck != null ? struck.name : "nothing")} ({(struck != null ? struck.mass : 0f):0} kg): speed {speedBefore:0.0} -> {speedAfter:0.0} m/s, moved {moved:0.00} m",
                speedAfter > speedBefore + 1f);

            // Fan the rest of the drum into the ground ahead, then the reload.
            me.Motor.DebugLook(HostSpot + new Vector3(0f, -1f, -5f));
            yield return new WaitForSeconds(0.2f);
            int fired = 0;
            yield return new WaitForSeconds(gun.aimedInterval);
            float fanStart = Time.time;
            while (gun.Rounds > 0 && Time.time - fanStart < 3f)
            {
                int rounds = gun.Rounds;
                gun.DebugFire(true);
                if (gun.Rounds < rounds) fired++;
                yield return null;
            }
            float fanTime = Time.time - fanStart;
            Check($"fanned {fired} shots in {fanTime:0.00} s (fan interval {gun.fanInterval})",
                fired == 3 && fanTime > 2f * gun.fanInterval - 0.02f && fanTime < 3f * gun.fanInterval + 0.3f);
            yield return new WaitForSeconds(gun.aimedInterval);
            gun.DebugFire(false);   // empty: the hammer clicks and the reload starts
            Check("empty drum starts the reload", gun.Reloading);
            yield return new WaitForSeconds(gun.reloadSeconds + 0.3f);
            Check($"reloaded: {gun.Rounds}/{gun.capacity}", gun.Rounds == gun.capacity && !gun.Reloading);
            Log($"host saw its own shots: {gun.ShotsShown}");
        }

        IEnumerator Aim(Vector3 point)
        {
            for (int i = 0; i < 10; i++)
            {
                me.Motor.DebugLook(point);
                yield return null;
            }
        }

        // ---- client ------------------------------------------------------------------------------------

        IEnumerator Client()
        {
            me.Motor.TeleportTo(ClientSpot, Quaternion.LookRotation(Vector3.left));
            var health = me.Health;
            float last = health.Health;
            var drops = new System.Collections.Generic.List<float>();
            bool wentDown = false, sawReload = false;
            float until = Time.time + 40f;
            while (Time.time < until)
            {
                if (health.Health < last - 0.01f)
                {
                    drops.Add(last - health.Health);
                    Log($"client hit: -{last - health.Health:0} HP ({health.LastDamageText}), now {health.Health:0}");
                }
                last = health.Health;
                if (me.Motor.IsRagdolled && drops.Count >= 2) wentDown = true;
                var other = Other();
                var otherGun = other != null ? other.GetComponent<PlayerTools>().Revolver : null;
                if (otherGun != null && otherGun.Reloading) sawReload = true;
                yield return null;
            }
            var host = Other();
            var hostGun = host != null ? host.GetComponent<PlayerTools>().Revolver : null;
            int shotsSeen = hostGun != null ? hostGun.ShotsShown : -1;
            Check($"client took two hits: {string.Join(", ", drops.ConvertAll(d => d.ToString("0")))}", drops.Count == 2);
            Check("head shot hurt more than the chest", drops.Count == 2 && drops[1] > drops[0]);
            Check("head shot knocked the client down", wentDown);
            Check($"client saw the host's shots: {shotsSeen} (want 6)", shotsSeen == 6);
            Check("client saw the host reload", sawReload);
        }
    }
}
