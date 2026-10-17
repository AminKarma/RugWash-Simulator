using System;

namespace RugWash.Network
{
    /// <summary>
    /// Chat vocal (grande mise à jour) : ce n'est PAS un service vocal natif de Steam. La voix est captée par le micro via
    /// l'API Microphone de Unity, encodée ici en μ-law (G.711, 8 bits par échantillon, 16 kHz mono : 16 ko/s par joueur qui
    /// parle) et transportée par la connexion du jeu (Netcode). Règles pures, testées en EditMode.
    /// </summary>
    public static class VoiceCodec
    {
        public const int SampleRate = 16000;
        /// <summary>Trame de 40 ms (640 échantillons, 640 octets) : envoyée telle quelle, sans fragmentation.</summary>
        public const int FrameSamples = 640;
        /// <summary>En-tête d'une trame : numéro (2 octets) et drapeaux (1 octet).</summary>
        public const int HeaderBytes = 3;
        public const int MaxPacketBytes = 8 + HeaderBytes + FrameSamples;

        const int Bias = 0x84;
        const int Clip = 32635;
        static readonly float[] decodeTable = BuildDecodeTable();

        static float[] BuildDecodeTable()
        {
            var table = new float[256];
            for (int i = 0; i < 256; i++) table[i] = DecodeSlow((byte)i) / 32768f;
            return table;
        }

        /// <summary>Échantillon [-1, 1] → octet μ-law.</summary>
        public static byte Encode(float sample)
        {
            int pcm = (int)Math.Round(Math.Max(-1f, Math.Min(1f, sample)) * 32767f);
            int sign = (pcm >> 8) & 0x80;
            if (sign != 0) pcm = -pcm;
            if (pcm > Clip) pcm = Clip;
            pcm += Bias;
            int exponent = 7;
            for (int mask = 0x4000; (pcm & mask) == 0 && exponent > 0; mask >>= 1) exponent--;
            int mantissa = (pcm >> (exponent + 3)) & 0x0F;
            return (byte)~(sign | (exponent << 4) | mantissa);
        }

        public static float Decode(byte value) => decodeTable[value];

        static int DecodeSlow(byte value)
        {
            int u = ~value & 0xFF;
            int sign = u & 0x80;
            int exponent = (u >> 4) & 0x07;
            int mantissa = u & 0x0F;
            int sample = (((mantissa << 3) + Bias) << exponent) - Bias;
            return sign != 0 ? -sample : sample;
        }

        public static void EncodeFrame(float[] source, int offset, int count, byte[] destination, int destinationOffset)
        {
            for (int i = 0; i < count; i++) destination[destinationOffset + i] = Encode(source[offset + i]);
        }

        public static void DecodeFrame(byte[] source, int offset, int count, float[] destination, int destinationOffset)
        {
            for (int i = 0; i < count; i++) destination[destinationOffset + i] = decodeTable[source[offset + i]];
        }

        /// <summary>Niveau efficace (RMS) d'une portion de signal.</summary>
        public static float Rms(float[] samples, int offset, int count)
        {
            if (count <= 0) return 0f;
            double sum = 0;
            for (int i = 0; i < count; i++)
            {
                float s = samples[offset + i];
                sum += s * s;
            }
            return (float)Math.Sqrt(sum / count);
        }

        /// <summary>Applique un gain et sature proprement à [-1, 1].</summary>
        public static void ApplyGain(float[] samples, int offset, int count, float gain)
        {
            for (int i = 0; i < count; i++) samples[offset + i] = Math.Max(-1f, Math.Min(1f, samples[offset + i] * gain));
        }
    }

    /// <summary>
    /// Détection de voix (micro « toujours ouvert ») : au-dessus du seuil, la voix part ; elle continue un court instant
    /// après la dernière trame forte (fin de mot non coupée). En mode « appuyer pour parler », la touche remplace ce test.
    /// </summary>
    public sealed class VoiceActivity
    {
        public float Threshold = 0.02f;
        /// <summary>Trames encore envoyées après la dernière trame au-dessus du seuil (8 × 40 ms = 320 ms).</summary>
        public int HangoverFrames = 8;
        int remaining;

        public bool Update(float rms)
        {
            if (rms >= Threshold) remaining = HangoverFrames;
            else if (remaining > 0) remaining--;
            return remaining > 0;
        }

        public void Reset() => remaining = 0;
    }

    /// <summary>
    /// Tampon de lecture d'une voix distante (anneau, accès protégé : la lecture se fait sur le fil audio). La lecture ne
    /// démarre qu'avec <see cref="StartSamples"/> d'avance (gigue du réseau) ; trop de retard est rattrapé en jetant le plus
    /// ancien ; un manque produit du silence, jamais de vieux échantillons.
    /// </summary>
    public sealed class VoiceJitterBuffer
    {
        readonly float[] ring;
        readonly object gate = new object();
        int read, count;
        bool playing;

        /// <summary>Avance avant de commencer à jouer (120 ms) et retard maximal toléré (500 ms).</summary>
        public int StartSamples { get; set; } = VoiceCodec.SampleRate * 12 / 100;
        public int MaxSamples { get; set; } = VoiceCodec.SampleRate / 2;
        public int Buffered { get { lock (gate) return count; } }
        public bool IsPlaying { get { lock (gate) return playing; } }
        public int Underruns { get; private set; }
        public int Dropped { get; private set; }

        public VoiceJitterBuffer(int capacity = VoiceCodec.SampleRate) => ring = new float[Math.Max(capacity, VoiceCodec.FrameSamples * 4)];

        public void Write(float[] samples, int offset, int length)
        {
            lock (gate)
            {
                for (int i = 0; i < length; i++)
                {
                    if (count == ring.Length)
                    {
                        read = (read + 1) % ring.Length;
                        count--;
                        Dropped++;
                    }
                    ring[(read + count) % ring.Length] = samples[offset + i];
                    count++;
                }
                // Trop de retard accumulé : on rattrape pour rester proche du temps réel.
                while (count > MaxSamples)
                {
                    read = (read + 1) % ring.Length;
                    count--;
                    Dropped++;
                }
                if (!playing && count >= StartSamples) playing = true;
            }
        }

        /// <summary>Remplit <paramref name="destination"/> (silence s'il manque des échantillons).</summary>
        public void Read(float[] destination, int offset, int length)
        {
            lock (gate)
            {
                for (int i = 0; i < length; i++)
                {
                    if (playing && count > 0)
                    {
                        destination[offset + i] = ring[read];
                        read = (read + 1) % ring.Length;
                        count--;
                    }
                    else destination[offset + i] = 0f;
                }
                if (playing && count == 0)
                {
                    playing = false;
                    Underruns++;
                }
            }
        }

        public void Clear()
        {
            lock (gate)
            {
                read = count = 0;
                playing = false;
            }
        }
    }
}
