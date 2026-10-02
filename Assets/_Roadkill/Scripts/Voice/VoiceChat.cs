using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Proximity voice (GDD section 11), built on Netcode so it needs no accounts or services.
    /// The owner captures the microphone at 16 kHz, mu-law encodes 20 ms frames and sends them
    /// unreliably to everyone else. Each listener plays them from a 3D AudioSource on the speaker's
    /// head: linear falloff to 25 m, muffled when a wall is in between, mouth flapping with the volume.
    /// Open mic with voice activation by default; F2 switches to push-to-talk on V; F3 hears yourself.
    /// Production would move to Opus over Steam or Vivox; the interface here stays the same.
    /// </summary>
    public class VoiceChat : NetworkBehaviour
    {
        public Transform mouthPoint;
        public Transform mouthVisual;
        [Tooltip("How many times taller the mouth gets at full volume.")]
        public float mouthOpenStretch = 5f;
        public float range = 25f;
        public float openMicThreshold = 0.012f;
        public float hangoverSeconds = 0.3f;

        public bool PushToTalk { get; private set; }
        public bool Loopback { get; private set; }
        public bool Transmitting { get; private set; }
        public float InputLevel { get; private set; }
        public string Status { get; private set; } = "Voice off";

        const int TargetRate = 16000;
        const int MaxPacketBytes = 1000;   // stay under one unreliable datagram

        // Capture (owner)
        string device;
        AudioClip micClip;
        int micRate;
        int readPosition;
        float[] frame;
        readonly List<byte> outgoing = new List<byte>(MaxPacketBytes);
        float hangover;

        // Playback (everyone else, or the owner in loopback)
        AudioSource source;
        AudioLowPassFilter lowPass;
        int playRate;
        readonly Queue<float> playQueue = new Queue<float>();
        bool playing;
        float mouthLevel;
        Vector3 mouthRestScale;
        int mouthAxis = 1;
        float nextOcclusionCheck;
        AudioListener cachedListener;

        public override void OnNetworkSpawn()
        {
            if (IsOwner) StartMicrophone();
        }

        public override void OnNetworkDespawn()
        {
            if (micClip != null) Microphone.End(device);
        }

        void StartMicrophone()
        {
            if (Microphone.devices.Length == 0)
            {
                Status = "No microphone found";
                return;
            }
            device = Microphone.devices[0];
            Microphone.GetDeviceCaps(device, out int minRate, out int maxRate);
            micRate = minRate == 0 && maxRate == 0 ? TargetRate : Mathf.Clamp(TargetRate, minRate, maxRate);
            micClip = Microphone.Start(device, true, 1, micRate);
            frame = new float[micRate / 50];   // 20 ms
            Status = $"Mic: {device}";
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (IsOwner)
            {
                if (RkInput.VoiceModePressed) PushToTalk = !PushToTalk;
                if (RkInput.LoopbackPressed) Loopback = !Loopback;
                Capture();
            }
            UpdateOcclusion();
            UpdateMouth();
        }

        void Capture()
        {
            if (micClip == null) return;
            int position = Microphone.GetPosition(device);
            if (position < 0) return;

            int length = micClip.samples;
            int available = (position - readPosition + length) % length;
            while (available >= frame.Length)
            {
                micClip.GetData(frame, readPosition);   // wraps around the looping clip
                readPosition = (readPosition + frame.Length) % length;
                available -= frame.Length;

                InputLevel = Rms(frame);
                bool wantsToTalk = PushToTalk ? RkInput.PushToTalk : InputLevel > openMicThreshold;
                hangover = wantsToTalk ? hangoverSeconds : hangover - frame.Length / (float)micRate;
                Transmitting = hangover > 0f;
                if (!Transmitting)
                {
                    if (outgoing.Count > 0) Flush();
                    continue;
                }

                foreach (float sample in frame) outgoing.Add(MuLaw.Encode(sample));
                if (outgoing.Count + frame.Length > MaxPacketBytes) Flush();
            }
        }

        void Flush()
        {
            byte[] packet = outgoing.ToArray();
            outgoing.Clear();
            VoiceRpc(packet, micRate);
            if (Loopback) Receive(packet, micRate);
        }

        [Rpc(SendTo.NotOwner, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]
        void VoiceRpc(byte[] packet, int sampleRate)
        {
            PacketsReceived++;
            Receive(packet, sampleRate);
        }

        /// <summary>Voice packets received from other players since start (diagnostics).</summary>
        public static int PacketsReceived { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => PacketsReceived = 0;

        /// <summary>Test hook: send a 440 Hz tone through the voice path, as if spoken.</summary>
        public System.Collections.IEnumerator DebugSendTone(float seconds)
        {
            int rate = micRate > 0 ? micRate : TargetRate;
            int frameLength = rate / 50;
            int frames = Mathf.RoundToInt(seconds * 50f);
            int framesPerPacket = Mathf.Max(1, MaxPacketBytes / frameLength);
            var packet = new List<byte>();
            for (int f = 0; f < frames; f++)
            {
                for (int i = 0; i < frameLength; i++)
                {
                    float t = (f * frameLength + i) / (float)rate;
                    packet.Add(MuLaw.Encode(0.3f * Mathf.Sin(2f * Mathf.PI * 440f * t)));
                }
                if ((f + 1) % framesPerPacket == 0)
                {
                    VoiceRpc(packet.ToArray(), rate);
                    packet.Clear();
                    yield return new WaitForSeconds(framesPerPacket * 0.02f);
                }
            }
        }

        void Receive(byte[] packet, int sampleRate)
        {
            EnsurePlayback(sampleRate);
            float sumSquares = 0f;
            lock (playQueue)
            {
                foreach (byte b in packet)
                {
                    float sample = MuLaw.Decode(b);
                    sumSquares += sample * sample;
                    playQueue.Enqueue(sample);
                }
                // Never let latency build up past half a second.
                while (playQueue.Count > playRate / 2) playQueue.Dequeue();
            }
            mouthLevel = Mathf.Max(mouthLevel, Mathf.Sqrt(sumSquares / Mathf.Max(1, packet.Length)));
        }

        void EnsurePlayback(int sampleRate)
        {
            if (source != null && playRate == sampleRate) return;
            playRate = sampleRate;
            if (source == null)
            {
                source = mouthPoint.gameObject.AddComponent<AudioSource>();
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 1f;
                source.maxDistance = range;
                source.dopplerLevel = 0f;
                source.loop = true;
                lowPass = mouthPoint.gameObject.AddComponent<AudioLowPassFilter>();
                lowPass.cutoffFrequency = 22000f;
            }
            // A one-second streamed clip that pulls samples from the jitter buffer on the audio thread.
            source.clip = AudioClip.Create($"Voice {OwnerClientId}", sampleRate, 1, sampleRate, true, OnAudioRead);
            source.Play();
        }

        void OnAudioRead(float[] data)
        {
            lock (playQueue)
            {
                // Wait for 60 ms of audio before starting, so small network hiccups do not crackle.
                if (!playing && playQueue.Count < playRate * 0.06f)
                {
                    System.Array.Clear(data, 0, data.Length);
                    return;
                }
                playing = true;
                for (int i = 0; i < data.Length; i++)
                {
                    if (playQueue.Count > 0) data[i] = playQueue.Dequeue();
                    else
                    {
                        data[i] = 0f;
                        playing = false;
                    }
                }
            }
        }

        /// <summary>Walls between you and the speaker muffle their voice.</summary>
        void UpdateOcclusion()
        {
            if (lowPass == null || Time.time < nextOcclusionCheck) return;
            nextOcclusionCheck = Time.time + 0.2f;
            if (cachedListener == null || !cachedListener.isActiveAndEnabled)
                cachedListener = FindAnyObjectByType<AudioListener>();
            if (cachedListener == null) return;

            bool blocked = false;
            Vector3 from = cachedListener.transform.position;
            Vector3 to = mouthPoint.position;
            foreach (var hit in Physics.RaycastAll(from, to - from, Vector3.Distance(from, to), ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.attachedRigidbody == null) { blocked = true; break; }   // static world only
            }
            lowPass.cutoffFrequency = blocked ? 1200f : 22000f;
        }

        void UpdateMouth()
        {
            if (mouthVisual == null) return;
            float level = IsOwner && !Loopback ? (Transmitting ? InputLevel : 0f) : mouthLevel;
            float open = Mathf.Clamp01(level * 12f);
            if (mouthRestScale == Vector3.zero)
            {
                mouthRestScale = mouthVisual.localScale;
                // Whichever local axis of the jaw / mouth points most nearly up is the one that opens.
                Vector3 up = mouthVisual.InverseTransformDirection(transform.up);
                Vector3 a = new Vector3(Mathf.Abs(up.x), Mathf.Abs(up.y), Mathf.Abs(up.z));
                mouthAxis = a.x > a.y && a.x > a.z ? 0 : a.y > a.z ? 1 : 2;
            }
            Vector3 scale = mouthRestScale;
            scale[mouthAxis] *= Mathf.Lerp(1f, mouthOpenStretch, open);
            mouthVisual.localScale = scale;
            mouthLevel = Mathf.MoveTowards(mouthLevel, 0f, Time.deltaTime * 0.5f);
        }

        static float Rms(float[] samples)
        {
            float sum = 0f;
            foreach (float s in samples) sum += s * s;
            return Mathf.Sqrt(sum / samples.Length);
        }
    }
}
