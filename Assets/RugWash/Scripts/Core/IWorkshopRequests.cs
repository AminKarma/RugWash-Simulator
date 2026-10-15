namespace RugWash.Core
{
    /// <summary>
    /// Multijoueur, côté invité : actions qui modifient l'état partagé de l'atelier. Elles sont envoyées à l'hôte, qui
    /// les valide et les exécute ; le résultat revient par la réplication (jamais calculé par l'invité).
    /// </summary>
    public interface IWorkshopRequests
    {
        void RequestStartOrder(string orderId);
        void RequestDeliver();
        void RequestAbandon();
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
    }
}
