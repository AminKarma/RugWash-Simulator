using System;
using UnityEngine;

namespace RugWash.Workshop
{
    public enum RobotMode
    {
        /// <summary>Sur sa base (en charge si la batterie n'est pas pleine).</summary>
        Docked = 0,
        /// <summary>En nettoyage autonome.</summary>
        Cleaning = 1,
        /// <summary>Retour vers la base (arrêt demandé, batterie faible ou sol propre).</summary>
        Returning = 2,
    }

    public enum RobotToggleResult
    {
        Started,
        Stopped,
        /// <summary>Batterie trop faible : le robot partira automatiquement une fois rechargé.</summary>
        WillStartWhenCharged,
    }

    /// <summary>
    /// Logique pure du robot aspirateur (testée sans scène) : modes, batterie, retour automatique à la base quand la
    /// batterie est faible puis reprise après recharge, démarrage et arrêt à la demande du joueur.
    /// </summary>
    public sealed class RobotVacuumBrain
    {
        public float DrainPerSecond { get; set; } = 1f / 240f;
        public float ChargePerSecond { get; set; } = 1f / 45f;
        public float LowBattery { get; set; } = 0.2f;
        public float MinimumToStart { get; set; } = 0.3f;

        public RobotMode Mode { get; private set; } = RobotMode.Docked;
        public float Battery01 { get; private set; } = 1f;
        /// <summary>Reprendre le nettoyage dès la batterie pleine (retour pour recharge, ou démarrage demandé batterie faible).</summary>
        public bool ResumeAfterCharge { get; private set; }

        public bool IsCharging => Mode == RobotMode.Docked && Battery01 < 1f;

        public event Action<RobotMode, RobotMode> ModeChanged;

        /// <summary>Action du joueur (touche d'interaction sur le robot ou sa base).</summary>
        public RobotToggleResult Toggle()
        {
            switch (Mode)
            {
                case RobotMode.Cleaning:
                    ResumeAfterCharge = false;
                    SetMode(RobotMode.Returning);
                    return RobotToggleResult.Stopped;
                case RobotMode.Docked when ResumeAfterCharge:
                    ResumeAfterCharge = false;
                    return RobotToggleResult.Stopped;
                default:
                    if (Battery01 < MinimumToStart)
                    {
                        // Trop faible pour repartir : il rentre (ou reste) se recharger, puis repart seul.
                        ResumeAfterCharge = true;
                        return RobotToggleResult.WillStartWhenCharged;
                    }
                    ResumeAfterCharge = false;
                    SetMode(RobotMode.Cleaning);
                    return RobotToggleResult.Started;
            }
        }

        /// <param name="atDock">Le robot est arrivé sur sa base (fin du retour).</param>
        public void Tick(float deltaTime, bool atDock)
        {
            if (deltaTime <= 0f) return;
            switch (Mode)
            {
                case RobotMode.Cleaning:
                    Battery01 = Mathf.Max(0f, Battery01 - DrainPerSecond * deltaTime);
                    if (Battery01 <= LowBattery)
                    {
                        ResumeAfterCharge = true;
                        SetMode(RobotMode.Returning);
                    }
                    break;
                case RobotMode.Returning:
                    // Réserve : le retour consomme peu et ne vide jamais complètement la batterie.
                    Battery01 = Mathf.Max(0.02f, Battery01 - DrainPerSecond * 0.5f * deltaTime);
                    if (atDock) SetMode(RobotMode.Docked);
                    break;
                case RobotMode.Docked:
                    Battery01 = Mathf.Min(1f, Battery01 + ChargePerSecond * deltaTime);
                    if (ResumeAfterCharge && Battery01 >= 1f)
                    {
                        ResumeAfterCharge = false;
                        SetMode(RobotMode.Cleaning);
                    }
                    break;
            }
        }

        /// <summary>Plus rien à nettoyer : retour à la base, sans reprise automatique.</summary>
        public void FinishCleaning()
        {
            if (Mode != RobotMode.Cleaning) return;
            ResumeAfterCharge = false;
            SetMode(RobotMode.Returning);
        }

        /// <summary>Restaure l'état sauvegardé (valeurs bornées ; mode inconnu → sur la base).</summary>
        public void Restore(int mode, float battery01, bool resumeAfterCharge)
        {
            Mode = Enum.IsDefined(typeof(RobotMode), mode) ? (RobotMode)mode : RobotMode.Docked;
            Battery01 = float.IsNaN(battery01) ? 1f : Mathf.Clamp01(battery01);
            ResumeAfterCharge = resumeAfterCharge;
        }

        public string Describe()
        {
            int battery = Mathf.RoundToInt(Battery01 * 100f);
            switch (Mode)
            {
                case RobotMode.Cleaning: return $"en nettoyage · batterie {battery} %";
                case RobotMode.Returning: return ResumeAfterCharge ? $"retour en charge · batterie {battery} %" : $"retour à la base · batterie {battery} %";
                default:
                    if (ResumeAfterCharge) return $"en charge, repart à 100 % · batterie {battery} %";
                    return Battery01 < 1f ? $"en charge · batterie {battery} %" : "prêt sur sa base · batterie 100 %";
            }
        }

        void SetMode(RobotMode mode)
        {
            if (mode == Mode) return;
            var previous = Mode;
            Mode = mode;
            ModeChanged?.Invoke(previous, mode);
        }
    }
}
