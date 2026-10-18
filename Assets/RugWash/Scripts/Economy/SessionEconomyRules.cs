using System.Collections.Generic;

namespace RugWash.Economy
{
    /// <summary>Revente d'un objet (V0.2.5).</summary>
    public enum SaleResult
    {
        Sold,
        /// <summary>Objet absent de l'inventaire et du casier du joueur.</summary>
        NotOwned,
        /// <summary>Objet qui ne s'achète pas (kit de départ) ou qui n'est pas un objet (amélioration, atelier, produit).</summary>
        NotResellable,
        /// <summary>Objet porté : il doit d'abord être rangé au casier (l'équipement en main n'est jamais retiré).</summary>
        Carried,
        Unknown,
        /// <summary>Multijoueur (invité) : demande envoyée à l'hôte.</summary>
        Requested = 100,
    }

    /// <summary>
    /// Règles pures de l'économie de session et de la revente (V0.2.5, testées en EditMode).
    /// <list type="bullet">
    /// <item>Partie en ligne : la sauvegarde de la session est l'emplacement multijoueur choisi par l'hôte. L'argent, la
    /// réputation, l'atelier (machines, robot, agrandissement, décor), les produits et le stock de détergent y sont
    /// communs à tous les joueurs ; chaque joueur y garde ses <b>achats personnels</b> (outils, améliorations d'outils,
    /// équipement, gadgets de farce). Les achats d'atelier sont réservés à l'hôte.</item>
    /// <item>Revente : seuls les objets achetés qui se portent (outils et gadgets de farce) se revendent, à 50 % de leur
    /// prix de base (prix de la définition, jamais un prix modifié), et seulement depuis le casier.</item>
    /// </list>
    /// </summary>
    public static class SessionEconomyRules
    {
        /// <summary>Achat propre à chaque joueur (tout sauf l'atelier).</summary>
        public static bool IsPersonal(UpgradeDefinition upgrade) => upgrade != null && !upgrade.IsWorkshopItem;

        /// <summary>Objet acheté qui se porte et se revend : un outil à débloquer ou un gadget de farce, payant.</summary>
        public static bool IsResellable(UpgradeDefinition upgrade) =>
            upgrade != null && upgrade.price > 0 && (upgrade.effect == UpgradeEffect.UnlockTool || upgrade.effect == UpgradeEffect.PrankGadget);

        /// <summary>Objet que l'on peut perdre en recevant un coup (mêmes objets que la revente).</summary>
        public static bool IsDroppable(UpgradeDefinition upgrade) => IsResellable(upgrade);

        /// <summary>Valeur de revente : la moitié du prix de base de la définition (arrondie à l'euro inférieur).</summary>
        public static int ResaleValue(UpgradeDefinition upgrade) => IsResellable(upgrade) ? upgrade.price / 2 : 0;

        /// <summary>Revente possible : objet revendable, possédé et rangé au casier.</summary>
        public static SaleResult CheckSale(UpgradeDefinition upgrade, ICollection<string> owned, bool carried)
        {
            if (upgrade == null || owned == null) return SaleResult.Unknown;
            if (!IsResellable(upgrade)) return SaleResult.NotResellable;
            if (!owned.Contains(upgrade.id)) return SaleResult.NotOwned;
            if (carried) return SaleResult.Carried;
            return SaleResult.Sold;
        }

        /// <summary>
        /// Revente appliquée par l'autorité : l'objet quitte les achats et la valeur est créditée une seule fois. Un second
        /// appel pour le même objet échoue (déjà retiré) : ni double crédit, ni duplication.
        /// </summary>
        public static SaleResult TrySell(UpgradeDefinition upgrade, ICollection<string> owned, bool carried, Wallet wallet)
        {
            var check = CheckSale(upgrade, owned, carried);
            if (check != SaleResult.Sold || wallet == null) return wallet == null ? SaleResult.Unknown : check;
            owned.Remove(upgrade.id);
            wallet.Add(ResaleValue(upgrade));
            return SaleResult.Sold;
        }

        /// <summary>Achats d'un invité vus par la session : l'atelier de l'hôte et ses propres achats personnels.</summary>
        public static HashSet<string> GuestOwned(IEnumerable<string> hostOwned, IEnumerable<string> personal, IReadOnlyList<UpgradeDefinition> catalog)
        {
            var owned = new HashSet<string>();
            if (catalog == null) return owned;
            var host = hostOwned != null ? new HashSet<string>(hostOwned) : new HashSet<string>();
            var mine = personal != null ? new HashSet<string>(personal) : new HashSet<string>();
            foreach (var upgrade in catalog)
            {
                if (upgrade == null) continue;
                if (upgrade.IsWorkshopItem ? host.Contains(upgrade.id) : mine.Contains(upgrade.id)) owned.Add(upgrade.id);
            }
            return owned;
        }

        /// <summary>Achats personnels d'une fiche de joueur : identifiants connus du catalogue, hors atelier, sans doublon.</summary>
        public static List<string> SanitizePersonal(IEnumerable<string> ids, IReadOnlyList<UpgradeDefinition> catalog)
        {
            var clean = new List<string>();
            if (ids == null || catalog == null) return clean;
            var wanted = new HashSet<string>(ids);
            foreach (var upgrade in catalog)
                if (IsPersonal(upgrade) && wanted.Contains(upgrade.id) && !clean.Contains(upgrade.id)) clean.Add(upgrade.id);
            return clean;
        }

        /// <summary>
        /// Achat demandé par un invité, vérifié par l'hôte : l'atelier est réservé à l'hôte ; sinon mêmes règles que la
        /// boutique, avec l'argent et la réputation de la session et les achats de cet invité.
        /// </summary>
        public static PurchaseResult CheckGuestPurchase(UpgradeDefinition upgrade, ICollection<string> guestOwned, Wallet sessionWallet, int sessionReputation)
        {
            if (upgrade == null || guestOwned == null || sessionWallet == null) return PurchaseResult.Unknown;
            if (!IsPersonal(upgrade)) return PurchaseResult.HostOnly;
            return ShopRules.Check(upgrade, guestOwned, sessionWallet, sessionReputation);
        }

        public static string Describe(SaleResult result, int amount = 0)
        {
            switch (result)
            {
                case SaleResult.Sold: return $"Objet revendu : +{WalletManager.Format(amount)}";
                case SaleResult.NotOwned: return "Vous ne possédez pas cet objet";
                case SaleResult.NotResellable: return "Cet objet ne se revend pas";
                case SaleResult.Carried: return "Rangez d'abord l'objet au casier pour le revendre";
                case SaleResult.Requested: return "Demande envoyée à l'hôte…";
                default: return "Objet inconnu";
            }
        }
    }
}
