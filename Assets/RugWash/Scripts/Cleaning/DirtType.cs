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
    }

    public static class DirtTypes
    {
        public const int Count = 8;

        /// <summary>Salissures sèches et non fixées : stockées dans le canal poussière d'une cellule.</summary>
        public static bool IsLooseDry(DirtType type) => type == DirtType.Dust || type == DirtType.HairDebris;
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
