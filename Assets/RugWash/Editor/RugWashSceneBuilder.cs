using System;
using System.Collections.Generic;
using System.IO;
using RugWash.Audio;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Diagnostics;
using RugWash.Economy;
using RugWash.Orders;
using RugWash.Effects;
using RugWash.Player;
using RugWash.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static RugWash.Editor.RugWashBuildHelpers;
using DebugOverlay = RugWash.Diagnostics.DebugOverlay;
using Object = UnityEngine.Object;

namespace RugWash.Editor
{
    /// <summary>
    /// Génère procéduralement la scène Atelier complète et câble toutes les références,
    /// sans aucune manipulation manuelle dans l'Inspector.
    /// </summary>
    public static class RugWashSceneBuilder
    {
        const string MeshStorePath = RugWashPaths.Root + "/Prefabs/Workshop_GeneratedMeshes.asset";

        // Dimensions intérieures de l'atelier (m).
        const float RoomHalfX = 6f, RoomHalfZ = 5f, RoomHeight = 3.6f, WallThickness = 0.2f;
        static readonly Vector3 WashPadCenter = new Vector3(0f, 0f, 0.8f);
        static readonly Vector3 WashPadSize = new Vector3(4.6f, 0.05f, 3.6f);

        [MenuItem("RugWash/Setup/Générer la Scène Atelier Complète", priority = 20)]
        static void BuildWorkshopSceneFromMenu()
        {
            if (File.Exists(RugWashPaths.WorkshopScene) &&
                !EditorUtility.DisplayDialog("RugWash", "Workshop.unity existe déjà. La regénérer entièrement ?", "Regénérer", "Annuler"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            BuildWorkshopScene();
            EditorUtility.DisplayDialog("RugWash", $"Scène générée et câblée : {RugWashPaths.WorkshopScene}", "OK");
        }

        /// <summary>(Re)construit Workshop.unity, câble les références et l'enregistre dans les Build Settings.</summary>
        public static Scene BuildWorkshopScene()
        {
            RugWashProjectBootstrapper.EnsureFolders();
            RugWashProjectBootstrapper.EnsureLayers();
            RugWashProjectBootstrapper.EnsureUniversalRenderPipeline();
            // NewScene(Single) décharge les assets non référencés : le contenu est chargé APRÈS,
            // sinon les références C# vers des assets fraîchement créés deviennent nulles.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var content = RugWashContentBuilder.EnsureContent();
            var meshes = new MeshStore(MeshStorePath);
            var sprites = new UISprites();
            ConfigureEnvironmentLighting();

            var environment = new GameObject("--- Environment").transform;
            var lighting = new GameObject("--- Lighting").transform;
            var gameplay = new GameObject("--- Gameplay").transform;
            var effects = new GameObject("--- Effects").transform;
            var uiRoot = new GameObject("--- UI").transform;

            var game = new GameObject("[Game]");
            game.transform.SetParent(gameplay, false);
            var gameState = game.AddComponent<GameStateManager>();
            var performance = game.AddComponent<PerformanceMonitor>();
            var audio = game.AddComponent<AudioManager>();
            Wire(audio, ("uiClick", Clip(RugWashAudioGenerator.UIClick)), ("uiConfirm", Clip(RugWashAudioGenerator.UIConfirm)));
            var bootstrap = game.AddComponent<GameBootstrap>();

            BuildRoom(environment, content, meshes);
            var washPad = BuildWashBay(environment, content, meshes, audio);
            BuildWaterStation(environment, content, meshes, audio);
            var shopCounter = BuildToolStorage(environment, content, meshes);
            var orderCounter = BuildOrderDesk(environment, content, meshes);
            BuildDryingArea(environment, content, meshes);
            BuildShelves(environment, content, meshes, audio);
            BuildLights(lighting, content, meshes);

            var rug = BuildRug(gameplay, content);
            var snapshots = BuildSnapshotCamera(gameplay, rug);

            var cleanPool = BuildCleanFeedbackPool(effects, content, audio);
            var player = BuildPlayer(gameplay, content, effects, gameState, audio, cleanPool,
                out var input, out var fps, out var toolController, out var interactor);

            var waterEffect = effects.gameObject.AddComponent<WaterEffectController>();
            WireList(waterEffect, "wetSurfaces", new Renderer[] { washPad });
            WireList(waterEffect, "wettingTools", new CleaningToolBase[]
            {
                player.GetComponentInChildren<WaterRinseTool>(true), player.GetComponentInChildren<SoapSprayerTool>(true),
            });

            var wallet = game.AddComponent<WalletManager>();
            var reputation = game.AddComponent<ReputationManager>();
            var upgrades = game.AddComponent<UpgradeManager>();
            Wire(upgrades, ("equipment", player.GetComponent<PlayerEquipment>()), ("tools", toolController));
            WireList(upgrades, "catalog", content.Upgrades);
            var shop = game.AddComponent<ShopManager>();
            Wire(shop, ("wallet", wallet), ("upgrades", upgrades), ("audioManager", audio));
            var orders = game.AddComponent<OrderManager>();
            Wire(orders, ("practiceRug", content.Oriental), ("rug", rug), ("snapshots", snapshots), ("gameState", gameState),
                ("wallet", wallet), ("reputation", reputation), ("audioManager", audio));
            WireList(orders, "orders", content.Orders);

            var ui = BuildUI(uiRoot, sprites, performance);

            var deskInteractable = orderCounter.AddComponent<PanelInteractable>();
            Wire(deskInteractable, ("orderBoard", ui.Board), ("orders", orders));
            SetInt(deskInteractable, "target", (int)PanelInteractable.Target.OrderBoard);
            var shopInteractable = shopCounter.AddComponent<PanelInteractable>();
            Wire(shopInteractable, ("shop", ui.Shop));
            SetInt(shopInteractable, "target", (int)PanelInteractable.Target.Shop);

            Wire(bootstrap,
                ("gameState", gameState),
                ("audioManager", audio),
                ("input", input),
                ("playerController", fps),
                ("toolController", toolController),
                ("interactor", interactor),
                ("rug", rug),
                ("snapshots", snapshots),
                ("hud", ui.Hud),
                ("toolbar", ui.Toolbar),
                ("pauseMenu", ui.Pause),
                ("inspection", ui.Inspection),
                ("orderBoard", ui.Board),
                ("shopUI", ui.Shop),
                ("results", ui.Results),
                ("orders", orders),
                ("wallet", wallet),
                ("reputation", reputation),
                ("upgrades", upgrades),
                ("shop", shop),
                ("debugOverlay", ui.Debug));
            WireList(bootstrap, "availableRugs", new[] { content.Oriental, content.Classic, content.Shag });

            if (!bootstrap.ValidateReferences(out string missing))
                throw new InvalidOperationException($"[RugWash] Scène Atelier incomplète, références manquantes : {missing}");
            var stations = Object.FindObjectsByType<ResourceStation>();
            foreach (ToolResource resource in new[] { ToolResource.Water, ToolResource.Soap, ToolResource.Recovery })
                if (Array.Find(stations, st => st.Resource == resource) == null)
                    throw new InvalidOperationException($"[RugWash] Poste manquant pour la ressource {resource}.");

            EditorSceneManager.SaveScene(scene, RugWashPaths.WorkshopScene);
            RugWashProjectBootstrapper.SyncBuildScenes();
            AssetDatabase.SaveAssets();
            Debug.Log($"[RugWash] Scène générée : {RugWashPaths.WorkshopScene}");
            return scene;
        }

        /// <summary>Scène minimale (caméra + titre) servant à réserver une entrée dans les Build Settings.</summary>
        public static void CreatePlaceholderScene(string path, string title)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.11f, 0.16f);
            cameraGo.AddComponent<AudioListener>();
            cameraGo.AddComponent<UniversalAdditionalCameraData>();

            var canvas = CreateCanvas("Canvas", null);
            var label = CreateRect("Title", canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1400f, 200f));
            AddText(label, title + "\n<size=28>Écran prévu en phase 4</size>", 72, TextAnchor.MiddleCenter, Color.white);
            label.GetComponent<Text>().supportRichText = true;
            CreateEventSystem(null);

            EditorSceneManager.SaveScene(scene, path);
        }

        // ------------------------------------------------------------------ Environnement

