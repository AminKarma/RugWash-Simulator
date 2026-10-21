using RugWash.Player;
using RugWash.Save;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>
    /// Fenêtre « Personnage » du menu principal (v0.1.2) : aperçu 3D tournant de la silhouette et choix réellement
    /// disponibles (couleur de la tenue, casquette ou non, couleur des gants). Les choix sont sauvegardés chez le joueur et
    /// transmis aux autres joueurs en ligne. L'aperçu (caméra et texture de rendu) n'existe que fenêtre ouverte.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterMenuUI : MonoBehaviour
    {
        [SerializeField] GameObject panel;
        [SerializeField] RawImage preview;
        [SerializeField] StepperView outfitStepper;
        [SerializeField] StepperView capStepper;
        [SerializeField] StepperView glovesStepper;
        [Tooltip("V0.4.2 : couleur de peau.")]
        [SerializeField] StepperView skinStepper;
        [SerializeField] Button rotateLeftButton;
        [SerializeField] Button rotateRightButton;
        [SerializeField] Button closeButton;
        [Header("Aperçu")]
        [SerializeField] Transform previewModel;
        [SerializeField] Camera previewCamera;
        [SerializeField] CharacterAppearance previewAppearance;
        [SerializeField, Min(0f)] float idleSpin = 18f;

        SaveManager save;
        RenderTexture texture;
        float yaw;

        public bool IsOpen { get; private set; }
        public CharacterLook Look { get; private set; }
        public CharacterAppearance PreviewAppearance => previewAppearance;
        public StepperView OutfitStepper => outfitStepper;
        public StepperView CapStepper => capStepper;
        public StepperView GlovesStepper => glovesStepper;
        public StepperView SkinStepper => skinStepper;
        public float Yaw => yaw;

        public bool IsConfigured(out string missing)
        {
            missing = panel == null ? nameof(panel) : preview == null ? nameof(preview) : outfitStepper == null ? nameof(outfitStepper)
                : capStepper == null ? nameof(capStepper) : glovesStepper == null ? nameof(glovesStepper) : closeButton == null ? nameof(closeButton)
                : previewModel == null ? nameof(previewModel) : previewCamera == null ? nameof(previewCamera)
                : previewAppearance == null ? nameof(previewAppearance) : null;
            return missing == null;
        }

        public void Bind(SaveManager saveManager)
        {
            save = saveManager;
            outfitStepper.Stepped += step => Step(0, step);
            capStepper.Stepped += step => Step(1, step);
            glovesStepper.Stepped += step => Step(2, step);
            if (skinStepper != null) skinStepper.Stepped += step => Step(3, step);
            closeButton.onClick.AddListener(Close);
            if (rotateLeftButton != null) rotateLeftButton.onClick.AddListener(() => Rotate(-45f));
            if (rotateRightButton != null) rotateRightButton.onClick.AddListener(() => Rotate(45f));
            previewCamera.enabled = false;
            panel.SetActive(false);
        }

        public bool Open()
        {
            if (IsOpen) return false;
            // L'apparence appartient au profil du joueur (commune à tous ses emplacements).
            Look = CharacterPalette.Clamp(save != null ? save.Profile.character : CharacterPalette.Default);
            if (texture == null)
            {
                texture = new RenderTexture(512, 768, 16, RenderTextureFormat.ARGB32) { name = "CharacterPreview", antiAliasing = 2 };
                texture.Create();
            }
            previewCamera.targetTexture = texture;
            previewCamera.enabled = true;
            preview.texture = texture;
            panel.SetActive(true);
            IsOpen = true;
            Refresh();
            return true;
        }

        /// <summary>Ferme la fenêtre et sauvegarde les choix (s'ils ont changé).</summary>
        public void Close()
        {
            if (!IsOpen) return;
            panel.SetActive(false);
            IsOpen = false;
            previewCamera.enabled = false;
            if (save != null && !save.Profile.character.Equals(Look))
            {
                save.Profile.character = Look;
                save.SaveProfile();
            }
        }

        void OnDestroy()
        {
            if (texture != null)
            {
                texture.Release();
                Destroy(texture);
            }
        }

        public void Rotate(float degrees)
        {
            yaw = Mathf.Repeat(yaw + degrees, 360f);
            ApplyYaw();
        }

        /// <summary>Choix suivant / précédent : 0 tenue, 1 casquette, 2 gants, 3 couleur de peau (V0.4.2).</summary>
        public void Step(int slot, int step)
        {
            var look = Look;
            if (slot == 0) look.outfit += step;
            else if (slot == 1) look.cap += step;
            else if (slot == 3) look.skin += step;
            else look.gloves += step;
            Look = CharacterPalette.Clamp(look);
            Refresh();
        }

        void Refresh()
        {
            previewAppearance.Apply(Look);
            outfitStepper.SetValue(CharacterPalette.Outfits[Look.outfit].name);
            capStepper.SetValue(CharacterPalette.Caps[Look.cap].name);
            glovesStepper.SetValue(CharacterPalette.Gloves[Look.gloves].name);
            if (skinStepper != null) skinStepper.SetValue(CharacterPalette.Skins[Look.skin].name);
        }

        void Update()
        {
            if (!IsOpen) return;
            float spin = idleSpin;
            var pad = Gamepad.current;
            if (pad != null)
            {
                if (pad.leftShoulder.isPressed) spin = -120f;
                else if (pad.rightShoulder.isPressed) spin = 120f;
            }
            yaw = Mathf.Repeat(yaw + spin * Time.unscaledDeltaTime, 360f);
            ApplyYaw();
        }

        void ApplyYaw()
        {
            if (previewModel != null) previewModel.localRotation = Quaternion.Euler(0f, 180f + yaw, 0f);
        }
    }
}
