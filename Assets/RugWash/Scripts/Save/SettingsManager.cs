using System;
using System.Collections.Generic;
using RugWash.Audio;
using RugWash.Core;
using RugWash.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RugWash.Save
{
    /// <summary>
    /// Liaisons modifiées, identifiées par nom d'action et index de liaison. Les actions étant créées
    /// en code (identifiants aléatoires à chaque lancement), le format JSON natif de l'Input System,
    /// basé sur ces identifiants, ne pourrait pas être réappliqué d'une session à l'autre.
    /// </summary>
    [Serializable]
    public sealed class BindingOverrideSet
    {
        [Serializable]
        public struct Entry
        {
            public string action;
            public int binding;
            public string path;
        }

        public List<Entry> entries = new List<Entry>();

        public static BindingOverrideSet Capture(InputActionAsset asset)
        {
            var set = new BindingOverrideSet();
            foreach (var action in asset)
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    string path = action.bindings[i].overridePath;
                    if (!string.IsNullOrEmpty(path)) set.entries.Add(new Entry { action = action.name, binding = i, path = path });
                }
            return set;
        }

        /// <summary>Applique les entrées valides ; ignore celles qui ne correspondent plus à une liaison.</summary>
        public int ApplyTo(InputActionAsset asset)
        {
            int applied = 0;
            foreach (var entry in entries)
            {
                var action = asset.FindAction(entry.action);
                if (action == null || entry.binding < 0 || entry.binding >= action.bindings.Count || string.IsNullOrEmpty(entry.path)) continue;
                action.ApplyBindingOverride(entry.binding, entry.path);
                applied++;
            }
            return applied;
        }
    }

    /// <summary>Action reconfigurable dans les paramètres.</summary>
    public readonly struct RebindableAction
    {
        public readonly string Label;
        public readonly string ActionName;
        /// <summary>Partie de composite (« up », « down »…) ou null pour un bouton simple.</summary>
        public readonly string CompositePart;

        public RebindableAction(string label, string actionName, string compositePart = null)
        {
            Label = label;
            ActionName = actionName;
            CompositePart = compositePart;
        }
    }

    /// <summary>
    /// Applique réellement les paramètres : volumes (AudioManager), souris (FirstPersonController),
    /// affichage (Screen), qualité (QualitySettings) et liaisons de touches (Input System).
    /// Présent dans le menu principal et dans l'atelier.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsManager : MonoBehaviour
    {
        public static readonly IReadOnlyList<RebindableAction> RebindableActions = new[]
        {
            new RebindableAction("Avancer", "Move", "up"),
            new RebindableAction("Reculer", "Move", "down"),
            new RebindableAction("Aller à gauche", "Move", "left"),
            new RebindableAction("Aller à droite", "Move", "right"),
            new RebindableAction("Courir", "Sprint"),
            new RebindableAction("Interagir", "Interact"),
            new RebindableAction("Scanner la saleté", "Scan"),
            new RebindableAction("Inspecter", "Inspect"),
        };

        static readonly FullScreenMode[] DisplayModes = { FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };

        [SerializeField] AudioManager audioManager;
        [Tooltip("Optionnel (atelier) : reçoit la sensibilité de la souris.")]
        [SerializeField] FirstPersonController player;
        [Tooltip("Optionnel (atelier) : reçoit les liaisons de touches.")]
        [SerializeField] PlayerInputReader input;
        [Tooltip("Optionnel (atelier) : reçoivent l'intensité du balancement.")]
        [SerializeField] ViewModelRig viewModelRig;
        [SerializeField] CameraMotion cameraMotion;

        InputActionAsset ownAsset;

        public SettingsData Current { get; private set; } = new SettingsData();

        /// <summary>Actions utilisées pour la reconfiguration (celles du joueur, ou une copie dans le menu).</summary>
        public InputActionAsset BindingAsset => input != null && input.Asset != null ? input.Asset : ownAsset;

        public event Action Changed;

        void Awake()
        {
            if (input == null) ownAsset = RugWashInputActions.Create();
        }

        void OnDestroy()
        {
            if (ownAsset != null) UnityObjectUtility.SafeDestroy(ownAsset);
        }

        /// <summary>Applique un jeu de paramètres complet (chargement).</summary>
        public void Apply(SettingsData data)
        {
            Current = (data ?? new SettingsData()).Clone();
            SaveValidator.ClampSettings(Current);
            ApplyAudio();
            ApplyMouse();
            ApplyMotion();
            ApplyQuality();
            ApplyDisplay();
            ApplyBindings();
        }

        // ---------------------------------------------------------------- Modifications

        public void SetMasterVolume(float value) { Current.masterVolume = Mathf.Clamp01(value); ApplyAudio(); Changed?.Invoke(); }
        public void SetEffectsVolume(float value) { Current.effectsVolume = Mathf.Clamp01(value); ApplyAudio(); Changed?.Invoke(); }
        public void SetInterfaceVolume(float value) { Current.interfaceVolume = Mathf.Clamp01(value); ApplyAudio(); Changed?.Invoke(); }

        public void SetMouseSensitivity(float value)
        {
            Current.mouseSensitivity = Mathf.Clamp(value, 0.01f, 0.5f);
            ApplyMouse();
            Changed?.Invoke();
        }

        public void SetInvertY(bool value) { Current.invertY = value; ApplyMouse(); Changed?.Invoke(); }

        public void SetMotionIntensity(float value) { Current.motionIntensity = Mathf.Clamp01(value); ApplyMotion(); Changed?.Invoke(); }

        public void SetQualityLevel(int level)
        {
            Current.qualityLevel = Mathf.Clamp(level, 0, QualitySettings.names.Length - 1);
            ApplyQuality();
            Changed?.Invoke();
        }

        public void SetResolution(Vector2Int resolution)
        {
            Current.resolutionWidth = resolution.x;
            Current.resolutionHeight = resolution.y;
            ApplyDisplay();
            Changed?.Invoke();
        }

        public void SetDisplayMode(FullScreenMode mode)
        {
            Current.fullScreenMode = (int)mode;
            ApplyDisplay();
            Changed?.Invoke();
        }

        /// <summary>Enregistre les liaisons actuelles de l'asset (après une reconfiguration).</summary>
        public void CaptureBindings()
        {
            var asset = BindingAsset;
            if (asset == null) return;
            var set = BindingOverrideSet.Capture(asset);
            Current.bindingOverrides = set.entries.Count > 0 ? JsonUtility.ToJson(set) : string.Empty;
            Changed?.Invoke();
        }

        public void ResetBindings()
        {
            var asset = BindingAsset;
            if (asset != null) asset.RemoveAllBindingOverrides();
            Current.bindingOverrides = string.Empty;
            Changed?.Invoke();
        }

        // ---------------------------------------------------------------- Options disponibles

        public static string[] QualityNames => QualitySettings.names;
        public int EffectiveQualityLevel => Current.qualityLevel >= 0 ? Current.qualityLevel : QualitySettings.GetQualityLevel();

        public static IReadOnlyList<FullScreenMode> AvailableDisplayModes => DisplayModes;

        public FullScreenMode EffectiveDisplayMode => Current.fullScreenMode >= 0 ? (FullScreenMode)Current.fullScreenMode : Screen.fullScreenMode;

        public Vector2Int EffectiveResolution => Current.resolutionWidth > 0
            ? new Vector2Int(Current.resolutionWidth, Current.resolutionHeight)
            : new Vector2Int(Screen.width, Screen.height);

        /// <summary>Résolutions proposées par l'écran (sans doublons de fréquence), ou la résolution actuelle.</summary>
        public static List<Vector2Int> AvailableResolutions()
        {
            var list = new List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                var size = new Vector2Int(r.width, r.height);
                if (!list.Contains(size)) list.Add(size);
            }
            if (list.Count == 0) list.Add(new Vector2Int(Screen.width, Screen.height));
            return list;
        }

        public static string DisplayModeLabel(FullScreenMode mode)
        {
            switch (mode)
            {
                case FullScreenMode.ExclusiveFullScreen: return "Plein écran";
                case FullScreenMode.FullScreenWindow: return "Plein écran fenêtré";
                default: return "Fenêtré";
            }
        }

        public static int FindBindingIndex(InputAction action, RebindableAction target)
        {
            for (int i = 0; i < action.bindings.Count; i++)
            {
                var binding = action.bindings[i];
                if (target.CompositePart == null ? !binding.isComposite && !binding.isPartOfComposite
                        : binding.isPartOfComposite && string.Equals(binding.name, target.CompositePart, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        // ---------------------------------------------------------------- Application

        void ApplyAudio()
        {
            if (audioManager == null) return;
            audioManager.MasterVolume = Current.masterVolume;
            audioManager.EffectsVolume = Current.effectsVolume;
            audioManager.InterfaceVolume = Current.interfaceVolume;
        }

        void ApplyMouse()
        {
            if (player == null) return;
            player.LookSensitivity = Current.mouseSensitivity;
            player.InvertY = Current.invertY;
        }

        void ApplyMotion()
        {
            if (viewModelRig != null) viewModelRig.Intensity = Current.motionIntensity;
            if (cameraMotion != null) cameraMotion.Intensity = Current.motionIntensity;
        }

        void ApplyQuality()
        {
            if (Current.qualityLevel >= 0 && Current.qualityLevel < QualitySettings.names.Length && QualitySettings.GetQualityLevel() != Current.qualityLevel)
                QualitySettings.SetQualityLevel(Current.qualityLevel, true);
        }

        void ApplyDisplay()
        {
            if (Current.resolutionWidth <= 0 && Current.fullScreenMode < 0) return;
            var size = EffectiveResolution;
            var mode = EffectiveDisplayMode;
            if (Screen.width == size.x && Screen.height == size.y && Screen.fullScreenMode == mode) return;
            Screen.SetResolution(size.x, size.y, mode);
        }

        void ApplyBindings()
        {
            var asset = BindingAsset;
            if (asset == null) return;
            asset.RemoveAllBindingOverrides();
            if (string.IsNullOrEmpty(Current.bindingOverrides)) return;
            try
            {
                var set = JsonUtility.FromJson<BindingOverrideSet>(Current.bindingOverrides);
                set?.ApplyTo(asset);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RugWash] Liaisons de touches illisibles, valeurs par défaut utilisées : {e.Message}");
                Current.bindingOverrides = string.Empty;
            }
        }
    }
}
