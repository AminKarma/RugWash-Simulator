using System.Collections.Generic;
using NUnit.Framework;
using RugWash.Network;

namespace RugWash.Tests
{
    /// <summary>
    /// Grande mise à jour, phase 5 : règles pures du chat (nettoyage, cadence), du chat vocal (codec μ-law, détection de voix,
    /// tampon de gigue) et de la modération (partie fermée, joueur expulsé, ticket Steam de la vérification Premium).
    /// </summary>
    public class SocialRulesTests
    {
        [Test]
        public void Chat_CleansText_AndBoundsItsLength()
        {
            Assert.AreEqual("Salut tout le monde", ChatRules.Clean("  Salut\t tout\n\nle   monde  "));
            Assert.AreEqual(string.Empty, ChatRules.Clean("\u0001\u0002 \n"));
            Assert.AreEqual("abc", ChatRules.Clean("a\u0007b\u001bc"));
            Assert.AreEqual("Bravo !", ChatRules.Clean("Bravo ! 😀"), "Emojis hors police retirés.");
            Assert.AreEqual(ChatRules.MaxLength, ChatRules.Clean(new string('x', 500)).Length);
            Assert.AreEqual("élégant façade", ChatRules.Clean("élégant façade"), "Accents conservés.");
        }

        [Test]
        public void Chat_HostLimitsTheRate_PerPlayer()
        {
            var limiter = new ChatRateLimiter();
            Assert.IsTrue(limiter.Allow(5, 10f));
            Assert.IsFalse(limiter.Allow(5, 10.2f), "Trop rapproché.");
            Assert.IsTrue(limiter.Allow(6, 10.2f), "Chaque joueur a sa propre limite.");
            float t = 11f;
            int accepted = 1;
            for (int i = 0; i < 10; i++, t += 0.7f)
                if (limiter.Allow(5, t)) accepted++;
            Assert.AreEqual(ChatRules.BurstMessages, accepted, "Au plus cinq messages par fenêtre de dix secondes.");
            Assert.IsTrue(limiter.Allow(5, t + ChatRules.BurstWindow), "La limite se relâche ensuite.");
        }

        [Test]
        public void Voice_MuLaw_RoundTripIsClose_AndKeepsTheSign()
        {
            foreach (float sample in new[] { 0f, 0.01f, -0.01f, 0.1f, -0.25f, 0.5f, -0.75f, 0.99f, -1f })
            {
                float back = VoiceCodec.Decode(VoiceCodec.Encode(sample));
                Assert.AreEqual(sample, back, 0.02f + 0.04f * System.Math.Abs(sample), $"Échantillon {sample}");
                if (System.Math.Abs(sample) > 0.005f) Assert.AreEqual(System.Math.Sign(sample), System.Math.Sign(back));
            }
            Assert.AreEqual(VoiceCodec.Encode(2f), VoiceCodec.Encode(1f), "Saturé proprement.");
            var frame = new float[VoiceCodec.FrameSamples];
            for (int i = 0; i < frame.Length; i++) frame[i] = 0.3f * (float)System.Math.Sin(i * 0.2);
            var bytes = new byte[frame.Length];
            VoiceCodec.EncodeFrame(frame, 0, frame.Length, bytes, 0);
            var decoded = new float[frame.Length];
            VoiceCodec.DecodeFrame(bytes, 0, bytes.Length, decoded, 0);
            Assert.AreEqual(VoiceCodec.Rms(frame, 0, frame.Length), VoiceCodec.Rms(decoded, 0, decoded.Length), 0.01f);
            Assert.AreEqual(VoiceCodec.SampleRate, VoiceCodec.FrameSamples * 25, "Trames de 40 ms.");
        }

        [Test]
        public void Voice_Activity_OpensOnSpeech_AndClosesAfterAShortHangover()
        {
            var activity = new VoiceActivity { Threshold = 0.02f, HangoverFrames = 3 };
            Assert.IsFalse(activity.Update(0.005f), "Silence : rien n'est envoyé.");
            Assert.IsTrue(activity.Update(0.1f));
            Assert.IsTrue(activity.Update(0.001f));
            Assert.IsTrue(activity.Update(0.001f));
            Assert.IsFalse(activity.Update(0.001f), "Fin de la phrase : le micro se referme.");
        }

