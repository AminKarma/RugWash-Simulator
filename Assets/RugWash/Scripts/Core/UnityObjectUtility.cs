using UnityEngine;

namespace RugWash.Core
{
    public static class UnityObjectUtility
    {
        /// <summary>Destroy en jeu, DestroyImmediate en mode édition (tests EditMode, outils).</summary>
        public static void SafeDestroy(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }
    }
}
