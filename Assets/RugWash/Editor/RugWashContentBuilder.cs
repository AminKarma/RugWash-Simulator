using System.Collections.Generic;
using RugWash.Cleaning;
using RugWash.Economy;
using RugWash.Orders;
using RugWash.RugDex;
using UnityEditor;
using UnityEngine;

namespace RugWash.Editor
{
    /// <summary>
    /// Crée les données de contenu (types de saleté, outils, tapis) et les matériaux.
    /// Création uniquement si absents : les réglages modifiés dans l'Inspector sont conservés.
    /// </summary>
    public static class RugWashContentBuilder
    {
        public const string DirtLibraryPath = RugWashPaths.DirtTypeDefinitions + "/DirtTypeLibrary.asset";
        public const string BrushToolPath = RugWashPaths.ToolDefinitions + "/Tool_Brush.asset";
        public const string RinseToolPath = RugWashPaths.ToolDefinitions + "/Tool_Rinse.asset";
        public const string VacuumToolPath = RugWashPaths.ToolDefinitions + "/Tool_Vacuum.asset";
        public const string SoapToolPath = RugWashPaths.ToolDefinitions + "/Tool_Soap.asset";
        public const string ExtractorToolPath = RugWashPaths.ToolDefinitions + "/Tool_Extractor.asset";
        public const string FoamCannonToolPath = RugWashPaths.ToolDefinitions + "/Tool_FoamCannon.asset";
        public const string BroomToolPath = RugWashPaths.ToolDefinitions + "/Tool_Broom.asset";
        public const string DetailBrushToolPath = RugWashPaths.ToolDefinitions + "/Tool_DetailBrush.asset";
        public const string BucketToolPath = RugWashPaths.ToolDefinitions + "/Tool_Bucket.asset";
        public const string SqueegeeToolPath = RugWashPaths.ToolDefinitions + "/Tool_Squeegee.asset";
        public const string RotaryToolPath = RugWashPaths.ToolDefinitions + "/Tool_Rotary.asset";
        public const string WorkSurfacePath = RugWashPaths.RugDefinitions + "/Surface_WorkSurface.asset";
        public const string FloorSurfacePath = RugWashPaths.RugDefinitions + "/Surface_Floor.asset";
        public const string TuningPath = RugWashPaths.Root + "/ScriptableObjects/CleaningTuning.asset";
        public const string OrientalRugPath = RugWashPaths.RugDefinitions + "/Rug_Oriental.asset";
        public const string ClassicRugPath = RugWashPaths.RugDefinitions + "/Rug_Classic.asset";
        public const string ShagRugPath = RugWashPaths.RugDefinitions + "/Rug_Shag.asset";
        public const string LuxuryRugPath = RugWashPaths.RugDefinitions + "/Rug_Luxury.asset";
        public const string ProductFolder = RugWashPaths.Root + "/ScriptableObjects/Products";
        public const string ProductLibraryPath = ProductFolder + "/ProductLibrary.asset";
        public const string RugCatalogPath = RugWashPaths.Root + "/ScriptableObjects/RugCatalog.asset";

        public sealed class Content
        {
            public DirtTypeLibrary DirtLibrary;
            public ToolDefinition Brush;
            public ToolDefinition Rinse;
            public ToolDefinition Vacuum, Soap, Extractor;
            public ToolDefinition FoamCannon, Broom, DetailBrush;
            /// <summary>Seau d'eau (kit de départ, v0.1.2).</summary>
            public ToolDefinition Bucket;
            /// <summary>v0.1.2 jalon 4 : raclette (aire de lavage) et monobrosse.</summary>
            public ToolDefinition Squeegee, Rotary;
            public RugDefinition Oriental, Classic, Shag, Luxury;
            public ProductLibrary ProductLibrary;
            public RugCatalog Catalog;
            /// <summary>Plan de travail de l'aire de lavage et sol de l'atelier (surfaces nettoyables).</summary>
            public RugDefinition WorkSurface, Floor;
            public CleaningTuning Tuning;
            /// <summary>Multijoueur : réglages et préfabriqués réseau.</summary>
            public RugWash.Network.NetworkContent Network;
            public readonly List<OrderDefinition> Orders = new List<OrderDefinition>();
            public readonly List<UpgradeDefinition> Upgrades = new List<UpgradeDefinition>();
            public readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

            public Material this[string name] => Materials[name];
        }

        [MenuItem("RugWash/Setup/Créer le Contenu Manquant (données, matériaux)", priority = 30)]
        static void EnsureContentMenu() => EnsureContent();

