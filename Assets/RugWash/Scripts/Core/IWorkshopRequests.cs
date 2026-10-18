namespace RugWash.Core
{
    /// <summary>
    /// Multijoueur, côté invité : actions qui modifient l'état partagé de l'atelier. Elles sont envoyées à l'hôte, qui
    /// les valide et les exécute ; le résultat revient par la réplication (jamais calculé par l'invité).
    /// </summary>
    public interface IWorkshopRequests
    {
        /// <summary>Accepter une commande ; <paramref name="bay"/> : zone de lavage voulue (l'hôte en choisit une autre si elle est prise).</summary>
        void RequestStartOrder(string orderId, int bay);
        /// <summary>Livrer ou abandonner la commande d'une zone de lavage (grande mise à jour : plusieurs zones).</summary>
        void RequestDeliver(int bay);
        void RequestAbandon(int bay);
        /// <summary>Machine à laver de l'hôte : charger le tapis d'une zone, récupérer le tapis lavé, ou abandonner la commande en machine.</summary>
        void RequestWasher(byte action, int bay);
        void RequestUpgrade(string upgradeId);
        void RequestProduct(string productId);
        void RequestRobotToggle();
        /// <summary>Achat d'un bidon de détergent (payé et stocké dans la carrière de ce joueur).</summary>
        void RequestDetergentCan();
        /// <summary>Remplissage de la réserve au poste détergent : prélèvement sur le stock de ce joueur.</summary>
        void RequestDetergentRefill(float liters);
        /// <summary>Machine de l'atelier de l'hôte (allumer, lancer un cycle).</summary>
        void RequestMachine(string machineId);
        /// <summary>Farce (v0.1.2) : validée et appliquée par l'hôte, qui répond au demandeur.</summary>
        void RequestPrank(Pranks.PrankRequest request);
        /// <summary>V0.2.5 : revente d'un objet rangé au casier (50 % du prix de base, versés dans l'argent de la session).</summary>
        void RequestSale(string upgradeId);
        /// <summary>V0.2.5 : ramassage d'un objet tombé au sol (le premier qui le demande l'obtient).</summary>
        void RequestPickup(uint dropId);
    }
}
