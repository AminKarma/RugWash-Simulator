using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RugWash.Tests
{
    /// <summary>
    /// V0.3.0 (refonte graphique) : galerie de contrôle visuel (Logs/ci/screens/v30_*, à relire à l'œil) — vues larges de
    /// l'atelier, aire de lavage, tapis sale puis nettoyé de près, HUD, chaque outil tenu en main. Nécessite un GPU.
    /// </summary>
    public class V030CapturePlayModeTests
    {
        GameBootstrap game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            TestSaveSandbox.Reset(legacyTools: true);
            SceneManager.LoadScene("Workshop", LoadSceneMode.Single);
            yield return null;
            yield return null;
            game = Object.FindAnyObjectByType<GameBootstrap>();
            Assert.IsTrue(game != null && game.IsReady);
            yield return null;
        }

        static string Folder()
        {
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ci", "screens");
            Directory.CreateDirectory(folder);
            return folder;
        }

        void Aim(Vector2 uv, float distance)
        {
            Vector3 target = game.Rug.UVToWorld(uv);
            float pitch = Mathf.Atan2(1.68f - target.y, distance) * Mathf.Rad2Deg;
            game.PlayerController.Teleport(new Vector3(target.x, 0.06f, target.z - distance), 0f, pitch);
        }

        [UnityTest, Category("Capture")]
        public IEnumerator Capture_V030_Workshop()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Pas de GPU (mode -nographics) : captures ignorées.");
            string folder = Folder();
            var camera = game.ToolController.GetComponentInChildren<Camera>();
            var canvas = game.Hud.GetComponentInParent<Canvas>();
            game.Rug.LoadRug(game.AvailableRugs[0]);

            var views = new (string name, Vector3 position, float yaw, float pitch)[]
            {
                ("v30_01_entrance", new Vector3(-4.6f, 0.06f, -4.2f), 42f, 6f),
                ("v30_02_washbay", new Vector3(0f, 0.06f, -2.3f), 0f, 22f),
                ("v30_03_left_wall", new Vector3(2.2f, 0.06f, -0.6f), -95f, 4f),
                ("v30_04_back_left", new Vector3(2.8f, 0.06f, -3.2f), -40f, 4f),
                ("v30_05_front_wall", new Vector3(0.5f, 0.06f, 3.6f), 180f, 4f),
                ("v30_06_right_wall", new Vector3(-2.6f, 0.06f, 0.6f), 90f, 4f),
            };
            foreach (var view in views)
            {
                game.PlayerController.Teleport(view.position, view.yaw, view.pitch);
                yield return new WaitForSeconds(0.5f);
                WorkshopPlayModeTests.Save(camera, Path.Combine(folder, view.name + ".png"));
            }

            // Tapis sale de près, puis une bande nettoyée (savon, brosse, rinçage).
            Aim(new Vector2(0.5f, 0.5f), 0.9f);
            yield return new WaitForSeconds(0.4f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "v30_07_rug_dirty_close.png"));
            game.ToolController.AutomationUseOverride = true;
            for (float u = 0.2f; u <= 0.5f; u += 0.07f)
            for (float v = 0.25f; v <= 0.75f; v += 0.12f)
            {
                Aim(new Vector2(u, v), 1.1f);
                foreach (var (slot, seconds) in new[] { (1, 0.25f), (2, 0.25f), (3, 0.45f), (4, 0.4f) })
                {
                    game.ToolController.SelectSlot(slot);
                    yield return new WaitForSeconds(seconds);
                }
                game.ToolController.Equipment.Water.Refill();
                game.ToolController.Equipment.Soap.Refill();
                game.ToolController.Equipment.Recovery.Empty();
            }
            game.ToolController.AutomationUseOverride = false;
            Aim(new Vector2(0.45f, 0.5f), 1.2f);
            yield return new WaitForSeconds(0.5f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "v30_08_rug_half_clean.png"));
            yield return WorkshopPlayModeTests.SaveWithUI(camera, canvas, Path.Combine(folder, "v30_09_hud.png"));

            // Mousse de près.
            Aim(new Vector2(0.7f, 0.5f), 0.9f);
            game.ToolController.SelectSlot(1);
            game.ToolController.AutomationUseOverride = true;
            yield return new WaitForSeconds(0.8f);
            game.ToolController.AutomationUseOverride = false;
            yield return new WaitForSeconds(0.2f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "v30_10_foam_close.png"));
        }

        /// <summary>Fenêtres de la charte UI : ordinateur, commandes, boutique, casier, pause, résultats.</summary>
        [UnityTest, Category("Capture")]
        public IEnumerator Capture_V030_Windows()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Pas de GPU (mode -nographics) : captures ignorées.");
            string folder = Folder();
            var camera = game.ToolController.GetComponentInChildren<Camera>();
            var canvas = game.Hud.GetComponentInParent<Canvas>();
            game.PlayerController.Teleport(new Vector3(0f, 0.06f, -2.3f), 0f, 18f);
            game.Wallet.Wallet.Add(650);
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(game.Computer.Open());
            yield return new WaitForSecondsRealtime(0.15f);
            yield return WorkshopPlayModeTests.SaveWithUI(camera, canvas, Path.Combine(folder, "v30_20_computer.png"));
            game.Computer.Close();
            Assert.IsTrue(game.OrderBoard.Open());
            yield return new WaitForSecondsRealtime(0.15f);
            yield return WorkshopPlayModeTests.SaveWithUI(camera, canvas, Path.Combine(folder, "v30_21_orders.png"));
            game.OrderBoard.Close();
            Assert.IsTrue(game.ShopUI.Open());
            yield return new WaitForSecondsRealtime(0.15f);
            yield return WorkshopPlayModeTests.SaveWithUI(camera, canvas, Path.Combine(folder, "v30_22_shop.png"));
            game.ShopUI.Close();
            Assert.IsTrue(game.Locker.Open());
            yield return new WaitForSecondsRealtime(0.15f);
            yield return WorkshopPlayModeTests.SaveWithUI(camera, canvas, Path.Combine(folder, "v30_23_locker.png"));
            game.Locker.Close();
            game.GameState.Pause();
            yield return new WaitForSecondsRealtime(0.15f);
            yield return WorkshopPlayModeTests.SaveWithUI(camera, canvas, Path.Combine(folder, "v30_24_pause.png"));
            game.GameState.Resume();
            yield return null;
            game.Orders.TryStart(game.Orders.Book.Orders[0]);
            yield return new WaitForSeconds(0.3f);
            GameLoopPlayModeTests.CleanThoroughly(game.Rug.Grid);
            yield return null;
            game.Orders.Deliver();
            yield return new WaitForSecondsRealtime(0.3f);
            yield return WorkshopPlayModeTests.SaveWithUI(camera, canvas, Path.Combine(folder, "v30_25_results.png"));
            game.Results.BackToWorkshop();
            yield return null;
        }

        /// <summary>
        /// Résolutions et formats d'écran (16:9 HD à QHD, 16:10, 5:4, ultra-large 21:9) : HUD avec un tapis, boutique, casier et
        /// pause. Les cartes du HUD restent entièrement à l'écran et ne se chevauchent pas ; captures v30_res_* à relire.
        /// </summary>
        [UnityTest, Category("Capture")]
        public IEnumerator Capture_V030_Resolutions()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Pas de GPU (mode -nographics) : captures ignorées.");
            string folder = Folder();
            var camera = game.ToolController.GetComponentInChildren<Camera>();
            var canvas = game.Hud.GetComponentInParent<Canvas>();
            var hud = game.Hud.transform;
            game.Rug.LoadRug(game.AvailableRugs[0]);
            game.Wallet.Wallet.Add(650);
            string[] cards = { "CleanlinessPanel", "EconomyPanel", "ToolPanel", "ToolBar", "ScanPanel" };
            var sizes = new (int w, int h, string name)[]
            {
                (1280, 720, "720p"), (1920, 1080, "1080p"), (2560, 1440, "1440p"), (1680, 1050, "16x10"), (1280, 1024, "5x4"), (3440, 1440, "21x9"),
            };
            Aim(new Vector2(0.5f, 0.45f), 1.25f);
            yield return new WaitForSeconds(0.5f);
            foreach (var size in sizes)
            {
                void CheckHud(RenderTexture rt)
                {
                    var rects = new List<Rect>();
                    foreach (string name in cards)
                    {
                        var card = (RectTransform)hud.Find(name);
                        Assert.IsNotNull(card, name);
                        if (!card.gameObject.activeInHierarchy) { rects.Add(Rect.zero); continue; }
                        var corners = new Vector3[4];
                        card.GetWorldCorners(corners);
                        Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
                        Vector2 b = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                        var r = Rect.MinMaxRect(a.x, a.y, b.x, b.y);
                        Assert.That(r.xMin >= -1f && r.yMin >= -1f && r.xMax <= rt.width + 1f && r.yMax <= rt.height + 1f,
                            $"{size.name} : {name} sort de l'écran ({r}).");
                        rects.Add(r);
                    }
                    for (int i = 0; i < rects.Count; i++)
                    for (int j = i + 1; j < rects.Count; j++)
                        if (rects[i].width > 0f && rects[j].width > 0f)
                            Assert.IsFalse(rects[i].Overlaps(rects[j]), $"{size.name} : {cards[i]} chevauche {cards[j]}.");
                }
                yield return WorkshopPlayModeTests.SaveWithUI(camera, canvas, Path.Combine(folder, $"v30_res_{size.name}_hud.png"), size.w, size.h, CheckHud);
                Assert.IsTrue(game.ShopUI.Open());
                yield return new WaitForSecondsRealtime(0.15f);
                yield return WorkshopPlayModeTests.SaveWithUI(camera, canvas, Path.Combine(folder, $"v30_res_{size.name}_shop.png"), size.w, size.h);
                game.ShopUI.Close();
                Assert.IsTrue(game.Locker.Open());
                yield return new WaitForSecondsRealtime(0.15f);
                yield return WorkshopPlayModeTests.SaveWithUI(camera, canvas, Path.Combine(folder, $"v30_res_{size.name}_locker.png"), size.w, size.h);
                game.Locker.Close();
                game.GameState.Pause();
                yield return new WaitForSecondsRealtime(0.15f);
                yield return WorkshopPlayModeTests.SaveWithUI(camera, canvas, Path.Combine(folder, $"v30_res_{size.name}_pause.png"), size.w, size.h);
                game.GameState.Resume();
                yield return null;
            }
        }

        /// <summary>Menu principal (charte UI, décor) et fenêtre Personnage (silhouette).</summary>
        [UnityTest, Category("Capture")]
        public IEnumerator Capture_V030_Menu()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Pas de GPU (mode -nographics) : captures ignorées.");
            string folder = Folder();
            SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
            yield return null;
            yield return new WaitForSeconds(1f);
            Canvas menuCanvas = null;
            foreach (var c in Object.FindObjectsByType<Canvas>())
                if (c.name == "Menu Canvas") menuCanvas = c;
            yield return WorkshopPlayModeTests.SaveWithUI(Camera.main, menuCanvas, Path.Combine(folder, "v30_30_main_menu.png"));
            var menu = Object.FindAnyObjectByType<RugWash.UI.MainMenuController>();
            menu.Character.Open();
            yield return new WaitForSecondsRealtime(0.4f);
            yield return WorkshopPlayModeTests.SaveWithUI(Camera.main, menuCanvas, Path.Combine(folder, "v30_31_character.png"));
            menu.Character.Close();
            menu.Settings.Open();
            yield return new WaitForSecondsRealtime(0.2f);
            yield return WorkshopPlayModeTests.SaveWithUI(Camera.main, menuCanvas, Path.Combine(folder, "v30_32_settings.png"));
        }

        [UnityTest, Category("Capture")]
        public IEnumerator Capture_V030_Tools()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Pas de GPU (mode -nographics) : captures ignorées.");
            string folder = Folder();
            var camera = game.ToolController.GetComponentInChildren<Camera>();
            var tools = game.ToolController;
            Player.PlayerInventory.AutomationCarriesOnSelect = true;
            var owned = new List<string>(game.Upgrades.OwnedIds)
            {
                "up_tool_vacuum", "up_tool_rinse", "up_tool_extractor", "up_tool_foam", "up_tool_broom", "up_tool_squeegee",
                "up_tool_rotary", "up_detail_brush",
            };
            game.Upgrades.Restore(owned);
            game.Rug.LoadRug(game.AvailableRugs[0]);
            yield return null;
            for (int i = 0; i < tools.Tools.Count; i++)
            {
                var tool = tools.Tools[i];
                tools.Inventory.CarryForAutomation(tool);
                tools.SelectIndex(i);
                Aim(new Vector2(0.5f, 0.45f), 1.25f);
                yield return new WaitForSeconds(0.6f);
                WorkshopPlayModeTests.Save(camera, Path.Combine(folder, $"v30_tool_{i:00}_{tool.Definition.id}.png"));
                // Au repos (rien de visé à portée) : l'outil tenu en main, de près.
                game.PlayerController.Teleport(new Vector3(0f, 0.06f, 3.6f), 180f, -4f);
                yield return new WaitForSeconds(0.5f);
                WorkshopPlayModeTests.Save(camera, Path.Combine(folder, $"v30_toolrest_{i:00}_{tool.Definition.id}.png"));
            }
            Player.PlayerInventory.AutomationCarriesOnSelect = false;
        }

        /// <summary>
        /// Ouvrier modélisé dans Blender (MCP), à squelette, tel que les autres joueurs le voient : salopette et combinaison, de
        /// face et de dos, en marche, outil en main, à terre (ragdoll). Vérifie aussi que les os suivent la silhouette du jeu.
        /// </summary>
        [UnityTest, Category("Capture")]
        public IEnumerator Capture_V030_Avatar()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Pas de GPU (mode -nographics) : captures ignorées.");
            string folder = Folder();
            var camera = game.ToolController.GetComponentInChildren<Camera>();
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RugWash/Prefabs/Network/PlayerAvatar.prefab");
#else
            GameObject prefab = null;
