using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RugWash.Cleaning;
using UnityEngine;

namespace RugWash.Orders
{
    /// <summary>Outils achetés qui élargissent les objectifs possibles d'une mission générée (aucun objectif impossible).</summary>
    [Flags]
    public enum MissionCapabilities
    {
        None = 0,
        Vacuum = 1,
        Broom = 2,
        Rinse = 4,
        /// <summary>Extracteur : seul outil qui sèche vraiment un tapis (objectif d'humidité maximale).</summary>
        Extractor = 8,
        Foam = 16,
        All = Vacuum | Broom | Rinse | Extractor | Foam,
    }

    /// <summary>Difficulté des missions générées (grande mise à jour), choisie dans les paramètres : celle de l'hôte en ligne.</summary>
    public enum MissionDifficulty
    {
        /// <summary>Tapis moins sales, objectif plus bas, chronomètre plus large ; paiement réduit.</summary>
        Relaxed = 0,
        Normal = 1,
        /// <summary>Tapis plus sales, objectif plus haut, chronomètre serré ; paiement majoré.</summary>
        Demanding = 2,
    }

    /// <summary>
    /// Clé d'une mission générée : elle suffit à la reproduire à l'identique (sauvegarde, invités en ligne). Son
    /// identifiant texte « gen.graine.numéro.palier.outils.variante » (suivi de « .difficulté » hors difficulté normale) sert
    /// d'identifiant de commande ; les anciens identifiants restent lisibles (difficulté normale).
    /// </summary>
    public readonly struct MissionKey : IEquatable<MissionKey>
    {
        public const string Prefix = "gen.";
        public const int MaxVariant = 8;

        public readonly int Seed;
        public readonly int Sequence;
        public readonly int Tier;
        public readonly int Capabilities;
        public readonly int Variant;
        public readonly int Difficulty;

        public MissionKey(int seed, int sequence, int tier, int capabilities, int variant, int difficulty = (int)MissionDifficulty.Normal)
        {
            Seed = seed;
            Sequence = sequence;
            Tier = tier;
            Capabilities = capabilities;
            Variant = variant;
            Difficulty = Mathf.Clamp(difficulty, 0, 2);
        }

        public string Id => Difficulty == (int)MissionDifficulty.Normal
            ? string.Format(CultureInfo.InvariantCulture, "{0}{1}.{2}.{3}.{4}.{5}", Prefix, Seed, Sequence, Tier, Capabilities, Variant)
            : string.Format(CultureInfo.InvariantCulture, "{0}{1}.{2}.{3}.{4}.{5}.{6}", Prefix, Seed, Sequence, Tier, Capabilities, Variant, Difficulty);

        public static bool IsGenerated(string id) => id != null && id.StartsWith(Prefix, StringComparison.Ordinal);

        /// <summary>Lecture bornée d'un identifiant (sauvegarde éditée, message réseau) : faux s'il est invalide.</summary>
        public static bool TryParse(string id, out MissionKey key)
        {
            key = default;
            if (!IsGenerated(id) || id.Length > 64) return false;
            var parts = id.Substring(Prefix.Length).Split('.');
            if (parts.Length != 5 && parts.Length != 6) return false;
            var values = new int[6];
            values[5] = (int)MissionDifficulty.Normal;
            for (int i = 0; i < parts.Length; i++)
                if (!int.TryParse(parts[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out values[i])) return false;
            if (values[1] < 0 || values[2] < 0 || values[2] > MissionRules.MaxTier) return false;
            if (values[3] < 0 || values[3] > (int)MissionCapabilities.All || values[4] < 0 || values[4] >= MaxVariant) return false;
            // « .1 » explicite : même mission que l'identifiant court (un seul identifiant par mission).
            if (values[5] < 0 || values[5] > 2 || (parts.Length == 6 && values[5] == (int)MissionDifficulty.Normal)) return false;
            key = new MissionKey(values[0], values[1], values[2], values[3], values[4], values[5]);
            return true;
        }

        public bool Equals(MissionKey other) => Seed == other.Seed && Sequence == other.Sequence && Tier == other.Tier
                                                 && Capabilities == other.Capabilities && Variant == other.Variant && Difficulty == other.Difficulty;

        public override bool Equals(object obj) => obj is MissionKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Seed, Sequence, Tier, Capabilities, Variant, Difficulty);
        public override string ToString() => Id;
    }

    /// <summary>Paliers de réputation des missions générées (règles pures, testées en EditMode).</summary>
    public static class MissionRules
    {
        public const int MaxTier = 30;

