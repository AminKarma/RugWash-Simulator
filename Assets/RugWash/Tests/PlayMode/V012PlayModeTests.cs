using System.Collections;
using System.IO;
using NUnit.Framework;
using System.Collections.Generic;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Network;
using RugWash.Orders;
using RugWash.Save;
using RugWash.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RugWash.Tests
{
    /// <summary>
    /// Update v0.1.2, jalon 1, dans la vraie scène de l'atelier : plus de tapis d'entraînement, fin du tapis à moins de 1 %
    /// de saleté, seuils de l'aire de lavage (25 % de propreté pour une commande, recontamination au-delà de 40 % de
    /// saleté), sol de la salle hors zone de nettoyage (y compris dans une ancienne sauvegarde).
    /// </summary>
    public class V012PlayModeTests
    {
        GameBootstrap game;

        [UnitySetUp]
        public IEnumerator LoadWorkshop()
        {
            TestSaveSandbox.Reset();
            yield return Load();
        }

        IEnumerator Load()
        {
            SceneManager.LoadScene("Workshop", LoadSceneMode.Single);
            yield return null;
            yield return null;
            game = Object.FindAnyObjectByType<GameBootstrap>();
            Assert.IsTrue(game != null && game.IsReady);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (game != null && game.ToolController != null) game.ToolController.AutomationUseOverride = false;
            if (Camera.main != null && cameraOffset != 0f)
            {
                Camera.main.transform.localPosition -= Vector3.up * cameraOffset;
                cameraOffset = 0f;
            }
            var session = MultiplayerSession.Find();
            if (session != null)
            {
                if (session.InSession) session.Manager.Shutdown();
                if (session.Manager != null) Object.Destroy(session.Manager.gameObject);
                Object.Destroy(session.gameObject);
            }
            Time.timeScale = 1f;
            yield return null;
        }

        float cameraOffset;

        /// <summary>Hauteur des yeux décalée (taille de personnage différente) : la caméra et les outils qu'elle porte.</summary>
        void SetEyeOffset(float offset)
        {
            Camera.main.transform.localPosition += Vector3.up * (offset - cameraOffset);
            cameraOffset = offset;
        }

        /// <summary>Place le joueur face à un point du tapis, puis corrige l'inclinaison avec la hauteur réelle des yeux.</summary>
        IEnumerator AimAtRug(Vector2 uv, float yaw, float standoff)
        {
            Vector3 target = game.Rug.UVToWorld(uv);
            Vector3 back = Quaternion.Euler(0f, yaw, 0f) * Vector3.back * standoff;
            game.PlayerController.Teleport(new Vector3(target.x + back.x, 0.06f, target.z + back.z), yaw, 40f);
            yield return new WaitForSeconds(0.15f);
            Vector3 eye = Camera.main.transform.position;
            float horizontal = new Vector2(target.x - eye.x, target.z - eye.z).magnitude;
            float pitch = Mathf.Atan2(eye.y - target.y, horizontal) * Mathf.Rad2Deg;
            game.PlayerController.Teleport(game.PlayerController.transform.position, yaw, pitch);
        }

        static readonly (Vector2 uv, float yaw, float standoff)[] Poses =
        {
            (new Vector2(0.5f, 0.5f), 0f, 0.7f),
            (new Vector2(0.3f, 0.35f), 0f, 1.2f),
            (new Vector2(0.7f, 0.6f), 90f, 1.0f),
            (new Vector2(0.45f, 0.55f), -90f, 1.5f),
            (new Vector2(0.6f, 0.4f), 180f, 0.9f),
            (new Vector2(0.5f, 0.5f), 35f, 1.6f),
        };

        RugCleaningController Pad => game.WorkSurface.Surface;

        /// <summary>Salit une part donnée de l'aire de lavage (rangées complètes).</summary>
        void SoilWashArea(float fraction)
        {
            var grid = Pad.Grid;
            var deposit = new DepositOperation { Type = DirtType.Mud, Mud = 0.4f, Water = 0.3f };
            grid.ApplyRect(new RectInt(0, 0, grid.Width, Mathf.RoundToInt(grid.Height * fraction)), 1f, ref deposit);
            game.WorkSurface.RefreshStatus(force: true);
        }

        /// <summary>Retire la saleté des premières rangées de l'aire de lavage (comme un balayage réussi).</summary>
        void CleanWashAreaRows(float fraction)
        {
            var grid = Pad.Grid;
            var clear = new ClearDirtOperation();
            grid.ApplyRect(new RectInt(0, 0, grid.Width, Mathf.RoundToInt(grid.Height * fraction)), 1f, ref clear);
            game.WorkSurface.RefreshStatus(force: true);
        }

        struct ScaleDirt : ICellOperation
        {
            public float Factor;

            public void Apply(ref RugCell cell, float weight)
            {
                cell.Dust *= Factor;
                cell.Stain *= Factor;
                cell.Loosened *= Factor;
            }
        }

        /// <summary>Opération réelle (un peu d'eau) passée par le contrôleur : déclenche la vérification de fin du tapis.</summary>
        int TouchRug()
        {
            var water = new DepositOperation { Water = 0.001f };
            return game.Rug.ApplyOperation(game.Rug.UVToWorld(new Vector2(0.5f, 0.5f)), 0.1f, 2f, ref water);
        }

        // ------------------------------------------------------------------ 1.9 Plus de tapis d'entraînement

        [UnityTest]
        public IEnumerator Workshop_StartsWithoutRug_ARugOnlyComesWithAnOrder_AndLeavesWithIt()
        {
            Assert.IsFalse(game.Rug.IsLoaded, "Aucun tapis d'entraînement au lancement.");
            Assert.IsNull(game.Rug.Definition);
            yield return new WaitForSeconds(0.15f);
            StringAssert.Contains("Aucun tapis", game.Hud.CleanlinessText);

            Assert.IsTrue(game.GameState.Pause());
            yield return null;
            var pause = Object.FindAnyObjectByType<PauseMenuUI>();
            foreach (var button in pause.GetComponentsInChildren<UnityEngine.UI.Button>(false))
                Assert.AreNotEqual("NextRug", button.name, "Plus de bouton « tapis d'entraînement suivant ».");
            Assert.IsFalse(GameObject.Find("ResetRug") != null && GameObject.Find("ResetRug").activeInHierarchy,
                "Hors commande, rien à recommencer.");
            game.GameState.Resume();
            yield return null;

            var order = game.Orders.Book.Orders[0];
            Assert.AreEqual(OrderStartResult.Started, game.Orders.TryStart(order));
            Assert.IsTrue(game.Rug.IsLoaded);
            Assert.AreEqual(order.rug, game.Rug.Definition);

            Assert.IsTrue(game.Orders.Abandon());
            yield return null;
            Assert.IsFalse(game.Rug.IsLoaded, "Commande abandonnée : le tapis repart.");

            Assert.AreEqual(OrderStartResult.Started, game.Orders.TryStart(order));
            Assert.IsNotNull(game.Orders.Deliver());
            yield return null;
            game.Results.BackToWorkshop();
            yield return null;
            Assert.IsFalse(game.Rug.IsLoaded, "Commande livrée : le tapis repart avec le client.");
            Assert.AreEqual(GameState.Workshop, game.GameState.State);

            // Ni la sauvegarde ni le rechargement ne font réapparaître un tapis.
            Assert.IsTrue(game.SaveSystem.SaveNow("test sans tapis"));
            yield return Load();
            Assert.IsFalse(game.Rug.IsLoaded);
        }

        // ------------------------------------------------------------------ Phase 6 : fin du tapis à moins de 1 %

        [UnityTest]
        public IEnumerator Rug_IsFinishedBelowOnePercentRemaining_ShowsSuccess_AndGradesAtOneHundredPercent()
        {
            var order = game.Orders.Book.Orders[0];
            Assert.AreEqual(OrderStartResult.Started, game.Orders.TryStart(order));
            var grid = game.Rug.Grid;
            float initial = grid.InitialDirt;
            Assert.Greater(initial, 0f);

            // 2 % restant : pas encore terminé.
            var scale = new ScaleDirt { Factor = 0.02f };
            grid.ApplyAll(ref scale);
            Assert.Greater(TouchRug(), 0);
            Assert.IsFalse(game.Rug.IsFinished);
            Assert.Less(game.Rug.Cleanliness01, 0.99f);

            // 0,5 % restant (sous 1 %) : terminé, le reliquat est retiré.
            scale.Factor = 0.25f;
            grid.ApplyAll(ref scale);
            Assert.Less(grid.TotalDirt, initial * 0.01f);
            TouchRug();
            Assert.IsTrue(game.Rug.IsFinished);
            Assert.AreEqual(0f, grid.TotalDirt, "Plus aucune saleté : la propreté ne reste pas bloquée à 99 %.");
            Assert.AreEqual(1f, game.Rug.Cleanliness01);
            Assert.IsTrue(game.Orders.ObjectiveNotified, "L'objectif de propreté est atteint.");
            yield return new WaitForSeconds(0.15f);
            StringAssert.Contains("100 %", game.Hud.CleanlinessText);
            StringAssert.Contains("terminé", game.Hud.CleanlinessText);
            StringAssert.Contains("Tapis terminé", game.Hud.ToastText);

            // La recontamination continue ne touche jamais un tapis terminé.
            SoilWashArea(0.9f);
            game.WorkSurface.ContaminateResidentRug(5f);
            Assert.AreEqual(0f, grid.TotalDirt);

            var result = game.Orders.Deliver();
            Assert.IsNotNull(result);
            Assert.AreEqual(1f, game.Orders.LastResult.Summary.Cleanliness01, 1e-6f, "Note calculée sur un tapis propre à 100 %.");
        }

        // ------------------------------------------------------------------ 1.10 Seuils de l'aire de lavage

        [UnityTest]
        public IEnumerator DirtyWashArea_BelowTwentyFivePercentClean_BlocksNewOrders_UntilCleaned()
        {
            SoilWashArea(0.8f);
            var soiled = game.WorkSurface.Status;
            Assert.Less(soiled.CleanlinessPercent, 25);
            Assert.AreEqual(Mathf.FloorToInt((1f - soiled.SoiledFraction) * 100f + 1e-4f), soiled.CleanlinessPercent, "Propreté = 1 − saleté.");
            Assert.IsFalse(game.WorkSurface.AllowsNewOrder);
            yield return new WaitForSeconds(0.5f);
            StringAssert.Contains("trop sale", game.Hud.WorkSurfaceAlert);
            StringAssert.Contains("25 % requis", game.Hud.WorkSurfaceAlert);

            var order = game.Orders.Book.Orders[0];
            Assert.AreEqual(OrderStartResult.WorkAreaTooDirty, game.Orders.TryStart(order));
            Assert.IsFalse(game.Rug.IsLoaded);
            Assert.IsTrue(game.OrderBoard.Open());
            yield return null;
            StringAssert.Contains("Aire de lavage à nettoyer", game.OrderBoard.Rows[0].StatusText);
            game.OrderBoard.Accept(0);
            StringAssert.Contains("Aire de lavage trop sale", game.OrderBoard.LastMessage, "Le refus explique pourquoi.");
            game.OrderBoard.Close();

            // Nettoyée jusqu'à 50 % de propreté : la commande démarre (mais le tapis se salit : saleté > 40 %).
            CleanWashAreaRows(0.3f);
            Assert.AreEqual(50, game.WorkSurface.Status.CleanlinessPercent);
            Assert.IsTrue(game.WorkSurface.AllowsNewOrder);
            Assert.AreEqual(OrderStartResult.Started, game.Orders.TryStart(order));
            Assert.Greater(game.WorkSurface.LastContamination, 0f, "Bordure recontaminée à la pose.");
        }

        [UnityTest]
        public IEnumerator RugOnADirtyWashArea_KeepsGettingDirtier_UntilTheAreaIsCleaned()
        {
            var order = game.Orders.Book.Orders[0];
            Assert.AreEqual(OrderStartResult.Started, game.Orders.TryStart(order));
            Assert.AreEqual(0f, game.WorkSurface.LastContamination);
            float start = game.Rug.Grid.TotalDirt;

            // L'aire devient très sale pendant le nettoyage (70 % de saleté) : la bordure du tapis reprend de la boue.
            SoilWashArea(0.7f);
            yield return new WaitForSeconds(1.5f);
            float dirtier = game.Rug.Grid.TotalDirt;
            Assert.Greater(dirtier, start, "Le tapis posé sur une aire trop sale se salit.");
            Assert.Greater(game.WorkSurface.ContinuousContamination, 0f);

            // Aire nettoyée sous le seuil (30 % de saleté) : plus rien n'est ajouté.
            CleanWashAreaRows(0.4f);
            Assert.IsFalse(game.WorkSurface.ContaminatesRug);
            yield return new WaitForSeconds(0.6f);
            float settled = game.Rug.Grid.TotalDirt;
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(settled, game.Rug.Grid.TotalDirt, 1e-4f);
        }

        // ------------------------------------------------------------------ 1.6 Sol de la salle (ancienne sauvegarde)

        [UnityTest]
        public IEnumerator OldSave_WithADirtyRoomFloor_LoadsWithACleanFloor()
        {
            var floor = game.RoomFloor;
            Assert.IsTrue(game.SaveSystem.SaveNow("base"));
            var dirty = new RugCleaningGrid(floor.Grid.Width, floor.Grid.Height, floor.Grid.SizeMeters);
            dirty.SetCell(10, 10, new RugCell { Dust = 0.8f, DustType = DirtType.Dust, Moisture = 0.5f });
            var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(TestSaveSandbox.SavePath));
            data.workshop.floor = SurfaceStateCodec.Encode(dirty);
            File.WriteAllText(TestSaveSandbox.SavePath, JsonUtility.ToJson(data));

            yield return Load();
            Assert.AreEqual(0f, game.RoomFloor.Grid.TotalDirt + game.RoomFloor.Grid.TotalMoisture, 1e-6f,
                "Le sol de la salle, hors zone de nettoyage, est ignoré à la lecture d'une ancienne sauvegarde.");
        }

        // ------------------------------------------------------------------ 1.4 / 1.3 Outils : tête sur le point visé, jet depuis la buse

        [UnityTest]
        public IEnumerator ContactTools_LocalHeadNeverSinksBelowTheSurface_AtAllDistancesAnglesAndEyeHeights()
        {
            TestRugs.LoadDefault(game);
            var tools = game.ToolController;
            var report = new List<string>();
            foreach (var tool in tools.Tools) tool.IsUnlocked = true;
            foreach (int slot in new[] { 1, 3, 5, 7, 8 })
            {
                Assert.IsTrue(tools.SelectSlot(slot));
                var tool = tools.ActiveTool;
                Assert.IsTrue(tool.IsContactTool, $"{tool.name} : manche télescopique câblé.");
                foreach (float eye in new[] { -0.15f, 0f, 0.15f })
                {
                    SetEyeOffset(eye);
                    foreach (var pose in Poses)
                    {
                        yield return AimAtRug(pose.uv, pose.yaw, pose.standoff);
                        yield return new WaitForSeconds(0.35f);
                        var aim = tools.CurrentAim;
                        Assert.IsTrue(aim.HasRug, $"{tool.name} : le tapis doit être visé ({pose.uv}).");
                        // Hors de portée, la cible est le point du rayon à la portée maximale (l'outil se tend sans tricher).
                        if (aim.Distance <= tool.Range)
                            Assert.Less(Vector3.Distance(tool.AimTarget, aim.Point), 1e-3f, "La cible est le point traité (réticule).");
                        // Mission de corrections : mesure pendant l'utilisation (animation comprise), là où la tête s'enfonçait.
                        tools.AutomationUseOverride = true;
                        float lowest = float.MaxValue;
                        for (int frame = 0; frame < 20; frame++)
                        {
                            yield return null;
                            lowest = Mathf.Min(lowest, LowestPoint(tool.AimTip.parent));
                            Assert.LessOrEqual(tool.ReachExtension, 1e-3f, $"{tool.name} : aucun allongement visible du manche à la première personne.");
                        }
                        tools.AutomationUseOverride = false;
                        Assert.GreaterOrEqual(lowest, aim.Point.y - 0.005f,
                            $"{tool.name} : tête à {(aim.Point.y - lowest) * 100f:0.0} cm sous la surface (distance {aim.Distance:0.00} m, " +
                            $"yeux {eye:+0.00;-0.00} m, lacet {pose.yaw}).");
                        report.Add($"{tool.name} d={aim.Distance:0.00} m yeux {eye:+0.00;-0.00} : point bas de la tête {(lowest - aim.Point.y) * 100f:0.0} cm " +
                                   $"au-dessus de la surface, extrémité à {tool.TipToTargetMeters * 100f:0.0} cm de la cible");
                    }
                }
                SetEyeOffset(0f);
            }
            Debug.Log("[RugWash] Portée des outils de contact :\n" + string.Join("\n", report));
        }

        /// <summary>Point le plus bas (monde) des maillages visibles sous <paramref name="root"/>.</summary>
        static float LowestPoint(Transform root)
        {
            float lowest = float.MaxValue;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null || !filter.GetComponent<MeshRenderer>().enabled) continue;
                var matrix = filter.transform.localToWorldMatrix;
                foreach (var v in filter.sharedMesh.vertices) lowest = Mathf.Min(lowest, matrix.MultiplyPoint3x4(v).y);
            }
            return lowest;
        }

        [UnityTest]
        public IEnumerator SprayEffects_LeaveFromTheToolMuzzle_AndFollowTheTarget_WhenMovingAndTurning()
        {
            TestRugs.LoadDefault(game);
            var tools = game.ToolController;
            foreach (var (slot, effect) in new[] { (4, "WaterJet"), (2, "SoapMist"), (6, "FoamStream") })
            {
                Assert.IsTrue(tools.SelectSlot(slot));
                var tool = tools.ActiveTool;
                ParticleSystem jet = null;
                foreach (var candidate in tool.AimTip.GetComponentsInChildren<ParticleSystem>(true))
                    if (candidate.name == effect) jet = candidate;
                Assert.IsNotNull(jet, $"{effect} est attaché à la bouche de {tool.name}.");
                foreach (var pose in new[] { Poses[1], Poses[2], Poses[5] })
                {
                    yield return AimAtRug(pose.uv, pose.yaw, pose.standoff);
                    tools.AutomationUseOverride = true;
                    yield return new WaitForSeconds(0.3f);
                    Assert.Less(Vector3.Distance(jet.transform.position, tool.AimTip.position), 0.02f, $"{effect} part de la buse.");
                    if (slot == 4)
                    {
                        Vector3 toTarget = tool.AimTarget - tool.AimTip.position;
                        Assert.Less(Vector3.Angle(jet.transform.forward, toTarget), 4f, "Le jet est dirigé vers le point visé.");
                    }
                    Assert.Less(tool.AimErrorDegrees, 1.5f, $"{tool.name} : buse alignée sur la cible.");
                    tools.AutomationUseOverride = false;
                    yield return null;
                }
            }
        }

        // ------------------------------------------------------------------ 1.7 / 1.8 Silhouette distante

        [UnityTest]
        public IEnumerator RemoteAvatar_HoldsTheSameDetailedTool_OnTheSameAimPoint_WithHandsOnTheGrip()
        {
            SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
            yield return null;
            yield return null;
            yield return null;
            var session = MultiplayerSession.Find();
            Assert.IsNotNull(session, "Session créée au menu principal.");
            var prefab = session.Content.avatarPrefab;
            yield return Load();
            TestRugs.LoadDefault(game);

            // Silhouette d'un « autre joueur » posée face au tapis, hors réseau (état reçu simulé).
            Vector3 target = game.Rug.UVToWorld(new Vector2(0.5f, 0.45f));
            var feet = new Vector3(target.x, 0.06f, target.z - 1.1f);
            var avatarObject = Object.Instantiate(prefab, feet, Quaternion.identity);
            var avatar = avatarObject.GetComponent<NetworkPlayerAvatar>();
            float eyeY = avatar.View.position.y;
            float pitch = Mathf.Atan2(eyeY - target.y, 1.1f) * Mathf.Rad2Deg;
            game.PlayerController.Teleport(new Vector3(feet.x + 1.6f, 0.06f, feet.z + 0.4f), -70f, 15f);

            var fpTips = new Dictionary<int, Transform>();
            foreach (var tool in game.ToolController.Tools) fpTips[tool.Definition.slot] = tool.AimTip;
            for (int slot = 1; slot <= 8; slot++)
            {
                avatar.SetPreviewState(slot, false, pitch);
                yield return new WaitForSeconds(0.4f);
                var held = avatar.ActiveTool;
                Assert.IsNotNull(held, $"Outil {slot} affiché.");
                Assert.IsTrue(held.root.activeInHierarchy);
                int renderers = held.root.GetComponentsInChildren<MeshRenderer>().Length;
                Assert.GreaterOrEqual(renderers, 4, $"Outil {slot} : modèle détaillé, pas un cylindre provisoire.");
                Assert.IsNotNull(held.root.transform.Find("Glove_R"), "Même modèle que la vue à la première personne (gant compris).");
                Assert.AreEqual(fpTips[slot].name, held.tip.name);
                if (held.head != null)
                    Assert.Less(avatar.TipToTargetMeters, 0.06f, $"Outil {slot} : tête posée sur le point visé par le joueur distant.");

                // La main droite de la silhouette est refermée sur la poignée (gant de l'outil).
                var fore = avatarObject.transform.Find("Visuals/ForeArm_R");
                Vector3 wrist = fore.position + fore.forward * 0.3f;
                Assert.Less(Vector3.Distance(wrist, held.rightHand.TransformPoint(new Vector3(0f, 0f, -0.06f))), 0.03f, $"Outil {slot} : main sur la poignée.");
            }

            // Lance en action : le jet de la silhouette part de la buse de SON outil.
            avatar.SetPreviewState(4, true, pitch);
            yield return new WaitForSeconds(0.3f);
            // Mission de corrections : l'effet est rattaché à la buse de l'outil tenu (origine réelle du jet).
            var liquid = avatar.LiquidEffect;
            Assert.AreEqual(avatar.ActiveTool.tip, liquid.transform.parent, "Jet distant rattaché à la buse.");
            Assert.IsTrue(liquid.isPlaying);
            Assert.Less(Vector3.Distance(liquid.transform.position, avatar.ActiveTool.tip.position), 0.01f, "Jet distant depuis la buse.");

            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                string folder = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ci", "screens");
                Directory.CreateDirectory(folder);
                avatar.SetPreviewState(3, true, pitch);
                yield return new WaitForSeconds(0.5f);
                yield return CaptureScreen(Path.Combine(folder, "62_remote_avatar_brush.png"));
                avatar.SetPreviewState(4, true, pitch);
                yield return new WaitForSeconds(0.5f);
                yield return CaptureScreen(Path.Combine(folder, "63_remote_avatar_rinse.png"));
            }
            Object.Destroy(avatarObject);
        }

        /// <summary>
        /// Capture de la caméra principale et du HUD. En mode batch, WaitForEndOfFrame ne se déclenche jamais : le rendu est
        /// demandé explicitement (même méthode que les autres galeries de captures).
        /// </summary>
        static IEnumerator CaptureScreen(string path)
        {
            var camera = Camera.main;
            Canvas hud = null;
            foreach (var canvas in Object.FindObjectsByType<Canvas>())
                if (canvas.name == "HUD Canvas") hud = canvas;
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = rt;
            if (hud != null)
            {
                hud.renderMode = RenderMode.ScreenSpaceCamera;
                hud.worldCamera = camera;
                hud.planeDistance = 0.2f;
            }
            yield return null;
            Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = rt });
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.Destroy(texture);
            if (hud != null) hud.renderMode = RenderMode.ScreenSpaceOverlay;
            camera.targetTexture = null;
            rt.Release();
            Object.Destroy(rt);
        }

        [UnityTest, Category("Capture")]
        public IEnumerator Capture_ToolReach()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Pas de GPU.");
            TestRugs.LoadDefault(game);
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ci", "screens");
            Directory.CreateDirectory(folder);
            var tools = game.ToolController;
            foreach (var tool in tools.Tools) tool.IsUnlocked = true;
            foreach (int slot in new[] { 1, 3, 4, 7 })
            {
                tools.SelectSlot(slot);
                yield return AimAtRug(new Vector2(0.45f, 0.45f), 10f, 1.3f);
                yield return new WaitForSeconds(0.5f);
                yield return CaptureScreen(Path.Combine(folder, $"64_tool_reach_{slot}.png"));
            }
        }
    }
}
