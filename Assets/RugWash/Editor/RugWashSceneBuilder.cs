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
using RugWash.Save;
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
        internal const float RoomHalfX = 6f, RoomHalfZ = 5f, RoomHeight = 3.6f, WallThickness = 0.2f;
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
            Wire(audio, ("uiClick", Clip(RugWashAudioGenerator.UIClick)), ("uiConfirm", Clip(RugWashAudioGenerator.UIConfirm)),
                ("objectiveClip", Clip(RugWashAudioGenerator.Objective)));
            var bootstrap = game.AddComponent<GameBootstrap>();

            BuildRoom(environment, content, meshes);
            var washPad = BuildWashBay(environment, content, meshes, audio);
            RugWashDecor.WaterStation(environment, content, meshes, audio);
            var shopCounter = RugWashDecor.ToolStorage(environment, content, meshes);
            var orderCounter = RugWashDecor.OrderDesk(environment, content, meshes);
            RugWashDecor.DryingArea(environment, content, meshes);
            RugWashDecor.Shelves(environment, content, meshes);
            RugWashDecor.SoapStation(environment, content, meshes, audio);
            RugWashDecor.Ceiling(environment, content, meshes);
            RugWashDecor.WallDetails(environment, content, meshes);
            BuildLights(lighting, content, meshes);
            var ambient = new GameObject("Ambience");
            ambient.transform.SetParent(environment, false);
            ambient.AddComponent<AudioSource>();
            Wire(ambient.AddComponent<AmbientAudio>(), ("audioManager", audio), ("loop", Clip(RugWashAudioGenerator.Ambient)));

            var rug = BuildRug(gameplay, content);
            var snapshots = BuildSnapshotCamera(gameplay, rug);

            var cleanPool = BuildCleanFeedbackPool(effects, content, audio);
            var player = BuildPlayer(gameplay, content, meshes, effects, gameState, audio, cleanPool,
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

            var settings = game.AddComponent<SettingsManager>();
            Wire(settings, ("audioManager", audio), ("player", fps), ("input", input));
            var save = game.AddComponent<SaveManager>();
            Wire(save, ("settings", settings), ("wallet", wallet), ("reputation", reputation), ("upgrades", upgrades),
                ("orders", orders), ("shop", shop));

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
                ("saveManager", save),
                ("settingsManager", settings),
                ("settingsUI", ui.Settings),
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

        [MenuItem("RugWash/Setup/Générer le Menu Principal", priority = 21)]
        static void BuildMainMenuFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildMainMenuScene();
            EditorUtility.DisplayDialog("RugWash", $"Menu principal généré : {RugWashPaths.MainMenuScene}", "OK");
        }

        /// <summary>Menu principal : l'atelier en arrière-plan (caméra en lent travelling) et l'interface du menu.</summary>
        public static Scene BuildMainMenuScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var content = RugWashContentBuilder.EnsureContent();
            var meshes = new MeshStore(RugWashPaths.Root + "/Prefabs/MainMenu_GeneratedMeshes.asset");
            var sprites = new UISprites();
            ConfigureEnvironmentLighting();

            var menu = new GameObject("[Menu]");
            var audio = menu.AddComponent<AudioManager>();
            Wire(audio, ("uiClick", Clip(RugWashAudioGenerator.UIClick)), ("uiConfirm", Clip(RugWashAudioGenerator.UIConfirm)),
                ("objectiveClip", Clip(RugWashAudioGenerator.Objective)));
            var settings = menu.AddComponent<SettingsManager>();
            Wire(settings, ("audioManager", audio));
            var save = menu.AddComponent<SaveManager>();
            Wire(save, ("settings", settings));

            // Décor : le même atelier, avec un tapis propre sur l'aire de lavage.
            var environment = new GameObject("--- Environment").transform;
            var lighting = new GameObject("--- Lighting").transform;
            BuildRoom(environment, content, meshes);
            BuildWashBay(environment, content, meshes, audio);
            RugWashDecor.WaterStation(environment, content, meshes, audio);
            RugWashDecor.ToolStorage(environment, content, meshes);
            RugWashDecor.OrderDesk(environment, content, meshes);
            RugWashDecor.DryingArea(environment, content, meshes);
            RugWashDecor.Shelves(environment, content, meshes);
            RugWashDecor.SoapStation(environment, content, meshes, audio);
            RugWashDecor.Ceiling(environment, content, meshes);
            RugWashDecor.WallDetails(environment, content, meshes);
            BuildLights(lighting, content, meshes);
            var rug = new GameObject("Rug_Display");
            rug.transform.SetParent(environment, false);
            rug.transform.localPosition = WashPadCenter + Vector3.up * WashPadSize.y;
            rug.AddComponent<MeshFilter>().sharedMesh = PreviewRugMesh(content.Oriental, RugWashPaths.Root + "/Prefabs/MenuRug_Mesh.asset");
            c_SetRugMaterials(rug.AddComponent<MeshRenderer>(), content);

            var cameraGo = new GameObject("MenuCamera") { tag = "MainCamera" };
            var camera = cameraGo.AddComponent<Camera>();
            camera.fieldOfView = 55f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 60f;
            cameraGo.AddComponent<AudioListener>();
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraGo.AddComponent<MenuCameraDrift>();
            cameraGo.transform.SetPositionAndRotation(new Vector3(-1.4f, 2.1f, -3.2f), Quaternion.Euler(25f, 20f, 0f));

            // Interface.
            var canvas = CreateCanvas("Menu Canvas", null);
            CreateEventSystem(null);
            var shade = CreateRect("LeftShade", canvas.transform, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(760f, 0f));
            AddImage(shade, sprites.White, new Color(0.03f, 0.04f, 0.05f, 0.86f), sliced: false);
            AddText(CreateRect("Title", canvas.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(90f, -110f), new Vector2(640f, 90f)),
                "RugWash Simulator", 64, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            AddText(CreateRect("Subtitle", canvas.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(94f, -200f), new Vector2(640f, 40f)),
                "Atelier de nettoyage de tapis", 28, TextAnchor.MiddleLeft, Accent);

            var column = CreateRect("Buttons", canvas.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(90f, -40f), new Vector2(460f, 420f));
            AddVerticalLayout(column.gameObject, 14, new RectOffset(0, 0, 0, 0), TextAnchor.UpperLeft);
            var play = CreateButton("Play", column, "Nouvelle partie", sprites.Rounded, new Vector2(460f, 70f));
            var resume = CreateButton("Continue", column, "Continuer", sprites.Rounded, new Vector2(460f, 70f));
            var summary = AddText(CreateRect("ContinueSummary", column, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(460f, 30f)),
                string.Empty, 20, TextAnchor.MiddleLeft, new Color(1f, 0.9f, 0.55f));
            summary.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
            var settingsButton = CreateButton("Settings", column, "Paramètres", sprites.Rounded, new Vector2(460f, 70f));
            var quit = CreateButton("Quit", column, "Quitter", sprites.Rounded, new Vector2(460f, 70f));

            var message = AddText(CreateRect("Message", canvas.transform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(90f, 60f), new Vector2(640f, 80f)),
                string.Empty, 20, TextAnchor.LowerLeft, new Color(1f, 0.75f, 0.45f));
            AddText(CreateRect("Version", canvas.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-30f, 24f), new Vector2(600f, 30f)),
                "Démo en développement — sons temporaires", 18, TextAnchor.LowerRight, Muted);

            // Confirmation de nouvelle partie.
            var confirm = Stretch("Confirm", canvas.transform);
            AddImage(confirm, sprites.White, new Color(0f, 0f, 0f, 0.65f), sliced: false, raycast: true);
            var confirmBox = CreateRect("Box", confirm, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 300f));
            AddImage(confirmBox, sprites.Rounded, new Color(0.08f, 0.1f, 0.12f, 0.97f));
            AddText(CreateRect("Text", confirmBox, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(680f, 130f)),
                "Commencer une nouvelle partie ?\n<size=22>L'argent, la réputation, les achats et les commandes terminées seront remis à zéro. Les paramètres sont conservés.</size>",
                30, TextAnchor.UpperCenter, Color.white, FontStyle.Bold).supportRichText = true;
            var yes = CreateButton("Yes", confirmBox, "Nouvelle partie", sprites.Rounded, new Vector2(300f, 64f));
            PlaceBottomLeft((RectTransform)yes.transform, new Vector2(60f, 32f));
            var no = CreateButton("No", confirmBox, "Annuler", sprites.Rounded, new Vector2(300f, 64f));
            PlaceBottomLeft((RectTransform)no.transform, new Vector2(400f, 32f));

            var loading = Stretch("Loading", canvas.transform);
            AddImage(loading, sprites.White, new Color(0f, 0f, 0f, 0.8f), sliced: false, raycast: true);
            AddText(Stretch("Text", loading), "Chargement de l'atelier…", 40, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);

            var settingsUI = BuildSettingsUI(canvas, sprites);

            var controller = menu.AddComponent<MainMenuController>();
            Wire(controller, ("save", save), ("settings", settings), ("audioManager", audio), ("settingsUI", settingsUI),
                ("playButton", play), ("continueButton", resume), ("settingsButton", settingsButton), ("quitButton", quit),
                ("continueSummary", summary), ("messageLabel", message), ("confirmPanel", confirm.gameObject),
                ("confirmYesButton", yes), ("confirmNoButton", no), ("loadingPanel", loading.gameObject));
            if (!controller.IsConfigured(out string missing))
                throw new InvalidOperationException($"[RugWash] Menu principal incomplet : {missing}");
            if (!settingsUI.IsConfigured(out missing))
                throw new InvalidOperationException($"[RugWash] Paramètres incomplets : {missing}");

            confirm.gameObject.SetActive(false);
            loading.gameObject.SetActive(false);
            EditorSceneManager.SaveScene(scene, RugWashPaths.MainMenuScene);
            RugWashProjectBootstrapper.SyncBuildScenes();
            AssetDatabase.SaveAssets();
            Debug.Log($"[RugWash] Scène générée : {RugWashPaths.MainMenuScene}");
            return scene;
        }

        static void c_SetRugMaterials(MeshRenderer renderer, RugWashContentBuilder.Content c)
        {
            c["RugSurface"].SetTexture("_BaseMap", c.Oriental.patternTexture);
            renderer.sharedMaterials = new[] { c["RugSurface"], c["RugFringe"] };
        }

        // ------------------------------------------------------------------ Environnement

        static void ConfigureEnvironmentLighting()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.56f, 0.58f, 0.62f);
            RenderSettings.ambientEquatorColor = new Color(0.4f, 0.39f, 0.37f);
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
            foreach (float x in new[] { -3.6f, 0f })
            {
                var frame = CreateBox(meshes, $"Window_{x:0}_Frame", room, new Vector3(x, 2.65f, RoomHalfZ - 0.03f), new Vector3(1.9f, 0.95f, 0.04f), c["DarkMetal"], collider: false);
                CreateBox(meshes, "Sill", frame.transform, new Vector3(0f, -0.5f, -0.04f), new Vector3(2f, 0.04f, 0.1f), c["DarkMetal"], collider: false);
                CreateBox(meshes, "Glass", frame.transform, new Vector3(0f, 0f, -0.012f), new Vector3(1.78f, 0.83f, 0.02f), c["Window"], collider: false);
                CreateBox(meshes, "Mullion", frame.transform, new Vector3(0f, 0f, -0.025f), new Vector3(0.04f, 0.83f, 0.02f), c["DarkMetal"], collider: false);
            }
            CreateBox(meshes, "Door_Frame", room, new Vector3(-3.2f, 1.1f, -RoomHalfZ + 0.02f), new Vector3(1.26f, 2.22f, 0.04f), c["DarkMetal"], collider: false);
            CreateBox(meshes, "Door", room, new Vector3(-3.2f, 1.05f, -RoomHalfZ + 0.045f), new Vector3(1.1f, 2.1f, 0.05f), c["DoorPaint"], collider: false);
            CreateBox(meshes, "Door_Panel_Top", room, new Vector3(-3.2f, 1.55f, -RoomHalfZ + 0.072f), new Vector3(0.8f, 0.7f, 0.01f), c["Window"], collider: false);
            CreateBox(meshes, "Door_Kick", room, new Vector3(-3.2f, 0.12f, -RoomHalfZ + 0.072f), new Vector3(1.06f, 0.2f, 0.01f), c["Steel"], collider: false);
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
            CreateBox(meshes, "Curb_Back", bay, new Vector3(0f, 0.07f, hz + 0.04f), new Vector3(WashPadSize.x + 0.16f, 0.14f, 0.08f), c["Hazard"]);
            CreateBox(meshes, "Curb_Front", bay, new Vector3(0f, 0.07f, -hz - 0.04f), new Vector3(WashPadSize.x + 0.16f, 0.14f, 0.08f), c["Hazard"]);
            CreateBox(meshes, "Curb_Left", bay, new Vector3(-hx - 0.04f, 0.07f, 0f), new Vector3(0.08f, 0.14f, WashPadSize.z), c["Hazard"]);
            CreateBox(meshes, "Curb_Right", bay, new Vector3(hx + 0.04f, 0.07f, 0f), new Vector3(0.08f, 0.14f, WashPadSize.z), c["Hazard"]);

            // Caniveau d'évacuation.
            var drain = CreateBox(meshes, "Drain_Channel", bay, new Vector3(hx - 0.22f, WashPadSize.y + 0.002f, 0f), new Vector3(0.2f, 0.006f, WashPadSize.z - 0.3f), c["Grate"]);
            CreateBox(meshes, "Drain_Frame", bay, new Vector3(hx - 0.22f, WashPadSize.y + 0.001f, 0f), new Vector3(0.24f, 0.004f, WashPadSize.z - 0.26f), c["DarkMetal"], collider: false);
            CreateSign(parent, "Sign_WashBay", "AIRE DE LAVAGE", new Vector3(0f, 1.95f, RoomHalfZ - 0.04f), Quaternion.identity, meshes, c);

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

        internal static void CreateSign(Transform parent, string name, string text, Vector3 position, Quaternion rotation, MeshStore meshes,
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
            var housing = new GameObject("Spot_Housing");
            housing.transform.SetParent(spotGo.transform, false);
            housing.transform.localPosition = new Vector3(0f, 0f, -0.1f);
            housing.AddComponent<MeshFilter>().sharedMesh = meshes.Add("SpotHousing", () => RugWashProceduralMeshes.Lathe(new[]
            {
                new Vector2(0f, -0.12f), new Vector2(0.12f, -0.12f), new Vector2(0.2f, 0.05f), new Vector2(0.26f, 0.12f), new Vector2(0.24f, 0.12f),
            }, 24, capTop: false));
            housing.AddComponent<MeshRenderer>().sharedMaterial = c["DarkMetal"];
            housing.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            housing.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Plafonniers d'ambiance.
            int index = 0;
            foreach (float x in new[] { -3.4f, 3.4f })
            foreach (float z in new[] { -2.6f, 2.6f })
            {
                var fixture = new GameObject($"CeilingLamp_{index++}");
                fixture.transform.SetParent(parent, false);
                fixture.transform.localPosition = new Vector3(x, RoomHeight - 0.4f, z);
                RugWashDecor.LampFixture(fixture.transform, c, meshes);

                var lightGo = new GameObject("Light");
                lightGo.transform.SetParent(fixture.transform, false);
                lightGo.transform.localPosition = new Vector3(0f, -0.2f, 0f);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 8f;
                light.intensity = 2.2f;
                light.color = new Color(1f, 0.95f, 0.88f);
                light.shadows = LightShadows.None;
                lightGo.AddComponent<UniversalAdditionalLightData>();
            }

            // Lumière du jour entrant par les fenêtres hautes (froide, sans ombres).
            foreach (float x in new[] { -3.6f, 0f })
            {
                var day = new GameObject("Daylight_Window");
                day.transform.SetParent(parent, false);
                day.transform.SetPositionAndRotation(new Vector3(x, RoomHeight - 0.4f, RoomHalfZ - 0.3f), Quaternion.Euler(58f, 180f, 0f));
                var dayLight = day.AddComponent<Light>();
                dayLight.type = LightType.Spot;
                dayLight.spotAngle = 80f;
                dayLight.innerSpotAngle = 30f;
                dayLight.range = 8f;
                dayLight.intensity = 2.4f;
                dayLight.color = new Color(0.72f, 0.82f, 1f);
                dayLight.shadows = LightShadows.None;
                day.AddComponent<UniversalAdditionalLightData>();
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
            probe.resolution = 256;
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
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, RugWashPaths.WorkshopVolumeProfile);
            }

            Ensure<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);
            var bloom = Ensure<Bloom>(profile);
            bloom.threshold.Override(1f);
            bloom.intensity.Override(0.4f);
            bloom.scatter.Override(0.65f);
            var color = Ensure<ColorAdjustments>(profile);
            color.postExposure.Override(0.3f);
            color.contrast.Override(12f);
            color.saturation.Override(8f);
            Ensure<WhiteBalance>(profile).temperature.Override(6f);
            var vignette = Ensure<Vignette>(profile);
            vignette.intensity.Override(0.22f);
            vignette.smoothness.Override(0.45f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        static T Ensure<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet(out T existing)) return existing;
            var component = profile.Add<T>(true);
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
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

        static Mesh PreviewRugMesh(RugDefinition definition, string path = RugWashPaths.Root + "/Prefabs/RugPreview_Mesh.asset")
        {
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

        static GameObject BuildPlayer(Transform parent, RugWashContentBuilder.Content c, MeshStore meshes, Transform effects, GameStateManager gameState,
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
            var rig = new GameObject("ViewModelRig").transform;
            rig.SetParent(cameraGo.transform, false);
            var ctx = new ToolBuildContext { Root = toolsRoot, Camera = rig, Content = c, Effects = effects, Audio = audio, CleanPool = cleanPool, Meshes = meshes };
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

            Wire(rig.gameObject.AddComponent<ViewModelRig>(), ("input", input), ("controller", fps), ("tools", toolController));
            var footsteps = player.AddComponent<FootstepAudio>();
            Wire(footsteps, ("controller", fps), ("audioManager", audio));
            var stepClips = new List<AudioClip>();
            foreach (var path in RugWashAudioGenerator.Footsteps) stepClips.Add(Clip(path));
            WireList(footsteps, "steps", stepClips);

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
            public MeshStore Meshes;
        }

        static AudioClip Clip(string path) => AssetDatabase.LoadAssetAtPath<AudioClip>(path);

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
            var model = RugWashToolModels.Vacuum(ctx.Camera, ctx.Meshes, c);
            RugWashToolModels.Finish(model);
            var suction = CreateParticles("VacuumDust", ctx.Effects, c["Dust"], new ParticleConfig
            {
                Lifetime = new Vector2(0.15f, 0.3f), Speed = new Vector2(1.5f, 3f), Size = new Vector2(0.01f, 0.025f), Color = Color.white,
                Gravity = 0f, MaxParticles = 160, Shape = ParticleSystemShapeType.Cone, Angle = 25f, Radius = 0.12f, Rate = 0f,
                Loop = true, PlayOnAwake = true,
            });
            var tool = CreateTool<VacuumTool>(ctx, "Vacuum", c.Vacuum, model.Root, RugWashAudioGenerator.VacuumLoop, 0.45f, 0.2f, -0.06f);
            Wire(tool, ("suctionParticles", suction), ("nozzle", model.Tip));
            return tool;
        }

        static SoapSprayerTool BuildSoap(ToolBuildContext ctx)
        {
            var c = ctx.Content;
            var model = RugWashToolModels.Soap(ctx.Camera, ctx.Meshes, c);
            RugWashToolModels.Finish(model);
            var spray = CreateParticles("SoapMist", model.Tip, c["SoapMist"], new ParticleConfig
            {
                Lifetime = new Vector2(0.3f, 0.45f), Speed = new Vector2(6f, 8f), Size = new Vector2(0.015f, 0.03f), Color = Color.white,
                Gravity = 0.2f, MaxParticles = 220, Shape = ParticleSystemShapeType.Cone, Angle = 9f, Radius = 0.005f, Rate = 0f,
                Loop = true, PlayOnAwake = true, SizeGrowth = 2.2f,
            });
            var puffs = CreateParticles("FoamPuffs", ctx.Effects, c["Foam"], new ParticleConfig
            {
                Lifetime = new Vector2(0.5f, 0.9f), Speed = new Vector2(0.05f, 0.25f), Size = new Vector2(0.03f, 0.07f), Color = Color.white,
                Gravity = 0f, MaxParticles = 80, Shape = ParticleSystemShapeType.Circle, Radius = 0.15f, Rate = 0f, Loop = true, PlayOnAwake = true,
            });
            var tool = CreateTool<SoapSprayerTool>(ctx, "Soap", c.Soap, model.Root, RugWashAudioGenerator.SoapLoop, 0.4f, 0.1f, 0f);
            Wire(tool, ("sprayParticles", spray), ("foamPuffs", puffs));
            return tool;
        }

        static BrushTool BuildBrush(ToolBuildContext ctx)
        {
            var c = ctx.Content;
            var model = RugWashToolModels.Brush(ctx.Camera, ctx.Meshes, c);
            RugWashToolModels.Finish(model);
            var debris = CreateParticles("BrushDebris", ctx.Effects, c["Debris"], new ParticleConfig
            {
                Lifetime = new Vector2(0.35f, 0.7f), Speed = new Vector2(0.3f, 1.1f), Size = new Vector2(0.008f, 0.02f),
                Color = Color.white, Gravity = 1f, MaxParticles = 160, Shape = ParticleSystemShapeType.Cone, Angle = 55f,
                Radius = 0.08f, Rate = 0f, Loop = true, PlayOnAwake = true,
            });
            var tool = CreateTool<BrushTool>(ctx, "Brush", c.Brush, model.Root, RugWashAudioGenerator.BrushLoop, 0.25f, 0.35f, 0.05f);
            Wire(tool, ("debrisParticles", debris));
            return tool;
        }

        static WaterRinseTool BuildRinse(ToolBuildContext ctx)
        {
            var c = ctx.Content;
            var model = RugWashToolModels.Rinse(ctx.Camera, ctx.Meshes, c);
            RugWashToolModels.Finish(model);
            var jet = CreateParticles("WaterJet", model.Tip, c["WaterJet"], new ParticleConfig
            {
                Lifetime = new Vector2(0.26f, 0.32f), Speed = new Vector2(13f, 15f), Size = new Vector2(0.018f, 0.032f),
                Color = Color.white, Gravity = 0.3f, MaxParticles = 600, Shape = ParticleSystemShapeType.Cone, Angle = 2f,
                Radius = 0.004f, Rate = 0f, Loop = true, PlayOnAwake = true, SizeGrowth = 2.2f, Stretch = 0.035f,
            });
            var splash = CreateParticles("WaterSplash", ctx.Effects, c["Splash"], new ParticleConfig
            {
                Lifetime = new Vector2(0.25f, 0.5f), Speed = new Vector2(1f, 2.8f), Size = new Vector2(0.015f, 0.04f),
                Color = Color.white, Gravity = 1.3f, MaxParticles = 300, Shape = ParticleSystemShapeType.Hemisphere,
                Radius = 0.05f, Rate = 0f, Loop = true, PlayOnAwake = true, Stretch = 0.02f,
            });
            var tool = CreateTool<WaterRinseTool>(ctx, "Rinse", c.Rinse, model.Root, RugWashAudioGenerator.RinseLoop, 0.45f, 0.15f, -0.05f);
            Wire(tool, ("jetParticles", jet), ("splashParticles", splash));
            SetFloat(tool, "jetRate", 520f);
            return tool;
        }

        static WaterExtractorTool BuildExtractor(ToolBuildContext ctx)
        {
            var c = ctx.Content;
            var model = RugWashToolModels.Extractor(ctx.Camera, ctx.Meshes, c);
            RugWashToolModels.Finish(model);
            var droplets = CreateParticles("ExtractorDroplets", ctx.Effects, c["Droplet"], new ParticleConfig
            {
                Lifetime = new Vector2(0.12f, 0.25f), Speed = new Vector2(1.8f, 3.2f), Size = new Vector2(0.01f, 0.02f), Color = Color.white,
                Gravity = 0f, MaxParticles = 180, Shape = ParticleSystemShapeType.Cone, Angle = 20f, Radius = 0.1f, Rate = 0f,
                Loop = true, PlayOnAwake = true, Stretch = 0.02f,
            });
            var tool = CreateTool<WaterExtractorTool>(ctx, "Extractor", c.Extractor, model.Root, RugWashAudioGenerator.ExtractorLoop, 0.45f, 0.25f, -0.1f);
            Wire(tool, ("suctionDroplets", droplets), ("nozzle", model.Tip));
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
            public SettingsUI Settings;
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
            var settingsButton = CreateButton("Settings", box, "Paramètres", sprites.Rounded, new Vector2(460f, 64f));
            var mainMenu = CreateButton("MainMenu", box, "Menu principal", sprites.Rounded, new Vector2(460f, 64f));
            var quit = CreateButton("Quit", box, "Quitter le jeu", sprites.Rounded, new Vector2(460f, 64f));
            built.Pause = pauseRoot.gameObject.AddComponent<PauseMenuUI>();
            Wire(built.Pause, ("panel", pausePanel.gameObject), ("resumeButton", resume), ("resetRugButton", reset), ("nextRugButton", next),
                ("quitButton", quit), ("subtitleLabel", subtitle), ("abandonOrderButton", abandon),
                ("resetRugLabel", reset.GetComponentInChildren<Text>()), ("settingsButton", settingsButton), ("mainMenuButton", mainMenu));

            BuildGameLoopUI(canvas, sprites, built);
            built.Settings = BuildSettingsUI(canvas, sprites);

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

        static RectTransform SettingRow(Transform column, string label)
        {
            var row = CreateRect(label, column, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(580f, 54f));
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 54f;
            AddText(CreateRect("Label", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(230f, 50f)),
                label, 22, TextAnchor.MiddleLeft, Color.white);
            return row;
        }

        static void SectionTitle(Transform column, string title)
        {
            var text = AddText(CreateRect(title, column, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(580f, 44f)),
                title, 26, TextAnchor.LowerLeft, Accent, FontStyle.Bold);
            text.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
        }

        static Slider SliderRow(Transform column, string label, UISprites sprites, out Text value)
        {
            var row = SettingRow(column, label);
            var slider = CreateSlider("Slider", row, sprites.Rounded, sprites.White, new Vector2(240f, 36f), Accent);
            PlaceRight((RectTransform)slider.transform, new Vector2(-90f, 0f));
            value = AddText(CreateRect("Value", row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(80f, 40f)),
                "0", 20, TextAnchor.MiddleRight, Muted);
            return slider;
        }

        static StepperView StepperRow(Transform column, string label, UISprites sprites)
        {
            var row = SettingRow(column, label);
            var previous = CreateButton("Previous", row, "‹", sprites.Rounded, new Vector2(48f, 44f));
            PlaceRight((RectTransform)previous.transform, new Vector2(-290f, 0f));
            var next = CreateButton("Next", row, "›", sprites.Rounded, new Vector2(48f, 44f));
            PlaceRight((RectTransform)next.transform, Vector2.zero);
            var value = AddText(CreateRect("Value", row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-54f, 0f), new Vector2(230f, 44f)),
                "—", 20, TextAnchor.MiddleCenter, Color.white);
            value.resizeTextForBestFit = true;
            value.resizeTextMinSize = 14;
            value.resizeTextMaxSize = 20;
            var stepper = row.gameObject.AddComponent<StepperView>();
            Wire(stepper, ("valueLabel", value), ("previousButton", previous), ("nextButton", next));
            return stepper;
        }

        static SettingsUI BuildSettingsUI(Canvas canvas, UISprites sprites)
        {
            var window = BuildWindow(canvas.transform, "Settings", new Vector2(1340f, 900f), sprites, "Paramètres", out var panel, out var close);

            var left = CreateRect("Left", window, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -100f), new Vector2(580f, 700f));
            AddVerticalLayout(left.gameObject, 6, new RectOffset(0, 0, 0, 0), TextAnchor.UpperLeft);
            SectionTitle(left, "Audio");
            var master = SliderRow(left, "Volume général", sprites, out var masterValue);
            var effects = SliderRow(left, "Effets", sprites, out var effectsValue);
            var interfaceSlider = SliderRow(left, "Interface", sprites, out var interfaceValue);
            SectionTitle(left, "Souris");
            var sensitivity = SliderRow(left, "Sensibilité", sprites, out var sensitivityValue);
            var invert = StepperRow(left, "Inverser l'axe vertical", sprites);
            SectionTitle(left, "Affichage");
            var resolution = StepperRow(left, "Résolution", sprites);
            var display = StepperRow(left, "Mode d'affichage", sprites);
            var quality = StepperRow(left, "Qualité graphique", sprites);

            var right = CreateRect("Right", window, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-50f, -100f), new Vector2(580f, 700f));
            AddVerticalLayout(right.gameObject, 6, new RectOffset(0, 0, 0, 0), TextAnchor.UpperLeft);
            SectionTitle(right, "Touches (disposition physique du clavier)");
            var list = CreateRect("Bindings", right, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(580f, 420f));
            list.gameObject.AddComponent<LayoutElement>().preferredHeight = 430f;
            AddVerticalLayout(list.gameObject, 6, new RectOffset(0, 0, 0, 0), TextAnchor.UpperLeft);

            var template = CreateRect("RebindTemplate", list, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(580f, 54f));
            template.gameObject.AddComponent<LayoutElement>().preferredHeight = 54f;
            AddImage(template, sprites.Rounded, new Color(0.12f, 0.15f, 0.18f, 0.95f));
            var actionLabel = AddText(CreateRect("Action", template, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(200f, 44f)),
                "Action", 21, TextAnchor.MiddleLeft, Color.white);
            var bindingLabel = AddText(CreateRect("Binding", template, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(220f, 0f), new Vector2(210f, 44f)),
                "—", 21, TextAnchor.MiddleLeft, new Color(1f, 0.9f, 0.55f), FontStyle.Bold);
            bindingLabel.resizeTextForBestFit = true;
            bindingLabel.resizeTextMinSize = 12;
            bindingLabel.resizeTextMaxSize = 21;
            var change = CreateButton("Change", template, "Modifier", sprites.Rounded, new Vector2(130f, 42f));
            PlaceRight((RectTransform)change.transform, new Vector2(-8f, 0f));
            change.GetComponentInChildren<Text>().fontSize = 21;
            var rebindRow = template.gameObject.AddComponent<RebindRowView>();
            Wire(rebindRow, ("actionLabel", actionLabel), ("bindingLabel", bindingLabel), ("changeButton", change));

            var reset = CreateButton("ResetBindings", right, "Touches par défaut", sprites.Rounded, new Vector2(300f, 56f));
            var message = AddText(CreateRect("Message", window, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(1200f, 40f)),
                string.Empty, 22, TextAnchor.MiddleCenter, new Color(0.55f, 0.95f, 0.6f));

            var ui = window.parent.parent.gameObject.AddComponent<SettingsUI>();
            Wire(ui, ("panel", panel), ("masterSlider", master), ("effectsSlider", effects), ("interfaceSlider", interfaceSlider),
                ("sensitivitySlider", sensitivity), ("masterValue", masterValue), ("effectsValue", effectsValue), ("interfaceValue", interfaceValue),
                ("sensitivityValue", sensitivityValue), ("invertYStepper", invert), ("resolutionStepper", resolution),
                ("displayModeStepper", display), ("qualityStepper", quality), ("rebindRoot", list), ("rebindTemplate", rebindRow),
                ("resetBindingsButton", reset), ("closeButton", close), ("messageLabel", message));
            panel.SetActive(false);
            return ui;
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
