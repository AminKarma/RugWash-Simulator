using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RugWash.Core;
using RugWash.Network;
using RugWash.Player;
using RugWash.Pranks;
using RugWash.Save;
using RugWash.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RugWash.Tests
{
    /// <summary>
    /// Mission de corrections, dans la vraie scène : sauvegardes de session (un invité n'écrit jamais l'économie de l'hôte
    /// dans sa sauvegarde), outils (tête jamais sous la surface, jet depuis la buse), raclette, chute et KO, HUD, boutique.
    /// </summary>
    public class BugfixPlayModeTests
    {
        GameBootstrap game;

        IEnumerator LoadWorkshop(bool legacyTools = true)
        {
            TestSaveSandbox.Reset(legacyTools);
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
            if (game != null && game.SaveSystem != null) game.SaveSystem.SessionGuest = false;
            Time.timeScale = 1f;
            yield return null;
        }

        SaveData ReadSave() => new SaveFileStore(TestSaveSandbox.Directory, SaveManager.DefaultFileName).Load().Data;

        // ------------------------------------------------------------------ 3. Sauvegardes et multijoueur

        /// <summary>
        /// V0.2.5 (sauvegarde de session) : un invité joue sur la sauvegarde choisie par l'hôte. Rien de la partie (argent,
        /// atelier, achats, livraisons) n'est écrit dans ses propres emplacements ; seuls ses paramètres vont à son profil.
        /// </summary>
        [UnityTest]
        public IEnumerator Guest_NeverWritesItsOwnSlots_PreferencesGoToTheProfile()
        {
            yield return LoadWorkshop();
            Assert.IsTrue(game.SaveSystem.SaveNow("départ"));
            var personal = ReadSave();
            string workshopBefore = personal.workshop.workSurface;
            int countBefore = game.SaveSystem.SaveCount;

            game.SaveSystem.SessionGuest = true;
            // L'atelier affiché et l'argent sont ceux de la session de l'hôte : rien n'entre dans la sauvegarde de l'invité.
            var pad = game.WorkSurface.Surface.Grid;
            var mud = new Cleaning.DepositOperation { Type = Cleaning.DirtType.Mud, Mud = 0.5f, Water = 0.3f };
            pad.ApplyRect(new RectInt(0, 0, pad.Width, pad.Height), 1f, ref mud);
            game.Wallet.Wallet.Restore(personal.money + 420);
            game.Settings.SetMusicVolume(0.25f);
            Assert.IsTrue(game.SaveSystem.SaveNow("partie en ligne"), "Le profil est écrit.");
            game.Orders.TryStart(game.Orders.Book.Orders[0]);
            yield return null;
            GameLoopPlayModeTests.CleanThoroughly(game.Rug.Grid);
            game.Orders.Deliver();
            yield return null;

            var after = ReadSave();
            Assert.AreEqual(personal.money, after.money, "L'argent de la session n'est jamais écrit chez l'invité.");
            Assert.AreEqual(personal.ordersDelivered, after.ordersDelivered, "La livraison appartient à la sauvegarde de l'hôte.");
            Assert.AreEqual(workshopBefore, after.workshop.workSurface, "L'atelier de l'hôte n'est jamais écrit chez l'invité.");
            Assert.AreEqual(countBefore, game.SaveSystem.SaveCount, "Aucun emplacement écrit.");
            var profile = new ProfileStore(TestSaveSandbox.Directory).Load(out _);
            Assert.AreEqual(0.25f, profile.settings.musicVolume, 1e-4f, "Les préférences vont au profil du joueur.");
        }

        // ------------------------------------------------------------------ 2.5 Fusil : KO cartoon de 5 s

        [UnityTest]
        public IEnumerator BlasterKnockout_LocksControlsForFiveSeconds_NoQuickRecoverNoSecondKo_ThenGetsUpAlone()
        {
            yield return LoadWorkshop();
            float startY = game.PlayerController.transform.position.y;
            game.Gadgets.ReceiveKnock(new Vector3(0f, 3f, 9f), 5f, "Testeur", PrankGadget.Blaster);
            Assert.IsTrue(game.Knockdown.IsDown);
            Assert.AreEqual(KnockCause.Blaster, game.Knockdown.Cause);
            Assert.IsTrue(game.ToolController.Locked, "Outils coupés pendant le KO.");
            yield return new WaitForSeconds(1.5f);
            Assert.IsFalse(game.Knockdown.TryQuickRecover(), "KO du fusil : pas de relevé anticipé.");
            Assert.IsFalse(game.Knockdown.Knockdown(Vector3.forward * 9f, 5f, null, KnockCause.Blaster), "Pas de second KO pendant le premier.");
            Assert.AreEqual(1, game.Knockdown.KnockdownCount);
            float start = Time.time;
            yield return new WaitUntil(() => !game.Knockdown.IsDown || Time.time - start > 8f);
            Assert.IsFalse(game.Knockdown.IsDown, "Relevé automatique, sans rester bloqué.");
            Assert.IsFalse(game.ToolController.Locked, "Contrôles rendus.");
            Assert.Greater(game.PlayerController.transform.position.y, startY - 0.2f, "Jamais à travers le sol.");
        }

        [UnityTest]
        public IEnumerator BlasterKnockout_RemoteAvatarHeadPops_ThenComesBackWhenStanding()
        {
            SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var prefab = MultiplayerSession.Find().Content.avatarPrefab;
            yield return LoadWorkshop();
            var avatar = Object.Instantiate(prefab, new Vector3(-3.5f, 0f, -2.5f), Quaternion.identity).GetComponent<NetworkPlayerAvatar>();
            avatar.SetPreviewState(3, false, 0f);
            yield return null;
            avatar.PreviewKnockdown(true, new Vector3(0f, 3f, 9f), KnockCause.Fall);
            Assert.IsFalse(avatar.HeadPopped, "Coup de batte : chute simple.");
            avatar.PreviewKnockdown(false, Vector3.zero);
            yield return new WaitForSeconds(0.7f);
            avatar.PreviewKnockdown(true, new Vector3(0f, 3f, 9f), KnockCause.Blaster);
            Assert.IsTrue(avatar.HeadPopped, "Fusil : la tête « éclate » (cartoon).");
            Assert.IsTrue(avatar.Ragdoll.IsActive, "Ragdoll avec un élan fort.");
            Assert.LessOrEqual(avatar.Ragdoll.BodyRigidbody.linearVelocity.magnitude, 12.01f, "Élan exagéré mais borné.");
            avatar.PreviewKnockdown(true, new Vector3(0f, 3f, 9f), KnockCause.Blaster);
            yield return new WaitForSeconds(1f);
            avatar.PreviewKnockdown(false, Vector3.zero);
            Assert.IsFalse(avatar.HeadPopped, "La tête revient quand il se relève.");
            yield return new WaitForSeconds(0.7f);
            Assert.IsFalse(avatar.Ragdoll.IsBusy, "Retour à la pose debout.");
            Object.Destroy(avatar.gameObject);
        }

        // ------------------------------------------------------------------ 2.10 Mousse et eau : pas d'envoi du tapis entier

        [UnityTest]
        public IEnumerator DirtyWashArea_RecontaminatesOneBorderAtATime_NeverTheWholeRugAtOnce()
        {
            yield return LoadWorkshop();
            Assert.AreEqual(Orders.OrderStartResult.Started, game.Orders.TryStart(game.Orders.Book.Orders[0]));
            var pad = game.WorkSurface.Surface.Grid;
            var deposit = new Cleaning.DepositOperation { Type = Cleaning.DirtType.Mud, Mud = 0.4f, Water = 0.3f };
            pad.ApplyRect(new RectInt(0, 0, pad.Width, Mathf.RoundToInt(pad.Height * 0.7f)), 1f, ref deposit);
            game.WorkSurface.RefreshStatus(force: true);
            Assert.IsTrue(game.WorkSurface.ContaminatesRug);

            var grid = game.Rug.Grid;
            float start = grid.TotalDirt;
            var rows = new List<Cleaning.RugCleaningGrid.RowSpan>();
            for (int pass = 0; pass < 4; pass++)
            {
                grid.TryConsumeNetworkDirtyRect(out _);
                grid.ConsumeDirtyRows(rows);
                game.WorkSurface.ContaminateResidentRug(0.4f);
                Assert.IsTrue(grid.TryConsumeNetworkDirtyRect(out RectInt sent), "La boue part vers les invités.");
                Assert.Less(sent.width * sent.height, grid.CellCount / 3,
                    $"Passage {pass} : un bord seulement ({sent}), pas tout le tapis renvoyé à chaque fois.");
                int encoded = grid.ConsumeDirtyRows(rows);
                Assert.Less(encoded, grid.CellCount / 3, "Réencodage limité au bord touché.");
            }
            Assert.Greater(grid.TotalDirt, start, "Le tapis posé sur une aire sale se salit toujours.");
            yield return null;
        }

        // ------------------------------------------------------------------ 2.6 HUD : seulement l'inventaire réel

        [UnityTest]
        public IEnumerator Toolbar_ShowsOnlyOwnedTools_AndDropsTheHighlightWhileAGadgetIsInHand()
        {
            yield return LoadWorkshop(legacyTools: false);
            var toolbar = Object.FindAnyObjectByType<ToolSelectionUI>();
            foreach (var tool in toolbar.Shown) Assert.IsTrue(tool.IsUnlocked, $"{tool.name} affiché sans être possédé.");
            foreach (var tool in game.ToolController.Tools)
                if (tool.IsUnlocked) CollectionAssert.Contains(toolbar.Shown, tool, $"{tool.name} possédé mais absent.");
            int active = toolbar.Shown.Count > 0 ? IndexOfActive(toolbar) : -1;
            Assert.GreaterOrEqual(active, 0, "Outil actif dans la barre.");
            Color highlighted = toolbar.SlotColor(active);

            game.ToolController.Suspended = true;
            yield return null;
            Assert.AreNotEqual(highlighted, toolbar.SlotColor(active), "Gadget en main : plus d'outil en surbrillance.");
            game.ToolController.Suspended = false;
            yield return null;
            Assert.AreEqual(highlighted, toolbar.SlotColor(active), "Outil ressorti : surbrillance rétablie.");
        }

        static int IndexOfActive(ToolSelectionUI toolbar)
        {
            var tools = Object.FindAnyObjectByType<PlayerToolController>();
            for (int i = 0; i < toolbar.Shown.Count; i++) if (toolbar.Shown[i] == tools.ActiveTool) return i;
            return -1;
        }
    }
}