#endif
            Assert.IsNotNull(prefab, "Préfabriqué de l'avatar réseau.");
            var feet = new Vector3(1.2f, 0.06f, 1.4f);
            var avatarObject = Object.Instantiate(prefab, feet, Quaternion.identity);
            var avatar = avatarObject.GetComponent<Network.NetworkPlayerAvatar>();
            var appearance = avatarObject.GetComponentInChildren<Player.CharacterAppearance>(true);
            var skeletons = avatarObject.GetComponentsInChildren<Player.AvatarSkeleton>(true);
            Assert.AreEqual(2, skeletons.Length, "Deux ouvriers à squelette (salopette, combinaison).");
            avatar.SetPreviewState(0, false, 0f);
            void Look(float distance, float yaw, float height = 0.9f)
            {
                var forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                var eye = feet - forward * distance;
                float pitch = Mathf.Atan2(eye.y + 1.62f - (feet.y + height), distance) * Mathf.Rad2Deg;
                game.PlayerController.Teleport(eye, yaw, pitch);
            }
            for (int style = 0; style < 2; style++)
            {
                appearance.Apply(new Player.CharacterLook { outfit = style, cap = style == 0 ? 1 : 2, gloves = style });
                Assert.AreEqual(style, appearance.Style);
                Assert.IsTrue(skeletons[style].gameObject.activeInHierarchy && !skeletons[1 - style].gameObject.activeInHierarchy, "Modèle de la tenue choisie.");
                avatar.SetPreviewState(0, false, 0f);
                Look(2.6f, 180f);
                yield return new WaitForSeconds(0.4f);
                WorkshopPlayModeTests.Save(camera, Path.Combine(folder, $"v30_avatar_{style}_front.png"));
                Look(1.2f, 180f, 1.55f);
                yield return new WaitForSeconds(0.2f);
                WorkshopPlayModeTests.Save(camera, Path.Combine(folder, $"v30_avatar_{style}_face.png"));
                Look(2.6f, 0f);
                yield return new WaitForSeconds(0.2f);
                WorkshopPlayModeTests.Save(camera, Path.Combine(folder, $"v30_avatar_{style}_back.png"));
                // Marche : la silhouette avance, jambes balancées et genoux fléchis.
                Look(2.8f, 250f);
                for (float t = 0f; t < 0.7f; t += Time.deltaTime)
                {
                    avatarObject.transform.position += Vector3.right * (2.2f * Time.deltaTime);
                    yield return null;
                }
                WorkshopPlayModeTests.Save(camera, Path.Combine(folder, $"v30_avatar_{style}_walk.png"));
                avatarObject.transform.position = feet;
                // Outil en main (lance de rinçage), visée vers le bas : bras refermés sur l'outil.
                avatar.SetPreviewState(4, true, 30f);
                Look(2.2f, 210f, 1.1f);
                yield return new WaitForSeconds(0.5f);
                Assert.Less(Vector3.Distance(avatar.RightWrist, avatar.ActiveTool.rightHand.TransformPoint(new Vector3(0f, 0f, -0.06f))), 0.03f, "Main sur la poignée.");
                Assert.Greater(skeletons[style].FingerCount, 0, "Doigts articulés.");
                WorkshopPlayModeTests.Save(camera, Path.Combine(folder, $"v30_avatar_{style}_tool.png"));
                avatar.SetPreviewState(0, false, 0f);
            }
            // À terre (ragdoll), puis KO cartoon du fusil : la tête disparaît et revient.
            avatar.PreviewKnockdown(true, new Vector3(0f, 1.5f, -3f));
            Look(3.2f, 180f, 0.3f);
            yield return new WaitForSeconds(1.2f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "v30_avatar_ragdoll.png"));
            avatar.PreviewKnockdown(false, Vector3.zero);
            yield return new WaitForSeconds(0.8f);
            avatar.PreviewKnockdown(true, Vector3.zero, Player.KnockCause.Blaster);
            yield return null;
            Assert.IsTrue(avatar.HeadPopped);
            Assert.Less(skeletons[1].HeadBone.localScale.x, 0.01f, "Tête masquée (os de la tête).");
            avatar.PreviewKnockdown(false, Vector3.zero);
            yield return null;
            Assert.IsFalse(avatar.HeadPopped);
            Object.Destroy(avatarObject);
        }

        /// <summary>
        /// Lisibilité des tapis sous différents éclairages et angles : vue plongeante, angle rasant (velours et relief), contre-jour
        /// depuis l'autre côté de l'aire, et tapis de la zone 2 sous l'éclairage de l'annexe. Captures v30_light_* à relire.
        /// </summary>
        [UnityTest, Category("Capture")]
        public IEnumerator Capture_V030_RugLighting()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Pas de GPU (mode -nographics) : captures ignorées.");
            string folder = Folder();
            var camera = game.ToolController.GetComponentInChildren<Camera>();
            game.Rug.LoadRug(game.AvailableRugs[0]);
            yield return null;
            Vector3 center = game.Rug.UVToWorld(new Vector2(0.5f, 0.5f));
            var views = new (string name, Vector3 offset, float yaw, float pitch)[]
            {
                ("v30_light_top", new Vector3(0f, 0f, -1.3f), 0f, 48f),
                ("v30_light_grazing", new Vector3(-2.6f, 0f, -0.4f), 80f, 14f),
                ("v30_light_backlit", new Vector3(0f, 0f, 2.6f), 180f, 24f),
            };
            foreach (var view in views)
            {
                game.PlayerController.Teleport(new Vector3(center.x, 0.06f, center.z) + view.offset, view.yaw, view.pitch);
                yield return new WaitForSeconds(0.35f);
                WorkshopPlayModeTests.Save(camera, Path.Combine(folder, view.name + ".png"));
            }
            // Zone 2 (annexe) : même tapis sous l'éclairage de l'annexe.
            Object.FindAnyObjectByType<Workshop.WorkshopExtension>().Apply(true, false, false);
            yield return null;
            // Tapis de la zone 2 (et non une surface nettoyable de l'aire de lavage, aussi pilotée par un RugCleaningController).
            var rug2 = game.Orders.BayCount > 1 ? game.Orders.RugOf(1) : null;
            Assert.IsNotNull(rug2, "Tapis de la zone 2.");
            if (rug2 != null)
            {
                for (int i = 0; i < Mathf.Min(2, game.AvailableRugs.Count); i++)
                {
                    rug2.LoadRug(game.AvailableRugs[i]);
                    yield return null;
                    Vector3 c2 = rug2.UVToWorld(new Vector2(0.5f, 0.5f));
                    game.PlayerController.Teleport(new Vector3(c2.x, 0.06f, c2.z - 1.5f), 0f, 40f);
                    yield return new WaitForSeconds(0.35f);
                    WorkshopPlayModeTests.Save(camera, Path.Combine(folder, $"v30_light_zone2_rug{i}.png"));
                }
                // Même second tapis dans la zone 1 (comparaison : tapis ou éclairage ?).
                game.Rug.LoadRug(game.AvailableRugs[1 % game.AvailableRugs.Count]);
                yield return null;
                game.PlayerController.Teleport(new Vector3(center.x, 0.06f, center.z - 1.5f), 0f, 40f);
                yield return new WaitForSeconds(0.35f);
                WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "v30_light_zone1_rug1.png"));
                Debug.Log($"[V030] Tapis : {string.Join(", ", System.Linq.Enumerable.Select(game.AvailableRugs, r => r.displayName))}");
            }
        }

        /// <summary>
        /// Décor de l'annexe (modèles Blender) : aucun objet ne déborde sur l'aire de lavage 2, ni devant l'ouverture vers
        /// l'atelier, ni devant la machine à laver ; tous restent dans l'annexe.
        /// </summary>
        [UnityTest]
        public IEnumerator AnnexDecor_KeepsTheWashBayPassageAndWasherFree()
        {
            Object.FindAnyObjectByType<Workshop.WorkshopExtension>().Apply(true, true, false);
            yield return null;
            var decor = GameObject.Find("AnnexDecor");
            Assert.IsNotNull(decor, "Décor de l'annexe présent.");
            var bayRenderer = GameObject.Find("WashBay_2").GetComponentInChildren<Renderer>();
            Bounds bay = bayRenderer.bounds;
            foreach (var r in GameObject.Find("WashBay_2").GetComponentsInChildren<Renderer>()) if (r.bounds.size.y < 0.3f) bay.Encapsulate(r.bounds);
            var washer = GameObject.Find("Machine_Washer").transform.position;
            foreach (Transform item in decor.transform)
            {
                var renderers = item.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                var b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                // Au sol : pas sur l'aire de lavage 2 (marge de 5 cm).
                if (b.min.y < 0.5f)
                {
                    bool overBay = b.max.x > bay.min.x + 0.05f && b.min.x < bay.max.x - 0.05f && b.max.z > bay.min.z + 0.05f && b.min.z < bay.max.z - 0.05f;
                    Assert.IsFalse(overBay, $"{item.name} empiète sur l'aire de lavage 2 ({b} / {bay}).");
                }
                // Pas devant l'ouverture de l'atelier (x < 7,2, z entre -2,15 et -0,75), ni devant la machine (1 m devant sa façade).
                Assert.IsFalse(b.min.x < 7.2f && b.max.z > -2.15f && b.min.z < -0.75f, $"{item.name} gêne le passage depuis l'atelier.");
                Assert.IsFalse(Mathf.Abs(b.center.x - washer.x) < 0.45f && b.min.z < washer.z - 0.45f && b.max.z > washer.z - 1.45f,
                    $"{item.name} gêne l'accès à la machine à laver.");
                Assert.That(b.min.x >= 6.2f && b.max.x <= 12.42f && b.min.z >= -4.42f && b.max.z <= 2.02f, $"{item.name} sort de l'annexe ({b}).");
            }
        }

        /// <summary>Modèles Blender (MCP) intégrés à l'atelier : casier (fermé, ouvert), caisse à outils, machine à laver de l'annexe.</summary>
        [UnityTest, Category("Capture")]
        public IEnumerator Capture_V030_BlenderModels()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Pas de GPU (mode -nographics) : captures ignorées.");
            string folder = Folder();
            var camera = game.ToolController.GetComponentInChildren<Camera>();
            var locker = Object.FindAnyObjectByType<Workshop.Locker>();
            var hinge = locker.transform.Find("Locker_DoorHinge");
            Assert.IsNotNull(hinge, "Casier FBX : porte à charnière.");
            game.PlayerController.Teleport(new Vector3(1.85f, 0.06f, 2.0f), 0f, 10f);
            yield return new WaitForSeconds(0.4f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "v30_fbx_ingame_locker.png"));
            locker.enabled = false;
            hinge.localRotation = Quaternion.Euler(0f, -100f, 0f);
            yield return null;
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "v30_fbx_ingame_locker_open.png"));
            locker.enabled = true;
            game.PlayerController.Teleport(new Vector3(3.6f, 0.06f, 3.0f), 0f, 22f);
            yield return new WaitForSeconds(0.4f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "v30_fbx_ingame_toolstorage.png"));
            // Affiche des cinq étapes conçue dans Canva (mur du fond).
            Assert.IsNotNull(GameObject.Find("Poster_Steps/Paper"), "Affiche des étapes.");
            game.PlayerController.Teleport(new Vector3(-1.75f, 0.06f, 2.9f), 0f, -6f);
            yield return new WaitForSeconds(0.4f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "v30_canva_poster.png"));
            Object.FindAnyObjectByType<Workshop.WorkshopExtension>().Apply(true, true, false);
            game.PlayerController.Teleport(new Vector3(10.9f, 0.06f, -0.3f), 0f, 18f);
            yield return new WaitForSeconds(0.6f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "v30_fbx_ingame_washer.png"));
            // Décor Blender de l'annexe : sèche-linge, aspirateur à eau, établi garni, panneau d'outils, bidons et pompe.
            Assert.IsNotNull(GameObject.Find("AnnexDecor/Dryer"), "Sèche-linge.");
            Assert.IsNotNull(GameObject.Find("AnnexDecor/Workbench"), "Établi.");
            game.PlayerController.Teleport(new Vector3(8.6f, 0.06f, -0.6f), 25f, 12f);
            yield return new WaitForSeconds(0.4f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "v30_fbx_ingame_annex_back.png"));
            game.PlayerController.Teleport(new Vector3(10.4f, 0.06f, -1.6f), 160f, 22f);
            yield return new WaitForSeconds(0.4f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "v30_fbx_ingame_workbench.png"));
            game.PlayerController.Teleport(new Vector3(8.2f, 0.06f, -1.8f), 205f, 14f);
            yield return new WaitForSeconds(0.4f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "v30_fbx_ingame_jerrycans.png"));
            // Enseigne au-dessus de l'entrée, tapis mouillé au séchage.
            Assert.IsNotNull(GameObject.Find("Sign_Brand"), "Enseigne RugWash.");
            game.PlayerController.Teleport(new Vector3(-3.2f, 0.06f, -1.6f), 180f, -12f);
            yield return new WaitForSeconds(0.4f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "v30_fbx_ingame_brand_sign.png"));
            Assert.IsNotNull(GameObject.Find("Hanging_Wet"), "Tapis mouillé au séchage (état « mouillé »).");
            game.PlayerController.Teleport(new Vector3(2.6f, 0.06f, 0.2f), 90f, 2f);
            yield return new WaitForSeconds(0.4f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "v30_fbx_ingame_drying_wet.png"));
        }
    }
}
