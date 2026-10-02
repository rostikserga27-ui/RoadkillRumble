using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Owner only. Looks for a shop item under the crosshair, highlights it, shows the buy prompt and
    /// buys it on E: a Tool goes into the first free hotbar slot, a slot upgrade adds a slot. Also keeps
    /// the price labels on the stall current for this player (slot upgrade price, sold out).
    /// </summary>
    public class ShopInteractor : MonoBehaviour
    {
        [SerializeField] Camera viewCamera;
        [SerializeField] MoneyManager money;
        [SerializeField] HotbarInventory hotbar;
        [SerializeField] PlayerMotor motor;
        [SerializeField] float range = 3f;
        [SerializeField] float messageSeconds = 2f;

        ShopItem target;
        ShopItem[] shopItems = new ShopItem[0];
        string message = "";
        float messageUntil;
        GUIStyle style;

        public void Setup(Camera view, MoneyManager wallet, HotbarInventory inventory, PlayerMotor playerMotor)
        {
            viewCamera = view;
            money = wallet;
            hotbar = inventory;
            motor = playerMotor;
        }

        void Start()
        {
            shopItems = FindObjectsByType<ShopItem>(FindObjectsSortMode.None);
            hotbar.Changed += RefreshLabels;
            RefreshLabels();
        }

        void OnDestroy()
        {
            if (hotbar != null) hotbar.Changed -= RefreshLabels;
        }

        void Update()
        {
            ShopItem looked = FindTarget();
            if (looked != target)
            {
                if (target != null) target.SetHighlighted(false);
                target = looked;
                if (target != null) target.SetHighlighted(true);
            }
            if (target != null && RkInput.InteractPressed) TryBuy(target);
        }

        ShopItem FindTarget()
        {
            if (viewCamera == null || Cursor.lockState != CursorLockMode.Locked || (motor != null && motor.IsRagdolled)) return null;
            Ray ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            var hits = Physics.RaycastAll(ray, range, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform)) continue;   // our own body
                var item = hit.collider.GetComponentInParent<ShopItem>();
                return item != null && !item.IsSold ? item : null;   // anything else blocks the view
            }
            return null;
        }

        bool IsMaxed(ShopItem item) => item.Data.Type == ShopItemData.ItemType.SlotUpgrade && hotbar.SlotCount >= hotbar.MaxSlots;

        int PriceOf(ShopItem item) => item.Data.Type == ShopItemData.ItemType.SlotUpgrade
            ? item.Data.PriceAfter(hotbar.UpgradesBought)
            : item.Data.Price;

        void TryBuy(ShopItem item)
        {
            var data = item.Data;
            int price = PriceOf(item);
            if (IsMaxed(item)) { Show("Sold out / Max"); return; }
            if (data.Type == ShopItemData.ItemType.Tool && !hotbar.HasFreeSlot) { Show("Hotbar is full"); return; }
            if (!money.TrySpend(price)) { Show($"Not enough money (${price})"); return; }

            if (data.Type == ShopItemData.ItemType.Tool)
            {
                hotbar.TryAdd(data);
                item.MarkSold();
            }
            else
            {
                hotbar.TryAddSlot();
            }
            Show($"Bought {data.DisplayName}");
            RefreshLabels();
        }

        void RefreshLabels()
        {
            foreach (var item in shopItems)
            {
                if (item == null || item.IsSold) continue;
                item.SetLabel(IsMaxed(item) ? $"{item.Data.DisplayName}\nSold out / Max" : $"{item.Data.DisplayName}\n${PriceOf(item)}");
            }
        }

        void Show(string text)
        {
            message = text;
            messageUntil = Time.time + messageSeconds;
        }

        void OnGUI()
        {
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            }
            float w = Screen.width, h = Screen.height;
            if (target != null)
            {
                string prompt = IsMaxed(target)
                    ? $"{target.Data.DisplayName} - Sold out / Max"
                    : $"[E] Buy - {target.Data.DisplayName} - ${PriceOf(target)}";
                bool affordable = IsMaxed(target) || money.CanAfford(PriceOf(target));
                style.normal.textColor = affordable ? Color.white : new Color(1f, 0.45f, 0.4f);
                GUI.Label(new Rect(0f, h * 0.5f + 30f, w, 30f), prompt, style);
            }
            if (Time.time < messageUntil)
            {
                style.normal.textColor = new Color(1f, 0.9f, 0.4f);
                GUI.Label(new Rect(0f, h * 0.5f + 62f, w, 30f), message, style);
            }
        }
    }
}
