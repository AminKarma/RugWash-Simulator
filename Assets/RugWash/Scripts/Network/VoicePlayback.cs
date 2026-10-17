using UnityEngine;

namespace RugWash.Network
{
    /// <summary>
    /// Voix d'un joueur distant, jouée depuis sa silhouette (grande mise à jour) : source audio 3D (le volume baisse avec la
    /// distance : chat vocal de proximité), clip en flux à 16 kHz alimenté par le tampon de gigue. Indicateur « parle » tant
    /// que des trames arrivent. Coupable localement (ce joueur, ou toutes les voix via le volume des paramètres).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class VoicePlayback : MonoBehaviour
    {
        [Tooltip("Distance à partir de laquelle la voix baisse, et au-delà de laquelle on ne l'entend plus (m).")]
        [SerializeField] float fullVolumeDistance = 2f;
        [SerializeField] float silentDistance = 16f;
        [Tooltip("L'indicateur « parle » reste affiché ce temps après la dernière trame reçue (s).")]
        [SerializeField] float speakingHold = 0.35f;

        readonly VoiceJitterBuffer buffer = new VoiceJitterBuffer();
        readonly float[] decoded = new float[VoiceCodec.FrameSamples];
        AudioSource source;
        AudioClip clip;
        float lastFrameTime = -10f;
        ushort lastSequence;
        bool hasSequence;

        /// <summary>Coupé par le joueur local (ce joueur seulement).</summary>
        public bool Muted { get; set; }
        /// <summary>Volume de toutes les voix (paramètres) : 0 = coupées.</summary>
        public static float GlobalVolume { get; set; } = 1f;
        public bool IsSpeaking => Time.unscaledTime - lastFrameTime < speakingHold;
        public int FramesReceived { get; private set; }
        public int FramesLost { get; private set; }
        public VoiceJitterBuffer Buffer => buffer;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = fullVolumeDistance;
            source.maxDistance = silentDistance;
            source.dopplerLevel = 0f;
            clip = AudioClip.Create("Voice", VoiceCodec.SampleRate, 1, VoiceCodec.SampleRate, true, OnRead);
            source.clip = clip;
        }

        void OnDestroy()
        {
            if (clip != null) Destroy(clip);
        }

        /// <summary>Lecture du flux (fil audio) : tampon de gigue, silence s'il est vide.</summary>
        void OnRead(float[] data) => buffer.Read(data, 0, data.Length);

        /// <summary>Trame μ-law reçue de ce joueur (numéro : trames perdues comptées, trames en retard ignorées).</summary>
        public void Push(byte[] data, int offset, int samples, ushort sequence)
        {
            samples = Mathf.Min(samples, VoiceCodec.FrameSamples);
            if (hasSequence)
            {
                short delta = (short)(sequence - lastSequence);
                if (delta <= 0) return;
                if (delta > 1) FramesLost += delta - 1;
            }
            hasSequence = true;
            lastSequence = sequence;
            FramesReceived++;
            lastFrameTime = Time.unscaledTime;
            if (Muted || GlobalVolume <= 0f) return;
            VoiceCodec.DecodeFrame(data, offset, samples, decoded, 0);
            buffer.Write(decoded, 0, samples);
        }

        void Update()
        {
            if (source == null) return;
            source.volume = Muted ? 0f : GlobalVolume;
            bool wanted = !Muted && GlobalVolume > 0f && (buffer.IsPlaying || buffer.Buffered > 0);
            if (wanted && !source.isPlaying) source.Play();
            else if (!wanted && source.isPlaying && !IsSpeaking)
            {
                source.Stop();
                buffer.Clear();
            }
        }
    }
}
