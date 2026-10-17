using NUnit.Framework;
using RugWash.Audio;
using RugWash.Cleaning;
using RugWash.Economy;
using RugWash.Player;
using RugWash.Save;
using UnityEngine;

namespace RugWash.Tests
{
    /// <summary>
    /// Update v0.1.2, jalon 5 (règles pures) : effets des machines de l'atelier (jamais un nettoyage complet), réputation
    /// requise à l'achat, palier de décor, migration v3 → v4, personnalisation (format réseau), glissade, musique.
    /// </summary>
    public class BaseV012Tests
    {
        [Test]
        public void Blower_RemovesMoistureOnly()
        {
            var cell = new RugCell { Moisture = 0.5f, Stain = 0.3f, Dust = 0.2f, Residue = 0.1f, Foam = 0.2f };
            var op = new MachineDryOperation { Amount = 0.1f };
            op.Apply(ref cell, 1f);
            Assert.AreEqual(0.4f, cell.Moisture, 1e-6f);
            Assert.AreEqual(0.1f, op.Removed, 1e-6f);
            Assert.AreEqual(0.3f, cell.Stain);
            Assert.AreEqual(0.2f, cell.Dust);
            Assert.AreEqual(0.1f, cell.Residue, 1e-6f, "Ni résidus…");
            Assert.AreEqual(0.2f, cell.Foam, 1e-6f, "…ni mousse : il sèche seulement.");
        }

        [Test]
        public void DustPlate_ShakesDryDustOnly()
        {
            var dry = new RugCell { Dust = 0.4f, Stain = 0.3f, Loosened = 0.2f };
            var op = new ShakeOperation { Fraction = 0.5f };
            op.Apply(ref dry, 1f);
            Assert.AreEqual(0.2f, dry.Dust, 1e-6f);
            Assert.AreEqual(0.3f, dry.Stain, 1e-6f, "Les taches restent.");
            Assert.AreEqual(0.2f, dry.Loosened, 1e-6f, "La boue détachée aussi.");
            var wet = new RugCell { Dust = 0.4f, Moisture = 0.5f };
            var onWet = new ShakeOperation { Fraction = 0.5f };
            onWet.Apply(ref wet, 1f);
            Assert.AreEqual(0.4f, wet.Dust, 1e-6f, "Tapis mouillé : la poussière colle.");
        }

        [Test]
        public void PreWet_WetsAndSoaks_ButRemovesNothing()
        {
            var table = new DirtTypeTable();
            var cell = new RugCell { Stain = 0.4f, StainType = DirtType.Drink };
            var op = new SoakOperation { Table = table, WetTarget = 0.55f, WettingStep = 1f, Loosen = 0.2f };
            float dirtBefore = cell.Dirt;
            op.Apply(ref cell, 1f);
            Assert.AreEqual(0.55f, cell.Moisture, 1e-5f, "Humidité visée, pas plus.");
            Assert.Greater(cell.Loosened, 0f, "Une part des taches trempe et se détache.");
            Assert.Less(cell.Stain, 0.4f);
            Assert.AreEqual(dirtBefore, cell.Dirt, 1e-5f, "Rien n'est retiré : il faut brosser et rincer.");
        }

        [Test]
        public void Purchases_RespectRequiredReputation()
        {
            var upgrade = ScriptableObject.CreateInstance<UpgradeDefinition>();
            upgrade.id = "up_machine_blower";
            upgrade.price = 500;
            upgrade.requiredReputation = 8;
            var owned = new System.Collections.Generic.HashSet<string>();
            Assert.AreEqual(PurchaseResult.ReputationTooLow, ShopRules.Check(upgrade, owned, new Wallet(9999), 7));
            Assert.AreEqual(PurchaseResult.Purchased, ShopRules.Check(upgrade, owned, new Wallet(9999), 8));
            Assert.AreEqual(PurchaseResult.Purchased, ShopRules.Check(upgrade, owned, new Wallet(9999)), "Sans réputation fournie : pas de condition.");
            var career = new PlayerCareer { Money = 9999, Reputation = 3 };
            Assert.AreEqual(PurchaseResult.ReputationTooLow, CareerRules.TryBuyUpgrade(career, upgrade), "En ligne, l'hôte applique la même règle.");
            Assert.AreEqual(9999, career.Money);
            Assert.AreEqual("Réputation insuffisante", ShopRules.Describe(PurchaseResult.ReputationTooLow));
            Object.DestroyImmediate(upgrade);
        }

