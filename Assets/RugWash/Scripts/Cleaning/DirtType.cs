using System;
using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>Catégories de salissures. Les valeurs sont sérialisées : ne pas renuméroter.</summary>
    public enum DirtType : byte
    {
        None = 0,
        Dust = 1,
        Mud = 2,
        Soil = 3,
        Drink = 4,
        Grease = 5,
        HairDebris = 6,
        Embedded = 7,
        /// <summary>Amas de poussière : plus épais que la poussière légère, l'aspirateur peine, le balai les emporte.</summary>
        DustClumps = 8,
        /// <summary>Boue sèche : croûte fixée qui s'effrite au brossage à sec, presque insensible au jet d'eau.</summary>
        DriedMud = 9,
        /// <summary>Poils d'animaux : s'accrochent aux fibres ; la brosse et le balai les décollent mieux que l'aspirateur.</summary>
        PetHair = 10,
        /// <summary>Peluches et fibres en surface : très légères, l'aspirateur les enlève facilement.</summary>
        Lint = 11,
        /// <summary>Farce « pipi » (v0.1.2) : auréole jaune et humide ; détergent, brossage et rinçage l'enlèvent.</summary>
        Urine = 12,
        /// <summary>Faux sang de théâtre (farces, v0.1.2) : éclaboussures stylisées rouge vif ; détergent et brossage humide.</summary>
        FakeBlood = 13,
    }

    public static class DirtTypes
    {
        public const int Count = 14;

        /// <summary>Salissures créées par les farces entre joueurs (temporaires, nettoyables comme les autres).</summary>
        public static bool IsPrank(DirtType type) => type == DirtType.Urine || type == DirtType.FakeBlood;

        /// <summary>Salissures sèches et non fixées : stockées dans le canal poussière d'une cellule.</summary>
        public static bool IsLooseDry(DirtType type) =>
            type == DirtType.Dust || type == DirtType.HairDebris || type == DirtType.DustClumps || type == DirtType.PetHair || type == DirtType.Lint;

        /// <summary>Fibres et petits débris (poils, cheveux, peluches) : ils s'accrochent au velours.</summary>
        public static bool IsFiber(DirtType type) => type == DirtType.PetHair || type == DirtType.HairDebris || type == DirtType.Lint;

        /// <summary>
        /// Aspect d'une salissure sèche dans le shader du tapis : 0 voile fin (poussière), 0,5 grains
        /// (amas, petits débris), 1 brins (poils d'animaux, peluches). Purement visuel.
        /// </summary>
        public static float LooseLook(DirtType type)
        {
            switch (type)
            {
                case DirtType.PetHair:
                case DirtType.Lint: return 1f;
                case DirtType.DustClumps:
                case DirtType.HairDebris: return 0.5f;
                default: return 0f;
            }
        }
    }

    /// <summary>
    /// Réaction d'un type de saleté à chaque traitement. Les valeurs sont des multiplicateurs
    /// de la puissance de l'outil (1 = réponse nominale, 0 = sans effet).
    /// </summary>
    [Serializable]
    public struct DirtResponse
    {
        [Tooltip("Aspiration (salissures sèches).")]
        [Range(0f, 2f)] public float vacuum;
        [Tooltip("Brossage à sec.")]
        [Range(0f, 2f)] public float brushDry;
        [Tooltip("Brossage sur fibres humides.")]
        [Range(0f, 2f)] public float brushWet;
        [Tooltip("Brossage avec mousse de détergent.")]
        [Range(0f, 2f)] public float brushSoap;
        [Tooltip("Retrait direct par le jet d'eau, sans brossage préalable.")]
        [Range(0f, 2f)] public float rinse;
        [Tooltip("Balayage (débris secs non fixés : poils, poussière).")]
        [Range(0f, 2f)] public float sweep;

        public DirtResponse(float vacuum, float brushDry, float brushWet, float brushSoap, float rinse, float sweep = 0f)
        {
            this.vacuum = vacuum;
            this.brushDry = brushDry;
            this.brushWet = brushWet;
            this.brushSoap = brushSoap;
            this.rinse = rinse;
            this.sweep = sweep;
        }
    }
}
