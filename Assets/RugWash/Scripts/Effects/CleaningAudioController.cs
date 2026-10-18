using RugWash.Audio;
using UnityEngine;

namespace RugWash.Effects
{
    /// <summary>
    /// Sons en boucle d'un outil : boucle de fonctionnement (moteur, jet…) et boucle de contact optionnelle
    /// (eau sur les fibres, poils qui frottent…) dosée par l'intensité réelle du contact avec le tapis.
    /// Fondu à l'activation, fondu très court au rangement (pas de clic audible), arrêt immédiat en pause
    /// ou à la désactivation. Volumes soumis à la catégorie « Effets ».
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    [DisallowMultipleComponent]
    public sealed class CleaningAudioController : MonoBehaviour
    {
        [SerializeField] AudioManager audioManager;
        [SerializeField] AudioClip loop;
        [SerializeField, Range(0f, 1f)] float baseVolume = 0.55f;
        [SerializeField, Range(0f, 1f)] float contactVolumeBoost = 0.3f;
        [SerializeField] float basePitch = 1f;
        [SerializeField] float contactPitchShift = -0.08f;
        [SerializeField] float fadeSpeed = 6f;

        [Header("Contact avec le tapis (optionnel)")]
        [SerializeField] AudioClip contactLoop;
        [SerializeField, Range(0f, 1f)] float contactLoopVolume = 0.5f;
        [SerializeField] float contactFadeSpeed = 10f;

        [Tooltip("Durée du fondu de sortie au rangement de l'outil (s).")]
        [SerializeField, Range(0.01f, 0.3f)] float releaseTime = 0.06f;

        AudioSource source;
        AudioSource contactSource;
        float level;
        float contactLevel;
        bool released;

        /// <summary>Boucles de l'outil (préchargées avec la scène, V0.2.5).</summary>
        public AudioClip Loop => loop;
        public AudioClip ContactLoop => contactLoop;

        public bool IsAudible => (source != null && source.isPlaying && source.volume > 0.001f) || IsContactAudible;
        public bool IsContactAudible => contactSource != null && contactSource.isPlaying && contactSource.volume > 0.001f;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            Configure(source, loop);
            if (contactLoop != null)
            {
                contactSource = gameObject.AddComponent<AudioSource>();
                Configure(contactSource, contactLoop);
            }
            // V0.2.5 : sons chargés avec la scène, jamais à la première utilisation de l'outil (chargement et décodage d'un
            // coup en pleine partie). Sans effet s'ils le sont déjà (préchargement à l'import).
            Preload(loop);
            Preload(contactLoop);
        }

        static void Preload(AudioClip clip)
        {
            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        }

        static void Configure(AudioSource audioSource, AudioClip clip)
        {
            audioSource.playOnAwake = false;
            audioSource.loop = true;
            audioSource.spatialBlend = 0f;
            audioSource.clip = clip;
            audioSource.volume = 0f;
        }

        /// <summary>Appelé chaque frame par l'outil actif.</summary>
        /// <param name="contact01">Intensité de travail (module la boucle principale).</param>
        /// <param name="surfaceContact01">Contact réel avec le tapis (dose la boucle de contact ; 0 hors du tapis).</param>
        public void Drive(bool active, float contact01, float surfaceContact01, float deltaTime)
        {
            released = false;
            level = Mathf.MoveTowards(level, active ? 1f : 0f, fadeSpeed * deltaTime);
            contactLevel = Mathf.MoveTowards(contactLevel, active ? Mathf.Clamp01(surfaceContact01) : 0f, contactFadeSpeed * deltaTime);
            Apply(contact01);
        }

        /// <summary>Outil rangé : fondu de sortie très court géré par ce composant (l'outil n'appelle plus Drive).</summary>
        public void Release() => released = true;

        void Update()
        {
            if (!released || (level <= 0f && contactLevel <= 0f)) return;
            // Temps non mis à l'échelle : le fondu se termine même si le jeu vient d'être mis en pause.
            float step = Time.unscaledDeltaTime / releaseTime;
            level = Mathf.MoveTowards(level, 0f, step);
            contactLevel = Mathf.MoveTowards(contactLevel, 0f, step);
            Apply(0f);
        }

        void Apply(float contact01)
        {
            float categoryVolume = audioManager != null ? audioManager.VolumeFor(AudioCategory.Effects) : 1f;
            if (loop != null)
            {
                source.volume = level * (baseVolume + contactVolumeBoost * contact01) * categoryVolume;
                source.pitch = basePitch + contactPitchShift * contact01;
                Toggle(source, level > 0f);
            }
            if (contactSource != null)
            {
                contactSource.volume = contactLevel * contactLoopVolume * categoryVolume;
                Toggle(contactSource, contactLevel > 0f);
            }
        }

        static void Toggle(AudioSource audioSource, bool play)
        {
            if (play && !audioSource.isPlaying) audioSource.Play();
            else if (!play && audioSource.isPlaying) audioSource.Stop();
        }

        public void StopImmediate()
        {
            level = 0f;
            contactLevel = 0f;
            released = false;
            foreach (var audioSource in new[] { source, contactSource })
            {
                if (audioSource == null) continue;
                audioSource.volume = 0f;
                if (audioSource.isPlaying) audioSource.Stop();
            }
        }

        void OnDisable() => StopImmediate();
    }
}
