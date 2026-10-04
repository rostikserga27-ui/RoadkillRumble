using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Scripted check of the sell van and the team's money (-rktestsell), logged as "RK-SELL". The host
    /// sells a gnome by throwing it in, carries one in (must not sell while held) and drops it, lets an
    /// unheld tyre fall in, and leaves a battery on the edge (must not sell) before pushing it in. Then it
    /// lays out a gnome for the client, which checks it sees the same team balance, buys the same shop
    /// item twice (stock never runs out), fails to buy beyond the balance, and throws the gnome in. Both
    /// sides log the team balance and the shared "sold today" total.
    /// </summary>
    public class SellAutotest : MonoBehaviour
    {
        public float waitForPlayerSeconds = 60f;

        NetworkManager net;
        PlayerNet me;
        MoneyManager wallet;
        SellVan van;
        int passed, failed;

        static void Log(string text) => Debug.Log("RK-SELL " + text);

        void Check(string what, bool ok)
        {
            if (ok) passed++;
            else failed++;
            Log($"{(ok ? "PASS" : "FAIL")} {what}");
        }

        Vector3 VanPoint(float x, float y, float z) => van.transform.TransformPoint(new Vector3(x, y, z));
        Vector3 DropSpot => VanPoint(0f, 0.3f, SellVan.Rear - 1.3f);
        Vector3 CargoCenter => VanPoint(0f, 1.0f, (SellVan.Rear + SellVan.CargoFront) * 0.5f);

        IEnumerator Start()
        {
            float until = Time.time + waitForPlayerSeconds;
            while (Time.time < until)
            {
                net = NetworkManager.Singleton;
                var player = net != null && net.LocalClient != null ? net.LocalClient.PlayerObject : null;
                van = FindAnyObjectByType<SellVan>();
                if (player != null && van != null && SellLedger.Instance != null)
                {
                    me = player.GetComponent<PlayerNet>();
                    wallet = player.GetComponent<MoneyManager>();
                    break;
                }
                yield return null;
            }
            if (me == null) { Log("FAIL no local player / van / ledger"); yield break; }
            yield return new WaitForSeconds(2f);
            if (net.IsServer) yield return Host();
            else yield return Client();
            Log($"DONE {(net.IsServer ? "host" : "client")}: {passed} passed, {failed} failed, team ${wallet.Money}, sold today ${SellLedger.SoldToday} ({SellLedger.ItemsSold} items)");
        }

        // ---- host --------------------------------------------------------------------------------------

        IEnumerator Host()
        {
            float until = Time.time + waitForPlayerSeconds;
            while (net.ConnectedClientsIds.Count < 2 && Time.time < until) yield return null;
            int players = net.ConnectedClientsIds.Count;
            Log($"host: {players} players, team ${wallet.Money}");
            yield return new WaitForSeconds(3f);

            // A: throw a gnome in.
            int money = wallet.Money, total = SellLedger.SoldToday;
            var gnome = Find("Gnome", null);
            yield return GrabAt(gnome);
            Check("A host holds the gnome", me.Hands.IsHolding(gnome.Body));
            me.Motor.DebugLook(CargoCenter + Vector3.up * 0.4f);
            yield return new WaitForSeconds(0.3f);
            me.Hands.DebugThrow(0.35f);
            yield return new WaitForSeconds(2.5f);
            Check($"A thrown gnome sold (gone: {gnome == null})", gnome == null);
            Check($"A team paid once: +${wallet.Money - money} (want $40)", wallet.Money - money == 40);
            Check($"A sold today +${SellLedger.SoldToday - total}", SellLedger.SoldToday - total == 40);

            // B: carry one into the cargo; it must not sell while held, then sells when dropped.
            money = wallet.Money;
            gnome = Find("Gnome", null);
            yield return GrabAt(gnome);
            bool everInside = false, soldWhileHeld = false;
            for (float t = 0f; t < 2.5f; t += Time.deltaTime)
            {
                me.Motor.DebugLook(CargoCenter);
                if (t < 1.6f) me.Motor.DebugMove(new Vector2(0f, 1f), 0.1f);
                if (gnome == null) { soldWhileHeld = true; break; }
                if (van.Zone.Contains(gnome.Body.worldCenterOfMass)) everInside = true;
                yield return null;
            }
            Log($"B carried gnome reached the cargo: {everInside}");
            Check("B not sold while held", !soldWhileHeld && gnome != null && !gnome.IsSold);
            me.Hands.DebugToggleGrab();   // let go
            yield return new WaitForSeconds(2f);
            Check($"B dropped gnome sold (gone: {gnome == null}, was inside: {everInside})", gnome == null);
            Check($"B team paid +${wallet.Money - money} (want $40)", wallet.Money - money == 40);
            me.Motor.TeleportTo(VanPoint(3f, 0.1f, SellVan.Rear - 3f), van.transform.rotation);   // out of the cargo
            yield return new WaitForSeconds(0.5f);

            // C: a tyre nobody held falls in: still the team's money.
            money = wallet.Money;
            var tire = Find("Tire", null);
            Place(tire, CargoCenter);
            yield return new WaitForSeconds(1.5f);
            Check($"C unheld tyre sold (gone: {tire == null})", tire == null);
            Check($"C team got +${wallet.Money - money} (want $25)", wallet.Money - money == 25);

            // D: a battery standing on the very edge of the cargo floor: touches the trigger, centre outside.
            money = wallet.Money;
            var battery = Find("Battery", null);
            Place(battery, VanPoint(0f, SellVanFloor + 0.25f, SellVan.Rear + 0.06f));
            yield return new WaitForSeconds(1.5f);
            Check($"D battery on the edge not sold (centre inside: {(battery != null && van.Zone.Contains(battery.Body.worldCenterOfMass))})",
                battery != null && !battery.IsSold);
            if (battery != null) Place(battery, CargoCenter);
            yield return new WaitForSeconds(1.5f);
            Check($"D pushed-in battery sold (gone: {battery == null}), team +${wallet.Money - money} (want $60)",
                battery == null && wallet.Money - money == 60);

            if (players < 2) yield break;

            // E: the client's turn. Lay a gnome out and wait for the client to sell it.
            money = wallet.Money;
            total = SellLedger.SoldToday;
            me.Motor.TeleportTo(VanPoint(3f, 0.1f, SellVan.Rear - 3f), van.transform.rotation);
            gnome = Find("Gnome", null);
            Place(gnome, DropSpot);
            Log($"CLIENT TURN at sold today ${total}");
            until = Time.time + 40f;
            while (gnome != null && Time.time < until) yield return null;
            yield return new WaitForSeconds(1f);
            Check($"E client sold the gnome (gone: {gnome == null}, sold today +${SellLedger.SoldToday - total})",
                gnome == null && SellLedger.SoldToday - total == 40);
            // The client bought two wrenches ($60 each) and sold the gnome ($40).
            Check($"E host sees the client's purchases and sale: team {wallet.Money - money:+0;-0} (want -80)", wallet.Money - money == -80);
        }

        const float SellVanFloor = 0.55f;

        Sellable Find(string prefab, Sellable except)
        {
            Sellable best = null;
            float bestDistance = float.MaxValue;
            foreach (var item in FindObjectsByType<Sellable>())
            {
                if (item == except || item.IsSold || !item.name.StartsWith(prefab)) continue;
                float d = (item.transform.position - van.transform.position).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = item; }
            }
            if (best == null) Log($"FAIL no {prefab} left");
            return best;
        }

        /// <summary>Server: put a prop somewhere, at rest.</summary>
        static void Place(Sellable item, Vector3 position)
        {
            if (item == null) return;
            item.Body.linearVelocity = Vector3.zero;
            item.Body.angularVelocity = Vector3.zero;
            item.Body.position = position;
            item.transform.SetPositionAndRotation(position, Quaternion.identity);
            item.Body.WakeUp();
        }

        /// <summary>Server: lay the prop at the drop spot behind the van; then this player stands behind it and grabs it.</summary>
        IEnumerator GrabAt(Sellable item)
        {
            Place(item, DropSpot);
            yield return GrabFromBehind(item);
        }

        IEnumerator GrabFromBehind(Sellable item)
        {
            if (item == null) yield break;
            me.Motor.TeleportTo(VanPoint(0f, 0.1f, SellVan.Rear - 2.4f), van.transform.rotation);
            yield return new WaitForSeconds(1.2f);
            me.Motor.DebugLook(item.Body.worldCenterOfMass);
            yield return new WaitForSeconds(0.3f);
            me.Hands.DebugToggleGrab();
            yield return new WaitForSeconds(0.6f);
        }

        // ---- client ------------------------------------------------------------------------------------

        IEnumerator Client()
        {
            int start = wallet.Money;
            Log($"client {net.LocalClientId}: team ${start}");
            // Wait for the host's part (A 40 + B 40 + C 25 + D 60) and a gnome at the drop spot.
            float until = Time.time + 120f;
            Sellable gnome = null;
            while (Time.time < until)
            {
                if (SellLedger.SoldToday >= 165)
                {
                    foreach (var item in FindObjectsByType<Sellable>())
                        if (item.name.StartsWith("Gnome") && (item.transform.position - DropSpot).sqrMagnitude < 0.8f * 0.8f) gnome = item;
                    if (gnome != null) break;
                }
                yield return null;
            }
            if (gnome == null) { Log($"FAIL no gnome for the client (sold today ${SellLedger.SoldToday})"); yield break; }
            Check($"client sees the team's earnings: ${wallet.Money} (want ${start + 165})", wallet.Money == start + 165);

            // The shop: the same item twice (no stock limit), then more than the team has.
            var shop = me.GetComponent<ShopInteractor>();
            var hotbar = me.GetComponent<HotbarInventory>();
            ShopItem wrench = null;
            foreach (var item in FindObjectsByType<ShopItem>())
                if (item.Data != null && item.Data.DisplayName == "Wrench") wrench = item;
            int beforeShop = wallet.Money;
            for (int i = 0; i < 2 && wrench != null; i++)
            {
                shop.DebugBuy(wrench);
                yield return new WaitForSeconds(0.8f);
            }
            int wrenches = 0;
            for (int i = 0; i < hotbar.SlotCount; i++)
                if (hotbar.Get(i) != null && hotbar.Get(i).DisplayName == "Wrench") wrenches++;
            Check($"shop sold the wrench twice: {wrenches} in the hotbar, team -${beforeShop - wallet.Money} (want 2, $120)",
                wrenches == 2 && beforeShop - wallet.Money == 120);
            bool? overspend = null;
            wallet.TrySpend(wallet.Money + 1000, ok => overspend = ok);
            yield return new WaitForSeconds(0.8f);
            Check($"spending more than the team has is refused ({overspend})", overspend == false);

            hotbar.Select(hotbar.SlotCount - 1);   // an empty slot: hands free to grab again
            int beforeOwn = wallet.Money;
            yield return new WaitForSeconds(1f);
            yield return GrabFromBehind(gnome);
            Check("E client holds the gnome", gnome != null && me.Hands.IsHolding(gnome.Body));
            me.Motor.DebugLook(CargoCenter + Vector3.up * 0.4f);
            yield return new WaitForSeconds(0.3f);
            me.Hands.DebugThrow(0.35f);
            yield return new WaitForSeconds(3f);
            Check($"E gnome gone on the client: {gnome == null}", gnome == null);
            Check($"E team paid once: +${wallet.Money - beforeOwn} (want $40)", wallet.Money - beforeOwn == 40);
            Check($"E client sees sold today ${SellLedger.SoldToday} (want $205)", SellLedger.SoldToday == 205);
        }
    }
}
