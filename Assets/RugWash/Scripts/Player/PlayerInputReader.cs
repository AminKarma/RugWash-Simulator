using RugWash.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RugWash.Player
{
    /// <summary>
    /// Définition des actions du joueur. Les liaisons clavier désignent des touches physiques
    /// (position QWERTY) : « &lt;Keyboard&gt;/w » est la touche Z sur un clavier AZERTY.
    /// Le déplacement fonctionne donc en ZQSD (AZERTY) comme en WASD (QWERTY) sans réglage.
    /// </summary>
    public static class RugWashInputActions
    {
        public const string GameplayMap = "Gameplay";
        public const string SystemMap = "System";
        public const int ToolSlotCount = 8;

        public static InputActionAsset Create()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.name = "RugWashInputActions";

            var gameplay = asset.AddActionMap(GameplayMap);
            var move = gameplay.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");

            gameplay.AddAction("Look", InputActionType.Value, "<Mouse>/delta", expectedControlLayout: "Vector2");
            gameplay.AddAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            gameplay.AddAction("Use", InputActionType.Button, "<Mouse>/leftButton");
            gameplay.AddAction("Interact", InputActionType.Button, "<Keyboard>/e");
            gameplay.AddAction("Scan", InputActionType.Button, "<Keyboard>/r");
            gameplay.AddAction("ToolScroll", InputActionType.Value, "<Mouse>/scroll/y", expectedControlLayout: "Axis");
            for (int i = 1; i <= ToolSlotCount; i++)
            {
                var slot = gameplay.AddAction($"Tool{i}", InputActionType.Button, $"<Keyboard>/{i}");
                slot.AddBinding($"<Keyboard>/numpad{i}");
            }

            var system = asset.AddActionMap(SystemMap);
            system.AddAction("Pause", InputActionType.Button, "<Keyboard>/escape");
            system.AddAction("Inspect", InputActionType.Button, "<Keyboard>/f");
            system.AddAction("DebugOverlay", InputActionType.Button, "<Keyboard>/f3");
            return asset;
        }
    }

    /// <summary>Lit les entrées du joueur. La carte « Gameplay » est coupée en pause et dans les menus.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInputReader : MonoBehaviour
    {
        InputActionAsset asset;
        InputActionMap gameplay;
        InputAction move, look, sprint, use, interact, scan, toolScroll, pause, inspect, debugOverlay;
        readonly InputAction[] toolSlots = new InputAction[RugWashInputActions.ToolSlotCount];

        public InputActionAsset Asset => asset;

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
            toolScroll = gameplay.FindAction("ToolScroll", true);
            for (int i = 0; i < toolSlots.Length; i++)
                toolSlots[i] = gameplay.FindAction($"Tool{i + 1}", true);
            pause = system.FindAction("Pause", true);
            inspect = system.FindAction("Inspect", true);
            debugOverlay = system.FindAction("DebugOverlay", true);

            system.Enable();
            gameplay.Enable();
        }

        public bool GameplayEnabled => gameplay != null && gameplay.enabled;

        public void SetGameplayEnabled(bool value)
        {
            if (gameplay == null || gameplay.enabled == value) return;
            if (value) gameplay.Enable();
            else gameplay.Disable();
        }

        public Vector2 Move => GameplayEnabled ? move.ReadValue<Vector2>() : Vector2.zero;
        public Vector2 LookDelta => GameplayEnabled ? look.ReadValue<Vector2>() : Vector2.zero;
        public bool SprintHeld => GameplayEnabled && sprint.IsPressed();
        public bool UseHeld => GameplayEnabled && use.IsPressed();
        public bool InteractPressed => GameplayEnabled && interact.WasPressedThisFrame();
        public bool ScanPressed => GameplayEnabled && scan.WasPressedThisFrame();
        public bool PausePressed => pause != null && pause.WasPressedThisFrame();
        public bool InspectPressed => inspect != null && inspect.WasPressedThisFrame();
        public bool DebugOverlayPressed => debugOverlay != null && debugOverlay.WasPressedThisFrame();

        /// <summary>Retourne l'emplacement d'outil (1..5) pressé cette frame, sinon 0.</summary>
        public int ToolSlotPressedThisFrame()
        {
            if (!GameplayEnabled) return 0;
            for (int i = 0; i < toolSlots.Length; i++)
                if (toolSlots[i].WasPressedThisFrame()) return i + 1;
            return 0;
        }

        /// <summary>-1, 0 ou +1 selon la molette.</summary>
        public int ToolScrollStep
        {
            get
            {
                if (!GameplayEnabled) return 0;
                float value = toolScroll.ReadValue<float>();
                return value > 0.01f ? 1 : value < -0.01f ? -1 : 0;
            }
        }

        /// <summary>Libellé de touche adapté à la disposition active (ex. « Z » sur AZERTY).</summary>
        public string GetBindingLabel(string actionName)
        {
            var action = asset != null ? asset.FindAction(actionName) : null;
            return action != null ? action.GetBindingDisplayString(0) : "?";
        }

        void OnDestroy()
        {
            if (asset == null) return;
            asset.Disable();
            UnityObjectUtility.SafeDestroy(asset);
        }
    }
}
