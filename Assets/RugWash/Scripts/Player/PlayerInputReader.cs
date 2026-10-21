using RugWash.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RugWash.Player
{
    /// <summary>
    /// Définition des actions du joueur. Les liaisons clavier désignent des touches physiques
    /// (position QWERTY) : « &lt;Keyboard&gt;/w » est la touche Z sur un clavier AZERTY.
    /// Le déplacement fonctionne donc en ZQSD (AZERTY) comme en WASD (QWERTY) sans réglage.
    /// Chaque liaison appartient au groupe « Keyboard&amp;Mouse » ou « Gamepad » (indications et reconfiguration
    /// par périphérique). Les liaisons manette sont ajoutées après les liaisons clavier : les index des liaisons
    /// clavier, utilisés par les touches reconfigurées sauvegardées, ne changent pas.
    /// </summary>
    public static class RugWashInputActions
    {
        public const string GameplayMap = "Gameplay";
        public const string SystemMap = "System";
        /// <summary>Outils sélectionnables au clavier : touches 1 à 9, puis 0 pour le dixième.</summary>
        public const int ToolSlotCount = 10;
        /// <summary>Emplacements de la barre d'outils (au-delà des touches : molette, LB / RB).</summary>
        public const int ToolbarSlotCount = 11;

        /// <summary>Touche affichée pour un emplacement (« 0 » pour le dixième, vide au-delà).</summary>
        public static string SlotKeyLabel(int slot) => slot >= 1 && slot <= 9 ? slot.ToString() : slot == 10 ? "0" : string.Empty;

        const string Kbm = InputSchemes.KeyboardMouseGroup;
        const string Pad = InputSchemes.GamepadGroup;

        public static InputActionAsset Create()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.name = "RugWashInputActions";

            var gameplay = asset.AddActionMap(GameplayMap);
            var move = gameplay.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w", Kbm).With("Down", "<Keyboard>/s", Kbm)
                .With("Left", "<Keyboard>/a", Kbm).With("Right", "<Keyboard>/d", Kbm);
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow", Kbm).With("Down", "<Keyboard>/downArrow", Kbm)
                .With("Left", "<Keyboard>/leftArrow", Kbm).With("Right", "<Keyboard>/rightArrow", Kbm);

            Button(gameplay, "Look", "<Mouse>/delta", null, InputActionType.Value, "Vector2");
            Button(gameplay, "Sprint", "<Keyboard>/leftShift", "<Gamepad>/leftStickPress");
            Button(gameplay, "Use", "<Mouse>/leftButton", "<Gamepad>/rightTrigger");
            Button(gameplay, "Interact", "<Keyboard>/e", "<Gamepad>/buttonSouth");
            Button(gameplay, "Scan", "<Keyboard>/r", "<Gamepad>/buttonNorth");
            Button(gameplay, "Product", "<Keyboard>/t", "<Gamepad>/buttonWest");
            // Gadgets de farce (v0.1.2) : sortir / changer de gadget.
            Button(gameplay, "Gadget", "<Keyboard>/g", "<Gamepad>/dpad/up");
            // V0.4.2 : lâcher au sol l'objet acheté tenu en main.
            Button(gameplay, "Drop", "<Keyboard>/q", "<Gamepad>/dpad/down");
            Button(gameplay, "ToolScroll", "<Mouse>/scroll/y", null, InputActionType.Value, "Axis");
            for (int i = 1; i <= ToolSlotCount; i++)
            {
                var slot = Button(gameplay, $"Tool{i}", $"<Keyboard>/{i % 10}", null);
                slot.AddBinding($"<Keyboard>/numpad{i % 10}").WithGroup(Kbm);
            }
            // Manette : sticks lus sans processeur (zone morte et courbe appliquées par PlayerInputReader, réglables).
            Button(gameplay, "MoveStick", null, "<Gamepad>/leftStick", InputActionType.Value, "Vector2");
            Button(gameplay, "LookStick", null, "<Gamepad>/rightStick", InputActionType.Value, "Vector2");
            Button(gameplay, "ToolNext", null, "<Gamepad>/rightShoulder");
            Button(gameplay, "ToolPrevious", null, "<Gamepad>/leftShoulder");

            var system = asset.AddActionMap(SystemMap);
            Button(system, "Pause", "<Keyboard>/escape", "<Gamepad>/start");
            Button(system, "Inspect", "<Keyboard>/f", "<Gamepad>/select");
            Button(system, "DebugOverlay", "<Keyboard>/f3", null);
            // Retour (fermer la fenêtre ouverte, reprendre depuis la pause) : Échap au clavier, déjà porté par « Pause ».
            Button(system, "Back", null, "<Gamepad>/buttonEast");
            return asset;
        }

        static InputAction Button(InputActionMap map, string name, string keyboard, string gamepad,
            InputActionType type = InputActionType.Button, string layout = null)
        {
            var action = map.AddAction(name, type, expectedControlLayout: layout);
            if (keyboard != null) action.AddBinding(keyboard).WithGroup(Kbm);
            if (gamepad != null) action.AddBinding(gamepad).WithGroup(Pad);
            return action;
        }
    }

    /// <summary>
    /// Lit les entrées du joueur (clavier/souris et manette). La carte « Gameplay » est coupée en pause et dans les menus.
    /// Les sticks passent par une zone morte radiale réglable puis une courbe de réponse (<see cref="StickResponse"/>).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInputReader : MonoBehaviour, IInputPrompts
    {
        [Tooltip("Périphérique actif : libellés des touches adaptés (clavier ou manette).")]
        [SerializeField] InputDeviceMonitor monitor;
        [Tooltip("Zone morte radiale des sticks (réglable dans les paramètres).")]
        [SerializeField, Range(0.02f, 0.5f)] float stickDeadZone = 0.15f;
        [Tooltip("Courbe du stick de caméra : 1 = linéaire, 2 = plus précis au centre.")]
        [SerializeField, Range(1f, 3f)] float lookStickExponent = 2f;

        InputActionAsset asset;
        InputActionMap gameplay;
        InputAction move, look, sprint, use, interact, scan, product, gadget, drop, toolScroll, pause, inspect, debugOverlay;
        InputAction moveStick, lookStick, toolNext, toolPrevious, back;
        readonly InputAction[] toolSlots = new InputAction[RugWashInputActions.ToolSlotCount];
        bool sprintLatched;

        public InputActionAsset Asset => asset;
        public InputDeviceMonitor Monitor => monitor;

        public float StickDeadZone
        {
            get => stickDeadZone;
            set => stickDeadZone = Mathf.Clamp(value, 0.02f, 0.5f);
        }

        void Awake()
        {
            asset = RugWashInputActions.Create();
            gameplay = asset.FindActionMap(RugWashInputActions.GameplayMap, true);
            var system = asset.FindActionMap(RugWashInputActions.SystemMap, true);

            move = gameplay.FindAction("Move", true);
            look = gameplay.FindAction("Look", true);
            sprint = gameplay.FindAction("Sprint", true);
            use = gameplay.FindAction("Use", true);
            interact = gameplay.FindAction("Interact", true);
            scan = gameplay.FindAction("Scan", true);
            product = gameplay.FindAction("Product", true);
            gadget = gameplay.FindAction("Gadget", true);
            drop = gameplay.FindAction("Drop", true);
            toolScroll = gameplay.FindAction("ToolScroll", true);
            moveStick = gameplay.FindAction("MoveStick", true);
            lookStick = gameplay.FindAction("LookStick", true);
            toolNext = gameplay.FindAction("ToolNext", true);
            toolPrevious = gameplay.FindAction("ToolPrevious", true);
            for (int i = 0; i < toolSlots.Length; i++)
                toolSlots[i] = gameplay.FindAction($"Tool{i + 1}", true);
            pause = system.FindAction("Pause", true);
            inspect = system.FindAction("Inspect", true);
            debugOverlay = system.FindAction("DebugOverlay", true);
            back = system.FindAction("Back", true);

            system.Enable();
            gameplay.Enable();
        }

        void Update()
        {
            // Course à la manette : un clic du stick gauche l'active, elle s'arrête quand le joueur s'immobilise.
            if (!GameplayEnabled)
            {
                sprintLatched = false;
                return;
            }
            if (sprint.WasPressedThisFrame() && sprint.activeControl?.device is Gamepad) sprintLatched = !sprintLatched;
            if (sprintLatched && StickResponse.Apply(moveStick.ReadValue<Vector2>(), stickDeadZone, 1f).sqrMagnitude < 0.01f) sprintLatched = false;
        }

        public bool GameplayEnabled => gameplay != null && gameplay.enabled;

        public void SetGameplayEnabled(bool value)
        {
            if (gameplay == null || gameplay.enabled == value) return;
            if (value) gameplay.Enable();
            else gameplay.Disable();
        }

        /// <summary>Déplacement : clavier s'il est utilisé, sinon stick gauche (zone morte, réponse linéaire).</summary>
        public Vector2 Move
        {
            get
            {
                if (!GameplayEnabled) return Vector2.zero;
                Vector2 keys = move.ReadValue<Vector2>();
                return keys.sqrMagnitude > 0f ? keys : StickResponse.Apply(moveStick.ReadValue<Vector2>(), stickDeadZone, 1f);
            }
        }

        /// <summary>Mouvement de la souris (pixels) de cette frame.</summary>
        public Vector2 LookDelta => GameplayEnabled ? look.ReadValue<Vector2>() : Vector2.zero;

        /// <summary>Stick de caméra après zone morte et courbe, dans [-1, 1] (à multiplier par une vitesse en degrés/s).</summary>
        public Vector2 LookStick => GameplayEnabled ? StickResponse.Apply(lookStick.ReadValue<Vector2>(), stickDeadZone, lookStickExponent) : Vector2.zero;

        public bool SprintHeld => GameplayEnabled && (sprintLatched || sprint.IsPressed());
        public bool UseHeld => GameplayEnabled && use.IsPressed();
        public bool InteractPressed => GameplayEnabled && interact.WasPressedThisFrame();
        public bool ScanPressed => GameplayEnabled && scan.WasPressedThisFrame();
        public bool ProductPressed => GameplayEnabled && product.WasPressedThisFrame();
        /// <summary>Gadget de farce suivant (ou retour aux outils après le dernier).</summary>
        public bool GadgetPressed => GameplayEnabled && gadget.WasPressedThisFrame();
        /// <summary>V0.4.2 : lâcher l'objet tenu en main.</summary>
        public bool DropPressed => GameplayEnabled && drop.WasPressedThisFrame();
        /// <summary>« Utiliser » vient d'être pressé (gadgets : une action par appui).</summary>
        public bool UsePressed => GameplayEnabled && use.WasPressedThisFrame();
        public bool PausePressed => pause != null && pause.WasPressedThisFrame();
        /// <summary>Retour (manette) : ferme la fenêtre ouverte ou reprend la partie ; Échap passe par <see cref="PausePressed"/>.</summary>
        public bool BackPressed => back != null && back.WasPressedThisFrame();
        public bool InspectPressed => inspect != null && inspect.WasPressedThisFrame();
        public bool DebugOverlayPressed => debugOverlay != null && debugOverlay.WasPressedThisFrame();

        /// <summary>Retourne l'emplacement d'outil (1..8) pressé cette frame, sinon 0.</summary>
        public int ToolSlotPressedThisFrame()
        {
            if (!GameplayEnabled) return 0;
            for (int i = 0; i < toolSlots.Length; i++)
                if (toolSlots[i].WasPressedThisFrame()) return i + 1;
            return 0;
        }

        /// <summary>
        /// -1, 0 ou +1 : molette (vers le haut = +1 = outil précédent) ou boutons d'épaule de la manette
        /// (RB = outil suivant = -1, LB = outil précédent = +1).
        /// </summary>
        public int ToolScrollStep
        {
            get
            {
                if (!GameplayEnabled) return 0;
                if (toolNext.WasPressedThisFrame()) return -1;
                if (toolPrevious.WasPressedThisFrame()) return 1;
                float value = toolScroll.ReadValue<float>();
                return value > 0.01f ? 1 : value < -0.01f ? -1 : 0;
            }
        }

        public InputScheme Scheme => monitor != null ? monitor.Scheme : InputScheme.KeyboardMouse;
        GamepadStyle Style => monitor != null ? monitor.Style : GamepadStyle.Xbox;

        /// <summary>Libellé de la touche de l'action pour le périphérique actif (ex. « Z » sur AZERTY, « A » à la manette).</summary>
        public string Label(string actionName) => InputPrompts.Label(asset != null ? asset.FindAction(actionName) : null, Scheme, Style);

        /// <summary>Synonyme historique de <see cref="Label"/>.</summary>
        public string GetBindingLabel(string actionName) => Label(actionName);

        /// <summary>Libellé de la touche « retour » de l'interface pour le périphérique actif.</summary>
        public string BackLabel => InputPrompts.Back(Scheme, Style);

        void OnDestroy()
        {
            if (asset == null) return;
            asset.Disable();
            UnityObjectUtility.SafeDestroy(asset);
        }
    }
}
