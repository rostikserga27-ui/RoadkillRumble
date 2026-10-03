using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Server only: keeps a supply of one networked prop (dodgeballs, boxes) at this spot. Drops a new
    /// one whenever fewer than `count` are left, and takes back the ones that wandered off or fell out.
    /// </summary>
    public class PropDispenser : MonoBehaviour
    {
        public string prefabName = "Box5";
        public int count = 4;
        public float interval = 1.5f;
        public Vector3 spread = new Vector3(1f, 0f, 1f);
        [Tooltip("Props further than this from the dispenser for strayTimeout seconds are taken back.")]
        public float leash = 35f;
        public float strayTimeout = 20f;

        readonly List<NetworkObject> mine = new List<NetworkObject>();
        readonly Dictionary<NetworkObject, float> strayed = new Dictionary<NetworkObject, float>();
        GameObject prefab;
        float timer;

        void FixedUpdate()
        {
            var network = NetworkManager.Singleton;
            if (network == null || !network.IsServer || !network.IsListening) return;

            mine.RemoveAll(o => o == null || !o.IsSpawned);
            for (int i = mine.Count - 1; i >= 0; i--)
            {
                var prop = mine[i];
                bool away = (prop.transform.position - transform.position).sqrMagnitude > leash * leash || prop.transform.position.y < -20f;
                strayed.TryGetValue(prop, out float time);
                strayed[prop] = away ? time + Time.fixedDeltaTime : 0f;
                if (strayed[prop] <= strayTimeout && prop.transform.position.y > -20f) continue;
                strayed.Remove(prop);
                mine.RemoveAt(i);
                prop.Despawn(true);
            }

            timer -= Time.fixedDeltaTime;
            if (mine.Count >= count || timer > 0f) return;
            timer = interval;
            if (prefab == null) prefab = Resources.Load<GameObject>($"{NetSession.ResourceFolder}/{prefabName}");
            if (prefab == null) return;
            Vector3 offset = Vector3.Scale(Random.insideUnitSphere, spread);
            var instance = Instantiate(prefab, transform.position + offset, Random.rotation);
            var networkObject = instance.GetComponent<NetworkObject>();
            networkObject.Spawn(true);
            mine.Add(networkObject);
        }
    }
}
