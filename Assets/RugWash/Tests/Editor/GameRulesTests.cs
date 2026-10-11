using NUnit.Framework;
using RugWash.Core;
using RugWash.Diagnostics;
using RugWash.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RugWash.Tests
{
    /// <summary>Règles d'état de jeu, de curseur et configuration des entrées.</summary>
    public class GameRulesTests
    {
        [TestCase(GameState.Workshop, 0, true)]
        [TestCase(GameState.ActiveOrder, 0, true)]
        [TestCase(GameState.Workshop, 1, false)]
        [TestCase(GameState.Paused, 0, false)]
        [TestCase(GameState.MainMenu, 0, false)]
        [TestCase(GameState.Results, 0, false)]
        public void Cursor_IsLockedOnlyDuringGameplayWithoutModal(GameState state, int modals, bool expected)
        {
            Assert.AreEqual(expected, GameStateRules.ShouldLockCursor(state, modals));
        }

        [Test]
        public void Pause_IsOnlyAllowedFromGameplay()
        {
            Assert.IsTrue(GameStateRules.CanPause(GameState.Workshop));
            Assert.IsTrue(GameStateRules.CanPause(GameState.ActiveOrder));
            Assert.IsFalse(GameStateRules.CanPause(GameState.MainMenu));
            Assert.IsFalse(GameStateRules.CanPause(GameState.Results));
            Assert.AreEqual(0f, GameStateRules.TimeScaleFor(GameState.Paused));
            Assert.AreEqual(1f, GameStateRules.TimeScaleFor(GameState.Workshop));
        }

        [Test]
        public void InputActions_DefineAllRequiredControls()
        {
            var asset = RugWashInputActions.Create();
            try
            {
                foreach (string name in new[] { "Move", "Look", "Use", "Interact", "ToolScroll", "Tool1", "Tool5", "Pause", "Inspect" })
                    Assert.IsNotNull(asset.FindAction(name), $"Action manquante : {name}");

                Assert.AreEqual("<Mouse>/leftButton", asset.FindAction("Use").bindings[0].path);
                Assert.AreEqual("<Keyboard>/e", asset.FindAction("Interact").bindings[0].path);
                Assert.AreEqual("<Keyboard>/f", asset.FindAction("Inspect").bindings[0].path);
                Assert.AreEqual("<Keyboard>/escape", asset.FindAction("Pause").bindings[0].path);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        /// <summary>
        /// Les liaisons utilisent des touches physiques : « w » désigne la touche Z d'un clavier AZERTY.
        /// ZQSD (AZERTY) et WASD (QWERTY) fonctionnent donc sans réglage.
        /// </summary>
        [Test]
        public void Movement_UsesPhysicalKeys_ForAzertyAndQwerty()
        {
            var asset = RugWashInputActions.Create();
            try
            {
                var move = asset.FindAction("Move");
                var paths = new System.Collections.Generic.List<string>();
                foreach (var binding in move.bindings)
                    if (binding.isPartOfComposite) paths.Add(binding.path);
                CollectionAssert.IsSupersetOf(paths, new[] { "<Keyboard>/w", "<Keyboard>/a", "<Keyboard>/s", "<Keyboard>/d" });
                CollectionAssert.IsSupersetOf(paths, new[] { "<Keyboard>/upArrow", "<Keyboard>/leftArrow" });
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void FrameTimeSampler_ComputesAverageAndWorst()
        {
            var sampler = new FrameTimeSampler(4);
            sampler.Add(0.010f);
            sampler.Add(0.020f);
            sampler.Add(0.030f);
            Assert.AreEqual(0.020f, sampler.AverageSeconds, 1e-6f);
            Assert.AreEqual(0.030f, sampler.WorstSeconds, 1e-6f);
            for (int i = 0; i < 10; i++) sampler.Add(0.016f);
            Assert.AreEqual(4, sampler.Count, "La fenêtre glissante ne doit pas grandir indéfiniment.");
        }
    }
}
