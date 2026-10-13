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

        /// <summary>Produits spécialisés ajoutés au détergent (null si aucun inventaire).</summary>
        IProductSupply Products { get; }

        /// <summary>Libellés des touches pour les indications des outils (null : libellés clavier par défaut).</summary>
        Core.IInputPrompts Prompts { get; }
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

        [Header("Alignement sur la cible")]
        [Tooltip("Extrémité active du modèle (buse, tête) : l'outil est orienté pour qu'elle pointe vers le point visé.")]
        [SerializeField] protected Transform aimTip;
        [Tooltip("Rotation maximale appliquée pour viser (évite les torsions extrêmes en regardant ses pieds).")]
        [SerializeField, Range(5f, 80f)] float aimMaxAngle = 70f;
        [Tooltip("Rapidité du suivi de la cible (plus élevé = plus vif, plus bas = plus doux).")]
        [SerializeField, Range(2f, 40f)] float aimSharpness = 16f;

        [Header("Portée (outils de contact : manche télescopique)")]
        [Tooltip("Tête de l'outil (enfant direct du modèle), déplacée le long du manche jusqu'au point visé. Vide pour une buse.")]
        [SerializeField] Transform reachHead;
        [Tooltip("Manche (enfant direct du modèle), étiré le long de son axe Z local.")]
        [SerializeField] Transform reachShaft;
        [Tooltip("Longueur du manche au repos (m).")]
        [SerializeField, Min(0f)] float reachShaftLength;
        [Tooltip("Allongement maximal du manche (m).")]
        [SerializeField, Range(0f, 4f)] float maxReachExtension = 2f;

        ToolPose pose;
        Vector3 aimTarget;
        bool snapAim;
        float contactKick;

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

        /// <summary>Faux tant qu'un outil « à débloquer » n'a pas été acheté à la boutique.</summary>
        public bool IsUnlocked { get; set; } = true;

        /// <summary>Multiplicateur de puissance issu des améliorations (1 = aucune).</summary>
        public float PowerMultiplier { get; set; } = 1f;
        /// <summary>Multiplicateur du rayon de la zone traitée issu des améliorations (1 = aucune).</summary>
        public float RadiusMultiplier { get; set; } = 1f;
        /// <summary>Multiplicateur de consommation issu des améliorations (1 = aucune).</summary>
        public float ConsumptionMultiplier { get; set; } = 1f;

        /// <summary>Puissance réellement appliquée (définition × améliorations).</summary>
        public float EffectivePower => definition != null ? definition.power * PowerMultiplier : 0f;
        public float EffectiveConsumptionPerSecond => definition != null ? definition.resourcePerSecond * ConsumptionMultiplier : 0f;

        public float Range => definition != null ? definition.range : 0f;
        public float Radius => definition != null ? definition.radius * RadiusMultiplier : 0f;

        /// <summary>Litres d'eau correspondant à 1 m² de fibres saturées (conversion humidité ↔ litres).</summary>
        protected const float LitersPerSaturatedSquareMeter = 3f;

        /// <summary>Ressource consommée pendant la dernière frame (litres).</summary>
        protected float LastConsumed { get; private set; }
        public CleaningAudioController ToolAudio => toolAudio;
        public Transform AimTip => aimTip;

        /// <summary>Écart (degrés) entre l'axe de l'outil et la direction du point visé, après alignement.</summary>
        public float AimErrorDegrees => pose != null ? pose.AimErrorDegrees : 0f;

        /// <summary>Écart qu'aurait l'outil sans alignement (pose de repos), pour le diagnostic.</summary>
        public float UnalignedErrorDegrees => pose != null ? pose.UnalignedErrorDegrees : 0f;

        /// <summary>Outil de contact (brosse, aspirateur, extracteur, balai) : sa tête se pose sur le point visé.</summary>
        public bool IsContactTool => reachHead != null && reachShaft != null && reachShaftLength > 0f;

        /// <summary>Allongement actuel du manche (m), 0 pour une buse.</summary>
        public float ReachExtension => pose != null ? pose.Extension : 0f;

        /// <summary>Point que l'extrémité active vise (point traité, ou point à portée maximale sur le rayon).</summary>
        public Vector3 AimTarget => aimTarget;

        /// <summary>Distance (m) entre l'extrémité active et le point visé après la pose de la frame (-1 sans extrémité).</summary>
        public float TipToTargetMeters { get; private set; } = -1f;

        /// <summary>Événement déclenché quand l'outil commence réellement à traiter le tapis (retours sonores/visuels).</summary>
        public event System.Action<CleaningToolBase> ContactStarted;

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
                pose = new ToolPose(viewModel, aimTip, reachHead, reachShaft, reachShaftLength, maxReachExtension)
                {
                    MaxAngle = aimMaxAngle,
                    Sharpness = aimSharpness,
                };
                viewModel.gameObject.SetActive(false);
            }
        }

        public void Equip()
        {
            IsEquipped = true;
            snapAim = true;
            if (viewModel != null) viewModel.gameObject.SetActive(true);
        }

        public void Unequip()
        {
            StopUsing(immediateAudio: false);
            contactKick = 0f;
            IsTargetOutOfRange = false;
            IsEquipped = false;
            if (viewModel != null)
            {
                pose?.ResetState();
                pose?.ResetToRest();
                viewModel.gameObject.SetActive(false);
            }
        }

        /// <summary>Appelé chaque frame pour l'outil actif.</summary>
        public void Tick(bool wantsUse, in ToolAim aim, float deltaTime)
        {
            SetUsing(wantsUse && IsUnlocked && CanOperate());

            IsTargetOutOfRange = aim.HasRug && aim.Distance > Range;
            bool wasAffecting = IsAffectingRug;
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

            if (IsAffectingRug && !wasAffecting)
            {
                contactKick = 1f;
                ContactStarted?.Invoke(this);
            }
            contactKick = Mathf.MoveTowards(contactKick, 0f, deltaTime * 6f);

            if (viewModel != null && pose != null)
            {
                // Portée et orientation sont calculées sur la pose de repos (manche allongé jusqu'au point visé pour un outil
                // de contact), puis l'orientation est appliquée par-dessus l'animation propre à l'outil (brossage, recul…).
                pose.ResetToRest();
                bool aligned = aimTip != null;
                if (aligned)
                {
                    aimTarget = ToolReach.Target(IsContactTool, aim.HasHit, aim.Point, aim.Distance, aim.Ray, Range);
                    pose.Solve(aimTarget, deltaTime, snapAim);
                    snapAim = false;
                }
                AnimateViewModel(viewModel, viewModelRestPosition, viewModelRestRotation, deltaTime);
                // Petit choc au premier contact avec le tapis (aller-retour en ~0,17 s).
                viewModel.localPosition += new Vector3(0f, -0.008f, -0.025f) * Mathf.Sin((1f - contactKick) * Mathf.PI) * (contactKick > 0f ? 1f : 0f);
                if (aligned) pose.Apply();
                TipToTargetMeters = aligned ? Vector3.Distance(aimTip.position, aimTarget) : -1f;
            }
            UpdateEffects(aim, deltaTime);
            if (toolAudio != null) toolAudio.Drive(IsUsing, ContactIntensity, IsAffectingRug ? ContactIntensity : 0f, deltaTime);
        }

        /// <summary>Arrête l'utilisation, les effets et le son (pause, menu, changement d'outil).</summary>
        public void StopUsing() => StopUsing(immediateAudio: true);

        /// <param name="immediateAudio">Faux au rangement : le son s'éteint par un fondu très court (évite le clic).</param>
        void StopUsing(bool immediateAudio)
        {
            SetUsing(false);
            IsAffectingRug = false;
            UpdateEffects(default, 0f);
            if (toolAudio == null) return;
            if (immediateAudio) toolAudio.StopImmediate();
            else toolAudio.Release();
        }

        /// <summary>Couleur et quantité de saleté de la cellule visée (pour teinter débris et éclaboussures).</summary>
        protected static bool TrySampleDirt(in ToolAim aim, out Color color, out RugCell cell)
        {
            color = default;
            cell = default;
            if (!aim.HasRug || !aim.Rug.TrySampleCell(aim.Point, out cell)) return false;
            DirtType type = cell.Stain + cell.Loosened >= cell.Dust ? cell.StainType : cell.DustType;
            color = aim.Rug.DirtTable != null ? aim.Rug.DirtTable.GetColor(type) : Color.gray;
            return true;
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
            LastConsumed = 0f;
            if (definition.resource != ToolResource.Water && definition.resource != ToolResource.Soap) return 1f;
            float requested = EffectiveConsumptionPerSecond * deltaTime;
            if (requested <= 0f) return 1f;
            LastConsumed = ResourceTank.Consume(requested);
            return LastConsumed / requested;
        }

        /// <summary>
        /// Applique l'opération sur la zone de l'outil : disque, ou éventail élargi perpendiculairement à la visée
        /// (<see cref="ToolDefinition.spreadAspect"/>), avec le ruissellement défini par l'outil.
        /// </summary>
        protected int ApplyArea<T>(RugCleaningController rug, in ToolAim aim, ref T operation) where T : struct, ICellOperation
        {
            Vector3 across = SpreadAxis(aim);
            float water = definition.resource == ToolResource.Water ? LastConsumed / LitersPerSaturatedSquareMeter : 0f;
            var runoff = new RunoffProfile(definition.runoffDirt, definition.runoffFoam, definition.runoffWater * water, definition.runoffIsDry);
            return rug.ApplyOperation(aim.Point, Radius, definition.falloffHardness, definition.spreadAspect, across, runoff, ref operation);
        }

        /// <summary>
        /// Prélève le produit spécialisé actif pour cette frame (pulvérisateurs) : retourne son identifiant dans la table
        /// du tapis visé (0 = détergent standard) et le multiplicateur de résidus correspondant.
        /// </summary>
        protected byte ConsumeActiveProduct(RugCleaningController rug, float deltaTime, out float residueFactor)
        {
            residueFactor = 1f;
            var supply = Resources?.Products;
            var product = supply?.Active;
            if (product == null || rug.Products == null) return ProductTable.Standard;
            byte id = rug.Products.IdOf(product);
            if (id == ProductTable.Standard || supply.Consume(product.litersPerSecond * deltaTime) <= 0f) return ProductTable.Standard;
            residueFactor = rug.Products.ResidueFactor(id);
            return id;
        }

        /// <summary>Libellé de la touche d'une action pour le périphérique actif (ou celui donné par défaut).</summary>
        protected string KeyLabel(string actionName, string fallback) => Resources?.Prompts?.Label(actionName) ?? fallback;

        /// <summary>Ligne d'état des pulvérisateurs : réservoir de détergent et produit ajouté.</summary>
        protected string ProductStatusLine()
        {
            string tank = GetBaseStatusLine();
            var active = Resources?.Products?.Active;
            string key = KeyLabel("Product", "T");
            if (active == null) return $"{tank} · produit : standard ({key})";
            return $"{tank} · + {active.displayName} {Resources.Products.StockOf(active):0.0} L ({key})";
        }

        string GetBaseStatusLine()
        {
            var tank = ResourceTank;
            return tank == null ? string.Empty : $"{tank.DisplayName} : {tank.Amount:0.0} / {tank.Capacity:0} {tank.Unit}";
        }

        /// <summary>Rapport largeur / profondeur de la zone traitée (1 = disque).</summary>
        public float SpreadAspect => definition != null ? Mathf.Max(1f, definition.spreadAspect) : 1f;

        /// <summary>Grand axe de la zone traitée : dans le plan de la surface, en travers de la visée.</summary>
        public static Vector3 SpreadAxis(in ToolAim aim)
        {
            Vector3 across = Vector3.Cross(aim.Normal, aim.Ray.direction);
            return across.sqrMagnitude < 1e-6f ? Vector3.right : across.normalized;
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
            emission.rateOverTimeMultiplier = EffectBudget.Rate(rate);
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
