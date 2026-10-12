using System;
using System.Collections.Generic;
using RugWash.Cleaning;
using RugWash.Orders;
using UnityEngine;

namespace RugWash.RugDex
{
    /// <summary>Entrée sauvegardée du RugDex.</summary>
    [Serializable]
    public sealed class RugDexRecord
    {
        public string id = string.Empty;
        public bool discovered;
        /// <summary>Commandes livrées et acceptées avec ce tapis.</summary>
        public int timesDelivered;
        /// <summary>Meilleure note obtenue (vide si aucune livraison acceptée).</summary>
        public string bestGrade = string.Empty;
    }

    /// <summary>
    /// Progression du RugDex (règle pure, testée) : découverte d'un tapis (première fois qu'il est posé dans l'atelier ou
    /// qu'une commande le propose), livraisons acceptées et meilleure note. Rien n'est inventé : toutes les valeurs
    /// viennent des événements réels du jeu.
    /// </summary>
    public sealed class RugDexBook
    {
        readonly Dictionary<string, RugDexRecord> records = new Dictionary<string, RugDexRecord>();

        /// <summary>Un tapis vient d'être découvert pour la première fois.</summary>
        public event Action<string> Discovered;
        public event Action Changed;

        public bool IsDiscovered(string id) => id != null && records.TryGetValue(id, out var record) && record.discovered;

        public RugDexRecord Get(string id) => id != null && records.TryGetValue(id, out var record) ? record : null;

        /// <summary>Marque un tapis comme découvert. Vrai seulement la première fois.</summary>
        public bool Discover(string id)
        {
            if (string.IsNullOrEmpty(id) || IsDiscovered(id)) return false;
            Record(id).discovered = true;
            Discovered?.Invoke(id);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Enregistre une livraison : seules les commandes acceptées comptent ; la meilleure note est conservée.</summary>
        public void RecordDelivery(string id, OrderGrade grade)
        {
            if (string.IsNullOrEmpty(id) || grade == OrderGrade.Refused) return;
            var record = Record(id);
            record.discovered = true;
            record.timesDelivered++;
            if (string.IsNullOrEmpty(record.bestGrade) || grade > ParseGrade(record.bestGrade)) record.bestGrade = grade.ToString();
            Changed?.Invoke();
        }

        public static OrderGrade ParseGrade(string grade) =>
            Enum.TryParse(grade, out OrderGrade parsed) ? parsed : OrderGrade.Refused;

        /// <summary>Tapis découverts parmi ceux du catalogue (les identifiants hors catalogue ne comptent pas).</summary>
        public int DiscoveredCount(IReadOnlyList<RugDefinition> catalog)
        {
            int count = 0;
            if (catalog == null) return 0;
            foreach (var rug in catalog)
                if (rug != null && IsDiscovered(rug.id)) count++;
            return count;
        }

        public static float CompletionPercent(int discovered, int total) => total <= 0 ? 0f : 100f * discovered / total;

        public List<RugDexRecord> Snapshot()
        {
            var list = new List<RugDexRecord>();
            foreach (var record in records.Values)
                list.Add(new RugDexRecord { id = record.id, discovered = record.discovered, timesDelivered = record.timesDelivered, bestGrade = record.bestGrade });
            list.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            return list;
        }

        /// <summary>Restaure la progression (entrées vides ou incohérentes ignorées, valeurs bornées).</summary>
        public void Restore(IEnumerable<RugDexRecord> saved)
        {
            records.Clear();
            if (saved != null)
                foreach (var entry in saved)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.id)) continue;
                    var record = Record(entry.id);
                    record.discovered = entry.discovered || entry.timesDelivered > 0;
                    record.timesDelivered = Mathf.Max(0, entry.timesDelivered);
                    record.bestGrade = ParseGrade(entry.bestGrade) == OrderGrade.Refused ? string.Empty : entry.bestGrade;
                }
            Changed?.Invoke();
        }

        RugDexRecord Record(string id)
        {
            if (!records.TryGetValue(id, out var record))
            {
                record = new RugDexRecord { id = id };
                records[id] = record;
            }
            return record;
        }
    }
}
