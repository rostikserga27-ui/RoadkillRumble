using Unity.Netcode;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Shared time for the playground's moving parts. Spinners, hammers and the merry-go-round are pure
    /// functions of this clock, so every peer moves them identically without sending anything: once a
    /// session runs, the clock follows Netcode's server time (offset eased in, snapped when far off);
    /// before that it is plain fixed time. Read it from FixedUpdate or physics callbacks.
    /// </summary>
    public class PlaygroundClock : MonoBehaviour
    {
        static double offset;

        public static double Now => Time.fixedTimeAsDouble + offset;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => offset = 0.0;

        void Update()
        {
            var network = NetworkManager.Singleton;
            if (network == null || !network.IsListening) return;
            if (!network.IsServer && !network.IsConnectedClient) return;
            double target = network.ServerTime.Time - Time.timeAsDouble;
            double error = target - offset;
            // A big jump happens once, when the session starts; afterwards drift is eased out unnoticed.
            offset = System.Math.Abs(error) > 0.5 ? target : offset + System.Math.Clamp(error, -0.002, 0.002);
        }
    }
}
