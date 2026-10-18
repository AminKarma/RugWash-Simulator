using System;
using RugWash.Cleaning;
using UnityEngine;

namespace RugWash.Pranks
{
    /// <summary>Gadgets de farce entre coéquipiers (v0.1.2). Valeurs figées : elles passent par le réseau.</summary>
    public enum PrankGadget : byte
    {
        None = 0,
        /// <summary>Pipi : tache jaune humide sur un tapis, ou gouttes sur l'écran d'un coéquipier.</summary>
        Pee = 1,
        /// <summary>Batte de baseball : petite tache de faux sang sur un tapis, ou chute comique d'un coéquipier.</summary>
        Bat = 2,
        /// <summary>Fusil à pompe de farces et attrapes : éclaboussures de faux sang de théâtre sur un tapis (jamais sur un joueur).</summary>
        Blaster = 3,
    }

    /// <summary>Cible d'une farce (valeurs figées : réseau).</summary>
    public enum PrankTarget : byte
    {
        Rug = 0,
        Player = 1,
    }

    /// <summary>
    /// Réglages centralisés des farces : portée, recharge, coût par usage, taches, effets sur les joueurs et garde-fous.
    /// Les prix d'achat sont ceux des améliorations « gadget de farce » du catalogue (onglet Farces de la boutique).
    /// </summary>
    [CreateAssetMenu(menuName = "RugWash/Réglages des farces", fileName = "PrankTuning")]
    public sealed class PrankTuning : ScriptableObject
    {
        [Serializable]
        public sealed class Gadget
        {
            public PrankGadget kind = PrankGadget.Pee;
            public string displayName = "Gadget";
            [Tooltip("Amélioration (onglet Farces) qui débloque ce gadget.")]
            public string upgradeId;
            [Tooltip("Portée maximale (m) entre les yeux du joueur et la cible.")]
            [Min(0.5f)] public float range = 2.5f;
            [Tooltip("Délai avant de pouvoir réutiliser ce gadget (s).")]
            [Min(1f)] public float cooldownSeconds = 20f;
            [Tooltip("Coût de chaque utilisation, débité sur la carrière de l'utilisateur (0 = gratuit).")]
            [Min(0)] public int costPerUse;
            public bool affectsRug = true;
            public bool affectsPlayers;
            [Header("Tache sur le tapis")]
            public DirtType stainType = DirtType.Urine;
            [Range(0.02f, 0.6f)] public float stainRadius = 0.2f;
            [Range(0f, 1f)] public float stainAmount = 0.5f;
            [Tooltip("Humidité laissée (pipi : mouillé).")]
            [Range(0f, 1f)] public float stainMoisture;
            [Tooltip("Nombre de taches (éclaboussures du fusil).")]
            [Range(1, 16)] public int splats = 1;
            [Tooltip("Dispersion des taches autour du point visé (m).")]
            [Range(0f, 1f)] public float splatSpread;
            [Tooltip("Durée de l'animation (jet, coup, tir).")]
            [Range(0.1f, 3f)] public float useSeconds = 0.4f;
        }

        public Gadget pee = new Gadget
        {
            kind = PrankGadget.Pee, displayName = "Pipi", upgradeId = "up_prank_pee", range = 2.4f, cooldownSeconds = 45f,
            affectsPlayers = true, stainType = DirtType.Urine, stainRadius = 0.26f, stainAmount = 0.55f, stainMoisture = 0.7f, useSeconds = 1.2f,
        };
        public Gadget bat = new Gadget
        {
            kind = PrankGadget.Bat, displayName = "Batte de baseball", upgradeId = "up_prank_bat", range = 2.2f, cooldownSeconds = 20f,
            affectsPlayers = true, stainType = DirtType.FakeBlood, stainRadius = 0.09f, stainAmount = 0.6f, useSeconds = 0.4f,
        };
        public Gadget blaster = new Gadget
        {
            kind = PrankGadget.Blaster, displayName = "Fusil à faux sang", upgradeId = "up_prank_blaster", range = 4.5f, cooldownSeconds = 12f,
            costPerUse = 60, affectsPlayers = true, stainType = DirtType.FakeBlood, stainRadius = 0.07f, stainAmount = 0.6f, splats = 9,
            splatSpread = 0.35f, useSeconds = 0.5f,
        };

        /// <summary>
        /// Version des valeurs par défaut (migration d'un asset existant vers les valeurs de la mission de corrections).
        /// Initialisée à 1 : un asset enregistré avant l'apparition de ce champ le relit à 1 et sera migré ; un asset neuf
        /// porte déjà les nouvelles valeurs (la migration ne change alors rien).
        /// </summary>
        public int defaultsVersion = 1;
        public const int CurrentDefaults = 2;

