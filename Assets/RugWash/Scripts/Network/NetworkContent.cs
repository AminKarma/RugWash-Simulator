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
        [Tooltip("Version du protocole : un joueur d'une autre version est refusé avec un message clair.")]
        public string protocolVersion = "rugwash-coop-1";
        [Range(2, 4)] public int maxPlayers = 4;
        [Tooltip("Port de la connexion directe (réseau local).")]
        public ushort defaultPort = 7777;
        [Tooltip("App ID Steam. 480 (« Spacewar ») est l'application de test publique de Valve, pour le développement uniquement.")]
        public uint steamAppId = 480;
        [Tooltip("Envois de cellules par seconde (hôte → invités).")]
        [Range(5f, 30f)] public float surfaceSendRate = 15f;
    }
}
