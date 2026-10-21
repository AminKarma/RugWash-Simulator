using System.Collections.Generic;
using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Economy;
using RugWash.Player;
using RugWash.Pranks;
using UnityEngine;

namespace RugWash.Tests
{
    /// <summary>
    /// Update v0.1.2, jalon 6 (règles pures) : salissures de farce nettoyables, tache plafonnée, conditions d'usage
    /// (achat, recharge, coût), portée, plafond par tapis, visée d'une silhouette, motif des éclaboussures, gouttes à
    /// l'écran (durée, fondu), relevé rapide, catégorie de boutique, emplacements des gadgets sur les silhouettes.
    /// </summary>
    public class PrankV012Tests
    {
        [Test]
        public void PrankDirt_IsRealAndCleanable_WithTheUsualTools()
        {
            foreach (var type in new[] { DirtType.Urine, DirtType.FakeBlood })
            {
                Assert.IsTrue(DirtTypes.IsPrank(type));
                Assert.Less((int)type, DirtTypes.Count, "Type reconnu par les formats réseau et de sauvegarde.");
                Assert.IsFalse(DirtTypes.IsLooseDry(type), "Une tache fixée : à détacher puis rincer.");
                var response = DirtTypeTable.DefaultResponse(type);
                Assert.Greater(response.brushSoap, 0.8f, "Détergent + brosse la détachent.");
                Assert.Greater(response.rinse, 0.25f, "Le rinçage l'évacue.");
                Assert.AreEqual(0f, response.vacuum, "L'aspirateur ne suffit pas.");
                Assert.AreNotEqual(DirtTypeNames.Default(DirtType.None), DirtTypeNames.Default(type));
            }
            Assert.IsFalse(DirtTypes.IsPrank(DirtType.Drink));
            Assert.Greater(DirtTypeTable.DefaultColor(DirtType.FakeBlood).r, 0.5f, "Faux sang : rouge vif de dessin animé.");
        }

        [Test]
        public void PrankStain_AddsATypedStain_Wets_AndRespectsTheCellCap()
        {
            var cell = new RugCell { Stain = 0.6f, StainType = DirtType.Mud };
            var pee = new PrankStainOperation { Type = DirtType.Urine, Amount = 0.5f, Moisture = 0.7f, CellCap = 0.85f };
            pee.Apply(ref cell, 1f);
            Assert.AreEqual(0.85f, cell.Stain, 1e-5f, "Plafonnée : aucune zone impossible à nettoyer.");
            Assert.AreEqual(0.25f, pee.Added, 1e-5f);
            Assert.AreEqual(0.7f, cell.Moisture, 1e-5f, "Le pipi mouille.");
            Assert.AreEqual(DirtType.Mud, cell.StainType, "Petite quantité sur une grosse tache : le type dominant reste.");

            var clean = new RugCell();
            var blood = new PrankStainOperation { Type = DirtType.FakeBlood, Amount = 0.6f, CellCap = 0.85f };
            blood.Apply(ref clean, 0.5f);
            Assert.AreEqual(DirtType.FakeBlood, clean.StainType);
            Assert.AreEqual(0.3f, clean.Stain, 1e-5f, "Bord de la tache : quantité pondérée.");
            Assert.AreEqual(0f, clean.Moisture, "Le faux sang ne mouille pas.");
        }

        [Test]
        public void CheckUse_RequiresPurchase_Cooldowns_AndMoneyForTheCost()
        {
            Assert.AreEqual(PrankResult.NotOwned, PrankRules.CheckUse(false, 100f, 0f, 0f, 9999, 0));
            Assert.AreEqual(PrankResult.Cooldown, PrankRules.CheckUse(true, 10f, 12f, 0f, 9999, 0), "Recharge du gadget.");
            Assert.AreEqual(PrankResult.Cooldown, PrankRules.CheckUse(true, 10f, 0f, 11f, 9999, 0), "Délai entre deux farces, tous gadgets.");
            Assert.AreEqual(PrankResult.InsufficientFunds, PrankRules.CheckUse(true, 10f, 0f, 0f, 59, 60));
            Assert.AreEqual(PrankResult.Done, PrankRules.CheckUse(true, 12f, 12f, 11f, 60, 60));
            foreach (PrankResult result in System.Enum.GetValues(typeof(PrankResult)))
                if (result != PrankResult.Done) Assert.IsNotEmpty(PrankRules.Describe(result, "Batte", 3f, 60), result.ToString());
            StringAssert.Contains("3 s", PrankRules.Describe(PrankResult.Cooldown, "Batte", 2.2f));
            StringAssert.Contains("60 €", PrankRules.Describe(PrankResult.InsufficientFunds, cost: 60));
        }

