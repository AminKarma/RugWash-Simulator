using System;
using System.Collections.Generic;
using UnityEngine;

namespace RugWash.Player
{
    /// <summary>Choix de personnalisation d'un joueur (sauvegardés chez lui, transmis aux autres joueurs en ligne).</summary>
    [Serializable]
    public struct CharacterLook : IEquatable<CharacterLook>
    {
        public int outfit;
        public int cap;
        public int gloves;

        public bool Equals(CharacterLook other) => outfit == other.outfit && cap == other.cap && gloves == other.gloves;
        public override bool Equals(object obj) => obj is CharacterLook other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(outfit, cap, gloves);

        /// <summary>Format réseau compact (un entier).</summary>
        public int Pack() => (outfit & 0xFF) | ((cap & 0xFF) << 8) | ((gloves & 0xFF) << 16);
        public static CharacterLook Unpack(int packed) =>
            CharacterPalette.Clamp(new CharacterLook { outfit = packed & 0xFF, cap = (packed >> 8) & 0xFF, gloves = (packed >> 16) & 0xFF });
    }

    /// <summary>
    /// Options réellement disponibles : la silhouette est un placeholder procédural, seules les couleurs de la tenue de
    /// travail, de la casquette (ou pas de casquette) et des gants peuvent changer.
    /// </summary>
    public static class CharacterPalette
    {
        public static readonly (string name, Color color)[] Outfits =
        {
            ("Bleu marine", new Color(0.16f, 0.24f, 0.36f)), ("Rouge brique", new Color(0.5f, 0.16f, 0.12f)),
            ("Vert forêt", new Color(0.14f, 0.32f, 0.2f)), ("Gris anthracite", new Color(0.2f, 0.21f, 0.23f)),
            ("Orange sécurité", new Color(0.85f, 0.42f, 0.1f)), ("Bleu ciel", new Color(0.3f, 0.52f, 0.75f)),
        };

        /// <summary>Indice 0 : pas de casquette.</summary>
        public static readonly (string name, Color color)[] Caps =
        {
            ("Aucune", Color.clear), ("Rouge", new Color(0.74f, 0.2f, 0.14f)), ("Bleue", new Color(0.15f, 0.3f, 0.65f)),
            ("Jaune", new Color(0.92f, 0.72f, 0.12f)), ("Noire", new Color(0.08f, 0.08f, 0.09f)),
        };

        public static readonly (string name, Color color)[] Gloves =
        {
            ("Orange", new Color(0.86f, 0.52f, 0.18f)), ("Noirs", new Color(0.1f, 0.1f, 0.11f)), ("Bleus", new Color(0.18f, 0.38f, 0.75f)),
        };

        /// <summary>Apparence d'origine (celle des modèles avant la personnalisation).</summary>
        public static CharacterLook Default => new CharacterLook { outfit = 0, cap = 1, gloves = 0 };

        public static CharacterLook Clamp(CharacterLook look) => new CharacterLook
        {
            outfit = Wrap(look.outfit, Outfits.Length), cap = Wrap(look.cap, Caps.Length), gloves = Wrap(look.gloves, Gloves.Length),
        };

        public static int Wrap(int value, int count) => ((value % count) + count) % count;
    }

    /// <summary>
    /// Applique une <see cref="CharacterLook"/> à une silhouette (tenue, casquette, gants) ou aux gants de la vue à la première
    /// personne, par blocs de propriétés (les matériaux partagés ne sont jamais modifiés).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterAppearance : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] List<Renderer> outfitRenderers = new List<Renderer>();
        [SerializeField] List<Renderer> capRenderers = new List<Renderer>();
        [SerializeField] List<Renderer> gloveRenderers = new List<Renderer>();

        MaterialPropertyBlock block;

        public CharacterLook Current { get; private set; } = CharacterPalette.Default;
        public int OutfitRendererCount => outfitRenderers.Count;
        public int GloveRendererCount => gloveRenderers.Count;
        public bool CapVisible => capRenderers.Count > 0 && capRenderers[0] != null && capRenderers[0].enabled;

        public void Apply(CharacterLook look)
        {
            look = CharacterPalette.Clamp(look);
            Current = look;
            block ??= new MaterialPropertyBlock();
            Paint(outfitRenderers, CharacterPalette.Outfits[look.outfit].color);
            Paint(gloveRenderers, CharacterPalette.Gloves[look.gloves].color);
            bool cap = look.cap > 0;
            foreach (var renderer in capRenderers)
                if (renderer != null) renderer.enabled = cap;
            if (cap) Paint(capRenderers, CharacterPalette.Caps[look.cap].color);
        }

        /// <summary>Couleur réellement appliquée au premier rendu d'une catégorie (tests).</summary>
        public Color AppliedColor(int slot)
        {
            var list = slot == 0 ? outfitRenderers : slot == 1 ? capRenderers : gloveRenderers;
            if (list.Count == 0 || list[0] == null) return Color.clear;
            block ??= new MaterialPropertyBlock();
            list[0].GetPropertyBlock(block);
            return block.GetColor(BaseColorId);
        }

        void Paint(List<Renderer> renderers, Color color)
        {
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(block);
                block.SetColor(BaseColorId, color);
                renderer.SetPropertyBlock(block);
            }
        }
    }
}