        static void ConfigureEnvironmentLighting()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.46f, 0.48f, 0.52f);
            RenderSettings.ambientEquatorColor = new Color(0.34f, 0.33f, 0.31f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.19f, 0.18f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.fog = false;
        }

        static void BuildRoom(Transform parent, RugWashContentBuilder.Content c, MeshStore meshes)
        {
            var room = new GameObject("Room").transform;
            room.SetParent(parent, false);
            float w = RoomHalfX * 2f + WallThickness * 2f, d = RoomHalfZ * 2f + WallThickness * 2f;

            CreateBox(meshes, "Floor", room, new Vector3(0f, -0.1f, 0f), new Vector3(w, 0.2f, d), c["Concrete"]);
            CreateBox(meshes, "Ceiling", room, new Vector3(0f, RoomHeight + 0.1f, 0f), new Vector3(w, 0.2f, d), c["Ceiling"]);
            CreateBox(meshes, "Wall_Back", room, new Vector3(0f, RoomHeight / 2f, RoomHalfZ + WallThickness / 2f), new Vector3(w, RoomHeight, WallThickness), c["Plaster"]);
            CreateBox(meshes, "Wall_Front", room, new Vector3(0f, RoomHeight / 2f, -RoomHalfZ - WallThickness / 2f), new Vector3(w, RoomHeight, WallThickness), c["Plaster"]);
            CreateBox(meshes, "Wall_Left", room, new Vector3(-RoomHalfX - WallThickness / 2f, RoomHeight / 2f, 0f), new Vector3(WallThickness, RoomHeight, d), c["Plaster"]);
            CreateBox(meshes, "Wall_Right", room, new Vector3(RoomHalfX + WallThickness / 2f, RoomHeight / 2f, 0f), new Vector3(WallThickness, RoomHeight, d), c["Plaster"]);

            // Soubassement peint (bas de mur lavable).
            const float band = 1.1f, inset = 0.012f;
            CreateBox(meshes, "Wainscot_Back", room, new Vector3(0f, band / 2f, RoomHalfZ - inset), new Vector3(RoomHalfX * 2f, band, 0.02f), c["Wainscot"], collider: false);
            CreateBox(meshes, "Wainscot_Front", room, new Vector3(0f, band / 2f, -RoomHalfZ + inset), new Vector3(RoomHalfX * 2f, band, 0.02f), c["Wainscot"], collider: false);
            CreateBox(meshes, "Wainscot_Left", room, new Vector3(-RoomHalfX + inset, band / 2f, 0f), new Vector3(0.02f, band, RoomHalfZ * 2f), c["Wainscot"], collider: false);
            CreateBox(meshes, "Wainscot_Right", room, new Vector3(RoomHalfX - inset, band / 2f, 0f), new Vector3(0.02f, band, RoomHalfZ * 2f), c["Wainscot"], collider: false);

            // Fenêtres hautes (lumière du jour) et porte d'entrée.
            for (int i = -1; i <= 1; i++)
            {
                var frame = CreateBox(meshes, $"Window_{i + 1}_Frame", room, new Vector3(i * 3.4f, 2.55f, RoomHalfZ - 0.03f), new Vector3(1.9f, 0.95f, 0.04f), c["DarkMetal"], collider: false);
                CreateBox(meshes, "Glass", frame.transform, new Vector3(0f, 0f, -0.012f), new Vector3(1.78f, 0.83f, 0.02f), c["Window"], collider: false);
                CreateBox(meshes, "Mullion", frame.transform, new Vector3(0f, 0f, -0.025f), new Vector3(0.04f, 0.83f, 0.02f), c["DarkMetal"], collider: false);
            }
            CreateBox(meshes, "Door", room, new Vector3(-3.2f, 1.05f, -RoomHalfZ + 0.03f), new Vector3(1.1f, 2.1f, 0.06f), c["DoorPaint"], collider: false);
            CreateBox(meshes, "Door_Handle", room, new Vector3(-2.8f, 1.0f, -RoomHalfZ + 0.08f), new Vector3(0.14f, 0.03f, 0.04f), c["Steel"], collider: false);
            CreateSign(room, "Sign_Exit", "SORTIE", new Vector3(-3.2f, 2.35f, -RoomHalfZ + 0.04f), Quaternion.Euler(0f, 180f, 0f), meshes, c);
        }

        static Renderer BuildWashBay(Transform parent, RugWashContentBuilder.Content c, MeshStore meshes, AudioManager audio)
        {
            var bay = new GameObject("WashBay").transform;
            bay.SetParent(parent, false);
            bay.localPosition = WashPadCenter;

            var pad = CreateBox(meshes, "WashPad", bay, new Vector3(0f, WashPadSize.y / 2f, 0f), WashPadSize, c["WashPad"]);
            float hx = WashPadSize.x / 2f, hz = WashPadSize.z / 2f;
            CreateBox(meshes, "Curb_Back", bay, new Vector3(0f, 0.07f, hz + 0.04f), new Vector3(WashPadSize.x + 0.16f, 0.14f, 0.08f), c["SafetyYellow"]);
            CreateBox(meshes, "Curb_Front", bay, new Vector3(0f, 0.07f, -hz - 0.04f), new Vector3(WashPadSize.x + 0.16f, 0.14f, 0.08f), c["SafetyYellow"]);
            CreateBox(meshes, "Curb_Left", bay, new Vector3(-hx - 0.04f, 0.07f, 0f), new Vector3(0.08f, 0.14f, WashPadSize.z), c["SafetyYellow"]);
            CreateBox(meshes, "Curb_Right", bay, new Vector3(hx + 0.04f, 0.07f, 0f), new Vector3(0.08f, 0.14f, WashPadSize.z), c["SafetyYellow"]);

            // Caniveau d'évacuation.
            var drain = CreateBox(meshes, "Drain_Channel", bay, new Vector3(hx - 0.22f, WashPadSize.y + 0.002f, 0f), new Vector3(0.2f, 0.006f, WashPadSize.z - 0.3f), c["Grate"]);
            CreateBox(meshes, "Drain_Frame", bay, new Vector3(hx - 0.22f, WashPadSize.y + 0.001f, 0f), new Vector3(0.24f, 0.004f, WashPadSize.z - 0.26f), c["DarkMetal"], collider: false);
            CreateSign(parent, "Sign_WashBay", "AIRE DE LAVAGE", new Vector3(0f, 2.9f, RoomHalfZ - 0.04f), Quaternion.identity, meshes, c);

            // Le caniveau sert de point de vidange de la cuve de l'extracteur.
            var gush = CreateParticles("DrainGush", drain.transform, c["Droplet"], new ParticleConfig
            {
                Lifetime = new Vector2(0.3f, 0.6f), Speed = new Vector2(0.3f, 0.9f), Size = new Vector2(0.02f, 0.05f), Color = Color.white,
                Gravity = 0.8f, MaxParticles = 150, Shape = ParticleSystemShapeType.Box, Radius = 0.05f, Rate = 120f, Loop = false,
            });
            gush.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var drainStation = drain.AddComponent<ResourceStation>();
            Wire(drainStation, ("effect", gush), ("audioManager", audio), ("sound", Clip(RugWashAudioGenerator.Drain)));
            SetInt(drainStation, "resource", (int)ToolResource.Recovery);
            return pad.GetComponent<Renderer>();
        }

        static ResourceStation BuildWaterStation(Transform parent, RugWashContentBuilder.Content c, MeshStore meshes, AudioManager audio)
        {
            var station = new GameObject("WaterStation").transform;
            station.SetParent(parent, false);
            station.localPosition = new Vector3(-RoomHalfX + 0.55f, 0f, 2.2f);

            CreateBox(meshes, "Base", station, new Vector3(0f, 0.2f, 0f), new Vector3(0.9f, 0.4f, 1.6f), c["DarkMetal"]);
            CreatePrimitive(PrimitiveType.Cylinder, "Tank", station, new Vector3(0f, 0.95f, 0.35f), new Vector3(0.7f, 0.55f, 0.7f), c["BluePlastic"]);
            CreatePrimitive(PrimitiveType.Cylinder, "Tank_Cap", station, new Vector3(0f, 1.52f, 0.35f), new Vector3(0.22f, 0.03f, 0.22f), c["Rubber"]);
            CreateBox(meshes, "Sink", station, new Vector3(0.05f, 0.78f, -0.45f), new Vector3(0.7f, 0.12f, 0.6f), c["Steel"], collider: false);
            CreatePrimitive(PrimitiveType.Cylinder, "Pipe", station, new Vector3(-0.32f, 1.2f, -0.45f), new Vector3(0.05f, 0.42f, 0.05f), c["Steel"]);
            CreatePrimitive(PrimitiveType.Cylinder, "Faucet", station, new Vector3(-0.17f, 1.58f, -0.45f), new Vector3(0.05f, 0.16f, 0.05f), c["Steel"],
                rotation: Quaternion.Euler(0f, 0f, 90f));
            CreatePrimitive(PrimitiveType.Cylinder, "Hose", station, new Vector3(0.38f, 0.75f, 0.35f), new Vector3(0.04f, 0.4f, 0.04f), c["Rubber"]);

            var collider = station.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.85f, 0f);
            collider.size = new Vector3(0.9f, 1.7f, 1.6f);

            var pour = CreateParticles("PourEffect", station, c["WaterJet"], new ParticleConfig
            {
                Lifetime = new Vector2(0.35f, 0.5f), Speed = new Vector2(0.4f, 0.8f), Size = new Vector2(0.02f, 0.04f),
                Color = Color.white, Gravity = 1f, MaxParticles = 200, Shape = ParticleSystemShapeType.Cone, Angle = 6f,
                Radius = 0.01f, Rate = 140f, Loop = false, PlayOnAwake = false,
            });
            pour.transform.localPosition = new Vector3(-0.05f, 1.55f, -0.45f);
            pour.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var refill = station.gameObject.AddComponent<ResourceStation>();
            Wire(refill, ("effect", pour), ("audioManager", audio), ("sound", Clip(RugWashAudioGenerator.Refill)));
            SetInt(refill, "resource", (int)ToolResource.Water);
            CreateSign(parent, "Sign_Water", "POSTE D'EAU", new Vector3(-RoomHalfX + 0.04f, 2.2f, 2.2f), Quaternion.Euler(0f, -90f, 0f), meshes, c);
            return refill;
        }

        static GameObject BuildToolStorage(Transform parent, RugWashContentBuilder.Content c, MeshStore meshes)
        {
            var storage = new GameObject("ToolStorage").transform;
            storage.SetParent(parent, false);
            storage.localPosition = new Vector3(3.6f, 0f, RoomHalfZ - 0.3f);

            CreateBox(meshes, "Cabinet", storage, new Vector3(0f, 0.45f, 0f), new Vector3(2.2f, 0.9f, 0.55f), c["DarkMetal"]);
            CreateBox(meshes, "Cabinet_Top", storage, new Vector3(0f, 0.915f, 0f), new Vector3(2.26f, 0.03f, 0.6f), c["Wood"], collider: false);
            CreateBox(meshes, "Pegboard", storage, new Vector3(0f, 1.65f, 0.24f), new Vector3(2.2f, 1.1f, 0.03f), c["Pegboard"], collider: false);
            for (int i = 0; i < 4; i++)
            {
                float x = -0.75f + i * 0.5f;
                CreatePrimitive(PrimitiveType.Cylinder, $"Hung_Handle_{i}", storage, new Vector3(x, 1.75f, 0.18f), new Vector3(0.03f, 0.28f, 0.03f), c["Wood"]);
                CreateBox(meshes, $"Hung_Head_{i}", storage, new Vector3(x, 1.42f, 0.18f), new Vector3(0.24f, 0.07f, 0.06f), i % 2 == 0 ? c["Bristle"] : c["GreenPlastic"], collider: false);
            }
            CreatePrimitive(PrimitiveType.Cylinder, "Bucket_A", storage, new Vector3(-0.7f, 1.08f, -0.05f), new Vector3(0.28f, 0.14f, 0.28f), c["RedPlastic"]);
            CreatePrimitive(PrimitiveType.Cylinder, "Bucket_B", storage, new Vector3(0.75f, 1.08f, -0.05f), new Vector3(0.28f, 0.14f, 0.28f), c["BluePlastic"]);
            CreateSign(parent, "Sign_Tools", "MATÉRIEL & BOUTIQUE", new Vector3(3.6f, 2.45f, RoomHalfZ - 0.04f), Quaternion.identity, meshes, c);
            return storage.gameObject;
        }

        static GameObject BuildOrderDesk(Transform parent, RugWashContentBuilder.Content c, MeshStore meshes)
        {
            var desk = new GameObject("OrderDesk").transform;
            desk.SetParent(parent, false);
            desk.localPosition = new Vector3(4.3f, 0f, -3.7f);

            CreateBox(meshes, "Counter", desk, new Vector3(0f, 0.5f, 0f), new Vector3(2.4f, 1f, 0.7f), c["DoorPaint"]);
            CreateBox(meshes, "Counter_Top", desk, new Vector3(0f, 1.02f, 0f), new Vector3(2.5f, 0.04f, 0.8f), c["Wood"], collider: false);
            CreateBox(meshes, "Monitor", desk, new Vector3(0.6f, 1.3f, 0.15f), new Vector3(0.55f, 0.36f, 0.04f), c["Rubber"], collider: false);
            CreateBox(meshes, "Monitor_Screen", desk, new Vector3(0.6f, 1.3f, 0.128f), new Vector3(0.5f, 0.31f, 0.005f), c["ScreenGlow"], collider: false);
            CreateBox(meshes, "Clipboard", desk, new Vector3(-0.5f, 1.05f, -0.1f), new Vector3(0.24f, 0.01f, 0.32f), c["Cardboard"], collider: false);
            for (int i = 0; i < 3; i++)
                CreateBox(meshes, $"RolledRug_{i}", desk, new Vector3(-1.5f - i * 0.02f, 0.18f + i * 0.33f, 0.6f), new Vector3(1.5f, 0.3f, 0.3f),
                    i == 1 ? c["RedPlastic"] : c["Cardboard"]);
            CreateSign(parent, "Sign_Orders", "RÉCEPTION DES COMMANDES", new Vector3(4.3f, 2.45f, -RoomHalfZ + 0.04f), Quaternion.Euler(0f, 180f, 0f), meshes, c);
            return desk.gameObject;
        }

        static void BuildDryingArea(Transform parent, RugWashContentBuilder.Content c, MeshStore meshes)
        {
            var area = new GameObject("DryingArea").transform;
            area.SetParent(parent, false);
            area.localPosition = new Vector3(RoomHalfX - 0.7f, 0f, 0.8f);

            for (int i = -1; i <= 1; i += 2)
                CreateBox(meshes, $"Post_{i}", area, new Vector3(0f, 1.1f, i * 1.3f), new Vector3(0.08f, 2.2f, 0.08f), c["Steel"]);
            CreateBox(meshes, "Bar_High", area, new Vector3(0f, 2.15f, 0f), new Vector3(0.06f, 0.06f, 2.7f), c["Steel"], collider: false);
            CreateBox(meshes, "Bar_Low", area, new Vector3(0.25f, 1.6f, 0f), new Vector3(0.05f, 0.05f, 2.7f), c["Steel"], collider: false);
            CreateBox(meshes, "Drip_Tray", area, new Vector3(0f, 0.03f, 0f), new Vector3(0.8f, 0.06f, 2.8f), c["DarkMetal"]);
            CreateSign(parent, "Sign_Drying", "SÉCHAGE", new Vector3(RoomHalfX - 0.04f, 2.6f, 0.8f), Quaternion.Euler(0f, 90f, 0f), meshes, c);
        }

        static void BuildShelves(Transform parent, RugWashContentBuilder.Content c, MeshStore meshes, AudioManager audio)
        {
            var shelf = new GameObject("Shelves").transform;
            shelf.SetParent(parent, false);
            shelf.localPosition = new Vector3(-RoomHalfX + 0.35f, 0f, -2.6f);

            for (int i = -1; i <= 1; i += 2)
                CreateBox(meshes, $"Upright_{i}", shelf, new Vector3(0f, 1f, i * 0.9f), new Vector3(0.5f, 2f, 0.05f), c["DarkMetal"]);
            Material[] bottles = { c["BluePlastic"], c["GreenPlastic"], c["RedPlastic"], c["SafetyYellow"] };
            for (int level = 0; level < 4; level++)
            {
                float y = 0.15f + level * 0.55f;
                CreateBox(meshes, $"Board_{level}", shelf, new Vector3(0f, y, 0f), new Vector3(0.5f, 0.03f, 1.8f), c["Wood"]);
                for (int b = 0; b < 5; b++)
                {
                    if ((level + b) % 3 == 0) continue;
                    float z = -0.7f + b * 0.35f;
                    CreatePrimitive(PrimitiveType.Cylinder, $"Bottle_{level}_{b}", shelf, new Vector3(0f, y + 0.14f, z),
                        new Vector3(0.1f, 0.12f, 0.1f), bottles[(level + b) % bottles.Length]);
                }
            }

            // Poste produits : bidon doseur de détergent.
            var soap = new GameObject("SoapStation").transform;
            soap.SetParent(parent, false);
            soap.localPosition = new Vector3(-RoomHalfX + 0.45f, 0f, -0.9f);
            CreateBox(meshes, "Stand", soap, new Vector3(0f, 0.35f, 0f), new Vector3(0.6f, 0.7f, 0.6f), c["DarkMetal"]);
            CreatePrimitive(PrimitiveType.Cylinder, "Drum", soap, new Vector3(0f, 1.05f, 0f), new Vector3(0.42f, 0.35f, 0.42f), c["GreenPlastic"]);
            CreatePrimitive(PrimitiveType.Cylinder, "Pump", soap, new Vector3(0f, 1.48f, 0f), new Vector3(0.06f, 0.1f, 0.06f), c["WhitePlastic"]);
            CreateBox(meshes, "Spout", soap, new Vector3(0.12f, 1.55f, 0f), new Vector3(0.18f, 0.03f, 0.03f), c["WhitePlastic"], collider: false);
            var soapCollider = soap.gameObject.AddComponent<BoxCollider>();
            soapCollider.center = new Vector3(0f, 0.8f, 0f);
            soapCollider.size = new Vector3(0.6f, 1.6f, 0.6f);
            var foamDrip = CreateParticles("FoamDrip", soap, c["Foam"], new ParticleConfig
            {
                Lifetime = new Vector2(0.4f, 0.7f), Speed = new Vector2(0.1f, 0.3f), Size = new Vector2(0.02f, 0.05f), Color = Color.white,
                Gravity = 0.6f, MaxParticles = 60, Shape = ParticleSystemShapeType.Cone, Angle = 10f, Radius = 0.01f, Rate = 50f, Loop = false,
            });
            foamDrip.transform.localPosition = new Vector3(0.2f, 1.53f, 0f);
            foamDrip.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var soapStation = soap.gameObject.AddComponent<ResourceStation>();
            Wire(soapStation, ("effect", foamDrip), ("audioManager", audio), ("sound", Clip(RugWashAudioGenerator.Refill)));
            SetInt(soapStation, "resource", (int)ToolResource.Soap);
            CreateSign(parent, "Sign_Soap", "PRODUITS", new Vector3(-RoomHalfX + 0.04f, 2.2f, -0.9f), Quaternion.Euler(0f, -90f, 0f), meshes, c);
        }

        static void CreateSign(Transform parent, string name, string text, Vector3 position, Quaternion rotation, MeshStore meshes,
            RugWashContentBuilder.Content c)
        {
            var sign = new GameObject(name).transform;
            sign.SetParent(parent, false);
            sign.SetPositionAndRotation(position, rotation);
            float width = 0.18f + text.Length * 0.085f;
            CreateBox(meshes, "Panel", sign, new Vector3(0f, 0f, -0.01f), new Vector3(width, 0.24f, 0.02f), c["Signage"], collider: false);

            var label = new GameObject("Text");
            label.transform.SetParent(sign, false);
            label.transform.localPosition = new Vector3(0f, 0f, -0.022f);
            var mesh = label.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.font = UIFont;
            mesh.fontSize = 64;
            mesh.characterSize = 0.022f;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = new Color(0.95f, 0.93f, 0.85f);
            label.GetComponent<MeshRenderer>().sharedMaterial = UIFont.material;
        }

        static void BuildLights(Transform parent, RugWashContentBuilder.Content c, MeshStore meshes)
        {
            // Projecteur principal sur l'aire de lavage (ombres douces).
            var spotGo = new GameObject("WashBay_Spot");
            spotGo.transform.SetParent(parent, false);
            spotGo.transform.SetPositionAndRotation(new Vector3(WashPadCenter.x, RoomHeight - 0.15f, WashPadCenter.z - 0.3f), Quaternion.Euler(80f, 0f, 0f));
            var spot = spotGo.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.spotAngle = 115f;
            spot.innerSpotAngle = 70f;
            spot.range = 7f;
            spot.intensity = 6f;
            spot.color = new Color(1f, 0.96f, 0.9f);
            spot.shadows = LightShadows.Soft;
            spot.shadowStrength = 0.75f;
            spotGo.AddComponent<UniversalAdditionalLightData>();
            CreateBox(meshes, "Spot_Housing", spotGo.transform, new Vector3(0f, 0f, -0.08f), new Vector3(0.6f, 0.6f, 0.1f), c["DarkMetal"], collider: false);

            // Plafonniers d'ambiance.
            int index = 0;
            foreach (float x in new[] { -3.4f, 3.4f })
            foreach (float z in new[] { -2.6f, 2.6f })
            {
                var fixture = new GameObject($"CeilingLamp_{index++}");
                fixture.transform.SetParent(parent, false);
                fixture.transform.localPosition = new Vector3(x, RoomHeight - 0.05f, z);
                CreateBox(meshes, "Housing", fixture.transform, Vector3.zero, new Vector3(1.4f, 0.08f, 0.36f), c["DarkMetal"], collider: false);
                CreateBox(meshes, "Diffuser", fixture.transform, new Vector3(0f, -0.045f, 0f), new Vector3(1.3f, 0.01f, 0.28f), c["LampPanel"], collider: false);

                var lightGo = new GameObject("Light");
                lightGo.transform.SetParent(fixture.transform, false);
                lightGo.transform.localPosition = new Vector3(0f, -0.3f, 0f);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 8f;
                light.intensity = 2.2f;
                light.color = new Color(1f, 0.95f, 0.88f);
                light.shadows = LightShadows.None;
                lightGo.AddComponent<UniversalAdditionalLightData>();
            }

            var probeGo = new GameObject("Reflection Probe");
            probeGo.transform.SetParent(parent, false);
            probeGo.transform.localPosition = new Vector3(0f, RoomHeight / 2f, 0f);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.size = new Vector3(RoomHalfX * 2f, RoomHeight, RoomHalfZ * 2f);
            probe.boxProjection = true;
            probe.resolution = 128;
            probe.hdr = true;

            var volumeGo = new GameObject("Global Volume");
            volumeGo.transform.SetParent(parent, false);
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = GetOrCreateWorkshopVolumeProfile();
        }

        static VolumeProfile GetOrCreateWorkshopVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(RugWashPaths.WorkshopVolumeProfile);
            if (profile != null)
                return profile;

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, RugWashPaths.WorkshopVolumeProfile);
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.1f);
            bloom.intensity.Override(0.25f);
            var colorAdjustments = profile.Add<ColorAdjustments>(true);
            colorAdjustments.postExposure.Override(0.2f);
            colorAdjustments.contrast.Override(8f);
            profile.Add<Vignette>(true).intensity.Override(0.18f);
            foreach (var component in profile.components)
            {
                component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        // ------------------------------------------------------------------ Tapis

        static RugCleaningController BuildRug(Transform parent, RugWashContentBuilder.Content c)
        {
            var go = new GameObject("Rug");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = WashPadCenter + Vector3.up * WashPadSize.y;
            go.layer = RugWashPaths.RugLayer;

            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            var collider = go.AddComponent<BoxCollider>();
            var dirtRenderer = go.AddComponent<RugDirtRenderer>();
            var rug = go.AddComponent<RugCleaningController>();

            // Aperçu en mode édition : le tapis réel (maillage, saleté) est créé au lancement.
            var definition = c.Oriental;
            filter.sharedMesh = PreviewRugMesh(definition);
            c["RugSurface"].SetTexture("_BaseMap", definition.patternTexture);
            renderer.sharedMaterials = new[] { c["RugSurface"], c["RugFringe"] };
            collider.size = new Vector3(definition.sizeMeters.x, Mathf.Max(0.01f, definition.thickness), definition.sizeMeters.y);
            collider.center = new Vector3(0f, definition.thickness / 2f, 0f);

            Wire(dirtRenderer, ("targetRenderer", renderer), ("rugMaterialTemplate", c["RugSurface"]), ("fringeMaterialTemplate", c["RugFringe"]));
            Wire(rug, ("definition", definition), ("dirtLibrary", c.DirtLibrary), ("dirtRenderer", dirtRenderer),
                ("meshFilter", filter), ("surfaceCollider", collider));
            return rug;
        }

        static Mesh PreviewRugMesh(RugDefinition definition)
        {
            const string path = RugWashPaths.Root + "/Prefabs/RugPreview_Mesh.asset";
            AssetDatabase.DeleteAsset(path);
            var mesh = RugMeshBuilder.Build(definition.sizeMeters, definition.thickness, definition.hasFringe);
            mesh.name = "RugPreview";
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static RugSnapshotService BuildSnapshotCamera(Transform parent, RugCleaningController rug)
        {
            var go = new GameObject("RugSnapshotCamera");
            go.transform.SetParent(parent, false);
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.cullingMask = 1 << RugWashPaths.RugLayer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.1f, 0.11f, 0.12f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 5f;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            var service = go.AddComponent<RugSnapshotService>();
            Wire(service, ("rug", rug), ("snapshotCamera", camera));
            return service;
        }

        // ------------------------------------------------------------------ Joueur et outils

        static GameObject BuildPlayer(Transform parent, RugWashContentBuilder.Content c, Transform effects, GameStateManager gameState,
            AudioManager audio, CleaningVFXPool cleanPool,
            out PlayerInputReader input, out FirstPersonController fps, out PlayerToolController toolController, out PlayerInteractor interactor)
        {
            var player = new GameObject("Player");
            player.transform.SetParent(parent, false);
            player.transform.SetPositionAndRotation(new Vector3(0f, 0.02f, -2.3f), Quaternion.identity);
            player.layer = RugWashPaths.PlayerLayer;

            var controller = player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.3f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.stepOffset = 0.3f;
            controller.slopeLimit = 50f;
            controller.skinWidth = 0.04f;

            var pivot = new GameObject("CameraPivot").transform;
            pivot.SetParent(player.transform, false);
            pivot.localPosition = new Vector3(0f, 1.62f, 0f);
            pivot.localRotation = Quaternion.Euler(22f, 0f, 0f);

            var cameraGo = new GameObject("PlayerCamera") { tag = "MainCamera" };
            cameraGo.transform.SetParent(pivot, false);
            var camera = cameraGo.AddComponent<Camera>();
            camera.fieldOfView = 70f;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 60f;
            cameraGo.AddComponent<AudioListener>();
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            input = player.AddComponent<PlayerInputReader>();
            var equipment = player.AddComponent<PlayerEquipment>();
            fps = player.AddComponent<FirstPersonController>();
            Wire(fps, ("input", input), ("cameraPivot", pivot));

            // Indicateur de zone ciblée.
            var ring = CreatePrimitive(PrimitiveType.Quad, "TargetIndicator", effects, Vector3.zero, Vector3.one * 0.3f, c["TargetRing"]);
            DisableShadows(ring);
            var indicator = ring.AddComponent<ToolTargetIndicator>();
            Wire(indicator, ("ringRenderer", ring.GetComponent<MeshRenderer>()));

            // Outils : logique et son sous le joueur, modèles tenus sous la caméra.
            var toolsRoot = new GameObject("Tools").transform;
            toolsRoot.SetParent(player.transform, false);
            var ctx = new ToolBuildContext { Root = toolsRoot, Camera = cameraGo.transform, Content = c, Effects = effects, Audio = audio, CleanPool = cleanPool };
            var tools = new CleaningToolBase[]
            {
                BuildVacuum(ctx), BuildSoap(ctx), BuildBrush(ctx), BuildRinse(ctx), BuildExtractor(ctx),
            };

            toolController = player.AddComponent<PlayerToolController>();
            Wire(toolController, ("playerCamera", camera), ("input", input), ("equipment", equipment), ("gameState", gameState),
                ("targetIndicator", indicator));
            WireList(toolController, "tools", tools);
            int aimMask = ~((1 << RugWashPaths.PlayerLayer) | (1 << 2) | (1 << 5));
            SetInt(toolController, "aimMask", aimMask);

            interactor = player.AddComponent<PlayerInteractor>();
            Wire(interactor, ("playerCamera", camera), ("input", input), ("equipment", equipment), ("gameState", gameState));
            SetInt(interactor, "mask", aimMask);
            return player;
        }

        sealed class ToolBuildContext
        {
            public Transform Root, Camera, Effects;
            public RugWashContentBuilder.Content Content;
            public AudioManager Audio;
            public CleaningVFXPool CleanPool;
        }

        static AudioClip Clip(string path) => AssetDatabase.LoadAssetAtPath<AudioClip>(path);

        static Transform CreateViewModel(ToolBuildContext ctx, string name, Vector3 position, Vector3 euler)
        {
            var model = new GameObject($"ViewModel_{name}").transform;
            model.SetParent(ctx.Camera, false);
            model.localPosition = position;
            model.localRotation = Quaternion.Euler(euler);
            return model;
        }

        static void FinishViewModel(Transform model)
        {
            SetLayerRecursively(model.gameObject, RugWashPaths.PlayerLayer);
            DisableShadows(model.gameObject);
        }

        /// <summary>Crée l'objet logique de l'outil avec sa source audio en boucle.</summary>
        static T CreateTool<T>(ToolBuildContext ctx, string name, ToolDefinition definition, Transform model, string loopClip,
            float baseVolume, float contactBoost, float contactPitch) where T : CleaningToolBase
        {
            var go = new GameObject(name);
            go.transform.SetParent(ctx.Root, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            var toolAudio = go.AddComponent<CleaningAudioController>();
            Wire(toolAudio, ("audioManager", ctx.Audio), ("loop", Clip(loopClip)));
            var serialized = new SerializedObject(toolAudio);
            serialized.FindProperty("baseVolume").floatValue = baseVolume;
            serialized.FindProperty("contactVolumeBoost").floatValue = contactBoost;
            serialized.FindProperty("contactPitchShift").floatValue = contactPitch;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var tool = go.AddComponent<T>();
            Wire(tool, ("definition", definition), ("viewModel", model), ("toolAudio", toolAudio), ("cleanFeedback", ctx.CleanPool));
            return tool;
        }

        static VacuumTool BuildVacuum(ToolBuildContext ctx)
        {
            var c = ctx.Content;
            var model = CreateViewModel(ctx, "Vacuum", new Vector3(0.28f, -0.34f, 0.48f), new Vector3(6f, -10f, 0f));
            CreatePrimitive(PrimitiveType.Cylinder, "Wand", model, new Vector3(0f, 0f, 0.2f), new Vector3(0.04f, 0.3f, 0.04f), c["Chrome"],
                rotation: Quaternion.Euler(90f, 0f, 0f));
            CreatePrimitive(PrimitiveType.Cube, "Grip", model, new Vector3(0f, -0.05f, -0.08f), new Vector3(0.05f, 0.12f, 0.08f), c["RedPlastic"]);
            var nozzle = CreatePrimitive(PrimitiveType.Cube, "Nozzle", model, new Vector3(0f, -0.04f, 0.52f), new Vector3(0.26f, 0.05f, 0.08f), c["Rubber"]).transform;
            CreatePrimitive(PrimitiveType.Cube, "Nozzle_Lip", model, new Vector3(0f, -0.07f, 0.52f), new Vector3(0.27f, 0.012f, 0.09f), c["RedPlastic"]);
            FinishViewModel(model);

            var suction = CreateParticles("VacuumDust", ctx.Effects, c["Dust"], new ParticleConfig
            {
                Lifetime = new Vector2(0.15f, 0.3f), Speed = new Vector2(1.5f, 3f), Size = new Vector2(0.01f, 0.025f), Color = Color.white,
                Gravity = 0f, MaxParticles = 160, Shape = ParticleSystemShapeType.Cone, Angle = 25f, Radius = 0.12f, Rate = 0f,
                Loop = true, PlayOnAwake = true,
            });
            var tool = CreateTool<VacuumTool>(ctx, "Vacuum", c.Vacuum, model, RugWashAudioGenerator.VacuumLoop, 0.45f, 0.2f, -0.06f);
            Wire(tool, ("suctionParticles", suction), ("nozzle", nozzle));
            return tool;
        }

        static SoapSprayerTool BuildSoap(ToolBuildContext ctx)
        {
            var c = ctx.Content;
            var model = CreateViewModel(ctx, "Soap", new Vector3(0.27f, -0.27f, 0.42f), new Vector3(4f, -8f, 0f));
            CreatePrimitive(PrimitiveType.Cylinder, "Bottle", model, new Vector3(0f, -0.04f, 0f), new Vector3(0.09f, 0.08f, 0.09f), c["GreenPlastic"]);
            CreatePrimitive(PrimitiveType.Cube, "Head", model, new Vector3(0f, 0.06f, 0.03f), new Vector3(0.05f, 0.05f, 0.1f), c["WhitePlastic"]);
            CreatePrimitive(PrimitiveType.Cube, "Trigger", model, new Vector3(0f, 0.02f, 0.07f), new Vector3(0.015f, 0.05f, 0.02f), c["WhitePlastic"]);
            FinishViewModel(model);

            var spray = CreateParticles("SoapMist", model, c["SoapMist"], new ParticleConfig
            {
                Lifetime = new Vector2(0.3f, 0.45f), Speed = new Vector2(6f, 8f), Size = new Vector2(0.015f, 0.03f), Color = Color.white,
                Gravity = 0.2f, MaxParticles = 220, Shape = ParticleSystemShapeType.Cone, Angle = 9f, Radius = 0.005f, Rate = 0f,
                Loop = true, PlayOnAwake = true, SizeGrowth = 2.2f,
            });
            spray.transform.localPosition = new Vector3(0f, 0.065f, 0.09f);
            var puffs = CreateParticles("FoamPuffs", ctx.Effects, c["Foam"], new ParticleConfig
            {
                Lifetime = new Vector2(0.5f, 0.9f), Speed = new Vector2(0.05f, 0.25f), Size = new Vector2(0.03f, 0.07f), Color = Color.white,
                Gravity = 0f, MaxParticles = 80, Shape = ParticleSystemShapeType.Circle, Radius = 0.15f, Rate = 0f, Loop = true, PlayOnAwake = true,
            });
            var tool = CreateTool<SoapSprayerTool>(ctx, "Soap", c.Soap, model, RugWashAudioGenerator.SoapLoop, 0.4f, 0.1f, 0f);
            Wire(tool, ("sprayParticles", spray), ("foamPuffs", puffs));
            return tool;
        }

        static BrushTool BuildBrush(ToolBuildContext ctx)
        {
            var c = ctx.Content;
            var model = CreateViewModel(ctx, "Brush", new Vector3(0.3f, -0.3f, 0.5f), new Vector3(8f, -12f, 0f));
            CreatePrimitive(PrimitiveType.Cylinder, "Handle", model, new Vector3(0f, 0f, 0.1f), new Vector3(0.035f, 0.32f, 0.035f), c["Wood"],
                rotation: Quaternion.Euler(90f, 0f, 0f));
            CreatePrimitive(PrimitiveType.Cube, "Head", model, new Vector3(0f, -0.02f, 0.44f), new Vector3(0.24f, 0.05f, 0.1f), c["GreenPlastic"]);
            CreatePrimitive(PrimitiveType.Cube, "Bristles", model, new Vector3(0f, -0.065f, 0.44f), new Vector3(0.22f, 0.045f, 0.09f), c["Bristle"]);
            FinishViewModel(model);

            var debris = CreateParticles("BrushDebris", ctx.Effects, c["Debris"], new ParticleConfig
            {
                Lifetime = new Vector2(0.35f, 0.7f), Speed = new Vector2(0.3f, 1.1f), Size = new Vector2(0.008f, 0.02f),
                Color = Color.white, Gravity = 1f, MaxParticles = 160, Shape = ParticleSystemShapeType.Cone, Angle = 55f,
                Radius = 0.08f, Rate = 0f, Loop = true, PlayOnAwake = true,
            });
            var tool = CreateTool<BrushTool>(ctx, "Brush", c.Brush, model, RugWashAudioGenerator.BrushLoop, 0.25f, 0.35f, 0.05f);
            Wire(tool, ("debrisParticles", debris));
            return tool;
        }

        static WaterRinseTool BuildRinse(ToolBuildContext ctx)
        {
            var c = ctx.Content;
            var model = CreateViewModel(ctx, "Rinse", new Vector3(0.26f, -0.26f, 0.42f), new Vector3(4f, -6f, 0f));
            CreatePrimitive(PrimitiveType.Cube, "Grip", model, new Vector3(0f, -0.07f, -0.02f), new Vector3(0.04f, 0.13f, 0.06f), c["Rubber"],
                rotation: Quaternion.Euler(-15f, 0f, 0f));
            CreatePrimitive(PrimitiveType.Cube, "Body", model, new Vector3(0f, 0f, 0.04f), new Vector3(0.06f, 0.07f, 0.16f), c["SafetyYellow"]);
            CreatePrimitive(PrimitiveType.Cylinder, "Lance", model, new Vector3(0f, 0.01f, 0.36f), new Vector3(0.022f, 0.24f, 0.022f), c["Steel"],
                rotation: Quaternion.Euler(90f, 0f, 0f));
            CreatePrimitive(PrimitiveType.Cylinder, "Nozzle", model, new Vector3(0f, 0.01f, 0.61f), new Vector3(0.04f, 0.025f, 0.04f), c["DarkMetal"],
                rotation: Quaternion.Euler(90f, 0f, 0f));
            FinishViewModel(model);

            var jet = CreateParticles("WaterJet", model, c["WaterJet"], new ParticleConfig
            {
                Lifetime = new Vector2(0.26f, 0.32f), Speed = new Vector2(13f, 15f), Size = new Vector2(0.018f, 0.032f),
                Color = Color.white, Gravity = 0.3f, MaxParticles = 600, Shape = ParticleSystemShapeType.Cone, Angle = 2f,
                Radius = 0.004f, Rate = 0f, Loop = true, PlayOnAwake = true, SizeGrowth = 2.2f, Stretch = 0.035f,
            });
            jet.transform.localPosition = new Vector3(0f, 0.01f, 0.64f);
            var splash = CreateParticles("WaterSplash", ctx.Effects, c["Splash"], new ParticleConfig
            {
                Lifetime = new Vector2(0.25f, 0.5f), Speed = new Vector2(1f, 2.8f), Size = new Vector2(0.015f, 0.04f),
                Color = Color.white, Gravity = 1.3f, MaxParticles = 300, Shape = ParticleSystemShapeType.Hemisphere,
                Radius = 0.05f, Rate = 0f, Loop = true, PlayOnAwake = true, Stretch = 0.02f,
            });
            var tool = CreateTool<WaterRinseTool>(ctx, "Rinse", c.Rinse, model, RugWashAudioGenerator.RinseLoop, 0.45f, 0.15f, -0.05f);
            Wire(tool, ("jetParticles", jet), ("splashParticles", splash));
            SetFloat(tool, "jetRate", 520f);
            return tool;
        }

        static WaterExtractorTool BuildExtractor(ToolBuildContext ctx)
        {
            var c = ctx.Content;
            var model = CreateViewModel(ctx, "Extractor", new Vector3(0.27f, -0.34f, 0.46f), new Vector3(8f, -10f, 0f));
            CreatePrimitive(PrimitiveType.Cylinder, "Wand", model, new Vector3(0f, 0f, 0.2f), new Vector3(0.045f, 0.3f, 0.045f), c["Steel"],
                rotation: Quaternion.Euler(90f, 0f, 0f));
            CreatePrimitive(PrimitiveType.Cube, "Grip", model, new Vector3(0f, -0.05f, -0.08f), new Vector3(0.05f, 0.12f, 0.08f), c["BluePlastic"]);
            var nozzle = CreatePrimitive(PrimitiveType.Cube, "Head", model, new Vector3(0f, -0.045f, 0.52f), new Vector3(0.3f, 0.05f, 0.06f), c["BluePlastic"]).transform;
            CreatePrimitive(PrimitiveType.Cube, "Slot", model, new Vector3(0f, -0.072f, 0.52f), new Vector3(0.28f, 0.01f, 0.02f), c["Rubber"]);
            FinishViewModel(model);

            var droplets = CreateParticles("ExtractorDroplets", ctx.Effects, c["Droplet"], new ParticleConfig
            {
                Lifetime = new Vector2(0.12f, 0.25f), Speed = new Vector2(1.8f, 3.2f), Size = new Vector2(0.01f, 0.02f), Color = Color.white,
                Gravity = 0f, MaxParticles = 180, Shape = ParticleSystemShapeType.Cone, Angle = 20f, Radius = 0.1f, Rate = 0f,
                Loop = true, PlayOnAwake = true, Stretch = 0.02f,
            });
            var tool = CreateTool<WaterExtractorTool>(ctx, "Extractor", c.Extractor, model, RugWashAudioGenerator.ExtractorLoop, 0.45f, 0.25f, -0.1f);
            Wire(tool, ("suctionDroplets", droplets), ("nozzle", nozzle));
            return tool;
        }

        static CleaningVFXPool BuildCleanFeedbackPool(Transform effects, RugWashContentBuilder.Content c, AudioManager audio)
        {
            var root = new GameObject("CleanFeedbackPool");
            root.transform.SetParent(effects, false);
            var template = CreateParticles("CleanSparkle", root.transform, c["Sparkle"], new ParticleConfig
            {
                Lifetime = new Vector2(0.35f, 0.6f), Speed = new Vector2(0.3f, 0.9f), Size = new Vector2(0.015f, 0.035f), Color = Color.white,
                Gravity = -0.1f, MaxParticles = 30, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.08f, Burst = 14,
            });
            var pool = root.AddComponent<CleaningVFXPool>();
            Wire(pool, ("template", template), ("audioManager", audio), ("spawnClip", Clip(RugWashAudioGenerator.CleanSparkle)));
            return pool;
        }

        // ------------------------------------------------------------------ Interface

        sealed class UISprites
        {
            public readonly Sprite White = AssetDatabase.LoadAssetAtPath<Sprite>(RugWashTextureGenerator.UIWhite);
            public readonly Sprite Rounded = AssetDatabase.LoadAssetAtPath<Sprite>(RugWashTextureGenerator.UIRounded);
        }

        sealed class BuiltUI
        {
            public HUDController Hud;
            public ToolSelectionUI Toolbar;
            public PauseMenuUI Pause;
            public InspectionPanelUI Inspection;
            public OrderBoardUI Board;
            public ShopUI Shop;
            public ResultsUI Results;
            public DebugOverlay Debug;
        }

        static readonly Color PanelColor = new Color(0.06f, 0.08f, 0.1f, 0.78f);
        static readonly Color Accent = new Color(0.33f, 0.78f, 0.92f, 1f);
        static readonly Color Muted = new Color(1f, 1f, 1f, 0.8f);

        static BuiltUI BuildUI(Transform parent, UISprites sprites, PerformanceMonitor performance)
        {
            var built = new BuiltUI();
            var canvas = CreateCanvas("HUD Canvas", parent);
            CreateEventSystem(parent);

            // ---- HUD
            var hudRoot = Stretch("HUD", canvas.transform);
            var hudGroup = hudRoot.gameObject.AddComponent<CanvasGroup>();
            hudGroup.blocksRaycasts = false;

            var crosshair = CreateRect("Crosshair", hudRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6f, 6f));
            AddImage(crosshair, sprites.White, new Color(1f, 1f, 1f, 0.85f), sliced: false);

            var cleanPanel = CreateRect("CleanlinessPanel", hudRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(560f, 78f));
            AddImage(cleanPanel, sprites.Rounded, PanelColor);
            var cleanLabel = AddText(CreateRect("Label", cleanPanel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(520f, 34f)),
                "Propreté du tapis : 0 %", 26, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            var cleanFill = CreateFillBar("Bar", cleanPanel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(520f, 18f),
                sprites.Rounded, sprites.White, new Color(0f, 0f, 0f, 0.5f), Accent, out _);

            var toast = AddText(CreateRect("Toast", hudRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(900f, 36f)),
                string.Empty, 24, TextAnchor.MiddleCenter, new Color(0.75f, 1f, 0.8f));

            var economyPanel = CreateRect("EconomyPanel", hudRoot, Vector2.one, Vector2.one, Vector2.one, new Vector2(-24f, -22f), new Vector2(300f, 78f));
            AddImage(economyPanel, sprites.Rounded, PanelColor);
            var economy = AddText(Stretch("Label", economyPanel, 10f), "0 €", 24, TextAnchor.MiddleRight, new Color(1f, 0.9f, 0.55f), FontStyle.Bold);
            var orderInfo = AddText(CreateRect("OrderInfo", hudRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -106f), new Vector2(1000f, 60f)),
                string.Empty, 21, TextAnchor.UpperCenter, Color.white);

            var prompt = AddText(CreateRect("Prompt", hudRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(1000f, 40f)),
                string.Empty, 26, TextAnchor.UpperCenter, Color.white);

            var hint = AddText(CreateRect("Hint", hudRoot, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -20f), new Vector2(560f, 120f)),
                string.Empty, 18, TextAnchor.UpperLeft, Muted);

            var toolPanel = CreateRect("ToolPanel", hudRoot, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(24f, 24f), new Vector2(420f, 116f));
            AddImage(toolPanel, sprites.Rounded, PanelColor);
            var toolName = AddText(CreateRect("ToolName", toolPanel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -10f), new Vector2(390f, 40f)),
                "Outil", 30, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            var toolStatus = AddText(CreateRect("ToolStatus", toolPanel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -50f), new Vector2(390f, 30f)),
                string.Empty, 22, TextAnchor.MiddleLeft, Muted);
            var resourceFill = CreateFillBar("ResourceBar", toolPanel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(384f, 14f),
                sprites.Rounded, sprites.White, new Color(0f, 0f, 0f, 0.5f), new Color(0.35f, 0.65f, 1f), out var resourceBar);

            built.Hud = hudRoot.gameObject.AddComponent<HUDController>();
            Wire(built.Hud, ("root", hudGroup), ("toolNameLabel", toolName), ("toolStatusLabel", toolStatus), ("resourceFill", resourceFill),
                ("resourceBar", resourceBar.gameObject), ("cleanlinessLabel", cleanLabel), ("cleanlinessFill", cleanFill),
                ("promptLabel", prompt), ("hintLabel", hint), ("toastLabel", toast), ("economyLabel", economy), ("orderLabel", orderInfo));

            // ---- Barre d'outils
            var bar = CreateRect("ToolBar", hudRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(900f, 52f));
            var barLayout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            barLayout.spacing = 10;
            barLayout.childAlignment = TextAnchor.MiddleCenter;
            barLayout.childControlWidth = true;
            barLayout.childControlHeight = true;
            barLayout.childForceExpandWidth = false;
            var slotImages = new List<Image>();
            var slotLabels = new List<Text>();
            for (int i = 0; i < RugWashInputActions.ToolSlotCount; i++)
            {
                var slot = CreateRect($"Slot_{i}", bar, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220f, 52f));
                var element = slot.gameObject.AddComponent<LayoutElement>();
                element.preferredWidth = 230f;
                element.preferredHeight = 52f;
                slotImages.Add(AddImage(slot, sprites.Rounded, PanelColor));
                slotLabels.Add(AddText(Stretch("Label", slot, 8f), "-", 22, TextAnchor.MiddleCenter, Color.white));
            }
            built.Toolbar = bar.gameObject.AddComponent<ToolSelectionUI>();
            WireList(built.Toolbar, "slotBackgrounds", slotImages);
            WireList(built.Toolbar, "slotLabels", slotLabels);

            // ---- Inspection (F)
            var inspectionRoot = Stretch("Inspection", canvas.transform);
            var inspectionPanel = Stretch("Panel", inspectionRoot);
            AddImage(inspectionPanel, sprites.White, new Color(0f, 0f, 0f, 0.6f), sliced: false, raycast: true);
            var window = CreateRect("Window", inspectionPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500f, 820f));
            AddImage(window, sprites.Rounded, new Color(0.08f, 0.1f, 0.12f, 0.96f));
            var title = AddText(CreateRect("Title", window, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(1400f, 50f)),
                "Inspection", 38, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            var beforeImage = BuildSnapshotColumn(window, "Before", "Avant", new Vector2(-370f, 40f), sprites, out var beforeAspect);
            var afterImage = BuildSnapshotColumn(window, "After", "Maintenant", new Vector2(370f, 40f), sprites, out var afterAspect);
            var stats = AddText(CreateRect("Stats", window, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-150f, 26f), new Vector2(900f, 175f)),
                string.Empty, 24, TextAnchor.MiddleLeft, Color.white);
            var close = CreateButton("CloseButton", window, "Fermer (F)", sprites.Rounded, new Vector2(300f, 64f));
            var closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 0f);
            closeRect.pivot = new Vector2(1f, 0f);
            closeRect.anchoredPosition = new Vector2(-40f, 40f);
            built.Inspection = inspectionRoot.gameObject.AddComponent<InspectionPanelUI>();
            Wire(built.Inspection, ("panel", inspectionPanel.gameObject), ("beforeImage", beforeImage), ("afterImage", afterImage),
                ("beforeAspect", beforeAspect), ("afterAspect", afterAspect), ("titleLabel", title), ("statsLabel", stats), ("closeButton", close));

            // ---- Pause (Échap)
            var pauseRoot = Stretch("PauseMenu", canvas.transform);
            var pausePanel = Stretch("Panel", pauseRoot);
            AddImage(pausePanel, sprites.White, new Color(0.02f, 0.03f, 0.04f, 0.7f), sliced: false, raycast: true);
            var box = CreateRect("Box", pausePanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 560f));
            AddImage(box, sprites.Rounded, new Color(0.08f, 0.1f, 0.12f, 0.97f));
            AddVerticalLayout(box.gameObject, 16, new RectOffset(48, 48, 40, 40), TextAnchor.UpperCenter);
            var fitter = box.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var pauseTitle = AddText(CreateRect("Title", box, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(460f, 60f)), "Pause", 46, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            pauseTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 60f;
            var subtitle = AddText(CreateRect("Subtitle", box, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(460f, 40f)), string.Empty, 22, TextAnchor.MiddleCenter, Muted);
            subtitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
            var resume = CreateButton("Resume", box, "Reprendre", sprites.Rounded, new Vector2(460f, 64f));
            var reset = CreateButton("ResetRug", box, "Recommencer ce tapis", sprites.Rounded, new Vector2(460f, 64f));
            var next = CreateButton("NextRug", box, "Tapis d'entraînement suivant", sprites.Rounded, new Vector2(460f, 64f));
            var abandon = CreateButton("AbandonOrder", box, "Abandonner la commande", sprites.Rounded, new Vector2(460f, 64f));
            var quit = CreateButton("Quit", box, "Quitter le jeu", sprites.Rounded, new Vector2(460f, 64f));
            built.Pause = pauseRoot.gameObject.AddComponent<PauseMenuUI>();
            Wire(built.Pause, ("panel", pausePanel.gameObject), ("resumeButton", resume), ("resetRugButton", reset), ("nextRugButton", next),
                ("quitButton", quit), ("subtitleLabel", subtitle), ("abandonOrderButton", abandon),
                ("resetRugLabel", reset.GetComponentInChildren<Text>()));

            BuildGameLoopUI(canvas, sprites, built);

            // ---- Diagnostic (F3, développement uniquement)
            var debugRoot = CreateRect("DebugOverlay", canvas.transform, Vector2.one, Vector2.one, Vector2.one, new Vector2(-24f, -110f), new Vector2(560f, 250f));
            var debugPanel = Stretch("Panel", debugRoot);
            AddImage(debugPanel, sprites.Rounded, new Color(0f, 0f, 0f, 0.72f));
            var debugLabel = AddText(Stretch("Text", debugPanel, 14f), string.Empty, 18, TextAnchor.UpperLeft, new Color(0.7f, 1f, 0.7f), shadow: false);
            built.Debug = debugRoot.gameObject.AddComponent<DebugOverlay>();
            Wire(built.Debug, ("panel", debugPanel.gameObject), ("label", debugLabel), ("performance", performance));

            inspectionPanel.gameObject.SetActive(false);
            pausePanel.gameObject.SetActive(false);
            debugPanel.gameObject.SetActive(false);
            return built;
        }

        static RectTransform BuildWindow(Transform canvas, string name, Vector2 size, UISprites sprites, string title,
            out GameObject panel, out Button closeButton)
        {
            var root = Stretch(name, canvas);
            var panelRect = Stretch("Panel", root);
            AddImage(panelRect, sprites.White, new Color(0f, 0f, 0f, 0.62f), sliced: false, raycast: true);
            var window = CreateRect("Window", panelRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            AddImage(window, sprites.Rounded, new Color(0.08f, 0.1f, 0.12f, 0.97f));
            AddText(CreateRect("Title", window, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(size.x - 80f, 54f)),
                title, 38, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            closeButton = CreateButton("Close", window, "Fermer (Échap)", sprites.Rounded, new Vector2(240f, 54f));
            var closeRect = (RectTransform)closeButton.transform;
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-24f, -22f);
            panel = panelRect.gameObject;
            return window;
        }

        static ListRowView BuildRowTemplate(Transform parent, UISprites sprites, float width, float height, float statusWidth)
        {
            var row = CreateRect("RowTemplate", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, height));
            var background = AddImage(row, sprites.Rounded, new Color(0.12f, 0.15f, 0.18f, 0.95f));
            var element = row.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;
            // Titre sur une ligne : la taille s'adapte pour ne jamais déborder sur les détails.
            var title = AddText(CreateRect("Title", row, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -8f),
                    new Vector2(width - statusWidth - 230f, 32f)),
                "Titre", 24, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = 15;
            title.resizeTextMaxSize = 24;
            var details = AddText(CreateRect("Details", row, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -40f), new Vector2(width - 260f, height - 44f)),
                "Détails", 18, TextAnchor.UpperLeft, new Color(0.82f, 0.85f, 0.88f));
            var status = AddText(CreateRect("Status", row, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-200f, -8f), new Vector2(statusWidth, 32f)),
                "Statut", 21, TextAnchor.MiddleRight, Color.white, FontStyle.Bold);
            status.resizeTextForBestFit = true;
            status.resizeTextMinSize = 14;
            status.resizeTextMaxSize = 21;
            var button = CreateButton("Action", row, "Action", sprites.Rounded, new Vector2(170f, 50f));
            var buttonRect = (RectTransform)button.transform;
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(1f, 0.5f);
            buttonRect.pivot = new Vector2(1f, 0.5f);
            buttonRect.anchoredPosition = new Vector2(-16f, 0f);
            var label = button.GetComponentInChildren<Text>();
            label.fontSize = 24;
            var view = row.gameObject.AddComponent<ListRowView>();
            Wire(view, ("background", background), ("titleLabel", title), ("detailsLabel", details), ("statusLabel", status),
                ("actionButton", button), ("actionLabel", label));
            return view;
        }

        static void BuildGameLoopUI(Canvas canvas, UISprites sprites, BuiltUI built)
        {
            // ---- Tableau des commandes
            var boardWindow = BuildWindow(canvas.transform, "OrderBoard", new Vector2(1320f, 960f), sprites, "Commandes", out var boardPanel, out var boardClose);
            var header = AddText(CreateRect("Header", boardWindow, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -28f), new Vector2(600f, 40f)),
                string.Empty, 22, TextAnchor.MiddleLeft, new Color(1f, 0.9f, 0.55f), FontStyle.Bold);
            var list = CreateRect("List", boardWindow, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(1260f, 640f));
            AddVerticalLayout(list.gameObject, 8, new RectOffset(0, 0, 0, 0), TextAnchor.UpperCenter);
            var boardRow = BuildRowTemplate(list, sprites, 1260f, 98f, 380f);
            var active = CreateRect("Active", boardWindow, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 64f), new Vector2(1260f, 150f));
            AddImage(active, sprites.Rounded, new Color(0.1f, 0.26f, 0.32f, 0.95f));
            var activeLabel = AddText(CreateRect("Label", active, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(760f, 130f)),
                string.Empty, 22, TextAnchor.MiddleLeft, Color.white);
            var deliver = CreateButton("Deliver", active, "Livrer la commande", sprites.Rounded, new Vector2(240f, 60f));
            PlaceRight((RectTransform)deliver.transform, new Vector2(-270f, 0f));
            var abandon = CreateButton("Abandon", active, "Abandonner", sprites.Rounded, new Vector2(220f, 60f));
            PlaceRight((RectTransform)abandon.transform, new Vector2(-20f, 0f));
            var message = AddText(CreateRect("Message", boardWindow, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(1200f, 40f)),
                string.Empty, 22, TextAnchor.MiddleCenter, new Color(1f, 0.75f, 0.45f));
            built.Board = boardWindow.parent.parent.gameObject.AddComponent<OrderBoardUI>();
            Wire(built.Board, ("panel", boardPanel), ("headerLabel", header), ("listRoot", list), ("rowTemplate", boardRow), ("activeSection", active.gameObject),
                ("activeLabel", activeLabel), ("deliverButton", deliver), ("abandonButton", abandon), ("messageLabel", message), ("closeButton", boardClose));

            // ---- Boutique
            var shopWindow = BuildWindow(canvas.transform, "Shop", new Vector2(1500f, 900f), sprites, "Boutique de matériel", out var shopPanel, out var shopClose);
            var balance = AddText(CreateRect("Balance", shopWindow, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -28f), new Vector2(500f, 40f)),
                string.Empty, 24, TextAnchor.MiddleLeft, new Color(1f, 0.9f, 0.55f), FontStyle.Bold);
            var grid = CreateRect("Grid", shopWindow, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(1440f, 700f));
            var gridLayout = grid.gameObject.AddComponent<GridLayoutGroup>();
            gridLayout.cellSize = new Vector2(714f, 128f);
            gridLayout.spacing = new Vector2(12f, 10f);
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = 2;
            var shopRow = BuildRowTemplate(grid, sprites, 714f, 128f, 120f);
            var shopMessage = AddText(CreateRect("Message", shopWindow, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(1300f, 44f)),
                string.Empty, 24, TextAnchor.MiddleCenter, Color.white);
            built.Shop = shopWindow.parent.parent.gameObject.AddComponent<ShopUI>();
            Wire(built.Shop, ("panel", shopPanel), ("balanceLabel", balance), ("listRoot", grid), ("rowTemplate", shopRow), ("messageLabel", shopMessage),
                ("closeButton", shopClose));

            // ---- Résultats
            var resultsWindow = BuildWindow(canvas.transform, "Results", new Vector2(1600f, 940f), sprites, "Résultats de la commande", out var resultsPanel, out var resultsClose);
            resultsClose.gameObject.SetActive(false);
            var orderTitle = AddText(CreateRect("Order", resultsWindow, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(1400f, 40f)),
                string.Empty, 26, TextAnchor.MiddleCenter, new Color(0.82f, 0.85f, 0.88f));
            var grade = AddText(CreateRect("Grade", resultsWindow, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -130f), new Vector2(560f, 150f)),
                "A", 120, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            var score = AddText(CreateRect("Score", resultsWindow, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -285f), new Vector2(560f, 36f)),
                string.Empty, 24, TextAnchor.MiddleCenter, Color.white);
            var lines = AddText(CreateRect("Lines", resultsWindow, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -340f), new Vector2(560f, 230f)),
                string.Empty, 22, TextAnchor.UpperLeft, Color.white);
            var reward = AddText(CreateRect("Reward", resultsWindow, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -590f), new Vector2(560f, 110f)),
                string.Empty, 26, TextAnchor.UpperLeft, new Color(1f, 0.9f, 0.55f), FontStyle.Bold);
            var before = BuildSnapshotColumn(resultsWindow, "Before", "Avant", new Vector2(300f, 140f), sprites, out var beforeAspect);
            var after = BuildSnapshotColumn(resultsWindow, "After", "Après", new Vector2(300f, -230f), sprites, out var afterAspect);
            ShrinkColumn(before, after);
            var back = CreateButton("BackToWorkshop", resultsWindow, "Retour à l'atelier", sprites.Rounded, new Vector2(300f, 64f));
            PlaceBottomLeft((RectTransform)back.transform, new Vector2(50f, 40f));
            var nextOrder = CreateButton("NextOrder", resultsWindow, "Choisir une autre commande", sprites.Rounded, new Vector2(400f, 64f));
            PlaceBottomLeft((RectTransform)nextOrder.transform, new Vector2(370f, 40f));
            built.Results = resultsWindow.parent.parent.gameObject.AddComponent<ResultsUI>();
            Wire(built.Results, ("panel", resultsPanel), ("titleLabel", orderTitle), ("gradeLabel", grade), ("scoreLabel", score), ("linesLabel", lines),
                ("rewardLabel", reward), ("beforeImage", before), ("afterImage", after), ("beforeAspect", beforeAspect), ("afterAspect", afterAspect),
                ("workshopButton", back), ("nextOrderButton", nextOrder));

            boardPanel.SetActive(false);
            shopPanel.SetActive(false);
            resultsPanel.SetActive(false);
        }

        static void PlaceRight(RectTransform rect, Vector2 offset)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = offset;
        }

        static void PlaceBottomLeft(RectTransform rect, Vector2 offset)
        {
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = offset;
        }

        /// <summary>Colonnes avant/après plus compactes, empilées à droite de l'écran de résultats.</summary>
        static void ShrinkColumn(params RawImage[] images)
        {
            foreach (var image in images)
            {
                var column = (RectTransform)image.transform.parent.parent;
                column.sizeDelta = new Vector2(820f, 330f);
                ((RectTransform)column.Find("Label")).sizeDelta = new Vector2(820f, 40f);
            }
        }

        static RawImage BuildSnapshotColumn(RectTransform window, string name, string label, Vector2 position, UISprites sprites, out AspectRatioFitter aspect)
        {
            var column = CreateRect(name, window, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, new Vector2(680f, 500f));
            AddText(CreateRect("Label", column, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(680f, 40f)),
                label, 28, TextAnchor.MiddleCenter, Accent, FontStyle.Bold);
            var frame = CreateRect("Frame", column, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -22f), new Vector2(0f, -44f));
            AddImage(frame, sprites.Rounded, new Color(0f, 0f, 0f, 0.5f));
            var imageRect = CreateRect("Image", frame, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-12f, -12f));
            var image = imageRect.gameObject.AddComponent<RawImage>();
            image.raycastTarget = false;
            aspect = imageRect.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = 1.5f;
            return image;
        }

        static Canvas CreateCanvas(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = 5;
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        static void CreateEventSystem(Transform parent)
        {
            var go = new GameObject("EventSystem");
            go.transform.SetParent(parent, false);
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
    }
}
