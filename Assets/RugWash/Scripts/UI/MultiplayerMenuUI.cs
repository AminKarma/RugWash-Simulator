using System.Collections.Generic;
using System.Text;
using RugWash.Core;
using RugWash.Network;
using RugWash.Save;
using UnityEngine;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>
    /// Fenêtre « Multijoueur » du menu principal.
    /// <list type="bullet">
    /// <item>Steam (Internet) : créer un salon (amis ou public), rechercher et rejoindre une partie, inviter des amis via
    /// Steam, voir les membres du salon, lancer la partie (hôte), quitter le salon.</item>
    /// <item>Réseau local : héberger sur ce PC ou rejoindre une adresse (connexion directe, sans invitation).</item>
    /// </list>
    /// Chaque bouton n'est actif que lorsque son action est possible ; les erreurs s'affichent sans bloquer l'interface.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MultiplayerMenuUI : MonoBehaviour, IModalPanel
    {
        [SerializeField] GameObject panel;
        [SerializeField] NetworkContent content;
        [SerializeField] Text steamStatusLabel;
        [SerializeField] Button createFriendsButton;
        [SerializeField] Button createPublicButton;
        [SerializeField] Button searchButton;
        [SerializeField] Transform lobbyListRoot;
        [SerializeField] ListRowView lobbyRowTemplate;
        [SerializeField] Text lobbyMembersLabel;
        [SerializeField] Button inviteButton;
        [SerializeField] Button startButton;
        [SerializeField] Button leaveLobbyButton;
        [SerializeField] InputField nameField;
        [SerializeField] InputField addressField;
        [SerializeField] Button hostDirectButton;
        [SerializeField] Button joinDirectButton;
        [SerializeField] Button reconnectButton;
        [SerializeField] Text messageLabel;
        [SerializeField] Button closeButton;
        [Header("Partie réservée au Premium (grande mise à jour)")]
        [SerializeField] Button premiumButton;
        [SerializeField] Text premiumLabel;
        [SerializeField] Text premiumNote;
        [Header("Emplacement de sauvegarde multijoueur")]
        [SerializeField] Text slotLabel;
        [SerializeField] Button slotButton;

        static readonly Color Ok = new Color(0.55f, 0.95f, 0.6f);
        static readonly Color Warn = new Color(1f, 0.6f, 0.45f);

        readonly List<ListRowView> lobbyRows = new List<ListRowView>();
        List<LobbySummary> lobbies = new List<LobbySummary>();
        MultiplayerSession session;
        SaveManager save;
        SaveSlotsUI saveSlots;

        /// <summary>Emplacement multijoueur utilisé pour héberger ou rejoindre (jamais un emplacement solo).</summary>
        public SaveSlot ChosenSlot { get; private set; } = new SaveSlot(SaveMode.Multiplayer, 1);
        public Button SlotButton => slotButton;
        public string SlotText => slotLabel.text;

        /// <summary>Relie la fenêtre aux sauvegardes : emplacement multijoueur choisi, rappelé d'une partie à l'autre.</summary>
        public void BindSaves(SaveManager saveManager, SaveSlotsUI slots)
        {
            save = saveManager;
            saveSlots = slots;
            if (save != null) ChosenSlot = save.Profile.LastSlot(SaveMode.Multiplayer);
            slotButton.onClick.AddListener(() =>
            {
                if (saveSlots == null) return;
                saveSlots.Highlighted = ChosenSlot;
                saveSlots.Open(SaveMode.Multiplayer);
            });
        }

        /// <summary>Emplacement multijoueur choisi dans la fenêtre des emplacements.</summary>
        public void SetSlot(SaveSlot slot)
        {
            if (slot.Mode != SaveMode.Multiplayer) return;
            ChosenSlot = slot;
            if (save != null)
            {
                save.Profile.lastMultiSlot = slot.Index;
                save.SaveProfile();
            }
            RefreshSlot();
        }

        void RefreshSlot()
        {
            var summary = SaveSlots.ReadSummary(ChosenSlot);
            string state = !summary.Exists ? "vide : progression de départ copiée depuis l'hôte à la première partie"
                : !summary.Readable ? summary.Problem
                : !summary.Data.multiplayerInitialized ? "pas encore jouée : progression copiée depuis l'hôte à la première partie"
                : $"{Economy.WalletManager.Format(summary.Data.money)} · réputation {summary.Data.reputation} · {summary.PlayTimeText}";
            slotLabel.text = $"Sauvegarde : {ChosenSlot.DisplayName} — {state}";
        }

        /// <summary>Avant d'héberger ou de rejoindre : l'atelier chargera cet emplacement.</summary>
        void UseChosenSlot() => SaveSlots.Selected = ChosenSlot;

        public bool IsOpen { get; private set; }
        public MultiplayerSession Session => session;
        public Button HostDirectButton => hostDirectButton;
        public Button JoinDirectButton => joinDirectButton;
        public Button CreateFriendsButton => createFriendsButton;
        public InputField AddressField => addressField;
        public InputField NameField => nameField;
        public string Message => messageLabel.text;
        public string SteamStatus => steamStatusLabel.text;

        public bool IsConfigured(out string missing)
        {
            missing = panel == null ? nameof(panel) : content == null ? nameof(content) : steamStatusLabel == null ? nameof(steamStatusLabel)
                : createFriendsButton == null || createPublicButton == null ? "createButtons" : searchButton == null ? nameof(searchButton)
                : premiumButton == null || premiumLabel == null || premiumNote == null ? nameof(premiumButton)
                : lobbyListRoot == null || lobbyRowTemplate == null ? "lobbyList" : lobbyMembersLabel == null ? nameof(lobbyMembersLabel)
                : inviteButton == null || startButton == null || leaveLobbyButton == null ? "lobbyButtons"
                : nameField == null || addressField == null ? "fields" : hostDirectButton == null || joinDirectButton == null ? "directButtons"
                : reconnectButton == null ? nameof(reconnectButton) : messageLabel == null ? nameof(messageLabel) : closeButton == null ? nameof(closeButton)
                : slotLabel == null || slotButton == null ? "slot" : null;
            return missing == null;
        }

        void Awake()
        {
            if (!IsConfigured(out _)) return;
            lobbyRowTemplate.gameObject.SetActive(false);
            premiumButton.onClick.AddListener(() =>
            {
                session.SetPremiumOnly(!session.PremiumOnly);
                Refresh();
            });
            createFriendsButton.onClick.AddListener(() => Run(() => session.CreateSteamLobby(false)));
            createPublicButton.onClick.AddListener(() => Run(() => session.CreateSteamLobby(true)));
            searchButton.onClick.AddListener(() => Run(session.SearchSteamLobbies));
            inviteButton.onClick.AddListener(() =>
            {
                if (!session.InviteFriends()) Show("La fenêtre d'invitation de Steam n'a pas pu s'ouvrir (overlay Steam désactivé ?).", false);
            });
            startButton.onClick.AddListener(() => Run(() => session.StartSteamGame()));
            leaveLobbyButton.onClick.AddListener(() => Run(() => session.Leave("Salon quitté.")));
            hostDirectButton.onClick.AddListener(HostDirect);
            joinDirectButton.onClick.AddListener(JoinDirect);
            reconnectButton.onClick.AddListener(() => Run(() => session.Reconnect()));
            closeButton.onClick.AddListener(Close);
            panel.SetActive(false);
        }

        void OnDestroy() => Unhook();

        public bool Open()
        {
            if (IsOpen) return false;
            session = MultiplayerSession.GetOrCreate(content);
            session.Changed += Refresh;
            session.LobbiesFound += OnLobbiesFound;
            if (string.IsNullOrWhiteSpace(nameField.text)) nameField.text = session.LocalName;
            if (string.IsNullOrWhiteSpace(addressField.text)) addressField.text = $"127.0.0.1:{content.defaultPort}";
            session.EnsureSteam();
            panel.SetActive(true);
            IsOpen = true;
            RefreshSlot();
            Refresh();
            return true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            Unhook();
            panel.SetActive(false);
            IsOpen = false;
        }

        void Unhook()
        {
            if (session == null) return;
            session.Changed -= Refresh;
            session.LobbiesFound -= OnLobbiesFound;
        }

        void Run(System.Action action)
        {
            ApplyName();
            UseChosenSlot();
            action();
            Refresh();
        }

        void ApplyName()
        {
            if (session.Steam == null || !session.Steam.IsInitialized) session.LocalName = SessionRules.CleanName(nameField.text);
        }

        public void HostDirect()
        {
            ApplyName();
            UseChosenSlot();
            if (!session.HostDirect(content.defaultPort)) Show(session.Message, false);
            Refresh();
        }

        public void JoinDirect()
        {
            ApplyName();
            UseChosenSlot();
            if (!SessionRules.TryParseAddress(addressField.text, content.defaultPort, out string address, out ushort port))
            {
                Show("Adresse invalide : indiquez une adresse IPv4, éventuellement suivie de « :port » (ex. 192.168.1.20:7777).", false);
                return;
            }
            if (!session.JoinDirect(address, port)) Show(session.Message, false);
            Refresh();
        }

        void OnLobbiesFound(List<LobbySummary> found)
        {
            lobbies = found ?? new List<LobbySummary>();
            Show(lobbies.Count == 0 ? "Aucune partie trouvée (amis ou publiques)." : $"{lobbies.Count} partie(s) trouvée(s).", lobbies.Count > 0);
            RefreshLobbies();
        }

        void Show(string message, bool success)
        {
            messageLabel.text = message ?? string.Empty;
            messageLabel.color = success ? Ok : Warn;
        }

        void Refresh()
        {
            if (session == null) return;
            var steam = session.Steam;
            bool steamReady = steam != null && steam.IsInitialized;
            bool busy = session.State == SessionState.Connecting || session.State == SessionState.JoiningLobby || session.State == SessionState.Leaving;
            bool inLobby = steamReady && steam.InLobby;
            bool owner = inLobby && steam.IsLobbyOwner;

            steamStatusLabel.text = steamReady
                ? $"Steam : connecté en tant que {steam.PersonaName}"
                : $"Steam indisponible — {(steam != null && !string.IsNullOrEmpty(steam.InitError) ? steam.InitError : "lancez Steam pour jouer en ligne")}";
            createFriendsButton.interactable = steamReady && !inLobby && !busy && !session.InSession;
            createPublicButton.interactable = createFriendsButton.interactable;
            searchButton.interactable = steamReady && !inLobby && !busy;
            inviteButton.interactable = inLobby;
            startButton.interactable = owner && !session.InSession && !busy;
            leaveLobbyButton.interactable = inLobby && !busy;
            hostDirectButton.interactable = !session.InSession && !inLobby && !busy;
            joinDirectButton.interactable = hostDirectButton.interactable;
            nameField.interactable = !steamReady && !session.InSession;
            slotButton.interactable = !session.InSession && !busy;
            reconnectButton.gameObject.SetActive(session.CanReconnect && session.LastEndedWithError);
            // Premium : seulement si la propriété peut vraiment être vérifiée par Steam ; sinon l'option est grisée et expliquée.
            bool premiumAvailable = session.PremiumAvailable;
            premiumButton.interactable = premiumAvailable && (!inLobby || owner);
            premiumLabel.text = session.PremiumOnly ? "Premium uniquement : oui" : "Premium uniquement : non";
            premiumNote.text = premiumAvailable
                ? "Si activé, l'hôte fait vérifier par Steam que chaque joueur qui rejoint possède l'édition Premium."
                : session.PremiumUnavailableReason;

            var members = new StringBuilder();
            if (inLobby)
            {
                members.Append(owner ? "Votre salon" : "Salon de l'hôte").Append(" — membres (dans le salon ; pas encore en jeu) :\n");
                foreach (var member in steam.Members()) members.Append("• ").Append(member.Name).Append(member.IsOwner ? " (hôte)" : string.Empty).Append('\n');
                if (owner) members.Append("Lancez la partie quand tout le monde est là ; les retardataires pourront rejoindre en cours de route.");
            }
            else members.Append("Pas de salon. Créez une partie ou rejoignez-en une.");
            lobbyMembersLabel.text = members.ToString();

            if (!string.IsNullOrEmpty(session.Message)) Show(session.Message, !session.LastEndedWithError);
            RefreshLobbies();
        }

        void RefreshLobbies()
        {
            while (lobbyRows.Count < lobbies.Count)
            {
                var row = Instantiate(lobbyRowTemplate, lobbyListRoot);
                int index = lobbyRows.Count;
                row.ActionButton.onClick.AddListener(() => JoinLobby(index));
                lobbyRows.Add(row);
            }
            for (int i = 0; i < lobbyRows.Count; i++)
            {
                bool used = i < lobbies.Count;
                lobbyRows[i].gameObject.SetActive(used);
                if (!used) continue;
                var lobby = lobbies[i];
                bool full = lobby.Members >= lobby.MaxMembers && lobby.MaxMembers > 0;
                lobbyRows[i].Set($"Partie de {(string.IsNullOrEmpty(lobby.HostName) ? "?" : lobby.HostName)}",
                    $"{lobby.Members} / {lobby.MaxMembers} joueurs · {(lobby.InGame ? "en cours" : "dans le salon")}",
                    full ? "Complet" : "Ouvert", full ? Warn : Ok, "Rejoindre", !full && session != null && !session.InSession,
                    new Color(0.12f, 0.15f, 0.18f, 0.95f));
            }
        }

        void JoinLobby(int index)
        {
            if (index < 0 || index >= lobbies.Count) return;
            Run(() => session.JoinSteamLobby(lobbies[index].Id));
        }
    }
}
