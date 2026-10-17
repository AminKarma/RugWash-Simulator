using System.Collections.Generic;
using UnityEngine;

namespace RugWash.Effects
{
    /// <summary>
    /// Texte 3D du décor (panneaux, affiches, afficheur de la machine à laver, étiquettes des machines). Le matériau intégré des
    /// polices (« GUI/Text Shader ») ne teste pas la profondeur : ces textes se voyaient à travers les murs. Ce composant lui
    /// substitue en jeu un matériau testé en profondeur (« RugWash/World Text »), partagé par police et tenu à jour quand Unity
    /// reconstruit la texture d'une police dynamique. Sans shader pris en charge, le matériau d'origine reste.
    /// Les noms au-dessus des joueurs n'ont pas ce composant : ils restent visibles à travers les murs, volontairement.
    /// </summary>
    [RequireComponent(typeof(TextMesh))]
    public class WorldText : MonoBehaviour
    {
        [SerializeField] Shader shader;

        static readonly Dictionary<Font, Material> Materials = new Dictionary<Font, Material>();
        static bool listening;

        void Awake()
        {
            var material = MaterialFor(GetComponent<TextMesh>().font, shader);
            var meshRenderer = GetComponent<MeshRenderer>();
            if (material != null && meshRenderer != null) meshRenderer.sharedMaterial = material;
        }

        /// <summary>Matériau testé en profondeur pour cette police (créé au premier appel), ou null si impossible.</summary>
        public static Material MaterialFor(Font font, Shader shader)
        {
            if (font == null || font.material == null || shader == null || !shader.isSupported) return null;
            if (Materials.TryGetValue(font, out var material) && material != null) return material;
            material = new Material(shader) { name = $"World Text ({font.name})", hideFlags = HideFlags.DontSave };
            material.mainTexture = font.material.mainTexture;
            Materials[font] = material;
            if (!listening)
            {
                Font.textureRebuilt += OnTextureRebuilt;
                listening = true;
            }
            return material;
        }

        static void OnTextureRebuilt(Font font)
        {
            if (font != null && font.material != null && Materials.TryGetValue(font, out var material) && material != null)
                material.mainTexture = font.material.mainTexture;
        }
    }
}