        /// <summary>Réputation qui ouvre un palier : 0, 3, 8, 15, 24, 35… (une à deux missions réussies de plus par palier).</summary>
        public static int Threshold(int tier)
        {
            tier = Mathf.Clamp(tier, 0, MaxTier);
            return tier * (tier + 2);
        }

        /// <summary>Palier le plus élevé ouvert par cette réputation.</summary>
        public static int TierFor(int reputation)
        {
            int tier = 0;
            while (tier < MaxTier && Threshold(tier + 1) <= reputation) tier++;
            return tier;
        }

        /// <summary>Outils débloqués (identifiants des outils achetables) → capacités utiles à la génération.</summary>
        public static int CapabilitiesFrom(Func<string, bool> isUnlocked)
        {
            if (isUnlocked == null) return 0;
            var caps = MissionCapabilities.None;
            if (isUnlocked("vacuum")) caps |= MissionCapabilities.Vacuum;
            if (isUnlocked("broom")) caps |= MissionCapabilities.Broom;
            if (isUnlocked("rinse")) caps |= MissionCapabilities.Rinse;
            if (isUnlocked("extractor")) caps |= MissionCapabilities.Extractor;
            if (isUnlocked("foam_cannon")) caps |= MissionCapabilities.Foam;
            return (int)caps;
        }
    }

    /// <summary>
    /// Génération déterministe des missions : la même clé et les mêmes tapis donnent toujours la même commande (client,
    /// tapis, salissures, objectifs, récompense). Les salissures, les tapis et les objectifs réutilisent les systèmes
    /// existants ; un objectif d'humidité n'est proposé qu'avec l'extracteur, un objectif d'usure qu'avec un tapis fragile.
    /// </summary>
    public static class MissionGenerator
    {
        readonly struct Layer
        {
            public readonly DirtType Type;
            public readonly DirtPattern Pattern;
            public readonly float Coverage, Intensity, Scale, Adhesion;

            public Layer(DirtType type, DirtPattern pattern, float coverage, float intensity, float scale, float adhesion = 0f)
            {
                Type = type;
                Pattern = pattern;
                Coverage = coverage;
                Intensity = intensity;
                Scale = scale;
                Adhesion = adhesion;
            }
        }

        sealed class Theme
        {
            public string Category;
            public int MinTier;
            public string Description;
            public string[] Clients;
            public string[] Titles;
            public Layer[] Layers;
        }

