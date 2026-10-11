using RugWash.Core;
using UnityEngine;
using UnityEngine.UI;

namespace RugWash.Audio
{
    public enum AudioCategory
    {
        Effects,
        Interface,
    }

    /// <summary>
    /// Gestion audio de la scène : volumes par catégorie (général, effets, interface), pause,
    /// sons d'interface et sons ponctuels joués par un petit ensemble de sources réutilisées.
    /// Composant de scène, transmis par référence (pas de singleton).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioManager : MonoBehaviour
    {
        [Header("Sons d'interface")]
        [SerializeField] AudioClip uiClick;
        [SerializeField] AudioClip uiConfirm;
        [SerializeField] AudioClip objectiveClip;

        [Header("Volumes")]
        [SerializeField, Range(0f, 1f)] float masterVolume = 0.9f;
        [SerializeField, Range(0f, 1f)] float effectsVolume = 0.8f;
        [SerializeField, Range(0f, 1f)] float interfaceVolume = 0.7f;
        [SerializeField, Range(1, 16)] int oneShotVoices = 8;

        AudioSource uiSource;
        AudioSource[] voices;
        int nextVoice;
        GameStateManager gameState;

        public float MasterVolume
        {
            get => masterVolume;
            set { masterVolume = Mathf.Clamp01(value); AudioListener.volume = masterVolume; }
        }

        public float EffectsVolume
        {
            get => effectsVolume;
            set => effectsVolume = Mathf.Clamp01(value);
        }

        public float InterfaceVolume
        {
            get => interfaceVolume;
            set => interfaceVolume = Mathf.Clamp01(value);
        }

        public AudioClip ConfirmClip => uiConfirm;
        public AudioClip ObjectiveClip => objectiveClip;

        public float VolumeFor(AudioCategory category) => category == AudioCategory.Interface ? interfaceVolume : effectsVolume;

        void Awake()
        {
            AudioListener.volume = masterVolume;
            uiSource = gameObject.AddComponent<AudioSource>();
            uiSource.playOnAwake = false;
            uiSource.ignoreListenerPause = true;
            uiSource.spatialBlend = 0f;

            voices = new AudioSource[oneShotVoices];
            for (int i = 0; i < voices.Length; i++)
            {
                var voice = new GameObject($"Voice_{i}").AddComponent<AudioSource>();
                voice.transform.SetParent(transform, false);
                voice.playOnAwake = false;
                voice.spatialBlend = 0.6f;
                voice.minDistance = 1.5f;
                voice.maxDistance = 20f;
                voices[i] = voice;
            }
        }

        public void Bind(GameStateManager state)
        {
            gameState = state;
            gameState.StateChanged += OnStateChanged;
        }

        void OnDestroy()
        {
            if (gameState != null) gameState.StateChanged -= OnStateChanged;
            AudioListener.pause = false;
        }

        void OnStateChanged(GameState previous, GameState next) => AudioListener.pause = next == GameState.Paused;

        /// <summary>Branche le son de clic sur tous les boutons d'une hiérarchie d'interface.</summary>
        public void RegisterButtons(Component root)
        {
            foreach (var button in root.GetComponentsInChildren<Button>(true))
                button.onClick.AddListener(PlayClick);
        }

        public void PlayClick() => PlayInterface(uiClick);

        public void PlayInterface(AudioClip clip)
        {
            if (clip == null) return;
            uiSource.PlayOneShot(clip, interfaceVolume);
        }

        /// <summary>Son ponctuel positionné ; vole la voix la plus ancienne si toutes sont occupées (nombre borné).</summary>
        public void PlayAt(AudioClip clip, Vector3 position, float volume = 1f)
        {
            if (clip == null) return;
            var voice = voices[nextVoice];
            nextVoice = (nextVoice + 1) % voices.Length;
            voice.transform.position = position;
            voice.pitch = Random.Range(0.94f, 1.06f);
            voice.PlayOneShot(clip, volume * effectsVolume);
        }
    }
}
