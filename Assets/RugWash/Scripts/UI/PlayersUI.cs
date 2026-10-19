using System.Collections.Generic;
using RugWash.Core;
using RugWash.Network;
using UnityEngine;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>
    /// Fenêtre « Joueurs » d'une partie en ligne (grande mise à jour, depuis le menu pause) : qui est là, qui parle, couper la
    /// voix d'un joueur (pour soi), et pour l'hôte seulement : expulser un joueur (après confirmation) et ouvrir ou fermer la
    /// partie aux nouveaux joueurs. Les décisions sont vérifiées par la session de l'hôte (<see cref="MultiplayerSession"/>).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayersUI : MonoBehaviour, IModalPanel
    {
        static readonly Color RowColor = UITheme.Row;
        static readonly Color Speaking = UITheme.Success;
        static readonly Color Muted = UITheme.TextSecondary;

        [SerializeField] GameObject panel;
        [SerializeField] List<PlayerRowView> rows = new List<PlayerRowView>();
        [SerializeField] Text statusLabel;
        [SerializeField] Button openButton;
        [SerializeField] Text openLabel;
        [SerializeField] GameObject confirmPanel;
        [SerializeField] Text confirmLabel;
        [SerializeField] Button confirmYes;
        [SerializeField] Button confirmNo;
        [SerializeField] Text messageLabel;
        [SerializeField] Button closeButton;

        GameStateManager gameState;
        readonly List<NetworkPlayerAvatar> shown = new List<NetworkPlayerAvatar>();
        ulong pendingKick;
        float refreshTimer;

        public bool IsOpen { get; private set; }
        public IReadOnlyList<PlayerRowView> Rows => rows;
        public bool IsConfirming => confirmPanel.activeSelf;
        public Button OpenButton => openButton;
        public Button ConfirmYes => confirmYes;
        public string Message => messageLabel.text;

        public bool IsConfigured(out string missing)
        {
            missing = panel == null ? nameof(panel) : rows.Count == 0 || rows.Contains(null) ? nameof(rows) : statusLabel == null ? nameof(statusLabel)
                : openButton == null ? nameof(openButton) : openLabel == null ? nameof(openLabel) : confirmPanel == null ? nameof(confirmPanel)
                : confirmLabel == null ? nameof(confirmLabel) : confirmYes == null ? nameof(confirmYes) : confirmNo == null ? nameof(confirmNo)
                : messageLabel == null ? nameof(messageLabel) : closeButton == null ? nameof(closeButton) : null;
            return missing == null;
        }

        public void Bind(GameStateManager state)
        {
            gameState = state;
            for (int i = 0; i < rows.Count; i++)
            {
                int index = i;
                rows[i].MuteButton.onClick.AddListener(() => ToggleMute(index));
                rows[i].KickButton.onClick.AddListener(() => AskKick(index));
            }
            openButton.onClick.AddListener(ToggleOpen);
            confirmYes.onClick.AddListener(ConfirmKick);
            confirmNo.onClick.AddListener(() => confirmPanel.SetActive(false));
            closeButton.onClick.AddListener(Close);
            confirmPanel.SetActive(false);
            panel.SetActive(false);
        }

        static MultiplayerSession Session => MultiplayerSession.Find();

        public bool Open()
        {
            var session = Session;
            if (IsOpen || session == null || !session.InSession) return false;
            messageLabel.text = string.Empty;
            confirmPanel.SetActive(false);
            panel.SetActive(true);
            IsOpen = true;
            gameState?.PushModal();
            Refresh();
            closeButton.Select();
            return true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            panel.SetActive(false);
            IsOpen = false;
            gameState?.PopModal();
        }

        void Update()
        {
            if (!IsOpen) return;
            var session = Session;
            if (session == null || !session.InSession)
            {
                Close();
                return;
            }
            refreshTimer += Time.unscaledDeltaTime;
            if (refreshTimer < 0.25f) return;
            refreshTimer = 0f;
            Refresh();
        }

        public void Refresh()
        {
            var session = Session;
            bool host = session != null && session.IsHost;
            shown.Clear();
            foreach (var avatar in FindObjectsByType<NetworkPlayerAvatar>())
                if (avatar.IsSpawned) shown.Add(avatar);
            shown.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));
            for (int i = 0; i < rows.Count; i++)
            {
                bool used = i < shown.Count;
                rows[i].gameObject.SetActive(used);
                if (!used) continue;
                var avatar = shown[i];
                bool self = avatar.IsOwner;
                bool isHost = avatar.OwnerClientId == Unity.Netcode.NetworkManager.ServerClientId;
                var voice = avatar.Voice;
                bool speaking = self ? IsLocalSpeaking() : voice != null && voice.IsSpeaking;
                bool muted = voice != null && voice.Muted;
                string role = self ? (isHost ? "vous, hôte" : "vous") : isHost ? "hôte" : "invité";
                string status = speaking ? "parle" : muted ? "voix coupée" : string.Empty;
                rows[i].Set($"{avatar.DisplayName} ({role})", status, speaking ? Speaking : Muted,
                    !self && voice != null, muted ? "Rétablir sa voix" : "Couper sa voix",
                    host && !self, RowColor);
            }
            statusLabel.text = session == null ? string.Empty
                : $"{shown.Count} joueur{(shown.Count > 1 ? "s" : string.Empty)} · partie {(session.IsOpen ? "ouverte aux nouveaux joueurs" : "fermée aux nouveaux joueurs")}"
                  + (session.PremiumOnly ? " · réservée au Premium" : string.Empty)
                  + (host ? string.Empty : "\nSeul l'hôte peut expulser un joueur ou fermer la partie.");
            openButton.gameObject.SetActive(host);
            openLabel.text = session != null && session.IsOpen ? "Fermer la partie" : "Ouvrir la partie";
        }

        static bool IsLocalSpeaking()
        {
            var game = NetworkWorkshop.Instance != null ? NetworkWorkshop.Instance.Game : null;
            return game != null && game.Voice != null && game.Voice.IsSpeaking;
        }

        void ToggleMute(int index)
        {
            if (index < 0 || index >= shown.Count || shown[index].Voice == null) return;
            var voice = shown[index].Voice;
            voice.Muted = !voice.Muted;
            messageLabel.text = voice.Muted ? $"Voix de {shown[index].DisplayName} coupée (pour vous seulement)." : $"Voix de {shown[index].DisplayName} rétablie.";
            Refresh();
        }

        void AskKick(int index)
        {
            var session = Session;
            if (session == null || !session.IsHost || index < 0 || index >= shown.Count || shown[index].IsOwner) return;
            pendingKick = shown[index].OwnerClientId;
            confirmLabel.text = $"Expulser {shown[index].DisplayName} ? Il quittera la partie et ne pourra plus la rejoindre.";
            confirmPanel.SetActive(true);
            confirmYes.Select();
        }

        void ConfirmKick()
        {
            confirmPanel.SetActive(false);
            var session = Session;
            if (session == null) return;
            string name = session.NameOf(pendingKick);
            bool done = session.Kick(pendingKick);
            messageLabel.text = done ? $"{name} a été expulsé." : "Expulsion impossible (joueur déjà parti ?).";
            Refresh();
        }

        void ToggleOpen()
        {
            var session = Session;
            if (session == null || !session.IsHost) return;
            session.SetOpen(!session.IsOpen);
            messageLabel.text = session.IsOpen ? "Partie ouverte : de nouveaux joueurs peuvent la rejoindre." : "Partie fermée : plus personne ne peut la rejoindre.";
            Refresh();
        }
    }
}