        [Test]
        public void Voice_JitterBuffer_WaitsALittle_FillsGapsWithSilence_AndCatchesUp()
        {
            var buffer = new VoiceJitterBuffer { StartSamples = 100, MaxSamples = 400 };
            var input = new float[80];
            for (int i = 0; i < input.Length; i++) input[i] = 0.5f;
            var output = new float[50];
            buffer.Write(input, 0, input.Length);
            Assert.IsFalse(buffer.IsPlaying, "Pas encore assez d'avance.");
            buffer.Read(output, 0, output.Length);
            Assert.AreEqual(0f, output[0], "Silence avant le démarrage.");
            buffer.Write(input, 0, input.Length);
            Assert.IsTrue(buffer.IsPlaying);
            buffer.Read(output, 0, output.Length);
            Assert.AreEqual(0.5f, output[10], 1e-5f);
            var big = new float[1000];
            buffer.Write(big, 0, big.Length);
            Assert.LessOrEqual(buffer.Buffered, 400, "Le retard accumulé est rattrapé.");
            Assert.Greater(buffer.Dropped, 0);
            var drain = new float[600];
            buffer.Read(drain, 0, drain.Length);
            Assert.IsFalse(buffer.IsPlaying, "Tampon vide : silence, puis nouvelle avance.");
            Assert.AreEqual(1, buffer.Underruns);
        }

        [Test]
        public void Session_ClosedGame_AndKickedPlayers_AreRefusedByTheHost()
        {
            var kicked = new HashSet<string> { "steam:42" };
            Assert.IsNull(SessionRules.HostRefusal(true, kicked, "steam:7"));
            Assert.AreEqual(SessionRules.ClosedReason, SessionRules.HostRefusal(false, kicked, "steam:7"));
            Assert.AreEqual(SessionRules.KickedRejoinReason, SessionRules.HostRefusal(true, kicked, "steam:42"), "Un joueur expulsé ne revient pas.");
            Assert.IsNull(SessionRules.HostRefusal(true, kicked, string.Empty), "Sans identité, pas de confusion avec un autre joueur.");
            Assert.AreEqual(76561197960265728UL, SessionRules.SteamIdOf("steam:76561197960265728"));
            Assert.AreEqual(0UL, SessionRules.SteamIdOf("0a1b2c"), "Identité hors Steam : aucune vérification Premium possible.");
            Assert.AreEqual(0UL, SessionRules.SteamIdOf("steam:abc"));
        }

        [Test]
        public void Payload_CarriesTheSteamTicket_AndStaysCompatible()
        {
            var ticket = new byte[] { 1, 2, 3, 250 };
            var payload = new ConnectionPayload("rugwash-coop-11", "Ana", "0.1.3", "steam:9", ticket);
            Assert.IsTrue(ConnectionPayload.TryDecode(payload.Encode(), out var back));
            CollectionAssert.AreEqual(ticket, back.AuthTicket);
            Assert.AreEqual("steam:9", back.PlayerKey);
            var withoutTicket = new ConnectionPayload("rugwash-coop-11", "Ana", "0.1.3", "key");
            Assert.IsTrue(ConnectionPayload.TryDecode(withoutTicket.Encode(), out back));
            Assert.AreEqual(0, back.AuthTicket.Length);
            // Ancien format (sans ticket) : toujours lisible.
            var writer = new ByteWriter();
            writer.String("rugwash-coop-11");
            writer.String("Bob");
            Assert.IsTrue(ConnectionPayload.TryDecode(writer.ToArray(), out back));
            Assert.AreEqual(0, back.AuthTicket.Length);
            // Ticket annoncé plus long que les données : refusé.
            var forged = new ByteWriter();
            foreach (string field in new[] { "v", "n", "g", "k" }) forged.String(field);
            forged.UShort(900);
            Assert.IsFalse(ConnectionPayload.TryDecode(forged.ToArray(), out _));
        }
    }
}
