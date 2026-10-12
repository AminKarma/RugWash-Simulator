using System.Collections.Generic;
using System.Text;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Orders;
using UnityEngine;
using UnityEngine.UI;

namespace RugWash.RugDex
{
    /// <summary>
    /// Fenêtre du RugDex (menu principal et menu pause) : une carte par tapis du catalogue, construite à partir des données
    /// réelles (motif, catégorie, dimensions, fragilité, salissures, commandes), fiche détaillée, conditions de découverte
    /// et pourcentage de collection. Ajouter un tapis au catalogue suffit pour qu'il apparaisse.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RugDexUI : MonoBehaviour, IModalPanel
    {
        static readonly Color Unknown = new Color(0.05f, 0.05f, 0.06f, 1f);

        [SerializeField] GameObject panel;
        [SerializeField] Text completionLabel;
        [SerializeField] Transform cardRoot;
        [SerializeField] RugDexCardView cardTemplate;
        [SerializeField] RawImage detailImage;
        [SerializeField] AspectRatioFitter detailAspect;
        [SerializeField] Text detailTitle;
        [SerializeField] Text detailBody;
        [SerializeField] Button closeButton;

        readonly List<RugDexCardView> cards = new List<RugDexCardView>();
        RugCatalog catalog;
        RugDexBook book;
        IReadOnlyList<OrderDefinition> orders;
        IReadOnlyList<RugDefinition> practiceRugs;
        DirtTypeLibrary dirtNames;
        GameStateManager gameState;
        int selected;

        public bool IsOpen { get; private set; }
        public IReadOnlyList<RugDexCardView> Cards => cards;
        public string CompletionText => completionLabel.text;
        public string DetailText => detailTitle.text + "\n" + detailBody.text;

        public bool IsConfigured(out string missing)
        {
            missing = panel == null ? nameof(panel) : completionLabel == null ? nameof(completionLabel) : cardRoot == null ? nameof(cardRoot)
                : cardTemplate == null ? nameof(cardTemplate) : detailImage == null ? nameof(detailImage) : detailTitle == null ? nameof(detailTitle)
                : detailBody == null ? nameof(detailBody) : closeButton == null ? nameof(closeButton) : null;
            return missing == null;
        }

        /// <param name="state">Optionnel : présent dans l'atelier (fenêtre modale), absent du menu principal.</param>
        /// <param name="practice">Tapis d'entraînement accessibles depuis le menu pause (découverte possible sans commande).</param>
        public void Bind(RugCatalog rugCatalog, RugDexBook progress, IReadOnlyList<OrderDefinition> orderList, IReadOnlyList<RugDefinition> practice,
            DirtTypeLibrary names, GameStateManager state)
        {
            catalog = rugCatalog;
            book = progress;
            orders = orderList;
            practiceRugs = practice;
            dirtNames = names;
            gameState = state;
            cardTemplate.gameObject.SetActive(false);
            closeButton.onClick.AddListener(Close);
            for (int i = 0; i < catalog.rugs.Count; i++)
            {
                var card = Instantiate(cardTemplate, cardRoot);
                card.gameObject.SetActive(true);
                int index = i;
                card.Button.onClick.AddListener(() => Select(index));
                card.Focused += _ => { if (index != selected) Select(index); };
                cards.Add(card);
            }
            book.Changed += RefreshIfOpen;
            panel.SetActive(false);
        }

        void OnDestroy()
        {
            if (book != null) book.Changed -= RefreshIfOpen;
        }

        public bool Open()
        {
            if (IsOpen) return false;
            panel.SetActive(true);
            IsOpen = true;
            gameState?.PushModal();
            Refresh();
            return true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            panel.SetActive(false);
            IsOpen = false;
            gameState?.PopModal();
        }

        void RefreshIfOpen()
        {
            if (IsOpen) Refresh();
        }

        void Refresh()
        {
            int total = catalog.rugs.Count;
            int found = book.DiscoveredCount(catalog.rugs);
            completionLabel.text = $"Collection : {found} / {total} tapis ({RugDexBook.CompletionPercent(found, total):0} %)";
            for (int i = 0; i < cards.Count; i++)
            {
                var rug = catalog.rugs[i];
                bool known = rug != null && book.IsDiscovered(rug.id);
                cards[i].Set(rug != null ? rug.patternTexture : null, $"N° {i + 1:00} · {(known ? rug.displayName : "???")}", known, i == selected);
            }
            Select(Mathf.Clamp(selected, 0, Mathf.Max(0, cards.Count - 1)));
        }

        public void Select(int index)
        {
            if (index < 0 || index >= catalog.rugs.Count) return;
            selected = index;
            for (int i = 0; i < cards.Count; i++) cards[i].SetSelected(i == selected);
            // Le focus de navigation suit la fiche affichée (sélection par clic ou par le jeu) ; il s'ouvre sur elle.
            var scope = panel.GetComponent<UI.NavigationScope>();
            if (scope != null && index < cards.Count) scope.FirstSelected = cards[index].Button;
            var events = UnityEngine.EventSystems.EventSystem.current;
            var focused = events != null ? events.currentSelectedGameObject : null;
            if (focused != null && index < cards.Count && focused != cards[index].Button.gameObject && focused.GetComponent<RugDexCardView>() != null)
                events.SetSelectedGameObject(cards[index].Button.gameObject);
            var rug = catalog.rugs[index];
            bool known = rug != null && book.IsDiscovered(rug.id);
            detailImage.texture = rug != null ? rug.patternTexture : null;
            detailImage.color = known ? Color.white : Unknown;
            if (detailAspect != null && rug != null) detailAspect.aspectRatio = rug.sizeMeters.x / Mathf.Max(0.01f, rug.sizeMeters.y);
            detailTitle.text = known ? $"N° {index + 1:00} · {rug.displayName}" : $"N° {index + 1:00} · Tapis inconnu";
            detailBody.text = known ? DescribeKnown(rug) : DescribeUnknown(rug);
        }

        string DescribeKnown(RugDefinition rug)
        {
            var text = new StringBuilder();
            text.AppendLine($"Catégorie : {RugDefinition.CategoryLabel(rug.pileType)}");
            text.AppendLine($"Dimensions : {rug.sizeMeters.x:0.0} × {rug.sizeMeters.y:0.0} m · épaisseur {rug.thickness * 1000f:0} mm");
            text.AppendLine($"Fragilité des fibres : {RugDefinition.FragilityLabel(rug.fragility)}");
            text.AppendLine($"Salissures rencontrées : {DirtSeen(rug)}");
            text.AppendLine($"Commandes : {OrdersWith(rug, includeReputation: false)}");
            var record = book.Get(rug.id);
            text.AppendLine(record != null && record.timesDelivered > 0
                ? $"Nettoyé et livré : {record.timesDelivered} fois · meilleure note : {record.bestGrade}"
                : "Pas encore livré à un client");
            if (!string.IsNullOrEmpty(rug.description)) text.Append(rug.description);
            return text.ToString().TrimEnd();
        }

        string DescribeUnknown(RugDefinition rug)
        {
            if (rug == null) return "Entrée vide.";
            bool practice = practiceRugs != null && ((IList<RugDefinition>)practiceRugs).Contains(rug);
            string practiceHint = practice ? "\nOu à l'entraînement : menu pause → « Tapis suivant »." : string.Empty;
            OrderDefinition first = null;
            if (orders != null)
                foreach (var order in orders)
                    if (order != null && order.rug == rug && (first == null || order.requiredReputation < first.requiredReputation)) first = order;
            return first == null
                ? $"Catégorie : {RugDefinition.CategoryLabel(rug.pileType)}\nAucune commande actuelle ne propose ce tapis.{practiceHint}"
                : $"Catégorie : {RugDefinition.CategoryLabel(rug.pileType)}\nPour le découvrir : accepter la commande « {first.title} » de {first.clientName}" +
                  (first.requiredReputation > 0 ? $" (réputation {first.requiredReputation} requise)." : ".") + practiceHint;
        }

        string DirtSeen(RugDefinition rug)
        {
            var types = new List<DirtType>();
            void Add(IEnumerable<DirtLayer> layers)
            {
                if (layers == null) return;
                foreach (var layer in layers)
                    if (layer.type != DirtType.None && !types.Contains(layer.type)) types.Add(layer.type);
            }
            Add(rug.defaultDirt);
            if (orders != null)
                foreach (var order in orders)
                    if (order != null && order.rug == rug) Add(order.dirt);
            if (types.Count == 0) return "—";
            var names = new List<string>();
            foreach (var type in types) names.Add(DirtTypeNames.Of(type, dirtNames).ToLowerInvariant());
            return string.Join(", ", names);
        }

        string OrdersWith(RugDefinition rug, bool includeReputation)
        {
            var titles = new List<string>();
            if (orders != null)
                foreach (var order in orders)
                    if (order != null && order.rug == rug)
                        titles.Add(includeReputation ? $"« {order.title} » (réputation {order.requiredReputation})" : $"« {order.title} »");
            return titles.Count == 0 ? "aucune" : string.Join(", ", titles);
        }
    }
}
