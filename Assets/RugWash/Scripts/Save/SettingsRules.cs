namespace RugWash.Save
{
    /// <summary>
    /// Réglages graphiques (grande mise à jour, règles pures testées) : quatre préréglages qui changent vraiment le rendu,
    /// synchronisation verticale indépendante, limite d'images cohérente avec elle.
    /// </summary>
    public static class SettingsRules
    {
        /// <summary>Niveaux de qualité du projet utilisés par les préréglages : Very Low, Low, Medium, Ultra.</summary>
        public static readonly int[] PresetLevels = { 0, 1, 2, 5 };
        public static readonly string[] PresetNames = { "Très bas", "Bas", "Moyen", "Ultra" };
        /// <summary>Ce que change chaque préréglage (affiché dans les paramètres).</summary>
        public static readonly string[] PresetDescriptions =
        {
            "Très bas : rendu à 70 %, aucune ombre, pas d'occlusion ambiante ni d'anticrénelage, textures en demi-résolution, HDR coupé.",
            "Bas : rendu à 85 %, pas d'ombres des projecteurs, pas d'occlusion ambiante ni d'anticrénelage.",
            "Moyen : rendu complet, ombres des projecteurs (1024), occlusion ambiante à mi-résolution, sans anticrénelage MSAA.",
            "Ultra : rendu complet, ombres 2048, occlusion ambiante pleine résolution, anticrénelage MSAA 4×, sondes de reflets en temps réel.",
        };
        /// <summary>Limites d'images proposées (0 = illimitée).</summary>
        public static readonly int[] FrameRateCaps = { 0, 30, 60, 120, 144, 165, 240 };

        /// <summary>Préréglage d'un niveau de qualité : les niveaux retirés du menu (Élevé, Très élevé) sont rangés avec Ultra.</summary>
        public static int PresetOf(int qualityLevel)
        {
            for (int i = 0; i < PresetLevels.Length; i++)
                if (PresetLevels[i] == qualityLevel) return i;
            return qualityLevel <= 0 ? 0 : PresetLevels.Length - 1;
        }

        /// <summary>Images par seconde visées : -1 (pas de limite) avec la synchronisation verticale ou sans limite choisie.</summary>
        public static int TargetFrameRate(bool vSync, int cap) => vSync || cap <= 0 ? -1 : cap;

        public static string FrameRateLabel(bool vSync, int cap) =>
            vSync ? "Réglée par la synchronisation" : cap <= 0 ? "Illimitée" : $"{cap} images/s";

        /// <summary>Limite suivante (ou précédente) dans la liste.</summary>
        public static int StepCap(int cap, int direction)
        {
            int index = System.Array.IndexOf(FrameRateCaps, cap);
            if (index < 0) index = 0;
            index = (index + direction + FrameRateCaps.Length) % FrameRateCaps.Length;
            return FrameRateCaps[index];
        }
    }
}
