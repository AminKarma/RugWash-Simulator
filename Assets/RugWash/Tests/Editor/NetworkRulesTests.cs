using System;
using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Network;
using UnityEngine;

namespace RugWash.Tests
{
    /// <summary>Jalon 5 — règles pures du multijoueur : codecs, accusés, admission, adresses, messages.</summary>
    public class NetworkRulesTests
    {
        // ------------------------------------------------------------------ Tampons

        [Test]
        public void ByteBuffer_RoundTrips_AndRejectsTruncatedOrInvalidData()
        {
            var writer = new ByteWriter(4);
            writer.Byte(7); writer.Bool(true); writer.UShort(65000); writer.UInt(4000000000u); writer.ULong(ulong.MaxValue - 3);
            writer.Float(-1.25f); writer.Vector3(new Vector3(1f, 2f, 3f)); writer.String("Tapis à médaillon");
            var reader = new ByteReader(writer.ToArray());
            Assert.AreEqual(7, reader.Byte());
            Assert.IsTrue(reader.Bool());
            Assert.AreEqual(65000, reader.UShort());
            Assert.AreEqual(4000000000u, reader.UInt());
            Assert.AreEqual(ulong.MaxValue - 3, reader.ULong());
            Assert.AreEqual(-1.25f, reader.Float());
            Assert.AreEqual(new Vector3(1f, 2f, 3f), reader.Vector3());
            Assert.AreEqual("Tapis à médaillon", reader.String());
            Assert.AreEqual(0, reader.Remaining);

            var truncated = new ByteReader(new byte[] { 1, 2 });
            Assert.Throws<FormatException>(() => truncated.UInt());
            var nan = new ByteWriter();
            nan.Float(float.NaN);
            var nanReader = new ByteReader(nan.ToArray());
            Assert.Throws<FormatException>(() => nanReader.Float(), "Une valeur réseau non finie est refusée.");
        }

        // ------------------------------------------------------------------ Opérations

        [Test]
        public void Operations_AreSerialized_WithTheirParametersOnly_AndUnknownOnesAreRefused()
        {
            var geometry = new SurfaceOpGeometry(new Vector3(1f, 0.2f, -0.5f), 0.14f, 3.5f, 2.6f, Vector3.right, new RunoffProfile(0.6f, 0.7f, 0.35f, false));
            var brush = new BrushOperation { Power = 0.6f, DeltaTime = 0.016f, FiberWearEnabled = true, WearPerSecond = 0.05f, StubbornBonus = 2.2f, LatherPerSecond = 0.5f, Loosened = 99f };
            var writer = new ByteWriter();
            Assert.IsTrue(SurfaceOperationCodec.TryWrite(writer, 1, 42u, geometry, ref brush));

            var reader = new ByteReader(writer.ToArray());
            var header = SurfaceOperationCodec.ReadHeader(ref reader);
            Assert.AreEqual(1, header.SurfaceId);
            Assert.AreEqual(42u, header.Sequence);
            Assert.AreEqual(SurfaceOpType.Brush, header.Type);
            Assert.AreEqual(geometry.Point, header.Geometry.Point);
            Assert.AreEqual(2.6f, header.Geometry.Aspect, 1e-6f);
            Assert.AreEqual(0.7f, header.Geometry.Runoff.Foam, 1e-6f);

            foreach (var op in new object[] { new RinseOperation(), new VacuumOperation(), new SoapOperation { Product = 3 }, new ExtractOperation(), new SweepOperation(), new DepositOperation() })
            {
                writer.Reset();
                bool ok = op switch
                {
                    RinseOperation r => SurfaceOperationCodec.TryWrite(writer, 0, 1u, geometry, ref r),
                    VacuumOperation v => SurfaceOperationCodec.TryWrite(writer, 0, 1u, geometry, ref v),
                    SoapOperation s => SurfaceOperationCodec.TryWrite(writer, 0, 1u, geometry, ref s),
                    ExtractOperation e => SurfaceOperationCodec.TryWrite(writer, 0, 1u, geometry, ref e),
                    SweepOperation w => SurfaceOperationCodec.TryWrite(writer, 0, 1u, geometry, ref w),
                    DepositOperation d => SurfaceOperationCodec.TryWrite(writer, 0, 1u, geometry, ref d),
                    _ => false,
                };
                Assert.IsTrue(ok, $"{op.GetType().Name} transmissible.");
            }

            var aging = new SurfaceAgingOperation();
            writer.Reset();
            Assert.IsFalse(SurfaceOperationCodec.TryWrite(writer, 0, 1u, geometry, ref aging), "La simulation lente reste à l'hôte.");
            Assert.AreEqual(0, writer.Length);

            var forged = new byte[] { 0, 1, 0, 0, 0 };
            var forgedReader = new ByteReader(forged);
            Assert.Throws<FormatException>(() => SurfaceOperationCodec.ReadHeader(ref forgedReader), "Message tronqué refusé.");
        }

