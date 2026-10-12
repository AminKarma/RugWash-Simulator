using System.Collections.Generic;
using RugWash.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>
    /// Navigation des menus sans souris (manette, flèches) : chaque fenêtre ouverte est une <see cref="NavigationScope"/>,
    /// la dernière ouverte reçoit le focus. À l'ouverture, son premier élément est sélectionné ; à la fermeture, la fenêtre
    /// du dessous retrouve l'élément qu'elle avait. La sélection ne peut pas rester sur une fenêtre recouverte, et un
    /// élément sélectionné dans une liste défilante est ramené dans la zone visible.
    /// Sans fenêtre ouverte (jeu), la sélection est vidée : « valider » ne peut pas déclencher un bouton caché.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EventSystem))]
    public sealed class UINavigator : MonoBehaviour
    {
        [SerializeField] InputDeviceMonitor monitor;
        [Tooltip("Marge (pixels de référence) gardée autour d'un élément ramené dans une liste défilante.")]
        [SerializeField] float scrollMargin = 12f;

        readonly List<NavigationScope> scopes = new List<NavigationScope>();
        EventSystem eventSystem;
        GameObject lastSelected;
        bool focusPending;

        public NavigationScope Top => scopes.Count > 0 ? scopes[scopes.Count - 1] : null;
        public int ScopeCount => scopes.Count;
        public InputDeviceMonitor Monitor => monitor;

        void Awake() => eventSystem = GetComponent<EventSystem>();

        void OnEnable()
        {
            // Fenêtres déjà actives avant ce composant (ordre d'activation de la scène).
            foreach (var scope in FindObjectsByType<NavigationScope>(FindObjectsInactive.Exclude))
                Register(scope);
            if (monitor != null) monitor.SchemeChanged += OnSchemeChanged;
        }

        void OnDisable()
        {
            if (monitor != null) monitor.SchemeChanged -= OnSchemeChanged;
        }

        void OnSchemeChanged(InputScheme scheme)
        {
            if (scheme == InputScheme.Gamepad) focusPending = true;
        }

        public void Register(NavigationScope scope)
        {
            if (scope == null || scopes.Contains(scope)) return;
            scopes.Add(scope);
            focusPending = true;
        }

        public void Unregister(NavigationScope scope)
        {
            if (scopes.Remove(scope)) focusPending = true;
        }

        // Après EventSystem.Update : une fenêtre ouverte par « valider » ne reçoit pas cette même pression.
        void LateUpdate()
        {
            if (eventSystem == null) return;
            var selected = eventSystem.currentSelectedGameObject;
            var top = Top;
            if (top == null)
            {
                if (selected != null) eventSystem.SetSelectedGameObject(null);
                lastSelected = null;
                focusPending = false;
                return;
            }

            bool inTop = selected != null && selected.activeInHierarchy && top.Contains(selected);
            bool wantsFocus = focusPending || (selected != null && !inTop) || (selected == null && monitor != null && monitor.IsGamepad);
            if (!inTop && wantsFocus)
            {
                var target = top.PreferredSelection();
                eventSystem.SetSelectedGameObject(target != null ? target.gameObject : null);
                selected = eventSystem.currentSelectedGameObject;
                inTop = selected != null;
            }
            focusPending = false;

            if (!inTop || selected == lastSelected) return;
            lastSelected = selected;
            top.Remember(selected);
            var scroll = selected.GetComponentInParent<ScrollRect>();
            if (scroll != null) ScrollIntoView(scroll, (RectTransform)selected.transform, scrollMargin);
        }

        /// <summary>Décale le contenu de la liste pour que l'élément soit entièrement visible (axe vertical).</summary>
        public static void ScrollIntoView(ScrollRect scroll, RectTransform target, float margin)
        {
            var content = scroll.content;
            if (content == null || target == content || !target.IsChildOf(content)) return;
            var viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, target);
            Rect view = viewport.rect;
            float shift = 0f;
            if (bounds.max.y > view.yMax) shift = bounds.max.y - view.yMax + margin;
            else if (bounds.min.y < view.yMin) shift = bounds.min.y - view.yMin - margin;
            if (Mathf.Approximately(shift, 0f)) return;
            scroll.StopMovement();
            content.anchoredPosition -= new Vector2(0f, shift);
        }
    }
}
