using System;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// The local player's view of the team's money. In a session that is the one shared balance
    /// (TeamWallet): sales add to it, any player's purchase spends from it, and everyone sees the same
    /// number. Without one (offline tests) it falls back to a local balance. F10 (or the component's
    /// context menu) adds money for testing.
    /// </summary>
    public class MoneyManager : MonoBehaviour
    {
        [Tooltip("Offline only; a session starts with TeamWallet's amount.")]
        [SerializeField, Min(0)] int startingMoney = 500;
        [SerializeField, Min(1)] int debugAddAmount = 100;

        int localMoney;

        public int Money => TeamWallet.Instance != null ? TeamWallet.Instance.Money : localMoney;
        public event Action<int> Changed;

        void Awake() => localMoney = startingMoney;

        void OnEnable() => TeamWallet.Changed += OnTeamChanged;
        void OnDisable() => TeamWallet.Changed -= OnTeamChanged;

        void OnTeamChanged(int value) => Changed?.Invoke(value);

        void Update()
        {
            if (RkInput.DebugMoneyPressed) Add(debugAddAmount);
        }

        public bool CanAfford(int amount) => Money >= amount;

        public void Add(int amount)
        {
            if (amount <= 0) return;
            if (TeamWallet.Instance != null)
            {
                TeamWallet.Instance.Add(amount);
                return;
            }
            localMoney += amount;
            Changed?.Invoke(localMoney);
        }

        /// <summary>Spend from the team's money; `done` says whether it went through (the server has the last word).</summary>
        public void TrySpend(int amount, Action<bool> done)
        {
            if (amount < 0 || Money < amount)
            {
                done(false);
                return;
            }
            if (TeamWallet.Instance != null)
            {
                TeamWallet.Instance.RequestSpend(amount, done);
                return;
            }
            localMoney -= amount;
            Changed?.Invoke(localMoney);
            done(true);
        }

        [ContextMenu("Debug: Add Money")]
        void DebugAdd() => Add(debugAddAmount);
    }
}