        [Test]
        public void Range_Cap_AndSplatPattern()
        {
            Assert.IsTrue(PrankRules.InRange(Vector3.zero, new Vector3(0f, 0f, 2.9f), 2.4f, 0.75f), "Tolérance de latence côté hôte.");
            Assert.IsFalse(PrankRules.InRange(Vector3.zero, new Vector3(0f, 0f, 3.3f), 2.4f, 0.75f));
            Assert.AreEqual(15f - 4f, PrankRules.CapRemaining(4f, 100f, 0.15f, 6f), 1e-4f);
            Assert.AreEqual(2f, PrankRules.CapRemaining(4f, 10f, 0.15f, 6f), 1e-4f, "Tapis presque propre : plafond minimal.");
            Assert.AreEqual(0f, PrankRules.CapRemaining(20f, 100f, 0.15f, 6f));

            Assert.AreEqual(Vector2.zero, PrankRules.SplatOffset(3, 0, 0.35f), "Première tache au point visé.");
            var seen = new HashSet<Vector2>();
            for (int i = 1; i < 9; i++)
            {
                var offset = PrankRules.SplatOffset(3, i, 0.35f);
                Assert.LessOrEqual(offset.magnitude, 0.35f + 1e-4f);
                Assert.AreEqual(offset, PrankRules.SplatOffset(3, i, 0.35f), "Même motif chez l'hôte et les invités.");
                Assert.IsTrue(seen.Add(offset), "Taches distinctes.");
            }
        }

        [Test]
        public void PickPlayer_TakesTheNearestSilhouetteOnTheRay_InRange()
        {
            var ray = new Ray(new Vector3(0f, 1.6f, 0f), Vector3.forward);
            var feet = new List<Vector3> { new Vector3(0.1f, 0f, 3f), new Vector3(-0.2f, 0f, 1.8f), new Vector3(1.5f, 0f, 1f), new Vector3(0f, 0f, -1.5f) };
            Assert.AreEqual(1, PrankRules.PickPlayer(ray, feet, 2.5f, 0.45f), "La plus proche sur la ligne de visée.");
            Assert.AreEqual(-1, PrankRules.PickPlayer(ray, new List<Vector3> { feet[0] }, 2.5f, 0.45f), "Trop loin.");
            Assert.AreEqual(-1, PrankRules.PickPlayer(ray, new List<Vector3> { feet[2] }, 2.5f, 0.45f), "À côté de la visée.");
            Assert.AreEqual(-1, PrankRules.PickPlayer(ray, new List<Vector3> { feet[3] }, 2.5f, 0.45f), "Derrière soi.");
            var down = new Ray(new Vector3(0f, 1.6f, 0f), new Vector3(0f, -0.6f, 1f).normalized);
            Assert.AreEqual(0, PrankRules.PickPlayer(down, new List<Vector3> { new Vector3(0f, 0f, 1.6f) }, 2.5f, 0.45f), "Visée vers le bas du corps.");
        }

