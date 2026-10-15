using System;
using System.Collections.Generic;
using RugWash.Audio;
using RugWash.Core;
using RugWash.Effects;
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

    /// <summary>Action reconfigurable dans les paramètres (colonne clavier et colonne manette).</summary>
    public readonly struct RebindableAction
    {
        public readonly string Label;
        public readonly string ActionName;
        /// <summary>Partie de composite (« up », « down »…) ou null pour un bouton simple.</summary>
        public readonly string CompositePart;
        /// <summary>La liaison clavier/souris peut être modifiée (le clic gauche et la molette restent fixes).</summary>
        public readonly bool KeyboardRebindable;
        /// <summary>Action portant la liaison manette affichée (ex. « MoveStick » pour le déplacement).</summary>
        public readonly string GamepadActionName;
        public readonly bool GamepadRebindable;
        /// <summary>Texte de la colonne clavier/souris quand la liaison n'y est pas reconfigurable (« Clic gauche », « Molette »).</summary>
        public readonly string KeyboardHint;

        public RebindableAction(string label, string actionName, string compositePart = null, bool keyboardRebindable = true,
            string gamepadActionName = null, bool gamepadRebindable = true, string keyboardHint = null)
        {
            Label = label;
            ActionName = actionName;
            CompositePart = compositePart;
            KeyboardRebindable = keyboardRebindable;
            GamepadActionName = gamepadActionName ?? actionName;
            GamepadRebindable = gamepadRebindable;
            KeyboardHint = keyboardHint;
        }
    }

    /// <summary>
    /// Applique réellement les paramètres : volumes (AudioManager), souris et stick de caméra (FirstPersonController),
    /// zone morte des sticks (PlayerInputReader), vibrations (GamepadRumble), affichage (Screen), qualité (QualitySettings)
    /// et liaisons de touches et de boutons (Input System).
    /// Présent dans le menu principal et dans l'atelier.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsManager : MonoBehaviour
    {
        // Nouvelles actions ajoutées à la fin : les lignes existantes gardent leur position.
        public static readonly IReadOnlyList<RebindableAction> RebindableActions = new[]
        {
            new RebindableAction("Avancer", "Move", "up", gamepadActionName: "MoveStick", gamepadRebindable: false),
            new RebindableAction("Reculer", "Move", "down", gamepadActionName: "MoveStick", gamepadRebindable: false),
            new RebindableAction("Aller à gauche", "Move", "left", gamepadActionName: "MoveStick", gamepadRebindable: false),
            new RebindableAction("Aller à droite", "Move", "right", gamepadActionName: "MoveStick", gamepadRebindable: false),
            new RebindableAction("Courir", "Sprint"),
            new RebindableAction("Interagir", "Interact"),
            new RebindableAction("Scanner la saleté", "Scan"),
            new RebindableAction("Changer de produit", "Product"),
            new RebindableAction("Inspecter", "Inspect"),
            new RebindableAction("Utiliser l'outil", "Use", keyboardRebindable: false, keyboardHint: "Clic gauche"),
            new RebindableAction("Outil suivant", "ToolNext", keyboardRebindable: false, keyboardHint: "Molette / 1-0"),
            new RebindableAction("Outil précédent", "ToolPrevious", keyboardRebindable: false, keyboardHint: "Molette / 1-0"),
            new RebindableAction("Gadget de farce", "Gadget"),
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
        [Tooltip("Optionnel (atelier) : reçoit l'intensité des vibrations.")]
        [SerializeField] GamepadRumble rumble;

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
            ApplyGamepad();
            ApplyMotion();
            ApplyQuality();
            ApplyDisplay();
            ApplyBindings();
        }

        // ---------------------------------------------------------------- Modifications

        public void SetMasterVolume(float value) { Current.masterVolume = Mathf.Clamp01(value); ApplyAudio(); Changed?.Invoke(); }
        public void SetEffectsVolume(float value) { Current.effectsVolume = Mathf.Clamp01(value); ApplyAudio(); Changed?.Invoke(); }
        public void SetInterfaceVolume(float value) { Current.interfaceVolume = Mathf.Clamp01(value); ApplyAudio(); Changed?.Invoke(); }
        public void SetMusicVolume(float value) { Current.musicVolume = Mathf.Clamp01(value); ApplyAudio(); Changed?.Invoke(); }

        public void SetMouseSensitivity(float value)
        {
            Current.mouseSensitivity = Mathf.Clamp(value, 0.01f, 0.5f);
            ApplyMouse();
            Changed?.Invoke();
        }

        public void SetInvertY(bool value) { Current.invertY = value; ApplyMouse(); Changed?.Invoke(); }

        public void SetMotionIntensity(float value) { Current.motionIntensity = Mathf.Clamp01(value); ApplyMotion(); Changed?.Invoke(); }

        public void SetGamepadLookSpeed(float value) { Current.gamepadLookSpeed = Mathf.Clamp(value, 30f, 360f); ApplyGamepad(); Changed?.Invoke(); }
        public void SetStickDeadZone(float value) { Current.stickDeadZone = Mathf.Clamp(value, 0.02f, 0.5f); ApplyGamepad(); Changed?.Invoke(); }
        public void SetVibration(float value) { Current.vibration = Mathf.Clamp01(value); ApplyGamepad(); Changed?.Invoke(); }

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

        /// <summary>Index de la liaison clavier/souris reconfigurable de la ligne (première trouvée), -1 si aucune.</summary>
        public static int FindBindingIndex(InputAction action, RebindableAction target) =>
            FindBindingIndex(action, target, InputSchemes.KeyboardMouseGroup);

        /// <summary>Index de la liaison de la ligne dans le groupe donné (clavier/souris ou manette), -1 si aucune.</summary>
        public static int FindBindingIndex(InputAction action, RebindableAction target, string group)
        {
            if (action == null) return -1;
            bool gamepad = group == InputSchemes.GamepadGroup;
            for (int i = 0; i < action.bindings.Count; i++)
            {
                var binding = action.bindings[i];
                if (!InputPrompts.InGroup(binding, group)) continue;
                if (gamepad || target.CompositePart == null ? !binding.isComposite && !binding.isPartOfComposite
                        : binding.isPartOfComposite && string.Equals(binding.name, target.CompositePart, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        // ---------------------------------------------------------------- Application

        void ApplyAudio()
        {
            // Musique : lecteur unique, conservé entre les scènes.
            if (Application.isPlaying) Audio.MusicPlayer.Ensure().Volume01 = Current.musicVolume;
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

        void ApplyGamepad()
        {
            if (player != null) player.StickLookSpeed = Current.gamepadLookSpeed;
            if (input != null) input.StickDeadZone = Current.stickDeadZone;
            if (rumble != null) rumble.Intensity = Current.vibration;
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
