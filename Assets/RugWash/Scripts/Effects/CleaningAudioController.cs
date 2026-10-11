using RugWash.Audio;
using UnityEngine;

namespace RugWash.Effects
{
    /// <summary>
    /// Son en boucle d'un outil : fondu à l'activation, hauteur/volume modulés par l'intensité réelle
    /// (contact avec le tapis), arrêt immédiat au changement d'outil, en pause ou à la destruction.
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

        AudioSource source;
        float level;

        public bool IsAudible => source != null && source.isPlaying && source.volume > 0.001f;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.clip = loop;
            source.volume = 0f;
        }

        /// <summary>Appelé chaque frame par l'outil actif.</summary>
        public void Drive(bool active, float contact01, float deltaTime)
        {
            if (loop == null) return;
            level = Mathf.MoveTowards(level, active ? 1f : 0f, fadeSpeed * deltaTime);
            float categoryVolume = audioManager != null ? audioManager.VolumeFor(AudioCategory.Effects) : 1f;
            source.volume = level * (baseVolume + contactVolumeBoost * contact01) * categoryVolume;
            source.pitch = basePitch + contactPitchShift * contact01;

            if (level > 0f && !source.isPlaying) source.Play();
            else if (level <= 0f && source.isPlaying) source.Stop();
        }

        public void StopImmediate()
        {
            level = 0f;
            if (source == null) return;
            source.volume = 0f;
            if (source.isPlaying) source.Stop();
        }

        void OnDisable() => StopImmediate();
    }
}
