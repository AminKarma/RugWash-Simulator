using System.Text;
using RugWash.Core;
using RugWash.Economy;
using RugWash.Effects;
using RugWash.Orders;
using UnityEngine;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>Écran de résultats d'une commande : note, critères mesurés, récompense, avant/après.</summary>
    [DisallowMultipleComponent]
    public sealed class ResultsUI : MonoBehaviour
    {
        [SerializeField] GameObject panel;
        [SerializeField] Text titleLabel;
        [SerializeField] Text gradeLabel;
        [SerializeField] Text scoreLabel;
        [SerializeField] Text linesLabel;
        [SerializeField] Text rewardLabel;
        [SerializeField] RawImage beforeImage;
        [SerializeField] RawImage afterImage;
        [SerializeField] AspectRatioFitter beforeAspect;
        [SerializeField] AspectRatioFitter afterAspect;
        [SerializeField] Button workshopButton;
        [SerializeField] Button nextOrderButton;

        OrderManager orders;
        RugSnapshotService snapshots;
        OrderBoardUI board;
        GameStateManager gameState;

        public bool IsVisible => panel != null && panel.activeSelf;
        public Button WorkshopButton => workshopButton;
        public Button NextOrderButton => nextOrderButton;

        public bool IsConfigured(out string missing)
        {
            missing = panel == null ? nameof(panel) : titleLabel == null ? nameof(titleLabel) : gradeLabel == null ? nameof(gradeLabel)
                : scoreLabel == null ? nameof(scoreLabel) : linesLabel == null ? nameof(linesLabel) : rewardLabel == null ? nameof(rewardLabel)
                : beforeImage == null ? nameof(beforeImage) : afterImage == null ? nameof(afterImage)
                : beforeAspect == null ? nameof(beforeAspect) : afterAspect == null ? nameof(afterAspect)
                : workshopButton == null ? nameof(workshopButton) : nextOrderButton == null ? nameof(nextOrderButton) : null;
            return missing == null;
        }

        public void Bind(GameStateManager state, OrderManager orderManager, RugSnapshotService snapshotService, OrderBoardUI orderBoard)
        {
            gameState = state;
            orders = orderManager;
            snapshots = snapshotService;
            board = orderBoard;
            orders.OrderFinished += Show;
            orders.ReturnedToWorkshop += Hide;
            workshopButton.onClick.AddListener(BackToWorkshop);
            nextOrderButton.onClick.AddListener(ChooseNextOrder);
            panel.SetActive(false);
        }

        void OnDestroy()
        {
            if (orders != null)
            {
                orders.OrderFinished -= Show;
                orders.ReturnedToWorkshop -= Hide;
            }
        }

        void Show(OrderResult result)
        {
            string zone = orders.UnlockedBayCount > 1 ? $" · {OrderManager.BayLabel(result.Bay).ToLowerInvariant()}" : string.Empty;
            titleLabel.text = $"{result.Order.title} — {result.Order.clientName}{zone}";
            gradeLabel.text = OrderEvaluator.GradeLabel(result.Grade);
            gradeLabel.color = GradeColor(result.Grade);
            scoreLabel.text = $"Score : {result.Score:0}/100 · durée {OrderEvaluator.FormatTime(result.ElapsedSeconds)}";

            var builder = new StringBuilder();
            foreach (var line in result.Lines)
                builder.Append(line.Met ? "✔ " : "✘ ").Append(line.Label).Append(" : ").Append(line.Value).Append('\n');
            builder.Append($"Surface propre : {result.Summary.CleanCellRatio01 * 100f:0} %");
            linesLabel.text = builder.ToString();

            rewardLabel.text = result.Accepted
                ? $"Paiement : +{WalletManager.Format(result.Reward)}" + (result.TimeBonus ? " (bonus de rapidité inclus)" : string.Empty) +
                  $"\nRéputation : {(result.ReputationDelta >= 0 ? "+" : "")}{result.ReputationDelta}"
                : $"Le client refuse le tapis : aucun paiement.\nRéputation : {result.ReputationDelta}. La commande reste disponible.";

            float aspect = result.Order.rug.sizeMeters.x / result.Order.rug.sizeMeters.y;
            beforeAspect.aspectRatio = aspect;
            afterAspect.aspectRatio = aspect;
            beforeImage.texture = snapshots.Before;
            afterImage.texture = snapshots.After;
            panel.SetActive(true);
            workshopButton.Select();
        }

        public void BackToWorkshop()
        {
            if (!panel.activeSelf) return;
            orders.ReturnToWorkshop();
            Hide();
        }

        /// <summary>Résumé fermé (bouton, ou nouveau tapis accepté par n'importe quel joueur) : plus aucune référence à l'ancien tapis.</summary>
        void Hide()
        {
            if (!panel.activeSelf) return;
            panel.SetActive(false);
            beforeImage.texture = null;
            afterImage.texture = null;
        }

        public void ChooseNextOrder()
        {
            BackToWorkshop();
            board.Open();
        }

        static Color GradeColor(OrderGrade grade)
        {
            switch (grade)
            {
                case OrderGrade.S: return UITheme.Gold;
                case OrderGrade.A: return UITheme.Success;
                case OrderGrade.B: return new Color(0.5f, 0.8f, 1f);
                case OrderGrade.C: return new Color(0.9f, 0.9f, 0.9f);
                case OrderGrade.D: return UITheme.Warning;
                default: return new Color(1f, 0.45f, 0.4f);
            }
        }
    }
}