        public static Content EnsureContent()
        {
            RugWashProjectBootstrapper.EnsureFolders();
            RugWashTextureGenerator.GenerateAll(force: false);
            RugWashAudioGenerator.GenerateAll(force: false);

            var content = new Content
            {
                DirtLibrary = EnsureDirtLibrary(),
                Brush = EnsureTool(BrushToolPath, "brush", "Brosse", 3,
                    "Détache la saleté incrustée et la poussière. Plus efficace sur fibres humides ou savonneuses.",
                    range: 2.2f, radius: 0.13f, hardness: 3.5f, power: 0.6f, wetting: 0f, ToolResource.None, 0f),
                Rinse = EnsureTool(RinseToolPath, "rinse", "Lance de rinçage", 4,
                    "Évacue la saleté détachée et la mousse. Mouille les fibres. Consomme de l'eau.",
                    range: 4.5f, radius: 0.2f, hardness: 2f, power: 0.9f, wetting: 0.9f, ToolResource.Water, 1.5f),
                Vacuum = EnsureTool(VacuumToolPath, "vacuum", "Aspirateur", 1,
                    "Retire la poussière et les débris secs. Sans effet sur les taches incrustées. Aspire aussi la saleté détachée sur fibres sèches.",
                    range: 2.2f, radius: 0.17f, hardness: 4f, power: 0.8f, wetting: 0f, ToolResource.None, 0f),
                Soap = EnsureTool(SoapToolPath, "soap", "Détergent", 2,
                    "Dépose une mousse détergente : la brosse devient très efficace sur graisse, boissons et taches incrustées. À rincer ensuite.",
                    range: 3f, radius: 0.24f, hardness: 2f, power: 1.4f, wetting: 0.15f, ToolResource.Soap, 0.1f),
                Extractor = EnsureTool(ExtractorToolPath, "extractor", "Extracteur", 5,
                    "Aspire l'eau des fibres avec la saleté et le produit qu'elle contient. Remplit la cuve de récupération.",
                    range: 2.2f, radius: 0.16f, hardness: 5f, power: 0.9f, wetting: 0f, ToolResource.Recovery, 0f),
                FoamCannon = EnsureTool(FoamCannonToolPath, "foam_cannon", "Canon à mousse", 6,
                    "Projette en éventail une mousse épaisse sur une grande zone. Laissée en place, elle détache peu à peu graisse, " +
                    "boissons et boue ; à brosser puis rincer. Consomme du détergent (moins de résidus que le détergent pur).",
                    range: 3.5f, radius: 0.3f, hardness: 2.5f, power: 2.4f, wetting: 0.08f, ToolResource.Soap, 0.22f),
                Broom = EnsureTool(BroomToolPath, "broom", "Balai", 7,
                    "Chasse les poils et les débris d'un tapis sec : à passer avant le produit. Sur l'aire de lavage, pousse l'eau sale, " +
                    "la boue et la mousse vers le caniveau.",
                    range: 2.4f, radius: 0.26f, hardness: 3f, power: 0.9f, wetting: 0f, ToolResource.None, 0f),
                DetailBrush = EnsureTool(DetailBrushToolPath, "detail_brush", "Brosse de détail", 8,
                    "Petite brosse dure : bien plus efficace sur la graisse et les taches incrustées, sur une petite zone.",
                    range: 2.1f, radius: 0.07f, hardness: 4f, power: 0.9f, wetting: 0f, ToolResource.None, 0f),
                Bucket = EnsureTool(BucketToolPath, "bucket", "Seau d'eau", 9,
                    "Le rinçage du kit de départ : on verse l'eau de près pour évacuer la saleté détachée et la mousse. " +
                    "Moins puissant et moins précis que la lance haute pression. Se remplit au poste d'eau.",
                    range: 1.9f, radius: 0.3f, hardness: 1.6f, power: 0.5f, wetting: 1f, ToolResource.Water, 2f),
                Squeegee = EnsureTool(SqueegeeToolPath, "squeegee", "Raclette", 10,
                    "Lame en caoutchouc pour l'aire de lavage : poussez l'eau, la mousse et la boue dans le sens du geste jusqu'au " +
                    "caniveau, où elles sont évacuées. Sans effet sur un tapis.",
                    range: 2.4f, radius: 0.3f, hardness: 4f, power: 1f, wetting: 0f, ToolResource.None, 0f),
                Rotary = EnsureTool(RotaryToolPath, "rotary", "Monobrosse", 11,
                    "Machine à disque rotatif : détache la saleté sur une large zone et fait monter la mousse. Pleine efficacité sur " +
                    "fibres humides ou savonneuses, faible à sec (et use alors les fibres fragiles). Ne retire rien : rincez et " +
                    "extrayez ensuite. Lourde : on avance moins vite quand elle tourne.",
                    range: 2.2f, radius: 0.3f, hardness: 3f, power: 1.3f, wetting: 0f, ToolResource.None, 0f),
            };
            EnsureToolBehaviour(content.Squeegee, aspect: 4f, requiresUnlock: true);
            EnsureToolBehaviour(content.Rotary, requiresUnlock: true);
            EnsureToolRumble(content.Squeegee, 0.05f, 0.08f);
            EnsureToolRumble(content.Rotary, 0.35f, 0.25f);
            EnsureToolBehaviour(content.FoamCannon, aspect: 2.6f);
            EnsureToolBehaviour(content.Broom, aspect: 2.2f, runoffDirt: 0.9f, dry: true);
            EnsureToolBehaviour(content.DetailBrush, requiresUnlock: true);
            EnsureToolBehaviour(content.Rinse, runoffDirt: 0.6f, runoffFoam: 0.7f, runoffWater: 0.35f);
            EnsureToolBehaviour(content.Bucket, runoffDirt: 0.7f, runoffFoam: 0.8f, runoffWater: 0.5f);
            // v0.1.2 : kit de départ = brosse, détergent, seau d'eau. Les autres outils s'achètent (boutique de l'ordinateur).
            foreach (var paid in new[] { content.Vacuum, content.Rinse, content.Extractor, content.FoamCannon, content.Broom })
                EnsureToolBehaviour(paid, requiresUnlock: true);
            // Vibrations à la manette (jalon 4) : grondement (basse fréquence) et bourdonnement (haute fréquence).
            EnsureToolRumble(content.Vacuum, 0.08f, 0.16f);
            EnsureToolRumble(content.Soap, 0f, 0.06f);
            EnsureToolRumble(content.Brush, 0.18f, 0.1f);
            EnsureToolRumble(content.Rinse, 0.22f, 0.12f);
            EnsureToolRumble(content.Extractor, 0.15f, 0.2f);
            EnsureToolRumble(content.FoamCannon, 0.2f, 0.1f);
            EnsureToolRumble(content.Broom, 0.12f, 0.05f);
            EnsureToolRumble(content.DetailBrush, 0.12f, 0.18f);
            EnsureToolRumble(content.Bucket, 0.06f, 0.02f);
            content.Tuning = LoadOrCreate<CleaningTuning>(TuningPath, out _);

            EnsureMaterials(content);
            content.Oriental = EnsureRug(OrientalRugPath, "oriental", "Tapis oriental à médaillon", RugPileType.Oriental,
                new Vector2(3f, 2f), 0.014f, 64, RugWashTextureGenerator.OrientalAlbedo, RugWashTextureGenerator.FiberShortNormal,
                fiberTiles: 7f, fiberStrength: 0.9f, fringe: true, seed: 11,
                new DirtLayer(DirtType.Dust, DirtPattern.Uniform, 0.9f, 0.6f, 0.3f),
                new DirtLayer(DirtType.Soil, DirtPattern.TrafficLane, 0.55f, 0.85f, 0.25f),
                new DirtLayer(DirtType.Mud, DirtPattern.Blotches, 0.3f, 0.85f, 0.35f),
                new DirtLayer(DirtType.Drink, DirtPattern.Spots, 0.16f, 0.9f, 0.14f),
                new DirtLayer(DirtType.Grease, DirtPattern.Spots, 0.08f, 0.8f, 0.1f));
            content.Classic = EnsureRug(ClassicRugPath, "classic", "Tapis classique à poils courts", RugPileType.ShortPile,
                new Vector2(2.4f, 1.6f), 0.01f, 64, RugWashTextureGenerator.ClassicAlbedo, RugWashTextureGenerator.FiberShortNormal,
                fiberTiles: 9f, fiberStrength: 0.7f, fringe: false, seed: 23,
                new DirtLayer(DirtType.Dust, DirtPattern.Uniform, 0.7f, 0.4f, 0.3f),
                new DirtLayer(DirtType.HairDebris, DirtPattern.Blotches, 0.35f, 0.5f, 0.3f),
                new DirtLayer(DirtType.Soil, DirtPattern.Edges, 0.5f, 0.6f, 0.2f),
                new DirtLayer(DirtType.Drink, DirtPattern.Spots, 0.2f, 0.9f, 0.12f));
            content.Shag = EnsureRug(ShagRugPath, "shag", "Tapis à poils longs", RugPileType.LongPile,
                new Vector2(2.1f, 1.4f), 0.035f, 64, RugWashTextureGenerator.ShagAlbedo, RugWashTextureGenerator.FiberLongNormal,
                fiberTiles: 3.5f, fiberStrength: 1.4f, fringe: false, seed: 37,
                new DirtLayer(DirtType.Dust, DirtPattern.Uniform, 0.9f, 0.55f, 0.25f),
                new DirtLayer(DirtType.HairDebris, DirtPattern.Uniform, 0.5f, 0.35f, 0.2f),
                new DirtLayer(DirtType.Mud, DirtPattern.Blotches, 0.25f, 0.8f, 0.3f),
                new DirtLayer(DirtType.Embedded, DirtPattern.Spots, 0.1f, 0.7f, 0.08f));

            content.Luxury = EnsureRug(LuxuryRugPath, "luxury", "Tapis de soie de Hereke", RugPileType.Silk,
                new Vector2(3.2f, 2.2f), 0.009f, 64, RugWashTextureGenerator.LuxuryAlbedo, RugWashTextureGenerator.FiberShortNormal,
                fiberTiles: 12f, fiberStrength: 0.45f, fringe: true, seed: 51,
                new DirtLayer(DirtType.Dust, DirtPattern.Uniform, 0.7f, 0.3f, 0.3f),
                new DirtLayer(DirtType.Drink, DirtPattern.Spots, 0.15f, 0.8f, 0.1f),
                new DirtLayer(DirtType.Lint, DirtPattern.Blotches, 0.25f, 0.3f, 0.3f));
            EnsureRugMatter(content.Luxury, fragility: 0.85f, sheen: 0.4f, fringeColor: new Color(0.95f, 0.92f, 0.84f));
            EnsureRugDescriptions(content);
            content.ProductLibrary = EnsureProducts();
            content.Catalog = EnsureCatalog(content);

            content.WorkSurface = EnsureSurface(WorkSurfacePath, "work_surface", "Plan de travail de l'aire de lavage", SurfaceKind.WorkSurface,
                new Vector2(4.6f, 3.6f), 20, drying: 0.012f, fiberTiles: 0.7f, seed: 5);
            content.Floor = EnsureSurface(FloorSurfacePath, "floor", "Sol de l'atelier", SurfaceKind.Floor,
                new Vector2(RugWashSceneBuilder.RoomHalfX * 2f, RugWashSceneBuilder.RoomHalfZ * 2f), 16, drying: 0.02f, fiberTiles: 0.5f, seed: 9);
            // v0.1.2 : seule l'aire de lavage est une zone de nettoyage. Le sol de la salle n'a plus de poussière
            // décorative et n'accumule rien (mise à jour aussi d'un asset existant).
            if (content.Floor.accumulatesDirt || content.Floor.defaultDirt.Count > 0)
            {
                content.Floor.accumulatesDirt = false;
                content.Floor.defaultDirt.Clear();
                EditorUtility.SetDirty(content.Floor);
            }

            EnsureOrders(content);
            EnsureUpgrades(content);
            content.Network = RugWashNetworkBuilder.Ensure(content);

            AssetDatabase.SaveAssets();
            return content;
        }

        // ------------------------------------------------------------------ Commandes

