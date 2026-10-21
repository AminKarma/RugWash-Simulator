using NUnit.Framework;
using RugWash.Economy;
using RugWash.Player;
using RugWash.Save;
using UnityEngine;

namespace RugWash.Tests
{
    /// <summary>V0.4.2 : règles pures (choix du micro, 5e emplacement d'inventaire).</summary>
    public class V042RulesTests
    {
        static readonly string[] Mics = { "Micro USB", "Casque" };

        [Test]
        public void InputDevice_CyclesDefaultThenEachPluggedMic()
        {
            Assert.AreEqual("Micro USB", SettingsManager.StepInputDevice(string.Empty, Mics, 1));
            Assert.AreEqual("Casque", SettingsManager.StepInputDevice("Micro USB", Mics, 1));
            Assert.AreEqual(string.Empty, SettingsManager.StepInputDevice("Casque", Mics, 1), "Retour au micro par défaut.");
            Assert.AreEqual("Casque", SettingsManager.StepInputDevice(string.Empty, Mics, -1));
            Assert.AreEqual(string.Empty, SettingsManager.StepInputDevice(string.Empty, new string[0], 1), "Aucun micro : par défaut seulement.");
            // Micro choisi puis débranché : on repart du début de la liste actuelle.
            Assert.AreEqual("Micro USB", SettingsManager.StepInputDevice("Ancien micro", Mics, 1));
        }

        [Test]
        public void InputDevice_LabelSaysDefaultOrUnplugged()
        {
            Assert.AreEqual("Par défaut (Windows)", SettingsManager.InputDeviceLabel(string.Empty, Mics));
            Assert.AreEqual("Casque", SettingsManager.InputDeviceLabel("Casque", Mics));
            StringAssert.Contains("débranché", SettingsManager.InputDeviceLabel("Ancien micro", Mics));
        }

        [Test]
        public void InputDevice_IsSavedInTheSettings()
        {
            var data = new SettingsData { voiceInputDevice = "Casque" };
            var back = JsonUtility.FromJson<SettingsData>(JsonUtility.ToJson(data));
            Assert.AreEqual("Casque", back.voiceInputDevice);
        }

        [Test]
        public void ExistingSave_KeepsTheStandardBrush_ForThePlayerAndEachGuest()
        {
            // Sauvegarde v6 (avant la V0.4.2) : la brosse standard faisait partie du kit de départ.
            string json = "{\"version\":6,\"profileId\":\"p\",\"money\":120,\"upgrades\":[\"up_tool_vacuum\"]," +
                          "\"members\":[{\"key\":\"g\",\"name\":\"Invité\",\"upgrades\":[\"up_tool_rinse\"],\"carried\":[\"brush\"]}]}";
            Assert.AreEqual(SaveMigration.Outcome.Migrated, SaveMigration.TryParse(json, out var data));
            CollectionAssert.Contains(data.upgrades, "up_tool_brush", "Partie existante : brosse standard gardée.");
            CollectionAssert.Contains(data.upgrades, "up_tool_vacuum", "Achats conservés.");
            Assert.AreEqual(120, data.money);
            CollectionAssert.Contains(data.members[0].upgrades, "up_tool_brush", "Fiche de l'invité : brosse gardée.");
            CollectionAssert.Contains(data.members[0].carried, "brush");
            Assert.AreEqual(1, data.upgrades.FindAll(id => id == "up_tool_brush").Count, "Aucun doublon.");
            // Relue une seconde fois (version actuelle) : rien n'est ajouté.
            string again = JsonUtility.ToJson(data);
            Assert.AreEqual(SaveMigration.Outcome.UpToDate, SaveMigration.TryParse(again, out var reread));
            Assert.AreEqual(1, reread.upgrades.FindAll(id => id == "up_tool_brush").Count);
            // Nouvelle partie : rien d'accordé ; une ancienne sauvegarde sans progression devient une nouvelle partie.
            CollectionAssert.DoesNotContain(new SaveData().upgrades, "up_tool_brush");
            Assert.AreEqual(SaveMigration.Outcome.Migrated, SaveMigration.TryParse("{\"version\":6,\"profileId\":\"p\"}", out var empty));
            CollectionAssert.DoesNotContain(empty.upgrades, "up_tool_brush");
        }

        [Test]
        public void InventorySlot_AddsOneSlot_AtMostFive()
        {
            var slot = ScriptableObject.CreateInstance<UpgradeDefinition>();
            slot.id = "up_inventory_slot";
            slot.effect = UpgradeEffect.InventorySlot;
            slot.value = 1f;
            Assert.AreEqual(4, PlayerInventory.CapacityFor(EquipmentStats.Compute(40f, 4f, 12f, new UpgradeDefinition[0]).ExtraInventorySlots));
            Assert.AreEqual(5, PlayerInventory.CapacityFor(EquipmentStats.Compute(40f, 4f, 12f, new[] { slot }).ExtraInventorySlots));
            Assert.AreEqual(5, PlayerInventory.CapacityFor(3), "Jamais plus de 5 emplacements.");
            Assert.IsFalse(SessionEconomyRules.IsDroppable(slot), "L'emplacement acheté ne tombe pas et ne se revend pas.");
            Object.DestroyImmediate(slot);
        }
    }
}
