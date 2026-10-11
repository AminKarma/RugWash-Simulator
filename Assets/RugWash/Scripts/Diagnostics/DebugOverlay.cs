using System.Text;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Player;
using UnityEngine;
using UnityEngine.UI;

namespace RugWash.Diagnostics
{
    /// <summary>
    /// Overlay de diagnostic (F3) : FPS, état de jeu, propreté, ressources, envois de texture.
    /// Disponible uniquement dans l'éditeur et les builds de développement ; masqué par défaut.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DebugOverlay : MonoBehaviour
    {
        [SerializeField] GameObject panel;
        [SerializeField] Text label;
        [SerializeField] PerformanceMonitor performance;
        [SerializeField] float refreshInterval = 0.25f;

        readonly StringBuilder builder = new StringBuilder(512);
        GameStateManager gameState;
        RugCleaningController rug;
        PlayerToolController tools;
        PlayerInputReader input;
        float nextRefresh;
        int lastUploads;
        float lastUploadTime;
        float uploadsPerSecond;

        public static bool IsAllowed => Application.isEditor || Debug.isDebugBuild;
        public bool IsVisible => panel != null && panel.activeSelf;

        public bool IsConfigured(out string missing)
        {
            missing = panel == null ? nameof(panel) : label == null ? nameof(label) : performance == null ? nameof(performance) : null;
            return missing == null;
        }

        public void Bind(GameStateManager state, RugCleaningController rugController, PlayerToolController toolController, PlayerInputReader inputReader)
        {
            gameState = state;
            rug = rugController;
            tools = toolController;
            input = inputReader;
            panel.SetActive(false);
            if (!IsAllowed) enabled = false;
        }

        void Update()
        {
            if (input == null) return;
            if (input.DebugOverlayPressed) panel.SetActive(!panel.activeSelf);
            if (!panel.activeSelf || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + refreshInterval;

            int uploads = rug.DirtRenderer.UploadCount;
            float elapsed = Time.unscaledTime - lastUploadTime;
            if (elapsed > 0f) uploadsPerSecond = (uploads - lastUploads) / elapsed;
            lastUploads = uploads;
            lastUploadTime = Time.unscaledTime;

            var sampler = performance.Sampler;
            builder.Clear();
            builder.Append("DIAGNOSTIC (F3)\n");
            builder.Append("FPS moy. ").Append(sampler.AverageFps.ToString("0")).Append("  |  pire frame ")
                .Append((sampler.WorstSeconds * 1000f).ToString("0.0")).Append(" ms\n");
            builder.Append("État : ").Append(gameState.State).Append("  |  fenêtres : ").Append(gameState.OpenModals).Append('\n');
            if (rug.Grid != null)
            {
                builder.Append("Tapis : ").Append(rug.Definition.displayName).Append(" (").Append(rug.Grid.Width).Append('x')
                    .Append(rug.Grid.Height).Append(" cellules)\n");
                builder.Append("Propreté : ").Append((rug.Grid.Cleanliness01 * 100f).ToString("0.0")).Append(" %  |  saleté ")
                    .Append(rug.Grid.TotalDirt.ToString("0.0")).Append(" / ").Append(rug.Grid.InitialDirt.ToString("0.0")).Append('\n');
                builder.Append("Humidité moy. ").Append((rug.Grid.AverageMoisture * 100f).ToString("0")).Append(" %  |  mousse ")
                    .Append((rug.Grid.AverageFoam * 100f).ToString("0")).Append(" %\n");
            }
            builder.Append("Envois texture : ").Append(uploadsPerSecond.ToString("0")).Append("/s\n");
            var tool = tools.ActiveTool;
            if (tool != null)
            {
                builder.Append("Outil : ").Append(tool.Definition.displayName).Append(tool.IsUsing ? " (actif)" : "")
                    .Append(tool.IsAffectingRug ? " → tapis" : "").Append('\n');
            }
            builder.Append("Eau ").Append(tools.Equipment.Water.Amount.ToString("0.0"))
                .Append(" L  |  produit ").Append(tools.Equipment.Soap.Amount.ToString("0.00"))
                .Append(" L  |  cuve ").Append(tools.Equipment.Recovery.Amount.ToString("0.0")).Append(" L");
            label.text = builder.ToString();
        }
    }
}