        [Test]
        public void ScreenSplash_FadesIn_HoldsCapped_FadesOut_ThenEnds()
        {
            Assert.AreEqual(0f, PrankRules.SplashAlpha(0f, 6f, 2.5f, 0.65f), "Entrée en fondu.");
            Assert.AreEqual(0.65f, PrankRules.SplashAlpha(1f, 6f, 2.5f, 0.65f), 1e-5f);
            Assert.AreEqual(0.65f * 0.5f, PrankRules.SplashAlpha(4.75f, 6f, 2.5f, 0.65f), 1e-4f, "Fondu de sortie.");
            Assert.AreEqual(0f, PrankRules.SplashAlpha(6f, 6f, 2.5f, 0.65f), "Durée limitée.");
            Assert.AreEqual(1f, PrankRules.SplashAlpha(1f, 6f, 2.5f, 3f), "Jamais plus que 100 %.");
            var tuning = ScriptableObject.CreateInstance<PrankTuning>();
            Assert.LessOrEqual(tuning.splashIntensity, 0.85f, "La vue n'est jamais masquée.");
            Assert.LessOrEqual(tuning.splashSeconds, 10f);
            Object.DestroyImmediate(tuning);
        }

        [Test]
        public void Tuning_Protects_AndPranksStayExpensiveButBounded()
        {
            var tuning = ScriptableObject.CreateInstance<PrankTuning>();
            foreach (var gadget in PrankTuning.All)
            {
                var settings = tuning.Get(gadget);
                Assert.AreEqual(gadget, settings.kind);
                Assert.IsNotEmpty(settings.upgradeId);
                Assert.GreaterOrEqual(settings.cooldownSeconds, 10f, $"{gadget} : pas de rafale.");
                // V0.4.2 : la dynamite ne tache pas ; elle soulève de la vraie poussière sur les tapis proches.
                if (gadget == PrankGadget.Dynamite) Assert.IsFalse(settings.affectsRug);
                else Assert.IsTrue(DirtTypes.IsPrank(settings.stainType));
            }
            Assert.IsTrue(tuning.blaster.affectsPlayers, "Mission de corrections : le fusil met un joueur KO.");
            Assert.AreEqual(5f, tuning.blasterKnockSeconds, 1e-4f, "KO de 5 s, puis relevé automatique.");
            Assert.LessOrEqual(tuning.blasterImpulse, 12f, "Élan exagéré mais borné.");
            Assert.Greater(tuning.blaster.costPerUse, 0, "Coût par tir.");
            Assert.Greater(tuning.knockProtectionSeconds, tuning.knockSeconds + 5f, "Protection après un coup de batte.");
            Assert.LessOrEqual(tuning.knockSeconds, 3f, "Chute courte.");
            Assert.Greater(tuning.globalCooldownSeconds, 0f);
            Assert.Less(tuning.rugCapFraction, 0.5f, "Les farces ne ruinent jamais un tapis.");
            Assert.IsNull(tuning.Get(PrankGadget.None));
            Object.DestroyImmediate(tuning);
        }

        [Test]
        public void QuickRecover_AfterAShortDelay_OnTheGroundOnly()
        {
            Assert.IsFalse(PlayerKnockdown.CanQuickRecover(true, 0.3f, 0.5f), "Trop tôt.");
            Assert.IsTrue(PlayerKnockdown.CanQuickRecover(true, 0.6f, 0.5f));
            Assert.IsFalse(PlayerKnockdown.CanQuickRecover(false, 2f, 0.5f), "Encore en train de tomber.");
        }

        [Test]
        public void PrankItems_HaveTheirOwnShopTab_AndAvatarSlots()
        {
            var upgrade = ScriptableObject.CreateInstance<UpgradeDefinition>();
            upgrade.effect = UpgradeEffect.PrankGadget;
            Assert.IsTrue(upgrade.IsPrankItem);
            Assert.IsFalse(upgrade.IsWorkshopItem);
            upgrade.effect = UpgradeEffect.UnlockTool;
            Assert.IsFalse(upgrade.IsPrankItem);
            Object.DestroyImmediate(upgrade);

            var slots = new HashSet<byte>();
            foreach (var gadget in PrankTuning.All)
            {
                byte slot = PrankTuning.AvatarSlot(gadget);
                Assert.Greater(slot, RugWash.Player.RugWashInputActions.ToolbarSlotCount, "Après les onze outils.");
                Assert.IsTrue(slots.Add(slot));
            }
            Assert.AreEqual(0, PrankTuning.AvatarSlot(PrankGadget.None));
        }
    }
}
