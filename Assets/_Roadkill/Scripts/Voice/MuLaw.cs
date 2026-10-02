using UnityEngine;

namespace Roadkill
{
    /// <summary>G.711 mu-law: 16-bit speech squeezed into 8 bits per sample, half the bandwidth of raw PCM.</summary>
    public static class MuLaw
    {
        const int Bias = 0x84;
        const int Clip = 32635;

        public static byte Encode(float sample)
        {
            int pcm = Mathf.Clamp(Mathf.RoundToInt(sample * 32767f), -32768, 32767);
            int sign = (pcm >> 8) & 0x80;
            if (sign != 0) pcm = -pcm;
            if (pcm > Clip) pcm = Clip;
            pcm += Bias;

            int exponent = 7;
            for (int mask = 0x4000; (pcm & mask) == 0 && exponent > 0; mask >>= 1) exponent--;
            int mantissa = (pcm >> (exponent + 3)) & 0x0F;
            return (byte)~(sign | (exponent << 4) | mantissa);
        }

        public static float Decode(byte value)
        {
            int muLaw = ~value & 0xFF;
            int sign = muLaw & 0x80;
            int exponent = (muLaw >> 4) & 0x07;
            int mantissa = muLaw & 0x0F;
            int pcm = ((mantissa << 3) + Bias) << exponent;
            pcm -= Bias;
            return (sign != 0 ? -pcm : pcm) / 32768f;
        }
    }
}
