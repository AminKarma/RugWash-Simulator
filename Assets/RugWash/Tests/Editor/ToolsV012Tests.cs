using System.Collections.Generic;
using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Economy;
using RugWash.Network;
using RugWash.Player;
using RugWash.Save;
using UnityEngine;

namespace RugWash.Tests
{
    /// <summary>
    /// Update v0.1.2, jalon 4 (règles pures) : raclette (ramassage et transport sans création de matière), accessoire
    /// anti-poils, bottes, robot achetable, monobrosse selon l'état des fibres, migration v2 → v3, format réseau, touches.
    /// </summary>
    public class ToolsV012Tests
    {
        static RugCleaningGrid WetPad(out float water, out float mud)
        {
            var grid = new RugCleaningGrid(40, 20, new Vector2(4f, 2f));
            var soil = new DepositOperation { Type = DirtType.Mud, Mud = 0.3f, Water = 0.5f, Foam = 0.2f };
            grid.ApplyRect(new RectInt(5, 5, 6, 10), 1f, ref soil);
            var stain = new DepositOperation { Type = DirtType.Grease, Debris = 0.2f };
            grid.ApplyRect(new RectInt(5, 5, 6, 10), 1f, ref stain);
            grid.RecomputeTotals();
            water = grid.TotalMoisture;
            mud = SumLoosened(grid);
            return grid;
        }

        static float SumLoosened(RugCleaningGrid grid)
        {
            float sum = 0f;
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
                sum += grid.GetCell(x, y).Loosened;
            return sum;
        }

        [Test]
        public void Gather_TakesWaterFoamAndLooseMud_ButNeverFixedDirt()
        {
            var grid = WetPad(out _, out _);
            var before = grid.GetCell(7, 8);
            var gather = new GatherOperation { Efficiency = 0.6f };
            grid.ApplyRect(new RectInt(5, 5, 6, 10), 1f, ref gather);
            var after = grid.GetCell(7, 8);
            Assert.AreEqual(before.Moisture * 0.4f, after.Moisture, 1e-5f);
            Assert.AreEqual(before.Foam * 0.4f, after.Foam, 1e-5f);
            Assert.AreEqual(before.Loosened * 0.4f, after.Loosened, 1e-5f);
            Assert.AreEqual(before.Stain, after.Stain, 1e-6f, "Les taches fixées ne se raclent pas.");
            Assert.AreEqual(before.Dust, after.Dust, 1e-6f);
            Assert.AreEqual(60f, gather.WeightSum, 1e-4f);
            Assert.AreEqual(before.Moisture * 0.6f * 60f, gather.Water, 1e-3f);
        }

        [Test]
        public void Squeegee_Transport_MovesWaterAndMudAhead_WithoutCreatingAny()
        {
            var grid = WetPad(out float water, out float mud);
            // Ramassage sous la lame, puis dépôt sur une zone de même forme juste devant (règle de la raclette).
            var gather = new GatherOperation { Efficiency = 0.8f };
            grid.ApplyRect(new RectInt(5, 5, 6, 10), 1f, ref gather);
            var push = new DepositOperation
            {
                Type = DirtType.Mud, Water = gather.Water / gather.WeightSum, Foam = gather.Foam / gather.WeightSum, Mud = gather.Mud / gather.WeightSum,
            };
            grid.ApplyRect(new RectInt(11, 5, 6, 10), 1f, ref push);
            grid.RecomputeTotals();
            Assert.AreEqual(water, grid.TotalMoisture, water * 1e-4f, "L'eau est déplacée, pas créée ni détruite.");
            Assert.AreEqual(mud, SumLoosened(grid), mud * 1e-4f);
            Assert.Greater(grid.GetCell(13, 8).Moisture, grid.GetCell(7, 8).Moisture, "L'eau est passée devant la lame.");
        }

        [Test]
        public void Deposit_ReportsWhatSaturatedCellsCannotHold()
        {
            var cell = new RugCell { Moisture = 0.8f, Foam = 0.9f, Loosened = 1.9f };
            var push = new DepositOperation { Water = 0.5f, Foam = 0.5f, Mud = 0.5f };
            push.Apply(ref cell, 1f);
            Assert.AreEqual(1f, cell.Moisture, 1e-6f);
            Assert.AreEqual(0.3f, push.WaterOverflow, 1e-5f, "Le surplus est mesuré (la raclette le reporte plus loin).");
            Assert.AreEqual(0.4f, push.FoamOverflow, 1e-5f);
            Assert.AreEqual(0.4f, push.MudOverflow, 1e-5f);
        }

        [Test]
        public void FiberAccessory_PicksUpHairFaster_AndOnlyHair()
        {
            var table = new DirtTypeTable();
            float Removed(DirtType type, float bonus)
            {
                var cell = new RugCell { Dust = 0.5f, DustType = type };
                var vacuum = new VacuumOperation { Table = table, Power = 0.4f, DeltaTime = 0.1f, FiberBonus = bonus };
                vacuum.Apply(ref cell, 1f);
                return vacuum.Removed;
            }
            Assert.Greater(Removed(DirtType.PetHair, 2f), Removed(DirtType.PetHair, 1f) * 1.8f);
            Assert.Greater(Removed(DirtType.Lint, 2f), Removed(DirtType.Lint, 1f) * 1.8f);
            Assert.AreEqual(Removed(DirtType.Dust, 1f), Removed(DirtType.Dust, 2f), 1e-6f, "La poussière ordinaire n'est pas concernée.");
            Assert.IsTrue(DirtTypes.IsFiber(DirtType.HairDebris));
            Assert.IsFalse(DirtTypes.IsFiber(DirtType.DustClumps));
        }

