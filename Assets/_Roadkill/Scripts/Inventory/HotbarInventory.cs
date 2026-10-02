using System;
using System.Collections.Generic;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// The only inventory: a hotbar of tool slots. Starts small and grows with slot upgrades up to a cap.
    /// Number keys 1-6 and the mouse wheel pick the slot; the picked tool is what the hand holds
    /// (PlayerTools shows it). An empty slot means empty hands, so grabbing works again.
    /// </summary>
    public class HotbarInventory : MonoBehaviour
    {
        [SerializeField, Range(1, 6)] int startingSlots = 3;
        [SerializeField, Range(1, 6)] int maxSlots = 6;

        public int SlotCount => slots.Count;
        public int MaxSlots => maxSlots;
        public int Selected { get; private set; }
        public int UpgradesBought { get; private set; }
        public bool HasFreeSlot => slots.IndexOf(null) >= 0;
        public ShopItemData SelectedItem => Get(Selected);

        /// <summary>Slots, selection or contents changed.</summary>
        public event Action Changed;

        readonly List<ShopItemData> slots = new List<ShopItemData>();

        void Awake()
        {
            maxSlots = Mathf.Max(maxSlots, startingSlots);
            for (int i = 0; i < startingSlots; i++) slots.Add(null);
        }

        void Update()
        {
            if (Cursor.lockState != CursorLockMode.Locked) return;
            int key = RkInput.SlotKeyPressed;
            if (key >= 0 && key < slots.Count) Select(key);

            float scroll = RkInput.Scroll;
            if (scroll > 0.01f) Select((Selected - 1 + slots.Count) % slots.Count);
            else if (scroll < -0.01f) Select((Selected + 1) % slots.Count);
        }

        public ShopItemData Get(int index) => index >= 0 && index < slots.Count ? slots[index] : null;

        public void Select(int index)
        {
            if (index < 0 || index >= slots.Count || index == Selected) return;
            Selected = index;
            Changed?.Invoke();
        }

        /// <summary>Put an item in the first free slot. False if the hotbar is full.</summary>
        public bool TryAdd(ShopItemData item)
        {
            int free = slots.IndexOf(null);
            if (item == null || free < 0) return false;
            slots[free] = item;
            Changed?.Invoke();
            return true;
        }

        /// <summary>One more slot, up to the cap. Counts towards the next upgrade's price.</summary>
        public bool TryAddSlot()
        {
            if (slots.Count >= maxSlots) return false;
            slots.Add(null);
            UpgradesBought++;
            Changed?.Invoke();
            return true;
        }
    }
}
