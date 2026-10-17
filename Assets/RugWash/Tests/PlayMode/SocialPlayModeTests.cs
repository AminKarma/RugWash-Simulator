using System.Collections;
using NUnit.Framework;
using RugWash.Core;
using RugWash.Save;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RugWash.Tests
{
    /// <summary>
    /// Grande mise à jour, phase 5 : en solo, rien de ce qui est propre au jeu en ligne n'apparaît (chat, joueurs, micro) ;
    /// les réglages du chat vocal sont des paramètres comme les autres (profil du joueur). Le chat, la voix, l'expulsion et
    /// l'ouverture de la partie demandent deux instances du jeu : voir les tests manuels (VALIDATION.md).
    /// </summary>
    public class SocialPlayModeTests
    {
        GameBootstrap game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            TestSaveSandbox.Reset(legacyTools: true);
            SceneManager.LoadScene("Workshop", LoadSceneMode.Single);
            yield return null;
            yield return null;
            game = Object.FindAnyObjectByType<GameBootstrap>();
            Assert.IsTrue(game != null && game.IsReady);
        }

        [UnityTest]
        public IEnumerator Solo_HidesChatPlayersAndMicrophone()
        {
            yield return null;
            Assert.IsFalse(game.Chat.IsOnline);
            Assert.IsFalse(game.Chat.Open(), "Pas de chat hors ligne.");
            Assert.IsFalse(game.Players.Open(), "Pas de fenêtre des joueurs hors ligne.");
            Assert.IsFalse(game.Voice.Online);
            Assert.IsFalse(game.Voice.IsCapturing, "Le micro ne s'ouvre jamais en solo.");
            Assert.IsTrue(game.GameState.Pause());
            yield return null;
            Assert.IsFalse(game.PauseMenu.PlayersButton.gameObject.activeSelf, "Bouton « Joueurs » seulement en ligne.");
            game.GameState.Resume();
        }

        [UnityTest]
        public IEnumerator VoiceSettings_AreSavedInTheProfile_AndApplied()
        {
            var settings = game.Settings;
            settings.SetVoiceMode(1);
            settings.SetVoiceVolume(0.4f);
            yield return null;
            Assert.AreEqual(Network.VoiceMode.OpenMic, game.Voice.Mode);
            Assert.AreEqual(0.4f, Network.VoicePlayback.GlobalVolume, 1e-4f);
            Assert.IsTrue(game.SaveSystem.SaveNow("test voix"));
            var profile = new ProfileStore(TestSaveSandbox.Directory).Load(out _);
            Assert.AreEqual(1, profile.settings.voiceMode);
            Assert.AreEqual(0.4f, profile.settings.voiceVolume, 1e-4f);
            settings.SetVoiceMode(0);
            settings.SetVoiceVolume(1f);
            yield return null;
            Assert.AreEqual(Network.VoiceMode.PushToTalk, game.Voice.Mode);
        }
    }
}
