using System.Collections.Generic;
using RugWash.Core;
using RugWash.Save;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>
    /// Fenêtre des paramètres (menu principal et pause). Seules des options réellement appliquées
    /// sont proposées : volumes, souris, résolution, mode d'affichage, qualité, touches.
    /// Les modifications s'appliquent immédiatement et sont sauvegardées à la fermeture.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsUI : MonoBehaviour, IModalPanel
    {
        [SerializeField] GameObject panel;
        [SerializeField] Slider masterSlider;
        [SerializeField] Slider effectsSlider;
        [SerializeField] Slider interfaceSlider;
        [SerializeField] Slider sensitivitySlider;
        [SerializeField] Text masterValue;
        [SerializeField] Text effectsValue;
        [SerializeField] Text interfaceValue;
        [SerializeField] Text sensitivityValue;
        [SerializeField] StepperView invertYStepper;
        [SerializeField] StepperView resolutionStepper;
        [SerializeField] StepperView displayModeStepper;
        [SerializeField] StepperView qualityStepper;
        [SerializeField] Transform rebindRoot;
        [SerializeField] RebindRowView rebindTemplate;
        [SerializeField] Button resetBindingsButton;
        [SerializeField] Button closeButton;
        [SerializeField] Text messageLabel;

        readonly List<RebindRowView> rows = new List<RebindRowView>();
        SettingsManager settings;
        SaveManager save;
        GameStateManager gameState;
        List<Vector2Int> resolutions;
        InputActionRebindingExtensions.RebindingOperation rebinding;
        int rebindEndedFrame = -10;

        public bool IsOpen { get; private set; }

        /// <summary>Vrai pendant une reconfiguration (et la frame suivante) : Échap sert alors à l'annuler.</summary>
        public bool IsRebinding => rebinding != null || Time.frameCount - rebindEndedFrame <= 1;

        public IReadOnlyList<RebindRowView> RebindRows => rows;
        public Slider MasterSlider => masterSlider;
        public Slider SensitivitySlider => sensitivitySlider;
        public StepperView InvertYStepper => invertYStepper;
        public StepperView QualityStepper => qualityStepper;
        public Button CloseButton => closeButton;

        public bool IsConfigured(out string missing)
        {
            missing = panel == null ? nameof(panel) : masterSlider == null ? nameof(masterSlider) : effectsSlider == null ? nameof(effectsSlider)
                : interfaceSlider == null ? nameof(interfaceSlider) : sensitivitySlider == null ? nameof(sensitivitySlider)
                : masterValue == null || effectsValue == null || interfaceValue == null || sensitivityValue == null ? "valueLabels"
                : invertYStepper == null || !invertYStepper.IsConfigured ? nameof(invertYStepper)
                : resolutionStepper == null || !resolutionStepper.IsConfigured ? nameof(resolutionStepper)
                : displayModeStepper == null || !displayModeStepper.IsConfigured ? nameof(displayModeStepper)
                : qualityStepper == null || !qualityStepper.IsConfigured ? nameof(qualityStepper)
                : rebindRoot == null ? nameof(rebindRoot) : rebindTemplate == null ? nameof(rebindTemplate)
                : resetBindingsButton == null ? nameof(resetBindingsButton) : closeButton == null ? nameof(closeButton)
                : messageLabel == null ? nameof(messageLabel) : null;
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

            invertYStepper.Stepped += _ => { settings.SetInvertY(!settings.Current.invertY); RefreshValues(); };
            resolutionStepper.Stepped += StepResolution;
            displayModeStepper.Stepped += StepDisplayMode;
            qualityStepper.Stepped += StepQuality;
            resetBindingsButton.onClick.AddListener(ResetBindings);
            closeButton.onClick.AddListener(Close);

            rebindTemplate.gameObject.SetActive(false);
            for (int i = 0; i < SettingsManager.RebindableActions.Count; i++)
            {
                var row = Instantiate(rebindTemplate, rebindRoot);
                row.gameObject.SetActive(true);
                int index = i;
                row.ChangeButton.onClick.AddListener(() => StartRebind(index));
                rows.Add(row);
            }
            panel.SetActive(false);
        }

        public bool Open()
        {
            if (IsOpen) return false;
            resolutions = SettingsManager.AvailableResolutions();
            messageLabel.text = string.Empty;
            panel.SetActive(true);
            IsOpen = true;
            gameState?.PushModal();
            RefreshValues();
            return true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            rebinding?.Cancel();
            panel.SetActive(false);
            IsOpen = false;
            gameState?.PopModal();
            save.SaveSettingsIfDirty();
        }

        void OnDisable() => rebinding?.Cancel();

        void RefreshValues()
        {
            var s = settings.Current;
            masterSlider.SetValueWithoutNotify(s.masterVolume);
            effectsSlider.SetValueWithoutNotify(s.effectsVolume);
            interfaceSlider.SetValueWithoutNotify(s.interfaceVolume);
            sensitivitySlider.SetValueWithoutNotify(s.mouseSensitivity);
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
            masterValue.text = $"{settings.Current.masterVolume * 100f:0} %";
            effectsValue.text = $"{settings.Current.effectsVolume * 100f:0} %";
            interfaceValue.text = $"{settings.Current.interfaceVolume * 100f:0} %";
            sensitivityValue.text = $"{settings.Current.mouseSensitivity * 10f:0.0}";
        }

        void RefreshBindings()
        {
            var asset = settings.BindingAsset;
            for (int i = 0; i < rows.Count; i++)
            {
                var target = SettingsManager.RebindableActions[i];
                var action = asset.FindAction(target.ActionName);
                int index = action != null ? SettingsManager.FindBindingIndex(action, target) : -1;
                rows[i].Set(target.Label, index >= 0 ? action.GetBindingDisplayString(index) : "—");
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

        public void StartRebind(int index)
        {
            if (rebinding != null || index < 0 || index >= rows.Count) return;
            var target = SettingsManager.RebindableActions[index];
            var action = settings.BindingAsset.FindAction(target.ActionName);
            int bindingIndex = action != null ? SettingsManager.FindBindingIndex(action, target) : -1;
            if (bindingIndex < 0) return;

            bool wasEnabled = action.enabled;
            action.Disable();
            rows[index].Set(target.Label, "Appuyez sur une touche… (Échap : annuler)");
            messageLabel.text = string.Empty;
            rebinding = action.PerformInteractiveRebinding(bindingIndex)
                .WithControlsExcluding("<Mouse>")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnMatchWaitForAnother(0.05f)
                .OnComplete(_ => FinishRebind(action, wasEnabled, true))
                .OnCancel(_ => FinishRebind(action, wasEnabled, false))
                .Start();
        }

        void FinishRebind(InputAction action, bool reEnable, bool applied)
        {
            rebinding?.Dispose();
            rebinding = null;
            rebindEndedFrame = Time.frameCount;
            if (reEnable) action.Enable();
            if (applied)
            {
                settings.CaptureBindings();
                messageLabel.text = "Touche modifiée.";
            }
            RefreshBindings();
        }

        void ResetBindings()
        {
            settings.ResetBindings();
            messageLabel.text = "Touches par défaut rétablies.";
            RefreshBindings();
        }
    }
}
