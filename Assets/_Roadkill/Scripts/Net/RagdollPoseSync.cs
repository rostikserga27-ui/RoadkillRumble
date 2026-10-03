using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Makes everyone see a downed player's body the way its owner does. While the owner's limp body
    /// leads (knocked down, possum, fired from a cannon), the owner streams every bone's pose about 15
    /// times a second; other peers turn their copy of the body into a kinematic puppet and replay the
    /// stream a little behind (interpolated), instead of simulating their own limp body that would land
    /// differently. When the owner gets up, the copy is physical again and balances as usual.
    /// About 250 bytes per snapshot, unreliable, and only while someone is down.
    /// </summary>
    public class RagdollPoseSync : NetworkBehaviour
    {
        public float sendRate = 15f;
        [Tooltip("Replay this far behind the newest pose, so late or uneven packets do not stutter.")]
        public float interpolationDelay = 0.12f;
        [Tooltip("No pose for this long: give the body back to physics.")]
        public float staleSeconds = 0.5f;

        struct Snapshot
        {
            public double Time;
            public Vector3[] Positions;
            public Quaternion[] Rotations;
        }

        PlayerNet player;
        int hipsIndex;
        float sendTimer;
        readonly List<Snapshot> buffer = new List<Snapshot>();
        double clockOffset;    // owner's clock minus ours, for the fastest packets
        bool hasOffset;
        double lastReceived = double.NegativeInfinity;
        Vector3 lastVelocity;

        ActiveRagdollController Body => player != null ? player.body : null;

        void Awake() => player = GetComponent<PlayerNet>();

        public override void OnNetworkSpawn()
        {
            if (Body == null) return;
            for (int i = 0; i < Body.PartCount; i++)
                if (Body.PartBody(i) == Body.Hips) hipsIndex = i;
        }

        void FixedUpdate()
        {
            if (!IsSpawned || Body == null) return;
            if (IsOwner) Send();
            else Play();
        }

        // ---- owner ---------------------------------------------------------------------------

        void Send()
        {
            if (!player.Motor.IsRagdolled || !Body.BodyLeads)
            {
                sendTimer = 0f;
                return;
            }
            sendTimer -= Time.fixedDeltaTime;
            if (sendTimer > 0f) return;
            sendTimer = 1f / sendRate;
            PoseRpc(Pack());
        }

        byte[] Pack()
        {
            var body = Body;
            Vector3 hips = body.Hips.position;
            using var stream = new MemoryStream(20 + body.PartCount * 14);
            using var writer = new BinaryWriter(stream);
            writer.Write(Time.timeAsDouble);
            writer.Write(hips.x);
            writer.Write(hips.y);
            writer.Write(hips.z);
            for (int i = 0; i < body.PartCount; i++)
            {
                var part = body.PartBody(i);
                Vector3 offset = part.position - hips;   // within a couple of metres: half precision is plenty
                writer.Write(Mathf.FloatToHalf(offset.x));
                writer.Write(Mathf.FloatToHalf(offset.y));
                writer.Write(Mathf.FloatToHalf(offset.z));
                Quaternion q = part.rotation;
                writer.Write((short)Mathf.RoundToInt(q.x * 32767f));
                writer.Write((short)Mathf.RoundToInt(q.y * 32767f));
                writer.Write((short)Mathf.RoundToInt(q.z * 32767f));
                writer.Write((short)Mathf.RoundToInt(q.w * 32767f));
            }
            writer.Flush();
            return stream.ToArray();
        }

        [Rpc(SendTo.NotOwner, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]
        void PoseRpc(byte[] data)
        {
            var body = Body;
            if (body == null || data == null || data.Length != 20 + body.PartCount * 14) return;
            using var reader = new BinaryReader(new MemoryStream(data));
            var snapshot = new Snapshot
            {
                Time = reader.ReadDouble(),
                Positions = new Vector3[body.PartCount],
                Rotations = new Quaternion[body.PartCount]
            };
            Vector3 hips = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            for (int i = 0; i < body.PartCount; i++)
            {
                snapshot.Positions[i] = hips + new Vector3(Mathf.HalfToFloat(reader.ReadUInt16()),
                    Mathf.HalfToFloat(reader.ReadUInt16()), Mathf.HalfToFloat(reader.ReadUInt16()));
                var q = new Quaternion(reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16());
                snapshot.Rotations[i] = Normalized(q);
            }

            // Track the owner's clock against ours from the quickest arrivals, easing down slowly for drift.
            double sample = snapshot.Time - Time.timeAsDouble;
            clockOffset = hasOffset ? System.Math.Max(sample, clockOffset - 0.005) : sample;
            hasOffset = true;
            lastReceived = Time.timeAsDouble;

            int at = buffer.Count;
            while (at > 0 && buffer[at - 1].Time > snapshot.Time) at--;
            if (at > 0 && buffer[at - 1].Time == snapshot.Time) return;
            buffer.Insert(at, snapshot);
            if (buffer.Count > 20) buffer.RemoveAt(0);
        }

        static Quaternion Normalized(Quaternion q)
        {
            float length = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return length > 0.0001f ? new Quaternion(q.x / length, q.y / length, q.z / length, q.w / length) : Quaternion.identity;
        }

        // ---- everyone else -----------------------------------------------------------------------

        void Play()
        {
            var body = Body;
            double now = Time.timeAsDouble;
            bool down = player.IsRagdolledShared;
            bool fresh = now - lastReceived < staleSeconds;
            if (!down || !fresh)
            {
                if (body.IsPuppet) body.SetPuppet(false, lastVelocity);
                if (!down) buffer.Clear();
                return;
            }

            double renderTime = now + clockOffset - interpolationDelay;
            if (buffer.Count == 0 || buffer[0].Time > renderTime) return;   // not enough yet: stay limp meanwhile
            while (buffer.Count >= 2 && buffer[1].Time <= renderTime) buffer.RemoveAt(0);

            Snapshot a = buffer[0];
            Snapshot b = buffer.Count > 1 ? buffer[1] : a;
            float t = b.Time > a.Time ? Mathf.Clamp01((float)((renderTime - a.Time) / (b.Time - a.Time))) : 0f;
            if (b.Time > a.Time) lastVelocity = (b.Positions[hipsIndex] - a.Positions[hipsIndex]) / (float)(b.Time - a.Time);

            if (!body.IsPuppet) body.SetPuppet(true, Vector3.zero);
            for (int i = 0; i < body.PartCount; i++)
                body.MovePuppet(i, Vector3.Lerp(a.Positions[i], b.Positions[i], t), Quaternion.Slerp(a.Rotations[i], b.Rotations[i], t));
        }
    }
}