        static OrderObjectives Objectives(float clean, float maxResidue = -1f, float maxMoisture = -1f, float bonusSeconds = 0f, float maxWear = -1f) =>
            new OrderObjectives
            {
                targetCleanliness = clean,
                limitResidue = maxResidue >= 0f,
                maxResidue = Mathf.Max(0f, maxResidue),
                limitMoisture = maxMoisture >= 0f,
                maxMoisture = Mathf.Max(0f, maxMoisture),
                bonusTimeSeconds = bonusSeconds,
                limitWear = maxWear >= 0f,
                maxWear = Mathf.Max(0f, maxWear),
            };

        static void EnsureOrders(Content c)
        {
            c.Orders.Add(EnsureOrder("order_salon", "Mme Dubois", "Le tapis du salon",
                "Un tapis du quotidien, poussiéreux et marqué par le passage. Idéal pour débuter.",
                c.Classic, 101, 1, 120, 0, Objectives(0.75f, bonusSeconds: 300f),
                new DirtLayer(DirtType.Dust, DirtPattern.Uniform, 0.7f, 0.4f, 0.3f),
                new DirtLayer(DirtType.Soil, DirtPattern.TrafficLane, 0.4f, 0.6f, 0.25f),
                new DirtLayer(DirtType.Drink, DirtPattern.Spots, 0.1f, 0.8f, 0.12f)));
            c.Orders.Add(EnsureOrder("order_cafe", "Café du Parc", "Tapis d'entrée du café",
                "Boue, terre et boissons renversées. Le gérant ne veut aucune trace de produit.",
                c.Classic, 102, 2, 190, 0, Objectives(0.8f, maxResidue: 0.2f, bonusSeconds: 420f),
                new DirtLayer(DirtType.Dust, DirtPattern.Uniform, 0.6f, 0.4f, 0.3f),
                new DirtLayer(DirtType.Mud, DirtPattern.Blotches, 0.4f, 0.85f, 0.3f),
                new DirtLayer(DirtType.Soil, DirtPattern.Edges, 0.5f, 0.7f, 0.2f),
                new DirtLayer(DirtType.Drink, DirtPattern.Spots, 0.3f, 0.9f, 0.12f)));
            c.Orders.Add(EnsureOrder("order_shag", "Famille Martin", "Tapis de chambre d'enfant",
                "Poils longs pleins de poussière, de poils d'animaux et de jus de fruits. À rendre bien sec.",
                c.Shag, 103, 2, 220, 1, Objectives(0.8f, maxMoisture: 0.3f, bonusSeconds: 420f),
                new DirtLayer(DirtType.Dust, DirtPattern.Uniform, 0.9f, 0.5f, 0.25f),
                new DirtLayer(DirtType.HairDebris, DirtPattern.Uniform, 0.6f, 0.4f, 0.2f),
                new DirtLayer(DirtType.Drink, DirtPattern.Spots, 0.2f, 0.85f, 0.1f),
                new DirtLayer(DirtType.Mud, DirtPattern.Blotches, 0.2f, 0.7f, 0.3f)));
            c.Orders.Add(EnsureOrder("order_heirloom", "M. Haddad", "Tapis oriental de famille",
                "Un tapis transmis depuis trois générations. Le motif doit retrouver tout son éclat.",
                c.Oriental, 104, 2, 280, 2, Objectives(0.85f, maxResidue: 0.15f, maxMoisture: 0.35f, bonusSeconds: 600f),
                new DirtLayer(DirtType.Dust, DirtPattern.Uniform, 0.9f, 0.6f, 0.3f),
                new DirtLayer(DirtType.Soil, DirtPattern.TrafficLane, 0.55f, 0.85f, 0.25f),
                new DirtLayer(DirtType.Mud, DirtPattern.Blotches, 0.3f, 0.85f, 0.35f),
                new DirtLayer(DirtType.Drink, DirtPattern.Spots, 0.16f, 0.9f, 0.14f)));
            c.Orders.Add(EnsureOrder("order_restaurant", "Restaurant Le Saule", "Tapis de la salle à manger",
                "Graisse, vin et passages répétés. Un vrai défi qui demande du détergent.",
                c.Oriental, 105, 3, 400, 4, Objectives(0.85f, maxResidue: 0.12f, maxMoisture: 0.3f, bonusSeconds: 720f),
                new DirtLayer(DirtType.Dust, DirtPattern.Uniform, 0.6f, 0.4f, 0.3f),
                new DirtLayer(DirtType.Soil, DirtPattern.TrafficLane, 0.5f, 0.8f, 0.25f),
                new DirtLayer(DirtType.Grease, DirtPattern.Spots, 0.35f, 0.9f, 0.14f),
                new DirtLayer(DirtType.Drink, DirtPattern.Spots, 0.25f, 0.9f, 0.12f)));
            c.Orders.Add(EnsureOrder("order_gallery", "Galerie Lumen", "Pièce de collection",
                "Un tapis exposé, à rendre parfaitement propre, sec et sans le moindre résidu.",
                c.Oriental, 106, 3, 560, 6, Objectives(0.95f, maxResidue: 0.05f, maxMoisture: 0.2f, bonusSeconds: 900f),
                new DirtLayer(DirtType.Dust, DirtPattern.Uniform, 0.8f, 0.5f, 0.3f),
                new DirtLayer(DirtType.Soil, DirtPattern.Blotches, 0.25f, 0.6f, 0.35f),
                new DirtLayer(DirtType.Embedded, DirtPattern.Spots, 0.2f, 0.8f, 0.08f)));
            c.Orders.Add(EnsureOrder("order_kennel", "Pension canine Les Truffes", "Tapis de la salle de jeux",
                "Des poils d'animaux partout, des amas de poussière dans les coins et quelques taches de boisson.",
                c.Classic, 107, 2, 230, 1, Objectives(0.8f, bonusSeconds: 420f),
                new DirtLayer(DirtType.PetHair, DirtPattern.Uniform, 0.8f, 0.55f, 0.25f),
                new DirtLayer(DirtType.DustClumps, DirtPattern.Edges, 0.6f, 0.6f, 0.25f),
                new DirtLayer(DirtType.Lint, DirtPattern.Blotches, 0.3f, 0.4f, 0.3f),
                new DirtLayer(DirtType.Drink, DirtPattern.Spots, 0.15f, 0.85f, 0.1f)));
            c.Orders.Add(EnsureOrder("order_tailor", "Atelier Couture Lemaire", "Tapis de l'atelier de couture",
                "Peluches et fils partout, et des taches d'huile de machine bien accrochées. Pas de traces de produit.",
                c.Classic, 109, 2, 250, 1, Objectives(0.8f, maxResidue: 0.15f, bonusSeconds: 480f),
                new DirtLayer(DirtType.Lint, DirtPattern.Uniform, 0.8f, 0.6f, 0.2f),
                new DirtLayer(DirtType.HairDebris, DirtPattern.Blotches, 0.4f, 0.5f, 0.3f),
                new DirtLayer(DirtType.Grease, DirtPattern.Spots, 0.25f, 0.85f, 0.1f, adhesionOffset: 0.25f)));
            c.Orders.Add(EnsureOrder("order_site", "Chantier Bâtir & Fils", "Tapis du bureau de chantier",
                "Boue séchée en croûtes, terre et poussière épaisse. Le client ne veut aucun résidu de produit.",
                c.Shag, 108, 3, 300, 2, Objectives(0.8f, maxResidue: 0.2f, bonusSeconds: 540f),
                new DirtLayer(DirtType.DriedMud, DirtPattern.Blotches, 0.45f, 0.9f, 0.3f, adhesionOffset: 0.2f),
                new DirtLayer(DirtType.DustClumps, DirtPattern.Uniform, 0.6f, 0.5f, 0.3f),
                new DirtLayer(DirtType.Soil, DirtPattern.TrafficLane, 0.5f, 0.7f, 0.25f),
                new DirtLayer(DirtType.Dust, DirtPattern.Uniform, 0.7f, 0.4f, 0.3f)));
            c.Orders.Add(EnsureOrder("order_palace", "Résidence Valmont", "Tapis de soie du grand salon",
                "Une pièce en soie nouée main, d'une grande valeur. Fibres très fragiles : pas de brossage à sec, pas de produit agressif, " +
                "à rendre propre, sec et sans résidu.",
                c.Luxury, 110, 4, 950, 8, Objectives(0.9f, maxResidue: 0.08f, maxMoisture: 0.25f, bonusSeconds: 1080f, maxWear: 0.04f),
                new DirtLayer(DirtType.Dust, DirtPattern.Uniform, 0.7f, 0.35f, 0.3f),
                new DirtLayer(DirtType.Drink, DirtPattern.Spots, 0.25f, 0.85f, 0.12f, adhesionOffset: 0.3f),
                new DirtLayer(DirtType.Grease, DirtPattern.Spots, 0.12f, 0.7f, 0.08f),
                new DirtLayer(DirtType.Lint, DirtPattern.Blotches, 0.3f, 0.35f, 0.3f),
                new DirtLayer(DirtType.Embedded, DirtPattern.Spots, 0.1f, 0.7f, 0.06f)));
        }