        static readonly Theme[] Themes =
        {
            new Theme
            {
                Category = "Particulier", MinTier = 0,
                Description = "Un tapis du quotidien, poussiéreux et marqué par le passage.",
                Clients = new[] { "Mme Dubois", "M. Garnier", "Famille Petit", "Mme Leroy", "M. Fontaine" },
                Titles = new[] { "Tapis du salon", "Tapis de la pièce à vivre", "Tapis devant le canapé" },
                Layers = new[]
                {
                    new Layer(DirtType.Dust, DirtPattern.Uniform, 0.7f, 0.4f, 0.3f),
                    new Layer(DirtType.Soil, DirtPattern.TrafficLane, 0.4f, 0.6f, 0.25f),
                    new Layer(DirtType.Drink, DirtPattern.Spots, 0.12f, 0.8f, 0.12f),
                },
            },
            new Theme
            {
                Category = "Commerce", MinTier = 0,
                Description = "Boue et terre ramenées de l'extérieur, à chaque passage de client.",
                Clients = new[] { "Boulangerie Au Bon Pain", "Cabinet Morel", "Hôtel des Tilleuls", "Librairie La Page" },
                Titles = new[] { "Tapis d'entrée", "Tapis de l'accueil", "Tapis du hall" },
                Layers = new[]
                {
                    new Layer(DirtType.Dust, DirtPattern.Uniform, 0.6f, 0.4f, 0.3f),
                    new Layer(DirtType.Mud, DirtPattern.Blotches, 0.35f, 0.8f, 0.3f),
                    new Layer(DirtType.Soil, DirtPattern.Edges, 0.45f, 0.7f, 0.2f),
                },
            },
            new Theme
            {
                Category = "Animaux", MinTier = 0,
                Description = "Des poils d'animaux partout et des amas de poussière dans les coins.",
                Clients = new[] { "Pension Les Truffes", "Mme Caron", "Toilettage Poil Doux", "M. Lambert" },
                Titles = new[] { "Tapis de la salle de jeux", "Tapis du panier", "Tapis du séjour" },
                Layers = new[]
                {
                    new Layer(DirtType.PetHair, DirtPattern.Uniform, 0.75f, 0.55f, 0.25f),
                    new Layer(DirtType.DustClumps, DirtPattern.Edges, 0.55f, 0.6f, 0.25f),
                    new Layer(DirtType.Drink, DirtPattern.Spots, 0.12f, 0.85f, 0.1f),
                },
            },
            new Theme
            {
                Category = "Restauration", MinTier = 1,
                Description = "Graisse et boissons renversées : un travail au détergent.",
                Clients = new[] { "Restaurant Le Saule", "Brasserie du Marché", "Pizzeria Bella", "Crêperie Ty Gwenn" },
                Titles = new[] { "Tapis de la salle à manger", "Tapis sous les tables", "Tapis de l'entrée du restaurant" },
                Layers = new[]
                {
                    new Layer(DirtType.Soil, DirtPattern.TrafficLane, 0.45f, 0.75f, 0.25f),
                    new Layer(DirtType.Grease, DirtPattern.Spots, 0.3f, 0.85f, 0.13f),
                    new Layer(DirtType.Drink, DirtPattern.Spots, 0.22f, 0.9f, 0.12f),
                    new Layer(DirtType.Dust, DirtPattern.Uniform, 0.5f, 0.4f, 0.3f),
                },
            },
            new Theme
            {
                Category = "Atelier", MinTier = 1,
                Description = "Peluches, fibres et taches d'huile bien accrochées.",
                Clients = new[] { "Atelier Couture Lemaire", "Menuiserie Bois Doré", "Imprimerie Lavigne", "Garage du Pont" },
                Titles = new[] { "Tapis de l'atelier", "Tapis du poste de travail", "Tapis du bureau d'accueil" },
                Layers = new[]
                {
                    new Layer(DirtType.Lint, DirtPattern.Uniform, 0.75f, 0.6f, 0.2f),
                    new Layer(DirtType.HairDebris, DirtPattern.Blotches, 0.4f, 0.5f, 0.3f),
                    new Layer(DirtType.Grease, DirtPattern.Spots, 0.22f, 0.85f, 0.1f, 0.2f),
                },
            },
            new Theme
            {
                Category = "Chantier", MinTier = 2,
                Description = "Boue séchée en croûtes, terre et poussière épaisse.",
                Clients = new[] { "Chantier Bâtir & Fils", "Paysagiste Vert Tendre", "Bureau d'études Arcade" },
                Titles = new[] { "Tapis du bureau de chantier", "Tapis du vestiaire", "Tapis de la cabane de chantier" },
                Layers = new[]
                {
                    new Layer(DirtType.DriedMud, DirtPattern.Blotches, 0.4f, 0.9f, 0.3f, 0.2f),
                    new Layer(DirtType.DustClumps, DirtPattern.Uniform, 0.55f, 0.5f, 0.3f),
                    new Layer(DirtType.Soil, DirtPattern.TrafficLane, 0.5f, 0.7f, 0.25f),
                    new Layer(DirtType.Dust, DirtPattern.Uniform, 0.6f, 0.4f, 0.3f),
                },
            },
            // Grande mise à jour : tapis très sales, presque noirs, aux paliers élevés.
            new Theme
            {
                Category = "Sinistre", MinTier = 4,
                Description = "Remonté d'une cave inondée : boue partout, croûtes et crasse noire. Le tapis est presque méconnaissable.",
                Clients = new[] { "Assurances Mutuelle Ouest", "M. Perrin", "Résidence Les Peupliers", "Mme Benali" },
                Titles = new[] { "Tapis de la cave inondée", "Tapis du sous-sol", "Tapis sinistré" },
                Layers = new[]
                {
                    new Layer(DirtType.Mud, DirtPattern.Uniform, 0.85f, 0.92f, 0.35f),
                    new Layer(DirtType.Grime, DirtPattern.Blotches, 0.55f, 0.9f, 0.35f, 0.25f),
                    new Layer(DirtType.DriedMud, DirtPattern.Edges, 0.5f, 0.9f, 0.25f, 0.2f),
                    new Layer(DirtType.Soil, DirtPattern.Uniform, 0.6f, 0.75f, 0.3f),
                },
            },
            new Theme
            {
                Category = "Grand nettoyage", MinTier = 6,
                Description = "Des années sans entretien : une couche de crasse noire recouvre tout le motif.",
                Clients = new[] { "Succession Moreau", "Brocante du Vieux Port", "Château de Saint-Clair", "Bar Le Pirate" },
                Titles = new[] { "Tapis oublié au grenier", "Tapis noir de crasse", "Tapis de l'ancien bar" },
                Layers = new[]
                {
                    new Layer(DirtType.Grime, DirtPattern.Uniform, 0.9f, 0.95f, 0.35f, 0.3f),
                    new Layer(DirtType.DustClumps, DirtPattern.Edges, 0.45f, 0.5f, 0.25f),
                    new Layer(DirtType.Grease, DirtPattern.Spots, 0.3f, 0.9f, 0.14f, 0.25f),
                    new Layer(DirtType.Embedded, DirtPattern.Spots, 0.2f, 0.85f, 0.1f),
                },
            },
            new Theme
            {
                Category = "Prestige", MinTier = 3,
                Description = "Un tapis de valeur : propreté irréprochable attendue.",
                Clients = new[] { "Galerie Lumen", "Résidence Valmont", "Hôtel Belvédère", "Maison Delacroix" },
                Titles = new[] { "Pièce de collection", "Tapis du grand salon", "Tapis d'exposition" },
                Layers = new[]
                {
                    new Layer(DirtType.Dust, DirtPattern.Uniform, 0.7f, 0.4f, 0.3f),
                    new Layer(DirtType.Embedded, DirtPattern.Spots, 0.15f, 0.8f, 0.08f),
                    new Layer(DirtType.Drink, DirtPattern.Spots, 0.2f, 0.85f, 0.12f, 0.25f),
                    new Layer(DirtType.Lint, DirtPattern.Blotches, 0.3f, 0.35f, 0.3f),
                },
            },
        };