        [Header("Fusil sur un joueur : KO cartoon")]
        [Tooltip("Durée du KO (s) : le joueur se relève ensuite automatiquement.")]
        [Range(1f, 6f)] public float blasterKnockSeconds = 5f;
        [Tooltip("Élan donné au corps (m/s), borné par la chute et le ragdoll.")]
        [Min(0f)] public float blasterImpulse = 9f;
        [Tooltip("Élan vertical ajouté (m/s) : le corps décolle un peu, effet exagéré.")]
        [Min(0f)] public float blasterLift = 3f;

        [Header("Pipi sur un coéquipier : gouttes à l'écran")]
        [Range(0.1f, 0.85f)] public float splashIntensity = 0.55f;
        [Min(1f)] public float splashSeconds = 6f;
        [Min(0.2f)] public float splashFadeSeconds = 2.5f;
        [Tooltip("Pendant ce délai après des gouttes, le même joueur ne peut pas en recevoir d'autres (s).")]
        [Min(0f)] public float splashProtectionSeconds = 20f;

        [Header("Batte : frappe (grande mise à jour)")]
        [Tooltip("Délai minimal entre deux coups de batte, dans le vide ou non (s) ; vérifié aussi par l'autorité.")]
        [Range(0.25f, 1.5f)] public float batSwingInterval = 0.45f;
        [Tooltip("Instant du coup dans le mouvement (part de sa durée) : la cible est cherchée à ce moment-là.")]
        [Range(0.1f, 0.8f)] public float batImpactFraction = 0.38f;

        [Header("Batte sur un coéquipier : chute comique")]
        [Tooltip("V0.2.5 : objets achetés portés que perd un joueur touché (en ligne) ; ils tombent au sol et se ramassent.")]
        [Range(0, 4)] public int batItemsLost = 1;
        [Range(0.5f, 3f)] public float knockSeconds = 1.4f;
        [Min(0f)] public float knockImpulse = 6f;
        [Tooltip("Après un coup, le même joueur est protégé pendant ce délai (s).")]
        [Min(0f)] public float knockProtectionSeconds = 15f;

        [Header("Garde-fous")]
        [Tooltip("Délai minimal entre deux farces d'un même joueur, tous gadgets confondus (s).")]
        [Min(0f)] public float globalCooldownSeconds = 3f;
        [Tooltip("Saleté que les farces peuvent ajouter à un tapis, en part de sa saleté initiale.")]
        [Range(0.01f, 1f)] public float rugCapFraction = 0.15f;
        [Tooltip("Plafond minimal (tapis presque propres) en quantité de saleté.")]
        [Min(0f)] public float rugCapMinimum = 6f;
        [Tooltip("Tache maximale d'une cellule après une farce : aucune zone ne devient impossible à nettoyer.")]
        [Range(0.1f, 1f)] public float cellStainCap = 0.85f;
        [Tooltip("Tolérance de portée côté hôte (latence de la position des joueurs, m).")]
        [Min(0f)] public float rangeTolerance = 0.75f;
        [Tooltip("Rayon autour d'une silhouette pour la viser (m).")]
        [Range(0.2f, 1f)] public float playerHitRadius = 0.45f;

        public Gadget Get(PrankGadget kind)
        {
            switch (kind)
            {
                case PrankGadget.Pee: return pee;
                case PrankGadget.Bat: return bat;
                case PrankGadget.Blaster: return blaster;
                default: return null;
            }
        }

        public static readonly PrankGadget[] All = { PrankGadget.Pee, PrankGadget.Bat, PrankGadget.Blaster };

        /// <summary>
        /// Asset créé avant la mission de corrections : fusil sur les joueurs (KO de 5 s), chutes plus exagérées. Les valeurs
        /// modifiées à la main depuis restent celles de l'utilisateur (seules les anciennes valeurs par défaut changent).
        /// Vrai si l'asset a été modifié.
        /// </summary>
        public bool UpgradeDefaults()
        {
            if (defaultsVersion >= CurrentDefaults) return false;
            blaster.affectsPlayers = true;
            if (Mathf.Approximately(knockImpulse, 3.5f)) knockImpulse = 6f;
            defaultsVersion = CurrentDefaults;
            return true;
        }

        /// <summary>
        /// Emplacement transmis par la silhouette pour un gadget en main (après les onze outils) : 12 batte, 13 fusil,
        /// 14 pipi (main libre, sans modèle). 0 : aucun gadget.
        /// </summary>
        public static byte AvatarSlot(PrankGadget gadget)
        {
            switch (gadget)
            {
                case PrankGadget.Bat: return 12;
                case PrankGadget.Blaster: return 13;
                case PrankGadget.Pee: return 14;
                default: return 0;
            }
        }
    }
}