        static OrderDefinition EnsureOrder(string id, string client, string title, string description, RugDefinition rug, int seed,
            int difficulty, int reward, int reputation, OrderObjectives objectives, params DirtLayer[] dirt)
        {
            var order = LoadOrCreate<OrderDefinition>($"{RugWashPaths.OrderDefinitions}/{id}.asset", out bool created);
            if (!created && order.rug != null) return order;
            order.id = id;
            order.clientName = client;
            order.title = title;
            order.description = description;
            order.rug = rug;
            order.dirtSeed = seed;
            order.dirt = new List<DirtLayer>(dirt);
            order.difficulty = difficulty;
            order.baseReward = reward;
            order.requiredReputation = reputation;
            order.objectives = objectives;
            EditorUtility.SetDirty(order);
            return order;
        }

        // ------------------------------------------------------------------ Améliorations

        static void EnsureUpgrades(Content c)
        {
            // v0.1.2 : achat des outils hors kit de départ (identifiants repris par la migration des sauvegardes v1).
            var vacuumTool = EnsureUpgrade("up_tool_vacuum", "Aspirateur", "Retire la poussière, les poils et les débris secs.",
                150, UpgradeEffect.UnlockTool, 1f, "vacuum", null);
            var broomTool = EnsureUpgrade("up_tool_broom", "Balai", "Chasse poils et débris ; pousse l'eau et la boue de l'aire vers le caniveau.",
                110, UpgradeEffect.UnlockTool, 1f, "broom", null);
            var rinseTool = EnsureUpgrade("up_tool_rinse", "Lance de rinçage haute pression", "Rince de plus loin et bien plus vite que le seau.",
                220, UpgradeEffect.UnlockTool, 1f, "rinse", null);
            var extractorTool = EnsureUpgrade("up_tool_extractor", "Extracteur", "Aspire l'eau des fibres : indispensable pour rendre un tapis sec.",
                320, UpgradeEffect.UnlockTool, 1f, "extractor", null);
            var foamTool = EnsureUpgrade("up_tool_foam", "Canon à mousse", "Couvre une grande zone de mousse épaisse (consomme du détergent).",
                380, UpgradeEffect.UnlockTool, 1f, "foam_cannon", null);
            // v0.1.2 jalon 4 : raclette et monobrosse.
            var squeegeeTool = EnsureUpgrade("up_tool_squeegee", "Raclette", "Pousse l'eau et la boue de l'aire de lavage jusqu'au caniveau.",
                90, UpgradeEffect.UnlockTool, 1f, "squeegee", null);
            var rotaryTool = EnsureUpgrade("up_tool_rotary", "Monobrosse professionnelle",
                "Disque rotatif : détache vite sur fibres humides ou savonneuses et fait monter la mousse (à rincer ensuite).",
                650, UpgradeEffect.UnlockTool, 1f, "rotary", null);
            toolUnlocks = new System.Collections.Generic.Dictionary<string, UpgradeDefinition>
            {
                ["vacuum"] = vacuumTool, ["broom"] = broomTool, ["rinse"] = rinseTool, ["extractor"] = extractorTool, ["foam_cannon"] = foamTool,
                ["squeegee"] = squeegeeTool, ["rotary"] = rotaryTool,
            };

            var water1 = EnsureUpgrade("up_water_1", "Réservoir d'eau 60 L", "Rincez plus longtemps entre deux remplissages.",
                150, UpgradeEffect.WaterCapacity, 20f, null, null);
            c.Upgrades.Add(water1);
            c.Upgrades.Add(EnsureUpgrade("up_water_2", "Réservoir d'eau 80 L", "Le plus grand réservoir du catalogue.",
                300, UpgradeEffect.WaterCapacity, 20f, null, water1));
            c.Upgrades.Add(EnsureUpgrade("up_soap_capacity", "Réserve de détergent 6 L", "Plus de produit embarqué.",
                120, UpgradeEffect.SoapCapacity, 2f, null, null));
            c.Upgrades.Add(EnsureUpgrade("up_soap_eco", "Buse économe", "La même mousse avec 30 % de produit en moins.",
                180, UpgradeEffect.ToolConsumption, 0.7f, "soap", null));
            c.Upgrades.Add(EnsureUpgrade("up_recovery", "Cuve de récupération 18 L", "Videz la cuve moins souvent.",
                160, UpgradeEffect.RecoveryCapacity, 6f, null, null));
            c.Upgrades.Add(EnsureUpgrade("up_vacuum", "Moteur d'aspiration turbo", "Aspire la poussière 40 % plus vite.",
                220, UpgradeEffect.ToolPower, 1.4f, "vacuum", null));
            c.Upgrades.Add(EnsureUpgrade("up_brush", "Brosse à poils renforcés", "Détache la saleté 35 % plus vite.",
                250, UpgradeEffect.ToolPower, 1.35f, "brush", null));
            c.Upgrades.Add(EnsureUpgrade("up_rinse", "Buse haute pression", "Évacue la saleté 30 % plus vite.",
                240, UpgradeEffect.ToolPower, 1.3f, "rinse", null));
            c.Upgrades.Add(EnsureUpgrade("up_extractor", "Extracteur haute dépression", "Sèche les fibres 40 % plus vite.",
                260, UpgradeEffect.ToolPower, 1.4f, "extractor", null));
            c.Upgrades.Add(EnsureUpgrade("up_detail_brush", "Brosse de détail", "Petite brosse dure pour les taches tenaces (graisse, taches incrustées).",
                140, UpgradeEffect.UnlockTool, 1f, "detail_brush", null));
            c.Upgrades.Add(EnsureUpgrade("up_broom_wide", "Balai large", "Balaie une bande 35 % plus large.",
                90, UpgradeEffect.ToolRadius, 1.35f, "broom", null));
            c.Upgrades.Add(EnsureUpgrade("up_foam_wide", "Buse à mousse large", "Le canon à mousse couvre une zone 30 % plus large.",
                120, UpgradeEffect.ToolRadius, 1.3f, "foam_cannon", null));
            // En fin de catalogue : l'ordre (et les indices) des améliorations existantes reste inchangé ; c'est la boutique
            // (ShopUI) qui affiche les outils à acheter en premier.
            c.Upgrades.AddRange(new[] { vacuumTool, broomTool, rinseTool, extractorTool, foamTool });
            // v0.1.2 jalon 4 (ajoutés en fin de catalogue, mêmes raisons).
            c.Upgrades.AddRange(new[] { squeegeeTool, rotaryTool });
            c.Upgrades.Add(EnsureUpgrade("up_vacuum_fiber", "Brosse turbo anti-poils",
                "Accessoire d'aspirateur : une brosse rotative décolle les poils, cheveux et peluches accrochés aux fibres.",
                130, UpgradeEffect.FiberPickup, 2f, "vacuum", null));
            c.Upgrades.Add(EnsureUpgrade("up_boots", "Bottes de lavage",
                "Semelles lisses qui retiennent peu la boue : vos pas salissent beaucoup moins l'aire de lavage et les tapis.",
                90, UpgradeEffect.FootprintTransfer, 0.3f, null, null));
            // v0.1.2 jalon 5 : machines fonctionnelles de l'atelier (réputation requise) et décor par paliers.
            c.Upgrades.Add(Reputation(EnsureUpgrade("up_machine_duster", "Plaque vibrante de dépoussiérage",
                "Sous l'aire de lavage : un cycle de 8 s par commande secoue le tapis sec et fait tomber environ la moitié de la " +
                "poussière, des poils et des débris. Sans effet sur les taches ni sur un tapis mouillé.",
                350, UpgradeEffect.WorkshopEquipment, 1f, "duster", null), 3));
            c.Upgrades.Add(Reputation(EnsureUpgrade("up_machine_blower", "Souffleur de séchage",
                "Ventilateur orienté vers l'aire de lavage : allumé, il sèche le tapis bien plus vite. Il ne retire ni saleté ni résidus.",
                500, UpgradeEffect.WorkshopEquipment, 1f, "blower", null), 8));
            c.Upgrades.Add(Reputation(EnsureUpgrade("up_machine_prewet", "Rampe de pré-mouillage",
                "Au-dessus de l'aire : un cycle de 8 s par commande humidifie tout le tapis et fait tremper les taches (une part se " +
                "détache, à brosser puis rincer). Elle ne nettoie rien seule.",
                750, UpgradeEffect.WorkshopEquipment, 1f, "prewet", null), 15));
            var tier1 = Reputation(EnsureUpgrade("up_base_1", "Atelier rangé", "Les cartons partent, une étagère et un tableau de liège arrivent, " +
                "les fenêtres sont dégagées et la lumière revient.", 400, UpgradeEffect.WorkshopTier, 1f, null, null), 3);
            var tier2 = Reputation(EnsureUpgrade("up_base_2", "Atelier professionnel", "Enseigne, banc d'attente, coin café et plantes : " +
                "un vrai atelier pour recevoir les clients.", 1200, UpgradeEffect.WorkshopTier, 2f, null, tier1), 15);
            var tier3 = Reputation(EnsureUpgrade("up_base_3", "Loft industriel", "Mur de briques, coin salon sous des suspensions, " +
                "tapis exposés aux murs.", 3000, UpgradeEffect.WorkshopTier, 3f, null, tier2), 35);
            var tier4 = Reputation(EnsureUpgrade("up_base_4", "Penthouse à Los Angeles", "Le dernier étage d'une tour : baies " +
                "panoramiques sur Los Angeles au crépuscule, finitions dorées, palmier d'intérieur.", 7500, UpgradeEffect.WorkshopTier, 4f, null, tier3), 63);
            c.Upgrades.AddRange(new[] { tier1, tier2, tier3, tier4 });
            c.Upgrades.Add(EnsureUpgrade("up_robot", "Robot aspirateur",
                "Aspire seul la poussière, les poils et les débris secs de l'aire de lavage (pas l'eau ni les taches) ; contourne " +
                "le tapis et les joueurs, rentre se recharger. E sur le robot ou sa base pour le lancer ou l'arrêter.",
                650, UpgradeEffect.WorkshopEquipment, 1f, UpgradeManager.RobotMachineId, null));
        }

