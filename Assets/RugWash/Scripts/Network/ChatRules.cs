using System.Collections.Generic;
using System.Text;

namespace RugWash.Network
{
    /// <summary>
    /// Règles du chat textuel en ligne (grande mise à jour, règles pures testées) : texte nettoyé (caractères de contrôle,
    /// espaces, longueur) chez l'émetteur ET chez l'hôte, cadence limitée par l'hôte (un client modifié ne peut pas inonder
    /// le chat), historique borné.
    /// </summary>
    public static class ChatRules
    {
        public const int MaxLength = 120;
        /// <summary>Délai minimal entre deux messages d'un même joueur (s).</summary>
        public const float MinInterval = 0.6f;
        /// <summary>Au plus <see cref="BurstMessages"/> messages par fenêtre de <see cref="BurstWindow"/> secondes.</summary>
        public const int BurstMessages = 5;
        public const float BurstWindow = 10f;

        /// <summary>Texte affichable : sans caractères de contrôle, espaces regroupés, borné à <see cref="MaxLength"/> caractères.</summary>
        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var builder = new StringBuilder(text.Length);
            bool space = false;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    space = builder.Length > 0;
                    continue;
                }
                // Caractères de contrôle, emojis et autres caractères hors police (paires de substitution) : retirés.
                if (char.IsControl(c) || char.IsSurrogate(c)) continue;
                if (space)
                {
                    builder.Append(' ');
                    space = false;
                }
                builder.Append(c);
                if (builder.Length >= MaxLength) break;
            }
            return builder.ToString();
        }
    }

    /// <summary>Limite de cadence par joueur, tenue par l'hôte.</summary>
    public sealed class ChatRateLimiter
    {
        readonly Dictionary<ulong, Queue<float>> sent = new Dictionary<ulong, Queue<float>>();

        public bool Allow(ulong clientId, float now)
        {
            if (!sent.TryGetValue(clientId, out var times)) sent[clientId] = times = new Queue<float>();
            while (times.Count > 0 && now - times.Peek() > ChatRules.BurstWindow) times.Dequeue();
            if (times.Count >= ChatRules.BurstMessages) return false;
            float last = 0f;
            foreach (float time in times) last = time;
            if (times.Count > 0 && now - last < ChatRules.MinInterval) return false;
            times.Enqueue(now);
            return true;
        }

        public void Forget(ulong clientId) => sent.Remove(clientId);
    }

    /// <summary>Ligne du chat (joueur ou message de l'atelier).</summary>
    public readonly struct ChatLine
    {
        public readonly string Sender;
        public readonly string Text;
        public readonly bool System;
        public readonly float Time;

        public ChatLine(string sender, string text, bool system, float time)
        {
            Sender = sender;
            Text = text;
            System = system;
            Time = time;
        }

        public string Format() => System || string.IsNullOrEmpty(Sender) ? Text : $"{Sender} : {Text}";
    }

    /// <summary>Historique borné du chat (dernières lignes).</summary>
    public sealed class ChatLog
    {
        public const int Capacity = 40;
        readonly List<ChatLine> lines = new List<ChatLine>();

        public IReadOnlyList<ChatLine> Lines => lines;
        public int Version { get; private set; }

        public void Add(ChatLine line)
        {
            lines.Add(line);
            if (lines.Count > Capacity) lines.RemoveAt(0);
            Version++;
        }
    }
}
