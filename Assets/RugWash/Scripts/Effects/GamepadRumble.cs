using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Orders;
using RugWash.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RugWash.Effects
{
    /// <summary>Règles pures des vibrations (testées).</summary>
    public static class RumbleRules
    {
        /// <summary>
        /// Vitesse des moteurs (x : basse fréquence, y : haute fréquence) : rien hors manette, hors jeu ou vibrations coupées ;
        /// sinon le maximum entre l'outil utilisé et l'impulsion en cours, multiplié par l'intensité réglée.
        /// </summary>
        public static Vector2 Target(bool gamepadActive, bool gameplayActive, float intensity, bool toolInUse, Vector2 tool, Vector2 pulse)
        {
            if (!gamepadActive || !gameplayActive || intensity <= 0f) return Vector2.zero;
            Vector2 value = Vector2.Max(toolInUse ? tool : Vector2.zero, pulse) * Mathf.Clamp01(intensity);
            return new Vector2(Mathf.Clamp01(value.x), Mathf.Clamp01(value.y));
        }
    }

    /// <summary>
    /// Vibrations de la manette : grondement propre à l'outil utilisé (réglé dans sa définition) et impulsions brèves
    /// (palier de propreté, objectif atteint, scan, outil verrouillé). Coupées en pause, dans les menus, quand la
    /// manette n'est plus le périphérique actif, à la perte du focus et à la fermeture de la scène.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GamepadRumble : MonoBehaviour
    {
        [SerializeField] InputDeviceMonitor monitor;
        [SerializeField] PlayerToolController tools;
        [SerializeField] GameStateManager gameState;
        [Tooltip("Intensité générale (paramètre « Vibrations » ; 0 = désactivées).")]
        [SerializeField, Range(0f, 1f)] float intensity = 1f;

        [Header("Impulsions (basse, haute, durée en s)")]
        [SerializeField] Vector3 objectivePulse = new Vector3(0.45f, 0.7f, 0.35f);
        [SerializeField] Vector3 milestonePulse = new Vector3(0.25f, 0.35f, 0.15f);
        [SerializeField] Vector3 scanPulse = new Vector3(0.1f, 0.3f, 0.12f);
        [SerializeField] Vector3 lockedPulse = new Vector3(0.3f, 0f, 0.1f);

        OrderManager orders;
        CleaningProgressFeedback feedback;
        DirtScanner scanner;
        Vector2 pulse;
        float pulseEnd;
        Vector2 sent;
        Gamepad sentTo;

        public float Intensity
        {
            get => intensity;
            set => intensity = Mathf.Clamp01(value);
        }

        /// <summary>Dernière vitesse demandée aux moteurs (x basse, y haute).</summary>
        public Vector2 Current => sent;

        public bool IsConfigured(out string missing)
        {
            missing = monitor == null ? nameof(monitor) : tools == null ? nameof(tools) : gameState == null ? nameof(gameState) : null;
            return missing == null;
        }

        public void Bind(OrderManager orderManager, CleaningProgressFeedback progress, DirtScanner dirtScanner)
        {
            orders = orderManager;
            feedback = progress;
            scanner = dirtScanner;
            if (orders != null) orders.ObjectiveReached += OnObjective;
            if (feedback != null) feedback.MilestoneReached += OnMilestone;
            if (scanner != null) scanner.Scanned += OnScanned;
            if (tools != null) tools.LockedToolRequested += OnLocked;
        }

        void OnDestroy()
        {
            if (orders != null) orders.ObjectiveReached -= OnObjective;
            if (feedback != null) feedback.MilestoneReached -= OnMilestone;
            if (scanner != null) scanner.Scanned -= OnScanned;
            if (tools != null) tools.LockedToolRequested -= OnLocked;
            Stop();
        }

        void OnObjective(OrderDefinition _) => Pulse(objectivePulse);
        void OnMilestone(float _) => Pulse(milestonePulse);
        void OnScanned(DirtScanResult _) => Pulse(scanPulse);
        void OnLocked(CleaningToolBase _) => Pulse(lockedPulse);

        /// <summary>Déclenche une impulsion (x basse, y haute, z durée en secondes, temps réel).</summary>
        public void Pulse(Vector3 value)
        {
            pulse = Vector2.Max(Time.unscaledTime < pulseEnd ? pulse : Vector2.zero, new Vector2(value.x, value.y));
            pulseEnd = Mathf.Max(pulseEnd, Time.unscaledTime + value.z);
        }

        void Update()
        {
            if (monitor == null || tools == null || gameState == null) return;
            var tool = tools.ActiveTool;
            Vector2 toolRumble = tool != null ? new Vector2(tool.Definition.rumbleLow, tool.Definition.rumbleHigh) : Vector2.zero;
            Vector2 currentPulse = Time.unscaledTime < pulseEnd ? pulse : Vector2.zero;
            Vector2 target = RumbleRules.Target(monitor.IsGamepad, gameState.IsGameplayActive, intensity,
                tool != null && tool.IsUsing, toolRumble, currentPulse);
            Send(target);
        }

        void Send(Vector2 target)
        {
            var pad = Gamepad.current;
            if (pad == sentTo && (target - sent).sqrMagnitude < 1e-6f) return;
            if (sentTo != null && sentTo != pad && sentTo.added) sentTo.ResetHaptics();
            sent = target;
            sentTo = pad;
            if (pad == null) return;
            if (target == Vector2.zero) pad.ResetHaptics();
            else pad.SetMotorSpeeds(target.x, target.y);
        }

        void Stop()
        {
            if (sentTo != null && sentTo.added) sentTo.ResetHaptics();
            sent = Vector2.zero;
            sentTo = null;
        }

        void OnDisable() => Stop();

        void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) Stop();
        }
    }
}
