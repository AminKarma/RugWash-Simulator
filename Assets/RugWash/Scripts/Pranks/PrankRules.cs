using System.Collections.Generic;
using UnityEngine;

namespace RugWash.Pranks
{
    /// <summary>Résultat d'une farce (valeurs figées : réseau).</summary>
    public enum PrankResult : byte
    {
        Done = 0,
        NotOwned = 1,
        Cooldown = 2,
        InsufficientFunds = 3,
        NoTarget = 4,
        OutOfRange = 5,
        RugFinished = 6,
        CapReached = 7,
        TargetProtected = 8,
        Unavailable = 9,
        InvalidTarget = 10,
    }

    /// <summary>Demande de farce : ce que le joueur vise, au moment où il agit.</summary>
    public struct PrankRequest
    {
        public PrankGadget Gadget;
        public PrankTarget Target;
        /// <summary>Point visé sur le tapis (cible « tapis »).</summary>
        public Vector3 Point;
        /// <summary>Joueur visé (cible « joueur ») : identifiant réseau du client.</summary>
        public ulong Victim;
        /// <summary>Position des yeux et direction de visée de l'auteur (effets, contrôle de portée).</summary>
        public Vector3 Origin;
        public Vector3 Direction;
    }

    /// <summary>Farce réalisée, telle que l'autorité l'a appliquée (diffusée aux autres joueurs pour les effets).</summary>
    public struct PrankOutcome
    {
        public PrankGadget Gadget;
        public ulong Actor;
        public Vector3 Origin;
        public Vector3 Point;
        public bool HitPlayer;
        public ulong Victim;
        /// <summary>Saleté réellement ajoutée au tapis.</summary>
        public float Added;
    }

    /// <summary>Auteur d'une farce, vu par l'autorité : possession, argent (débité par l'autorité), position, état.</summary>
    public interface IPrankActor
    {
        ulong Id { get; }
        string Name { get; }
        Vector3 Eye { get; }
        bool IsDown { get; }
        bool Owns(PrankGadget gadget);
        int Money { get; }
        bool TrySpend(int amount);
    }

    /// <summary>Joueurs pouvant être visés (partie en ligne, côté hôte) : position et effets appliqués chez eux.</summary>
    public interface IPrankVictims
    {
        bool TryGetFeet(ulong clientId, out Vector3 feet);
        bool IsDown(ulong clientId);
        void Splash(ulong clientId, float intensity, float seconds, float fadeSeconds, string from);
        /// <param name="cause">Gadget responsable (batte : chute ; fusil : KO cartoon, tête qui éclate).</param>
        void Knock(ulong clientId, Vector3 impulse, float seconds, string from, PrankGadget cause);
    }

    /// <summary>Règles pures des farces (testées en EditMode).</summary>
    public static class PrankRules
    {
        /// <summary>Hauteur visée d'une silhouette au-dessus de ses pieds (portée, effets).</summary>
        public const float BodyHeight = 1.1f;

        /// <summary>Conditions propres à l'auteur : achat, recharge (du gadget et globale), argent pour le coût d'usage.</summary>
        public static PrankResult CheckUse(bool owned, float now, float readyAt, float globalReadyAt, int money, int cost)
        {
            if (!owned) return PrankResult.NotOwned;
            if (now < readyAt || now < globalReadyAt) return PrankResult.Cooldown;
            if (cost > 0 && money < cost) return PrankResult.InsufficientFunds;
            return PrankResult.Done;
        }

        public static bool InRange(Vector3 from, Vector3 to, float range, float tolerance) =>
            (to - from).sqrMagnitude <= (range + Mathf.Max(0f, tolerance)) * (range + Mathf.Max(0f, tolerance));

        /// <summary>Saleté que les farces peuvent encore ajouter à ce tapis.</summary>
        public static float CapRemaining(float added, float initialDirt, float fraction, float minimum) =>
            Mathf.Max(0f, Mathf.Max(minimum, initialDirt * Mathf.Clamp01(fraction)) - Mathf.Max(0f, added));

