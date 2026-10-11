using RugWash.Effects;
using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>Résultat du ciblage de l'outil (raycast depuis la caméra).</summary>
    public struct ToolAim
    {
        public Ray Ray;
        public bool HasHit;
        public Vector3 Point;
        public Vector3 Normal;
        public float Distance;
        public RugCleaningController Rug;

        public bool HasRug => HasHit && Rug != null;
    }

    /// <summary>Accès aux consommables du joueur, fourni aux outils (pas d'état global).</summary>
    public interface IToolResources
    {
        ResourceTank Water { get; }
        ResourceTank Soap { get; }
        /// <summary>Cuve d'eau sale de l'extracteur.</summary>
        ResourceTank Recovery { get; }

        ResourceTank Get(ToolResource resource);
    }

    /// <summary>
    /// Base commune des outils : sélection, utilisation effective (viser + clic), consommation,
    /// application sur le tapis visé, son et effets. Choisir un outil ne nettoie rien à lui seul.
    /// </summary>
    public abstract class CleaningToolBase : MonoBehaviour
    {
        [SerializeField] protected ToolDefinition definition;
        [Tooltip("Modèle tenu en main (enfant de la caméra).")]
        [SerializeField] protected Transform viewModel;
        [Tooltip("Son en boucle de l'outil (optionnel).")]
        [SerializeField] protected CleaningAudioController toolAudio;
        [Tooltip("Effet joué quand une zone devient propre (optionnel).")]
        [SerializeField] protected CleaningVFXPool cleanFeedback;

        Vector3 viewModelRestPosition;
        Quaternion viewModelRestRotation;

        protected IToolResources Resources { get; private set; }

        public ToolDefinition Definition => definition;
        public bool IsEquipped { get; private set; }
        public bool IsUsing { get; private set; }

        /// <summary>Vrai si, pendant la dernière frame, l'outil a réellement traité le tapis.</summary>
        public bool IsAffectingRug { get; private set; }

        /// <summary>Vrai si le tapis est visé mais hors de portée.</summary>
        public bool IsTargetOutOfRange { get; private set; }

        /// <summary>Multiplicateur de puissance issu des améliorations (1 = aucune).</summary>
        public float PowerMultiplier { get; set; } = 1f;
        /// <summary>Multiplicateur de consommation issu des améliorations (1 = aucune).</summary>
        public float ConsumptionMultiplier { get; set; } = 1f;

        /// <summary>Puissance réellement appliquée (définition × améliorations).</summary>
        public float EffectivePower => definition != null ? definition.power * PowerMultiplier : 0f;
        public float EffectiveConsumptionPerSecond => definition != null ? definition.resourcePerSecond * ConsumptionMultiplier : 0f;

        public float Range => definition != null ? definition.range : 0f;
        public float Radius => definition != null ? definition.radius : 0f;
        public CleaningAudioController ToolAudio => toolAudio;

        /// <summary>Intensité [0..1] de l'action réelle de la dernière frame (son, effets).</summary>
        protected virtual float ContactIntensity => IsAffectingRug ? 1f : 0f;

        protected ResourceTank ResourceTank => Resources?.Get(definition.resource);

        public virtual bool IsConfigured(out string missing)
        {
            missing = definition == null ? nameof(definition) : viewModel == null ? nameof(viewModel) : null;
            return missing == null;
        }

        public virtual void Initialize(IToolResources resources)
        {
            Resources = resources;
            if (viewModel != null)
            {
                viewModelRestPosition = viewModel.localPosition;
                viewModelRestRotation = viewModel.localRotation;
                viewModel.gameObject.SetActive(false);
            }
        }

        public void Equip()
        {
            IsEquipped = true;
            if (viewModel != null) viewModel.gameObject.SetActive(true);
        }

        public void Unequip()
        {
            StopUsing();
            IsTargetOutOfRange = false;
            IsEquipped = false;
            if (viewModel != null)
            {
                viewModel.localPosition = viewModelRestPosition;
                viewModel.localRotation = viewModelRestRotation;
                viewModel.gameObject.SetActive(false);
            }
        }

        /// <summary>Appelé chaque frame pour l'outil actif.</summary>
        public void Tick(bool wantsUse, in ToolAim aim, float deltaTime)
        {
            SetUsing(wantsUse && CanOperate());

            IsTargetOutOfRange = aim.HasRug && aim.Distance > Range;
            IsAffectingRug = false;
            if (IsUsing && deltaTime > 0f)
            {
                float supply = ConsumeResources(deltaTime);
                if (supply <= 0f)
                {
                    SetUsing(false);
                }
                else if (aim.HasRug && !IsTargetOutOfRange)
                {
                    int cleaned = ApplyToRug(aim.Rug, aim, deltaTime, supply);
                    IsAffectingRug = true;
                    if (cleaned > 0 && cleanFeedback != null) cleanFeedback.Spawn(aim.Point + aim.Normal * 0.02f, aim.Normal);
                }
            }

            UpdateEffects(aim, deltaTime);
            if (toolAudio != null) toolAudio.Drive(IsUsing, ContactIntensity, deltaTime);
            AnimateViewModel(viewModel, viewModelRestPosition, viewModelRestRotation, deltaTime);
        }

        /// <summary>Arrête l'utilisation, les effets et le son (pause, menu, changement d'outil).</summary>
        public void StopUsing()
        {
            SetUsing(false);
            IsAffectingRug = false;
            UpdateEffects(default, 0f);
            if (toolAudio != null) toolAudio.StopImmediate();
        }

        void SetUsing(bool value)
        {
            if (value == IsUsing) return;
            IsUsing = value;
            if (value) OnUseStarted();
            else OnUseStopped();
        }

        /// <summary>Conditions d'utilisation : un consommable doit être disponible.</summary>
        protected virtual bool CanOperate()
        {
            var tank = ResourceTank;
            switch (definition.resource)
            {
                case ToolResource.Water:
                case ToolResource.Soap:
                    return tank != null && !tank.IsEmpty;
                default:
                    return true;
            }
        }

        /// <summary>Consomme les ressources de la frame et retourne la fraction obtenue [0..1].</summary>
        protected virtual float ConsumeResources(float deltaTime)
        {
            if (definition.resource != ToolResource.Water && definition.resource != ToolResource.Soap) return 1f;
            float requested = EffectiveConsumptionPerSecond * deltaTime;
            if (requested <= 0f) return 1f;
            return ResourceTank.Consume(requested) / requested;
        }

        /// <summary>Applique l'outil ; retourne le nombre de cellules devenues propres.</summary>
        protected abstract int ApplyToRug(RugCleaningController rug, in ToolAim aim, float deltaTime, float supply);

        protected virtual void OnUseStarted() { }
        protected virtual void OnUseStopped() { }
        protected virtual void UpdateEffects(in ToolAim aim, float deltaTime) { }

        protected virtual void AnimateViewModel(Transform model, Vector3 restPosition, Quaternion restRotation, float deltaTime) { }

        /// <summary>Ligne d'état pour le HUD (ressource), ou null.</summary>
        public virtual string GetStatusLine()
        {
            var tank = ResourceTank;
            return tank == null ? null : $"{tank.DisplayName} : {tank.Amount:0.0} / {tank.Capacity:0} {tank.Unit}";
        }

        /// <summary>Fraction de ressource pour la jauge du HUD, ou -1 si sans objet.</summary>
        public virtual float GetResourceFraction() => ResourceTank?.Fraction01 ?? -1f;

        /// <summary>Raison empêchant l'utilisation, ou null.</summary>
        public virtual string GetBlockReason()
        {
            var tank = ResourceTank;
            if (tank == null) return null;
            switch (definition.resource)
            {
                case ToolResource.Water when tank.IsEmpty: return "Réservoir d'eau vide — remplissez-le au poste d'eau";
                case ToolResource.Soap when tank.IsEmpty: return "Détergent épuisé — rechargez au poste produits";
                default: return null;
            }
        }

        /// <summary>Verbe d'action affiché dans l'aide contextuelle (« brosser », « rincer »).</summary>
        public abstract string ActionVerb { get; }

        protected static void SetEmission(ParticleSystem system, float rate, Vector3 position, Quaternion rotation)
        {
            if (system == null) return;
            var emission = system.emission;
            emission.rateOverTimeMultiplier = rate;
            if (rate <= 0f) return;
            system.transform.SetPositionAndRotation(position, rotation);
            if (!system.isPlaying) system.Play();
        }

        protected static void StopEmission(ParticleSystem system)
        {
            if (system == null) return;
            var emission = system.emission;
            emission.rateOverTimeMultiplier = 0f;
        }
    }
}