        [Test]
        public void Upgrades_BootsAccessoryAndRobot_HaveRealEffects()
        {
            var boots = ScriptableObject.CreateInstance<UpgradeDefinition>();
            boots.effect = UpgradeEffect.FootprintTransfer;
            boots.value = 0.3f;
            var accessory = ScriptableObject.CreateInstance<UpgradeDefinition>();
            accessory.effect = UpgradeEffect.FiberPickup;
            accessory.value = 2f;
            accessory.targetToolId = "vacuum";
            var robot = ScriptableObject.CreateInstance<UpgradeDefinition>();
            robot.effect = UpgradeEffect.WorkshopEquipment;
            robot.targetToolId = UpgradeManager.RobotMachineId;

            var none = EquipmentStats.Compute(40f, 4f, 12f, new UpgradeDefinition[0]);
            Assert.AreEqual(1f, none.FootprintTransfer);
            Assert.AreEqual(1f, none.FiberMultiplier("vacuum"));
            Assert.IsFalse(none.HasMachine(UpgradeManager.RobotMachineId), "Nouvelle partie : pas de robot.");

            var all = EquipmentStats.Compute(40f, 4f, 12f, new[] { boots, accessory, robot });
            Assert.AreEqual(0.3f, all.FootprintTransfer, 1e-6f, "Les bottes réduisent le transfert sans l'annuler.");
            Assert.AreEqual(2f, all.FiberMultiplier("vacuum"));
            Assert.AreEqual(1f, all.FiberMultiplier("brush"));
            Assert.IsTrue(all.HasMachine(UpgradeManager.RobotMachineId));
            Assert.IsFalse(all.IsUnlocked(UpgradeManager.RobotMachineId), "Le robot n'est pas un outil en main.");
            foreach (var asset in new Object[] { boots, accessory, robot }) Object.DestroyImmediate(asset);
        }

        [Test]
        public void RotaryScrubber_IsWeakOnDryFibers_AndFullOnWetOrSoapyOnes()
        {
            Assert.AreEqual(0.35f, RotaryScrubberTool.Efficiency(new RugCell(), 0.35f), 1e-6f);
            Assert.AreEqual(1f, RotaryScrubberTool.Efficiency(new RugCell { Moisture = 0.5f }, 0.35f), 1e-6f);
            Assert.AreEqual(1f, RotaryScrubberTool.Efficiency(new RugCell { Foam = 0.4f }, 0.35f), 1e-6f);
            float half = RotaryScrubberTool.Efficiency(new RugCell { Moisture = 0.2f }, 0.35f);
            Assert.That(half, Is.InRange(0.35f, 1f));
        }

        [Test]
        public void Migration_V2WithProgress_KeepsTheRobot_NewGameDoesNot()
        {
            Assert.AreEqual(SaveMigration.Outcome.Migrated, SaveMigration.TryParse("{\"version\":2,\"profileId\":\"p\",\"money\":40}", out var started));
            Assert.AreEqual(SaveMigration.CurrentVersion, started.version);
            CollectionAssert.Contains(started.upgrades, "up_robot", "Le robot était gratuit : une partie commencée le garde.");
            Assert.AreEqual(SaveMigration.Outcome.Migrated, SaveMigration.TryParse("{\"version\":2,\"profileId\":\"p\"}", out var fresh));
            CollectionAssert.DoesNotContain(fresh.upgrades, "up_robot");
            Assert.AreEqual(SaveMigration.Outcome.Migrated, SaveMigration.TryParse("{\"version\":1,\"profileId\":\"p\",\"reputation\":3}", out var v1));
            CollectionAssert.Contains(v1.upgrades, "up_robot");
            CollectionAssert.Contains(v1.upgrades, "up_tool_vacuum");
        }

        [Test]
        public void SqueegeeAndFiberBonus_AreTransmissible_AndClamped()
        {
            var geometry = new SurfaceOpGeometry(Vector3.zero, 0.3f, 4f, 4f, Vector3.right, default);
            var gather = new GatherOperation { Efficiency = 0.6f, Water = 99f };
            var writer = new ByteWriter();
            Assert.IsTrue(SurfaceOperationCodec.TryWrite(writer, 1, 7u, geometry, ref gather));
            var reader = new ByteReader(writer.ToArray());
            Assert.AreEqual(SurfaceOpType.Gather, SurfaceOperationCodec.ReadHeader(ref reader).Type);
            Assert.AreEqual(0.6f, reader.Float(), 1e-6f, "Seul le réglage est transmis (pas les quantités ramassées).");

            var vacuum = new VacuumOperation { Power = 0.8f, DeltaTime = 0.02f, FiberBonus = 2f };
            writer.Reset();
            Assert.IsTrue(SurfaceOperationCodec.TryWrite(writer, 0, 1u, geometry, ref vacuum));
            reader = new ByteReader(writer.ToArray());
            SurfaceOperationCodec.ReadHeader(ref reader);
            Assert.AreEqual(0.8f, reader.Float(), 1e-6f);
            Assert.AreEqual(0.02f, reader.Float(), 1e-6f);
            Assert.AreEqual(2f, reader.Float(), 1e-6f);
        }

        [Test]
        public void ToolSlots_AreKeys1To9Then0_AndTheEleventhUsesTheWheel()
        {
            var labels = new List<string>();
            for (int slot = 1; slot <= RugWashInputActions.ToolbarSlotCount; slot++) labels.Add(RugWashInputActions.SlotKeyLabel(slot));
            CollectionAssert.AreEqual(new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "" }, labels);
            var asset = RugWashInputActions.Create();
            var tool10 = asset.FindAction("Gameplay/Tool10", true);
            Assert.AreEqual("<Keyboard>/0", tool10.bindings[0].path);
            Object.DestroyImmediate(asset);
        }
    }
}