        /// <summary>
        /// Silhouette visée : la plus proche le long du rayon, à moins de <paramref name="radius"/> de l'axe de son corps
        /// (des pieds à la tête) et à portée. -1 si aucune.
        /// </summary>
        public static int PickPlayer(Ray ray, IReadOnlyList<Vector3> feet, float range, float radius)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < feet.Count; i++)
            {
                Vector3 bottom = feet[i] + Vector3.up * 0.25f, top = feet[i] + Vector3.up * 1.75f;
                ClosestPoints(ray.origin, ray.direction, bottom, top, out float along, out Vector3 onRay, out Vector3 onBody);
                if (along < 0f || along > range) continue;
                if ((onRay - onBody).sqrMagnitude > radius * radius) continue;
                if (along < bestDistance)
                {
                    bestDistance = along;
                    best = i;
                }
            }
            return best;
        }

        static void ClosestPoints(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, out float along, out Vector3 onRay, out Vector3 onSegment)
        {
            Vector3 d1 = direction.normalized, d2 = b - a, r = origin - a;
            float e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r), c = Vector3.Dot(d1, r), bb = Vector3.Dot(d1, d2);
            float denominator = e - bb * bb;
            float t = denominator > 1e-6f ? Mathf.Clamp01((f - bb * c) / denominator) : 0f;
            onSegment = a + d2 * t;
            along = Vector3.Dot(onSegment - origin, d1);
            onRay = origin + d1 * Mathf.Max(0f, along);
        }

        /// <summary>
        /// Position de la i-ème tache autour du point visé (repère du tapis, m) : la première au centre, les autres
        /// réparties en spirale avec un léger désordre déterministe (même motif chez l'hôte et chez les invités).
        /// </summary>
        public static Vector2 SplatOffset(int seed, int index, float spread)
        {
            if (index <= 0 || spread <= 0f) return Vector2.zero;
            uint h = (uint)(seed * 73856093) ^ (uint)(index * 19349663);
            h = (h ^ (h >> 13)) * 1274126177u;
            float jitter = (h & 0xFFFF) / 65535f;
            float angle = index * 2.39996f + jitter * 0.8f;
            float distance = spread * Mathf.Sqrt(index / 9f) * (0.75f + 0.5f * jitter);
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Mathf.Min(distance, spread);
        }

        /// <summary>Opacité des gouttes à l'écran : pleine, puis fondu de sortie sur les dernières secondes.</summary>
        public static float SplashAlpha(float elapsed, float duration, float fade, float intensity)
        {
            if (elapsed < 0f || elapsed >= duration || intensity <= 0f) return 0f;
            float remaining = duration - elapsed;
            float fadeIn = Mathf.Clamp01(elapsed / 0.12f);
            float fadeOut = fade > 0f ? Mathf.Clamp01(remaining / fade) : 1f;
            return Mathf.Clamp01(intensity) * fadeIn * fadeOut;
        }

        public static string Describe(PrankResult result, string gadget = null, float remainingSeconds = 0f, int cost = 0)
        {
            switch (result)
            {
                case PrankResult.Done: return string.Empty;
                case PrankResult.NotOwned: return $"{gadget ?? "Gadget"} : à acheter sur l'ordinateur (Boutique, onglet Farces)";
                case PrankResult.Cooldown:
                    return remainingSeconds > 0f ? $"{gadget ?? "Farce"} : recharge encore {Mathf.CeilToInt(remainingSeconds)} s" : "Farce en recharge";
                case PrankResult.InsufficientFunds: return $"Fonds insuffisants ({cost} € par utilisation)";
                case PrankResult.NoTarget: return "Visez le tapis de la commande (ou un coéquipier proche)";
                case PrankResult.OutOfRange: return "Trop loin — rapprochez-vous";
                case PrankResult.RugFinished: return "Tapis terminé : on ne salit plus un travail fini";
                case PrankResult.CapReached: return "Ce tapis a eu son lot de farces";
                case PrankResult.TargetProtected: return "Ce joueur vient d'être visé : laissez-le souffler";
                case PrankResult.InvalidTarget: return "Ce gadget ne vise que les tapis";
                default: return "Farce impossible pour l'instant";
            }
        }
    }
}
