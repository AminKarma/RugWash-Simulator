using System.Collections.Generic;
using NUnit.Framework;
using RugWash.Network;

namespace RugWash.Tests
{
    /// <summary>
    /// Mission de corrections (règles pures) : compatibilité des versions avant de rejoindre une partie (message avec les
    /// deux versions, anciens clients lus sans erreur) et reconnexion du même joueur (ancienne connexion repérée).
    /// </summary>
    public class BugfixSessionTests
    {
        [Test]
        public void Payload_CarriesGameVersionAndIdentity_AndOldClientsStillDecode()
        {
            var payload = new ConnectionPayload("rugwash-coop-8", "Ana", "0.1.2", "key-1");
            Assert.IsTrue(ConnectionPayload.TryDecode(payload.Encode(), out var back));
            Assert.AreEqual("rugwash-coop-8", back.Version);
            Assert.AreEqual("Ana", back.DisplayName);
            Assert.AreEqual("0.1.2", back.GameVersion);
            Assert.AreEqual("key-1", back.PlayerKey);

            // Ancien client : seulement protocole et nom.
            var writer = new ByteWriter(32);
            writer.String("rugwash-coop-6");
            writer.String("Bob");
            Assert.IsTrue(ConnectionPayload.TryDecode(writer.ToArray(), out var old));
            Assert.AreEqual("rugwash-coop-6", old.Version);
            Assert.AreEqual(string.Empty, old.GameVersion);
            Assert.IsFalse(ConnectionPayload.TryDecode(null, out _));
        }

        [Test]
        public void IncompatibleVersion_IsRefusedBeforeLoading_WithBothVersions()
        {
            var client = new ConnectionPayload("rugwash-coop-7", "Ana", "0.1.1", "k").Encode();
            Assert.IsFalse(SessionRules.Approve(client, "rugwash-coop-8", "0.1.2", 1, 4, out _, out string reason, out _));
            StringAssert.Contains("0.1.1", reason, "Version du joueur.");
            StringAssert.Contains("0.1.2", reason, "Version de l'hôte.");
            StringAssert.Contains("rugwash-coop-7", reason);
            StringAssert.Contains("rugwash-coop-8", reason);
            StringAssert.Contains("même version", reason, "Ce qu'il faut faire.");

            var compatible = new ConnectionPayload("rugwash-coop-8", "Ana", "0.1.2", "k").Encode();
            Assert.IsTrue(SessionRules.Approve(compatible, "rugwash-coop-8", "0.1.2", 1, 4, out string name, out _, out var read));
            Assert.AreEqual("Ana", name);
            Assert.AreEqual("k", read.PlayerKey);
            Assert.IsFalse(SessionRules.Approve(compatible, "rugwash-coop-8", "0.1.2", 4, 4, out _, out string full, out _));
            StringAssert.Contains("complète", full);
        }

        [Test]
        public void Reconnection_FindsTheStaleConnectionOfTheSamePlayer_Only()
        {
            var keys = new Dictionary<ulong, string> { { 1, "steam:42" }, { 2, "abc" } };
            Assert.IsTrue(SessionRules.TryFindStaleConnection(keys, "steam:42", 5, out ulong stale));
            Assert.AreEqual(1ul, stale, "L'ancienne connexion du même joueur est fermée.");
            Assert.IsFalse(SessionRules.TryFindStaleConnection(keys, "other", 5, out _), "Un autre joueur n'évince personne.");
            Assert.IsFalse(SessionRules.TryFindStaleConnection(keys, string.Empty, 5, out _), "Sans identité : aucune éviction.");
            Assert.IsFalse(SessionRules.TryFindStaleConnection(keys, "abc", 2, out _), "Pas soi-même.");
        }
    }
}