        [Test]
        public void ForgedOperationValues_AreClamped_ToWhatARealToolCanDo()
        {
            var geometry = new SurfaceOpGeometry(Vector3.zero, 50f, 0f, 99f, Vector3.forward, new RunoffProfile(5f, 5f, 500f, false));
            var vacuum = new VacuumOperation { Power = 1000f, DeltaTime = 60f };
            var writer = new ByteWriter();
            SurfaceOperationCodec.TryWrite(writer, 0, 1u, geometry, ref vacuum);
            var reader = new ByteReader(writer.ToArray());
            var header = SurfaceOperationCodec.ReadHeader(ref reader);
            Assert.LessOrEqual(header.Geometry.Radius, 2f);
            Assert.LessOrEqual(header.Geometry.Aspect, 6f);
            Assert.GreaterOrEqual(header.Geometry.Hardness, 0.5f);
            Assert.LessOrEqual(header.Geometry.Runoff.Dirt, 1f);
            Assert.LessOrEqual(header.Geometry.Runoff.Water, 10f);
        }

        // ------------------------------------------------------------------ Cellules

        [Test]
        public void CellCodec_RoundTripsARect_Quantized_AndCompressesLargeOnes()
        {
            var source = new RugCleaningGrid(40, 30, new Vector2(2f, 1.5f));
            var random = new System.Random(5);
            for (int y = 0; y < 30; y++)
            for (int x = 0; x < 40; x++)
                source.SetCell(x, y, new RugCell
                {
                    Dust = (float)random.NextDouble(), Stain = (float)random.NextDouble() * 0.5f, Loosened = (float)random.NextDouble() * 1.6f,
                    Foam = (float)random.NextDouble(), Moisture = (float)random.NextDouble(), Residue = (float)random.NextDouble() * 0.3f,
                    Wear = (float)random.NextDouble() * 0.1f, Adhesion = (float)random.NextDouble(), DustType = DirtType.PetHair, StainType = DirtType.Grease, Product = 2,
                });
            var target = new RugCleaningGrid(40, 30, new Vector2(2f, 1.5f));
            var writer = new ByteWriter();
            var rect = new RectInt(5, 4, 20, 12);
            SurfaceCellCodec.Write(writer, source, rect);
            var reader = new ByteReader(writer.ToArray());
            var applied = SurfaceCellCodec.Read(ref reader, target);
            Assert.AreEqual(rect, applied);
            for (int y = rect.yMin; y < rect.yMax; y++)
            for (int x = rect.xMin; x < rect.xMax; x++)
            {
                var a = source.GetCell(x, y);
                var b = target.GetCell(x, y);
                Assert.AreEqual(a.Dust, b.Dust, 0.5f / 255f + 1e-6f);
                Assert.AreEqual(a.Loosened, b.Loosened, 1f / 255f + 1e-6f);
                Assert.AreEqual(a.Wear, b.Wear, 0.5f / 255f + 1e-6f);
                Assert.AreEqual(a.Adhesion, b.Adhesion, 0.5f / 255f + 1e-6f);
                Assert.AreEqual(a.DustType, b.DustType);
                Assert.AreEqual(a.StainType, b.StainType);
                Assert.AreEqual(a.Product, b.Product);
            }
            Assert.AreEqual(default(RugCell).Dust, target.GetCell(0, 0).Dust, "Hors du rectangle : rien n'est écrit.");
            Assert.Less(writer.Length, rect.width * rect.height * SurfaceCellCodec.Channels, "Compressé au-delà de quelques centaines d'octets.");

            var outside = new ByteWriter();
            SurfaceCellCodec.Write(outside, new RugCleaningGrid(60, 60, Vector2.one), new RectInt(0, 0, 60, 60));
            var small = new ByteReader(outside.ToArray());
            Assert.Throws<FormatException>(() => SurfaceCellCodec.Read(ref small, target), "Une zone plus grande que la grille est refusée.");
        }