        [Test]
        public void WorkshopTier_IsTheHighestOwned()
        {
            var t1 = ScriptableObject.CreateInstance<UpgradeDefinition>();
            t1.effect = UpgradeEffect.WorkshopTier;
            t1.value = 1f;
            var t3 = ScriptableObject.CreateInstance<UpgradeDefinition>();
            t3.effect = UpgradeEffect.WorkshopTier;
            t3.value = 3f;
            Assert.AreEqual(0, EquipmentStats.Compute(40f, 4f, 12f, new UpgradeDefinition[0]).WorkshopTier, "Départ : cagibi.");
            Assert.AreEqual(3, EquipmentStats.Compute(40f, 4f, 12f, new[] { t3, t1 }).WorkshopTier);
            Assert.IsTrue(t1.IsWorkshopItem);
            Object.DestroyImmediate(t1);
            Object.DestroyImmediate(t3);
        }

        [Test]
        public void Migration_V3WithProgress_KeepsTheTidyWorkshop()
        {
            Assert.AreEqual(SaveMigration.Outcome.Migrated, SaveMigration.TryParse("{\"version\":3,\"profileId\":\"p\",\"money\":10}", out var started));
            Assert.AreEqual(SaveMigration.CurrentVersion, started.version);
            CollectionAssert.Contains(started.upgrades, "up_base_1", "Une partie commencée garde l'atelier qu'elle avait.");
            Assert.AreEqual(SaveMigration.Outcome.Migrated, SaveMigration.TryParse("{\"version\":3,\"profileId\":\"p\"}", out var fresh));
            CollectionAssert.DoesNotContain(fresh.upgrades, "up_base_1", "Nouvelle partie : le cagibi.");
            Assert.AreEqual(CharacterPalette.Default, fresh.character, "Apparence d'origine par défaut.");
        }

        [Test]
        public void CharacterLook_RoundTrips_AndWrapsOutOfRangeChoices()
        {
            var look = new CharacterLook { outfit = 4, cap = 0, gloves = 2 };
            Assert.AreEqual(look, CharacterLook.Unpack(look.Pack()));
            var wrapped = CharacterPalette.Clamp(new CharacterLook { outfit = -1, cap = 99, gloves = 3 });
            Assert.AreEqual(CharacterPalette.Outfits.Length - 1, wrapped.outfit);
            Assert.That(wrapped.cap, Is.InRange(0, CharacterPalette.Caps.Length - 1));
            Assert.AreEqual(0, wrapped.gloves);
            Assert.AreEqual("Aucune", CharacterPalette.Caps[0].name, "Option « pas de casquette ».");
        }

        [Test]
        public void Slip_OnlyWhenSprintingFastOnThickFoam()
        {
            Assert.IsTrue(PlayerKnockdown.ShouldSlip(true, true, 5f, 0.6f, 4f, 0.45f));
            Assert.IsFalse(PlayerKnockdown.ShouldSlip(false, true, 5f, 0.6f, 4f, 0.45f), "En marchant : pas de chute.");
            Assert.IsFalse(PlayerKnockdown.ShouldSlip(true, true, 3f, 0.6f, 4f, 0.45f));
            Assert.IsFalse(PlayerKnockdown.ShouldSlip(true, true, 5f, 0.2f, 4f, 0.45f), "Mousse légère : on tient debout.");
            Assert.IsFalse(PlayerKnockdown.ShouldSlip(true, false, 5f, 0.6f, 4f, 0.45f));
        }

        [Test]
        public void Music_StaysUnderGameplaySounds()
        {
            Assert.Less(MusicPlayer.MaxGain, 0.5f, "Musique à 100 % : moins de la moitié du volume plein.");
            Assert.Less(MusicPlayer.PausedFactor, 1f);
        }
    }
}
