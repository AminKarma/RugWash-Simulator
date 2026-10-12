using System;
using UnityEngine;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>Sélecteur « ‹ valeur › » (résolution, mode d'affichage, qualité, oui/non).</summary>
    [DisallowMultipleComponent]
    public sealed class StepperView : MonoBehaviour
    {
        [SerializeField] Text valueLabel;
        [SerializeField] Button previousButton;
        [SerializeField] Button nextButton;

        Action<int> stepped;
        bool wired;

        /// <summary>
        /// Pas de -1 ou +1. Les boutons sont branchés dès le premier abonné, même si le sélecteur n'a jamais été affiché
        /// (onglet de paramètres pas encore ouvert) : Awake ne s'exécute qu'à la première activation.
        /// </summary>
        public event Action<int> Stepped
        {
            add
            {
                WireButtons();
                stepped += value;
            }
            remove => stepped -= value;
        }

        public string Value => valueLabel.text;
        public Button PreviousButton => previousButton;
        public Button NextButton => nextButton;

        public bool IsConfigured => valueLabel != null && previousButton != null && nextButton != null;

        void Awake() => WireButtons();

        void WireButtons()
        {
            if (wired || !IsConfigured) return;
            wired = true;
            previousButton.onClick.AddListener(() => stepped?.Invoke(-1));
            nextButton.onClick.AddListener(() => stepped?.Invoke(1));
        }

        public void SetValue(string text) => valueLabel.text = text;
    }
}
