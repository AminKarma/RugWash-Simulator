using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>
    /// Zone « Actualités » du menu principal : charge les annonces locales en arrière-plan (sans bloquer le menu) et les
    /// affiche ; en cas d'absence ou d'erreur, un message neutre remplace la liste.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NewsPanelUI : MonoBehaviour
    {
        [Tooltip("Fichier JSON dans un dossier Resources (sans extension).")]
        [SerializeField] string resourcePath = "RugWashNews";
        [SerializeField] Text bodyLabel;
        [SerializeField] Color metaColor = UITheme.Saffron;

        ResourceRequest request;

        public bool IsLoaded { get; private set; }
        public IReadOnlyList<NewsItem> Items { get; private set; } = new List<NewsItem>();
        /// <summary>Erreur de chargement ou de lecture (null si tout va bien).</summary>
        public string Error { get; private set; }
        public string DisplayedText => bodyLabel != null ? bodyLabel.text : string.Empty;

        public bool IsConfigured(out string missing)
        {
            missing = bodyLabel == null ? nameof(bodyLabel) : string.IsNullOrEmpty(resourcePath) ? nameof(resourcePath) : null;
            return missing == null;
        }

        void Start()
        {
            bodyLabel.text = "Chargement des actualités…";
            request = Resources.LoadAsync<TextAsset>(resourcePath);
            request.completed += OnLoaded;
        }

        void OnDestroy()
        {
            if (request != null) request.completed -= OnLoaded;
        }

        void OnLoaded(AsyncOperation operation)
        {
            if (this == null) return;
            var asset = request.asset as TextAsset;
            string error = null;
            Items = asset != null ? NewsFeed.Parse(asset.text, out error) : new List<NewsItem>();
            Error = asset == null ? "fichier d'actualités introuvable" : error;
            if (Error != null) Debug.LogWarning($"[RugWash] Actualités : {Error}.");
            if (asset != null) Resources.UnloadAsset(asset);
            bodyLabel.text = Render(Items, metaColor);
            IsLoaded = true;
        }

        public static string Render(IReadOnlyList<NewsItem> items, Color metaColor)
        {
            if (items == null || items.Count == 0) return "Aucune actualité pour le moment.";
            string hex = ColorUtility.ToHtmlStringRGB(metaColor);
            var text = new StringBuilder();
            foreach (var item in items)
            {
                if (text.Length > 0) text.Append("\n\n");
                text.Append("<b>").Append(item.title).Append("</b>");
                string meta = NewsFeed.Meta(item);
                if (meta.Length > 0) text.Append("\n<size=18><color=#").Append(hex).Append('>').Append(meta).Append("</color></size>");
                if (item.body.Length > 0) text.Append('\n').Append(item.body);
            }
            return text.ToString();
        }
    }
}
