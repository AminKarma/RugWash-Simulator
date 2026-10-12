using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>
    /// Fenêtre navigable (manette, flèches) : s'inscrit auprès du <see cref="UINavigator"/> quand elle s'affiche et se
    /// retire quand elle se cache. Se place sur l'objet activé/désactivé par la fenêtre.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NavigationScope : MonoBehaviour
    {
        [Tooltip("Élément sélectionné à l'ouverture (sinon le premier élément actif et utilisable).")]
        [SerializeField] Selectable firstSelected;
        [Tooltip("Choisi seulement s'il n'y a rien d'autre (ex. « Fermer » : l'ouverture d'une liste sélectionne sa première ligne).")]
        [SerializeField] Selectable lastResort;

        static readonly List<Selectable> Buffer = new List<Selectable>();
        UINavigator navigator;
        GameObject remembered;

        public Selectable FirstSelected
        {
            get => firstSelected;
            set => firstSelected = value;
        }

        public Selectable LastResort
        {
            get => lastResort;
            set => lastResort = value;
        }

        void OnEnable()
        {
            navigator = FindNavigator();
            navigator?.Register(this);
        }

        void OnDisable()
        {
            navigator?.Unregister(this);
        }

        static UINavigator FindNavigator()
        {
            var system = EventSystem.current;
            var found = system != null ? system.GetComponent<UINavigator>() : null;
            return found != null ? found : FindAnyObjectByType<UINavigator>();
        }

        public bool Contains(GameObject target) => target != null && target.transform.IsChildOf(transform);

        public void Remember(GameObject target) => remembered = target;

        /// <summary>
        /// Dernier élément sélectionné s'il est encore utilisable, sinon l'élément d'ouverture, sinon le premier utilisable
        /// (dans l'ordre de la hiérarchie), sinon l'élément de dernier recours.
        /// </summary>
        public Selectable PreferredSelection()
        {
            if (remembered != null && remembered.activeInHierarchy && remembered.TryGetComponent(out Selectable last) && last.IsInteractable())
                return last;
            if (firstSelected != null && firstSelected.isActiveAndEnabled && firstSelected.IsInteractable()) return firstSelected;
            GetComponentsInChildren(false, Buffer);
            Selectable result = null;
            foreach (var selectable in Buffer)
                if (selectable != lastResort && selectable.isActiveAndEnabled && selectable.IsInteractable() && selectable.navigation.mode != Navigation.Mode.None)
                {
                    result = selectable;
                    break;
                }
            Buffer.Clear();
            if (result == null && lastResort != null && lastResort.isActiveAndEnabled && lastResort.IsInteractable()) result = lastResort;
            return result;
        }
    }
}
