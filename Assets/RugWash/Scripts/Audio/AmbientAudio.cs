using UnityEngine;

namespace RugWash.Audio
{
    /// <summary>Ambiance de l'atelier en boucle (son temporaire synthétisé), suit le volume des effets et la pause.</summary>
    [RequireComponent(typeof(AudioSource))]
    [DisallowMultipleComponent]
    public sealed class AmbientAudio : MonoBehaviour
    {
        [SerializeField] AudioManager audioManager;
        [SerializeField] AudioClip loop;
        [SerializeField, Range(0f, 1f)] float volume = 0.35f;

        AudioSource source;

        public bool IsPlaying => source != null && source.isPlaying;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.clip = loop;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
        }

        void Start()
        {
            if (loop != null) source.Play();
        }

        void Update()
        {
            if (audioManager != null) source.volume = volume * audioManager.EffectsVolume;
        }
    }
}
