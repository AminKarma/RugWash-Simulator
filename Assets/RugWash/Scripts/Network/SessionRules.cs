using System;
using System.Collections.Generic;

namespace RugWash.Network
{
    /// <summary>État d'une session multijoueur. Le salon Steam et la connexion au jeu sont deux états distincts.</summary>
    public enum SessionState
    {
        Offline,
        /// <summary>Salon Steam en cours de création ou de connexion.</summary>
        JoiningLobby,
        /// <summary>Dans un salon Steam, pas encore connecté au jeu.</summary>
        InLobby,
        /// <summary>Connexion réseau en cours (hôte qui démarre, invité qui rejoint).</summary>
        Connecting,
        /// <summary>Connecté et synchronisé : dans l'atelier partagé.</summary>
        InGame,
        Leaving,
    }

    /// <summary>
    /// Données envoyées par un invité à la connexion : protocole (clé de compatibilité), nom affiché, version du jeu (pour
    /// le message de refus) et identité stable du joueur (reconnexion : l'ancienne connexion du même joueur est fermée).
    /// Les deux derniers champs sont facultatifs : une version plus ancienne qui ne les envoie pas est lue sans erreur.
    /// </summary>
    public readonly struct ConnectionPayload
    {
        public readonly string Version;
        public readonly string DisplayName;
        public readonly string GameVersion;
        public readonly string PlayerKey;

        public ConnectionPayload(string version, string displayName, string gameVersion = null, string playerKey = null)
        {
            Version = version ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            GameVersion = gameVersion ?? string.Empty;
            PlayerKey = playerKey ?? string.Empty;
        }

        public byte[] Encode()
        {
            var writer = new ByteWriter(128);
            writer.String(Version);
            writer.String(DisplayName);
            writer.String(GameVersion);
            writer.String(PlayerKey);
            return writer.ToArray();
        }

        public static bool TryDecode(byte[] data, out ConnectionPayload payload)
        {
            payload = default;
            if (data == null) return false;
            try
            {
                var reader = new ByteReader(data);
                string version = reader.String(), name = reader.String();
                string game = reader.Remaining > 0 ? reader.String() : string.Empty;
                string key = reader.Remaining > 0 ? reader.String() : string.Empty;
                payload = new ConnectionPayload(version, name, game, key);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }

    /// <summary>Règles pures des sessions (testées) : admission, noms, messages d'erreur.</summary>
    public static class SessionRules
    {
        public const int MaxNameLength = 24;

        /// <summary>Accepte ou refuse un invité ; <paramref name="reason"/> est affiché chez lui en cas de refus.</summary>
        public static bool Approve(byte[] payloadData, string expectedVersion, int connectedPlayers, int maxPlayers, out string displayName, out string reason) =>
            Approve(payloadData, expectedVersion, null, connectedPlayers, maxPlayers, out displayName, out reason, out _);

        /// <summary>
        /// Admission d'un invité : même protocole que l'hôte (sinon refus avant tout chargement, avec les deux versions), place
        /// libre. <paramref name="connectedPlayers"/> ne compte pas une ancienne connexion du même joueur qui sera fermée.
        /// </summary>
        public static bool Approve(byte[] payloadData, string expectedVersion, string hostGameVersion, int connectedPlayers, int maxPlayers,
            out string displayName, out string reason, out ConnectionPayload payload)
        {
            displayName = string.Empty;
            if (!ConnectionPayload.TryDecode(payloadData, out payload))
            {
                reason = "Données de connexion invalides (version du jeu trop différente de celle de l'hôte).";
                return false;
            }
            if (!string.Equals(payload.Version, expectedVersion, StringComparison.Ordinal))
            {
                reason = VersionMismatch(payload.GameVersion, payload.Version, hostGameVersion, expectedVersion);
                return false;
            }
            if (connectedPlayers >= maxPlayers)
            {
                reason = $"La partie est complète ({maxPlayers} joueurs).";
                return false;
            }
            displayName = CleanName(payload.DisplayName);
            reason = string.Empty;
            return true;
        }

        /// <summary>Message de refus pour des versions incompatibles : celle du joueur, celle de l'hôte, et quoi faire.</summary>
        public static string VersionMismatch(string clientGame, string clientProtocol, string hostGame, string hostProtocol)
        {
            string Describe(string game, string protocol) =>
                string.IsNullOrEmpty(game) ? $"protocole {protocol}" : $"{game} (protocole {protocol})";
            return $"Versions incompatibles : votre jeu {Describe(clientGame, clientProtocol)}, partie de l'hôte {Describe(hostGame, hostProtocol)}. " +
                   "Installez la même version que l'hôte pour jouer ensemble.";
        }

        /// <summary>
        /// Reconnexion : une connexion encore ouverte du même joueur (même identité), à fermer avant d'accepter la nouvelle.
        /// Sans identité, aucune éviction (impossible de savoir qui c'est).
        /// </summary>
        public static bool TryFindStaleConnection(IReadOnlyDictionary<ulong, string> keys, string playerKey, ulong newClientId, out ulong staleClientId)
        {
            staleClientId = 0;
            if (string.IsNullOrEmpty(playerKey) || keys == null) return false;
            foreach (var pair in keys)
                if (pair.Key != newClientId && string.Equals(pair.Value, playerKey, StringComparison.Ordinal))
                {
                    staleClientId = pair.Key;
                    return true;
                }
            return false;
        }

        /// <summary>Nom affiché sûr : caractères de contrôle retirés, longueur bornée, « Joueur » si vide.</summary>
        public static string CleanName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Joueur";
            var chars = new List<char>(name.Length);
            foreach (char c in name.Trim())
                if (!char.IsControl(c) && c != '<' && c != '>') chars.Add(c);
            var clean = new string(chars.ToArray()).Trim();
            if (clean.Length > MaxNameLength) clean = clean.Substring(0, MaxNameLength).TrimEnd();
            return clean.Length == 0 ? "Joueur" : clean;
        }

        /// <summary>Adresse de connexion directe acceptable (IPv4 ou nom d'hôte simple) et port.</summary>
        public static bool TryParseAddress(string text, ushort defaultPort, out string address, out ushort port)
        {
            address = string.Empty;
            port = defaultPort;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            int colon = text.LastIndexOf(':');
            if (colon > 0)
            {
                if (!ushort.TryParse(text.Substring(colon + 1), out port) || port == 0) return false;
                text = text.Substring(0, colon);
            }
            if (string.Equals(text, "localhost", StringComparison.OrdinalIgnoreCase)) text = "127.0.0.1";
            if (!System.Net.IPAddress.TryParse(text, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
            address = text;
            return true;
        }

        /// <summary>Message affiché après une déconnexion, selon qui l'a provoquée.</summary>
        public static string DisconnectMessage(bool wasHost, bool requestedLocally, string serverReason)
        {
            if (requestedLocally) return wasHost ? "Partie fermée." : "Vous avez quitté la partie.";
            if (!string.IsNullOrEmpty(serverReason)) return serverReason;
            return wasHost ? "La session réseau s'est arrêtée." : "Connexion perdue avec l'hôte.";
        }
    }
}