        /// <summary>Tapis utilisables par les missions : ceux des contrats du catalogue, sans doublon, dans l'ordre du catalogue.</summary>
        public static List<RugDefinition> RugsFrom(IEnumerable<OrderDefinition> catalog)
        {
            var rugs = new List<RugDefinition>();
            if (catalog == null) return rugs;
            foreach (var order in catalog)
                if (order != null && order.rug != null && !rugs.Contains(order.rug)) rugs.Add(order.rug);
            return rugs;
        }

        /// <summary>Palier à partir duquel un tapis peut être confié (soie fragile : clients confirmés seulement).</summary>
        public static int RugMinTier(RugDefinition rug)
        {
            if (rug == null) return int.MaxValue;
            if (rug.pileType == RugPileType.Silk || rug.fragility > 0.5f) return 3;
            return rug.pileType == RugPileType.Oriental ? 1 : 0;
        }

        /// <summary>
        /// Progression (grande mise à jour) : les premières missions confient les plus petits tapis ; les plus grands arrivent
        /// avec les paliers (rang de surface : les deux plus petits dès le départ, le suivant au palier 1, les autres au
        /// palier 2 ; au-delà de 7 m², palier 4), en plus de la règle de matière (<see cref="RugMinTier(RugDefinition)"/>).
        /// </summary>
        public static int RugMinTier(RugDefinition rug, IReadOnlyList<RugDefinition> rugs)
        {
            int byMaterial = RugMinTier(rug);
            if (rug == null || rugs == null) return byMaterial;
            float area = rug.sizeMeters.x * rug.sizeMeters.y;
            int smaller = 0;
            foreach (var other in rugs)
                if (other != null && other != rug && other.sizeMeters.x * other.sizeMeters.y < area - 1e-4f) smaller++;
            int bySize = smaller <= 1 ? 0 : smaller == 2 ? 1 : 2;
            if (area > 7.5f) bySize = Mathf.Max(bySize, 4);
            return Mathf.Max(byMaterial, bySize);
        }

        /// <summary>Saleté (étendue, intensité), objectif, temps de bonus et paiement selon la difficulté choisie.</summary>
        public readonly struct DifficultyScale
        {
            public readonly float Coverage, Intensity, Target, BonusTime, Reward;

            public DifficultyScale(float coverage, float intensity, float target, float bonusTime, float reward)
            {
                Coverage = coverage;
                Intensity = intensity;
                Target = target;
                BonusTime = bonusTime;
                Reward = reward;
            }
        }

        public static DifficultyScale ScaleFor(int difficulty)
        {
            switch ((MissionDifficulty)Mathf.Clamp(difficulty, 0, 2))
            {
                case MissionDifficulty.Relaxed: return new DifficultyScale(0.85f, 0.85f, -0.05f, 1.3f, 0.85f);
                case MissionDifficulty.Demanding: return new DifficultyScale(1.1f, 1.12f, 0.04f, 0.8f, 1.3f);
                default: return new DifficultyScale(1f, 1f, 0f, 1f, 1f);
            }
        }

