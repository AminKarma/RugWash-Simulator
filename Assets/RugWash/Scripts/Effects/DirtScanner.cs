using System;
using RugWash.Audio;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Player;
using UnityEngine;

namespace RugWash.Effects
{
    /// <summary>Minuterie pure du scan : durée d'affichage puis temps de recharge (testable sans scène).</summary>
    public sealed class ScanTimer
    {
        public float Duration { get; }
        public float Cooldown { get; }
        public float ActiveRemaining { get; private set; }
        public float CooldownRemaining { get; private set; }
        /// <summary>Temps écoulé depuis le début du scan en cours.</summary>
        public float Elapsed => IsActive ? Duration - ActiveRemaining : 0f;

        public bool IsActive => ActiveRemaining > 0f;
        public bool IsReady => !IsActive && CooldownRemaining <= 0f;

        /// <summary>0 juste après un scan, 1 quand le scan est de nouveau disponible.</summary>
        public float Readiness01 => IsActive ? 0f : Cooldown <= 0f ? 1f : 1f - Mathf.Clamp01(CooldownRemaining / Cooldown);

        public ScanTimer(float duration, float cooldown)
        {
            Duration = Mathf.Max(0.1f, duration);
            Cooldown = Mathf.Max(0f, cooldown);
        }

        public bool TryStart()
        {
            if (!IsReady) return false;
            ActiveRemaining = Duration;
            return true;
        }

        /// <returns>Vrai si le scan vient de se terminer pendant ce pas.</returns>
        public bool Tick(float deltaTime)
        {
            if (deltaTime <= 0f) return false;
            if (IsActive)
            {
                ActiveRemaining = Mathf.Max(0f, ActiveRemaining - deltaTime);
                if (ActiveRemaining > 0f) return false;
                CooldownRemaining = Cooldown;
                return true;
            }
            CooldownRemaining = Mathf.Max(0f, CooldownRemaining - deltaTime);
            return false;
        }

        /// <summary>Intensité d'affichage : pleine pendant le scan, fondu sur la dernière fraction.</summary>
        public float Strength(float fadeOutSeconds)
        {
            if (!IsActive) return 0f;
            return fadeOutSeconds <= 0f ? 1f : Mathf.Clamp01(ActiveRemaining / fadeOutSeconds);
        }
    }

    /// <summary>
    /// Scan de saleté : sur demande du joueur, une onde part de sa position et révèle sur le tapis les zones
    /// réellement sales (même seuil que la grille) et les résidus de produit, pendant une durée limitée,
    /// puis le scan se recharge. Le rendu est fait par le shader du tapis à partir des textures de contrôle.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DirtScanner : MonoBehaviour
    {
        static readonly int ScanParamsId = Shader.PropertyToID("_RugScanParams");
        static readonly int ScanOriginId = Shader.PropertyToID("_RugScanOrigin");
        static readonly int ScanThresholdsId = Shader.PropertyToID("_RugScanThresholds");
        static readonly int ScanDirtColorId = Shader.PropertyToID("_RugScanDirtColor");
        static readonly int ScanResidueColorId = Shader.PropertyToID("_RugScanResidueColor");

        [SerializeField] PlayerInputReader input;
        [SerializeField] GameStateManager gameState;
        [SerializeField] RugCleaningController rug;
        [Tooltip("Origine de l'onde (caméra du joueur).")]
        [SerializeField] Transform origin;
        [SerializeField] AudioManager audioManager;
        [SerializeField] AudioClip scanClip;
        [SerializeField] AudioClip deniedClip;

        [Header("Réglages")]
        [SerializeField, Min(0.5f)] float duration = 6f;
        [SerializeField, Min(0f)] float cooldown = 10f;
        [Tooltip("Vitesse de propagation de l'onde (m/s).")]
        [SerializeField, Min(0.5f)] float waveSpeed = 7f;
        [SerializeField, Min(0.01f)] float waveWidth = 0.35f;
        [SerializeField, Min(0f)] float fadeOut = 0.8f;
        [SerializeField, Range(0.01f, 1f)] float residueThreshold = DirtScan.DefaultResidueThreshold;
        [SerializeField] Color dirtColor = new Color(1f, 0.55f, 0.12f, 1f);
        [SerializeField] Color residueColor = new Color(0.25f, 0.75f, 1f, 1f);

        ScanTimer timer;
        Vector3 waveOrigin;

        public float Duration => duration;
        public float Cooldown => cooldown;
        public bool IsActive => timer != null && timer.IsActive;
        public bool IsReady => timer != null && timer.IsReady;
        public float ActiveRemaining => timer?.ActiveRemaining ?? 0f;
        public float CooldownRemaining => timer?.CooldownRemaining ?? 0f;
        public float Readiness01 => timer?.Readiness01 ?? 0f;
        /// <summary>Intensité actuelle envoyée au shader (0 = rien n'est surligné).</summary>
        public float CurrentStrength { get; private set; }
        public float CurrentRadius { get; private set; }
        public DirtScanResult LastResult { get; private set; }

        /// <summary>Scan lancé (résultat chiffré calculé sur la grille).</summary>
        public event Action<DirtScanResult> Scanned;
        /// <summary>Demande refusée (scan en cours ou en recharge) : temps restant avant disponibilité.</summary>
        public event Action<float> Denied;

        public bool IsConfigured(out string missing)
        {
            missing = input == null ? nameof(input) : gameState == null ? nameof(gameState) : rug == null ? nameof(rug)
                : origin == null ? nameof(origin) : null;
            return missing == null;
        }

        void Awake()
        {
            timer = new ScanTimer(duration, cooldown);
            Shader.SetGlobalVector(ScanThresholdsId, new Vector4(DirtScan.DirtThreshold, residueThreshold, 0f, 0f));
            Shader.SetGlobalColor(ScanDirtColorId, dirtColor);
            Shader.SetGlobalColor(ScanResidueColorId, residueColor);
            PushToShader();
        }

        void OnDisable()
        {
            CurrentStrength = 0f;
            Shader.SetGlobalVector(ScanParamsId, Vector4.zero);
        }

        void Update()
        {
            if (input != null && input.ScanPressed) TryScan();

            if (timer.Tick(Time.deltaTime)) CurrentStrength = 0f;
            PushToShader();
        }

        /// <summary>Lance un scan si possible. Retourne faux (et signale le refus) pendant un scan ou la recharge.</summary>
        public bool TryScan()
        {
            if (gameState != null && !gameState.IsGameplayActive) return false;
            if (rug == null || rug.Grid == null) return false;
            if (!timer.TryStart())
            {
                if (audioManager != null) audioManager.PlayInterface(deniedClip);
                Denied?.Invoke(timer.IsActive ? 0f : timer.CooldownRemaining);
                return false;
            }

            Vector3 from = origin != null ? origin.position : transform.position;
            waveOrigin = new Vector3(from.x, rug.transform.position.y, from.z);
            LastResult = DirtScan.Analyze(rug.Grid, residueThreshold);
            if (audioManager != null) audioManager.PlayEffect(scanClip, 0.8f);
            Scanned?.Invoke(LastResult);
            PushToShader();
            return true;
        }

        void PushToShader()
        {
            CurrentStrength = timer.Strength(fadeOut);
            CurrentRadius = timer.IsActive ? timer.Elapsed * waveSpeed : 0f;
            Shader.SetGlobalVector(ScanParamsId, new Vector4(CurrentStrength, CurrentRadius, waveWidth, Time.time));
            Shader.SetGlobalVector(ScanOriginId, waveOrigin);
        }
    }
}
