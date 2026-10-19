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
using RugWash.Workshop;
using RugWash.RugDex;
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
                ("uiDeny", Clip(RugWashAudioGenerator.UIDeny)),
                ("objectiveClip", Clip(RugWashAudioGenerator.Objective)));
            var bootstrap = game.AddComponent<GameBootstrap>();

            var closedWall = BuildRoom(environment, content, meshes);
            BuildWashBay(environment, content, meshes, audio);
            // Agrandissement (grande mise à jour) : annexe derrière le mur droit, rendue seulement après l'achat.
            var annex = RugWashExtensionBuilder.Annex(environment, content, meshes, audio);
            RugWashDecor.WaterStation(environment, content, meshes, audio);
            // Meuble à outils : décor seulement (v0.1.2) — achats et missions passent par l'ordinateur du bureau.
            RugWashDecor.ToolStorage(environment, content, meshes);
            var orderCounter = RugWashDecor.OrderDesk(environment, content, meshes);
            RugWashDecor.DryingArea(environment, content, meshes);
            RugWashDecor.Shelves(environment, content, meshes);
            RugWashDecor.PolyHavenProps(environment, content, meshes);
            var soapStation = RugWashDecor.SoapStation(environment, content, meshes, audio);
            RugWashDecor.Ceiling(environment, content, meshes);
            RugWashDecor.WallDetails(environment, content, meshes);
            BuildLights(lighting, content, meshes);
            // Décor de l'atelier par paliers (v0.1.2 jalon 5) : du cagibi au penthouse avec vue sur Los Angeles.
            var roomLights = new List<Light>(lighting.GetComponentsInChildren<Light>(true));
            var decor = RugWashWorkshopTiers.Build(environment, environment.Find("Room"), content, meshes, roomLights);
            var ambient = new GameObject("Ambience");
            ambient.transform.SetParent(environment, false);
            ambient.AddComponent<AudioSource>();
            Wire(ambient.AddComponent<AmbientAudio>(), ("audioManager", audio), ("loop", Clip(RugWashAudioGenerator.Ambient)));

            var rug = BuildRug(gameplay, content);
            // Zone de lavage 2 (annexe) : son tapis et son aire existent toujours (réplication réseau), l'annexe les rend accessibles.
            var rug2 = BuildRug(gameplay, content, "Rug_Zone2", RugWashExtensionBuilder.Bay2Center + Vector3.up * WashPadSize.y,
                RugWashPaths.Root + "/Prefabs/RugPreview2_Mesh.asset");
            var snapshots = BuildSnapshotCamera(gameplay, rug);
            WireList(snapshots, "extraRugs", new[] { rug2 });

            // Surfaces de l'atelier, nettoyables comme le tapis : sol (traces, poussière) et plan de travail de l'aire de lavage.
            var floorSurface = BuildSurface(gameplay, "FloorSurface", content.Floor, content["FloorSurface"], content, Vector3.zero);
            var workSurfaceArea = BuildSurface(gameplay, "WorkSurface", content.WorkSurface, content["WorkSurface"], content,
                WashPadCenter + Vector3.up * WashPadSize.y);
            var workSurfaceArea2 = BuildSurface(gameplay, "WorkSurface_Zone2", content.WorkSurface, content["WorkSurface"], content,
                RugWashExtensionBuilder.Bay2Center + Vector3.up * WashPadSize.y);
            environment.Find("Room/Floor").GetComponent<MeshRenderer>().enabled = false;

            var cleanPool = BuildCleanFeedbackPool(effects, content, audio);
            var player = BuildPlayer(gameplay, content, meshes, effects, gameState, audio, cleanPool,
                out var input, out var fps, out var toolController, out var interactor);
            // Fût du poste détergent (V0.2.5) : son liquide suit le stock réel du joueur (en ligne : celui de la session).
            var stockLevel = soapStation.GetComponentInChildren<ContainerLevel>(true);
            if (stockLevel != null) Wire(stockLevel, ("stock", player.GetComponent<DetergentSupply>()));

            // Plan de travail : reçoit le ruissellement du tapis et recontamine le tapis suivant s'il reste sale.
            var workSurface = game.AddComponent<WorkSurface>();
            Wire(workSurface, ("surface", workSurfaceArea), ("orderRug", rug));
            var zone2 = new GameObject("Zone2");
            zone2.transform.SetParent(gameplay, false);
            var workSurface2 = zone2.AddComponent<WorkSurface>();
            Wire(workSurface2, ("surface", workSurfaceArea2), ("orderRug", rug2));
            var bay2 = zone2.AddComponent<RugBay>();
            Wire(bay2, ("rug", rug2), ("workSurface", workSurface2));
            SetInt(bay2, "number", 2);
            SetBool(bay2, "unlockedByDefault", false);

            // Robot aspirateur et sa base : dans le coin arrière gauche de l'aire de lavage (zone de nettoyage), qu'il ne quitte
            // jamais ; la base fait face à l'avant de l'aire.
            var dockPosition = WashPadCenter + new Vector3(-WashPadSize.x * 0.5f + 0.35f, WashPadSize.y + content.WorkSurface.thickness,
                WashPadSize.z * 0.5f - 0.3f);
            var dockParts = RugWashWorkshopModels.Dock(gameplay, meshes, content, dockPosition, Quaternion.Euler(0f, 180f, 0f));
            var robotParts = RugWashWorkshopModels.Robot(gameplay, meshes, content);
            robotParts.Root.position = dockParts.Root.position;
            robotParts.Root.rotation = Quaternion.LookRotation(-dockParts.Root.forward);
            var motor = robotParts.Root.gameObject.AddComponent<AudioSource>();
            motor.playOnAwake = false;
            motor.loop = true;
            motor.clip = Clip(RugWashAudioGenerator.RobotHumLoop);
            motor.volume = 0f;
            motor.spatialBlend = 1f;
            motor.rolloffMode = AudioRolloffMode.Linear;
            motor.minDistance = 0.6f;
            motor.maxDistance = 9f;
            var robot = robotParts.Root.gameObject.AddComponent<RobotVacuum>();
            Wire(robot, ("floor", workSurfaceArea), ("orderRug", rug), ("dock", dockParts.Root), ("obstacleRoot", environment), ("audioManager", audio),
                ("motorSource", motor), ("startClip", Clip(RugWashAudioGenerator.RobotStart)), ("stopClip", Clip(RugWashAudioGenerator.RobotStop)),
                ("bumpClip", Clip(RugWashAudioGenerator.RobotBump)), ("dockedClip", Clip(RugWashAudioGenerator.RobotDocked)),
                ("ledRenderer", robotParts.Led), ("sideBrush", robotParts.SideBrush), ("turret", robotParts.Turret));
            SetInt(robot, "playerMask", 1 << RugWashPaths.PlayerLayer);
            SetInt(robot, "obstacleMask", ~((1 << RugWashPaths.PlayerLayer) | (1 << 2) | (1 << 5)));
            Wire(dockParts.Root.gameObject.AddComponent<RobotDock>(), ("robot", robot));

            var workshopState = game.AddComponent<WorkshopStateService>();
            Wire(workshopState, ("workSurface", workSurfaceArea), ("floor", floorSurface), ("robot", robot));

            var wallet = game.AddComponent<WalletManager>();
            var reputation = game.AddComponent<ReputationManager>();
            var upgrades = game.AddComponent<UpgradeManager>();
            Wire(upgrades, ("equipment", player.GetComponent<PlayerEquipment>()), ("tools", toolController));
            WireList(upgrades, "catalog", content.Upgrades);
            var productInventory = player.GetComponent<ProductInventory>();
            var shop = game.AddComponent<ShopManager>();
            Wire(shop, ("reputation", reputation));
            Wire(shop, ("wallet", wallet), ("upgrades", upgrades), ("audioManager", audio), ("products", productInventory),
                ("detergent", player.GetComponent<DetergentSupply>()), ("inventory", player.GetComponent<PlayerInventory>()));
            var orders = game.AddComponent<OrderManager>();
            Wire(orders, ("upgrades", upgrades));
            WireList(orders, "extraBays", new[] { bay2 });
            Wire(orders, ("rug", rug), ("workSurface", workSurface), ("snapshots", snapshots), ("gameState", gameState),
                ("wallet", wallet), ("reputation", reputation), ("audioManager", audio), ("rewardClip", Clip(RugWashAudioGenerator.Reward)));

            // Retours de nettoyage : scan de saleté (touche R) et paliers de propreté.
            var scanner = game.AddComponent<DirtScanner>();
            Wire(scanner, ("input", input), ("gameState", gameState), ("rug", rug), ("orders", orders),
                ("origin", player.GetComponentInChildren<Camera>(true).transform), ("audioManager", audio),
                ("scanClip", Clip(RugWashAudioGenerator.ScanPulse)), ("deniedClip", Clip(RugWashAudioGenerator.ScanDenied)));
            var progressFeedback = game.AddComponent<CleaningProgressFeedback>();
            Wire(progressFeedback, ("rug", rug), ("orders", orders), ("audioManager", audio),
                ("milestoneClip", Clip(RugWashAudioGenerator.Milestone)), ("celebrationPool", cleanPool));
            WireList(progressFeedback, "extraRugs", new[] { rug2 });
            WireList(orders, "orders", content.Orders);

            // Machines fonctionnelles de l'atelier (v0.1.2 jalon 5), cachées tant qu'elles ne sont pas achetées.
            var machineRoot = new GameObject("Machines").transform;
            machineRoot.SetParent(gameplay, false);
            Vector3 rugCenter = WashPadCenter + Vector3.up * WashPadSize.y;
            var machines = new List<WorkshopMachine>
            {
                RugWashMachineModels.DustPlate(machineRoot, meshes, content, rug, orders, Clip(RugWashAudioGenerator.PlateLoop),
                    new Vector3(-2.85f, 0f, -1.6f), 0f, rugCenter, new Vector2(3f, 2f)),
                RugWashMachineModels.Blower(machineRoot, meshes, content, rug, orders, Clip(RugWashAudioGenerator.BlowerLoop),
                    new Vector3(3.4f, 0f, 2.2f), -112f),
                RugWashMachineModels.PreWet(machineRoot, meshes, content, rug, orders, Clip(RugWashAudioGenerator.RinseContactLoop),
                    new Vector3(-3.0f, 0f, 2.95f), 90f, new Vector3(0f, 2.75f, WashPadCenter.z), 4.2f, RoomHeight),
            };
            foreach (var machine in machines)
                if (!machine.IsConfigured(out string missingMachine))
                    throw new InvalidOperationException($"[RugWash] Machine incomplète : {machine.name}.{missingMachine}");
            var rugDexService = game.AddComponent<RugDexService>();
            Wire(rugDexService, ("catalog", content.Catalog), ("rug", rug), ("orders", orders));

            var settings = game.AddComponent<SettingsManager>();
            Wire(settings, ("playerCamera", player.GetComponentInChildren<Camera>(true)));
            Wire(settings, ("audioManager", audio), ("player", fps), ("input", input),
                ("viewModelRig", player.GetComponentInChildren<ViewModelRig>(true)), ("cameraMotion", player.GetComponentInChildren<CameraMotion>(true)));
            var save = game.AddComponent<SaveManager>();
            Wire(save, ("settings", settings), ("wallet", wallet), ("reputation", reputation), ("upgrades", upgrades),
                ("orders", orders), ("shop", shop), ("workshop", workshopState), ("products", productInventory), ("rugDex", rugDexService),
                ("equipment", player.GetComponent<PlayerEquipment>()), ("detergent", player.GetComponent<DetergentSupply>()),
                ("firstPersonAppearance", player.GetComponentInChildren<CharacterAppearance>(true)),
                ("inventory", player.GetComponent<PlayerInventory>()));
            Wire(player.GetComponent<PlayerInventory>(), ("upgrades", upgrades));

            var ui = BuildUI(uiRoot, sprites, performance);
            // Casier contre le mur du fond (grande mise à jour).
            var (lockerObject, lockerDoor) = RugWashDecor.Locker(environment, content, meshes);
            Wire(lockerObject.AddComponent<RugWash.Workshop.Locker>(), ("ui", ui.Locker), ("door", lockerDoor));

            // Machine à laver (annexe) et agrandissement : achats de l'onglet Atelier ; cachés avant l'achat.
            var washerParts = RugWashExtensionBuilder.WasherModel(machineRoot, content, meshes, Clip(RugWashAudioGenerator.PlateLoop),
                RugWashExtensionBuilder.WasherPosition, 0f);
            var washer = washerParts.Root.gameObject.AddComponent<WashingMachine>();
            Wire(washer, ("orders", orders), ("ui", ui.Washer), ("body", washerParts.Body), ("drum", washerParts.Drum), ("door", washerParts.Door),
                ("display", washerParts.Display), ("statusLamp", washerParts.Lamp), ("loopSource", washerParts.Loop), ("audioManager", audio),
                ("doneClip", Clip(RugWashAudioGenerator.Reward)));
            var extension = zone2.AddComponent<WorkshopExtension>();
            Wire(extension, ("bay", bay2), ("closedWall", closedWall), ("annex", annex), ("washer", washer));
            annex.SetActive(false);
            Wire(workshopState, ("workSurface2", workSurfaceArea2), ("washer", washer));

            // Farces entre coéquipiers (v0.1.2 jalon 6) : autorité (solo, hôte), effets reproduits localement, gadgets du joueur.
            var pranks = game.AddComponent<RugWash.Pranks.PrankService>();
            Wire(pranks, ("tuning", content.Pranks), ("rug", rug), ("workSurface", workSurfaceArea));
            WireList(pranks, "extraRugs", new[] { rug2 });
            WireList(pranks, "extraWorkSurfaces", new[] { workSurfaceArea2 });
            var prankEffects = BuildPrankEffects(effects, content);
            var gadgetController = player.GetComponent<RugWash.Pranks.PrankGadgetController>();
            Wire(gadgetController, ("tuning", content.Pranks), ("upgrades", upgrades), ("wallet", wallet), ("service", pranks),
                ("effects", prankEffects), ("splash", ui.Splash), ("rug", rug));

            // Manette (jalon 4) : périphérique actif, curseur, libellés de touches, vibrations.
            Wire(input, ("monitor", ui.Monitor));
            Wire(gameState, ("inputMonitor", ui.Monitor));
            var rumble = game.AddComponent<GamepadRumble>();
            Wire(rumble, ("monitor", ui.Monitor), ("tools", toolController), ("gameState", gameState));
            Wire(settings, ("rumble", rumble));

            // Ordinateur du bureau : seul accès aux missions (acceptation, livraison) et à la boutique.
            var deskInteractable = orderCounter.AddComponent<PanelInteractable>();
            Wire(deskInteractable, ("computer", ui.Computer), ("orderBoard", ui.Board), ("shop", ui.Shop), ("orders", orders));
            SetInt(deskInteractable, "target", (int)PanelInteractable.Target.Computer);

            Wire(bootstrap, ("workshopMusic", Clip(RugWashAudioGenerator.MusicWorkshop)), ("decor", decor), ("pranks", pranks), ("gadgets", gadgetController),
                ("extension", extension), ("washerUI", ui.Washer));
            // En ligne (grande mise à jour) : chat textuel, joueurs, micro du chat vocal (inactif hors ligne).
            var voiceChat = game.AddComponent<RugWash.Network.VoiceChat>();
            Wire(bootstrap, ("chatUI", ui.Chat), ("playersUI", ui.Players), ("voice", voiceChat));
            Wire(bootstrap, ("toolIcons", BuildToolIconStudio(gameplay, content)));
            WireList(bootstrap, "machines", machines);
            Wire(bootstrap,
                ("gameState", gameState),
                ("audioManager", audio),
                ("input", input),
                ("inputMonitor", ui.Monitor),
                ("navigator", ui.Navigator),
                ("rumble", rumble),
                ("playerController", fps),
                ("toolController", toolController),
                ("interactor", interactor),
                ("rug", rug),
                ("snapshots", snapshots),
                ("dirtScanner", scanner),
                ("progressFeedback", progressFeedback),
                ("workSurface", workSurface),
                ("robot", robot),
                ("workshopState", workshopState),
                ("rugDex", rugDexService),
                ("rugDexUI", ui.RugDex),
                ("productInventory", productInventory),
                ("dirtLibrary", content.DirtLibrary),
                ("hud", ui.Hud),
                ("toolbar", ui.Toolbar),
                ("pauseMenu", ui.Pause),
                ("inspection", ui.Inspection),
                ("orderBoard", ui.Board),
                ("shopUI", ui.Shop),
                ("computer", ui.Computer),
                ("results", ui.Results),
                ("orders", orders),
                ("wallet", wallet),
                ("reputation", reputation),
                ("upgrades", upgrades),
                ("shop", shop),
                ("saveManager", save),
                ("settingsManager", settings),
                ("settingsUI", ui.Settings),
                ("debugOverlay", ui.Debug),
                ("inventory", player.GetComponent<PlayerInventory>()),
                ("lockerUI", ui.Locker));
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
                ("uiDeny", Clip(RugWashAudioGenerator.UIDeny)),
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
            RugWashDecor.PolyHavenProps(environment, content, meshes);
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
            Wire(settings, ("playerCamera", camera));
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
            var menuNavigator = CreateEventSystem(null);
            // V0.3.0 : voile en dégradé (opaque à gauche, l'atelier reste visible), logo, titre en Inter ExtraBold.
            var shade = CreateRect("LeftShade", canvas.transform, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(620f, 0f));
            AddImage(shade, sprites.White, new Color(0.03f, 0.04f, 0.05f, 0.9f), sliced: false);
            var fade = CreateRect("LeftShadeFade", canvas.transform, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(620f, 0f), new Vector2(560f, 0f));
            AddImage(fade, sprites.GradientH, new Color(0.03f, 0.04f, 0.05f, 0.9f), sliced: false);
            var logo = CreateRect("Logo", canvas.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(90f, -78f), new Vector2(92f, 92f));
            Card(logo, UITheme.Saffron, outline: false);
            Icon(logo, "Rug", new Vector2(0.5f, 0.5f), Vector2.zero, 64f, UITheme.TextOnAccent);
            AddText(CreateRect("Title", canvas.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(204f, -70f), new Vector2(760f, 80f)),
                "RugWash Simulator", 66, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            AddText(CreateRect("Subtitle", canvas.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(207f, -146f), new Vector2(760f, 34f)),
                "ATELIER DE NETTOYAGE DE TAPIS", 21, TextAnchor.MiddleLeft, UITheme.Saffron, FontStyle.Bold, shadow: false);

            var column = CreateRect("Buttons", canvas.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(90f, -90f), new Vector2(460f, 700f));
            AddVerticalLayout(column.gameObject, 14, new RectOffset(0, 0, 0, 0), TextAnchor.UpperLeft);
            var play = CreateButton("Play", column, "Jouer (solo)", sprites.Rounded, new Vector2(460f, 70f));
            var resume = CreateButton("Continue", column, "Continuer", sprites.Rounded, new Vector2(460f, 70f));
            var summary = AddText(CreateRect("ContinueSummary", column, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(460f, 30f)),
                string.Empty, 19, TextAnchor.MiddleLeft, UITheme.Money);
            summary.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
            var multiplayerMenuButton = CreateButton("Multiplayer", column, "Multijoueur", sprites.Rounded, new Vector2(460f, 70f));
            var characterMenuButton = CreateButton("Character", column, "Personnage", sprites.Rounded, new Vector2(460f, 70f));
            var rugDexMenuButton = CreateButton("RugDex", column, "RugDex", sprites.Rounded, new Vector2(460f, 70f));
            var settingsButton = CreateButton("Settings", column, "Paramètres", sprites.Rounded, new Vector2(460f, 70f));
            var quit = CreateButton("Quit", column, "Quitter", sprites.Rounded, new Vector2(460f, 70f));
            // Navigation à la manette : la colonne de boutons est la fenêtre de base du menu.
            column.gameObject.AddComponent<NavigationScope>().FirstSelected = play;

            var message = AddText(CreateRect("Message", canvas.transform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(90f, 60f), new Vector2(640f, 80f)),
                string.Empty, 20, TextAnchor.LowerLeft, UITheme.Warning);
            AddText(CreateRect("Version", canvas.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-30f, 24f), new Vector2(600f, 30f)),
                "Version 0.3.0 · démo en développement — sons temporaires", 17, TextAnchor.LowerRight, UITheme.TextMuted);
            BuildNewsPanel(canvas, sprites);

            var loading = Stretch("Loading", canvas.transform);
            AddImage(loading, sprites.White, new Color(0.03f, 0.04f, 0.05f, 0.92f), sliced: false, raycast: true);
            AddText(Stretch("Text", loading), "Chargement de l'atelier…", 40, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);

            var rugDexUI = BuildRugDexUI(canvas, sprites);
            var characterUI = BuildCharacterUI(canvas, sprites, content, meshes);
            var settingsUI = BuildSettingsUI(canvas, sprites);
            var multiplayerUI = BuildMultiplayerUI(canvas, sprites, content.Network);
            // Après la fenêtre Multijoueur : la fenêtre des emplacements s'ouvre par-dessus (choix de l'emplacement en ligne).
            var saveSlotsUI = BuildSaveSlotsUI(canvas, sprites);

            var controller = menu.AddComponent<MainMenuController>();
            Wire(controller, ("multiplayerUI", multiplayerUI), ("multiplayerButton", multiplayerMenuButton), ("networkContent", content.Network));
            Wire(controller, ("rugDexUI", rugDexUI), ("rugDexButton", rugDexMenuButton), ("rugCatalog", content.Catalog),
                ("dirtLibrary", content.DirtLibrary));
            WireList(controller, "orders", content.Orders);
            Wire(controller, ("characterUI", characterUI), ("characterButton", characterMenuButton));
            Wire(controller, ("inputMonitor", menuNavigator.Monitor));
            Wire(controller, ("menuMusic", Clip(RugWashAudioGenerator.MusicMenu)));
            Wire(controller, ("save", save), ("settings", settings), ("audioManager", audio), ("settingsUI", settingsUI),
                ("playButton", play), ("continueButton", resume), ("settingsButton", settingsButton), ("quitButton", quit),
                ("continueSummary", summary), ("messageLabel", message), ("saveSlotsUI", saveSlotsUI), ("loadingPanel", loading.gameObject));
            if (!controller.IsConfigured(out string missing))
                throw new InvalidOperationException($"[RugWash] Menu principal incomplet : {missing}");
            if (!settingsUI.IsConfigured(out missing))
                throw new InvalidOperationException($"[RugWash] Paramètres incomplets : {missing}");

            loading.gameObject.SetActive(false);
            EditorSceneManager.SaveScene(scene, RugWashPaths.MainMenuScene);
            RugWashProjectBootstrapper.SyncBuildScenes();
            AssetDatabase.SaveAssets();
            Debug.Log($"[RugWash] Scène générée : {RugWashPaths.MainMenuScene}");
            return scene;
        }

        /// <summary>
        /// Fenêtre « Personnage » (v0.1.2) : aperçu 3D de la silhouette (scène isolée sous l'atelier, filmée dans une texture
        /// seulement fenêtre ouverte) et sélecteurs des options réellement disponibles.
        /// </summary>
        static CharacterMenuUI BuildCharacterUI(Canvas canvas, UISprites sprites, RugWashContentBuilder.Content c, MeshStore meshes)
        {
            var window = BuildWindow(canvas.transform, "Character", new Vector2(1300f, 860f), sprites, "Personnage", out var panel, out var close);
            var frame = CreateRect("PreviewFrame", window, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(40f, -10f), new Vector2(460f, 640f));
            Card(frame, new Color(0.05f, 0.06f, 0.075f, 0.98f));
            var image = Stretch("Preview", frame, 8f).gameObject.AddComponent<RawImage>();
            image.raycastTarget = false;
            var rotateLeft = CreateButton("RotateLeft", window, "‹ Tourner", sprites.Rounded, new Vector2(220f, 56f));
            PlaceBottomLeft((RectTransform)rotateLeft.transform, new Vector2(40f, 36f));
            var rotateRight = CreateButton("RotateRight", window, "Tourner ›", sprites.Rounded, new Vector2(220f, 56f));
            PlaceBottomLeft((RectTransform)rotateRight.transform, new Vector2(280f, 36f));

            var options = CreateRect("Options", window, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -130f), new Vector2(700f, 260f));
            AddVerticalLayout(options.gameObject, 18, new RectOffset(0, 0, 0, 0), TextAnchor.UpperLeft);
            var outfit = StepperRow(options, "Tenue de travail", sprites);
            var cap = StepperRow(options, "Casquette", sprites);
            var gloves = StepperRow(options, "Gants", sprites);
            AddText(CreateRect("Note", window, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -420f), new Vector2(700f, 200f)),
                "Couleurs de la tenue et des gants, casquette ou cheveux à l'air : vos choix sont sauvegardés " +
                "et visibles par les autres joueurs en ligne.",
                20, TextAnchor.UpperLeft, UITheme.TextSecondary);
            panel.GetComponent<NavigationScope>().FirstSelected = outfit.NextButton;

            // Aperçu : silhouette et éclairage propres, loin sous la salle (la caméra du menu ne les voit pas).
            var rig = new GameObject("CharacterPreview").transform;
            rig.position = new Vector3(0f, -50f, 0f);
            var model = new GameObject("Model").transform;
            model.SetParent(rig, false);
            model.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var parts = RugWashNetworkBuilder.BuildAvatarVisuals(model, c, meshes);
            var appearance = RugWashNetworkBuilder.AddAppearance(parts.Visuals, c);
            var cameraGo = new GameObject("PreviewCamera");
            cameraGo.transform.SetParent(rig, false);
            cameraGo.transform.localPosition = new Vector3(0f, 1.05f, -3.1f);
            cameraGo.transform.localRotation = Quaternion.Euler(4f, 0f, 0f);
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.07f, 0.09f, 0.11f);
            camera.fieldOfView = 34f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 8f;
            camera.enabled = false;
            foreach (var (name, position, intensity) in new[] { ("Key", new Vector3(1.4f, 2.4f, -2f), 3.5f), ("Fill", new Vector3(-1.6f, 1.6f, -1.6f), 1.6f), ("Rim", new Vector3(0f, 2.2f, 1.8f), 2f) })
            {
                var lightGo = new GameObject($"PreviewLight_{name}");
                lightGo.transform.SetParent(rig, false);
                lightGo.transform.localPosition = position;
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 7f;
                light.intensity = intensity;
                light.shadows = LightShadows.None;
                lightGo.AddComponent<UniversalAdditionalLightData>();
            }

            var ui = window.parent.parent.gameObject.AddComponent<CharacterMenuUI>();
            Wire(ui, ("panel", panel), ("preview", image), ("outfitStepper", outfit), ("capStepper", cap), ("glovesStepper", gloves),
                ("rotateLeftButton", rotateLeft), ("rotateRightButton", rotateRight), ("closeButton", close), ("previewModel", model),
                ("previewCamera", camera), ("previewAppearance", appearance));
            if (!ui.IsConfigured(out string missing)) throw new InvalidOperationException($"[RugWash] Fenêtre Personnage incomplète : {missing}");
            panel.SetActive(false);
            return ui;
        }

        /// <summary>
        /// Zone « Actualités » à droite du menu : annonces locales versionnées (Resources/RugWashNews.json), présentées
        /// comme telles — aucune connexion n'est simulée.
        /// </summary>
        static void BuildNewsPanel(Canvas canvas, UISprites sprites)
        {
            var news = CreateRect("News", canvas.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -40f), new Vector2(640f, 600f));
            Card(news, new Color(0.05f, 0.065f, 0.08f, 0.88f));
            AccentBar(news, new Vector2(0f, 1f), new Vector2(24f, -41f), new Vector2(6f, 30f), UITheme.Saffron);
            AddText(CreateRect("Header", news, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(14f, -18f), new Vector2(570f, 46f)),
                "Actualités", 32, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            AddText(CreateRect("Source", news, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -64f), new Vector2(590f, 28f)),
                "Annonces locales incluses dans cette version du jeu (hors ligne)", 17, TextAnchor.MiddleLeft, Muted);
            var body = AddText(CreateRect("Body", news, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -106f), new Vector2(590f, 474f)),
                string.Empty, 21, TextAnchor.UpperLeft, UITheme.TextSecondary);
            body.supportRichText = true;
            body.verticalOverflow = VerticalWrapMode.Truncate;
            body.resizeTextForBestFit = true;
            body.resizeTextMinSize = 14;
            body.resizeTextMaxSize = 21;
            var panel = news.gameObject.AddComponent<NewsPanelUI>();
            Wire(panel, ("bodyLabel", body));
            if (!panel.IsConfigured(out string missing))
                throw new InvalidOperationException($"[RugWash] Actualités incomplètes : {missing}");
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
            // V0.3.0 : ambiance plus lumineuse et plus chaude au niveau des murs (rebond du plâtre crème), sol plus sombre.
            RenderSettings.ambientSkyColor = new Color(0.6f, 0.63f, 0.68f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.47f, 0.43f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.23f, 0.21f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.fog = false;
        }

        /// <summary>Salle de l'atelier ; retourne la cloison qui ferme l'ouverture vers l'annexe (agrandissement).</summary>
        static GameObject BuildRoom(Transform parent, RugWashContentBuilder.Content c, MeshStore meshes)
        {
            var room = new GameObject("Room").transform;
            room.SetParent(parent, false);
            float w = RoomHalfX * 2f + WallThickness * 2f, d = RoomHalfZ * 2f + WallThickness * 2f;

            CreateBox(meshes, "Floor", room, new Vector3(0f, -0.1f, 0f), new Vector3(w, 0.2f, d), c["Concrete"]);
            CreateBox(meshes, "Ceiling", room, new Vector3(0f, RoomHeight + 0.1f, 0f), new Vector3(w, 0.2f, d), c["Ceiling"]);
            CreateBox(meshes, "Wall_Back", room, new Vector3(0f, RoomHeight / 2f, RoomHalfZ + WallThickness / 2f), new Vector3(w, RoomHeight, WallThickness), c["Plaster"]);
            CreateBox(meshes, "Wall_Front", room, new Vector3(0f, RoomHeight / 2f, -RoomHalfZ - WallThickness / 2f), new Vector3(w, RoomHeight, WallThickness), c["Plaster"]);
            CreateBox(meshes, "Wall_Left", room, new Vector3(-RoomHalfX - WallThickness / 2f, RoomHeight / 2f, 0f), new Vector3(WallThickness, RoomHeight, d), c["Plaster"]);
            // Grande mise à jour : mur droit percé vers l'annexe (cloison retirée à l'achat de l'agrandissement).
            var closedWall = RugWashExtensionBuilder.RightWall(room, c, meshes);

            // Soubassement peint (bas de mur lavable).
            const float band = 1.1f, inset = 0.012f;
            CreateBox(meshes, "Wainscot_Back", room, new Vector3(0f, band / 2f, RoomHalfZ - inset), new Vector3(RoomHalfX * 2f, band, 0.02f), c["Wainscot"], collider: false);
            CreateBox(meshes, "Wainscot_Front", room, new Vector3(0f, band / 2f, -RoomHalfZ + inset), new Vector3(RoomHalfX * 2f, band, 0.02f), c["Wainscot"], collider: false);
            CreateBox(meshes, "Wainscot_Left", room, new Vector3(-RoomHalfX + inset, band / 2f, 0f), new Vector3(0.02f, band, RoomHalfZ * 2f), c["Wainscot"], collider: false);

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
            return closedWall;
        }

        static Renderer BuildWashBay(Transform parent, RugWashContentBuilder.Content c, MeshStore meshes, AudioManager audio)
        {
            var pad = BuildWashPad(parent, c, meshes, audio, WashPadCenter, "WashBay");
            CreateSign(parent, "Sign_WashBay", "AIRE DE LAVAGE", new Vector3(0f, 1.95f, RoomHalfZ - 0.04f), Quaternion.identity, meshes, c);
            return pad;
        }

        /// <summary>Aire de lavage : dalle, bordures, caniveau (vidange de la cuve de l'extracteur) ; zone 1 et zone 2.</summary>
        internal static Renderer BuildWashPad(Transform parent, RugWashContentBuilder.Content c, MeshStore meshes, AudioManager audio, Vector3 center, string name)
        {
            var bay = new GameObject(name).transform;
            bay.SetParent(parent, false);
            bay.localPosition = center;

            var pad = CreateBox(meshes, "WashPad", bay, new Vector3(0f, WashPadSize.y / 2f, 0f), WashPadSize, c["WashPad"]);
            float hx = WashPadSize.x / 2f, hz = WashPadSize.z / 2f;
            CreateBox(meshes, "Curb_Back", bay, new Vector3(0f, 0.07f, hz + 0.04f), new Vector3(WashPadSize.x + 0.16f, 0.14f, 0.08f), c["Hazard"]);
            CreateBox(meshes, "Curb_Front", bay, new Vector3(0f, 0.07f, -hz - 0.04f), new Vector3(WashPadSize.x + 0.16f, 0.14f, 0.08f), c["Hazard"]);
            CreateBox(meshes, "Curb_Left", bay, new Vector3(-hx - 0.04f, 0.07f, 0f), new Vector3(0.08f, 0.14f, WashPadSize.z), c["Hazard"]);
            CreateBox(meshes, "Curb_Right", bay, new Vector3(hx + 0.04f, 0.07f, 0f), new Vector3(0.08f, 0.14f, WashPadSize.z), c["Hazard"]);

            // Caniveau d'évacuation.
            var drain = CreateBox(meshes, "Drain_Channel", bay, new Vector3(hx - 0.22f, WashPadSize.y + 0.002f, 0f), new Vector3(0.2f, 0.006f, WashPadSize.z - 0.3f), c["Grate"]);
            CreateBox(meshes, "Drain_Frame", bay, new Vector3(hx - 0.22f, WashPadSize.y + 0.001f, 0f), new Vector3(0.24f, 0.004f, WashPadSize.z - 0.26f), c["DarkMetal"], collider: false);

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
            DepthTestedText(mesh);
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
            spot.range = 7.5f;
            spot.intensity = 7.5f;
            spot.color = new Color(1f, 0.95f, 0.87f);
            spot.shadows = LightShadows.Soft;
            spot.shadowStrength = 0.8f;
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
                light.range = 9f;
                light.intensity = 3f;
                light.color = new Color(1f, 0.96f, 0.9f);
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
                dayLight.spotAngle = 95f;
                dayLight.innerSpotAngle = 40f;
                dayLight.range = 9f;
                dayLight.intensity = 3.4f;
                dayLight.color = new Color(0.72f, 0.82f, 1f);
                dayLight.shadows = LightShadows.None;
                day.AddComponent<UniversalAdditionalLightData>();

                // V0.3.0 : rai de lumière très léger sous la fenêtre (volume additif, sans ombre ni collision).
                var shaftGo = new GameObject("LightShaft");
                shaftGo.transform.SetParent(parent, false);
                shaftGo.transform.localPosition = new Vector3(x, 2.65f, RoomHalfZ - 0.06f);
                shaftGo.AddComponent<MeshFilter>().sharedMesh = meshes.Add("LightShaft", () =>
                    LightShaftMesh(new Vector2(1.7f, 0.78f), new Vector3(0f, -Mathf.Sin(58f * Mathf.Deg2Rad), -Mathf.Cos(58f * Mathf.Deg2Rad)), 3.1f, 1.25f));
                var shaftRenderer = shaftGo.AddComponent<MeshRenderer>();
                shaftRenderer.sharedMaterial = c["LightShaft"];
                shaftRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                shaftRenderer.receiveShadows = false;
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

        /// <summary>
        /// Volume d'un rai de lumière : rectangle de la fenêtre (plan XY, centré) prolongé selon <paramref name="direction"/> sur
        /// <paramref name="length"/> m en s'élargissant de <paramref name="spread"/>. Quatre faces ; UV u en travers, v le long.
        /// </summary>
        static Mesh LightShaftMesh(Vector2 window, Vector3 direction, float length, float spread)
        {
            var near = new[]
            {
                new Vector3(-window.x * 0.5f, -window.y * 0.5f, 0f), new Vector3(window.x * 0.5f, -window.y * 0.5f, 0f),
                new Vector3(window.x * 0.5f, window.y * 0.5f, 0f), new Vector3(-window.x * 0.5f, window.y * 0.5f, 0f),
            };
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = near[i], b = near[(i + 1) % 4];
                Vector3 fa = a * spread + direction * length, fb = b * spread + direction * length;
                int start = vertices.Count;
                vertices.AddRange(new[] { a, b, fb, fa });
                uvs.AddRange(new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
                triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
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
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.6f);
            // V0.3.0 : étalonnage plus franc — exposition relevée, contraste, ombres légèrement froides et hautes lumières chaudes.
            var color = Ensure<ColorAdjustments>(profile);
            color.postExposure.Override(0.45f);
            color.contrast.Override(16f);
            color.saturation.Override(6f);
            Ensure<WhiteBalance>(profile).temperature.Override(4f);
            var split = Ensure<SplitToning>(profile);
            split.shadows.Override(new Color(0.42f, 0.5f, 0.56f));
            split.highlights.Override(new Color(0.58f, 0.52f, 0.44f));
            split.balance.Override(10f);
            var vignette = Ensure<Vignette>(profile);
            vignette.intensity.Override(0.24f);
            vignette.smoothness.Override(0.5f);
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

        static RugCleaningController BuildRug(Transform parent, RugWashContentBuilder.Content c) =>
            BuildRug(parent, c, "Rug", WashPadCenter + Vector3.up * WashPadSize.y, RugWashPaths.Root + "/Prefabs/RugPreview_Mesh.asset");

        static RugCleaningController BuildRug(Transform parent, RugWashContentBuilder.Content c, string name, Vector3 position, string previewPath)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.layer = RugWashPaths.RugLayer;

            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            var collider = go.AddComponent<BoxCollider>();
            var dirtRenderer = go.AddComponent<RugDirtRenderer>();
            var rug = go.AddComponent<RugCleaningController>();

            // Aperçu en mode édition : le tapis réel (maillage, saleté) est créé au lancement.
            var definition = c.Oriental;
            filter.sharedMesh = PreviewRugMesh(definition, previewPath);
            c["RugSurface"].SetTexture("_BaseMap", definition.patternTexture);
            renderer.sharedMaterials = new[] { c["RugSurface"], c["RugFringe"] };
            collider.size = new Vector3(definition.sizeMeters.x, Mathf.Max(0.01f, definition.thickness), definition.sizeMeters.y);
            collider.center = new Vector3(0f, definition.thickness / 2f, 0f);

            Wire(dirtRenderer, ("targetRenderer", renderer), ("rugMaterialTemplate", c["RugSurface"]), ("fringeMaterialTemplate", c["RugFringe"]));
            Wire(rug, ("definition", definition), ("dirtLibrary", c.DirtLibrary), ("dirtRenderer", dirtRenderer),
                ("meshFilter", filter), ("surfaceCollider", collider), ("tuning", c.Tuning), ("productLibrary", c.ProductLibrary));
            // Pas de tapis d'entraînement : le tapis n'apparaît qu'avec une commande (la définition sert d'aperçu dans l'éditeur).
            SetBool(rug, "loadOnAwake", false);
            return rug;
        }

        /// <summary>
        /// Surface nettoyable de l'atelier (sol, plan de travail) : même composant, même grille, même rendu que le tapis.
        /// Couche par défaut (absente des captures avant/après du tapis) ; le maillage est créé au lancement.
        /// </summary>
        static RugCleaningController BuildSurface(Transform parent, string name, RugDefinition definition, Material material,
            RugWashContentBuilder.Content c, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            var collider = go.AddComponent<BoxCollider>();
            collider.size = new Vector3(definition.sizeMeters.x, 0.01f, definition.sizeMeters.y);
            collider.center = new Vector3(0f, definition.thickness - 0.005f, 0f);
            var dirtRenderer = go.AddComponent<RugDirtRenderer>();
            var surface = go.AddComponent<RugCleaningController>();
            Wire(dirtRenderer, ("targetRenderer", renderer), ("rugMaterialTemplate", material), ("fringeMaterialTemplate", c["RugFringe"]));
            Wire(surface, ("definition", definition), ("dirtLibrary", c.DirtLibrary), ("dirtRenderer", dirtRenderer),
                ("meshFilter", filter), ("surfaceCollider", collider), ("tuning", c.Tuning));
            return surface;
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
            camera.cullingMask = ~(1 << RugWashPaths.ToolIconLayer);
            camera.fieldOfView = 70f;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 60f;
            cameraGo.AddComponent<AudioListener>();
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            input = player.AddComponent<PlayerInputReader>();
            var productInventory = player.AddComponent<ProductInventory>();
            Wire(productInventory, ("library", c.ProductLibrary));
            var equipment = player.AddComponent<PlayerEquipment>();
            var detergent = player.AddComponent<DetergentSupply>();
            Wire(equipment, ("productInventory", productInventory), ("input", input), ("detergent", detergent));
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
            var ctx = new ToolBuildContext
            {
                Root = toolsRoot, Camera = rig, Content = c, Effects = effects, Audio = audio, CleanPool = cleanPool, Meshes = meshes,
                Equipment = player.GetComponent<PlayerEquipment>(),
                Drain = GameObject.Find("Drain_Channel")?.GetComponent<Collider>(),
            };
            var tools = new CleaningToolBase[]
            {
                BuildVacuum(ctx), BuildSoap(ctx), BuildBrush(ctx), BuildRinse(ctx), BuildExtractor(ctx),
                BuildFoamCannon(ctx), BuildBroom(ctx), BuildDetailBrush(ctx), BuildBucket(ctx), BuildSqueegee(ctx), BuildRotary(ctx),
            };

            toolController = player.AddComponent<PlayerToolController>();
            Wire(toolController, ("playerCamera", camera), ("input", input), ("equipment", equipment), ("gameState", gameState),
                ("targetIndicator", indicator), ("audioManager", audio), ("movement", fps));
            WireList(toolController, "tools", tools);
            int aimMask = ~((1 << RugWashPaths.PlayerLayer) | (1 << 2) | (1 << 5));
            SetInt(toolController, "aimMask", aimMask);

            // Gadgets de farce (v0.1.2 jalon 6) : batte et fusil-jouet tenus en main (cachés tant qu'aucun gadget n'est sorti).
            var batModel = RugWashToolModels.Bat(rig, meshes, c);
            var blasterModel = RugWashToolModels.Blaster(rig, meshes, c);
            RugWashToolModels.Finish(batModel);
            RugWashToolModels.Finish(blasterModel);
            // Personnalisation (v0.1.2) : couleur des gants choisie dans « Personnage », aussi à la première personne.
            var firstPersonLook = RugWashNetworkBuilder.AddAppearance(rig, c);
            Wire(rig.gameObject.AddComponent<ViewModelRig>(), ("input", input), ("controller", fps), ("tools", toolController),
                ("audioManager", audio), ("equipClip", Clip(RugWashAudioGenerator.ToolEquip)));
            Wire(cameraGo.AddComponent<CameraMotion>(), ("controller", fps));
            // Chute contrôlée (ragdoll du joueur local, v0.1.2) : caméra au sol, contrôles coupés, relevé garanti.
            var knockdown = player.AddComponent<PlayerKnockdown>();
            Wire(knockdown, ("controller", fps), ("cameraMotion", cameraGo.GetComponent<CameraMotion>()), ("tools", toolController), ("input", input));
            SetInt(knockdown, "surfaceMask", aimMask);
            // Le reste (autorité, effets, boutique, interface) est câblé avec la scène.
            var gadgets = player.AddComponent<RugWash.Pranks.PrankGadgetController>();
            Wire(gadgets, ("input", input), ("playerCamera", camera), ("tools", toolController), ("gameState", gameState), ("audioManager", audio),
                ("knockdown", knockdown), ("batModel", batModel.Root), ("blasterModel", blasterModel.Root), ("blasterTip", blasterModel.Tip));
            SetInt(gadgets, "aimMask", aimMask);
            var footprints = player.AddComponent<FootprintTrail>();
            Wire(footprints, ("controller", fps), ("equipment", player.GetComponent<PlayerEquipment>()));
            SetInt(footprints, "surfaceMask", aimMask);
            var footsteps = player.AddComponent<FootstepAudio>();
            Wire(footsteps, ("controller", fps), ("audioManager", audio));
            var stepClips = new List<AudioClip>();
            foreach (var path in RugWashAudioGenerator.Footsteps) stepClips.Add(Clip(path));
            WireList(footsteps, "steps", stepClips);

            // Inventaire à 4 emplacements (grande mise à jour) : seuls les objets portés se sélectionnent ; le reste au casier.
            var inventory = player.AddComponent<PlayerInventory>();
            Wire(inventory, ("tools", toolController), ("gadgets", gadgets));
            Wire(toolController, ("inventory", inventory));
            Wire(gadgets, ("inventory", inventory));

            interactor = player.AddComponent<PlayerInteractor>();
            Wire(interactor, ("playerCamera", camera), ("input", input), ("equipment", equipment), ("gameState", gameState));
            SetInt(interactor, "mask", aimMask);
            Wire(player.GetComponent<PlayerKnockdown>(), ("interactor", interactor));
            return player;
        }

        sealed class ToolBuildContext
        {
            public Transform Root, Camera, Effects;
            public RugWashContentBuilder.Content Content;
            public AudioManager Audio;
            public CleaningVFXPool CleanPool;
            public MeshStore Meshes;
            public PlayerEquipment Equipment;
            /// <summary>Caniveau de l'aire de lavage (raclette).</summary>
            public Collider Drain;
        }

        /// <summary>Niveau de la réserve de détergent visible dans le contenant de l'outil (shader « Liquid Container »).</summary>
        static void AddContainerLevel(ToolBuildContext ctx, RugWashToolModels.Model model, string partName)
        {
            var part = model.Root.Find(partName);
            if (part == null || ctx.Equipment == null) return;
            var level = part.gameObject.AddComponent<ContainerLevel>();
            Wire(level, ("target", part.GetComponent<MeshRenderer>()), ("equipment", ctx.Equipment));
        }

        static AudioClip Clip(string path) => AssetDatabase.LoadAssetAtPath<AudioClip>(path);

        /// <summary>Crée l'objet logique de l'outil avec sa source audio en boucle.</summary>
        static T CreateTool<T>(ToolBuildContext ctx, string name, ToolDefinition definition, RugWashToolModels.Model model, string loopClip,
            float baseVolume, float contactBoost, float contactPitch, string contactClip, float contactVolume) where T : CleaningToolBase
        {
            var go = new GameObject(name);
            go.transform.SetParent(ctx.Root, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            var toolAudio = go.AddComponent<CleaningAudioController>();
            Wire(toolAudio, ("audioManager", ctx.Audio), ("loop", Clip(loopClip)), ("contactLoop", Clip(contactClip)));
            var serialized = new SerializedObject(toolAudio);
            serialized.FindProperty("baseVolume").floatValue = baseVolume;
            serialized.FindProperty("contactVolumeBoost").floatValue = contactBoost;
            serialized.FindProperty("contactPitchShift").floatValue = contactPitch;
            serialized.FindProperty("contactLoopVolume").floatValue = contactVolume;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var tool = go.AddComponent<T>();
            Wire(tool, ("definition", definition), ("viewModel", model.Root), ("aimTip", model.Tip), ("toolAudio", toolAudio),
                ("cleanFeedback", ctx.CleanPool), ("goldenSkin", RugWashToolModels.Gild(model, ctx.Content)));
            if (model.Head != null && model.Shaft != null)
            {
                // Outil de contact, vue à la première personne : le manche garde sa longueur (aucun allongement visible ;
                // un manche de 1 à 2 m pivotant vers une cible proche enfonçait la tête dans le sol). Il peut seulement se
                // raccourcir devant un obstacle proche. Les silhouettes des autres joueurs gardent le manche télescopique.
                Wire(tool, ("reachHead", model.Head), ("reachShaft", model.Shaft));
                SetFloat(tool, "reachShaftLength", model.ShaftLength);
                SetFloat(tool, "maxReachExtension", 0f);
            }
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
            var tool = CreateTool<VacuumTool>(ctx, "Vacuum", c.Vacuum, model, RugWashAudioGenerator.VacuumLoop, 0.45f, 0.1f, -0.06f,
                RugWashAudioGenerator.VacuumContactLoop, 0.35f);
            Wire(tool, ("suctionParticles", suction), ("nozzle", model.Tip));
            return tool;
        }

        static SoapSprayerTool BuildSoap(ToolBuildContext ctx)
        {
            var c = ctx.Content;
            var model = RugWashToolModels.Soap(ctx.Camera, ctx.Meshes, c);
            RugWashToolModels.Finish(model);
            AddContainerLevel(ctx, model, "Bottle");
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
            var tool = CreateTool<SoapSprayerTool>(ctx, "Soap", c.Soap, model, RugWashAudioGenerator.SoapLoop, 0.4f, 0.1f, 0f,
                RugWashAudioGenerator.FoamContactLoop, 0.4f);
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
                Color = new Color(0.36f, 0.27f, 0.18f, 1f), Gravity = 1f, MaxParticles = 160, Shape = ParticleSystemShapeType.Cone, Angle = 55f,
                Radius = 0.08f, Rate = 0f, Loop = true, PlayOnAwake = true,
            });
            var tool = CreateTool<BrushTool>(ctx, "Brush", c.Brush, model, RugWashAudioGenerator.BrushLoop, 0.25f, 0.15f, 0.05f,
                RugWashAudioGenerator.BrushContactLoop, 0.45f);
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
            var tool = CreateTool<WaterRinseTool>(ctx, "Rinse", c.Rinse, model, RugWashAudioGenerator.RinseLoop, 0.45f, 0.1f, -0.05f,
                RugWashAudioGenerator.RinseContactLoop, 0.4f);
            Wire(tool, ("jetParticles", jet), ("splashParticles", splash));
            SetFloat(tool, "jetRate", 520f);
            return tool;
        }

        /// <summary>Seau d'eau (kit de départ) : même outil de rinçage que la lance, versé de près, moins puissant.</summary>
        static WaterRinseTool BuildBucket(ToolBuildContext ctx)
        {
            var c = ctx.Content;
            var model = RugWashToolModels.Bucket(ctx.Camera, ctx.Meshes, c);
            RugWashToolModels.Finish(model);
            var pour = CreateParticles("WaterPour", model.Tip, c["WaterJet"], new ParticleConfig
            {
                Lifetime = new Vector2(0.35f, 0.5f), Speed = new Vector2(2.4f, 3.4f), Size = new Vector2(0.03f, 0.06f), Color = Color.white,
                Gravity = 1.1f, MaxParticles = 300, Shape = ParticleSystemShapeType.Cone, Angle = 12f, Radius = 0.04f, Rate = 0f,
                Loop = true, PlayOnAwake = true, SizeGrowth = 1.6f, Stretch = 0.03f,
            });
            var splash = CreateParticles("BucketSplash", ctx.Effects, c["Splash"], new ParticleConfig
            {
                Lifetime = new Vector2(0.25f, 0.5f), Speed = new Vector2(0.8f, 2.2f), Size = new Vector2(0.015f, 0.04f),
                Color = Color.white, Gravity = 1.3f, MaxParticles = 220, Shape = ParticleSystemShapeType.Hemisphere,
                Radius = 0.08f, Rate = 0f, Loop = true, PlayOnAwake = true, Stretch = 0.02f,
            });
            var tool = CreateTool<WaterRinseTool>(ctx, "Bucket", c.Bucket, model, RugWashAudioGenerator.RinseLoop, 0.25f, 0.1f, -0.25f,
                RugWashAudioGenerator.RinseContactLoop, 0.35f);
            Wire(tool, ("jetParticles", pour), ("splashParticles", splash));
            SetFloat(tool, "jetRate", 180f);
            SetFloat(tool, "splashRate", 110f);
            SetBool(tool, "pourMotion", true);
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
            var tool = CreateTool<WaterExtractorTool>(ctx, "Extractor", c.Extractor, model, RugWashAudioGenerator.ExtractorLoop, 0.45f, 0.12f, -0.1f,
                RugWashAudioGenerator.ExtractorContactLoop, 0.45f);
            Wire(tool, ("suctionDroplets", droplets), ("nozzle", model.Tip));
            return tool;
        }

        static FoamCannonTool BuildFoamCannon(ToolBuildContext ctx)
        {
            var c = ctx.Content;
            var model = RugWashToolModels.FoamCannon(ctx.Camera, ctx.Meshes, c);
            RugWashToolModels.Finish(model);
            AddContainerLevel(ctx, model, "Canister");
            var stream = CreateParticles("FoamStream", model.Tip, c["Foam"], new ParticleConfig
            {
                Lifetime = new Vector2(0.35f, 0.5f), Speed = new Vector2(6f, 8f), Size = new Vector2(0.03f, 0.06f), Color = Color.white,
                Gravity = 0.4f, MaxParticles = 260, Shape = ParticleSystemShapeType.Cone, Angle = 9f, Radius = 0.02f, Rate = 0f,
                Loop = true, PlayOnAwake = true, SizeGrowth = 2.5f,
            });
            var puffs = CreateParticles("FoamImpact", ctx.Effects, c["Foam"], new ParticleConfig
            {
                Lifetime = new Vector2(0.6f, 1f), Speed = new Vector2(0.05f, 0.2f), Size = new Vector2(0.05f, 0.1f), Color = Color.white,
                Gravity = 0f, MaxParticles = 120, Shape = ParticleSystemShapeType.Circle, Radius = 0.25f, Rate = 0f, Loop = true, PlayOnAwake = true,
            });
            var tool = CreateTool<FoamCannonTool>(ctx, "FoamCannon", c.FoamCannon, model, RugWashAudioGenerator.FoamCannonLoop, 0.4f, 0.08f, 0f,
                RugWashAudioGenerator.FoamContactLoop, 0.45f);
            Wire(tool, ("foamStream", stream), ("impactPuffs", puffs));
            return tool;
        }

        static BroomTool BuildBroom(ToolBuildContext ctx)
        {
            var c = ctx.Content;
            var model = RugWashToolModels.Broom(ctx.Camera, ctx.Meshes, c);
            RugWashToolModels.Finish(model);
            var dust = CreateParticles("BroomDust", ctx.Effects, c["Dust"], new ParticleConfig
            {
                Lifetime = new Vector2(0.4f, 0.8f), Speed = new Vector2(0.2f, 0.7f), Size = new Vector2(0.02f, 0.05f), Color = Color.white,
                Gravity = 0.3f, MaxParticles = 160, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.15f, Rate = 0f, Loop = true, PlayOnAwake = true,
            });
            var water = CreateParticles("BroomWater", ctx.Effects, c["Droplet"], new ParticleConfig
            {
                Lifetime = new Vector2(0.3f, 0.5f), Speed = new Vector2(0.4f, 1.2f), Size = new Vector2(0.015f, 0.03f), Color = Color.white,
                Gravity = 1.2f, MaxParticles = 160, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.12f, Rate = 0f, Loop = true,
                PlayOnAwake = true, Stretch = 0.02f,
            });
            var tool = CreateTool<BroomTool>(ctx, "Broom", c.Broom, model, RugWashAudioGenerator.BroomLoop, 0.35f, 0.1f, 0f,
                RugWashAudioGenerator.BroomContactLoop, 0.5f);
            Wire(tool, ("dustPuffs", dust), ("waterPush", water));
            return tool;
        }

        static SqueegeeTool BuildSqueegee(ToolBuildContext ctx)
        {
            var c = ctx.Content;
            var model = RugWashToolModels.Squeegee(ctx.Camera, ctx.Meshes, c);
            RugWashToolModels.Finish(model);
            // Vaguelette poussée devant la lame, et giclée au caniveau : quelques gouttes, sans collision.
            var push = CreateParticles("SqueegeeWave", ctx.Effects, c["Droplet"], new ParticleConfig
            {
                Lifetime = new Vector2(0.2f, 0.4f), Speed = new Vector2(0.3f, 0.8f), Size = new Vector2(0.012f, 0.025f), Color = new Color(0.8f, 0.85f, 0.9f, 0.8f),
                Gravity = 1.4f, MaxParticles = 90, Shape = ParticleSystemShapeType.Cone, Angle = 35f, Radius = 0.2f, Rate = 0f, Loop = true,
                PlayOnAwake = true, Stretch = 0.02f,
            });
            var splash = CreateParticles("SqueegeeDrain", ctx.Effects, c["Droplet"], new ParticleConfig
            {
                Lifetime = new Vector2(0.25f, 0.5f), Speed = new Vector2(0.4f, 1f), Size = new Vector2(0.015f, 0.03f), Color = new Color(0.75f, 0.8f, 0.85f, 0.85f),
                Gravity = 1.6f, MaxParticles = 90, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.08f, Rate = 0f, Loop = true, PlayOnAwake = true,
            });
            var tool = CreateTool<SqueegeeTool>(ctx, "Squeegee", c.Squeegee, model, RugWashAudioGenerator.SqueegeeLoop, 0.3f, 0.15f, 0f,
                RugWashAudioGenerator.BroomContactLoop, 0.45f);
            Wire(tool, ("waterPush", push), ("drainSplash", splash), ("drain", ctx.Drain));
            return tool;
        }

        static RotaryScrubberTool BuildRotary(ToolBuildContext ctx)
        {
            var c = ctx.Content;
            var model = RugWashToolModels.Rotary(ctx.Camera, ctx.Meshes, c);
            RugWashToolModels.Finish(model);
            var spray = CreateParticles("RotarySpray", ctx.Effects, c["Debris"], new ParticleConfig
            {
                Lifetime = new Vector2(0.25f, 0.5f), Speed = new Vector2(0.6f, 1.4f), Size = new Vector2(0.008f, 0.02f),
                Color = new Color(0.9f, 0.92f, 0.95f, 0.9f), Gravity = 1.2f, MaxParticles = 120, Shape = ParticleSystemShapeType.Circle,
                Radius = 0.22f, Rate = 0f, Loop = true, PlayOnAwake = true,
            });
            var tool = CreateTool<RotaryScrubberTool>(ctx, "Rotary", c.Rotary, model, RugWashAudioGenerator.RotaryLoop, 0.4f, 0.15f, -0.05f,
                RugWashAudioGenerator.RotaryContactLoop, 0.5f);
            Wire(tool, ("spray", spray), ("spinner", model.Head.Find("Disc")));
            return tool;
        }

        static BrushTool BuildDetailBrush(ToolBuildContext ctx)
        {
            var c = ctx.Content;
            var model = RugWashToolModels.DetailBrush(ctx.Camera, ctx.Meshes, c);
            RugWashToolModels.Finish(model);
            var debris = CreateParticles("DetailBrushDebris", ctx.Effects, c["Debris"], new ParticleConfig
            {
                Lifetime = new Vector2(0.3f, 0.6f), Speed = new Vector2(0.3f, 0.9f), Size = new Vector2(0.006f, 0.015f),
                Color = new Color(0.36f, 0.27f, 0.18f, 1f), Gravity = 1f, MaxParticles = 100, Shape = ParticleSystemShapeType.Cone, Angle = 50f,
                Radius = 0.04f, Rate = 0f, Loop = true, PlayOnAwake = true,
            });
            var tool = CreateTool<BrushTool>(ctx, "DetailBrush", c.DetailBrush, model, RugWashAudioGenerator.BrushLoop, 0.2f, 0.15f, 0.12f,
                RugWashAudioGenerator.BrushContactLoop, 0.4f);
            Wire(tool, ("debrisParticles", debris));
            SetFloat(tool, "stubbornBonus", 2.2f);
            SetFloat(tool, "abrasiveness", 0.15f);
            SetFloat(tool, "scrubFrequency", 14f);
            SetFloat(tool, "scrubAmplitude", 0.02f);
            return tool;
        }

        /// <summary>Effets des farces (v0.1.2) : gouttes et éclaboussures émises à la demande, sons 3D réutilisés.</summary>
        static RugWash.Pranks.PrankEffects BuildPrankEffects(Transform parent, RugWashContentBuilder.Content c)
        {
            var root = new GameObject("PrankEffects").transform;
            root.SetParent(parent, false);
            ParticleSystem Drops(string name, Color color, Vector2 size, Vector2 lifetime, float gravity, int max, float stretch) =>
                CreateParticles(name, root, c["Debris"], new ParticleConfig
                {
                    Lifetime = lifetime, Speed = Vector2.zero, Size = size, Color = color, Gravity = gravity, MaxParticles = max,
                    Shape = ParticleSystemShapeType.Sphere, Radius = 0.01f, Loop = true, PlayOnAwake = true, Rate = 0f, Stretch = stretch,
                });
            var stream = Drops("PeeStream", new Color(1f, 0.86f, 0.22f, 0.85f), new Vector2(0.007f, 0.014f), new Vector2(0.55f, 0.7f), 1f, 160, 0.025f);
            var yellow = Drops("SplashYellow", new Color(1f, 0.84f, 0.2f, 0.85f), new Vector2(0.02f, 0.04f), new Vector2(0.3f, 0.5f), 1.2f, 60, 0f);
            // Faux sang de théâtre : rouge cerise de dessin animé, gouttes rondes (pas de réalisme).
            var red = Drops("SplashRed", new Color(0.86f, 0.06f, 0.14f, 0.95f), new Vector2(0.022f, 0.05f), new Vector2(0.3f, 0.55f), 1.2f, 120, 0f);
            var puff = CreateParticles("BlasterPuff", root, c["Foam"], new ParticleConfig
            {
                Lifetime = new Vector2(0.18f, 0.32f), Speed = Vector2.zero, Size = new Vector2(0.012f, 0.028f), Color = new Color(1f, 0.82f, 0.6f, 0.45f),
                Gravity = -0.1f, MaxParticles = 40, Shape = ParticleSystemShapeType.Sphere, Radius = 0.01f, Loop = true, PlayOnAwake = true, Rate = 0f,
                SizeGrowth = 1.5f,
            });
            foreach (var system in new[] { stream, yellow, red, puff }) DisableShadows(system.gameObject);
            var voices = new List<AudioSource>();
            for (int i = 0; i < 4; i++)
            {
                var voice = new GameObject($"Voice_{i}").AddComponent<AudioSource>();
                voice.transform.SetParent(root, false);
                voice.playOnAwake = false;
                voice.spatialBlend = 1f;
                voice.rolloffMode = AudioRolloffMode.Linear;
                voice.minDistance = 1f;
                voice.maxDistance = 16f;
                voices.Add(voice);
            }
            var prankEffects = root.gameObject.AddComponent<RugWash.Pranks.PrankEffects>();
            Wire(prankEffects, ("droplets", stream), ("splashYellow", yellow), ("splashRed", red), ("puff", puff),
                ("streamClip", Clip(RugWashAudioGenerator.PrankStream)), ("swingClip", Clip(RugWashAudioGenerator.BatSwing)),
                ("bonkClip", Clip(RugWashAudioGenerator.BatBonk)), ("shotClip", Clip(RugWashAudioGenerator.BlasterShot)),
                ("splatClip", Clip(RugWashAudioGenerator.PrankSplat)));
            WireList(prankEffects, "voices", voices);
            if (!prankEffects.IsConfigured(out string missing))
                throw new InvalidOperationException($"[RugWash] Effets des farces incomplets : {missing}");
            return prankEffects;
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
            /// <summary>V0.3.0 : panneau arrondi de la charte (rayon 14 px, anticrénelé).</summary>
            public readonly Sprite Rounded = LoadPanel();
            public readonly Sprite Pill = RugWashUIStyle.Sprite("UI_Pill");
            public readonly Sprite GradientH = RugWashUIStyle.Sprite("UI_GradientH");
            public readonly Sprite GradientV = RugWashUIStyle.Sprite("UI_GradientV");

            static Sprite LoadPanel()
            {
                RugWashUIStyle.ConfigureImports();
                return RugWashUIStyle.Sprite("UI_Panel") ?? AssetDatabase.LoadAssetAtPath<Sprite>(RugWashTextureGenerator.UIRounded);
            }
        }

        sealed class BuiltUI
        {
            public HUDController Hud;
            public ToolSelectionUI Toolbar;
            public PauseMenuUI Pause;
            public InspectionPanelUI Inspection;
            public OrderBoardUI Board;
            public ShopUI Shop;
            public ComputerUI Computer;
            public ResultsUI Results;
            public SettingsUI Settings;
            public DebugOverlay Debug;
            public RugDexUI RugDex;
            public InputDeviceMonitor Monitor;
            public UINavigator Navigator;
            /// <summary>Gouttes de farce sur l'écran (v0.1.2).</summary>
            public ScreenSplash Splash;
            /// <summary>Casier : inventaire porté et objets rangés (grande mise à jour).</summary>
            public LockerUI Locker;
            /// <summary>Machine à laver : chargement, temps restant, récupération (grande mise à jour).</summary>
            public WashingMachineUI Washer;
            /// <summary>En ligne : chat textuel et fenêtre des joueurs (voix, expulsion, partie ouverte).</summary>
            public ChatUI Chat;
            public PlayersUI Players;
        }

        /// <summary>Moniteur de périphérique de l'interface en cours de construction (libellés « Fermer (Échap / B) »).</summary>
        static InputDeviceMonitor uiMonitor;

        // V0.3.0 : couleurs de la charte (RugWash.UI.UITheme).
        static readonly Color PanelColor = UITheme.HudCard;
        static readonly Color Accent = UITheme.PetrolBright;
        static readonly Color Muted = UITheme.TextSecondary;

        static BuiltUI BuildUI(Transform parent, UISprites sprites, PerformanceMonitor performance)
        {
            var built = new BuiltUI();
            var canvas = CreateCanvas("HUD Canvas", parent);
            built.Navigator = CreateEventSystem(parent);
            built.Monitor = built.Navigator.Monitor;

            // ---- Gouttes de farce (v0.1.2) : au-dessus de la vue, sous le HUD (qui reste lisible).
            var splashRoot = Stretch("ScreenSplash", canvas.transform);
            var splashGroup = splashRoot.gameObject.AddComponent<CanvasGroup>();
            splashGroup.blocksRaycasts = false;
            splashGroup.interactable = false;
            var haze = AddImage(Stretch("Haze", splashRoot), sprites.White, new Color(0.95f, 0.85f, 0.35f, 0f), sliced: false);
            var drops = Stretch("Droplets", splashRoot).gameObject.AddComponent<RawImage>();
            drops.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(RugWashTextureGenerator.ScreenDroplets);
            drops.raycastTarget = false;
            drops.color = new Color(1f, 1f, 1f, 0f);
            built.Splash = splashRoot.gameObject.AddComponent<ScreenSplash>();
            Wire(built.Splash, ("group", splashGroup), ("droplets", drops), ("haze", haze));

            // ---- HUD
            var hudRoot = Stretch("HUD", canvas.transform);
            var hudGroup = hudRoot.gameObject.AddComponent<CanvasGroup>();
            hudGroup.blocksRaycasts = false;

            // V0.3.0 : point de visée rond cerclé de sombre (lisible sur un tapis clair comme sur le béton).
            var crosshairRing = CreateRect("CrosshairRing", hudRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 10f));
            AddImage(crosshairRing, sprites.Pill, new Color(0f, 0f, 0f, 0.4f));
            var crosshair = CreateRect("Crosshair", hudRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6f, 6f));
            AddImage(crosshair, sprites.Pill, new Color(1f, 1f, 1f, 0.92f));

            var cleanPanel = CreateRect("CleanlinessPanel", hudRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(560f, 78f));
            Card(cleanPanel, PanelColor);
            Icon(cleanPanel, "Sparkle", new Vector2(0f, 1f), new Vector2(34f, -27f), 26f, UITheme.Saffron);
            var cleanLabel = AddText(CreateRect("Label", cleanPanel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(14f, -9f), new Vector2(480f, 36f)),
                "Propreté du tapis : 0 %", 24, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            var cleanFill = CreateFillBar("Bar", cleanPanel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(516f, 14f),
                sprites.Rounded, sprites.White, UITheme.Track, Accent, out _);

            var scanPanel = CreateRect("ScanPanel", hudRoot, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 24f), new Vector2(300f, 64f));
            Card(scanPanel, PanelColor);
            var scanLabel = AddText(CreateRect("Label", scanPanel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(276f, 30f)),
                "Scanner la saleté", 21, TextAnchor.MiddleLeft, Color.white);
            scanLabel.resizeTextForBestFit = true;
            scanLabel.resizeTextMinSize = 14;
            scanLabel.resizeTextMaxSize = 21;
            var scanFill = CreateFillBar("Bar", scanPanel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(276f, 10f),
                sprites.Rounded, sprites.White, UITheme.Track, UITheme.Saffron, out _);

            var toast = AddText(CreateRect("Toast", hudRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(900f, 36f)),
                string.Empty, 24, TextAnchor.MiddleCenter, UITheme.Success, FontStyle.Bold);

            // V0.3.0 : solde (pièce) et réputation (étoile) sur deux lignes, icônes à gauche ; texte enrichi par le HUD.
            var economyPanel = CreateRect("EconomyPanel", hudRoot, Vector2.one, Vector2.one, Vector2.one, new Vector2(-24f, -22f), new Vector2(330f, 86f));
            Card(economyPanel, PanelColor);
            Icon(economyPanel, "Coin", new Vector2(0f, 1f), new Vector2(32f, -28f), 28f, UITheme.Money);
            Icon(economyPanel, "Star", new Vector2(0f, 1f), new Vector2(32f, -62f), 22f, UITheme.Saffron);
            var economy = AddText(CreateRect("Label", economyPanel, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(22f, 0f), new Vector2(-64f, -14f)),
                "0 €", 24, TextAnchor.MiddleLeft, UITheme.Money, FontStyle.Bold);
            economy.supportRichText = true;
            var players = AddText(CreateRect("Players", hudRoot, Vector2.one, Vector2.one, Vector2.one, new Vector2(-24f, -106f), new Vector2(720f, 30f)),
                string.Empty, 19, TextAnchor.UpperRight, UITheme.TextSecondary);
            var orderInfo = AddText(CreateRect("OrderInfo", hudRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -106f), new Vector2(1000f, 90f)),
                string.Empty, 21, TextAnchor.UpperCenter, Color.white);

            var prompt = AddText(CreateRect("Prompt", hudRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(1000f, 40f)),
                string.Empty, 26, TextAnchor.UpperCenter, Color.white);

            var hint = AddText(CreateRect("Hint", hudRoot, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -20f), new Vector2(640f, 52f)),
                string.Empty, 18, TextAnchor.UpperLeft, Muted);
            hint.resizeTextForBestFit = true;
            hint.resizeTextMinSize = 12;
            hint.resizeTextMaxSize = 18;
            var workSurfaceLabel = AddText(CreateRect("WorkSurfaceAlert", hudRoot, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(24f, -74f), new Vector2(760f, 30f)), string.Empty, 20, TextAnchor.UpperLeft, UITheme.Warning, FontStyle.Bold);

            var toolPanel = CreateRect("ToolPanel", hudRoot, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(24f, 24f), new Vector2(380f, 116f));
            Card(toolPanel, PanelColor);
            AccentBar(toolPanel, new Vector2(0f, 1f), new Vector2(0f, -30f), new Vector2(5f, 30f), UITheme.Saffron);
            var toolName = AddText(CreateRect("ToolName", toolPanel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -10f), new Vector2(350f, 40f)),
                "Outil", 30, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            toolName.resizeTextForBestFit = true;
            toolName.resizeTextMinSize = 14;
            toolName.resizeTextMaxSize = 30;
            // Une seule ligne : un nom long (niveau, outil doré) rétrécit au lieu de passer sur la réserve en dessous.
            toolName.horizontalOverflow = HorizontalWrapMode.Wrap;
            toolName.verticalOverflow = VerticalWrapMode.Truncate;
            var toolStatus = AddText(CreateRect("ToolStatus", toolPanel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -50f), new Vector2(350f, 30f)),
                string.Empty, 22, TextAnchor.MiddleLeft, Muted);
            var resourceFill = CreateFillBar("ResourceBar", toolPanel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(344f, 14f),
                sprites.Rounded, sprites.White, UITheme.Track, new Color(0.35f, 0.68f, 1f), out var resourceBar);

            built.Hud = hudRoot.gameObject.AddComponent<HUDController>();
            Wire(built.Hud, ("root", hudGroup), ("toolNameLabel", toolName), ("toolStatusLabel", toolStatus), ("resourceFill", resourceFill),
                ("resourceBar", resourceBar.gameObject), ("cleanlinessLabel", cleanLabel), ("cleanlinessFill", cleanFill),
                ("promptLabel", prompt), ("hintLabel", hint), ("toastLabel", toast), ("economyLabel", economy), ("orderLabel", orderInfo),
                ("cleanlinessPanel", cleanPanel), ("scanLabel", scanLabel), ("scanFill", scanFill), ("workSurfaceLabel", workSurfaceLabel),
                ("playersLabel", players));

            // ---- Barre d'outils
            // Quatre emplacements d'inventaire (grande mise à jour) entre le panneau d'outil (gauche) et le panneau de scan (droite).
            var bar = CreateRect("ToolBar", hudRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(40f, 24f), new Vector2(1000f, 60f));
            var barLayout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            barLayout.spacing = 6;
            barLayout.childAlignment = TextAnchor.MiddleCenter;
            barLayout.childControlWidth = true;
            barLayout.childControlHeight = true;
            barLayout.childForceExpandWidth = false;
            var slotImages = new List<Image>();
            var slotLabels = new List<Text>();
            var slotIcons = new List<RawImage>();
            for (int i = 0; i < PlayerInventory.Capacity; i++)
            {
                var slot = CreateRect($"Slot_{i}", bar, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240f, 60f));
                var element = slot.gameObject.AddComponent<LayoutElement>();
                element.preferredWidth = 240f;
                element.preferredHeight = 60f;
                slotImages.Add(Card(slot, UITheme.SlotNormal));
                // Icône 3D de l'objet porté (grande mise à jour), à gauche du nom.
                var slotIcon = CreateRect("Icon", slot, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(54f, 54f))
                    .gameObject.AddComponent<RawImage>();
                slotIcon.raycastTarget = false;
                slotIcon.gameObject.SetActive(false);
                slotIcons.Add(slotIcon);
                var labelRect = CreateRect("Label", slot, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(29f, 0f), new Vector2(-66f, -8f));
                var slotLabel = AddText(labelRect, "-", 19, TextAnchor.MiddleCenter, Color.white);
                slotLabel.resizeTextForBestFit = true;
                slotLabel.resizeTextMinSize = 10;
                slotLabel.resizeTextMaxSize = 19;
                slotLabels.Add(slotLabel);
            }
            built.Toolbar = bar.gameObject.AddComponent<ToolSelectionUI>();
            WireList(built.Toolbar, "slotBackgrounds", slotImages);
            WireList(built.Toolbar, "slotLabels", slotLabels);
            WireList(built.Toolbar, "slotIcons", slotIcons);

            // ---- Inspection (F)
            var inspectionRoot = Stretch("Inspection", canvas.transform);
            var inspectionPanel = Stretch("Panel", inspectionRoot);
            AddImage(inspectionPanel, sprites.White, UITheme.Backdrop, sliced: false, raycast: true);
            var window = CreateRect("Window", inspectionPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500f, 820f));
            Card(window, UITheme.Window);
            DropShadow(window);
            var title = AddText(CreateRect("Title", window, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(1400f, 50f)),
                "Inspection", 38, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            var beforeImage = BuildSnapshotColumn(window, "Before", "Avant", new Vector2(-370f, 40f), sprites, out var beforeAspect);
            var afterImage = BuildSnapshotColumn(window, "After", "Maintenant", new Vector2(370f, 40f), sprites, out var afterAspect);
            var stats = AddText(CreateRect("Stats", window, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-150f, 26f), new Vector2(900f, 175f)),
                string.Empty, 24, TextAnchor.MiddleLeft, Color.white);
            var close = CreateButton("CloseButton", window, "Fermer (Échap)", sprites.Rounded, new Vector2(300f, 64f));
            AddPrompt(close, "Fermer ({back})");
            inspectionPanel.gameObject.AddComponent<NavigationScope>().FirstSelected = close;
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
            AddImage(pausePanel, sprites.White, UITheme.Backdrop, sliced: false, raycast: true);
            var box = CreateRect("Box", pausePanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 560f));
            Card(box, UITheme.Window);
            AddVerticalLayout(box.gameObject, 16, new RectOffset(48, 48, 40, 40), TextAnchor.UpperCenter);
            var fitter = box.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var pauseTitle = AddText(CreateRect("Title", box, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(460f, 60f)), "Pause", 46, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            pauseTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 60f;
            var subtitle = AddText(CreateRect("Subtitle", box, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(460f, 40f)), string.Empty, 22, TextAnchor.MiddleCenter, Muted);
            subtitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
            var resume = CreateButton("Resume", box, "Reprendre", sprites.Rounded, new Vector2(460f, 64f));
            var reset = CreateButton("ResetRug", box, "Recommencer la commande", sprites.Rounded, new Vector2(460f, 64f));
            var abandon = CreateButton("AbandonOrder", box, "Abandonner la commande", sprites.Rounded, new Vector2(460f, 64f));
            var rugDexButton = CreateButton("RugDex", box, "RugDex (collection)", sprites.Rounded, new Vector2(460f, 64f));
            var settingsButton = CreateButton("Settings", box, "Paramètres", sprites.Rounded, new Vector2(460f, 64f));
            var playersButton = CreateButton("Players", box, "Joueurs (voix, expulsion)", sprites.Rounded, new Vector2(460f, 64f));
            var mainMenu = CreateButton("MainMenu", box, "Menu principal", sprites.Rounded, new Vector2(460f, 64f));
            var quit = CreateButton("Quit", box, "Quitter le jeu", sprites.Rounded, new Vector2(460f, 64f));
            pausePanel.gameObject.AddComponent<NavigationScope>().FirstSelected = resume;
            built.Pause = pauseRoot.gameObject.AddComponent<PauseMenuUI>();
            Wire(built.Pause, ("panel", pausePanel.gameObject), ("resumeButton", resume), ("resetRugButton", reset),
                ("quitButton", quit), ("subtitleLabel", subtitle), ("abandonOrderButton", abandon),
                ("resetRugLabel", reset.GetComponentInChildren<Text>()), ("settingsButton", settingsButton), ("mainMenuButton", mainMenu),
                ("rugDexButton", rugDexButton), ("mainMenuLabel", mainMenu.GetComponentInChildren<Text>()), ("playersButton", playersButton));

            BuildGameLoopUI(canvas, sprites, built);
            built.RugDex = BuildRugDexUI(canvas, sprites);
            built.Settings = BuildSettingsUI(canvas, sprites);

            // ---- Diagnostic (F3, développement uniquement)
            var debugRoot = CreateRect("DebugOverlay", canvas.transform, Vector2.one, Vector2.one, Vector2.one, new Vector2(-24f, -110f), new Vector2(560f, 250f));
            var debugPanel = Stretch("Panel", debugRoot);
            AddImage(debugPanel, sprites.Rounded, new Color(0f, 0f, 0f, 0.72f));
            var debugLabel = AddText(Stretch("Text", debugPanel, 14f), string.Empty, 18, TextAnchor.UpperLeft, new Color(0.7f, 1f, 0.7f), shadow: false);
            built.Debug = debugRoot.gameObject.AddComponent<DebugOverlay>();
            Wire(built.Debug, ("panel", debugPanel.gameObject), ("label", debugLabel), ("performance", performance));

            built.Locker = BuildLockerUI(canvas, sprites);
            built.Washer = BuildWasherUI(canvas, sprites);
            built.Chat = BuildChatUI(canvas, sprites);
            built.Players = BuildPlayersUI(canvas, sprites);

            inspectionPanel.gameObject.SetActive(false);
            pausePanel.gameObject.SetActive(false);
            debugPanel.gameObject.SetActive(false);
            return built;
        }

        /// <summary>Casier : à gauche les quatre emplacements portés, à droite le contenu du casier (liste défilante).</summary>
        /// <summary>
        /// Studio des icônes 3D (grande mise à jour) : sous l'atelier, hors de vue, une caméra désactivée (rendue à la demande,
        /// sans post-traitement ni ombres) et une petite lumière allumée seulement pendant les prises de vue.
        /// </summary>
        static RugWash.Effects.ToolIconRenderer BuildToolIconStudio(Transform parent, RugWashContentBuilder.Content c)
        {
            var studio = new GameObject("ToolIconStudio");
            studio.transform.SetParent(parent, false);
            studio.transform.localPosition = new Vector3(0f, -60f, 0f);
            var cameraGo = new GameObject("IconCamera");
            cameraGo.transform.SetParent(studio.transform, false);
            var camera = cameraGo.AddComponent<Camera>();
            camera.enabled = false;
            camera.cullingMask = 1 << RugWashPaths.ToolIconLayer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            var lightGo = new GameObject("IconLight");
            lightGo.transform.SetParent(studio.transform, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 4f;
            light.intensity = 3f;
            light.color = new Color(1f, 0.97f, 0.92f);
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << RugWashPaths.ToolIconLayer;
            lightGo.AddComponent<UniversalAdditionalLightData>();
            light.enabled = false;
            var icons = studio.AddComponent<RugWash.Effects.ToolIconRenderer>();
            Wire(icons, ("iconCamera", camera), ("iconLight", light));
            SetInt(icons, "iconLayer", RugWashPaths.ToolIconLayer);
            WireList(icons, "hiddenMaterials", new[] { c["Glove"], c["GloveCuff"] });
            WireList(icons, "ignoredForFraming", new[] { c["Rubber"], c["GreyHose"] });
            return icons;
        }

        /// <summary>Chat textuel : journal en bas à gauche (texte enrichi : noms en couleur) et ligne de saisie (Entrée).</summary>
        static ChatUI BuildChatUI(Canvas canvas, UISprites sprites)
        {
            var root = CreateRect("Chat", canvas.transform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(24f, 200f), new Vector2(680f, 300f));
            var logRect = CreateRect("Log", root, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 56f), new Vector2(680f, 230f));
            var group = logRect.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            var log = AddText(logRect, string.Empty, 19, TextAnchor.LowerLeft, Color.white);
            log.supportRichText = true;
            var inputRow = CreateRect("InputRow", root, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(680f, 48f));
            AddImage(inputRow, sprites.Rounded, new Color(0.04f, 0.05f, 0.06f, 0.85f));
            var field = CreateInputField("Input", inputRow, new Vector2(0f, 0f), new Vector2(520f, 48f), "Message…", sprites);
            var hint = AddText(CreateRect("Hint", inputRow, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(150f, 44f)),
                string.Empty, 14, TextAnchor.MiddleRight, Muted);
            var ui = root.gameObject.AddComponent<ChatUI>();
            Wire(ui, ("root", root.gameObject), ("logLabel", log), ("logGroup", group), ("inputRow", inputRow.gameObject), ("input", field), ("hintLabel", hint));
            if (!ui.IsConfigured(out string missing)) throw new InvalidOperationException($"[RugWash] Chat incomplet : {missing}");
            return ui;
        }

        /// <summary>Joueurs en ligne : une ligne par joueur (voix, expulsion par l'hôte), partie ouverte ou fermée, confirmation.</summary>
        static PlayersUI BuildPlayersUI(Canvas canvas, UISprites sprites)
        {
            var window = BuildWindow(canvas.transform, "Players", new Vector2(1180f, 760f), sprites, "Joueurs", out var panel, out var close);
            var status = AddText(CreateRect("Status", window, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(1100f, 60f)),
                string.Empty, 21, TextAnchor.MiddleLeft, Muted);
            var listRoot = CreateRect("List", window, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -160f), new Vector2(1100f, 400f));
            AddVerticalLayout(listRoot.gameObject, 10, new RectOffset(0, 0, 0, 0), TextAnchor.UpperCenter);
            var rows = new List<PlayerRowView>();
            for (int i = 0; i < 4; i++)
            {
                var row = CreateRect($"Player{i + 1}", listRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100f, 88f));
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 88f;
                var background = AddImage(row, sprites.Rounded, UITheme.Row);
                var name = AddText(CreateRect("Name", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(20f, 12f), new Vector2(560f, 40f)),
                    string.Empty, 24, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
                name.resizeTextForBestFit = true;
                name.resizeTextMinSize = 14;
                name.resizeTextMaxSize = 24;
                var state = AddText(CreateRect("State", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(20f, -22f), new Vector2(560f, 30f)),
                    string.Empty, 19, TextAnchor.MiddleLeft, Muted);
                var mute = CreateButton("Mute", row, "Couper sa voix", sprites.Rounded, new Vector2(240f, 56f));
                PlaceRight((RectTransform)mute.transform, new Vector2(-230f, 0f));
                var kick = CreateButton("Kick", row, "Expulser", sprites.Rounded, new Vector2(190f, 56f));
                PlaceRight((RectTransform)kick.transform, new Vector2(-24f, 0f));
                var view = row.gameObject.AddComponent<PlayerRowView>();
                Wire(view, ("background", background), ("nameLabel", name), ("statusLabel", state), ("muteButton", mute),
                    ("muteLabel", mute.GetComponentInChildren<Text>()), ("kickButton", kick));
                rows.Add(view);
            }
            var open = CreateButton("OpenClose", window, "Fermer la partie", sprites.Rounded, new Vector2(320f, 60f));
            PlaceBottomLeft((RectTransform)open.transform, new Vector2(40f, 90f));
            var message = AddText(CreateRect("Message", window, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(160f, 100f), new Vector2(720f, 50f)),
                string.Empty, 20, TextAnchor.MiddleLeft, UITheme.Success);
            message.horizontalOverflow = HorizontalWrapMode.Wrap;
            AddText(CreateRect("Note", window, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(1100f, 44f)),
                "Chat vocal : touche V pour parler (Paramètres → Chat vocal). Couper une voix n'agit que chez vous. Entrée : chat écrit.",
                17, TextAnchor.MiddleCenter, Muted);

            // Confirmation de l'expulsion, par-dessus la liste.
            var confirm = Stretch("Confirm", window);
            AddImage(confirm, sprites.White, new Color(0f, 0f, 0f, 0.75f), sliced: false, raycast: true);
            var box = CreateRect("Box", confirm, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 260f));
            AddImage(box, sprites.Rounded, new Color(0.1f, 0.12f, 0.15f, 1f));
            var confirmLabel = AddText(CreateRect("Label", box, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(680f, 110f)),
                string.Empty, 22, TextAnchor.MiddleCenter, Color.white);
            var yes = CreateButton("Yes", box, "Expulser", sprites.Rounded, new Vector2(240f, 60f));
            PlaceBottomLeft((RectTransform)yes.transform, new Vector2(120f, 30f));
            var no = CreateButton("No", box, "Annuler", sprites.Rounded, new Vector2(240f, 60f));
            PlaceBottomLeft((RectTransform)no.transform, new Vector2(400f, 30f));

            var ui = window.parent.parent.gameObject.AddComponent<PlayersUI>();
            Wire(ui, ("panel", panel), ("statusLabel", status), ("openButton", open), ("openLabel", open.GetComponentInChildren<Text>()),
                ("confirmPanel", confirm.gameObject), ("confirmLabel", confirmLabel), ("confirmYes", yes), ("confirmNo", no),
                ("messageLabel", message), ("closeButton", close));
            WireList(ui, "rows", rows);
            panel.GetComponent<NavigationScope>().FirstSelected = close;
            if (!ui.IsConfigured(out string missing)) throw new InvalidOperationException($"[RugWash] Fenêtre des joueurs incomplète : {missing}");
            confirm.gameObject.SetActive(false);
            panel.SetActive(false);
            return ui;
        }

        static WashingMachineUI BuildWasherUI(Canvas canvas, UISprites sprites)
        {
            var window = BuildWindow(canvas.transform, "Washer", new Vector2(1300f, 820f), sprites, "Machine à laver", out var panel, out var close);
            var status = AddText(CreateRect("Status", window, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -92f), new Vector2(1220f, 40f)),
                string.Empty, 26, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            var track = CreateRect("Progress", window, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -146f), new Vector2(1220f, 22f));
            AddImage(track, sprites.Rounded, new Color(1f, 1f, 1f, 0.1f));
            var fill = AddImage(Stretch("Fill", track, 3f), sprites.White, UITheme.PetrolBright, sliced: false);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0f;
            AddText(CreateRect("BaysHeader", window, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -190f), new Vector2(1220f, 34f)),
                "Charger le tapis d'une zone de lavage", 22, TextAnchor.MiddleLeft, Accent, FontStyle.Bold);
            var baysRoot = CreateRect("Bays", window, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -232f), new Vector2(1220f, 230f));
            AddVerticalLayout(baysRoot.gameObject, 10, new RectOffset(0, 0, 0, 0), TextAnchor.UpperCenter);
            var rows = new List<ListRowView>();
            for (int i = 0; i < 2; i++)
            {
                var row = BuildRowTemplate(baysRoot, sprites, 1220f, 104f, 220f);
                row.name = $"Bay{i + 1}";
                rows.Add(row);
            }
            var collect = CreateButton("Collect", window, "Récupérer le tapis", sprites.Rounded, new Vector2(340f, 64f));
            PlaceBottomLeft((RectTransform)collect.transform, new Vector2(40f, 160f));
            var abandon = CreateButton("Abandon", window, "Abandonner la commande", sprites.Rounded, new Vector2(360f, 64f));
            PlaceBottomLeft((RectTransform)abandon.transform, new Vector2(400f, 160f));
            var note = AddText(CreateRect("Note", window, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 84f), new Vector2(1220f, 66f)),
                string.Empty, 19, TextAnchor.MiddleLeft, Muted);
            var message = AddText(CreateRect("Message", window, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(1220f, 44f)),
                string.Empty, 22, TextAnchor.MiddleCenter, UITheme.Success);
            var ui = window.parent.parent.gameObject.AddComponent<WashingMachineUI>();
            Wire(ui, ("panel", panel), ("statusLabel", status), ("progressFill", fill), ("collectButton", collect),
                ("collectLabel", collect.GetComponentInChildren<Text>()), ("abandonButton", abandon), ("noteLabel", note), ("messageLabel", message),
                ("closeButton", close));
            WireList(ui, "bayRows", rows);
            panel.GetComponent<NavigationScope>().FirstSelected = rows[0].ActionButton;
            if (!ui.IsConfigured(out string missing)) throw new InvalidOperationException($"[RugWash] Fenêtre de la machine à laver incomplète : {missing}");
            panel.SetActive(false);
            return ui;
        }

        static LockerUI BuildLockerUI(Canvas canvas, UISprites sprites)
        {
            var window = BuildWindow(canvas.transform, "Locker", new Vector2(1440f, 860f), sprites, "Casier", out var panel, out var close);
            AddText(CreateRect("CarriedHeader", window, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -92f), new Vector2(660f, 36f)),
                "Sur vous (touches 1 à 4)", 24, TextAnchor.MiddleLeft, Accent, FontStyle.Bold);
            var carriedRoot = CreateRect("Carried", window, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -136f), new Vector2(660f, 520f));
            AddVerticalLayout(carriedRoot.gameObject, 10, new RectOffset(0, 0, 0, 0), TextAnchor.UpperCenter);
            var carried = new List<ListRowView>();
            for (int i = 0; i < PlayerInventory.Capacity; i++)
            {
                var row = BuildRowTemplate(carriedRoot, sprites, 660f, 112f, 150f, withIcon: true);
                row.name = $"Carried{i + 1}";
                carried.Add(row);
            }
            var storedHeader = AddText(CreateRect("StoredHeader", window, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -92f), new Vector2(660f, 36f)),
                "Dans le casier", 24, TextAnchor.MiddleLeft, Accent, FontStyle.Bold);
            var list = CreateScrollList(window, "Stored", new Vector2(330f, -136f), new Vector2(660f, 520f), sprites);
            AddVerticalLayout(list.gameObject, 10, new RectOffset(0, 0, 0, 0), TextAnchor.UpperCenter);
            var template = BuildRowTemplate(list, sprites, 660f, 100f, 190f, withIcon: true);
            var note = AddText(CreateRect("Note", window, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 96f), new Vector2(1360f, 60f)),
                string.Empty, 20, TextAnchor.MiddleCenter, Muted);
            note.horizontalOverflow = HorizontalWrapMode.Wrap;
            var message = AddText(CreateRect("Message", window, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(1360f, 50f)),
                string.Empty, 22, TextAnchor.MiddleCenter, UITheme.Success);
            message.horizontalOverflow = HorizontalWrapMode.Wrap;
            var ui = window.parent.parent.gameObject.AddComponent<LockerUI>();
            Wire(ui, ("panel", panel), ("storedRoot", list), ("storedTemplate", template), ("storedHeader", storedHeader), ("noteLabel", note),
                ("messageLabel", message), ("closeButton", close));
            WireList(ui, "carriedRows", carried);
            panel.GetComponent<NavigationScope>().FirstSelected = carried[0].ActionButton;
            if (!ui.IsConfigured(out string missing)) throw new InvalidOperationException($"[RugWash] Casier incomplet : {missing}");
            panel.SetActive(false);
            return ui;
        }

        /// <summary>
        /// Zone défilante verticale (molette, glisser) : retourne le contenu, à qui l'appelant ajoute sa mise en page.
        /// Le contenu s'étire en largeur et grandit en hauteur avec ses éléments.
        /// </summary>
        static RectTransform CreateScrollList(Transform parent, string name, Vector2 position, Vector2 size, UISprites sprites)
        {
            var root = CreateRect(name, parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), position, size);
            var scroll = root.gameObject.AddComponent<ScrollRect>();
            var viewport = Stretch("Viewport", root);
            viewport.gameObject.AddComponent<RectMask2D>();
            AddImage(viewport, sprites.White, new Color(0f, 0f, 0f, 0.001f), sliced: false, raycast: true);
            var content = CreateRect("Content", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            // Barre verticale juste à droite de la liste : signale qu'il reste des entrées et se masque sinon.
            var track = CreateRect("Scrollbar", root, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(12f, 0f));
            AddImage(track, sprites.Rounded, new Color(1f, 1f, 1f, 0.08f), raycast: true);
            var slidingArea = Stretch("Sliding Area", track);
            var handle = Stretch("Handle", slidingArea);
            var handleImage = AddImage(handle, sprites.Rounded, new Color(0.95f, 0.78f, 0.35f, 0.85f), raycast: true);
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return content;
        }

        /// <summary>RugDex : cartes construites à l'exécution depuis le catalogue (aucune entrée codée en dur).</summary>
        static RugDexUI BuildRugDexUI(Canvas canvas, UISprites sprites)
        {
            var window = BuildWindow(canvas.transform, "RugDex", new Vector2(1500f, 900f), sprites, "RugDex", out var panel, out var close);
            // V0.3.0 : titre à gauche, informations de l'en-tête à droite (avant le bouton Fermer).
            var completion = AddText(CreateRect("Completion", window, Vector2.one, Vector2.one, Vector2.one, new Vector2(-290f, -29f), new Vector2(560f, 40f)),
                string.Empty, 22, TextAnchor.MiddleRight, UITheme.Money, FontStyle.Bold);

            var cards = CreateScrollList(window, "Cards", new Vector2(-370f, -100f), new Vector2(680f, 770f), sprites);
            var gridLayout = cards.gameObject.AddComponent<GridLayoutGroup>();
            gridLayout.cellSize = new Vector2(326f, 236f);
            gridLayout.spacing = new Vector2(16f, 16f);
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = 2;
            gridLayout.childAlignment = TextAnchor.UpperCenter;

            var card = CreateRect("CardTemplate", cards, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(326f, 236f));
            // Liseré de focus derrière la carte : invisible au repos, cyan au survol, ambre quand la carte est sélectionnée.
            var focus = CreateRect("Focus", card, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12f, 12f));
            var focusImage = AddImage(focus, sprites.Rounded, Color.white);
            // Le fond de la carte est un enfant (et non la carte elle-même) pour être dessiné par-dessus le liseré.
            var frame = AddImage(Stretch("Frame", card), sprites.Rounded, UITheme.Row, raycast: true);
            var button = card.gameObject.AddComponent<Button>();
            button.targetGraphic = focusImage;
            var cardColors = button.colors;
            cardColors.normalColor = new Color(1f, 1f, 1f, 0f);
            cardColors.highlightedColor = new Color(0.33f, 0.78f, 0.92f, 0.7f);
            cardColors.pressedColor = UITheme.PetrolBright;
            cardColors.selectedColor = FocusColor;
            cardColors.disabledColor = new Color(1f, 1f, 1f, 0f);
            cardColors.fadeDuration = 0.05f;
            button.colors = cardColors;
            var thumbArea = CreateRect("Thumbnail", card, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(300f, 180f));
            var thumbnail = thumbArea.gameObject.AddComponent<RawImage>();
            thumbnail.raycastTarget = false;
            var label = AddText(CreateRect("Label", card, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(306f, 34f)),
                "N° 00", 20, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 13;
            label.resizeTextMaxSize = 20;
            var cardView = card.gameObject.AddComponent<RugDexCardView>();
            Wire(cardView, ("frame", frame), ("thumbnail", thumbnail), ("label", label), ("button", button));

            var detail = CreateRect("Detail", window, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-30f, -100f), new Vector2(740f, 770f));
            AddImage(detail, sprites.Rounded, new Color(0.06f, 0.08f, 0.1f, 0.9f));
            var imageBox = CreateRect("ImageBox", detail, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(700f, 360f));
            var imageRect = CreateRect("Image", imageBox, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700f, 360f));
            var detailImage = imageRect.gameObject.AddComponent<RawImage>();
            var aspect = imageRect.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = 1.5f;
            var detailTitle = AddText(CreateRect("Title", detail, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -388f), new Vector2(700f, 44f)),
                string.Empty, 28, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            detailTitle.resizeTextForBestFit = true;
            detailTitle.resizeTextMinSize = 18;
            detailTitle.resizeTextMaxSize = 28;
            var detailBody = AddText(CreateRect("Body", detail, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -440f), new Vector2(700f, 318f)),
                string.Empty, 20, TextAnchor.UpperLeft, new Color(0.85f, 0.87f, 0.9f));
            detailBody.resizeTextForBestFit = true;
            detailBody.resizeTextMinSize = 14;
            detailBody.resizeTextMaxSize = 20;

            var ui = window.parent.parent.gameObject.AddComponent<RugDexUI>();
            Wire(ui, ("panel", panel), ("completionLabel", completion), ("cardRoot", cards), ("cardTemplate", cardView), ("detailImage", detailImage),
                ("detailAspect", aspect), ("detailTitle", detailTitle), ("detailBody", detailBody), ("closeButton", close));
            panel.SetActive(false);
            return ui;
        }

        static RectTransform BuildWindow(Transform canvas, string name, Vector2 size, UISprites sprites, string title,
            out GameObject panel, out Button closeButton)
        {
            var root = Stretch(name, canvas);
            var panelRect = Stretch("Panel", root);
            AddImage(panelRect, sprites.White, UITheme.Backdrop, sliced: false, raycast: true);
            var window = CreateRect("Window", panelRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            // V0.3.0 : fenêtre de la charte — ombre portée, contour fin, titre à gauche souligné d'une barre safran, filet sous l'en-tête.
            Card(window, UITheme.Window);
            DropShadow(window);
            AccentBar(window, new Vector2(0f, 1f), new Vector2(28f, -49f), new Vector2(6f, 34f), UITheme.Saffron);
            AddText(CreateRect("Title", window, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(48f, -22f), new Vector2(size.x - 340f, 54f)),
                title, 36, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            var separator = CreateRect("HeaderLine", window, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(-48f, 2f));
            AddImage(separator, sprites.White, UITheme.Outline, sliced: false);
            closeButton = CreateButton("Close", window, "Fermer (Échap)", sprites.Rounded, new Vector2(240f, 54f));
            var closeRect = (RectTransform)closeButton.transform;
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-24f, -22f);
            AddPrompt(closeButton, "Fermer ({back})");
            panelRect.gameObject.AddComponent<NavigationScope>().LastResort = closeButton;
            panel = panelRect.gameObject;
            return window;
        }

        static ListRowView BuildRowTemplate(Transform parent, UISprites sprites, float width, float height, float statusWidth,
            bool withIcon = false, bool fitHeight = false)
        {
            var row = CreateRect("RowTemplate", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, height));
            var background = Card(row, UITheme.Row);
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
            // Hauteur bornée : un titre long rétrécit au lieu de passer à la ligne par-dessus les détails.
            title.verticalOverflow = VerticalWrapMode.Truncate;
            var details = AddText(CreateRect("Details", row, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -40f), new Vector2(width - 260f, height - 44f)),
                "Détails", 18, TextAnchor.UpperLeft, UITheme.TextSecondary);
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
                ("actionButton", button), ("actionLabel", label), ("layout", element));
            if (withIcon)
            {
                // Icône 3D de l'outil (atlas des icônes), à gauche ; le texte se décale seulement quand elle est affichée.
                var icon = CreateRect("Icon", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(78f, 78f))
                    .gameObject.AddComponent<RawImage>();
                icon.raycastTarget = false;
                icon.gameObject.SetActive(false);
                Wire(view, ("icon", icon));
            }
            if (fitHeight) SetBool(view, "fitHeight", true);
            return view;
        }

        static void BuildGameLoopUI(Canvas canvas, UISprites sprites, BuiltUI built)
        {
            // ---- Tableau des commandes
            var boardWindow = BuildWindow(canvas.transform, "OrderBoard", new Vector2(1320f, 960f), sprites, "Commandes", out var boardPanel, out var boardClose);
            var header = AddText(CreateRect("Header", boardWindow, Vector2.one, Vector2.one, Vector2.one, new Vector2(-290f, -14f), new Vector2(560f, 66f)),
                string.Empty, 20, TextAnchor.MiddleRight, UITheme.Money, FontStyle.Bold);
            header.resizeTextForBestFit = true;
            header.resizeTextMinSize = 12;
            header.resizeTextMaxSize = 20;
            // Liste défilante (molette) : le nombre de commandes peut grandir sans changer l'interface.
            var list = CreateScrollList(boardWindow, "List", new Vector2(0f, -96f), new Vector2(1260f, 640f), sprites);
            AddVerticalLayout(list.gameObject, 8, new RectOffset(0, 0, 0, 0), TextAnchor.UpperCenter);
            var boardRow = BuildRowTemplate(list, sprites, 1260f, 98f, 380f);
            var active = CreateRect("Active", boardWindow, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 64f), new Vector2(1260f, 150f));
            AddImage(active, sprites.Rounded, UITheme.RowActive);
            var activeLabel = AddText(CreateRect("Label", active, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(520f, 140f)),
                string.Empty, 21, TextAnchor.MiddleLeft, Color.white);
            // Zones de lavage (grande mise à jour) : visible avec deux commandes en cours.
            var bayButton = CreateButton("NextBay", active, "Autre zone", sprites.Rounded, new Vector2(190f, 60f));
            PlaceRight((RectTransform)bayButton.transform, new Vector2(-520f, 0f));
            var deliver = CreateButton("Deliver", active, "Livrer la commande", sprites.Rounded, new Vector2(240f, 60f));
            PlaceRight((RectTransform)deliver.transform, new Vector2(-270f, 0f));
            var abandon = CreateButton("Abandon", active, "Abandonner", sprites.Rounded, new Vector2(220f, 60f));
            PlaceRight((RectTransform)abandon.transform, new Vector2(-20f, 0f));
            var message = AddText(CreateRect("Message", boardWindow, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(1200f, 40f)),
                string.Empty, 22, TextAnchor.MiddleCenter, UITheme.Warning);
            built.Board = boardWindow.parent.parent.gameObject.AddComponent<OrderBoardUI>();
            Wire(built.Board, ("panel", boardPanel), ("headerLabel", header), ("listRoot", list), ("rowTemplate", boardRow), ("activeSection", active.gameObject),
                ("activeLabel", activeLabel), ("deliverButton", deliver), ("abandonButton", abandon), ("messageLabel", message), ("closeButton", boardClose),
                ("bayButton", bayButton), ("bayButtonLabel", bayButton.GetComponentInChildren<Text>()));

            // ---- Boutique
            var shopWindow = BuildWindow(canvas.transform, "Shop", new Vector2(1500f, 980f), sprites, "Boutique", out var shopPanel, out var shopClose);
            // Catégories : Outils et Améliorations séparés (mêmes articles qu'avant, mêmes prix et règles).
            var equipmentTab = CreateButton("Tab_Equipment", shopWindow, "Outils", sprites.Rounded, new Vector2(230f, 50f));
            var upgradesTab = CreateButton("Tab_Upgrades", shopWindow, "Améliorations", sprites.Rounded, new Vector2(230f, 50f));
            var workshopTab = CreateButton("Tab_Workshop", shopWindow, "Atelier", sprites.Rounded, new Vector2(230f, 50f));
            var pranksTab = CreateButton("Tab_Pranks", shopWindow, "Farces", sprites.Rounded, new Vector2(230f, 50f));
            var productsTab = CreateButton("Tab_Products", shopWindow, "Produits", sprites.Rounded, new Vector2(230f, 50f));
            foreach (var (tab, x) in new[] { (equipmentTab, -490f), (upgradesTab, -245f), (workshopTab, 0f), (pranksTab, 245f), (productsTab, 490f) })
            {
                StyleTab(tab);
                var rect = (RectTransform)tab.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(x, -86f);
            }
            var balance = AddText(CreateRect("Balance", shopWindow, Vector2.one, Vector2.one, Vector2.one, new Vector2(-290f, -29f), new Vector2(500f, 40f)),
                string.Empty, 24, TextAnchor.MiddleRight, UITheme.Money, FontStyle.Bold);
            // Liste défilante : le catalogue (outils à acheter, améliorations, détergent) dépasse la hauteur de la fenêtre.
            // Grande mise à jour : une colonne pleine largeur, chaque ligne à la hauteur de son texte (plus de texte qui déborde),
            // icône 3D pour les outils et leurs améliorations.
            var grid = CreateScrollList(shopWindow, "Grid", new Vector2(0f, -150f), new Vector2(1440f, 700f), sprites);
            AddVerticalLayout(grid.gameObject, 8, new RectOffset(0, 0, 0, 0), TextAnchor.UpperCenter);
            var shopRow = BuildRowTemplate(grid, sprites, 1440f, 96f, 300f, withIcon: true, fitHeight: true);
            var shopMessage = AddText(CreateRect("Message", shopWindow, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(1300f, 44f)),
                string.Empty, 24, TextAnchor.MiddleCenter, Color.white);
            built.Shop = shopWindow.parent.parent.gameObject.AddComponent<ShopUI>();
            Wire(built.Shop, ("panel", shopPanel), ("balanceLabel", balance), ("listRoot", grid), ("rowTemplate", shopRow), ("messageLabel", shopMessage),
                ("closeButton", shopClose), ("equipmentTabButton", equipmentTab), ("productsTabButton", productsTab), ("workshopTabButton", workshopTab),
                ("pranksTabButton", pranksTab), ("upgradesTabButton", upgradesTab));

            // ---- Résultats
            var resultsWindow = BuildWindow(canvas.transform, "Results", new Vector2(1600f, 940f), sprites, "Résultats de la commande", out var resultsPanel, out var resultsClose);
            resultsClose.gameObject.SetActive(false);
            var orderTitle = AddText(CreateRect("Order", resultsWindow, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(1400f, 40f)),
                string.Empty, 26, TextAnchor.MiddleCenter, UITheme.TextSecondary);
            var grade = AddText(CreateRect("Grade", resultsWindow, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -130f), new Vector2(560f, 150f)),
                "A", 120, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            var score = AddText(CreateRect("Score", resultsWindow, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -285f), new Vector2(560f, 36f)),
                string.Empty, 24, TextAnchor.MiddleCenter, Color.white);
            var lines = AddText(CreateRect("Lines", resultsWindow, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -340f), new Vector2(560f, 230f)),
                string.Empty, 22, TextAnchor.UpperLeft, Color.white);
            var reward = AddText(CreateRect("Reward", resultsWindow, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -590f), new Vector2(560f, 110f)),
                string.Empty, 26, TextAnchor.UpperLeft, UITheme.Money, FontStyle.Bold);
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
            BuildComputerUI(canvas, sprites, built);
        }

        /// <summary>
        /// Ordinateur de l'atelier : un bureau avec exactement deux icônes cliquables, « Missions / Commandes » (bloc-notes
        /// coché) et « Boutique » (sac d'achats), et sous chacune un résumé tiré des données du jeu.
        /// </summary>
        static void BuildComputerUI(Canvas canvas, UISprites sprites, BuiltUI built)
        {
            var window = BuildWindow(canvas.transform, "Computer", new Vector2(1180f, 760f), sprites, "Ordinateur de l'atelier", out var panel, out var close);
            AddText(CreateRect("Hint", window, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(1000f, 34f)),
                "Choisissez une application", 22, TextAnchor.MiddleCenter, UITheme.TextMuted);
            var desktop = CreateRect("Desktop", window, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(1100f, 600f));
            AddImage(desktop, sprites.Rounded, new Color(0.06f, 0.16f, 0.24f, 0.95f));

            var missions = BuildDesktopIcon(desktop, "App_Missions", "Missions / Commandes", new Vector2(-265f, 0f), sprites, out var missionsStatus, out var missionsIcon);
            // Bloc-notes à pince, trois lignes cochées.
            IconShape(missionsIcon, "Board", new Vector2(0f, -6f), new Vector2(136f, 176f), new Color(0.62f, 0.45f, 0.28f), sprites.Rounded);
            IconShape(missionsIcon, "Paper", new Vector2(0f, -14f), new Vector2(112f, 142f), new Color(0.96f, 0.95f, 0.9f), sprites.Rounded);
            IconShape(missionsIcon, "Clip", new Vector2(0f, 76f), new Vector2(66f, 26f), new Color(0.55f, 0.58f, 0.62f), sprites.Rounded);
            for (int i = 0; i < 3; i++)
            {
                float y = 32f - 38f * i;
                IconShape(missionsIcon, $"Check_{i}", new Vector2(-34f, y), new Vector2(20f, 20f), new Color(0.3f, 0.75f, 0.4f), sprites.Rounded);
                IconShape(missionsIcon, $"Line_{i}", new Vector2(14f, y), new Vector2(62f, 10f), new Color(0.35f, 0.38f, 0.42f), sprites.White);
            }

            var shop = BuildDesktopIcon(desktop, "App_Shop", "Boutique", new Vector2(265f, 0f), sprites, out var shopStatus, out var shopIcon);
            // Sac d'achats à anses, marqué « € ».
            var handle = new Color(0.55f, 0.32f, 0.12f);
            IconShape(shopIcon, "Handle_L", new Vector2(-32f, 62f), new Vector2(12f, 50f), handle, sprites.White);
            IconShape(shopIcon, "Handle_R", new Vector2(32f, 62f), new Vector2(12f, 50f), handle, sprites.White);
            IconShape(shopIcon, "Handle_Top", new Vector2(0f, 84f), new Vector2(76f, 12f), handle, sprites.White);
            IconShape(shopIcon, "Bag", new Vector2(0f, -12f), new Vector2(156f, 136f), new Color(0.95f, 0.68f, 0.25f), sprites.Rounded);
            AddText(CreateRect("Euro", shopIcon, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -12f), new Vector2(130f, 120f)),
                "€", 86, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);

            panel.GetComponent<NavigationScope>().FirstSelected = missions;
            built.Computer = window.parent.parent.gameObject.AddComponent<ComputerUI>();
            Wire(built.Computer, ("panel", panel), ("missionsButton", missions), ("missionsStatus", missionsStatus), ("shopButton", shop),
                ("shopStatus", shopStatus), ("closeButton", close));
            panel.SetActive(false);
        }

        static Button BuildDesktopIcon(RectTransform desktop, string name, string label, Vector2 position, UISprites sprites, out Text status,
            out RectTransform icon)
        {
            var button = CreateButton(name, desktop, label, sprites.Rounded, new Vector2(470f, 500f));
            var rect = (RectTransform)button.transform;
            rect.anchoredPosition = position;
            var title = button.GetComponentInChildren<Text>();
            title.fontSize = 34;
            title.fontStyle = FontStyle.Bold;
            var titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(1f, 0f);
            titleRect.pivot = new Vector2(0.5f, 0f);
            titleRect.anchoredPosition = new Vector2(0f, 120f);
            titleRect.sizeDelta = new Vector2(-24f, 50f);
            status = AddText(CreateRect("Status", rect, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(-32f, 96f)),
                string.Empty, 22, TextAnchor.UpperCenter, UITheme.TextSecondary);
            icon = CreateRect("Icon", rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(220f, 220f));
            return button;
        }

        static void IconShape(RectTransform icon, string name, Vector2 position, Vector2 size, Color color, Sprite sprite) =>
            AddImage(CreateRect(name, icon, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size), sprite, color);

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

        /// <summary>Champ de saisie (pseudo, adresse) : fond arrondi, texte et texte indicatif.</summary>
        static InputField CreateInputField(string name, Transform parent, Vector2 position, Vector2 size, string placeholder, UISprites sprites)
        {
            var rect = CreateRect(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), position, size);
            var background = AddImage(rect, sprites.Rounded, new Color(0.04f, 0.05f, 0.06f, 0.95f), raycast: true);
            var text = AddText(Stretch("Text", rect, 12f), string.Empty, 24, TextAnchor.MiddleLeft, Color.white, shadow: false);
            text.supportRichText = false;
            var hint = AddText(Stretch("Placeholder", rect, 12f), placeholder, 22, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.35f), FontStyle.Italic, shadow: false);
            var field = rect.gameObject.AddComponent<InputField>();
            field.targetGraphic = background;
            field.textComponent = text;
            field.placeholder = hint;
            field.characterLimit = 40;
            var colors = field.colors;
            colors.selectedColor = FocusColor;
            colors.highlightedColor = new Color(0.8f, 0.93f, 1f, 1f);
            field.colors = colors;
            return field;
        }

        static Button PlaceButton(string name, Transform parent, string label, Vector2 position, Vector2 size, UISprites sprites)
        {
            var button = CreateButton(name, parent, label, sprites.Rounded, size);
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            var text = button.GetComponentInChildren<Text>();
            text.fontSize = 22;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 14;
            text.resizeTextMaxSize = 22;
            return button;
        }

        /// <summary>
        /// Fenêtre « Multijoueur » : Steam (salon, recherche, invitations, lancement) à gauche, connexion directe en réseau
        /// local à droite, message d'état et « Se reconnecter » en bas.
        /// </summary>
        static MultiplayerMenuUI BuildMultiplayerUI(Canvas canvas, UISprites sprites, RugWash.Network.NetworkContent networkContent)
        {
            var window = BuildWindow(canvas.transform, "Multiplayer", new Vector2(1500f, 920f), sprites, "Multijoueur", out var panel, out var close);
            Text Label(Transform parent, string text, Vector2 position, Vector2 size, int fontSize, Color color, FontStyle style = FontStyle.Normal)
            {
                var label = AddText(CreateRect("Label", parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), position, size),
                    text, fontSize, TextAnchor.UpperLeft, color, style);
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                return label;
            }

            // ---- Steam (Internet)
            var steam = CreateRect("Steam", window, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -96f), new Vector2(760f, 760f));
            Label(steam, "Steam (Internet)", Vector2.zero, new Vector2(760f, 40f), 28, Accent, FontStyle.Bold);
            var status = Label(steam, "Steam : …", new Vector2(0f, -44f), new Vector2(760f, 52f), 19, Muted);
            var createFriends = PlaceButton("CreateFriends", steam, "Créer une partie (amis)", new Vector2(0f, -102f), new Vector2(372f, 58f), sprites);
            var createPublic = PlaceButton("CreatePublic", steam, "Créer une partie (publique)", new Vector2(388f, -102f), new Vector2(372f, 58f), sprites);
            var search = PlaceButton("Search", steam, "Rechercher des parties", new Vector2(0f, -172f), new Vector2(372f, 58f), sprites);
            var lobbyList = CreateScrollList(steam, "Lobbies", new Vector2(-10f, -240f), new Vector2(740f, 236f), sprites);
            ((RectTransform)lobbyList.parent.parent).anchorMin = ((RectTransform)lobbyList.parent.parent).anchorMax = ((RectTransform)lobbyList.parent.parent).pivot = new Vector2(0f, 1f);
            ((RectTransform)lobbyList.parent.parent).anchoredPosition = new Vector2(0f, -240f);
            AddVerticalLayout(lobbyList.gameObject, 6, new RectOffset(0, 0, 0, 0), TextAnchor.UpperLeft);
            var lobbyRow = BuildRowTemplate(lobbyList, sprites, 740f, 78f, 110f);
            var members = Label(steam, string.Empty, new Vector2(0f, -490f), new Vector2(760f, 118f), 20, Color.white);
            // V0.3.0 : boutons du salon remontés, la ligne de sauvegarde du bas ne les recouvre plus.
            var invite = PlaceButton("Invite", steam, "Inviter des amis", new Vector2(0f, -620f), new Vector2(245f, 58f), sprites);
            var start = PlaceButton("Start", steam, "Lancer la partie", new Vector2(257f, -620f), new Vector2(245f, 58f), sprites);
            var leave = PlaceButton("LeaveLobby", steam, "Quitter le salon", new Vector2(514f, -620f), new Vector2(246f, 58f), sprites);

            // ---- Réseau local
            var local = CreateRect("Local", window, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(860f, -96f), new Vector2(600f, 760f));
            Label(local, "Réseau local (connexion directe)", Vector2.zero, new Vector2(600f, 40f), 28, Accent, FontStyle.Bold);
            Label(local, "Pseudo", new Vector2(0f, -56f), new Vector2(600f, 30f), 21, Muted);
            var nameField = CreateInputField("Name", local, new Vector2(0f, -88f), new Vector2(600f, 54f), "Votre nom", sprites);
            Label(local, "Adresse de l'hôte (IPv4 et port)", new Vector2(0f, -160f), new Vector2(600f, 30f), 21, Muted);
            var address = CreateInputField("Address", local, new Vector2(0f, -192f), new Vector2(600f, 54f), "192.168.1.20:7777", sprites);
            var host = PlaceButton("HostDirect", local, "Héberger sur ce PC", new Vector2(0f, -268f), new Vector2(292f, 58f), sprites);
            var join = PlaceButton("JoinDirect", local, "Rejoindre", new Vector2(308f, -268f), new Vector2(292f, 58f), sprites);
            Label(local, "Pour jouer sur le même réseau (ou une adresse joignable, port 7777 ouvert). " +
                "Pas d'invitation ni de recherche : l'hôte communique son adresse. Pour jouer par Internet sans ouvrir de port, utilisez Steam.",
                new Vector2(0f, -346f), new Vector2(600f, 140f), 19, Muted);

            // ---- Option de l'hôte (grande mise à jour) : partie réservée aux possesseurs de l'édition Premium (vérifiée par Steam).
            Label(local, "Option de l'hôte", new Vector2(0f, -500f), new Vector2(600f, 30f), 21, Muted);
            var premium = PlaceButton("PremiumOnly", local, "Premium uniquement : non", new Vector2(0f, -534f), new Vector2(372f, 54f), sprites);
            var premiumNote = Label(local, string.Empty, new Vector2(0f, -596f), new Vector2(600f, 150f), 17, Muted);
            premiumNote.resizeTextForBestFit = true;
            premiumNote.resizeTextMinSize = 12;
            premiumNote.resizeTextMaxSize = 17;

            // ---- État
            var message = AddText(CreateRect("Message", window, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-150f, 28f), new Vector2(1100f, 56f)),
                string.Empty, 21, TextAnchor.MiddleCenter, UITheme.Success);
            message.horizontalOverflow = HorizontalWrapMode.Wrap;
            var reconnect = CreateButton("Reconnect", window, "Se reconnecter", sprites.Rounded, new Vector2(260f, 56f));
            var reconnectRect = (RectTransform)reconnect.transform;
            reconnectRect.anchorMin = reconnectRect.anchorMax = reconnectRect.pivot = new Vector2(1f, 0f);
            reconnectRect.anchoredPosition = new Vector2(-40f, 28f);

            // ---- Emplacement de sauvegarde multijoueur (jamais un emplacement solo)
            var slotLabel = AddText(CreateRect("SlotLabel", window, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(40f, 88f), new Vector2(1060f, 50f)),
                "Sauvegarde : …", 20, TextAnchor.MiddleLeft, UITheme.Money);
            // V0.2.5 : la ligne explique aussi qu'en rejoignant, on joue sur la sauvegarde de l'hôte.
            slotLabel.resizeTextForBestFit = true;
            slotLabel.resizeTextMinSize = 13;
            slotLabel.resizeTextMaxSize = 20;
            slotLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            // V0.3.0 : le texte rétrécit dans sa zone (Inter est plus large qu'Arial) au lieu de déborder sur les boutons du salon.
            slotLabel.verticalOverflow = VerticalWrapMode.Truncate;
            slotLabel.resizeTextMinSize = 12;
            var slotButton = CreateButton("Slot", window, "Changer d'emplacement", sprites.Rounded, new Vector2(320f, 56f));
            var slotRect = (RectTransform)slotButton.transform;
            slotRect.anchorMin = slotRect.anchorMax = slotRect.pivot = new Vector2(1f, 0f);
            slotRect.anchoredPosition = new Vector2(-40f, 88f);

            var ui = window.parent.parent.gameObject.AddComponent<MultiplayerMenuUI>();
            Wire(ui, ("panel", panel), ("content", networkContent), ("steamStatusLabel", status), ("createFriendsButton", createFriends),
                ("createPublicButton", createPublic), ("searchButton", search), ("lobbyListRoot", lobbyList), ("lobbyRowTemplate", lobbyRow),
                ("lobbyMembersLabel", members), ("inviteButton", invite), ("startButton", start), ("leaveLobbyButton", leave),
                ("nameField", nameField), ("addressField", address), ("hostDirectButton", host), ("joinDirectButton", join),
                ("reconnectButton", reconnect), ("messageLabel", message), ("closeButton", close),
                ("slotLabel", slotLabel), ("slotButton", slotButton));
            Wire(ui, ("premiumButton", premium), ("premiumLabel", premium.GetComponentInChildren<Text>()), ("premiumNote", premiumNote));
            panel.GetComponent<NavigationScope>().FirstSelected = host;
            if (!ui.IsConfigured(out string missing)) throw new System.InvalidOperationException($"[RugWash] Fenêtre Multijoueur incomplète : {missing}");
            panel.SetActive(false);
            return ui;
        }

        /// <summary>
        /// Emplacements de sauvegarde (trois par mode) : une ligne par emplacement (résumé lu dans le fichier, bouton d'action,
        /// « Effacer »), le niveau du profil, et une confirmation avant d'effacer.
        /// </summary>
        static SaveSlotsUI BuildSaveSlotsUI(Canvas canvas, UISprites sprites)
        {
            var window = BuildWindow(canvas.transform, "SaveSlots", new Vector2(1380f, 820f), sprites, "Parties", out var panel, out var close);
            var title = window.Find("Title").GetComponent<Text>();
            var profile = AddText(CreateRect("Profile", window, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(1300f, 34f)),
                "Niveau 1", 22, TextAnchor.MiddleCenter, UITheme.Money, FontStyle.Bold);
            var list = CreateRect("Slots", window, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -132f), new Vector2(1300f, 480f));
            AddVerticalLayout(list.gameObject, 14, new RectOffset(0, 0, 0, 0), TextAnchor.UpperCenter);
            var rows = new List<ListRowView>();
            var erases = new List<Button>();
            for (int i = 0; i < SaveSlots.CountPerMode; i++)
            {
                var row = BuildRowTemplate(list, sprites, 1300f, 150f, 360f);
                row.name = $"Slot{i + 1}";
                // Détails sur deux lignes, à l'écart des boutons (en bas à droite) : rien ne se chevauche.
                var details = (RectTransform)row.transform.Find("Details");
                details.sizeDelta = new Vector2(1300f - 420f, 150f - 48f);
                var detailsText = details.GetComponent<Text>();
                detailsText.horizontalOverflow = HorizontalWrapMode.Wrap;
                detailsText.verticalOverflow = VerticalWrapMode.Truncate;
                detailsText.resizeTextForBestFit = true;
                detailsText.resizeTextMinSize = 14;
                detailsText.resizeTextMaxSize = 19;
                var action = (RectTransform)row.ActionButton.transform;
                action.anchorMin = action.anchorMax = action.pivot = new Vector2(1f, 0f);
                action.anchoredPosition = new Vector2(-16f, 14f);
                action.sizeDelta = new Vector2(220f, 54f);
                var erase = CreateButton("Erase", row.transform, "Effacer", sprites.Rounded, new Vector2(150f, 54f));
                var eraseRect = (RectTransform)erase.transform;
                eraseRect.anchorMin = eraseRect.anchorMax = eraseRect.pivot = new Vector2(1f, 0f);
                eraseRect.anchoredPosition = new Vector2(-250f, 14f);
                erase.GetComponentInChildren<Text>().fontSize = 21;
                rows.Add(row);
                erases.Add(erase);
            }
            var message = AddText(CreateRect("Message", window, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(1280f, 110f)),
                string.Empty, 20, TextAnchor.MiddleCenter, UITheme.TextSecondary);
            message.horizontalOverflow = HorizontalWrapMode.Wrap;

            var confirm = Stretch("Confirm", window);
            AddImage(confirm, sprites.Rounded, new Color(0.02f, 0.03f, 0.04f, 0.92f), raycast: true);
            var confirmLabel = AddText(CreateRect("Text", confirm, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 70f), new Vector2(980f, 200f)),
                "Effacer ?", 30, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            confirmLabel.supportRichText = true;
            confirmLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            var yes = CreateButton("Yes", confirm, "Effacer", sprites.Rounded, new Vector2(300f, 64f));
            ((RectTransform)yes.transform).anchoredPosition = new Vector2(-170f, -110f);
            var no = CreateButton("No", confirm, "Annuler", sprites.Rounded, new Vector2(300f, 64f));
            ((RectTransform)no.transform).anchoredPosition = new Vector2(170f, -110f);
            confirm.gameObject.AddComponent<NavigationScope>().FirstSelected = no;

            var ui = window.parent.parent.gameObject.AddComponent<SaveSlotsUI>();
            Wire(ui, ("panel", panel), ("titleLabel", title), ("profileLabel", profile), ("messageLabel", message), ("confirmPanel", confirm.gameObject),
                ("confirmLabel", confirmLabel), ("confirmYesButton", yes), ("confirmNoButton", no), ("closeButton", close));
            WireList(ui, "rows", rows);
            WireList(ui, "eraseButtons", erases);
            panel.GetComponent<NavigationScope>().FirstSelected = rows[0].ActionButton;
            if (!ui.IsConfigured(out string missing)) throw new InvalidOperationException($"[RugWash] Fenêtre des emplacements incomplète : {missing}");
            confirm.gameObject.SetActive(false);
            panel.SetActive(false);
            return ui;
        }

        /// <summary>
        /// Paramètres en trois onglets (Général, Commandes, Touches). Les touches sont dans une liste défilante avec une
        /// colonne clavier/souris et une colonne manette.
        /// </summary>
        static SettingsUI BuildSettingsUI(Canvas canvas, UISprites sprites)
        {
            var window = BuildWindow(canvas.transform, "Settings", new Vector2(1340f, 900f), sprites, "Paramètres", out var panel, out var close);

            var tabs = new List<Button>();
            var pages = new List<GameObject>();
            string[] tabNames = { "Général", "Graphismes", "Commandes", "Touches" };
            for (int i = 0; i < tabNames.Length; i++)
            {
                var tab = CreateButton($"Tab_{i}", window, tabNames[i], sprites.Rounded, new Vector2(250f, 50f));
                StyleTab(tab);
                var rect = (RectTransform)tab.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2((i - 1.5f) * 265f, -86f);
                tabs.Add(tab);
                var page = CreateRect($"Page_{i}", window, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                pages.Add(page.gameObject);
            }

            RectTransform Column(Transform page, bool left)
            {
                var anchor = left ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
                var column = CreateRect(left ? "Left" : "Right", page, anchor, anchor, anchor, new Vector2(left ? 50f : -50f, -160f), new Vector2(580f, 640f));
                AddVerticalLayout(column.gameObject, 6, new RectOffset(0, 0, 0, 0), TextAnchor.UpperLeft);
                return column;
            }

            // ---- Général : audio et affichage
            var general = pages[0].transform;
            var audioColumn = Column(general, true);
            SectionTitle(audioColumn, "Audio");
            var master = SliderRow(audioColumn, "Volume général", sprites, out var masterValue);
            var effects = SliderRow(audioColumn, "Effets", sprites, out var effectsValue);
            var interfaceSlider = SliderRow(audioColumn, "Interface", sprites, out var interfaceValue);
            var musicSlider = SliderRow(audioColumn, "Musique", sprites, out var musicValue);
            // V0.2.5 : plus de choix de difficulté ; elle suit la progression des missions (réputation, taille des tapis).
            var displayColumn = Column(general, false);
            SectionTitle(displayColumn, "Affichage");
            var resolution = StepperRow(displayColumn, "Résolution", sprites);
            var display = StepperRow(displayColumn, "Mode d'affichage", sprites);
            // ---- Graphismes (grande mise à jour) : préréglage, synchronisation, limite d'images, post-traitement, état du ray tracing.
            var graphics = pages[1].transform;
            var graphicsColumn = Column(graphics, true);
            SectionTitle(graphicsColumn, "Rendu");
            var quality = StepperRow(graphicsColumn, "Qualité graphique", sprites);
            var vSync = StepperRow(graphicsColumn, "Synchronisation verticale", sprites);
            var frameCap = StepperRow(graphicsColumn, "Limite d'images", sprites);
            var postProcess = StepperRow(graphicsColumn, "Post-traitement", sprites);
            var infoColumn = Column(graphics, false);
            SectionTitle(infoColumn, "Détails");
            var graphicsInfo = AddText(CreateRect("GraphicsInfo", infoColumn, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(580f, 320f)),
                string.Empty, 19, TextAnchor.UpperLeft, Muted);
            graphicsInfo.gameObject.AddComponent<LayoutElement>().preferredHeight = 320f;
            // Grande mise à jour : chat vocal en ligne (micro de Windows, voix entendue selon la distance).
            SectionTitle(displayColumn, "Chat vocal (en ligne)");
            var voiceMode = StepperRow(displayColumn, "Micro", sprites);
            var voiceVolume = SliderRow(displayColumn, "Voix des joueurs", sprites, out var voiceVolumeValue);

            // ---- Commandes : souris, caméra, manette
            var controls = pages[2].transform;
            var mouseColumn = Column(controls, true);
            SectionTitle(mouseColumn, "Souris et caméra");
            var sensitivity = SliderRow(mouseColumn, "Sensibilité souris", sprites, out var sensitivityValue);
            var invert = StepperRow(mouseColumn, "Inverser l'axe vertical", sprites);
            var motion = SliderRow(mouseColumn, "Balancement", sprites, out var motionValue);
            var padColumn = Column(controls, false);
            SectionTitle(padColumn, "Manette");
            var padLook = SliderRow(padColumn, "Vitesse caméra (stick)", sprites, out var padLookValue);
            var deadZone = SliderRow(padColumn, "Zone morte des sticks", sprites, out var deadZoneValue);
            var vibration = SliderRow(padColumn, "Vibrations", sprites, out var vibrationValue);
            foreach (var value in new[] { padLookValue, vibrationValue })
            {
                value.resizeTextForBestFit = true;
                value.resizeTextMinSize = 12;
                value.resizeTextMaxSize = 20;
            }

            // ---- Touches : clavier/souris et manette
            var keys = pages[3].transform;
            var header = CreateRect("Header", keys, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(1240f, 36f));
            AddText(CreateRect("Action", header, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(300f, 36f)),
                "Action", 22, TextAnchor.MiddleLeft, Accent, FontStyle.Bold);
            AddText(CreateRect("Keyboard", header, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(330f, 0f), new Vector2(380f, 36f)),
                "Clavier / souris (disposition physique)", 22, TextAnchor.MiddleLeft, Accent, FontStyle.Bold);
            AddText(CreateRect("Gamepad", header, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(760f, 0f), new Vector2(300f, 36f)),
                "Manette", 22, TextAnchor.MiddleLeft, Accent, FontStyle.Bold);
            var list = CreateScrollList(keys, "Bindings", new Vector2(-10f, -192f), new Vector2(1240f, 520f), sprites);
            AddVerticalLayout(list.gameObject, 6, new RectOffset(0, 0, 0, 0), TextAnchor.UpperLeft);

            var template = CreateRect("RebindTemplate", list, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1240f, 54f));
            template.gameObject.AddComponent<LayoutElement>().preferredHeight = 54f;
            AddImage(template, sprites.Rounded, UITheme.Row);
            var actionLabel = AddText(CreateRect("Action", template, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(300f, 44f)),
                "Action", 21, TextAnchor.MiddleLeft, Color.white);
            Text BindingText(string name, float x, float width)
            {
                var text = AddText(CreateRect(name, template, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(width, 44f)),
                    "—", 21, TextAnchor.MiddleLeft, UITheme.Money, FontStyle.Bold);
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = 12;
                text.resizeTextMaxSize = 21;
                return text;
            }
            Button ChangeButton(string name, float x)
            {
                var button = CreateButton(name, template, "Modifier", sprites.Rounded, new Vector2(140f, 42f));
                var rect = (RectTransform)button.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = new Vector2(x, 0f);
                button.GetComponentInChildren<Text>().fontSize = 21;
                return button;
            }
            var bindingLabel = BindingText("Binding", 330f, 250f);
            var change = ChangeButton("Change", 590f);
            var gamepadLabel = BindingText("GamepadBinding", 760f, 300f);
            var gamepadChange = ChangeButton("ChangeGamepad", 1080f);
            var rebindRow = template.gameObject.AddComponent<RebindRowView>();
            Wire(rebindRow, ("actionLabel", actionLabel), ("bindingLabel", bindingLabel), ("changeButton", change),
                ("gamepadLabel", gamepadLabel), ("gamepadButton", gamepadChange));

            var reset = CreateButton("ResetBindings", keys, "Touches et boutons par défaut", sprites.Rounded, new Vector2(420f, 56f));
            var resetRect = (RectTransform)reset.transform;
            resetRect.anchorMin = resetRect.anchorMax = resetRect.pivot = new Vector2(0.5f, 0f);
            resetRect.anchoredPosition = new Vector2(0f, 76f);

            var message = AddText(CreateRect("Message", window, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(1200f, 40f)),
                string.Empty, 22, TextAnchor.MiddleCenter, UITheme.Success);

            var ui = window.parent.parent.gameObject.AddComponent<SettingsUI>();
            Wire(ui, ("musicSlider", musicSlider), ("musicValue", musicValue));
            Wire(ui, ("voiceModeStepper", voiceMode), ("voiceVolumeSlider", voiceVolume), ("voiceVolumeValue", voiceVolumeValue));
            Wire(ui, ("vSyncStepper", vSync), ("frameCapStepper", frameCap), ("postProcessStepper", postProcess), ("graphicsInfoLabel", graphicsInfo));
            Wire(ui, ("panel", panel), ("masterSlider", master), ("effectsSlider", effects), ("interfaceSlider", interfaceSlider),
                ("sensitivitySlider", sensitivity), ("masterValue", masterValue), ("effectsValue", effectsValue), ("interfaceValue", interfaceValue),
                ("sensitivityValue", sensitivityValue), ("motionSlider", motion), ("motionValue", motionValue), ("invertYStepper", invert), ("resolutionStepper", resolution),
                ("displayModeStepper", display), ("qualityStepper", quality), ("rebindRoot", list), ("rebindTemplate", rebindRow),
                ("resetBindingsButton", reset), ("closeButton", close), ("messageLabel", message),
                ("gamepadLookSlider", padLook), ("deadZoneSlider", deadZone), ("vibrationSlider", vibration),
                ("gamepadLookValue", padLookValue), ("deadZoneValue", deadZoneValue), ("vibrationValue", vibrationValue), ("monitor", uiMonitor));
            WireList(ui, "tabButtons", tabs);
            WireList(ui, "tabPages", pages);
            panel.GetComponent<NavigationScope>().FirstSelected = tabs[2];
            for (int i = 1; i < pages.Count; i++) pages[i].SetActive(false);
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

        /// <summary>
        /// EventSystem (souris, clavier et manette via les actions d'interface par défaut), moniteur du périphérique actif
        /// et navigateur des fenêtres (focus, sélection restaurée, défilement).
        /// </summary>
        static UINavigator CreateEventSystem(Transform parent)
        {
            var go = new GameObject("EventSystem");
            go.transform.SetParent(parent, false);
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
            uiMonitor = go.AddComponent<InputDeviceMonitor>();
            var navigator = go.AddComponent<UINavigator>();
            Wire(navigator, ("monitor", uiMonitor));
            return navigator;
        }

        /// <summary>Libellé de bouton réécrit selon le périphérique actif (ex. « Fermer ({back}) »).</summary>
        static void AddPrompt(Button button, string template)
        {
            var label = button.GetComponentInChildren<Text>(true);
            var prompt = label.gameObject.AddComponent<InputPromptText>();
            Wire(prompt, ("monitor", uiMonitor));
            SetString(prompt, "template", template);
        }
    }
}