        public static string DifficultyLabel(int difficulty)
        {
            switch ((MissionDifficulty)Mathf.Clamp(difficulty, 0, 2))
            {
                case MissionDifficulty.Relaxed: return "Détendu";
                case MissionDifficulty.Demanding: return "Exigeant";
                default: return "Normal";
            }
        }

        /// <summary>Signature (catégorie + tapis) servant à éviter deux missions identiques coup sur coup.</summary>
        public static string Signature(MissionKey key, IReadOnlyList<RugDefinition> rugs)
        {
            var rng = new Rng(key);
            int theme = PickTheme(ref rng, key.Tier);
            var rug = PickRug(ref rng, key.Tier, rugs);
            return rug == null ? null : $"{theme}:{rug.id}";
        }

        /// <summary>Crée la commande décrite par la clé (null sans tapis disponible). Instance d'exécution, jamais sauvegardée.</summary>
        public static OrderDefinition Create(MissionKey key, IReadOnlyList<RugDefinition> rugs)
        {
            var rng = new Rng(key);
            int themeIndex = PickTheme(ref rng, key.Tier);
            var rug = PickRug(ref rng, key.Tier, rugs);
            if (rug == null) return null;
            var theme = Themes[themeIndex];
            int scale = Mathf.Min(key.Tier, 10);
            var difficulty = ScaleFor(key.Difficulty);
            // Premières missions (palier 0) : petits tapis peu sales, objectif modeste ; la saleté grandit avec les paliers.
            float light = key.Tier == 0 ? 0.7f : key.Tier == 1 ? 0.85f : 1f;

            var order = ScriptableObject.CreateInstance<OrderDefinition>();
            order.hideFlags = HideFlags.DontSave;
            order.id = key.Id;
            order.name = key.Id;
            order.rug = rug;
            order.clientName = theme.Clients[rng.Range(theme.Clients.Length)];
            order.title = theme.Titles[rng.Range(theme.Titles.Length)];
            order.dirtSeed = 1000 + (int)(rng.Next() % 900000u);

            // Salissures : celles de la catégorie, plus nombreuses, étendues et incrustées aux paliers élevés.
            int layerCount = Mathf.Min(theme.Layers.Length, 2 + (key.Tier >= 1 ? 1 : 0) + (key.Tier >= 3 ? 1 : 0));
            order.dirt = new List<DirtLayer>(layerCount);
            for (int i = 0; i < layerCount; i++)
            {
                var layer = theme.Layers[i];
                float coverage = Mathf.Clamp(layer.Coverage * (0.85f + 0.3f * rng.Value()) * (1f + 0.05f * scale) * light * difficulty.Coverage, 0.05f, 0.97f);
                float intensity = Mathf.Clamp((layer.Intensity + 0.025f * scale) * Mathf.Lerp(1f, light, 0.6f) * difficulty.Intensity, 0.1f, 0.99f);
                float adhesion = DirtTypes.IsLooseDry(layer.Type) ? layer.Adhesion : Mathf.Min(0.5f, layer.Adhesion + 0.03f * scale);
                order.dirt.Add(new DirtLayer(layer.Type, layer.Pattern, coverage, intensity, layer.Scale, adhesion));
            }

            // Objectifs atteignables avec l'équipement du joueur au moment de la génération.
            var objectives = new OrderObjectives
            {
                // Difficulté normale : bornes d'avant (70 à 95 %) ; détendu descend à 65 %, exigeant monte à 97 %.
                targetCleanliness = Mathf.Clamp(0.72f + 0.03f * scale + 0.04f * (rng.Value() - 0.5f) + difficulty.Target,
                    difficulty.Target < 0f ? 0.65f : 0.7f, 0.95f + Mathf.Max(0f, difficulty.Target) * 0.5f),
            };
            int constraints = 0;
            if (key.Tier >= 1 && rng.Value() < 0.3f + 0.1f * scale)
            {
                objectives.limitResidue = true;
                objectives.maxResidue = Mathf.Clamp(0.26f - 0.02f * scale, 0.08f, 0.26f);
                constraints++;
            }
            bool extractor = (key.Capabilities & (int)MissionCapabilities.Extractor) != 0;
            if (extractor && key.Tier >= 1 && rng.Value() < 0.35f + 0.05f * scale)
            {
                objectives.limitMoisture = true;
                objectives.maxMoisture = Mathf.Clamp(0.42f - 0.02f * scale, 0.2f, 0.42f);
                constraints++;
            }
            if (rug.fragility > 0f)
            {
                objectives.limitWear = true;
                objectives.maxWear = Mathf.Clamp(0.06f - 0.004f * scale, 0.035f, 0.06f);
                constraints++;
            }
            // Plus le tapis est grand, plus il faut de temps ; la difficulté resserre ou élargit le chronomètre du bonus.
            float area = rug.sizeMeters.x * rug.sizeMeters.y;
            objectives.bonusTimeSeconds = Mathf.Round((240f + 75f * layerCount + 45f * scale + 30f * Mathf.Max(0f, area - 3f)) * difficulty.BonusTime / 30f) * 30f;
            order.objectives = objectives;

            order.difficulty = Mathf.Clamp(1 + (scale >= 1 ? 1 : 0) + (scale >= 3 ? 1 : 0) + (scale >= 6 ? 1 : 0) + (constraints >= 2 ? 1 : 0), 1, 4);
            float reward = 85f + 38f * scale + 22f * layerCount + 12f * Mathf.Max(0, key.Tier - 10)
                           + (objectives.limitResidue ? 30f : 0f) + (objectives.limitMoisture ? 40f : 0f) + (objectives.limitWear ? 70f : 0f)
                           + RugBonus(rug) + 35f * Mathf.Max(0f, area - 3f) + (theme.MinTier >= 4 ? 120f : 0f);
            if (key.Tier == 0) reward *= 0.8f;
            order.baseReward = Mathf.RoundToInt(reward * difficulty.Reward * (0.92f + 0.16f * rng.Value()) / 5f) * 5;
            order.requiredReputation = MissionRules.Threshold(key.Tier);
            order.description = Describe(theme, objectives, key);
            if (key.Difficulty != (int)MissionDifficulty.Normal) order.description += $" — difficulté : {DifficultyLabel(key.Difficulty).ToLowerInvariant()}";
            return order;
        }