        static System.Collections.Generic.Dictionary<string, UpgradeDefinition> toolUnlocks;

        /// <summary>Réputation requise (fixée à la création ; modifiable ensuite dans l'Inspector).</summary>
        static UpgradeDefinition Reputation(UpgradeDefinition upgrade, int reputation)
        {
            if (upgrade.requiredReputation == 0 && reputation > 0)
            {
                upgrade.requiredReputation = reputation;
                EditorUtility.SetDirty(upgrade);
            }
            return upgrade;
        }

        static UpgradeDefinition EnsureUpgrade(string id, string name, string description, int price, UpgradeEffect effect, float value,
            string toolId, UpgradeDefinition prerequisite)
        {
            var upgrade = LoadOrCreate<UpgradeDefinition>($"{RugWashPaths.UpgradeDefinitions}/{id}.asset", out bool created);
            // Les améliorations d'un outil à acheter exigent l'outil (aussi pour un asset existant).
            if (effect != UpgradeEffect.UnlockTool && prerequisite == null && toolUnlocks != null && toolId != null
                && toolUnlocks.TryGetValue(toolId, out var unlock)) prerequisite = unlock;
            if (!created)
            {
                if (upgrade.prerequisite != prerequisite && prerequisite != null)
                {
                    upgrade.prerequisite = prerequisite;
                    EditorUtility.SetDirty(upgrade);
                }
                return upgrade;
            }
            upgrade.id = id;
            upgrade.displayName = name;
            upgrade.description = description;
            upgrade.price = price;
            upgrade.effect = effect;
            upgrade.value = value;
            upgrade.targetToolId = toolId;
            upgrade.prerequisite = prerequisite;
            EditorUtility.SetDirty(upgrade);
            return upgrade;
        }

        // ------------------------------------------------------------------ Données

        static DirtTypeLibrary EnsureDirtLibrary()
        {
            var library = LoadOrCreate<DirtTypeLibrary>(DirtLibraryPath, out _);
            var names = new Dictionary<DirtType, string>
            {
                { DirtType.Dust, DirtTypeNames.Default(DirtType.Dust) }, { DirtType.Mud, DirtTypeNames.Default(DirtType.Mud) },
                { DirtType.Soil, DirtTypeNames.Default(DirtType.Soil) }, { DirtType.Drink, DirtTypeNames.Default(DirtType.Drink) },
                { DirtType.Grease, DirtTypeNames.Default(DirtType.Grease) }, { DirtType.HairDebris, DirtTypeNames.Default(DirtType.HairDebris) },
                { DirtType.Embedded, DirtTypeNames.Default(DirtType.Embedded) }, { DirtType.DustClumps, DirtTypeNames.Default(DirtType.DustClumps) },
                { DirtType.DriedMud, DirtTypeNames.Default(DirtType.DriedMud) }, { DirtType.PetHair, DirtTypeNames.Default(DirtType.PetHair) },
                { DirtType.Lint, DirtTypeNames.Default(DirtType.Lint) },
            };
            // Anciens noms (avant le jalon 3) : remplacés s'ils n'ont pas été modifiés à la main.
            var legacyNames = new HashSet<string> { "Poussière", "Boue", "Boisson", "Graisse", "Poils et débris", "Tache incrustée" };
            foreach (var pair in names)
            {
                string path = $"{RugWashPaths.DirtTypeDefinitions}/Dirt_{pair.Key}.asset";
                var definition = LoadOrCreate<DirtTypeDefinition>(path, out bool created);
                if (created)
                {
                    definition.type = pair.Key;
                    definition.displayName = pair.Value;
                    definition.color = DirtTypeTable.DefaultColor(pair.Key);
                    definition.response = DirtTypeTable.DefaultResponse(pair.Key);
                    definition.adhesion = DirtTypeTable.DefaultAdhesion(pair.Key);
                    definition.productSensitivity = pair.Key == DirtType.Embedded ? 0.7f : 1f;
                    EditorUtility.SetDirty(definition);
                }
                else
                {
                    bool changed = false;
                    if (legacyNames.Contains(definition.displayName) && definition.displayName != pair.Value) { definition.displayName = pair.Value; changed = true; }
                    if (definition.adhesion <= 0f && DirtTypeTable.DefaultAdhesion(pair.Key) > 0f)
                    {
                        definition.adhesion = DirtTypeTable.DefaultAdhesion(pair.Key);
                        if (pair.Key == DirtType.Embedded) definition.productSensitivity = 0.7f;
                        changed = true;
                    }
                    if (definition.response.sweep <= 0f && DirtTypeTable.DefaultResponse(pair.Key).sweep > 0f)
                    {
                        // Champ ajouté avec le balai : les définitions existantes reçoivent la valeur de référence.
                        definition.response.sweep = DirtTypeTable.DefaultResponse(pair.Key).sweep;
                        changed = true;
                    }
                    if (changed) EditorUtility.SetDirty(definition);
                }
                if (!library.definitions.Contains(definition))
                {
                    library.definitions.Add(definition);
                    EditorUtility.SetDirty(library);
                }
            }
            return library;
        }

        static ToolDefinition EnsureTool(string path, string id, string displayName, int slot, string description,
            float range, float radius, float hardness, float power, float wetting, ToolResource resource, float perSecond)
        {
            var tool = LoadOrCreate<ToolDefinition>(path, out bool created);
            if (!created) return tool;
            tool.id = id;
            tool.displayName = displayName;
            tool.slot = slot;
            tool.description = description;
            tool.range = range;
            tool.radius = radius;
            tool.falloffHardness = hardness;
            tool.power = power;
            tool.wettingPerSecond = wetting;
            tool.resource = resource;
            tool.resourcePerSecond = perSecond;
            EditorUtility.SetDirty(tool);
            return tool;
        }

