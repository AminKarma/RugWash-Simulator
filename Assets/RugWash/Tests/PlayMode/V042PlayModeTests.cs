using System.Collections;
using System.IO;
using NUnit.Framework;
using RugWash.Core;
using RugWash.Network;
using RugWash.Player;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RugWash.Tests
{
    /// <summary>
    /// V0.4.2, dans la vraie scène : barres de réserve et de propreté du HUD (remplissage visible, égal à la valeur réelle),
    /// glissade sur la mousse vue par les autres (chute cartoon qui reste avec la silhouette, puis relevé). Le multijoueur
    /// réel (voix, objets au sol, fusil) est vérifié par l'étape « multiplayer » de RugWash-CI.
    /// </summary>
    public class V042PlayModeTests
    {
        GameBootstrap game;

        IEnumerator LoadWorkshop()
        {
            TestSaveSandbox.Reset(legacyTools: true);
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
            PlayerInventory.AutomationCarriesOnSelect = false;
            Time.timeScale = 1f;
            yield return null;
        }

        /// <summary>
        /// Avant : marge fixe de 3 px dans des barres de 5 à 7 px, remplissage de 0 à 1 px de haut (invisible) ; seule la
        /// quantité en texte restait. Les barres montrent maintenant la réserve réelle, à chaque image, bornée entre 0 et 1.
        /// </summary>
        [UnityTest]
        public IEnumerator HudBars_FillIsVisible_AndMatchesTheRealReserve()
        {
            yield return LoadWorkshop();
            // Chaque outil est pris en main pour le test (sinon seuls les outils portés sont sélectionnables).
            PlayerInventory.AutomationCarriesOnSelect = true;
            Assert.GreaterOrEqual(game.Hud.CleanlinessBarPixelHeight, 2f, "Barre de propreté : remplissage visible.");
            int checkedTools = 0;
            foreach (var tool in game.ToolController.Tools)
            {
                if (tool == null || !tool.IsUnlocked) continue;
                game.ToolController.SelectSlot(tool.Definition.slot);
                yield return null;
                yield return null;
                float fraction = tool.GetResourceFraction();
                if (fraction < 0f) continue;
                Assert.AreSame(tool, game.ToolController.ActiveTool, $"{tool.Definition.displayName} en main.");
                checkedTools++;
                Assert.IsTrue(game.Hud.ResourceBarShown, $"{tool.Definition.displayName} : barre de réserve affichée.");
                Assert.GreaterOrEqual(game.Hud.ResourceBarPixelHeight, 2f, $"{tool.Definition.displayName} : remplissage visible.");
                Assert.AreEqual(Mathf.Clamp01(fraction), game.Hud.ResourceBarFill, 0.02f, $"{tool.Definition.displayName} : barre = réserve réelle.");
            }
            Assert.Greater(checkedTools, 0, "Au moins un outil à réserve.");

            // Pendant l'utilisation : la barre suit la réserve à l'image près (plus de lissage en retard sur le texte).
            var rinse = game.ToolController.Tools[3];
            game.ToolController.SelectSlot(rinse.Definition.slot);
            Vector3 target = game.Rug.UVToWorld(new Vector2(0.5f, 0.5f));
            game.PlayerController.Teleport(new Vector3(target.x, 0.06f, target.z - 1.1f), 0f, 50f);
            yield return null;
            game.ToolController.AutomationUseOverride = true;
            float start = rinse.GetResourceFraction(), worst = 0f;
            for (int i = 0; i < 90; i++)
            {
                yield return null;
                worst = Mathf.Max(worst, Mathf.Abs(Mathf.Clamp01(rinse.GetResourceFraction()) - game.Hud.ResourceBarFill));
            }
            game.ToolController.AutomationUseOverride = false;
            Assert.Less(rinse.GetResourceFraction(), start, "La lance a consommé de l'eau.");
            Assert.Less(worst, 0.02f, "Barre et réserve identiques pendant l'utilisation.");
            Assert.That(game.Hud.ResourceBarFill, Is.InRange(0f, 1f));
        }

        /// <summary>
        /// Glissade sur la mousse vue par un autre joueur : chute cartoon animée (pas de ragdoll physique qui s'arrête sur
        /// place), le corps reste avec la silhouette qui continue de glisser, sans saut ; au relevé, retour à la pose.
        /// Capture v42_slip_*.png à relire.
        /// </summary>
        [UnityTest]
        public IEnumerator RemoteSlip_CartoonFallFollowsTheSlidingAvatar_ThenStandsBackUp()
        {
            SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var prefab = MultiplayerSession.Find().Content.avatarPrefab;
            yield return LoadWorkshop();
            var avatar = Object.Instantiate(prefab, new Vector3(-3.5f, 0f, -2.5f), Quaternion.identity).GetComponent<NetworkPlayerAvatar>();
            avatar.SetPreviewState(3, false, 0f);
            yield return null;
            var camera = game.ToolController.GetComponentInChildren<Camera>();
            game.PlayerController.Teleport(new Vector3(-1.2f, 0.06f, -2.5f), -90f, 12f);

            avatar.PreviewKnockdown(true, new Vector3(-6f, 2f, 0f), KnockCause.Slip);
            var ragdoll = avatar.Ragdoll;
            Assert.IsTrue(ragdoll.IsSliding, "Glissade : chute cartoon animée.");
            Assert.IsNull(ragdoll.BodyRigidbody, "Pas de ragdoll physique pendant une glissade.");
            Vector3 lastHip = ragdoll.HipPosition, lastRoot = avatar.transform.position;
            float maxSpeed = 0f, maxOffset = 0f;
            for (float t = 0f; t < 1.2f; t += Time.deltaTime)
            {
                // La silhouette continue de glisser (position reçue du joueur) : le corps la suit.
                avatar.transform.position += Vector3.left * 2.5f * Time.deltaTime;
                yield return null;
                // Vitesse des hanches par rapport à la silhouette (la glissade elle-même exclue) : une chute de 0,3 s, jamais un saut.
                Vector3 relative = (ragdoll.HipPosition - lastHip) - (avatar.transform.position - lastRoot);
                maxSpeed = Mathf.Max(maxSpeed, relative.magnitude / Mathf.Max(Time.deltaTime, 1e-3f));
                maxOffset = Mathf.Max(maxOffset, ragdoll.BodyOffsetFromRoot);
                lastHip = ragdoll.HipPosition;
                lastRoot = avatar.transform.position;
                if (Mathf.Abs(t - 0.6f) < Time.deltaTime * 0.5f && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                    WorkshopPlayModeTests.Save(camera, Path.Combine(Folder(), "v42_slip_down.png"));
            }
            Assert.Less(maxOffset, 1.6f, "Corps resté avec la silhouette.");
            Assert.Less(maxSpeed, 8f, "Aucun saut d'une image à l'autre (vitesse des hanches bornée).");
            Vector3 hips = ragdoll.transform.position;
            Assert.Less(Vector3.Dot(ragdoll.transform.up, Vector3.up), 0.4f, "Sur le dos (corps basculé).");
            Assert.Greater(hips.y, -0.3f, "Jamais sous le sol.");

            avatar.PreviewKnockdown(false, Vector3.zero);
            Assert.IsFalse(ragdoll.IsSliding);
            yield return new WaitForSeconds(0.8f);
            Assert.IsFalse(ragdoll.IsBusy, "Relevé terminé.");
            Assert.Less(ragdoll.BodyOffsetFromRoot, 0.02f, "Corps revenu à sa pose, à la place de la silhouette.");
            Assert.Greater(Vector3.Dot(ragdoll.transform.up, Vector3.up), 0.99f, "Debout.");
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) WorkshopPlayModeTests.Save(camera, Path.Combine(Folder(), "v42_slip_up.png"));

            // Les autres chutes gardent le ragdoll physique.
            avatar.PreviewKnockdown(true, new Vector3(0f, 3f, 9f), KnockCause.Bat);
            Assert.IsFalse(ragdoll.IsSliding);
            Assert.IsNotNull(ragdoll.BodyRigidbody, "Coup de batte : ragdoll physique inchangé.");
            avatar.PreviewKnockdown(false, Vector3.zero);
            yield return new WaitForSeconds(0.7f);
            Object.Destroy(avatar.gameObject);
        }

        /// <summary>
        /// Jalon 7 : l'onglet « Améliorations » montre une carte par outil (jamais deux cartes pour le même outil) ; le bouton
        /// achète la prochaine amélioration de l'outil, la carte avance ; les nouvelles améliorations ont un effet réel.
        /// </summary>
        [UnityTest]
        public IEnumerator UpgradeCards_OnePerTool_BuyAdvancesTheCard_AndExtrasAreReal()
        {
            yield return LoadWorkshop();
            game.Wallet.Wallet.Add(20000);
            game.Reputation.Add(60);
            Assert.IsTrue(game.ShopUI.Open());
            game.ShopUI.ShowTab(RugWash.UI.ShopUI.Tab.Upgrades);
            yield return null;
            var vacuumCard = game.ShopUI.RowOf(game.Upgrades.Find("up_vacuum"));
            Assert.IsNotNull(vacuumCard);
            Assert.AreSame(vacuumCard, game.ShopUI.RowOf(game.Upgrades.Find("up_vacuum_lvl2")), "Niveaux sur la même carte.");
            Assert.AreSame(vacuumCard, game.ShopUI.RowOf(game.Upgrades.Find("up_vacuum_fiber")), "Accessoire sur la même carte.");
            Assert.AreSame(vacuumCard, game.ShopUI.RowOf(game.Upgrades.Find("up_vacuum_wide")), "Nouvelle amélioration sur la même carte.");
            Assert.AreSame(game.ShopUI.RowOf(game.Upgrades.Find("up_water_1")), game.ShopUI.RowOf(game.Upgrades.Find("up_rinse")),
                "Réservoir d'eau sur la carte de la lance.");
            int cards = 0;
            foreach (var row in game.ShopUI.Rows) if (row.gameObject.activeSelf) cards++;
            Assert.Less(cards, game.ShopUI.ShownItemCount, "Les améliorations sont regroupées.");

            var first = game.ShopUI.ItemOf(vacuumCard);
            Assert.IsFalse(game.Upgrades.IsOwned(first));
            vacuumCard.ActionButton.onClick.Invoke();
            yield return null;
            Assert.IsTrue(game.Upgrades.IsOwned(first), "Le bouton de la carte achète la prochaine amélioration.");
            var next = game.ShopUI.ItemOf(game.ShopUI.RowOf(first));
            Assert.AreNotSame(first, next, "La carte passe à l'amélioration suivante.");
            StringAssert.Contains(" 1/", string.Join("|", Texts(vacuumCard)), "Progression affichée sur la carte.");

            // Nouvelles améliorations : effet réel sur l'outil.
            var squeegee = FindTool("squeegee");
            game.Upgrades.Grant("up_tool_squeegee");
            float radius = squeegee.Radius;
            game.Upgrades.Grant("up_squeegee_wide");
            Assert.AreEqual(radius * 1.3f, squeegee.Radius, 1e-4f, "Raclette large : bande 30 % plus large.");
            var detail = FindTool("detail_brush");
            float power = detail.EffectivePower;
            game.Upgrades.Grant("up_detail_brass");
            Assert.AreEqual(power * 1.35f, detail.EffectivePower, 1e-4f, "Poils en laiton : 35 % plus puissante.");
            var bucket = FindTool("bucket");
            power = bucket.EffectivePower;
            game.Upgrades.Grant("up_bucket_spout");
            Assert.AreEqual(power * 1.3f, bucket.EffectivePower, 1e-4f);
            game.ShopUI.Close();
        }

        /// <summary>Jalon 8 : les bottes sont dans l'onglet « Outils », avec l'icône photographiée du modèle 3D.</summary>
        [UnityTest]
        public IEnumerator Boots_AreInTheToolsTab_WithTheirModelIcon()
        {
            yield return LoadWorkshop();
            yield return new WaitForSeconds(0.2f);
            Assert.IsTrue(game.ShopUI.Open());
            game.ShopUI.ShowTab(RugWash.UI.ShopUI.Tab.Equipment);
            yield return null;
            var boots = game.Upgrades.Find("up_boots");
            var row = game.ShopUI.RowOf(boots);
            Assert.IsNotNull(row, "Bottes dans l'onglet Outils.");
            Assert.IsTrue(row.HasIcon, "Icône des bottes (modèle 3D).");
            game.ShopUI.ShowTab(RugWash.UI.ShopUI.Tab.Upgrades);
            yield return null;
            Assert.IsNull(game.ShopUI.RowOf(boots), "Plus dans l'onglet Améliorations.");
            game.ShopUI.Close();
        }

        /// <summary>Jalon 9 : nouvelle partie, la brosse de détail est dans le kit de départ (portée, sans achat).</summary>
        [UnityTest]
        public IEnumerator NewGame_DetailBrushIsInTheStartingKit()
        {
            TestSaveSandbox.Reset(legacyTools: false);
            SceneManager.LoadScene("Workshop", LoadSceneMode.Single);
            yield return null;
            yield return null;
            game = Object.FindAnyObjectByType<GameBootstrap>();
            yield return null;
            var detail = FindTool("detail_brush");
            Assert.IsTrue(detail.IsUnlocked);
            CollectionAssert.Contains(game.ToolController.Inventory.Carried, "detail_brush", "Portée dès le départ.");
            Assert.AreEqual(0, game.Upgrades.OwnedIds.Count, "Aucun achat nécessaire.");
            // Au lieu de la brosse standard : elle s'achète désormais (onglet Outils).
            var brush = FindTool("brush");
            Assert.IsFalse(brush.IsUnlocked, "Brosse standard plus dans le kit de départ.");
            CollectionAssert.DoesNotContain(game.ToolController.Inventory.Carried, "brush");
            var unlock = game.Upgrades.Find("up_tool_brush");
            Assert.IsNotNull(unlock, "Brosse standard en vente.");
            game.Wallet.Wallet.Add(unlock.price);
            Assert.AreEqual(Economy.PurchaseResult.Purchased, game.Shop.TryPurchase(unlock));
            Assert.IsTrue(brush.IsUnlocked, "Achetée : utilisable.");
        }

        /// <summary>
        /// Jalon 9 : 5e emplacement. Verrouillé (prix affiché) tant qu'il n'est pas acheté ; acheté, il porte réellement un 5e
        /// objet (touche 5), gardé par la sauvegarde.
        /// </summary>
        [UnityTest]
        public IEnumerator FifthSlot_LockedWithPrice_ThenBought_CarriesFive_AndIsSaved()
        {
            yield return LoadWorkshop();
            PlayerInventory.AutomationCarriesOnSelect = false;
            var inventory = game.ToolController.Inventory;
            var toolbar = Object.FindAnyObjectByType<RugWash.UI.ToolSelectionUI>();
            Assert.AreEqual(4, inventory.SlotCount);
            var slot = inventory.SlotUpgrade;
            Assert.IsNotNull(slot, "Article « 5e emplacement » en boutique.");
            yield return null;
            StringAssert.Contains("verrouillé", toolbar.SlotText(4));
            StringAssert.Contains(Economy.WalletManager.Format(slot.price), toolbar.SlotText(4), "Prix affiché sur l'emplacement.");
            while (inventory.Carried.Count < 4)
            {
                var stored = inventory.Stored();
                Assert.Greater(stored.Count, 0);
                inventory.Carry(stored[0]);
            }
            string extra = inventory.Stored()[0];
            Assert.AreEqual(InventoryResult.Full, inventory.Carry(extra), "4 emplacements : plein.");

            game.Wallet.Wallet.Add(slot.price);
            game.Reputation.Add(slot.requiredReputation);
            Assert.AreEqual(Economy.PurchaseResult.Purchased, game.Shop.TryPurchase(slot));
            yield return null;
            Assert.AreEqual(5, inventory.SlotCount);
            Assert.AreEqual(InventoryResult.Done, inventory.Carry(extra), "5e objet porté.");
            Assert.AreEqual(5, inventory.Carried.Count);
            yield return null;
            StringAssert.DoesNotContain("verrouillé", toolbar.SlotText(4));
            Assert.IsTrue(game.SaveSystem.SaveNow("test 5e emplacement"));

            SceneManager.LoadScene("Workshop", LoadSceneMode.Single);
            yield return null;
            yield return null;
            game = Object.FindAnyObjectByType<GameBootstrap>();
            yield return null;
            Assert.AreEqual(5, game.ToolController.Inventory.SlotCount, "Emplacement acheté gardé.");
            Assert.AreEqual(5, game.ToolController.Inventory.Carried.Count, "Les cinq objets portés sont relus.");
            CollectionAssert.Contains(game.ToolController.Inventory.Carried, extra);
        }

        /// <summary>Jalon 9 : la touche « Lâcher » existe (remappable, Q par défaut).</summary>
        [UnityTest]
        public IEnumerator DropKey_IsRebindable()
        {
            yield return LoadWorkshop();
            bool listed = false;
            foreach (var action in RugWash.Save.SettingsManager.RebindableActions) listed |= action.ActionName == "Drop";
            Assert.IsTrue(listed, "Touche « Lâcher l'objet en main » dans les paramètres.");
            Assert.AreEqual("Q", game.Input.Label("Drop").ToUpperInvariant());
        }

        /// <summary>
        /// Jalon 10 : le micro choisi est appliqué à la capture du chat vocal et gardé dans les paramètres ; la musique reprend
        /// quand Windows change de sortie audio (le moteur audio de Unity arrête alors les sources).
        /// </summary>
        [UnityTest]
        public IEnumerator AudioDevices_MicChoiceAppliedAndSaved_MusicResumesAfterOutputChange()
        {
            yield return LoadWorkshop();
            var settings = Object.FindAnyObjectByType<RugWash.Save.SettingsManager>();
            settings.SetVoiceInputDevice("Micro de test");
            yield return null;
            Assert.AreEqual("Micro de test", game.Voice.PreferredDevice, "Choix appliqué à la capture.");
            settings.SetVoiceInputDevice(string.Empty);
            yield return null;
            Assert.AreEqual(string.Empty, game.Voice.PreferredDevice, "Retour au micro par défaut de Windows.");

            var music = RugWash.Audio.MusicPlayer.Instance;
            if (music == null || music.CurrentClip == null) Assert.Ignore("Pas de musique dans cette scène.");
            AudioSource playing = null;
            foreach (var source in music.GetComponents<AudioSource>()) if (source.clip == music.CurrentClip) playing = source;
            if (playing == null || !playing.isPlaying) Assert.Ignore("Audio indisponible (mode batch sans sortie audio).");
            playing.Stop();
            music.ResumeAfterDeviceChange();
            Assert.IsTrue(playing.isPlaying, "La musique reprend après le changement de sortie.");
        }

        /// <summary>
        /// Jalon 11 : robot de la zone 2. Toutes les salissures de son aire (poussière, boue, eau, mousse, savon, taches) baissent
        /// nettement plus que sur l'aire témoin de la zone 1 (même dépôt, sans robot : seule l'évolution naturelle) ; il reste
        /// sur son aire, dans des cases libres ; vitesse mesurée (0,32 m/s avant la V0.4.2).
        /// </summary>
        [UnityTest]
        public IEnumerator Robot_Zone2_CleansEveryDirtTypeOfItsPad_Faster_StaysOnIt()
        {
            yield return LoadWorkshop();
            var robot = game.Robot;
            Assert.IsFalse(robot.ZoneReady, "Sans agrandissement : zone 2 fermée.");
            Assert.AreEqual(RugWash.Workshop.RobotVacuum.ZoneLockedMessage, robot.Toggle());
            game.Upgrades.Grant("up_extension");
            yield return null;
            Assert.IsTrue(robot.ZoneReady);
            var pad = robot.Floor;
            var control = game.WorkSurface.Surface;
            Assert.AreNotSame(control, pad, "Le robot travaille sur l'aire de la zone 2.");
            Soil(pad.Grid);
            Soil(control.Grid);
            var before = Totals(pad.Grid);

            StringAssert.Contains("démarré", robot.Toggle());
            Assert.AreEqual(RugWash.Workshop.RobotMode.Cleaning, robot.Mode);
            Time.timeScale = 4f;
            float moving = 0f, travelled = 0f, elapsed = 0f;
            // Eau de la bande d'eau seule (le robot n'en apporte que sur les taches) : comparée au témoin tant qu'il en reste.
            float waterBefore = BandWater(control.Grid, 2), bestWaterRatio = float.MaxValue;
            Vector3 last = robot.transform.position;
            while (elapsed < 140f)
            {
                yield return null;
                elapsed += Time.deltaTime;
                float step = Vector3.Distance(last, robot.transform.position);
                if (step > 1e-4f && Time.deltaTime > 0f)
                {
                    moving += Time.deltaTime;
                    travelled += step;
                }
                last = robot.transform.position;
                Assert.IsTrue(pad.TryWorldToUV(robot.transform.position, out _), "Le robot reste sur l'aire de la zone 2.");
                var cell = robot.Occupancy.WorldToCell(new Vector2(robot.transform.position.x, robot.transform.position.z));
                Assert.IsFalse(robot.Occupancy.IsBlocked(cell), "Jamais dans un obstacle (bordure, mur, tapis).");
                if (robot.Brain.Battery01 < 0.3f) robot.Brain.Restore((int)robot.Mode, 1f, false);
                float controlWater = BandWater(control.Grid, 2);
                if (controlWater > waterBefore * 0.2f) bestWaterRatio = Mathf.Min(bestWaterRatio, BandWater(pad.Grid, 2) / controlWater);
            }
            Time.timeScale = 1f;
            var robotLeft = Totals(pad.Grid);
            var controlLeft = Totals(control.Grid);
            string[] names = { "poussière", "boue détachée", "eau", "mousse", "savon", "taches" };
            for (int i = 0; i < names.Length; i++)
            {
                TestContext.WriteLine($"{names[i]} : {before[i]:0.0} → robot {robotLeft[i]:0.0} / témoin {controlLeft[i]:0.0}");
                // L'eau sèche aussi d'elle-même sur les deux aires : on compte celle que le robot a réellement ramassée.
                if (i == 2) Assert.Less(bestWaterRatio, 0.8f,
                    $"eau : ramassée par le robot (bande d'eau : au mieux {bestWaterRatio:P0} de celle du témoin ; {robot.WaterCollected:0.0} ramassés).");
                // La mousse retombe aussi en eau d'elle-même : celle que le robot a ramassée.
                else if (i == 3) Assert.Greater(robot.LiquidCollected - robot.WaterCollected, before[3] * 0.1f, "mousse : ramassée par le robot.");
                else Assert.Less(robotLeft[i], controlLeft[i] * 0.8f, $"{names[i]} : réellement traitée par le robot.");
            }
            float speed = moving > 0f ? travelled / moving : 0f;
            TestContext.WriteLine($"Vitesse mesurée : {speed:0.00} m/s (réglage {robot.Speed:0.00} ; avant la V0.4.2 : 0,32 m/s)");
            Assert.Greater(speed, 0.45f, "Plus rapide qu'avant (0,32 m/s).");
        }

        /// <summary>Une bande par salissure prévue sur une aire de lavage : débris, boue, eau, mousse, savon, taches de farce.</summary>
        static void Soil(RugWash.Cleaning.RugCleaningGrid grid)
        {
            // Taches regroupées au centre de l'aire (un tiers de sa largeur) : le robot peut tout visiter pendant le test.
            int band = Mathf.Max(2, grid.Height / 7);
            int x0 = grid.Width / 3, width = grid.Width / 3;
            var deposits = new[]
            {
                new RugWash.Cleaning.DepositOperation { Type = RugWash.Cleaning.DirtType.Dust, Debris = 0.4f },
                new RugWash.Cleaning.DepositOperation { Type = RugWash.Cleaning.DirtType.Mud, Mud = 0.5f },
                new RugWash.Cleaning.DepositOperation { Water = 0.8f },
                new RugWash.Cleaning.DepositOperation { Foam = 0.8f },
                new RugWash.Cleaning.DepositOperation { Residue = 0.6f },
            };
            for (int i = 0; i < deposits.Length; i++)
            {
                var deposit = deposits[i];
                grid.ApplyRect(new RectInt(x0, i * band, width, band), 1f, ref deposit);
            }
            for (int y = deposits.Length * band; y < Mathf.Min(grid.Height, (deposits.Length + 1) * band); y++)
            for (int x = x0; x < x0 + width; x++)
            {
                var cell = grid.GetCell(x, y);
                cell.Stain = 0.5f;
                cell.StainType = RugWash.Cleaning.DirtType.FakeBlood;
                grid.SetCell(x, y, cell);
            }
        }

        /// <summary>Humidité de la bande <paramref name="index"/> déposée par <see cref="Soil"/>.</summary>
        static float BandWater(RugWash.Cleaning.RugCleaningGrid grid, int index)
        {
            int band = Mathf.Max(2, grid.Height / 7), x0 = grid.Width / 3, width = grid.Width / 3;
            float total = 0f;
            for (int y = index * band; y < (index + 1) * band; y++)
            for (int x = x0; x < x0 + width; x++)
                total += grid.GetCell(x, y).Moisture;
            return total;
        }

        static float[] Totals(RugWash.Cleaning.RugCleaningGrid grid)
        {
            var t = new float[6];
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var c = grid.GetCell(x, y);
                t[0] += c.Dust; t[1] += c.Loosened; t[2] += c.Moisture; t[3] += c.Foam; t[4] += c.Residue; t[5] += c.Stain;
            }
            return t;
        }

        sealed class BlastVictims : RugWash.Pranks.IPrankVictims
        {
            public readonly System.Collections.Generic.Dictionary<ulong, Vector3> Feet = new System.Collections.Generic.Dictionary<ulong, Vector3>();
            public readonly System.Collections.Generic.HashSet<ulong> Down = new System.Collections.Generic.HashSet<ulong>();
            public readonly System.Collections.Generic.List<ulong> Knocked = new System.Collections.Generic.List<ulong>();
            public float LastSeconds;
            public RugWash.Pranks.PrankGadget LastCause;
            public bool TryGetFeet(ulong clientId, out Vector3 feet) => Feet.TryGetValue(clientId, out feet);
            public bool IsDown(ulong clientId) => Down.Contains(clientId);
            public void CollectPlayers(System.Collections.Generic.List<ulong> into) => into.AddRange(Feet.Keys);
            public void Splash(ulong clientId, float intensity, float seconds, float fadeSeconds, string from) { }
            public void Knock(ulong clientId, Vector3 impulse, float seconds, string from, RugWash.Pranks.PrankGadget cause)
            {
                Knocked.Add(clientId);
                LastSeconds = seconds;
                LastCause = cause;
            }
        }

        sealed class Losses : RugWash.Pranks.IItemLoss
        {
            public readonly System.Collections.Generic.List<ulong> Victims = new System.Collections.Generic.List<ulong>();
            public void LoseItems(ulong victim, Vector3 feet, Vector3 push, int count) => Victims.Add(victim);
        }

        /// <summary>
        /// Jalon 12 : l'autorité de la dynamite. Un lancer validé = une seule explosion, après la mèche ; une seule détection :
        /// joueurs dans le rayon mis KO 5 s (cause dynamite) et objet tenu lâché ; hors de portée, à terre ou l'auteur : épargnés.
        /// Coût débité une fois, recharge ; poussière sur le tapis proche selon la distance (pas sur un tapis loin), une fois.
        /// </summary>
        [UnityTest]
        public IEnumerator Dynamite_OneExplosion_KnocksPlayersInRange_DropsHeldItems_DustsNearRugsOnce()
        {
            yield return LoadWorkshop();
            var service = game.Pranks;
            float clock = 100f;
            service.Clock = () => clock;
            var victims = new BlastVictims();
            var losses = new Losses();
            service.Victims = victims;
            service.ItemLoss = losses;
            game.Upgrades.Grant("up_prank_dynamite");
            game.Wallet.Wallet.Add(500);
            Assert.AreEqual(RugWash.Orders.OrderStartResult.Started, game.Orders.TryStart(game.Orders.Book.Orders[0]));
            yield return null;
            var tuning = service.Tuning;
            Vector3 rugCenter = game.Rug.UVToWorld(new Vector2(0.5f, 0.5f));
            game.PlayerController.Teleport(new Vector3(rugCenter.x, 0.06f, rugCenter.z - 3f), 0f, 20f);
            yield return null;
            Vector3 point = rugCenter + Vector3.up * 0.05f;
            victims.Feet[2] = point + new Vector3(1f, 0f, 0f);
            victims.Feet[3] = point + new Vector3(0f, 0f, 2.4f);
            victims.Feet[4] = point + new Vector3(6f, 0f, 0f);
            victims.Feet[5] = point + new Vector3(-1f, 0f, 0f);
            victims.Down.Add(5);
            victims.Feet[0] = point + new Vector3(0.5f, 0f, 0f);
            RugWash.Pranks.PrankService.DynamiteBlast blast = default;
            int detonated = 0;
            service.Detonated += b => { blast = b; detonated++; };
            float dirt = game.Rug.Grid.TotalDirt;
            int money = game.Wallet.Balance;

            var request = new RugWash.Pranks.PrankRequest { Gadget = RugWash.Pranks.PrankGadget.Dynamite, Target = RugWash.Pranks.PrankTarget.Area, Point = point };
            Assert.AreEqual(RugWash.Pranks.PrankResult.Done, service.Execute(game.Gadgets.Actor, request, out _));
            Assert.AreEqual(money - tuning.dynamite.costPerUse, game.Wallet.Balance, "Coût d'un bâton débité une fois.");
            Assert.AreEqual(1, service.PendingBlasts);
            service.Tick();
            Assert.AreEqual(0, victims.Knocked.Count, "Rien avant la fin de la mèche.");
            clock += tuning.dynamite.useSeconds + 0.01f;
            service.Tick();
            service.Tick();
            Assert.AreEqual(1, detonated, "Une seule explosion par lancer.");
            Assert.AreEqual(0, service.PendingBlasts);
            CollectionAssert.AreEquivalent(new ulong[] { 2, 3 }, victims.Knocked, "Dans le rayon : KO ; hors de portée, déjà à terre, l'auteur : épargnés.");
            Assert.AreEqual(RugWash.Pranks.PrankGadget.Dynamite, victims.LastCause);
            Assert.AreEqual(5f, victims.LastSeconds, 1e-4f, "KO de 5 s.");
            CollectionAssert.AreEquivalent(new ulong[] { 2, 3 }, losses.Victims, "Chaque joueur touché lâche son objet, une fois.");
            Assert.Greater(game.Rug.Grid.TotalDirt, dirt, "Poussière soulevée sur le tapis proche.");
            Assert.AreEqual(game.Rug.Grid.TotalDirt - dirt, blast.DustAdded, 0.01f, "Poussière comptée une fois.");
            Assert.AreEqual(1, blast.RugsDusted);

            // Joueurs encore à terre : un second bâton ne les remet pas KO ; un point hors de portée est refusé.
            clock += tuning.dynamite.cooldownSeconds + 1f;
            victims.Down.Add(2);
            victims.Down.Add(3);
            Assert.AreEqual(RugWash.Pranks.PrankResult.OutOfRange, service.Execute(game.Gadgets.Actor,
                new RugWash.Pranks.PrankRequest { Gadget = RugWash.Pranks.PrankGadget.Dynamite, Target = RugWash.Pranks.PrankTarget.Area, Point = point + Vector3.forward * 30f }, out _));
            Assert.AreEqual(RugWash.Pranks.PrankResult.Done, service.Execute(game.Gadgets.Actor, request, out _));
            clock += tuning.dynamite.useSeconds + 0.01f;
            service.Tick();
            Assert.AreEqual(2, detonated);
            Assert.AreEqual(2, victims.Knocked.Count, "Pas de KO enchaîné sur un joueur encore à terre.");
            victims.Down.Remove(2);
            victims.Down.Remove(3);

            // Explosion loin du tapis : pas de poussière.
            clock += tuning.dynamite.cooldownSeconds + 1f;
            float before = game.Rug.Grid.TotalDirt;
            var far = new Vector3(rugCenter.x + 4.5f, 0.06f, rugCenter.z - 3.5f);
            Assert.AreEqual(RugWash.Pranks.PrankResult.Done, service.Execute(game.Gadgets.Actor,
                new RugWash.Pranks.PrankRequest { Gadget = RugWash.Pranks.PrankGadget.Dynamite, Target = RugWash.Pranks.PrankTarget.Area, Point = far }, out _));
            clock += tuning.dynamite.useSeconds + 0.01f;
            service.Tick();
            Assert.AreEqual(3, detonated);
            Assert.AreEqual(before, game.Rug.Grid.TotalDirt, 1e-4f, "Tapis hors du rayon : aucune poussière.");
            Assert.AreEqual(0, blast.RugsDusted);
            service.Clock = () => Time.time;
            service.Victims = null;
            service.ItemLoss = null;
        }

        /// <summary>
        /// Jalon 12 : lancer en solo : le bâton vole, la mèche crépite, explosion (flash bref, secousse, ralenti bref puis temps
        /// normal) ; poussière réelle sur le tapis ; le lanceur n'est pas mis KO.
        /// </summary>
        [UnityTest]
        public IEnumerator Dynamite_LocalThrow_PlaysTheExplosion_FlashShakeSlowMo_ThenNormalTime()
        {
            yield return LoadWorkshop();
            game.Upgrades.Grant("up_prank_dynamite");
            game.Wallet.Wallet.Add(500);
            Assert.AreEqual(RugWash.Orders.OrderStartResult.Started, game.Orders.TryStart(game.Orders.Book.Orders[0]));
            yield return null;
            var gadgets = game.Gadgets;
            Assert.IsTrue(gadgets.Equip(RugWash.Pranks.PrankGadget.Dynamite));
            Vector3 center = game.Rug.UVToWorld(new Vector2(0.5f, 0.5f));
            game.PlayerController.Teleport(new Vector3(center.x, 0.06f, center.z - 3.2f), 0f, Mathf.Atan2(1.62f - center.y, 3.2f) * Mathf.Rad2Deg);
            yield return null;
            yield return null;
            float dirt = game.Rug.Grid.TotalDirt;
            int explosions = gadgets.Effects.ExplosionsPlayed;
            Assert.IsTrue(gadgets.TryUse(), "Lancer validé.");
            Assert.AreEqual(1, gadgets.Effects.ActiveThrows, "Bâton en vol.");
            var camera = game.ToolController.GetComponentInChildren<Camera>();
            bool flashed = false, shook = false, slowed = false;
            var motion = camera.GetComponent<CameraMotion>();
            float end = Time.unscaledTime + 3f;
            while (Time.unscaledTime < end)
            {
                yield return null;
                flashed |= gadgets.Effects.FlashOn;
                shook |= motion != null && motion.IsShaking;
                slowed |= Time.timeScale < 0.99f;
                if (gadgets.Effects.FlashOn && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null && !System.IO.File.Exists(Path.Combine(Folder(), "v42_dynamite_boom.png")))
                    WorkshopPlayModeTests.Save(camera, Path.Combine(Folder(), "v42_dynamite_boom.png"));
            }
            Assert.AreEqual(explosions + 1, gadgets.Effects.ExplosionsPlayed, "Une explosion.");
            Assert.IsTrue(flashed, "Flash bref.");
            Assert.IsFalse(gadgets.Effects.FlashOn, "Le flash s'éteint.");
            Assert.IsTrue(shook, "Secousse de la caméra.");
            Assert.IsTrue(slowed && gadgets.SlowMotions == 1, "Ralenti bref.");
            Assert.AreEqual(1f, Time.timeScale, 1e-4f, "Le temps normal revient.");
            Assert.Greater(game.Rug.Grid.TotalDirt, dirt, "Poussière soulevée sur le tapis.");
            Assert.IsFalse(game.Knockdown.IsDown, "Le lanceur n'est pas touché par sa propre farce.");
        }

        /// <summary>
        /// Jalon 12 : silhouette d'un joueur touché vue par les autres : chute exagérée, membres dispersés dans l'atelier
        /// (morceaux lancés, vrais membres cachés), puis tout se rassemble au relevé : apparence normale, rien de détruit.
        /// </summary>
        [UnityTest]
        public IEnumerator Dynamite_RemoteAvatar_LimbsScatter_ThenReassemble()
        {
            SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var prefab = MultiplayerSession.Find().Content.avatarPrefab;
            yield return LoadWorkshop();
            var avatar = Object.Instantiate(prefab, new Vector3(-3.5f, 0f, -2.5f), Quaternion.identity).GetComponent<NetworkPlayerAvatar>();
            avatar.SetPreviewState(14, false, 0f);
            yield return null;
            var camera = game.ToolController.GetComponentInChildren<Camera>();
            game.PlayerController.Teleport(new Vector3(-0.8f, 0.06f, -2.5f), -90f, 10f);
            int renderers = avatar.GetComponentsInChildren<Renderer>(true).Length;
            avatar.PreviewKnockdown(true, new Vector3(-4f, 5f, 2f), KnockCause.Dynamite);
            var ragdoll = avatar.Ragdoll;
            Assert.IsTrue(ragdoll.LimbsScattered, "Membres dispersés.");
            Assert.AreEqual(4, ragdoll.ScatterPiecesShown, "Quatre morceaux lancés.");
            yield return new WaitForSeconds(0.8f);
            Assert.Greater(ragdoll.ScatterSpread, 0.5f, "Les morceaux s'éloignent dans l'atelier.");
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) WorkshopPlayModeTests.Save(camera, Path.Combine(Folder(), "v42_dynamite_scatter.png"));
            avatar.PreviewKnockdown(false, Vector3.zero);
            yield return new WaitForSeconds(0.8f);
            Assert.IsFalse(ragdoll.LimbsScattered, "Membres rassemblés au relevé.");
            Assert.AreEqual(0, ragdoll.ScatterPiecesShown);
            Assert.IsFalse(ragdoll.IsBusy);
            Assert.Less(ragdoll.BodyOffsetFromRoot, 0.02f, "Corps revenu à sa place.");
            foreach (var skeleton in avatar.GetComponentsInChildren<AvatarSkeleton>(true)) Assert.IsFalse(skeleton.LimbsHidden, "Membres de nouveau visibles.");
            Assert.AreEqual(renderers, avatar.GetComponentsInChildren<Renderer>(true).Length, "Rien de détruit ni d'ajouté.");
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) WorkshopPlayModeTests.Save(camera, Path.Combine(Folder(), "v42_dynamite_reassembled.png"));
            Object.Destroy(avatar.gameObject);
        }

        /// <summary>
        /// Jalon 13 : couleur de peau choisie dans « Personnage » : seule la peau change (tenue, gants intacts), le choix est
        /// gardé dans le profil et relu au retour dans le menu ; le format réseau la transporte.
        /// </summary>
        [UnityTest]
        public IEnumerator SkinColor_OnlyTheSkinChanges_SavedAndRestored_SentOnline()
        {
            TestSaveSandbox.Reset();
            SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var menu = Object.FindAnyObjectByType<RugWash.UI.MainMenuController>();
            menu.Character.Open();
            var appearance = menu.Character.PreviewAppearance;
            Assert.Greater(appearance.SkinRendererCount, 0, "Matériaux de peau repérés sur l'ouvrier.");
            Color outfit = appearance.AppliedColor(0), gloves = appearance.AppliedColor(2);
            Assert.AreEqual(Color.clear, appearance.AppliedColor(3), "Peau d'origine par défaut.");
            menu.Character.Step(3, 4);
            yield return null;
            Assert.AreEqual(4, menu.Character.Look.skin);
            Assert.Less(((Vector4)(CharacterPalette.Skins[4].color - appearance.AppliedColor(3))).magnitude, 0.01f, "Peau teintée.");
            Assert.AreEqual(outfit, appearance.AppliedColor(0), "Tenue inchangée.");
            Assert.AreEqual(gloves, appearance.AppliedColor(2), "Gants inchangés.");
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                yield return new WaitForSecondsRealtime(0.3f);
                var preview = menu.Character.GetComponentInChildren<UnityEngine.UI.RawImage>(true);
                if (preview != null && preview.texture is RenderTexture rt)
                {
                    var previous = RenderTexture.active;
                    RenderTexture.active = rt;
                    var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    texture.Apply();
                    RenderTexture.active = previous;
                    File.WriteAllBytes(Path.Combine(Folder(), "v42_skin_tone.png"), texture.EncodeToPNG());
                    Object.Destroy(texture);
                }
            }
            menu.Character.Close();

            SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
            yield return null;
            yield return null;
            menu = Object.FindAnyObjectByType<RugWash.UI.MainMenuController>();
            menu.Character.Open();
            Assert.AreEqual(4, menu.Character.Look.skin, "Choix relu après rechargement.");
            Assert.Less(((Vector4)(CharacterPalette.Skins[4].color - menu.Character.PreviewAppearance.AppliedColor(3))).magnitude, 0.01f);
            menu.Character.Close();
            var look = menu.Character.Look;
            Assert.AreEqual(look, CharacterLook.Unpack(look.Pack()), "Le format réseau transporte la peau.");
            Assert.AreEqual(0, CharacterLook.Unpack(new CharacterLook { outfit = 1, cap = 2, gloves = 3 }.Pack()).skin, "Ancienne apparence : peau d'origine.");
        }

        RugWash.Cleaning.CleaningToolBase FindTool(string id)
        {
            foreach (var tool in game.ToolController.Tools)
                if (tool != null && tool.Definition != null && tool.Definition.id == id) return tool;
            return null;
        }

        static string[] Texts(Component row)
        {
            var labels = row.GetComponentsInChildren<UnityEngine.UI.Text>();
            var texts = new string[labels.Length];
            for (int i = 0; i < labels.Length; i++) texts[i] = labels[i].text;
            return texts;
        }

        static string Folder()
        {
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ci", "screens");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }
}
