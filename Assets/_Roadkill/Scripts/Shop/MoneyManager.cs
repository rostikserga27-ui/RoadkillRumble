using System;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// The local player's wallet. No economy yet: a starting amount for testing, F5 (or the component's
    /// context menu) adds more. Local only, never sent over the network.
    /// </summary>
    public class MoneyManager : MonoBehaviour
    {
        [SerializeField, Min(0)] int startingMoney = 500;
        [SerializeField, Min(1)] int debugAddAmount = 100;

        public int Money { get; private set; }
        public event Action<int> Changed;

        void Awake() => Money = startingMoney;

        void Update()
        {
            if (RkInput.DebugMoneyPressed) Add(debugAddAmount);
        }

        public bool CanAfford(int amount) => Money >= amount;

        public void Add(int amount)
        {
            if (amount <= 0) return;
            Money += amount;
            Changed?.Invoke(Money);
        }

        public bool TrySpend(int amount)
        {
            if (amount < 0 || Money < amount) return false;
            Money -= amount;
            Changed?.Invoke(Money);
            return true;
        }

        [ContextMenu("Debug: Add Money")]
        void DebugAdd() => Add(debugAddAmount);
    }
}
