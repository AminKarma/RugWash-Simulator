using System.Collections.Generic;
using RugWash.Cleaning;
using UnityEngine;

namespace RugWash.Orders
{
    public enum OrderGrade
    {
        /// <summary>Refusée par le client : aucun paiement, la commande redevient disponible.</summary>
        Refused,
        D,
        C,
        B,
        A,
        S,
    }

    /// <summary>Ligne du résumé de résultat (critère réellement mesuré).</summary>
    public readonly struct ResultLine
    {
        public readonly string Label;
        public readonly string Value;
        public readonly bool Met;

        public ResultLine(string label, string value, bool met)
        {
            Label = label;
            Value = value;
            Met = met;
        }
    }

    public sealed class OrderResult
    {
        public OrderDefinition Order;
        public float Score;
        public OrderGrade Grade;
        public int Reward;
        public bool TimeBonus;
        public int ReputationDelta;
        public float ElapsedSeconds;
        /// <summary>Zone de lavage de la commande livrée (0 = zone 1).</summary>
        public int Bay;
        public RugCleaningSummary Summary;
        public readonly List<ResultLine> Lines = new List<ResultLine>();

        public bool Accepted => Grade != OrderGrade.Refused;
    }

    /// <summary>
    /// Règles d'évaluation centralisées (pures, testées). La note est calculée uniquement à partir
    /// des données de la grille : saleté retirée, résidus, humidité, et temps pour un bonus.
    /// Un critère désactivé dans la commande ne compte pas dans la note.
    /// </summary>
    public static class OrderEvaluator
    {
        public const float RefusedBelowScore = 40f;
        public const float TimeBonusRatio = 0.15f;

        public static OrderResult Evaluate(OrderDefinition order, in RugCleaningSummary summary, float elapsedSeconds)
        {
            var objectives = order.objectives;
            var result = new OrderResult { Order = order, Summary = summary, ElapsedSeconds = elapsedSeconds };

            float target = Mathf.Max(0.01f, objectives.targetCleanliness);
            float cleanFactor = Mathf.Clamp01(summary.Cleanliness01 / target);
            bool cleanMet = summary.Cleanliness01 >= target - 1e-4f;
            result.Lines.Add(new ResultLine("Saleté retirée", $"{summary.Cleanliness01 * 100f:0} % (objectif {target * 100f:0} %)", cleanMet));

            float weightedSum = 0.7f * cleanFactor;
            float weights = 0.7f;
            bool allMet = cleanMet;

            if (objectives.limitResidue)
            {
                float factor = summary.AverageResidue01 <= objectives.maxResidue
                    ? 1f : Mathf.Clamp01(1f - (summary.AverageResidue01 - objectives.maxResidue) / 0.3f);
                bool met = summary.AverageResidue01 <= objectives.maxResidue + 1e-4f;
                weightedSum += 0.15f * factor;
                weights += 0.15f;
                allMet &= met;
                result.Lines.Add(new ResultLine("Résidus de produit",
                    $"{summary.AverageResidue01 * 100f:0} % (max {objectives.maxResidue * 100f:0} %)", met));
            }

            if (objectives.limitMoisture)
            {
                float factor = summary.AverageMoisture01 <= objectives.maxMoisture
                    ? 1f : Mathf.Clamp01(1f - (summary.AverageMoisture01 - objectives.maxMoisture) / 0.5f);
                bool met = summary.AverageMoisture01 <= objectives.maxMoisture + 1e-4f;
                weightedSum += 0.15f * factor;
                weights += 0.15f;
                allMet &= met;
                result.Lines.Add(new ResultLine("Humidité", $"{summary.AverageMoisture01 * 100f:0} % (max {objectives.maxMoisture * 100f:0} %)", met));
            }

            if (objectives.limitWear)
            {
                float factor = summary.AverageWear01 <= objectives.maxWear
                    ? 1f : Mathf.Clamp01(1f - (summary.AverageWear01 - objectives.maxWear) / 0.2f);
                bool met = summary.AverageWear01 <= objectives.maxWear + 1e-4f;
                weightedSum += 0.15f * factor;
                weights += 0.15f;
                allMet &= met;
                result.Lines.Add(new ResultLine("Fibres abîmées", $"{summary.AverageWear01 * 100f:0.#} % (max {objectives.maxWear * 100f:0.#} %)", met));
            }

            result.Score = Mathf.Round(100f * weightedSum / weights * 10f) / 10f;
            result.Grade = GradeFor(result.Score, allMet);

            if (objectives.bonusTimeSeconds > 0f)
            {
                result.TimeBonus = result.Grade >= OrderGrade.B && elapsedSeconds <= objectives.bonusTimeSeconds;
                result.Lines.Add(new ResultLine("Temps", $"{FormatTime(elapsedSeconds)} (bonus sous {FormatTime(objectives.bonusTimeSeconds)})", result.TimeBonus));
            }

            float reward = order.baseReward * RewardMultiplier(result.Grade);
            if (result.TimeBonus) reward += order.baseReward * TimeBonusRatio;
            result.Reward = Mathf.RoundToInt(reward);
            result.ReputationDelta = ReputationFor(result.Grade);
            return result;
        }

        /// <summary>S exige tous les objectifs atteints ; en dessous du seuil, la commande est refusée.</summary>
        public static OrderGrade GradeFor(float score, bool allObjectivesMet)
        {
            if (score < RefusedBelowScore) return OrderGrade.Refused;
            if (score >= 95f && allObjectivesMet) return OrderGrade.S;
            if (score >= 85f) return OrderGrade.A;
            if (score >= 70f) return OrderGrade.B;
            if (score >= 55f) return OrderGrade.C;
            return OrderGrade.D;
        }

        public static float RewardMultiplier(OrderGrade grade)
        {
            switch (grade)
            {
                case OrderGrade.S: return 1.25f;
                case OrderGrade.A: return 1f;
                case OrderGrade.B: return 0.8f;
                case OrderGrade.C: return 0.55f;
                case OrderGrade.D: return 0.3f;
                default: return 0f;
            }
        }

        public static int ReputationFor(OrderGrade grade)
        {
            switch (grade)
            {
                case OrderGrade.S: return 3;
                case OrderGrade.A: return 2;
                case OrderGrade.B: return 1;
                case OrderGrade.C: return 0;
                default: return -1;
            }
        }

        public static string GradeLabel(OrderGrade grade) => grade == OrderGrade.Refused ? "Refusée" : grade.ToString();

        public static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }
    }
}
