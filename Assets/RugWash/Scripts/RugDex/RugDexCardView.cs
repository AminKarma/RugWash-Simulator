using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RugWash.RugDex
{
    /// <summary>
    /// Carte du RugDex : motif du tapis (silhouette sombre s'il est inconnu), numéro et nom.
    /// Recevoir le focus (manette, clavier) affiche sa fiche, comme un clic.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RugDexCardView : MonoBehaviour, ISelectHandler
    {
        static readonly Color Normal = new Color(0.12f, 0.15f, 0.18f, 0.95f);
        static readonly Color Selected = new Color(0.16f, 0.45f, 0.58f, 0.98f);

        [SerializeField] Image frame;
        [SerializeField] RawImage thumbnail;
        [SerializeField] Text label;
        [SerializeField] Button button;

        public Button Button => button;

        /// <summary>La carte vient de recevoir le focus de navigation.</summary>
        public event Action<RugDexCardView> Focused;

        public void OnSelect(BaseEventData eventData) => Focused?.Invoke(this);
        public string Label => label.text;
        public bool ShowsKnownRug { get; private set; }

        public void Set(Texture texture, string text, bool known, bool selected)
        {
            thumbnail.texture = texture;
            thumbnail.color = known ? Color.white : new Color(0.05f, 0.05f, 0.06f, 1f);
            label.text = text;
            label.color = known ? Color.white : new Color(1f, 1f, 1f, 0.55f);
            ShowsKnownRug = known;
            SetSelected(selected);
        }

        public void SetSelected(bool selected) => frame.color = selected ? Selected : Normal;
    }
}
