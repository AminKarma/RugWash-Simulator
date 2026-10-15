using System.Collections;
using System.IO;
using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Economy;
using RugWash.Orders;
using RugWash.RugDex;
using RugWash.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RugWash.Tests
{
    /// <summary>
    /// Jalon 3 (V2) dans la vraie scène : tapis de luxe et sa commande, nouvelles saletés dans les commandes, produits
    /// spécialisés (achat, sélection, effet, épuisement, sauvegarde), usure des fibres, RugDex (atelier et menu principal).
    /// </summary>
    public class ContentV3PlayModeTests
    {
        GameBootstrap game;
        Keyboard keyboard;
        InputSettings originalInputSettings;

        [UnitySetUp]
        public IEnumerator LoadWorkshop()
        {
            TestSaveSandbox.Reset();
            yield return Load("Workshop");
        }

        IEnumerator Load(string scene)
        {
            SceneManager.LoadScene(scene, LoadSceneMode.Single);
            yield return null;
            yield return null;
            yield return null;
            game = Object.FindAnyObjectByType<GameBootstrap>();
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (game != null) game.ToolController.AutomationUseOverride = false;
            if (keyboard != null)
            {
                InputSystem.RemoveDevice(keyboard);
                keyboard = null;
            }
            if (originalInputSettings != null)
            {
                var temporary = InputSystem.settings;
                InputSystem.settings = originalInputSettings;
                originalInputSettings = null;
                Object.Destroy(temporary);
            }
            Time.timeScale = 1f;
            yield return null;
        }

        // ------------------------------------------------------------------ Aides

        OrderDefinition Order(string id)
        {
            foreach (var order in game.Orders.Book.Orders)
                if (order.id == id) return order;
            Assert.Fail($"Commande {id} absente.");
            return null;
        }

        IEnumerator Accept(string id)
        {
            Assert.AreEqual(OrderStartResult.Started, game.Orders.TryStart(Order(id)));
            yield return null;
        }

        void AimAtRug(Vector2 uv, float standoff = 0.9f)
        {
            Vector3 target = game.Rug.UVToWorld(uv);
            float pitch = Mathf.Atan2(1.68f - target.y, standoff) * Mathf.Rad2Deg;
            game.PlayerController.Teleport(new Vector3(target.x, 0.06f, target.z - standoff), 0f, pitch);
        }

        IEnumerator Use(int slot, float seconds)
        {
            Assert.IsTrue(game.ToolController.SelectSlot(slot));
            yield return new WaitForSeconds(0.2f);
            game.ToolController.AutomationUseOverride = true;
            yield return new WaitForSeconds(seconds);
            game.ToolController.AutomationUseOverride = false;
            yield return null;
        }

        void Paint(Vector2 uv, float radius, RugCell cell)
        {
            var grid = game.Rug.Grid;
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var cellUv = new Vector2((x + 0.5f) / grid.Width, (y + 0.5f) / grid.Height);
                if (Vector2.Distance(Vector2.Scale(cellUv - uv, grid.SizeMeters), Vector2.zero) <= radius) grid.SetCell(x, y, cell);
            }
        }

        RugCell CellAt(Vector2 uv)
        {
            var c = game.Rug.Grid.UVToCell(uv);
            return game.Rug.Grid.GetCell(c.x, c.y);
        }

        ProductDefinition ProductById(string id)
        {
            foreach (var product in game.Products.Book.Catalog)
                if (product.id == id) return product;
            Assert.Fail($"Produit {id} absent.");
            return null;
        }

        Keyboard VirtualKeyboard()
        {
            if (keyboard != null) return keyboard;
            originalInputSettings = InputSystem.settings;
            var settings = Object.Instantiate(originalInputSettings);
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = settings;
            keyboard = InputSystem.AddDevice<Keyboard>("RugWashTestKeyboard3");
            return keyboard;
        }

        IEnumerator Press(Key key)
        {
            InputSystem.QueueStateEvent(VirtualKeyboard(), new KeyboardState(key));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }

        // ------------------------------------------------------------------ 3.1 Tapis de luxe

        [UnityTest]
        public IEnumerator LuxuryOrder_IsLockedByReputation_ThenLoadsTheSilkRug()
        {
            var palace = Order("order_palace");
            Assert.AreEqual(OrderStartResult.ReputationTooLow, game.Orders.TryStart(palace), "Réputation insuffisante au départ.");
            game.Reputation.Restore(palace.requiredReputation);
            yield return Accept("order_palace");

            Assert.AreEqual(GameState.ActiveOrder, game.GameState.State);
            Assert.AreEqual("luxury", game.Rug.Definition.id);
            Assert.AreEqual(RugPileType.Silk, game.Rug.Definition.pileType);
            Assert.Greater(game.Rug.Fragility, 0.5f);
            Assert.Greater(game.Rug.Grid.Width, 200, "Grand tapis : grille plus fine (3,2 m × 64 cellules/m).");
            Assert.AreEqual(game.Rug.Definition.sheen, game.Rug.DirtRenderer.RugMaterial.GetFloat("_DrySmoothness"), 1e-4f, "Éclat de la soie appliqué.");
            Assert.IsTrue(game.RugDexService.Book.IsDiscovered("luxury"), "Le tapis entre dans le RugDex.");
        }

        [UnityTest]
        public IEnumerator FragileRug_DryBrushingWears_FoamProtects()
        {
            game.Reputation.Restore(10);
            yield return Accept("order_palace");
            float wearBefore = game.Rug.Grid.AverageWear;
            AimAtRug(new Vector2(0.3f, 0.5f));
            yield return Use(3, 1.5f);
            float dryWear = game.Rug.Grid.AverageWear - wearBefore;
            Assert.Greater(dryWear, 0f, "Brossage à sec : fibres usées.");
            yield return new WaitForSeconds(0.2f);

            // Même brossage sur une zone savonneuse : aucune usure supplémentaire.
            AimAtRug(new Vector2(0.7f, 0.5f));
            yield return Use(2, 0.8f);
            float before = game.Rug.Grid.AverageWear;
            yield return Use(3, 1.5f);
            Assert.AreEqual(before, game.Rug.Grid.AverageWear, 1e-6f, "La mousse protège les fibres.");
        }

        // ------------------------------------------------------------------ 3.2 Nouvelles saletés

        [UnityTest]
        public IEnumerator NewDirtTypes_ArePresentInRealOrders()
        {
            game.Reputation.Restore(10);
            var expectations = new (string order, DirtType type)[]
            {
                ("order_kennel", DirtType.PetHair), ("order_kennel", DirtType.DustClumps), ("order_tailor", DirtType.Lint),
                ("order_site", DirtType.DriedMud),
            };
            foreach (var (orderId, type) in expectations)
            {
                if (game.Orders.ActiveOrder != null && game.Orders.ActiveOrder.id != orderId) game.Orders.Abandon();
                if (game.Orders.ActiveOrder == null) yield return Accept(orderId);
                var grid = game.Rug.Grid;
                int found = 0;
                for (int y = 0; y < grid.Height; y++)
                for (int x = 0; x < grid.Width; x++)
                {
                    var cell = grid.GetCell(x, y);
                    if ((cell.DustType == type && cell.Dust > 0f) || (cell.StainType == type && cell.Stain > 0f)) found++;
                }
                Assert.Greater(found, 50, $"{type} doit être présente dans « {Order(orderId).title} ».");
            }
            // Adhérence de la boue sèche du chantier (type + couche) bien inscrite dans la grille.
            float maxAdhesion = 0f;
            var siteGrid = game.Rug.Grid;
            for (int y = 0; y < siteGrid.Height; y++)
            for (int x = 0; x < siteGrid.Width; x++)
                maxAdhesion = Mathf.Max(maxAdhesion, siteGrid.GetCell(x, y).Adhesion);
            Assert.Greater(maxAdhesion, 0.3f);
        }

        // ------------------------------------------------------------------ 3.3 Produits spécialisés

        [UnityTest]
        public IEnumerator Products_BoughtSelectedWithT_ConsumedAndTheyReallyHelp()
        {
            TestRugs.LoadDefault(game);
            var degreaser = ProductById("product_degreaser");
            game.Wallet.Wallet.Add(500);
            int balance = game.Wallet.Balance;
            Assert.IsTrue(game.ShopUI.Open());
            game.ShopUI.ShowTab(true);
            int index = -1;
            for (int i = 0; i < game.Products.Book.Catalog.Count; i++)
                if (game.Products.Book.Catalog[i] == degreaser) index = i;
            Assert.AreEqual(ProductPurchaseResult.Purchased, game.ShopUI.BuyProduct(index));
            game.ShopUI.Close();
            Assert.AreEqual(balance - degreaser.packPrice, game.Wallet.Balance);
            Assert.AreEqual(degreaser.packLiters, game.Products.Book.StockOf(degreaser), 1e-4f);

            yield return null;
            yield return Press(Key.T);
            Assert.AreEqual(degreaser, game.Products.Active, "La touche T sélectionne le produit en stock.");

            // Deux plaques de graisse identiques : l'une savonnée au détergent standard, l'autre avec le dégraissant.
            var grease = new RugCell { Stain = 0.8f, StainType = DirtType.Grease, Adhesion = 0.25f };
            Paint(new Vector2(0.3f, 0.5f), 0.3f, grease);
            Paint(new Vector2(0.7f, 0.5f), 0.3f, grease);
            AimAtRug(new Vector2(0.7f, 0.5f));
            float stockBefore = game.Products.Book.StockOf(degreaser);
            yield return Use(2, 0.6f);
            Assert.Less(game.Products.Book.StockOf(degreaser), stockBefore, "Le produit est consommé en pulvérisant.");
            Assert.AreEqual(game.Rug.Products.IdOf(degreaser), CellAt(new Vector2(0.7f, 0.5f)).Product, "La mousse porte le produit.");
            game.Products.Book.Select(null);
            AimAtRug(new Vector2(0.3f, 0.5f));
            yield return Use(2, 0.6f);
            Assert.AreEqual(ProductTable.Standard, CellAt(new Vector2(0.3f, 0.5f)).Product);

            AimAtRug(new Vector2(0.7f, 0.5f));
            yield return Use(3, 1f);
            AimAtRug(new Vector2(0.3f, 0.5f));
            yield return Use(3, 1f);
            float withProduct = 0.8f - CellAt(new Vector2(0.7f, 0.5f)).Stain;
            float standard = 0.8f - CellAt(new Vector2(0.3f, 0.5f)).Stain;
            Assert.Greater(withProduct, standard * 1.5f, $"Dégraissant {withProduct:0.000} contre détergent standard {standard:0.000}.");
        }

        [UnityTest]
        public IEnumerator Product_RunsOut_ThenSprayerFallsBackToStandard()
        {
            TestRugs.LoadDefault(game);
            var enzyme = ProductById("product_enzyme");
            game.Products.Restore(new[] { new System.Collections.Generic.KeyValuePair<string, float>(enzyme.id, 0.01f) }, enzyme.id);
            Assert.AreEqual(enzyme, game.Products.Active);
            ProductDefinition depleted = null;
            game.Products.Book.Depleted += p => depleted = p;
            AimAtRug(new Vector2(0.5f, 0.5f));
            yield return Use(2, 0.6f);
            Assert.AreEqual(enzyme, depleted);
            Assert.IsNull(game.Products.Active, "Retour au détergent standard.");
            Assert.Less(game.ToolController.Equipment.Soap.Amount, game.ToolController.Equipment.Soap.Capacity, "Le détergent continue d'être utilisé.");
        }

        [UnityTest]
        public IEnumerator Products_AndRugDex_AreSavedAndRestored()
        {
            var silk = ProductById("product_silk");
            game.Products.Book.Add(silk, 3f);
            game.Products.Book.Select(silk);
            game.RugDexService.Book.RecordDelivery("shag", OrderGrade.A);
            Assert.IsTrue(game.SaveSystem.SaveNow("test produits"));

            yield return Load("Workshop");
            Assert.AreEqual(3f, game.Products.Book.StockOf(ProductById("product_silk")), 1e-4f);
            Assert.AreEqual("product_silk", game.Products.Active.id);
            Assert.AreEqual("A", game.RugDexService.Book.Get("shag").bestGrade);
            Assert.IsFalse(game.Rug.IsLoaded, "Aucun tapis d'entraînement ne réapparaît au chargement.");
        }

        [UnityTest]
        public IEnumerator ShopProductsTab_ListsProductsWithRealEffects()
        {
            Assert.IsTrue(game.ShopUI.Open());
            game.ShopUI.ShowTab(true);
            yield return null;
            int visible = 0;
            string all = string.Empty;
            foreach (var row in game.ShopUI.Rows)
                if (row.gameObject.activeSelf)
                {
                    visible++;
                    foreach (var text in row.GetComponentsInChildren<Text>()) all += text.text + "\n";
                }
            Assert.AreEqual(game.Products.Book.Catalog.Count + 1, visible, "Bidon de détergent puis produits spécialisés.");
            StringAssert.Contains("Cible :", all);
            StringAssert.Contains("Flacon de", all);
            game.ShopUI.ShowTab(false);
            yield return null;
            int upgrades = 0;
            foreach (var row in game.ShopUI.Rows) if (row.gameObject.activeSelf) upgrades++;
            // v0.1.2 (jalon 5) : l'équipement d'atelier (paliers, machines, robot) a son propre onglet.
            game.ShopUI.ShowTab(ShopUI.Tab.Workshop);
            yield return null;
            foreach (var row in game.ShopUI.Rows) if (row.gameObject.activeSelf) upgrades++;
            // Jalon 6 : les gadgets de farce ont aussi leur onglet.
            game.ShopUI.ShowTab(ShopUI.Tab.Pranks);
            yield return null;
            foreach (var row in game.ShopUI.Rows) if (row.gameObject.activeSelf) upgrades++;
            Assert.AreEqual(game.Upgrades.Catalog.Count, upgrades, "Les onglets Matériel, Atelier et Farces listent toutes les améliorations.");
            game.ShopUI.Close();
        }

        // ------------------------------------------------------------------ Commandes : usure et liste défilante

        [UnityTest]
        public IEnumerator PalaceDelivery_WithDamagedFibers_ShowsTheWearCriterionFailed()
        {
            game.Reputation.Restore(10);
            yield return Accept("order_palace");
            GameLoopPlayModeTests.CleanThoroughly(game.Rug.Grid);
            var wear = new BrushOperation { Table = game.Rug.DirtTable, Power = 0.1f, DeltaTime = 4f, FiberWearEnabled = true, WearPerSecond = 0.1f };
            game.Rug.Grid.ApplyAll(ref wear);
            var result = game.Orders.Deliver();
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Lines.Exists(l => l.Label == "Fibres abîmées" && !l.Met), "Critère d'usure affiché et non atteint.");
            Assert.AreNotEqual(OrderGrade.S, result.Grade);
            Assert.AreEqual(1, game.RugDexService.Book.Get("luxury").timesDelivered, "Livraison enregistrée dans le RugDex.");
        }

        [UnityTest]
        public IEnumerator OrderBoard_ListsAllOrders_InAScrollableList()
        {
            Assert.IsTrue(game.OrderBoard.Open());
            yield return null;
            Assert.AreEqual(game.Orders.Book.Orders.Count, game.OrderBoard.Rows.Count);
            Assert.GreaterOrEqual(game.OrderBoard.Rows.Count, 10);
            Assert.IsNotNull(game.OrderBoard.Rows[0].GetComponentInParent<ScrollRect>(), "Liste défilante.");
            game.OrderBoard.Close();
        }

        // ------------------------------------------------------------------ 3.4 RugDex

        [UnityTest]
        public IEnumerator RugDex_FromPauseMenu_ShowsRealData_UnknownConditions_AndCompletion()
        {
            Assert.IsTrue(game.GameState.Pause());
            yield return null;
            var pause = Object.FindAnyObjectByType<PauseMenuUI>();
            Button rugDexButton = null;
            foreach (var button in pause.GetComponentsInChildren<Button>(true))
                if (button.name == "RugDex") rugDexButton = button;
            Assert.IsNotNull(rugDexButton);
            rugDexButton.onClick.Invoke();
            yield return null;
            var dex = game.RugDexUI;
            Assert.IsTrue(dex.IsOpen);
            Assert.AreEqual(game.RugDexService.Catalog.rugs.Count, dex.Cards.Count, "Une carte par tapis du catalogue.");
            StringAssert.Contains($"0 / {dex.Cards.Count} tapis", dex.CompletionText);

            int known = 0;
            foreach (var card in dex.Cards) if (card.ShowsKnownRug) known++;
            Assert.AreEqual(0, known, "Aucun tapis connu au départ (plus de tapis d'entraînement).");

            int luxuryIndex = game.RugDexService.Catalog.rugs.FindIndex(r => r.id == "luxury");
            dex.Select(luxuryIndex);
            StringAssert.Contains("???", dex.Cards[luxuryIndex].Label);
            StringAssert.Contains("Résidence Valmont", dex.DetailText, "Condition de découverte réelle (commande qui propose ce tapis).");
            StringAssert.Contains("réputation 8", dex.DetailText);

            // Découverte en acceptant une commande : la complétion augmente aussitôt et la fiche devient réelle.
            dex.Close();
            game.GameState.Resume();
            yield return null;
            var first = game.Orders.Book.Orders[0];
            Assert.AreEqual(OrderStartResult.Started, game.Orders.TryStart(first));
            yield return null;
            Assert.IsTrue(game.GameState.Pause());
            yield return null;
            Assert.IsTrue(dex.Open() || dex.IsOpen);
            StringAssert.Contains($"1 / {dex.Cards.Count} tapis", dex.CompletionText);
            int discovered = game.RugDexService.Catalog.rugs.IndexOf(first.rug);
            dex.Select(discovered);
            StringAssert.Contains(RugDefinition.CategoryLabel(first.rug.pileType), dex.DetailText);
            StringAssert.Contains("Dimensions : ", dex.DetailText);
            dex.Close();
            game.GameState.Resume();
        }

        [UnityTest]
        public IEnumerator RugDex_InMainMenu_ReadsTheSavedProgress()
        {
            game.RugDexService.Book.RecordDelivery("classic", OrderGrade.S);
            Assert.IsTrue(game.SaveSystem.SaveNow("test rugdex"));
            yield return Load("MainMenu");
            var menu = Object.FindAnyObjectByType<MainMenuController>();
            Assert.IsNotNull(menu);
            menu.RugDexButton.onClick.Invoke();
            yield return null;
            Assert.IsTrue(menu.RugDex.IsOpen);
            StringAssert.Contains("1 / 4 tapis", menu.RugDex.CompletionText);
            menu.RugDex.Select(0);
            StringAssert.Contains("meilleure note : S", menu.RugDex.DetailText);
            menu.RugDex.Close();
        }

        // ------------------------------------------------------------------ Captures

        [UnityTest, Category("Capture")]
        public IEnumerator Capture_ContentGallery()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Pas de GPU.");
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ci", "screens");
            Directory.CreateDirectory(folder);
            var camera = Camera.main;
            Canvas hud = null;
            foreach (var canvas in Object.FindObjectsByType<Canvas>())
                if (canvas.name == "HUD Canvas") hud = canvas;

            // Tapis de luxe propre (motif et éclat), puis sale.
            game.Reputation.Restore(10);
            yield return Accept("order_palace");
            var luxuryGrid = game.Rug.Grid;
            game.PlayerController.Teleport(new Vector3(0f, 0.06f, -1.9f), 0f, 38f);
            yield return new WaitForSeconds(0.4f);
            yield return Capture(camera, hud, Path.Combine(folder, "40_luxury_dirty.png"));
            for (int y = 0; y < luxuryGrid.Height; y++)
            for (int x = 0; x < luxuryGrid.Width; x++)
                luxuryGrid.SetCell(x, y, new RugCell());
            // Une bande usée par un brossage à sec, pour juger le rendu de l'usure.
            var worn = new BrushOperation { Table = game.Rug.DirtTable, Power = 0.1f, DeltaTime = 6f, FiberWearEnabled = true, WearPerSecond = 0.1f };
            luxuryGrid.ApplyRect(new RectInt(luxuryGrid.Width / 2 - 20, 0, 40, luxuryGrid.Height), 1f, ref worn);
            yield return new WaitForSeconds(0.3f);
            yield return Capture(camera, hud, Path.Combine(folder, "41_luxury_clean_worn_band.png"));
            game.Orders.Abandon();

            // Nouvelles saletés : chenil (poils, amas, peluches) et chantier (boue sèche).
            yield return Accept("order_kennel");
            AimAtRug(new Vector2(0.5f, 0.5f), 1.1f);
            yield return new WaitForSeconds(0.4f);
            yield return Capture(camera, hud, Path.Combine(folder, "42_kennel_pet_hair.png"));
            AimAtRug(new Vector2(0.35f, 0.6f), 0.35f);
            yield return new WaitForSeconds(0.3f);
            yield return Capture(camera, hud, Path.Combine(folder, "42b_kennel_pet_hair_close.png"));
            game.Orders.Abandon();
            yield return Accept("order_site");
            AimAtRug(new Vector2(0.5f, 0.5f), 1.1f);
            yield return new WaitForSeconds(0.4f);
            yield return Capture(camera, hud, Path.Combine(folder, "43_site_dried_mud.png"));
            game.Orders.Abandon();

            // Boutique (onglet produits) et RugDex.
            game.Wallet.Wallet.Add(400);
            Assert.IsTrue(game.ShopUI.Open());
            game.ShopUI.ShowTab(true);
            yield return null;
            yield return Capture(camera, hud, Path.Combine(folder, "44_shop_products.png"));
            game.ShopUI.Close();
            Assert.IsTrue(game.GameState.Pause());
            yield return null;
            // Progression réaliste : deux tapis connus, dont le tapis de soie déjà livré une fois.
            game.RugDexService.Book.Restore(new[] { new RugDexRecord { id = "classic", discovered = true } });
            game.RugDexService.Book.RecordDelivery("luxury", OrderGrade.A);
            Assert.IsTrue(game.RugDexUI.Open());
            game.RugDexUI.Select(game.RugDexService.Catalog.rugs.FindIndex(r => r.id == "luxury"));
            yield return null;
            yield return Capture(camera, hud, Path.Combine(folder, "45_rugdex_luxury.png"));
            game.RugDexUI.Select(game.RugDexService.Catalog.rugs.FindIndex(r => r.id == "shag"));
            yield return null;
            yield return Capture(camera, hud, Path.Combine(folder, "46_rugdex_unknown.png"));
            game.RugDexUI.Close();
            game.GameState.Resume();
            Assert.IsTrue(game.OrderBoard.Open());
            yield return null;
            yield return Capture(camera, hud, Path.Combine(folder, "47_order_board.png"));
            var orderScroll = game.OrderBoard.Rows[0].GetComponentInParent<ScrollRect>();
            Assert.IsNotNull(orderScroll.verticalScrollbar, "Barre de défilement visible.");
            orderScroll.verticalNormalizedPosition = 0f;
            yield return null;
            yield return Capture(camera, hud, Path.Combine(folder, "48_order_board_bottom.png"));
            game.OrderBoard.Close();
        }

        static IEnumerator Capture(Camera camera, Canvas canvas, string path)
        {
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = rt;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 0.2f;
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
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            camera.targetTexture = null;
            rt.Release();
            Object.Destroy(rt);
        }
    }
}
