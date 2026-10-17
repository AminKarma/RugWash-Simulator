using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace RugWash.Network
{
    /// <summary>Salon trouvé par la recherche Steam.</summary>
    public readonly struct LobbySummary
    {
        public readonly ulong Id;
        public readonly string HostName;
        public readonly int Members;
        public readonly int MaxMembers;
        public readonly bool InGame;

        public LobbySummary(ulong id, string hostName, int members, int maxMembers, bool inGame)
        {
            Id = id;
            HostName = hostName;
            Members = members;
            MaxMembers = maxMembers;
            InGame = inGame;
        }
    }

    /// <summary>Membre d'un salon Steam (présent dans le salon, pas forcément connecté au jeu).</summary>
    public readonly struct LobbyMember
    {
        public readonly ulong SteamId;
        public readonly string Name;
        public readonly bool IsOwner;

        public LobbyMember(ulong steamId, string name, bool isOwner)
        {
            SteamId = steamId;
            Name = name;
            IsOwner = isOwner;
        }
    }

    /// <summary>
    /// Fonctions Steam du jeu (Steamworks.NET) : initialisation, salons (création, recherche, entrée, sortie, données),
    /// invitations (fenêtre d'invitation de Steam, invitations acceptées en jeu ou au lancement « +connect_lobby »).
    /// Steam gère les sessions ; le jeu lui-même passe par Netcode for GameObjects (transport Steam Networking Sockets).
    /// Sans client Steam lancé, l'initialisation échoue proprement : le reste du jeu fonctionne.
    /// </summary>
    public sealed class SteamPlatform : IDisposable
    {
        public const string GameKey = "game";
        public const string GameValue = "rugwash";
        public const string VersionKey = "version";
        public const string HostKey = "host";
        public const string HostNameKey = "host_name";
        public const string StateKey = "state";
        /// <summary>Grande mise à jour : partie fermée par l'hôte (« 1 »), réservée au Premium (« 1 »).</summary>
        public const string LockKey = "locked";
        public const string PremiumKey = "premium";

        Callback<LobbyEnter_t> lobbyEnter;
        Callback<LobbyChatUpdate_t> lobbyChatUpdate;
        Callback<LobbyDataUpdate_t> lobbyDataUpdate;
        Callback<GameLobbyJoinRequested_t> joinRequested;
        CallResult<LobbyCreated_t> lobbyCreated;
        CallResult<LobbyMatchList_t> lobbyList;

        public bool IsInitialized { get; private set; }
        public string InitError { get; private set; } = string.Empty;
        public ulong LocalSteamId { get; private set; }
        public string PersonaName { get; private set; } = string.Empty;
        public ulong CurrentLobby { get; private set; }
        public bool InLobby => CurrentLobby != 0;

        public event Action<ulong> LobbyCreated;
        public event Action<ulong> LobbyEntered;
        public event Action LobbyMembersChanged;
        public event Action LobbyDataChanged;
        public event Action<List<LobbySummary>> LobbyListReceived;
        /// <summary>Invitation acceptée depuis Steam (ou « Rejoindre la partie » d'un ami) pendant que le jeu tourne.</summary>
        public event Action<ulong> JoinRequested;
        public event Action<string> Error;

        /// <summary>Initialise Steam avec l'App ID donné (variable d'environnement SteamAppId, sans fichier à livrer).</summary>
        public bool Initialize(uint appId)
        {
            if (IsInitialized) return true;
            try
            {
                if (!Packsize.Test()) return Fail("Steamworks.NET : mauvaise configuration de la plateforme (Packsize).");
                if (!DllCheck.Test()) return Fail("Steamworks.NET : bibliothèque steam_api64 introuvable ou incorrecte.");
                Environment.SetEnvironmentVariable("SteamAppId", appId.ToString());
                Environment.SetEnvironmentVariable("SteamGameId", appId.ToString());
                var result = SteamAPI.InitEx(out string message);
                if (result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
                    return Fail(result == ESteamAPIInitResult.k_ESteamAPIInitResult_NoSteamClient
                        ? "Steam n'est pas lancé : lancez Steam (compte connecté) pour jouer en ligne."
                        : $"Steam n'a pas pu être initialisé : {message}");
            }
            catch (DllNotFoundException e)
            {
                return Fail($"Bibliothèque Steam absente : {e.Message}");
            }

            IsInitialized = true;
            InitError = string.Empty;
            LocalSteamId = SteamUser.GetSteamID().m_SteamID;
            PersonaName = SteamFriends.GetPersonaName();
            lobbyEnter = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
            lobbyChatUpdate = Callback<LobbyChatUpdate_t>.Create(_ => LobbyMembersChanged?.Invoke());
            lobbyDataUpdate = Callback<LobbyDataUpdate_t>.Create(OnLobbyData);
            joinRequested = Callback<GameLobbyJoinRequested_t>.Create(data => JoinRequested?.Invoke(data.m_steamIDLobby.m_SteamID));
            lobbyCreated = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            lobbyList = CallResult<LobbyMatchList_t>.Create(OnLobbyList);
            SteamNetworkingUtils.InitRelayNetworkAccess();
            return true;
        }

        bool Fail(string message)
        {
            InitError = message;
            IsInitialized = false;
            return false;
        }

        public void RunCallbacks()
        {
            if (IsInitialized) SteamAPI.RunCallbacks();
        }

        /// <summary>Salon passé au lancement par Steam (invitation acceptée jeu fermé) : « +connect_lobby &lt;id&gt; ».</summary>
        public static ulong LobbyFromCommandLine(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out ulong id)) return id;
            return 0;
        }

        string pendingVersion = string.Empty;

        public void CreateLobby(bool publicLobby, int maxMembers, string version)
        {
            if (!IsInitialized) return;
            pendingVersion = version ?? string.Empty;
            var call = SteamMatchmaking.CreateLobby(publicLobby ? ELobbyType.k_ELobbyTypePublic : ELobbyType.k_ELobbyTypeFriendsOnly, Mathf.Clamp(maxMembers, 2, 4));
            lobbyCreated.Set(call);
        }

        void OnLobbyCreated(LobbyCreated_t data, bool ioFailure)
        {
            if (ioFailure || data.m_eResult != EResult.k_EResultOK)
            {
                Error?.Invoke($"Création du salon Steam impossible ({(ioFailure ? "erreur réseau" : data.m_eResult.ToString())}).");
                return;
            }
            CurrentLobby = data.m_ulSteamIDLobby;
            var lobby = new CSteamID(CurrentLobby);
            SteamMatchmaking.SetLobbyData(lobby, GameKey, GameValue);
            SteamMatchmaking.SetLobbyData(lobby, VersionKey, pendingVersion);
            SteamMatchmaking.SetLobbyData(lobby, HostNameKey, PersonaName);
            SteamMatchmaking.SetLobbyData(lobby, StateKey, "lobby");
            LobbyCreated?.Invoke(CurrentLobby);
        }

        /// <summary>Recherche les salons RugWash de la même version (amis et publics visibles par Steam).</summary>
        public void RequestLobbies(string version)
        {
            if (!IsInitialized) return;
            SteamMatchmaking.AddRequestLobbyListStringFilter(GameKey, GameValue, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter(VersionKey, version, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            SteamMatchmaking.AddRequestLobbyListResultCountFilter(30);
            lobbyList.Set(SteamMatchmaking.RequestLobbyList());
        }

        void OnLobbyList(LobbyMatchList_t data, bool ioFailure)
        {
            var list = new List<LobbySummary>();
            if (ioFailure)
            {
                Error?.Invoke("Recherche de salons impossible (erreur réseau Steam).");
                LobbyListReceived?.Invoke(list);
                return;
            }
            for (int i = 0; i < data.m_nLobbiesMatching; i++)
            {
                var id = SteamMatchmaking.GetLobbyByIndex(i);
                list.Add(new LobbySummary(id.m_SteamID, SteamMatchmaking.GetLobbyData(id, HostNameKey),
                    SteamMatchmaking.GetNumLobbyMembers(id), SteamMatchmaking.GetLobbyMemberLimit(id),
                    SteamMatchmaking.GetLobbyData(id, StateKey) == "playing"));
            }
            LobbyListReceived?.Invoke(list);
        }

        public void JoinLobby(ulong lobbyId)
        {
            if (!IsInitialized || lobbyId == 0) return;
            SteamMatchmaking.JoinLobby(new CSteamID(lobbyId));
        }

        void OnLobbyEnter(LobbyEnter_t data)
        {
            var response = (EChatRoomEnterResponse)data.m_EChatRoomEnterResponse;
            if (response != EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                Error?.Invoke(DescribeEnterFailure(response));
                return;
            }
            CurrentLobby = data.m_ulSteamIDLobby;
            LobbyEntered?.Invoke(CurrentLobby);
        }

        public static string DescribeEnterFailure(EChatRoomEnterResponse response)
        {
            switch (response)
            {
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseFull: return "Le salon est complet.";
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseDoesntExist: return "Le salon n'existe plus.";
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseNotAllowed:
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseBanned: return "Accès au salon refusé.";
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseLimited: return "Compte Steam limité : impossible de rejoindre un salon.";
                default: return $"Impossible de rejoindre le salon ({response}).";
            }
        }

        void OnLobbyData(LobbyDataUpdate_t data)
        {
            if (data.m_ulSteamIDLobby == CurrentLobby) LobbyDataChanged?.Invoke();
        }

        public void LeaveLobby()
        {
            if (!IsInitialized || CurrentLobby == 0) return;
            SteamMatchmaking.LeaveLobby(new CSteamID(CurrentLobby));
            CurrentLobby = 0;
        }

        public void SetLobbyData(string key, string value)
        {
            if (IsInitialized && CurrentLobby != 0) SteamMatchmaking.SetLobbyData(new CSteamID(CurrentLobby), key, value ?? string.Empty);
        }

        public string GetLobbyData(string key) =>
            IsInitialized && CurrentLobby != 0 ? SteamMatchmaking.GetLobbyData(new CSteamID(CurrentLobby), key) : string.Empty;

        /// <summary>Un salon fermé n'est plus rejoignable ni listé (pas de salon fantôme après le départ de l'hôte).</summary>
        public void SetJoinable(bool joinable)
        {
            if (IsInitialized && CurrentLobby != 0) SteamMatchmaking.SetLobbyJoinable(new CSteamID(CurrentLobby), joinable);
        }

        public ulong LobbyOwner() => IsInitialized && CurrentLobby != 0 ? SteamMatchmaking.GetLobbyOwner(new CSteamID(CurrentLobby)).m_SteamID : 0;

        public bool IsLobbyOwner => InLobby && LobbyOwner() == LocalSteamId;

        public List<LobbyMember> Members()
        {
            var list = new List<LobbyMember>();
            if (!IsInitialized || CurrentLobby == 0) return list;
            var lobby = new CSteamID(CurrentLobby);
            ulong owner = LobbyOwner();
            int count = SteamMatchmaking.GetNumLobbyMembers(lobby);
            for (int i = 0; i < count; i++)
            {
                var member = SteamMatchmaking.GetLobbyMemberByIndex(lobby, i);
                list.Add(new LobbyMember(member.m_SteamID, SteamFriends.GetFriendPersonaName(member), member.m_SteamID == owner));
            }
            return list;
        }

        /// <summary>Ouvre la fenêtre d'invitation de Steam pour le salon courant (liste d'amis de Steam).</summary>
        public bool OpenInviteOverlay()
        {
            if (!IsInitialized || CurrentLobby == 0) return false;
            SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(CurrentLobby));
            return true;
        }

        public void Dispose()
        {
            LeaveLobby();
            lobbyEnter?.Dispose();
            lobbyChatUpdate?.Dispose();
            lobbyDataUpdate?.Dispose();
            joinRequested?.Dispose();
            lobbyCreated?.Dispose();
            lobbyList?.Dispose();
            lobbyEnter = null;
            lobbyChatUpdate = null;
            lobbyDataUpdate = null;
            joinRequested = null;
            lobbyCreated = null;
            lobbyList = null;
            if (IsInitialized) SteamAPI.Shutdown();
            IsInitialized = false;
        }
    }
}
