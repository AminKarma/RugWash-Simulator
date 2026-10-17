using System;
using System.Collections.Generic;

namespace RugWash.Orders
{
    /// <summary>
    /// Offres de missions générées (règles pures, testées en EditMode) : trois offres à la fois, dont au moins deux
    /// accessibles avec la réputation actuelle et une « à viser » au palier suivant. Une mission livrée est remplacée par
    /// la suivante (numéro croissant) : rien n'est généré d'avance, et tout se reproduit à l'identique depuis la graine,
    /// le numéro et les offres sauvegardés.
    /// </summary>
    public sealed class MissionBoard
    {
        public const int OfferCount = 3;
        /// <summary>Offres toujours accessibles : la progression ne peut pas se bloquer, même après une baisse de réputation.</summary>
        public const int MinOpen = 2;
        /// <summary>
        /// Offres au plus (grande mise à jour) : une commande active de plus que la première (deuxième zone de lavage, machine
        /// à laver) garde sa place sur le tableau sans priver le joueur d'une offre.
        /// </summary>
        public const int MaxOffers = OfferCount + 3;

        readonly List<MissionKey> offers = new List<MissionKey>();

        public bool IsInitialized { get; private set; }
        public int Seed { get; private set; }
        /// <summary>Numéro de la prochaine mission générée.</summary>
        public int Sequence { get; private set; }
        /// <summary>Dernière mission livrée (évite de reproposer la même aussitôt).</summary>
        public string LastCompleted { get; private set; } = string.Empty;
        public IReadOnlyList<MissionKey> Offers => offers;
        /// <summary>Difficulté des prochaines missions (paramètres du joueur ; en ligne, ceux de l'hôte). Les offres affichées gardent la leur.</summary>
        public int Difficulty { get; set; } = (int)MissionDifficulty.Normal;

        public static int NewSeed()
        {
            int seed = Guid.NewGuid().GetHashCode() ^ Environment.TickCount;
            return seed == 0 ? 1 : seed;
        }

        /// <summary>Reprend un état sauvegardé : les identifiants invalides, en double ou d'une autre graine sont ignorés.</summary>
        public void Restore(int seed, int sequence, IEnumerable<string> savedOffers, string lastCompleted)
        {
            Seed = seed == 0 ? NewSeed() : seed;
            Sequence = Math.Max(0, sequence);
            LastCompleted = lastCompleted ?? string.Empty;
            offers.Clear();
            if (savedOffers != null)
                foreach (string id in savedOffers)
                    if (offers.Count < MaxOffers && MissionKey.TryParse(id, out var key) && key.Seed == Seed && !offers.Contains(key))
                    {
                        offers.Add(key);
                        // Un numéro sauvegardé trop bas (fichier édité) ne doit jamais régénérer une offre déjà affichée.
                        if (key.Sequence >= Sequence) Sequence = key.Sequence + 1;
                    }
            IsInitialized = true;
        }

        public bool Contains(string id)
        {
            foreach (var offer in offers)
                if (offer.Id == id) return true;
            return false;
        }

        /// <summary>Retire une mission livrée et acceptée ; vrai si c'était une offre.</summary>
        public bool Retire(string id)
        {
            int index = offers.FindIndex(o => o.Id == id);
            if (index < 0) return false;
            offers.RemoveAt(index);
            LastCompleted = id;
            return true;
        }

        public int OpenCount(int reputation)
        {
            int open = 0;
            foreach (var offer in offers)
                if (MissionRules.Threshold(offer.Tier) <= reputation) open++;
            return open;
        }

        /// <summary>
        /// Complète les offres. <paramref name="signature"/> décrit une clé (catégorie + tapis) pour éviter les doublons
        /// immédiats ; <paramref name="activeId"/> (commande en cours) n'est jamais retiré. Retourne vrai si les offres ont changé.
        /// </summary>
        public bool Refill(int reputation, int capabilities, Func<MissionKey, string> signature, string activeId) =>
            RefillKeeping(reputation, capabilities, signature, string.IsNullOrEmpty(activeId) ? Array.Empty<string>() : new[] { activeId });

        /// <summary>
        /// Complète les offres ; les missions en cours (<paramref name="activeIds"/> : zones de lavage, machine à laver) ne
        /// sont jamais retirées. Au-delà de la première, chaque mission en cours ajoute une offre (au plus <see cref="MaxOffers"/>).
        /// </summary>
        public bool RefillKeeping(int reputation, int capabilities, Func<MissionKey, string> signature, ICollection<string> activeIds)
        {
            if (!IsInitialized) return false;
            activeIds ??= Array.Empty<string>();
            bool changed = false;
            int tier = MissionRules.TierFor(reputation);
            // Une seule offre verrouillée (celle « à viser ») : après une baisse de réputation, les autres offres devenues
            // inaccessibles cèdent la place, la plus exigeante d'abord (jamais une commande en cours).
            while (LockedCount(reputation, activeIds) > OfferCount - MinOpen && DropLocked(reputation, activeIds)) changed = true;
            int active = 0;
            foreach (var offer in offers)
                if (activeIds.Contains(offer.Id)) active++;
            int wanted = Math.Min(MaxOffers, OfferCount + Math.Max(0, active - 1));
            while (offers.Count < wanted)
            {
                bool hasStretch = offers.Exists(o => MissionRules.Threshold(o.Tier) > reputation);
                int target = OpenCount(reputation) < MinOpen || hasStretch || tier >= MissionRules.MaxTier ? tier : tier + 1;
                offers.Add(Next(target, capabilities, signature));
                changed = true;
            }
            return changed;
        }

        int LockedCount(int reputation, ICollection<string> activeIds)
        {
            int locked = 0;
            foreach (var offer in offers)
                if (MissionRules.Threshold(offer.Tier) > reputation && !activeIds.Contains(offer.Id)) locked++;
            return locked;
        }

        bool DropLocked(int reputation, ICollection<string> activeIds)
        {
            int drop = -1;
            for (int i = 0; i < offers.Count; i++)
                if (MissionRules.Threshold(offers[i].Tier) > reputation && !activeIds.Contains(offers[i].Id) && (drop < 0 || offers[i].Tier > offers[drop].Tier))
                    drop = i;
            if (drop < 0) return false;
            offers.RemoveAt(drop);
            return true;
        }

        MissionKey Next(int tier, int capabilities, Func<MissionKey, string> signature)
        {
            var avoid = new HashSet<string>();
            if (signature != null)
            {
                foreach (var offer in offers) avoid.Add(signature(offer));
                if (MissionKey.TryParse(LastCompleted, out var last)) avoid.Add(signature(last));
            }
            var key = new MissionKey(Seed, Sequence, tier, capabilities, 0, Difficulty);
            for (int variant = 0; signature != null && variant < MissionKey.MaxVariant; variant++)
            {
                key = new MissionKey(Seed, Sequence, tier, capabilities, variant, Difficulty);
                if (!avoid.Contains(signature(key))) break;
            }
            Sequence++;
            return key;
        }
    }
}
