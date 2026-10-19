using System;
using System.Collections.Generic;
using RugWash.Core;
using RugWash.Economy;
using RugWash.Save;
using UnityEngine;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>
    /// Choix d'un emplacement de sauvegarde (trois par mode). Chaque ligne affiche ce que contient réellement le fichier :
    /// temps de jeu, date de la dernière session, mode, argent, réputation, commandes livrées, achats — rien d'inventé.
    /// En solo, « Continuer » / « Nouvelle partie » lance l'atelier ; en multijoueur, « Choisir » retient l'emplacement de
    /// la prochaine partie en ligne. « Effacer » demande confirmation. Le niveau du joueur (profil) n'appartient à aucun
    /// emplacement : il est affiché à part et n'est jamais effacé.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SaveSlotsUI : MonoBehaviour, IModalPanel
    {
        static readonly Color RowEmpty = UITheme.Row;
        static readonly Color RowUsed = UITheme.RowActive;
        static readonly Color RowChosen = UITheme.RowOwned;
        static readonly Color Muted = UITheme.TextSecondary;
        static readonly Color Warn = UITheme.Warning;

        [SerializeField] GameObject panel;
        [SerializeField] Text titleLabel;
        [SerializeField] Text profileLabel;
        [SerializeField] List<ListRowView> rows = new List<ListRowView>();
        [SerializeField] List<Button> eraseButtons = new List<Button>();
        [SerializeField] Text messageLabel;
        [SerializeField] GameObject confirmPanel;
        [SerializeField] Text confirmLabel;
        [SerializeField] Button confirmYesButton;
        [SerializeField] Button confirmNoButton;
        [SerializeField] Button closeButton;

        SaveManager save;
        SaveSlot pendingErase;
        readonly SlotSummary[] summaries = new SlotSummary[SaveSlots.CountPerMode];

        public bool IsOpen { get; private set; }
        public SaveMode Mode { get; private set; }
        /// <summary>Emplacement marqué comme choisi (multijoueur), mis en évidence.</summary>
        public SaveSlot? Highlighted { get; set; }
        public IReadOnlyList<ListRowView> Rows => rows;
        public IReadOnlyList<Button> EraseButtons => eraseButtons;
        public bool IsConfirmVisible => confirmPanel.activeSelf;
        public Button ConfirmYesButton => confirmYesButton;
        public Button ConfirmNoButton => confirmNoButton;
        public string Message => messageLabel.text;
        public SlotSummary SummaryOf(int index) => summaries[Mathf.Clamp(index, 0, summaries.Length - 1)];

        /// <summary>Emplacement validé par le joueur (bouton d'action de sa ligne).</summary>
        public event Action<SaveSlot, SlotSummary> SlotChosen;
        /// <summary>Un emplacement a été effacé (résumés à relire).</summary>
        public event Action SlotsChanged;

        public bool IsConfigured(out string missing)
        {
            missing = panel == null ? nameof(panel) : titleLabel == null ? nameof(titleLabel) : profileLabel == null ? nameof(profileLabel)
                : rows.Count != SaveSlots.CountPerMode ? nameof(rows) : eraseButtons.Count != SaveSlots.CountPerMode ? nameof(eraseButtons)
                : messageLabel == null ? nameof(messageLabel) : confirmPanel == null ? nameof(confirmPanel) : confirmLabel == null ? nameof(confirmLabel)
                : confirmYesButton == null ? nameof(confirmYesButton) : confirmNoButton == null ? nameof(confirmNoButton)
                : closeButton == null ? nameof(closeButton) : null;
            return missing == null;
        }

        public void Bind(SaveManager saveManager)
        {
            save = saveManager;
            for (int i = 0; i < rows.Count; i++)
            {
                int index = i;
                rows[i].ActionButton.onClick.AddListener(() => Choose(index));
                eraseButtons[i].onClick.AddListener(() => AskErase(index));
            }
            confirmYesButton.onClick.AddListener(ConfirmErase);
            confirmNoButton.onClick.AddListener(() => confirmPanel.SetActive(false));
            closeButton.onClick.AddListener(Close);
            confirmPanel.SetActive(false);
            panel.SetActive(false);
        }

        public bool Open(SaveMode mode)
        {
            Mode = mode;
            titleLabel.text = mode == SaveMode.Solo ? "Parties solo" : "Sauvegardes multijoueur";
            messageLabel.text = mode == SaveMode.Solo
                ? string.Empty
                : "Première partie en ligne sur un emplacement : votre progression de départ est copiée depuis l'hôte, une seule fois. " +
                  "Ensuite elle vous appartient (les achats de l'hôte ne la modifient plus, et inversement).";
            confirmPanel.SetActive(false);
            panel.SetActive(true);
            IsOpen = true;
            Refresh();
            rows[0].ActionButton.Select();
            return true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            confirmPanel.SetActive(false);
            panel.SetActive(false);
            IsOpen = false;
        }

        public void Refresh()
        {
            if (save != null)
            {
                var profile = save.Profile;
                int experience = profile.experience, level = profile.Level;
                int next = PlayerLevelRules.ThresholdFor(level + 1);
                profileLabel.text = level >= PlayerLevelRules.MaxLevel
                    ? $"Niveau {level} (maximum) — votre niveau est commun à tous les emplacements"
                    : $"Niveau {level} · {experience - PlayerLevelRules.ThresholdFor(level)} / {next - PlayerLevelRules.ThresholdFor(level)} XP — commun à tous les emplacements";
            }
            for (int i = 0; i < rows.Count; i++)
            {
                var slot = new SaveSlot(Mode, i + 1);
                var summary = SaveSlots.ReadSummary(slot);
                summaries[i] = summary;
                bool chosen = Highlighted.HasValue && Highlighted.Value.Equals(slot);
                string action = Mode == SaveMode.Multiplayer ? (chosen ? "Choisi" : "Choisir")
                    : summary.HasProgress ? "Continuer" : "Nouvelle partie";
                rows[i].Set(slot.DisplayName, Describe(summary), StatusOf(summary), summary.Readable ? Muted : Warn, action,
                    summary.Readable, chosen ? RowChosen : summary.Exists ? RowUsed : RowEmpty);
                eraseButtons[i].gameObject.SetActive(summary.Exists && summary.Readable);
            }
        }

        static string StatusOf(SlotSummary summary)
        {
            if (!summary.Exists) return "Vide";
            if (!summary.Readable) return "Inutilisable";
            return summary.Slot.Mode == SaveMode.Multiplayer && !summary.Data.HasProgress ? "Prête (à héberger)" : "En cours";
        }

        /// <summary>Résumé lisible d'un emplacement (valeurs du fichier).</summary>
        public static string Describe(SlotSummary summary)
        {
            if (!summary.Exists)
                return summary.Slot.Mode == SaveMode.Solo ? "Emplacement libre : une nouvelle partie commencera avec le kit de départ."
                    : "Emplacement libre : la progression de départ sera copiée depuis l'hôte à votre première partie en ligne.";
            if (!summary.Readable) return $"{summary.Problem} : cet emplacement n'est ni chargé ni modifié.";
            var data = summary.Data;
            string last = summary.LastSessionText;
            string line1 = $"{(summary.Slot.Mode == SaveMode.Solo ? "Solo" : "Multijoueur")} · temps de jeu {summary.PlayTimeText}" +
                           (string.IsNullOrEmpty(last) ? string.Empty : $" · dernière session le {last}");
            string line2 = $"{WalletManager.Format(data.money)} · réputation {data.reputation} · {data.ordersDelivered} commande(s) livrée(s) · {data.upgrades.Count} achat(s)";
            return $"{line1}\n{line2}";
        }

        void Choose(int index)
        {
            if (confirmPanel.activeSelf) return;
            var summary = summaries[index];
            if (!summary.Readable) return;
            var slot = new SaveSlot(Mode, index + 1);
            if (Mode == SaveMode.Multiplayer)
            {
                Highlighted = slot;
                Refresh();
            }
            SlotChosen?.Invoke(slot, summary);
        }

        void AskErase(int index)
        {
            pendingErase = new SaveSlot(Mode, index + 1);
            confirmLabel.text = $"Effacer « {pendingErase.DisplayName} » ?\n<size=22>Toute la progression de cet emplacement sera perdue " +
                                "(argent, réputation, achats, atelier). Votre niveau et vos paramètres sont conservés.</size>";
            confirmPanel.SetActive(true);
            confirmNoButton.Select();
        }

        void ConfirmErase()
        {
            confirmPanel.SetActive(false);
            messageLabel.text = SaveSlots.Delete(pendingErase) ? $"{pendingErase.DisplayName} effacé." : "Impossible d'effacer cet emplacement.";
            Refresh();
            SlotsChanged?.Invoke();
        }
    }
}
