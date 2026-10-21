using UnityEngine;

namespace RugWash.Audio
{
    /// <summary>
    /// Musique de fond (v0.1.2) : un seul lecteur pour tout le jeu (conservé d'une scène à l'autre, jamais dupliqué), deux
    /// sources pour un fondu enchaîné entre la musique du menu et celle de l'atelier. Le volume « Musique » des paramètres
    /// est plafonné (<see cref="MaxGain"/>) pour que la musique reste sous les sons du jeu ; en pause, elle est atténuée
    /// (elle ignore la pause de l'écouteur pour ne pas s'arrêter net) ; à 0, elle se tait.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MusicPlayer : MonoBehaviour
    {
        /// <summary>Gain maximal de la musique (volume « Musique » à 100 %) : sous les effets du jeu.</summary>
        public const float MaxGain = 0.45f;
        /// <summary>Atténuation pendant la pause.</summary>
        public const float PausedFactor = 0.4f;
        public const float FadeSeconds = 1.5f;

        static MusicPlayer instance;
        AudioSource[] sources;
        int current = -1;

        /// <summary>Volume « Musique » des paramètres (0..1).</summary>
        public float Volume01 { get; set; } = 0.6f;
        /// <summary>Vrai pendant la pause : musique atténuée.</summary>
        public bool Ducked { get; set; }
        public AudioClip CurrentClip => current >= 0 ? sources[current].clip : null;
        /// <summary>Volume visé par la source en cours (pour les tests et le diagnostic).</summary>
        public float TargetVolume => Volume01 * MaxGain * (Ducked ? PausedFactor : 1f);
        public float CurrentVolume => current >= 0 ? sources[current].volume : 0f;

        public static MusicPlayer Instance => instance;

        /// <summary>Le lecteur unique (créé au premier appel, puis conservé entre les scènes).</summary>
        public static MusicPlayer Ensure()
        {
            if (instance != null) return instance;
            var go = new GameObject("[Music]");
            if (Application.isPlaying) DontDestroyOnLoad(go);
            return go.AddComponent<MusicPlayer>();
        }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            sources = new AudioSource[2];
            for (int i = 0; i < sources.Length; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.loop = true;
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.ignoreListenerPause = true;
                source.volume = 0f;
                sources[i] = source;
            }
        }

        void OnEnable() => AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        void OnDisable() => AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;

        /// <summary>
        /// V0.4.2 : périphérique de sortie changé (casque débranché, autre sortie par défaut dans Windows) : Unity relance son
        /// moteur audio sur la nouvelle sortie et arrête les sources ; la musique en cours reprend.
        /// </summary>
        void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            if (deviceWasChanged) ResumeAfterDeviceChange();
        }

        public void ResumeAfterDeviceChange()
        {
            if (current >= 0 && sources[current].clip != null && !sources[current].isPlaying) sources[current].Play();
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        /// <summary>Passe à <paramref name="clip"/> par un fondu enchaîné ; sans effet si elle joue déjà.</summary>
        public void Play(AudioClip clip)
        {
            if (clip == null || (current >= 0 && sources[current].clip == clip && sources[current].isPlaying)) return;
            int next = current < 0 ? 0 : 1 - current;
            var source = sources[next];
            source.Stop();
            source.clip = clip;
            source.volume = 0f;
            source.Play();
            current = next;
        }

        void Update()
        {
            if (current < 0) return;
            float step = Time.unscaledDeltaTime / FadeSeconds;
            for (int i = 0; i < sources.Length; i++)
            {
                var source = sources[i];
                float wanted = i == current ? TargetVolume : 0f;
                source.volume = Mathf.MoveTowards(source.volume, wanted, Mathf.Max(step * MaxGain, 1e-4f));
                if (i != current && source.isPlaying && source.volume <= 0f) source.Stop();
            }
        }
    }
}
