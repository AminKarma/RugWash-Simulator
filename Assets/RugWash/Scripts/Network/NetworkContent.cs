using UnityEngine;

namespace RugWash.Network
{
    /// <summary>Réglages et préfabriqués du multijoueur (modifiables dans l'Inspector, sans code).</summary>
    [CreateAssetMenu(menuName = "RugWash/Réseau", fileName = "NetworkContent")]
    public sealed class NetworkContent : ScriptableObject
    {
        [Tooltip("Objet réseau de l'atelier (réplication des surfaces, commandes, économie, robot), créé par l'hôte.")]
        public GameObject workshopPrefab;
        [Tooltip("Silhouette d'un joueur vue par les autres (position, outil, effets).")]
        public GameObject avatarPrefab;
        /// <summary>Version courante du protocole (imposée à l'asset par le constructeur de contenu).</summary>
        public const string CurrentProtocol = "rugwash-coop-11";

        [Tooltip("Version du protocole : un joueur d'une autre version est refusé avec un message clair.")]
        public string protocolVersion = CurrentProtocol;
        [Range(2, 4)] public int maxPlayers = 4;
        [Tooltip("Port de la connexion directe (réseau local).")]
        public ushort defaultPort = 7777;
        [Tooltip("App ID Steam. 480 (« Spacewar ») est l'application de test publique de Valve, pour le développement uniquement.")]
        public uint steamAppId = 480;
        [Tooltip("App ID Steam du DLC « édition Premium » (grande mise à jour). 0 : aucune édition Premium déclarée, " +
                 "l'option « Premium uniquement » reste indisponible (jamais de vérification factice).")]
        public uint premiumAppId;
        [Tooltip("Envois de cellules par seconde (hôte → invités).")]
        [Range(5f, 30f)] public float surfaceSendRate = 15f;
        [Tooltip("Intervalle (s) de correction de la simulation lente (mousse qui retombe, séchage). Chaque poste la reproduit ; " +
                 "l'hôte n'envoie la sienne qu'à cet intervalle, au lieu de toute la grille plusieurs fois par seconde.")]
        [Range(0.5f, 10f)] public float slowSurfaceSyncSeconds = 2f;
    }
}
