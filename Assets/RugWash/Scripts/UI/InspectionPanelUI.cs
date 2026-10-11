using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Effects;
using UnityEngine;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>
    /// Inspection du tapis (touche F) : comparaison avant/maintenant et statistiques issues de la grille.
    /// Libère le curseur sans mettre le jeu en pause.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InspectionPanelUI : MonoBehaviour, IModalPanel
    {
        [SerializeField] GameObject panel;
        [SerializeField] RawImage beforeImage;
        [SerializeField] RawImage afterImage;
        [SerializeField] AspectRatioFitter beforeAspect;
        [SerializeField] AspectRatioFitter afterAspect;
        [SerializeField] Text titleLabel;
        [SerializeField] Text statsLabel;
        [SerializeField] Button closeButton;

        GameStateManager gameState;
        RugCleaningController rug;
        RugSnapshotService snapshots;

        public bool IsOpen { get; private set; }

        public bool IsConfigured(out string missing)
        {
            missing = panel == null ? nameof(panel)
                : beforeImage == null ? nameof(beforeImage)
                : afterImage == null ? nameof(afterImage)
                : beforeAspect == null ? nameof(beforeAspect)
                : afterAspect == null ? nameof(afterAspect)
                : titleLabel == null ? nameof(titleLabel)
                : statsLabel == null ? nameof(statsLabel)
                : closeButton == null ? nameof(closeButton) : null;
            return missing == null;
        }

        public void Bind(GameStateManager state, RugCleaningController rugController, RugSnapshotService snapshotService)
        {
            gameState = state;
            rug = rugController;
            snapshots = snapshotService;
            closeButton.onClick.AddListener(Close);
            panel.SetActive(false);
        }

        /// <summary>Ouvre si le joueur est en jeu, ferme si déjà ouvert.</summary>
        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public bool Open()
        {
            if (IsOpen || !gameState.IsGameplayActive || rug.Grid == null) return false;
            snapshots.CaptureAfter();
            beforeImage.texture = snapshots.Before;
            afterImage.texture = snapshots.After;
            float aspect = rug.Definition.sizeMeters.x / rug.Definition.sizeMeters.y;
            beforeAspect.aspectRatio = aspect;
            afterAspect.aspectRatio = aspect;

            RugCleaningSummary s = rug.Grid.ComputeSummary();
            titleLabel.text = $"Inspection — {rug.Definition.displayName}";
            statsLabel.text =
                $"Saleté retirée : {s.Cleanliness01 * 100f:0} %\n" +
                $"Surface propre : {s.CleanCellRatio01 * 100f:0} %\n" +
                $"Humidité moyenne : {s.AverageMoisture01 * 100f:0} %\n" +
                $"Mousse restante : {s.AverageFoam01 * 100f:0} %\n" +
                $"Résidus de produit : {s.AverageResidue01 * 100f:0} %";

            panel.SetActive(true);
            IsOpen = true;
            gameState.PushModal();
            return true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            panel.SetActive(false);
            IsOpen = false;
            gameState.PopModal();
        }
    }
}
