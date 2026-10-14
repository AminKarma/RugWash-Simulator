using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace RugWash.UI
{
    /// <summary>Annonce du menu principal (titre, texte court, date et version facultatives).</summary>
    [Serializable]
    public sealed class NewsItem
    {
        public string id = string.Empty;
        public string date = string.Empty;
        public string version = string.Empty;
        public string title = string.Empty;
        public string body = string.Empty;
    }

    /// <summary>
    /// Annonces locales versionnées : un fichier JSON livré avec le jeu (<c>Resources/RugWashNews.json</c>), modifié à
    /// chaque mise à jour. Aucune connexion réseau ni clé : le menu les présente comme des annonces locales. Lecture
    /// robuste (règles pures, testées en EditMode) : un fichier invalide ne bloque jamais le menu, une entrée invalide est
    /// ignorée, les textes trop longs sont tronqués.
    /// </summary>
    public static class NewsFeed
    {
        public const int Format = 1;
        public const int MaxItems = 3;
        public const int MaxTitleLength = 80;
        public const int MaxBodyLength = 320;

        [Serializable]
        sealed class NewsFile
        {
            public int format;
            public List<NewsItem> items = new List<NewsItem>();
        }

        /// <summary>Annonces valides, de la plus récente à la plus ancienne (au plus <see cref="MaxItems"/>).</summary>
        public static List<NewsItem> Parse(string json, out string error)
        {
            var result = new List<NewsItem>();
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "fichier d'actualités vide";
                return result;
            }
            NewsFile file;
            try
            {
                file = JsonUtility.FromJson<NewsFile>(json);
            }
            catch (Exception exception)
            {
                error = $"fichier d'actualités illisible ({exception.GetType().Name})";
                return result;
            }
            if (file == null || file.items == null || file.items.Count == 0)
            {
                error = "fichier d'actualités sans entrée";
                return result;
            }
            if (file.format != Format)
            {
                error = $"format d'actualités {file.format} non pris en charge";
                return result;
            }
            foreach (var raw in file.items)
            {
                var item = Clean(raw);
                if (item != null) result.Add(item);
            }
            if (result.Count == 0)
            {
                error = "aucune annonce valide";
                return result;
            }
            // Les plus récentes d'abord ; ordre du fichier conservé entre entrées de même date (tri stable).
            var ordered = new List<NewsItem>(result);
            ordered.Sort((a, b) =>
            {
                int byDate = string.CompareOrdinal(b.date, a.date);
                return byDate != 0 ? byDate : result.IndexOf(a).CompareTo(result.IndexOf(b));
            });
            if (ordered.Count > MaxItems) ordered.RemoveRange(MaxItems, ordered.Count - MaxItems);
            return ordered;
        }

        static NewsItem Clean(NewsItem raw)
        {
            if (raw == null) return null;
            string title = Sanitize(raw.title, MaxTitleLength);
            if (title.Length == 0) return null;
            string date = (raw.date ?? string.Empty).Trim();
            // Date au format AAAA-MM-JJ (sinon ignorée : l'annonce reste affichée sans date).
            if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) date = string.Empty;
            return new NewsItem
            {
                id = Sanitize(raw.id, 40),
                date = date,
                version = Sanitize(raw.version, 20),
                title = title,
                body = Sanitize(raw.body, MaxBodyLength),
            };
        }

        /// <summary>Texte d'une ligne sûr pour le texte enrichi d'uGUI (balises neutralisées), tronqué proprement.</summary>
        static string Sanitize(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            text = text.Replace('<', '‹').Replace('>', '›').Replace("\r", string.Empty).Trim();
            return text.Length <= max ? text : text.Substring(0, max - 1).TrimEnd() + "…";
        }

        /// <summary>Date affichée (JJ/MM/AAAA) et version, séparées par « · » ; vide si aucune n'est connue.</summary>
        public static string Meta(NewsItem item)
        {
            var parts = new List<string>(2);
            if (DateTime.TryParseExact(item.date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                parts.Add(date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(item.version)) parts.Add($"version {item.version}");
            return string.Join(" · ", parts);
        }
    }
}