        /// <summary>
        /// Réglages ajoutés après la création des premiers outils (éventail, ruissellement, déblocage) : appliqués aux
        /// outils existants seulement s'ils sont encore à leur valeur neutre, pour conserver les réglages faits à la main.
        /// </summary>
        static void EnsureToolBehaviour(ToolDefinition tool, float aspect = 1f, float runoffDirt = 0f, float runoffFoam = 0f,
            float runoffWater = 0f, bool dry = false, bool requiresUnlock = false)
        {
            bool changed = false;
            if (aspect > 1f && Mathf.Approximately(tool.spreadAspect, 1f)) { tool.spreadAspect = aspect; changed = true; }
            if (tool.runoffDirt <= 0f && tool.runoffFoam <= 0f && tool.runoffWater <= 0f && (runoffDirt > 0f || runoffFoam > 0f || runoffWater > 0f))
            {
                tool.runoffDirt = runoffDirt;
                tool.runoffFoam = runoffFoam;
                tool.runoffWater = runoffWater;
                tool.runoffIsDry = dry;
                changed = true;
            }
            if (requiresUnlock && !tool.requiresUnlock) { tool.requiresUnlock = true; changed = true; }
            if (changed) EditorUtility.SetDirty(tool);
        }

        /// <summary>Vibrations d'un outil (ajoutées au jalon 4) : appliquées tant qu'elles sont à leur valeur par défaut.</summary>
        static void EnsureToolRumble(ToolDefinition tool, float low, float high)
        {
            if (!Mathf.Approximately(tool.rumbleLow, ToolDefinition.DefaultRumble) || !Mathf.Approximately(tool.rumbleHigh, ToolDefinition.DefaultRumble)) return;
            tool.rumbleLow = low;
            tool.rumbleHigh = high;
            EditorUtility.SetDirty(tool);
        }

        /// <summary>Matière d'un tapis (ajoutée au jalon 3) : appliquée tant qu'elle n'a pas été réglée à la main.</summary>
        static void EnsureRugMatter(RugDefinition rug, float fragility, float sheen, Color fringeColor)
        {
            if (rug.fragility > 0f || rug.sheen > 0f) return;
            rug.fragility = fragility;
            rug.sheen = sheen;
            rug.fringeColor = fringeColor;
            EditorUtility.SetDirty(rug);
        }

        static void EnsureRugDescriptions(Content c)
        {
            void Describe(RugDefinition rug, string text)
            {
                if (rug == null || !string.IsNullOrEmpty(rug.description)) return;
                rug.description = text;
                EditorUtility.SetDirty(rug);
            }
            Describe(c.Classic, "Tapis tufté à poils courts, robuste : le compagnon des entrées et des salons.");
            Describe(c.Oriental, "Laine nouée à la main, médaillon central et bordures florales ; le tapis d'entraînement de l'atelier.");
            Describe(c.Shag, "Poils longs et moelleux qui retiennent poussière, poils et miettes au cœur du velours.");
            Describe(c.Luxury, "Soie nouée main, plus de cent nœuds au centimètre carré. Éclat soyeux et fibres très délicates.");
        }

        static ProductLibrary EnsureProducts()
        {
            if (!AssetDatabase.IsValidFolder(ProductFolder)) AssetDatabase.CreateFolder(RugWashPaths.Root + "/ScriptableObjects", "Products");
            var library = LoadOrCreate<ProductLibrary>(ProductLibraryPath, out _);
            void Add(ProductDefinition product)
            {
                if (library.products.Contains(product)) return;
                library.products.Add(product);
                EditorUtility.SetDirty(library);
            }
            Add(EnsureProduct("product_degreaser", "Dégraissant", "Dissout graisses et huiles.",
                new[] { DirtType.Grease }, boost: 2.5f, dwell: 3f, residue: 1.3f, harshness: 0.8f, lps: 0.04f, pack: 2f, price: 90, max: 6f));
            Add(EnsureProduct("product_enzyme", "Détachant enzymatique", "Décompose lentement les taches organiques ; à laisser agir.",
                new[] { DirtType.Drink, DirtType.Embedded }, boost: 2f, dwell: 4f, residue: 1f, harshness: 0f, lps: 0.03f, pack: 1.5f, price: 110, max: 4.5f));
            Add(EnsureProduct("product_silk", "Shampooing soie et laine", "Doux pour les fibres délicates, rince sans laisser de traces.",
                new[] { DirtType.Dust, DirtType.DustClumps, DirtType.Soil, DirtType.Mud, DirtType.Drink }, boost: 1.3f, dwell: 1.2f, residue: 0.5f,
                harshness: 0f, lps: 0.03f, pack: 2f, price: 70, max: 6f));
            Add(EnsureProduct("product_mud", "Décolleur de boue", "Ramollit la boue sèche et la terre incrustée.",
                new[] { DirtType.DriedMud, DirtType.Mud, DirtType.Soil }, boost: 2.2f, dwell: 2f, residue: 1.1f, harshness: 0.3f, lps: 0.04f,
                pack: 2f, price: 80, max: 6f));
            return library;
        }

        static ProductDefinition EnsureProduct(string id, string name, string description, DirtType[] targets, float boost, float dwell,
            float residue, float harshness, float lps, float pack, int price, float max)
        {
            var product = LoadOrCreate<ProductDefinition>($"{ProductFolder}/{id}.asset", out bool created);
            if (!created) return product;
            product.id = id;
            product.displayName = name;
            product.description = description;
            product.targets = new List<DirtType>(targets);
            product.targetBoost = boost;
            product.dwellBoost = dwell;
            product.residueFactor = residue;
            product.harshness = harshness;
            product.litersPerSecond = lps;
            product.packLiters = pack;
            product.packPrice = price;
            product.maxStock = max;
            EditorUtility.SetDirty(product);
            return product;
        }

        static RugCatalog EnsureCatalog(Content c)
        {
            var catalog = LoadOrCreate<RugCatalog>(RugCatalogPath, out _);
            foreach (var rug in new[] { c.Classic, c.Oriental, c.Shag, c.Luxury })
                if (rug != null && !catalog.rugs.Contains(rug))
                {
                    catalog.rugs.Add(rug);
                    EditorUtility.SetDirty(catalog);
                }
            return catalog;
        }

        static RugDefinition EnsureSurface(string path, string id, string displayName, SurfaceKind kind, Vector2 size, int cellsPerMeter,
            float drying, float fiberTiles, int seed, params DirtLayer[] dirt)
        {
            var surface = LoadOrCreate<RugDefinition>(path, out bool created);
            if (!created && surface.patternTexture != null) return surface;
            surface.id = id;
            surface.displayName = displayName;
            surface.surfaceKind = kind;
            surface.sizeMeters = size;
            surface.thickness = 0.004f;
            surface.cellsPerMeter = cellsPerMeter;
            surface.patternTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(RugWashTextureGenerator.ConcreteAlbedo);
            surface.fiberNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(RugWashTextureGenerator.ConcreteNormal);
            surface.fiberTilesPerMeter = fiberTiles;
            surface.fiberNormalStrength = 0.6f;
            surface.hasFringe = false;
            surface.dryingPerSecond = drying;
            surface.dirtSeed = seed;
            surface.defaultDirt = new List<DirtLayer>(dirt);
            EditorUtility.SetDirty(surface);
            return surface;
        }

        static RugDefinition EnsureRug(string path, string id, string displayName, RugPileType pile, Vector2 size, float thickness,
            int cellsPerMeter, string albedoPath, string normalPath, float fiberTiles, float fiberStrength, bool fringe, int seed,
            params DirtLayer[] dirt)
        {
            var rug = LoadOrCreate<RugDefinition>(path, out bool created);
            if (!created && rug.patternTexture != null) return rug;
            rug.id = id;
            rug.displayName = displayName;
            rug.pileType = pile;
            rug.sizeMeters = size;
            rug.thickness = thickness;
            rug.cellsPerMeter = cellsPerMeter;
            rug.patternTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
            rug.fiberNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            rug.fiberTilesPerMeter = fiberTiles;
            rug.fiberNormalStrength = fiberStrength;
            rug.hasFringe = fringe;
            rug.dirtSeed = seed;
            rug.defaultDirt = new List<DirtLayer>(dirt);
            EditorUtility.SetDirty(rug);
            return rug;
        }

        // ------------------------------------------------------------------ Matériaux

