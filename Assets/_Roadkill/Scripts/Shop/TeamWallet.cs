using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// The team's one balance, on the PlaygroundRules object: everybody earns into it (the sell van) and
    /// spends from it (the shop). The server owns the number and everyone reads the NetworkVariable;
    /// spending asks the server, which checks and deducts, so two players cannot spend the same money.
    /// Players use it through their MoneyManager.
    /// </summary>
    public class TeamWallet : NetworkBehaviour
    {
        [SerializeField, Min(0)] int startingMoney = 500;

        public static TeamWallet Instance { get; private set; }

        /// <summary>The balance changed (every peer).</summary>
        public static event Action<int> Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            Changed = null;
        }

        // Written by the server.
        NetworkVariable<int> money = new NetworkVariable<int>(0);

        // This peer's spend requests waiting for the server's answer.
        readonly Dictionary<int, Action<bool>> pending = new Dictionary<int, Action<bool>>();
        int nextRequest;

        public int Money => money.Value;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (IsServer) money.Value = startingMoney;
            money.OnValueChanged += OnMoneyChanged;
            Changed?.Invoke(money.Value);
        }

        public override void OnNetworkDespawn()
        {
            money.OnValueChanged -= OnMoneyChanged;
            if (Instance == this) Instance = null;
            foreach (var done in pending.Values) done(false);
            pending.Clear();
        }

        void OnMoneyChanged(int previous, int value) => Changed?.Invoke(value);

        /// <summary>Server: money for the team (a sale).</summary>
        public void ServerAdd(int amount)
        {
            if (IsServer && amount > 0) money.Value += amount;
        }

        /// <summary>Any peer: add money (the F10 debug key).</summary>
        public void Add(int amount)
        {
            if (IsServer) ServerAdd(amount);
            else AddRpc(amount);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void AddRpc(int amount) => ServerAdd(amount);

        /// <summary>Any peer: spend from the team's money. `done` gets the server's answer (at once on the host).</summary>
        public void RequestSpend(int amount, Action<bool> done)
        {
            int id = ++nextRequest;
            pending[id] = done;
            SpendRpc(amount, id);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void SpendRpc(int amount, int id, RpcParams rpcParams = default)
        {
            bool ok = amount >= 0 && money.Value >= amount;
            if (ok) money.Value -= amount;
            SpendResultRpc(id, ok, RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void SpendResultRpc(int id, bool ok, RpcParams rpcParams = default)
        {
            if (pending.Remove(id, out var done)) done(ok);
        }
    }
}
