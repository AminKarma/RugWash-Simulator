using System.Collections.Generic;
using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>
    /// Produit de nettoyage spécialisé, ajouté au détergent des pulvérisateurs (détergent, canon à mousse) tant qu'il est
    /// sélectionné et en stock. Chaque valeur est réellement appliquée par les opérations de nettoyage ; la description
    /// affichée au joueur est générée à partir de ces valeurs (voir <see cref="ProductTable.Describe"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "RugWash/Produit", fileName = "Product_")]
    public sealed class ProductDefinition : ScriptableObject
    {
        public string id = "product";
        public string displayName = "Produit";
        [TextArea] public string description;

        [Header("Efficacité")]
        [Tooltip("Types de saleté ciblés.")]
        public List<DirtType> targets = new List<DirtType>();
        [Tooltip("Multiplicateur du brossage sur mousse pour les types ciblés.")]
        [Range(1f, 4f)] public float targetBoost = 2f;
        [Tooltip("Multiplicateur de l'action de la mousse au repos pour les types ciblés.")]
        [Range(1f, 5f)] public float dwellBoost = 1.5f;
        [Tooltip("Multiplicateur des résidus déposés (< 1 = rince mieux).")]
        [Range(0.2f, 2f)] public float residueFactor = 1f;

        [Header("Restrictions")]
        [Tooltip("Agressivité pour les fibres : use les tapis fragiles tant que la mousse reste en place (0 = sans risque).")]
        [Range(0f, 1f)] public float harshness;

        [Header("Consommation et vente")]
        [Tooltip("Litres consommés par seconde de pulvérisation, en plus du détergent.")]
        [Min(0.001f)] public float litersPerSecond = 0.05f;
        [Tooltip("Contenance d'un flacon vendu (L).")]
        [Min(0.1f)] public float packLiters = 2f;
        [Min(0)] public int packPrice = 80;
        [Tooltip("Stock maximal transportable (L).")]
        [Min(0.1f)] public float maxStock = 6f;
    }
}