        [Test]
        public void AckTable_KeepsTheHighestSequencePerClient()
        {
            var table = new SurfaceAckTable();
            table.Record(5, 10);
            table.Record(5, 8);
            table.Record(9, 3);
            Assert.AreEqual(10u, table.LastApplied(5));
            var writer = new ByteWriter();
            table.Write(writer);
            var reader = new ByteReader(writer.ToArray());
            Assert.AreEqual(3u, SurfaceAckTable.ReadAckFor(ref reader, 9));
            var other = new ByteReader(writer.ToArray());
            Assert.AreEqual(0u, SurfaceAckTable.ReadAckFor(ref other, 77), "Client absent : aucun accusé.");
            table.Remove(5);
            Assert.AreEqual(0u, table.LastApplied(5));
        }

        // ------------------------------------------------------------------ Sessions

        [Test]
        public void Approval_ChecksVersionAndRoom_AndCleansTheName()
        {
            var payload = new ConnectionPayload("v1", "  <b>Ami</b>\n ").Encode();
            Assert.IsTrue(SessionRules.Approve(payload, "v1", 1, 4, out string name, out string reason));
            Assert.AreEqual("bAmi/b", name, "Balises et caractères de contrôle retirés.");
            Assert.IsFalse(SessionRules.Approve(new ConnectionPayload("v0", "Bob").Encode(), "v1", 1, 4, out _, out reason));
            Assert.That(reason, Does.Contain("Version"));
            Assert.IsFalse(SessionRules.Approve(new ConnectionPayload("v1", "Bob").Encode(), "v1", 4, 4, out _, out reason));
            Assert.That(reason, Does.Contain("complète"));
            Assert.IsFalse(SessionRules.Approve(new byte[] { 9 }, "v1", 1, 4, out _, out reason));
            Assert.AreEqual("Joueur", SessionRules.CleanName("   "));
            Assert.LessOrEqual(SessionRules.CleanName(new string('x', 80)).Length, SessionRules.MaxNameLength);
        }

        [Test]
        public void DirectAddresses_AreParsed_WithAnOptionalPort()
        {
            Assert.IsTrue(SessionRules.TryParseAddress("192.168.1.20:7778", 7777, out string address, out ushort port));
            Assert.AreEqual("192.168.1.20", address);
            Assert.AreEqual(7778, port);
            Assert.IsTrue(SessionRules.TryParseAddress("10.0.0.5", 7777, out _, out port));
            Assert.AreEqual(7777, port);
            Assert.IsTrue(SessionRules.TryParseAddress("localhost", 7777, out address, out _));
            Assert.AreEqual("127.0.0.1", address);
            Assert.IsFalse(SessionRules.TryParseAddress("", 7777, out _, out _));
            Assert.IsFalse(SessionRules.TryParseAddress("pas une adresse", 7777, out _, out _));
            Assert.IsFalse(SessionRules.TryParseAddress("1.2.3.4:0", 7777, out _, out _));
            Assert.IsFalse(SessionRules.TryParseAddress("1.2.3.4:99999", 7777, out _, out _));
        }

        [Test]
        public void DisconnectMessages_SayWhatHappened()
        {
            Assert.AreEqual("Vous avez quitté la partie.", SessionRules.DisconnectMessage(false, true, null));
            Assert.AreEqual("Partie fermée.", SessionRules.DisconnectMessage(true, true, null));
            Assert.AreEqual("L'hôte a fermé la partie.", SessionRules.DisconnectMessage(false, false, "L'hôte a fermé la partie."));
            Assert.AreEqual("Connexion perdue avec l'hôte.", SessionRules.DisconnectMessage(false, false, null));
        }

        [Test]
        public void SteamInvite_LaunchArgument_IsRecognized()
        {
            Assert.AreEqual(109775241234567890ul, SteamPlatform.LobbyFromCommandLine(new[] { "game.exe", "+connect_lobby", "109775241234567890" }));
            Assert.AreEqual(0ul, SteamPlatform.LobbyFromCommandLine(new[] { "game.exe", "+connect_lobby" }));
            Assert.AreEqual(0ul, SteamPlatform.LobbyFromCommandLine(new[] { "game.exe" }));
        }

        [Test]
        public void SharedTime_DoesNotStopWhenOnePlayerPauses()
        {
            Assert.AreEqual(0f, Core.GameStateRules.TimeScaleFor(Core.GameState.Paused, networked: false));
            Assert.AreEqual(1f, Core.GameStateRules.TimeScaleFor(Core.GameState.Paused, networked: true));
            Assert.AreEqual(1f, Core.GameStateRules.TimeScaleFor(Core.GameState.Workshop, networked: true));
        }
    }
}