        static void EnsureMaterials(Content content)
        {
            Texture2D Tex(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

            var rugSurface = EnsureMaterial("M_RugSurface", "RugWash/Rug Surface", out bool created);
            if (created || rugSurface.GetTexture("_DirtDetail") == null)
            {
                rugSurface.SetTexture("_DirtDetail", Tex(RugWashTextureGenerator.DirtDetail));
                rugSurface.SetTexture("_FiberNormal", Tex(RugWashTextureGenerator.FiberShortNormal));
                EditorUtility.SetDirty(rugSurface);
            }
            content.Materials["RugSurface"] = rugSurface;
            rugSurface.SetFloat("_ScanResponse", 1f);
            content.Materials["WorkSurface"] = EnsureSurfaceMaterial("M_WorkSurface", new Color(0.78f, 0.8f, 0.82f), new Vector2(3.2f, 2.5f),
                drySmoothness: 0.42f, wetSmoothness: 0.78f, wetDarkening: 0.28f, scanResponse: 1f, Tex);
            content.Materials["FloorSurface"] = EnsureSurfaceMaterial("M_FloorSurface", Color.white, new Vector2(6f, 5f),
                drySmoothness: 0.2f, wetSmoothness: 0.68f, wetDarkening: 0.3f, scanResponse: 0f, Tex);

            var fringe = EnsureLit("M_RugFringe", new Color(0.9f, 0.86f, 0.75f), 0.15f);
            fringe.SetFloat("_Cull", 0f);
            fringe.doubleSidedGI = true;
            EditorUtility.SetDirty(fringe);
            content.Materials["RugFringe"] = fringe;

            content.Materials["TargetRing"] = EnsureMaterial("M_TargetRing", "RugWash/Target Ring", out _);
            content.Materials["WaterJet"] = EnsureParticle("M_Particle_Water", new Color(0.72f, 0.88f, 1f, 0.55f), 0.7f);
            content.Materials["Splash"] = EnsureParticle("M_Particle_Splash", new Color(0.9f, 0.96f, 1f, 0.7f), 0.5f);
            content.Materials["Debris"] = EnsureParticle("M_Particle_Debris", new Color(1f, 1f, 1f, 0.9f), 0.35f);
            content.Materials["Dust"] = EnsureParticle("M_Particle_Dust", new Color(0.66f, 0.62f, 0.55f, 0.55f), 0.8f);
            content.Materials["SoapMist"] = EnsureParticle("M_Particle_SoapMist", new Color(0.9f, 0.97f, 1f, 0.35f), 0.9f);
            content.Materials["Foam"] = EnsureParticle("M_Particle_Foam", new Color(1f, 1f, 1f, 0.9f), 0.25f);
            content.Materials["Sparkle"] = EnsureParticle("M_Particle_Sparkle", new Color(1f, 0.96f, 0.75f, 1f), 0.5f);
            content.Materials["Droplet"] = EnsureParticle("M_Particle_Droplet", new Color(0.6f, 0.75f, 0.9f, 0.7f), 0.4f);

            content.Materials["Concrete"] = EnsureLit("M_Concrete", new Color(1f, 1f, 1f), 0.22f,
                Tex(RugWashTextureGenerator.ConcreteAlbedo), Tex(RugWashTextureGenerator.ConcreteNormal), tiling: 0.5f);
            content.Materials["WashPad"] = EnsureLit("M_WashPad", new Color(0.78f, 0.8f, 0.82f), 0.5f,
                Tex(RugWashTextureGenerator.ConcreteAlbedo), Tex(RugWashTextureGenerator.ConcreteNormal), tiling: 0.7f);
            content.Materials["Plaster"] = EnsureLit("M_Plaster", new Color(0.95f, 0.94f, 0.9f), 0.12f,
                Tex(RugWashTextureGenerator.PlasterAlbedo), null, tiling: 0.6f);
            content.Materials["Wainscot"] = EnsureLit("M_Wainscot", new Color(0.24f, 0.36f, 0.38f), 0.35f,
                Tex(RugWashTextureGenerator.PlasterAlbedo), null, tiling: 0.6f);
            content.Materials["Ceiling"] = EnsureLit("M_Ceiling", new Color(0.55f, 0.56f, 0.57f), 0.1f,
                Tex(RugWashTextureGenerator.PlasterAlbedo), null, tiling: 0.5f);
            content.Materials["Wood"] = EnsureLit("M_Wood", Color.white, 0.3f, Tex(RugWashTextureGenerator.WoodAlbedo), null, tiling: 1f);
            content.Materials["Grate"] = EnsureLit("M_Grate", Color.white, 0.55f, Tex(RugWashTextureGenerator.GrateAlbedo), null,
                tiling: 1f, metallic: 0.8f);
            content.Materials["Steel"] = EnsureLit("M_Steel", new Color(0.62f, 0.64f, 0.66f), 0.6f, metallic: 0.9f);
            content.Materials["DarkMetal"] = EnsureLit("M_DarkMetal", new Color(0.16f, 0.17f, 0.19f), 0.45f, metallic: 0.7f);
            content.Materials["SafetyYellow"] = EnsureLit("M_SafetyYellow", new Color(0.95f, 0.72f, 0.12f), 0.35f);
            content.Materials["BluePlastic"] = EnsureLit("M_BluePlastic", new Color(0.12f, 0.36f, 0.68f), 0.55f);
            content.Materials["WhitePlastic"] = EnsureLit("M_WhitePlastic", new Color(0.88f, 0.89f, 0.9f), 0.5f);
            content.Materials["Chrome"] = EnsureLit("M_Chrome", new Color(0.8f, 0.82f, 0.85f), 0.85f, metallic: 1f);
            content.Materials["Glass"] = EnsureLit("M_Glass", new Color(0.55f, 0.7f, 0.78f), 0.92f, metallic: 0.2f);
            content.Materials["GreyHose"] = EnsureLit("M_GreyHose", new Color(0.32f, 0.34f, 0.36f), 0.35f);
            content.Materials["RobotShell"] = EnsureLit("M_RobotShell", new Color(0.9f, 0.91f, 0.92f), 0.62f);
            content.Materials["RobotDark"] = EnsureLit("M_RobotDark", new Color(0.07f, 0.075f, 0.08f), 0.5f);
            content.Materials["RobotLed"] = EnsureEmissive("M_RobotLed", Color.white, 2f);
            content.Materials["FoamCanister"] = EnsureLit("M_FoamCanister", new Color(0.82f, 0.9f, 0.95f), 0.85f);
            // Contenants à niveau de liquide visible (réserve de détergent, v0.1.2) : hauteurs en espace objet des maillages.
            content.Materials["LiquidBottle"] = EnsureLiquid("M_LiquidBottle", new Color(0.8f, 0.9f, 0.82f, 0.35f), new Color(0.2f, 0.62f, 0.32f, 0.94f), -0.195f, -0.035f);
            content.Materials["LiquidCanister"] = EnsureLiquid("M_LiquidCanister", new Color(0.84f, 0.92f, 0.97f, 0.3f), new Color(0.93f, 0.97f, 1f, 0.92f), -0.165f, -0.045f);
            content.Materials["Hazard"] = EnsureLit("M_Hazard", Color.white, 0.3f, Tex(RugWashTextureGenerator.Hazard), null, tiling: 2f);
            content.Materials["Paper"] = EnsureLit("M_Paper", Color.white, 0.05f, Tex(RugWashTextureGenerator.Paper), null, tiling: 1f);
            content.Materials["ClockFace"] = EnsureLit("M_ClockFace", Color.white, 0.3f, Tex(RugWashTextureGenerator.ClockFace), null, tiling: 1f);
            content.Materials["FloorMarking"] = EnsureLit("M_FloorMarking", new Color(0.92f, 0.72f, 0.15f), 0.25f);
            content.Materials["StepBlue"] = EnsureLit("M_StepBlue", new Color(0.2f, 0.45f, 0.75f), 0.3f);
            content.Materials["StepGreen"] = EnsureLit("M_StepGreen", new Color(0.25f, 0.6f, 0.35f), 0.3f);
            content.Materials["StepOrange"] = EnsureLit("M_StepOrange", new Color(0.9f, 0.55f, 0.15f), 0.3f);
            content.Materials["StepCyan"] = EnsureLit("M_StepCyan", new Color(0.2f, 0.7f, 0.8f), 0.3f);
            content.Materials["StepPurple"] = EnsureLit("M_StepPurple", new Color(0.55f, 0.35f, 0.75f), 0.3f);
            content.Materials["RugClassic"] = EnsureLit("M_RugClassic_Display", Color.white, 0.08f,
                Tex(RugWashTextureGenerator.ClassicAlbedo), Tex(RugWashTextureGenerator.FiberShortNormal), tiling: 1f);
            content.Materials["RugShag"] = EnsureLit("M_RugShag_Display", Color.white, 0.05f,
                Tex(RugWashTextureGenerator.ShagAlbedo), Tex(RugWashTextureGenerator.FiberLongNormal), tiling: 1f);
            content.Materials["RugOriental"] = EnsureLit("M_RugOriental_Display", Color.white, 0.08f,
                Tex(RugWashTextureGenerator.OrientalAlbedo), Tex(RugWashTextureGenerator.FiberShortNormal), tiling: 1f);
            foreach (var key in new[] { "RugClassic", "RugShag", "RugOriental" })
            {
                content.Materials[key].SetFloat("_Cull", 0f);
                content.Materials[key].doubleSidedGI = true;
                EditorUtility.SetDirty(content.Materials[key]);
            }
            content.Materials["RedPlastic"] = EnsureLit("M_RedPlastic", new Color(0.72f, 0.16f, 0.12f), 0.5f);
            content.Materials["GreenPlastic"] = EnsureLit("M_GreenPlastic", new Color(0.2f, 0.55f, 0.32f), 0.5f);
            content.Materials["Rubber"] = EnsureLit("M_Rubber", new Color(0.07f, 0.07f, 0.08f), 0.25f);
            content.Materials["Bristle"] = EnsureLit("M_Bristle", new Color(0.85f, 0.55f, 0.15f), 0.2f);
            content.Materials["Pegboard"] = EnsureLit("M_Pegboard", new Color(0.62f, 0.5f, 0.36f), 0.15f);
            content.Materials["Cardboard"] = EnsureLit("M_Cardboard", new Color(0.6f, 0.45f, 0.3f), 0.1f);
            // Décor de l'atelier par paliers (v0.1.2 jalon 5).
            content.Materials["Plywood"] = EnsureLit("M_Plywood", new Color(0.85f, 0.72f, 0.52f), 0.15f, Tex(RugWashTextureGenerator.WoodAlbedo), null, tiling: 1f);
            content.Materials["WallStain"] = EnsureCutout("M_WallStain", Tex(RugWashTextureGenerator.WallStain), 0.05f);
            content.Materials["Brick"] = EnsureLit("M_Brick", Color.white, 0.12f, Tex(RugWashTextureGenerator.BrickAlbedo), null, tiling: 1f);
            content.Materials["Leather"] = EnsureLit("M_Leather", new Color(0.3f, 0.17f, 0.1f), 0.55f);
            content.Materials["Plant"] = EnsureLit("M_Plant", new Color(0.2f, 0.45f, 0.18f), 0.3f);
            content.Materials["Terracotta"] = EnsureLit("M_Terracotta", new Color(0.7f, 0.36f, 0.22f), 0.2f);
            content.Materials["Gold"] = EnsureLit("M_Gold", new Color(0.95f, 0.75f, 0.35f), 0.8f, metallic: 1f);
            content.Materials["WarmBulb"] = EnsureEmissive("M_WarmBulb", new Color(1f, 0.78f, 0.5f), 3f);
            content.Materials["Skyline"] = EnsureUnlitTexture("M_Skyline", Tex(RugWashTextureGenerator.Skyline));
            content.Materials["DoorPaint"] = EnsureLit("M_DoorPaint", new Color(0.2f, 0.3f, 0.42f), 0.4f);
            content.Materials["Signage"] = EnsureLit("M_Signage", new Color(0.1f, 0.13f, 0.16f), 0.3f);
            // Gants et silhouettes des joueurs (placeholders procéduraux : tenue de travail, peau, bottes, casquette).
            content.Materials["Glove"] = EnsureLit("M_Glove", new Color(0.86f, 0.52f, 0.18f), 0.18f);
            content.Materials["GloveCuff"] = EnsureLit("M_GloveCuff", new Color(0.16f, 0.2f, 0.28f), 0.2f);
            content.Materials["Workwear"] = EnsureLit("M_Workwear", new Color(0.16f, 0.24f, 0.36f), 0.15f);
            content.Materials["Shirt"] = EnsureLit("M_Shirt", new Color(0.66f, 0.69f, 0.72f), 0.2f);
            content.Materials["Skin"] = EnsureLit("M_Skin", new Color(0.86f, 0.67f, 0.53f), 0.35f);
            content.Materials["Boot"] = EnsureLit("M_Boot", new Color(0.2f, 0.13f, 0.08f), 0.3f);
            content.Materials["CapFabric"] = EnsureLit("M_CapFabric", new Color(0.74f, 0.2f, 0.14f), 0.15f);
            content.Materials["Reflective"] = EnsureLit("M_Reflective", new Color(0.85f, 0.88f, 0.4f), 0.5f);
            content.Materials["LampPanel"] = EnsureEmissive("M_LampPanel", new Color(1f, 0.95f, 0.85f), 4f);
            content.Materials["Window"] = EnsureEmissive("M_Window", new Color(0.55f, 0.7f, 0.88f), 0.75f);
            content.Materials["ScreenGlow"] = EnsureEmissive("M_ScreenGlow", new Color(0.35f, 0.75f, 0.85f), 1.2f);
        }

        static Material EnsureLiquid(string name, Color empty, Color liquid, float minY, float maxY)
        {
            var material = EnsureMaterial(name, "RugWash/Liquid Container", out _);
            material.SetColor("_EmptyColor", empty);
            material.SetColor("_LiquidColor", liquid);
            material.SetFloat("_MinY", minY);
            material.SetFloat("_MaxY", maxY);
            material.SetFloat("_Fill", 1f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material EnsureMaterial(string name, string shaderName, out bool created)
        {
            string path = $"{RugWashPaths.Materials}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            created = material == null;
            if (created)
            {
                var shader = Shader.Find(shaderName);
                if (shader == null) throw new System.InvalidOperationException($"[RugWash] Shader introuvable : {shaderName}");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            return material;
        }

        static Material EnsureLit(string name, Color color, float smoothness, Texture2D albedo = null, Texture2D normal = null,
            float tiling = 1f, float metallic = 0f)
        {
            var material = EnsureMaterial(name, "Universal Render Pipeline/Lit", out bool created);
            if (!created) return material;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            if (albedo != null)
            {
                material.SetTexture("_BaseMap", albedo);
                material.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
            }
            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.SetFloat("_BumpScale", 0.6f);
                material.EnableKeyword("_NORMALMAP");
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Matériau de surface nettoyable (shader du tapis) : valeurs réappliquées à chaque génération.</summary>
        static Material EnsureSurfaceMaterial(string name, Color color, Vector2 tiling, float drySmoothness, float wetSmoothness,
            float wetDarkening, float scanResponse, System.Func<string, Texture2D> tex)
        {
            var material = EnsureMaterial(name, "RugWash/Rug Surface", out _);
            material.SetTexture("_DirtDetail", tex(RugWashTextureGenerator.DirtDetail));
            material.SetTexture("_FiberNormal", tex(RugWashTextureGenerator.ConcreteNormal));
            material.SetTexture("_BaseMap", tex(RugWashTextureGenerator.ConcreteAlbedo));
            material.SetTextureScale("_BaseMap", tiling);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_DrySmoothness", drySmoothness);
            material.SetFloat("_WetSmoothness", wetSmoothness);
            material.SetFloat("_WetDarkening", wetDarkening);
            material.SetFloat("_CleanBoost", 0f);
            material.SetFloat("_ScanResponse", scanResponse);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Matériau éclairé découpé par l'alpha de sa texture (taches au mur) : valeurs réappliquées à chaque génération.</summary>
        static Material EnsureCutout(string name, Texture2D texture, float smoothness)
        {
            var material = EnsureMaterial(name, "Universal Render Pipeline/Lit", out _);
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.5f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            material.SetOverrideTag("RenderType", "TransparentCutout");
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Texture affichée telle quelle, sans éclairage (vue par une baie vitrée).</summary>
        static Material EnsureUnlitTexture(string name, Texture2D texture)
        {
            var material = EnsureMaterial(name, "Universal Render Pipeline/Unlit", out _);
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material EnsureEmissive(string name, Color color, float intensity)
        {
            var material = EnsureLit(name, color * 0.6f, 0.4f);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * intensity);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material EnsureParticle(string name, Color color, float softness)
        {
            var material = EnsureMaterial(name, "RugWash/Particle Soft", out bool created);
            // Valeurs de référence réappliquées à chaque génération (les matériaux de particules sont générés).
            if (created || material.GetColor("_Color") != color || !Mathf.Approximately(material.GetFloat("_Softness"), softness))
            {
                material.SetColor("_Color", color);
                material.SetFloat("_Softness", softness);
                EditorUtility.SetDirty(material);
            }
            return material;
        }

        static T LoadOrCreate<T>(string path, out bool created) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            created = asset == null;
            if (created)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }
    }
}
