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
    }
}
