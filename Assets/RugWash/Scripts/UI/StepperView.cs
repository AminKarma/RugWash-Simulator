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

        public event Action<int> Stepped;

        public string Value => valueLabel.text;
        public Button PreviousButton => previousButton;
        public Button NextButton => nextButton;

        public bool IsConfigured => valueLabel != null && previousButton != null && nextButton != null;

        void Awake()
        {
            previousButton.onClick.AddListener(() => Stepped?.Invoke(-1));
            nextButton.onClick.AddListener(() => Stepped?.Invoke(1));
        }

        public void SetValue(string text) => valueLabel.text = text;
    }
}
