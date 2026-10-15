using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>Liste des produits spécialisés du jeu. L'indice + 1 sert d'identifiant compact dans la grille (0 = détergent standard).</summary>
    [CreateAssetMenu(menuName = "RugWash/Bibliothèque de produits", fileName = "ProductLibrary")]
    public sealed class ProductLibrary : ScriptableObject
    {
        public List<ProductDefinition> products = new List<ProductDefinition>();

        public ProductTable BuildTable(DirtTypeTable dirt) => new ProductTable(products, dirt);
    }

    /// <summary>
    /// Effets des produits, précalculés par (produit, type de saleté) pour les opérations de nettoyage (aucune allocation
    /// en jeu). Le produit 0 est le détergent standard : aucun bonus, aucune restriction.
    /// </summary>
    public sealed class ProductTable
    {
        public const byte Standard = 0;

        readonly List<ProductDefinition> products;
        readonly float[,] targetBoost;
        readonly float[,] dwellBoost;
        readonly float[] residue;
        readonly float[] harshness;

        public ProductTable(IReadOnlyList<ProductDefinition> definitions, DirtTypeTable dirt)
        {
            products = new List<ProductDefinition>();
            if (definitions != null)
                foreach (var definition in definitions)
                    if (definition != null && products.Count < 254) products.Add(definition);

            int count = products.Count + 1;
            targetBoost = new float[count, DirtTypes.Count];
            dwellBoost = new float[count, DirtTypes.Count];
            residue = new float[count];
            harshness = new float[count];
            for (int t = 0; t < DirtTypes.Count; t++)
            {
                targetBoost[0, t] = 1f;
                dwellBoost[0, t] = 1f;
            }
            residue[0] = 1f;

            for (int p = 0; p < products.Count; p++)
            {
                var product = products[p];
                residue[p + 1] = product.residueFactor;
                harshness[p + 1] = product.harshness;
                for (int t = 0; t < DirtTypes.Count; t++)
                {
                    bool targeted = product.targets.Contains((DirtType)t);
                    float sensitivity = dirt != null ? dirt.GetProductSensitivity((DirtType)t) : 1f;
                    targetBoost[p + 1, t] = targeted ? 1f + (product.targetBoost - 1f) * sensitivity : 1f;
                    dwellBoost[p + 1, t] = targeted ? 1f + (product.dwellBoost - 1f) * sensitivity : 1f;
                }
            }
        }

        public int Count => products.Count;

        /// <summary>Identifiant compact (1..n) d'un produit, 0 si inconnu ou standard.</summary>
        public byte IdOf(ProductDefinition product)
        {
            int index = product != null ? products.IndexOf(product) : -1;
            return index < 0 ? Standard : (byte)(index + 1);
        }

        public ProductDefinition Get(byte id) => id > 0 && id <= products.Count ? products[id - 1] : null;

        public float TargetBoost(byte product, DirtType type) => product < residue.Length ? targetBoost[product, (int)type] : 1f;
        public float DwellBoost(byte product, DirtType type) => product < residue.Length ? dwellBoost[product, (int)type] : 1f;
        public float ResidueFactor(byte product) => product < residue.Length ? residue[product] : 1f;
        public float Harshness(byte product) => product < residue.Length ? harshness[product] : 0f;

        /// <summary>Description générée à partir des valeurs réelles du produit (jamais de bonus inventé).</summary>
        public static string Describe(ProductDefinition product, DirtTypeLibrary names = null)
        {
            var text = new StringBuilder();
            if (product.targets.Count > 0)
            {
                text.Append("Cible : ");
                for (int i = 0; i < product.targets.Count; i++)
                {
                    if (i > 0) text.Append(", ");
                    text.Append(DirtTypeNames.Of(product.targets[i], names).ToLowerInvariant());
                }
                text.Append($" — brossage ×{product.targetBoost:0.#}, action au repos ×{product.dwellBoost:0.#}");
            }
            if (!Mathf.Approximately(product.residueFactor, 1f))
                text.Append(product.residueFactor < 1f ? $" · résidus ×{product.residueFactor:0.##}" : $" · résidus ×{product.residueFactor:0.##} (à bien rincer)");
            if (product.harshness > 0f) text.Append(" · abîme les tapis fragiles (soie)");
            return text.ToString();
        }
    }

    /// <summary>Noms affichés des types de saleté (bibliothèque si disponible, sinon noms de référence).</summary>
    public static class DirtTypeNames
    {
        public static string Of(DirtType type, DirtTypeLibrary library = null)
        {
            if (library != null)
                foreach (var definition in library.definitions)
                    if (definition != null && definition.type == type && !string.IsNullOrEmpty(definition.displayName))
                        return definition.displayName;
            return Default(type);
        }

        public static string Default(DirtType type)
        {
            switch (type)
            {
                case DirtType.Dust: return "Poussière légère";
                case DirtType.Mud: return "Boue humide";
                case DirtType.Soil: return "Terre";
                case DirtType.Drink: return "Taches de boissons";
                case DirtType.Grease: return "Taches grasses";
                case DirtType.HairDebris: return "Petits débris";
                case DirtType.Embedded: return "Taches incrustées";
                case DirtType.DustClumps: return "Amas de poussière";
                case DirtType.DriedMud: return "Boue sèche";
                case DirtType.PetHair: return "Poils d'animaux";
                case DirtType.Lint: return "Peluches et fibres";
                case DirtType.Urine: return "Pipi (farce)";
                case DirtType.FakeBlood: return "Faux sang (farce)";
                default: return "Aucune";
            }
        }
    }
}
