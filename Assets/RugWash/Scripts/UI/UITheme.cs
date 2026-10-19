using UnityEngine;

namespace RugWash.UI
{
    /// <summary>
    /// Charte de l'interface (V0.3.0) : source unique des couleurs de l'interface, partagée par le générateur de scènes et
    /// par les fenêtres qui recolorent leurs lignes à l'exécution.
    /// Surfaces anthracite légèrement bleutées, accent safran (action principale, focus), pétrole (information,
    /// progression, sélection), crème pour le texte. États : vert réussite, ambre avertissement, rouge erreur.
    /// Typographie : Roboto Black pour les titres, Inter (Regular, SemiBold) pour le texte — polices libres (Apache 2.0, OFL)
    /// copiées dans Assets/RugWash/Fonts. Rayon des panneaux 14 px, des boutons 14 px, espacement de base 8 px.
    /// </summary>
    public static class UITheme
    {
        // ---- Surfaces
        /// <summary>Voile derrière une fenêtre modale.</summary>
        public static readonly Color Backdrop = new Color(0.02f, 0.03f, 0.035f, 0.72f);
        /// <summary>Fond des fenêtres.</summary>
        public static readonly Color Window = new Color(0.075f, 0.09f, 0.105f, 0.97f);
        /// <summary>Cartes du HUD (translucides, lisibles sur l'atelier).</summary>
        public static readonly Color HudCard = new Color(0.06f, 0.075f, 0.09f, 0.82f);
        /// <summary>Bandeau d'en-tête des fenêtres.</summary>
        public static readonly Color Header = new Color(0.1f, 0.125f, 0.145f, 1f);
        /// <summary>Bord fin des panneaux (contour clair à faible opacité).</summary>
        public static readonly Color Outline = new Color(1f, 1f, 1f, 0.09f);
        public static readonly Color Shadow = new Color(0f, 0f, 0f, 0.55f);
        /// <summary>Fond d'une jauge.</summary>
        public static readonly Color Track = new Color(0f, 0f, 0f, 0.45f);

        // ---- Lignes de liste
        public static readonly Color Row = new Color(0.115f, 0.137f, 0.157f, 0.96f);
        public static readonly Color RowActive = new Color(0.09f, 0.255f, 0.29f, 0.98f);
        public static readonly Color RowOwned = new Color(0.1f, 0.22f, 0.165f, 0.97f);
        public static readonly Color RowLocked = new Color(0.085f, 0.09f, 0.1f, 0.92f);
        public static readonly Color RowEmpty = new Color(0.1f, 0.115f, 0.13f, 0.9f);

        // ---- Marque
        public static readonly Color Petrol = new Color(0.118f, 0.306f, 0.361f, 1f);
        /// <summary>Pétrole lumineux : jauges, informations, page courante.</summary>
        public static readonly Color PetrolBright = new Color(0.22f, 0.66f, 0.7f, 1f);
        public static readonly Color Saffron = new Color(0.949f, 0.663f, 0.231f, 1f);
        public static readonly Color SaffronBright = new Color(1f, 0.76f, 0.36f, 1f);

        // ---- Texte
        public static readonly Color Text = new Color(0.96f, 0.95f, 0.92f, 1f);
        public static readonly Color TextSecondary = new Color(0.76f, 0.79f, 0.81f, 1f);
        public static readonly Color TextMuted = new Color(0.56f, 0.6f, 0.63f, 1f);
        /// <summary>Texte sur un bouton safran.</summary>
        public static readonly Color TextOnAccent = new Color(0.1f, 0.08f, 0.05f, 1f);

        // ---- États
        public static readonly Color Success = new Color(0.45f, 0.86f, 0.53f, 1f);
        public static readonly Color Warning = new Color(1f, 0.72f, 0.34f, 1f);
        public static readonly Color Danger = new Color(0.95f, 0.42f, 0.36f, 1f);
        public static readonly Color Gold = new Color(1f, 0.83f, 0.38f, 1f);
        public static readonly Color Money = new Color(1f, 0.86f, 0.5f, 1f);

        // ---- Boutons
        public static readonly Color Button = new Color(0.15f, 0.178f, 0.2f, 0.98f);
        public static readonly Color ButtonHover = new Color(0.19f, 0.39f, 0.43f, 1f);
        public static readonly Color ButtonPressed = new Color(0.12f, 0.29f, 0.32f, 1f);
        public static readonly Color ButtonDisabled = new Color(0.26f, 0.27f, 0.28f, 0.5f);
        /// <summary>Élément sélectionné à la manette ou au clavier.</summary>
        public static readonly Color Focus = Saffron;
        /// <summary>Onglet de la page affichée.</summary>
        public static readonly Color TabCurrent = new Color(0.14f, 0.43f, 0.48f, 1f);

        // ---- HUD
        public static readonly Color SlotNormal = new Color(0.06f, 0.075f, 0.09f, 0.78f);
        public static readonly Color SlotActive = new Color(0.13f, 0.44f, 0.49f, 0.94f);
        public static readonly Color SlotEmpty = new Color(0.06f, 0.075f, 0.09f, 0.4f);
    }
}