        static float RugBonus(RugDefinition rug)
        {
            switch (rug.pileType)
            {
                case RugPileType.Silk: return 200f;
                case RugPileType.Oriental: return 60f;
                case RugPileType.LongPile: return 25f;
                default: return 0f;
            }
        }

        static string Describe(Theme theme, OrderObjectives objectives, MissionKey key)
        {
            var text = new StringBuilder(theme.Category).Append(" — ").Append(theme.Description);
            if (objectives.limitResidue) text.Append(" Aucune trace de produit tolérée.");
            if (objectives.limitMoisture) text.Append(" À rendre sec.");
            if (objectives.limitWear) text.Append(" Fibres fragiles.");
            return text.Append(" (mission n° ").Append(key.Sequence + 1).Append(')').ToString();
        }

        static int PickTheme(ref Rng rng, int tier)
        {
            int count = 0;
            foreach (var theme in Themes)
                if (theme.MinTier <= tier) count++;
            int pick = rng.Range(count);
            for (int i = 0; i < Themes.Length; i++)
                if (Themes[i].MinTier <= tier && pick-- == 0) return i;
            return 0;
        }

        static RugDefinition PickRug(ref Rng rng, int tier, IReadOnlyList<RugDefinition> rugs)
        {
            if (rugs == null) return null;
            int count = 0;
            foreach (var rug in rugs)
                if (RugMinTier(rug, rugs) <= tier) count++;
            if (count == 0) return null;
            int pick = rng.Range(count);
            foreach (var rug in rugs)
                if (RugMinTier(rug, rugs) <= tier && pick-- == 0) return rug;
            return null;
        }

        /// <summary>Générateur pseudo-aléatoire local (xorshift) : indépendant de UnityEngine.Random et identique partout.</summary>
        struct Rng
        {
            uint state;

            public Rng(MissionKey key)
            {
                uint hash = 2166136261u;
                foreach (int value in new[] { key.Seed, key.Sequence, key.Tier, key.Capabilities, key.Variant })
                {
                    hash ^= (uint)value;
                    hash *= 16777619u;
                    hash ^= hash >> 13;
                }
                state = hash == 0 ? 0x9E3779B9u : hash;
                for (int i = 0; i < 4; i++) Next();
            }

            public uint Next()
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return state;
            }

            public float Value() => (Next() & 0xFFFFFF) / 16777216f;

            public int Range(int count) => count <= 0 ? 0 : (int)(Next() % (uint)count);
        }
    }
}
