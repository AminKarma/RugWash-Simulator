using System.Collections;
using System.Collections.Generic;
using RugWash.Core;
using RugWash.Player;
using RugWash.Save;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>
    /// Fenêtre des paramètres (menu principal et pause), en trois onglets : Général (audio, affichage),
    /// Commandes (souris, manette, balancement) et Touches (clavier et manette). Seules des options réellement appliquées
    /// sont proposées. Les modifications s'appliquent immédiatement et sont sauvegardées à la fermeture.
    /// À la manette : LB / RB changent d'onglet.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsUI : MonoBehaviour, IModalPanel
    {
        [SerializeField] GameObject panel;
        [SerializeField] Slider masterSlider;
        [SerializeField] Slider effectsSlider;
        [SerializeField] Slider interfaceSlider;
        [SerializeField] Slider sensitivitySlider;
        [SerializeField] Slider motionSlider;
        [SerializeField] Text masterValue;
        [SerializeField] Text effectsValue;
        [SerializeField] Text interfaceValue;
        [SerializeField] Text sensitivityValue;
        [SerializeField] Text motionValue;
        [SerializeField] StepperView invertYStepper;
        [SerializeField] StepperView resolutionStepper;
        [SerializeField] StepperView displayModeStepper;
        [SerializeField] StepperView qualityStepper;
        [SerializeField] Transform rebindRoot;
        [SerializeField] RebindRowView rebindTemplate;
        [SerializeField] Button resetBindingsButton;
        [SerializeField] Button closeButton;
        [SerializeField] Text messageLabel;

        [Header("Manette")]
        [SerializeField] Slider gamepadLookSlider;
        [SerializeField] Slider deadZoneSlider;
        [SerializeField] Slider vibrationSlider;
        [SerializeField] Text gamepadLookValue;
        [SerializeField] Text deadZoneValue;
        [SerializeField] Text vibrationValue;
        [Tooltip("Optionnel : noms des boutons selon la manette (Xbox ou PlayStation).")]
        [SerializeField] InputDeviceMonitor monitor;

        [Header("Onglets")]
        [SerializeField] List<Button> tabButtons = new List<Button>();
        [SerializeField] List<GameObject> tabPages = new List<GameObject>();

        readonly List<RebindRowView> rows = new List<RebindRowView>();
        SettingsManager settings;
        SaveManager save;
        GameStateManager gameState;
        List<Vector2Int> resolutions;
        InputActionRebindingExtensions.RebindingOperation rebinding;
        Coroutine pendingRebind;
        int rebindEndedFrame = -10;

        public bool IsOpen { get; private set; }

        /// <summary>Vrai pendant une reconfiguration (et la frame suivante) : Échap / Menu servent alors à l'annuler.</summary>
        public bool IsRebinding => rebinding != null || pendingRebind != null || Time.frameCount - rebindEndedFrame <= 1;

        public IReadOnlyList<RebindRowView> RebindRows => rows;
        public Slider MasterSlider => masterSlider;
        public Slider SensitivitySlider => sensitivitySlider;
        public Slider MotionSlider => motionSlider;
        public Slider GamepadLookSlider => gamepadLookSlider;
        public Slider DeadZoneSlider => deadZoneSlider;
        public Slider VibrationSlider => vibrationSlider;
        public StepperView InvertYStepper => invertYStepper;
        public StepperView QualityStepper => qualityStepper;
        public Button CloseButton => closeButton;
        public IReadOnlyList<Button> TabButtons => tabButtons;
        public int CurrentTab { get; private set; }
        public string Message => messageLabel.text;

        public bool IsConfigured(out string missing)
        {
            missing = panel == null ? nameof(panel) : masterSlider == null ? nameof(masterSlider) : effectsSlider == null ? nameof(effectsSlider)
                : interfaceSlider == null ? nameof(interfaceSlider) : sensitivitySlider == null ? nameof(sensitivitySlider)
                : motionSlider == null ? nameof(motionSlider)
                : masterValue == null || effectsValue == null || interfaceValue == null || sensitivityValue == null || motionValue == null ? "valueLabels"
                : invertYStepper == null || !invertYStepper.IsConfigured ? nameof(invertYStepper)
                : resolutionStepper == null || !resolutionStepper.IsConfigured ? nameof(resolutionStepper)
                : displayModeStepper == null || !displayModeStepper.IsConfigured ? nameof(displayModeStepper)
                : qualityStepper == null || !qualityStepper.IsConfigured ? nameof(qualityStepper)
                : rebindRoot == null ? nameof(rebindRoot) : rebindTemplate == null ? nameof(rebindTemplate)
                : resetBindingsButton == null ? nameof(resetBindingsButton) : closeButton == null ? nameof(closeButton)
                : messageLabel == null ? nameof(messageLabel)
                : gamepadLookSlider == null ? nameof(gamepadLookSlider) : deadZoneSlider == null ? nameof(deadZoneSlider)
                : vibrationSlider == null ? nameof(vibrationSlider)
                : gamepadLookValue == null || deadZoneValue == null || vibrationValue == null ? "gamepadValueLabels"
                : tabButtons.Count == 0 || tabButtons.Count != tabPages.Count ? nameof(tabPages) : null;
            return missing == null;
        }

        /// <param name="state">Optionnel : présent dans l'atelier (fenêtre modale), absent du menu principal.</param>
        public void Bind(SettingsManager settingsManager, SaveManager saveManager, GameStateManager state)
        {
            settings = settingsManager;
            save = saveManager;
            gameState = state;

            masterSlider.onValueChanged.AddListener(v => { settings.SetMasterVolume(v); RefreshLabels(); });
            effectsSlider.onValueChanged.AddListener(v => { settings.SetEffectsVolume(v); RefreshLabels(); });
            interfaceSlider.onValueChanged.AddListener(v => { settings.SetInterfaceVolume(v); RefreshLabels(); });
            sensitivitySlider.minValue = 0.02f;
            sensitivitySlider.maxValue = 0.4f;
            sensitivitySlider.onValueChanged.AddListener(v => { settings.SetMouseSensitivity(v); RefreshLabels(); });
            motionSlider.onValueChanged.AddListener(v => { settings.SetMotionIntensity(v); RefreshLabels(); });
            gamepadLookSlider.minValue = 30f;
            gamepadLookSlider.maxValue = 360f;
            gamepadLookSlider.onValueChanged.AddListener(v => { settings.SetGamepadLookSpeed(v); RefreshLabels(); });
            deadZoneSlider.minValue = 0.02f;
            deadZoneSlider.maxValue = 0.5f;
            deadZoneSlider.onValueChanged.AddListener(v => { settings.SetStickDeadZone(v); RefreshLabels(); });
            vibrationSlider.minValue = 0f;
            vibrationSlider.maxValue = 1f;
            vibrationSlider.onValueChanged.AddListener(v => { settings.SetVibration(v); RefreshLabels(); });

            invertYStepper.Stepped += _ => { settings.SetInvertY(!settings.Current.invertY); RefreshValues(); };
            resolutionStepper.Stepped += StepResolution;
            displayModeStepper.Stepped += StepDisplayMode;
            qualityStepper.Stepped += StepQuality;
            resetBindingsButton.onClick.AddListener(ResetBindings);
            closeButton.onClick.AddListener(Close);
            for (int i = 0; i < tabButtons.Count; i++)
            {
                int tab = i;
                tabButtons[i].onClick.AddListener(() => ShowTab(tab));
            }

            rebindTemplate.gameObject.SetActive(false);
            for (int i = 0; i < SettingsManager.RebindableActions.Count; i++)
            {
                var row = Instantiate(rebindTemplate, rebindRoot);
                row.gameObject.SetActive(true);
                int index = i;
                row.ChangeButton.onClick.AddListener(() => StartRebind(index));
                if (row.GamepadButton != null) row.GamepadButton.onClick.AddListener(() => StartGamepadRebind(index));
                var target = SettingsManager.RebindableActions[i];
                row.SetRebindable(target.KeyboardRebindable, target.GamepadRebindable);
                rows.Add(row);
            }
            if (monitor != null) monitor.SchemeChanged += OnSchemeChanged;
            panel.SetActive(false);
        }

        void OnDestroy()
        {
            if (monitor != null) monitor.SchemeChanged -= OnSchemeChanged;
        }

        void OnSchemeChanged(InputScheme _)
        {
            if (IsOpen && rebinding == null) RefreshBindings();
        }

        public bool Open()
        {
            if (IsOpen) return false;
            resolutions = SettingsManager.AvailableResolutions();
            messageLabel.text = string.Empty;
            panel.SetActive(true);
            IsOpen = true;
            gameState?.PushModal();
            ShowTab(0);
            RefreshValues();
            return true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            CancelRebind();
            panel.SetActive(false);
            IsOpen = false;
            gameState?.PopModal();
            save.SaveSettingsIfDirty();
        }

        void OnDisable() => CancelRebind();

        void CancelRebind()
        {
            if (pendingRebind != null)
            {
                StopCoroutine(pendingRebind);
                pendingRebind = null;
                rebindEndedFrame = Time.frameCount;
                RefreshBindings();
            }
            rebinding?.Cancel();
        }

        void Update()
        {
            if (!IsOpen) return;
            var pad = Gamepad.current;
            if (rebinding != null)
            {
                // Annulation depuis l'autre périphérique (Échap pendant une saisie manette, Menu pendant une saisie clavier).
                bool escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
                bool start = pad != null && pad.startButton.wasPressedThisFrame;
                if (escape || start) rebinding.Cancel();
                return;
            }
            if (pendingRebind != null || pad == null) return;
            if (pad.rightShoulder.wasPressedThisFrame) ShowTab((CurrentTab + 1) % tabButtons.Count);
            else if (pad.leftShoulder.wasPressedThisFrame) ShowTab((CurrentTab - 1 + tabButtons.Count) % tabButtons.Count);
        }

        /// <summary>Affiche un onglet (0 Général, 1 Commandes, 2 Touches).</summary>
        public void ShowTab(int index)
        {
            if (tabPages.Count == 0) return;
            CurrentTab = Mathf.Clamp(index, 0, tabPages.Count - 1);
            for (int i = 0; i < tabPages.Count; i++)
            {
                tabPages[i].SetActive(i == CurrentTab);
                tabButtons[i].interactable = i != CurrentTab;
            }
        }

        void RefreshValues()
        {
            var s = settings.Current;
            masterSlider.SetValueWithoutNotify(s.masterVolume);
            effectsSlider.SetValueWithoutNotify(s.effectsVolume);
            interfaceSlider.SetValueWithoutNotify(s.interfaceVolume);
            sensitivitySlider.SetValueWithoutNotify(s.mouseSensitivity);
            motionSlider.SetValueWithoutNotify(s.motionIntensity);
            gamepadLookSlider.SetValueWithoutNotify(s.gamepadLookSpeed);
            deadZoneSlider.SetValueWithoutNotify(s.stickDeadZone);
            vibrationSlider.SetValueWithoutNotify(s.vibration);
            invertYStepper.SetValue(s.invertY ? "Oui" : "Non");
            var resolution = settings.EffectiveResolution;
            resolutionStepper.SetValue($"{resolution.x} × {resolution.y}");
            displayModeStepper.SetValue(SettingsManager.DisplayModeLabel(settings.EffectiveDisplayMode));
            var names = SettingsManager.QualityNames;
            int quality = settings.EffectiveQualityLevel;
            qualityStepper.SetValue(quality >= 0 && quality < names.Length ? names[quality] : "—");
            RefreshLabels();
            RefreshBindings();
        }

        void RefreshLabels()
        {
            var s = settings.Current;
            masterValue.text = $"{s.masterVolume * 100f:0} %";
            effectsValue.text = $"{s.effectsVolume * 100f:0} %";
            interfaceValue.text = $"{s.interfaceVolume * 100f:0} %";
            sensitivityValue.text = $"{s.mouseSensitivity * 10f:0.0}";
            motionValue.text = s.motionIntensity <= 0.001f ? "Désactivé" : $"{s.motionIntensity * 100f:0} %";
            gamepadLookValue.text = $"{s.gamepadLookSpeed:0} °/s";
            deadZoneValue.text = $"{s.stickDeadZone * 100f:0} %";
            vibrationValue.text = s.vibration <= 0.001f ? "Désactivées" : $"{s.vibration * 100f:0} %";
        }

        GamepadStyle Style => monitor != null ? monitor.Style : GamepadStyle.Xbox;

        void RefreshBindings()
        {
            var asset = settings.BindingAsset;
            for (int i = 0; i < rows.Count; i++)
            {
                var target = SettingsManager.RebindableActions[i];
                var action = asset.FindAction(target.ActionName);
                int index = action != null ? SettingsManager.FindBindingIndex(action, target) : -1;
                string keyboard = target.KeyboardHint ?? (index >= 0 ? action.GetBindingDisplayString(index) : "—");
                rows[i].Set(target.Label, keyboard);
                var padAction = asset.FindAction(target.GamepadActionName);
                int padIndex = padAction != null ? SettingsManager.FindBindingIndex(padAction, target, InputSchemes.GamepadGroup) : -1;
                rows[i].SetGamepad(padIndex >= 0 ? InputPrompts.GamepadControl(padAction.bindings[padIndex].effectivePath, Style) : "—");
            }
        }

        void StepResolution(int direction)
        {
            if (resolutions.Count == 0) return;
            int current = resolutions.IndexOf(settings.EffectiveResolution);
            int next = current < 0 ? resolutions.Count - 1 : (current + direction + resolutions.Count) % resolutions.Count;
            settings.SetResolution(resolutions[next]);
            RefreshValues();
        }

        void StepDisplayMode(int direction)
        {
            var modes = SettingsManager.AvailableDisplayModes;
            int current = -1;
            for (int i = 0; i < modes.Count; i++)
                if (modes[i] == settings.EffectiveDisplayMode) current = i;
            int next = current < 0 ? 0 : (current + direction + modes.Count) % modes.Count;
            settings.SetDisplayMode(modes[next]);
            RefreshValues();
        }

        void StepQuality(int direction)
        {
            int count = SettingsManager.QualityNames.Length;
            if (count == 0) return;
            settings.SetQualityLevel((settings.EffectiveQualityLevel + direction + count) % count);
            RefreshValues();
        }

        /// <summary>Reconfigure la touche clavier de la ligne (souris et manette exclues ; Échap annule).</summary>
        public void StartRebind(int index)
        {
            if (rebinding != null || pendingRebind != null || index < 0 || index >= rows.Count) return;
            var target = SettingsManager.RebindableActions[index];
            if (!target.KeyboardRebindable) return;
            var action = settings.BindingAsset.FindAction(target.ActionName);
            int bindingIndex = action != null ? SettingsManager.FindBindingIndex(action, target) : -1;
            if (bindingIndex < 0) return;

            bool wasEnabled = action.enabled;
            action.Disable();
            rows[index].Set(target.Label, "Appuyez sur une touche… (Échap : annuler)");
            messageLabel.text = string.Empty;
            rebinding = action.PerformInteractiveRebinding(bindingIndex)
                .WithControlsExcluding("<Mouse>")
                .WithControlsExcluding("<Gamepad>")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnMatchWaitForAnother(0.05f)
                .OnComplete(_ => FinishRebind(action, bindingIndex, wasEnabled, true, InputSchemes.KeyboardMouseGroup))
                .OnCancel(_ => FinishRebind(action, bindingIndex, wasEnabled, false, InputSchemes.KeyboardMouseGroup))
                .Start();
        }

        /// <summary>
        /// Reconfigure le bouton de manette de la ligne. Les sticks, Menu (pause) et B / Rond (retour) restent fixes ;
        /// Menu annule. La saisie commence une fois relâché le bouton qui a validé « Modifier ».
        /// </summary>
        public void StartGamepadRebind(int index)
        {
            if (rebinding != null || pendingRebind != null || index < 0 || index >= rows.Count) return;
            var target = SettingsManager.RebindableActions[index];
            if (!target.GamepadRebindable) return;
            var action = settings.BindingAsset.FindAction(target.GamepadActionName);
            int bindingIndex = action != null ? SettingsManager.FindBindingIndex(action, target, InputSchemes.GamepadGroup) : -1;
            if (bindingIndex < 0) return;
            rows[index].SetGamepad("Appuyez sur un bouton… (Menu : annuler)");
            messageLabel.text = string.Empty;
            pendingRebind = StartCoroutine(GamepadRebindWhenReleased(action, bindingIndex));
        }

        IEnumerator GamepadRebindWhenReleased(InputAction action, int bindingIndex)
        {
            yield return null;
            while (AnyGamepadButtonHeld()) yield return null;
            pendingRebind = null;
            bool wasEnabled = action.enabled;
            action.Disable();
            rebinding = action.PerformInteractiveRebinding(bindingIndex)
                .WithControlsHavingToMatchPath("<Gamepad>")
                .WithControlsExcluding("<Gamepad>/leftStick")
                .WithControlsExcluding("<Gamepad>/leftStick/*")
                .WithControlsExcluding("<Gamepad>/rightStick")
                .WithControlsExcluding("<Gamepad>/rightStick/*")
                .WithControlsExcluding("<Gamepad>/buttonEast")
                .WithCancelingThrough("<Gamepad>/start")
                .OnMatchWaitForAnother(0.05f)
                .OnComplete(_ => FinishRebind(action, bindingIndex, wasEnabled, true, InputSchemes.GamepadGroup))
                .OnCancel(_ => FinishRebind(action, bindingIndex, wasEnabled, false, InputSchemes.GamepadGroup))
                .Start();
        }

        static bool AnyGamepadButtonHeld()
        {
            var pad = Gamepad.current;
            if (pad == null) return false;
            foreach (var control in pad.allControls)
                if (control is UnityEngine.InputSystem.Controls.ButtonControl button && button.isPressed) return true;
            return false;
        }

        void FinishRebind(InputAction action, int bindingIndex, bool reEnable, bool applied, string group)
        {
            rebinding?.Dispose();
            rebinding = null;
            rebindEndedFrame = Time.frameCount;
            if (reEnable) action.Enable();
            if (applied)
            {
                settings.CaptureBindings();
                string duplicate = FindDuplicate(action, bindingIndex, group);
                messageLabel.text = duplicate == null ? "Touche modifiée." : $"Touche modifiée — également utilisée pour « {duplicate} ».";
            }
            RefreshBindings();
        }

        /// <summary>Autre ligne du même périphérique utilisant déjà cette touche (avertissement, sans blocage).</summary>
        string FindDuplicate(InputAction action, int bindingIndex, string group)
        {
            string path = action.bindings[bindingIndex].effectivePath;
            var asset = settings.BindingAsset;
            foreach (var target in SettingsManager.RebindableActions)
            {
                bool gamepad = group == InputSchemes.GamepadGroup;
                var other = asset.FindAction(gamepad ? target.GamepadActionName : target.ActionName);
                if (other == null) continue;
                int index = SettingsManager.FindBindingIndex(other, target, group);
                if (index < 0 || (other == action && index == bindingIndex)) continue;
                if (string.Equals(other.bindings[index].effectivePath, path, System.StringComparison.OrdinalIgnoreCase)) return target.Label;
            }
            return null;
        }

        void ResetBindings()
        {
            settings.ResetBindings();
            messageLabel.text = "Touches et boutons par défaut rétablis.";
            RefreshBindings();
        }
    }
}
