# Multijoueur coopératif — RugWash Simulator (jalon 5 V2, mis à jour en v0.1.2)

Ce document décrit les choix techniques, l'architecture, la configuration Steam, la procédure de test et les limites du
mode coopératif. **Ce qui a été réellement testé et ce qui reste à vérifier** est détaillé à la fin (et dans `VALIDATION.md`).

## 1. Décisions techniques

| Besoin | Choix | Raison |
|---|---|---|
| Synchronisation du jeu | **Netcode for GameObjects 2.13.3** (+ Unity Transport 2.6.0) | NGO 3.x exige Unity 6000.7 : incompatible avec Unity 6000.5.7f1 du projet. 2.13.3 est la dernière version compatible (Unity 6000.0+). |
| Sessions Steam (salons, invitations, « rejoindre un ami ») | **Steamworks.NET 2025.164.1** (paquet UPM) | Bibliothèque C# de référence, compatible Unity 6, sans dépendance supplémentaire. |
| Jeu par Internet | **Transport Steam Networking Sockets** écrit pour le projet (`SteamSocketsTransport`) | Pair à pair via les relais de Steam : ni adresse IP ni ouverture de port. Le transport communautaire existant vise NGO 1.x ; un transport de ~250 lignes sur l'API de Steamworks.NET évite une dépendance non maintenue. |
| Réseau local et tests automatisés | **Unity Transport** (connexion directe IP:port) | Tests à plusieurs processus sur une même machine sans compte Steam ; jeu en réseau local. Les deux transports ne sont jamais actifs en même temps : la session choisit l'un ou l'autre selon le mode. |

## 2. Architecture

L'**hôte fait autorité**. Tout ce qui modifie l'état partagé est exécuté chez lui ; les invités envoient des demandes ou
des opérations et reçoivent l'état. Les effets visuels et sonores sont produits localement par chaque poste.

| Élément | Hôte | Invité |
|---|---|---|
| Tapis, aire de lavage, sol (grilles de nettoyage) | Grilles de référence ; applique ses opérations et celles des invités ; diffuse les cellules modifiées (15/s) et un instantané après chaque chargement de tapis | Répliques : ses outils envoient chaque opération (outil, zone, paramètres) à l'hôte **et** l'appliquent tout de suite (prédiction) ; les cellules reçues remplacent les valeurs locales, puis les opérations pas encore confirmées sont rejouées sur cette zone |
| Simulation lente (mousse qui retombe, séchage) | Oui ; n'en envoie qu'une correction toutes les 2 s (`slowSurfaceSyncSeconds`) | **Reproduite localement** (même règle), corrigée par l'hôte |
| Ruissellement vers l'aire de lavage, recontamination | Oui | Non (arrive avec les cellules) |
| Commandes (accepter, livrer, abandonner), évaluation | Oui, une seule fois par commande (tableau de l'hôte, déverrouillé par sa réputation) | Demande à l'hôte ; reçoit l'état et les mesures de livraison de l'hôte pour afficher le même résultat |
| Missions générées (v0.1.2) | Génère ses offres (graine de sa partie, numéro, palier de sa réputation, outils qu'il possède) ; publie leurs identifiants | Régénère chaque mission à l'identique depuis son identifiant (`gen.graine.numéro.palier.outils.variante`) ; ne démarre que celles du tableau de l'hôte |
| Détergent en bidons (v0.1.2) | Achat d'un bidon et remplissage de la réserve débités sur la carrière du joueur demandeur (stock borné à 160 L) | Demande à l'hôte ; reçoit la quantité accordée et son stock validé |
| Carrière (argent, réputation, améliorations, produits) — **personnelle** (v0.1.2) | Sa propre carrière ; tient aussi la carrière de chaque invité : bornée à l'arrivée, puis achats, consommations et récompenses appliqués par lui seul | Annonce à l'arrivée la carrière de SA sauvegarde ; achats demandés à l'hôte ; reçoit sa carrière validée et la sauvegarde chez lui |
| Livraison payée | Récompense et réputation complètes pour **chaque joueur présent**, chacun dans sa carrière | Reçoit sa récompense (aucun paiement local) |
| Robot aspirateur (limité à l'aire de lavage) | Simulé chez l'hôte | Suit la position et l'état reçus ; E envoie la demande |
| Joueurs | Fait apparaître une silhouette par joueur | Chaque joueur garde sa caméra, ses entrées, ses réservoirs (eau, détergent, cuve) |
| Arrivée | Envoie les instantanés des surfaces et la carrière validée | « Synchronisation » (contrôles coupés, HUD en chargement, aucune valeur provisoire) jusqu'à réception de tout |
| Sauvegarde | Son atelier et sa carrière | **Sa carrière seulement** (argent, réputation, achats, produits, RugDex, commandes livrées) ; l'atelier de l'hôte n'est jamais écrit chez lui |

**Silhouettes (v0.1.2)** : chaque silhouette porte le **même modèle détaillé** que la vue à la première personne, au même
décalage devant ses yeux. Le point visé est retrouvé localement par le même rayon (position, lacet, tangage déjà
transmis) et l'outil y est posé avec la même règle que chez soi (`ToolPose` : manche télescopique des outils de contact,
orientation des buses). Le jet, la brume ou la mousse partent de la bouche de cet outil ; les bras (deux segments) se
referment sur la poignée, les jambes suivent la vitesse. Aucune donnée réseau supplémentaire.

**Pourquoi des opérations et pas des positions de souris** : une opération est exactement ce que l'outil applique
(disque ou éventail, puissance, durée, produit) ; l'hôte l'applique sur sa grille avec la même règle que le solo. Les
valeurs reçues sont bornées (un message trafiqué ne peut pas « nettoyer » plus qu'un outil réel) et un message invalide est
ignoré. Les opérations sont exprimées dans l'espace de la surface (indépendantes de sa position dans la scène).

**Nettoyage simultané** : deux joueurs sur la même zone ne se gênent pas. L'hôte applique les opérations dans l'ordre
où elles arrivent ; chaque invité reçoit, avec les cellules, le dernier numéro d'opération appliqué pour lui (accusé) ; ses
opérations plus récentes sont rejouées par-dessus. Résultat : chaque opération compte une fois, la propreté vue par un
invité ne recule pas quand l'hôte est en retard, et tous les postes convergent vers les mêmes valeurs (quantifiées sur un
octet, soit 1/255).

**Bande passante** : seules les cellules modifiées sont envoyées (rectangle, 11 octets par cellule, compression Deflate
au-delà de quelques centaines d'octets) ; jamais les particules ni les sons. Mesure du test à deux processus : voir
`VALIDATION.md`.

### Fichiers

| Fichier | Rôle |
|---|---|
| `Scripts/Network/MultiplayerSession.cs` | Session conservée entre les scènes : NetworkManager, transports, admission (version, places), salon Steam, départs, déconnexions, retour au menu, reconnexion |
| `Scripts/Network/SteamPlatform.cs` | Steamworks.NET : initialisation, salons, recherche, invitations, « +connect_lobby » |
| `Scripts/Network/SteamSocketsTransport.cs` | Transport NGO sur Steam Networking Sockets |
| `Scripts/Network/NetworkWorkshop.cs` | Objet réseau de l'atelier : surfaces, commandes, économie, boutique, robot, silhouettes, messages |
| `Scripts/Network/NetworkPlayerAvatar.cs` | Silhouette d'un joueur (position, tête, outil, effets, nom) |
| `Scripts/Network/SurfaceReplicators.cs`, `SurfaceSync.cs`, `SurfaceOperationCodec.cs`, `ByteBuffer.cs` | Réplication des grilles (pure, testée sans réseau) |
| `Scripts/Network/SessionRules.cs` | Règles pures : admission, noms, adresses, messages |
| `Scripts/UI/MultiplayerMenuUI.cs` | Fenêtre « Multijoueur » du menu principal |
| `Scripts/Diagnostics/MultiplayerAutotest.cs` | Scénario automatique à deux processus (`-rugwash-mp-test`) |
| `Editor/RugWashNetworkBuilder.cs` | Préfabriqués réseau et réglages `ScriptableObjects/NetworkContent.asset` |

## 3. Jouer

**Solo** : inchangé (Nouvelle partie / Continuer). Aucun réseau n'est démarré.

**Réseau local (connexion directe)** — menu principal → **Multijoueur** :
1. L'hôte choisit un pseudo et clique **Héberger sur ce PC** : l'atelier se charge avec sa progression.
2. Les autres saisissent l'adresse IPv4 de l'hôte (ex. `192.168.1.20:7777`) et cliquent **Rejoindre**.
3. Port UDP 7777 (modifiable dans `NetworkContent`) : à autoriser dans le pare-feu de l'hôte. Pas d'invitation ni de recherche.

**Steam (Internet)** — Steam lancé et connecté sur chaque PC **avant** de lancer le jeu. Steam est initialisé dès le menu
principal (invitations reçues sans ouvrir la fenêtre Multijoueur) :
1. **Créer une partie (amis)** ou **(publique)** : un salon Steam est créé (4 places).
2. **Inviter des amis** ouvre la fenêtre d'invitation de Steam. Un ami peut aussi **Rechercher des parties** (salons
   publics uniquement), ou accepter l'invitation depuis Steam. Jeu ouvert : depuis le menu ou en pleine partie solo (la
   partie solo est sauvegardée, la commande en cours abandonnée, puis le salon est rejoint depuis le menu). Jeu fermé :
   Steam lance le jeu avec `+connect_lobby` — **avec l'App ID de test 480, Steam lance « Spacewar » et non RugWash** :
   pour les tests, accepter l'invitation avec le jeu déjà ouvert.
3. Les membres apparaissent « dans le salon » ; l'hôte clique **Lancer la partie** : la partie démarre, chaque membre se
   connecte au jeu (transport Steam) et entre dans l'atelier. Les retardataires rejoignent en cours de partie.

**En partie** : la liste des joueurs (et la latence pour un invité) est en haut à droite. La pause ne fige pas le jeu.
« Quitter la partie » (pause) : un invité revient au menu ; l'hôte ferme la partie pour tous (« L'hôte a fermé la
partie »). Après une perte de connexion, le menu affiche la raison et propose **Se reconnecter**.

## 4. Configuration Steam (développement et tests)

- **App ID** : `NetworkContent.steamAppId` (480 par défaut, l'application de test publique « Spacewar » de Valve). Il est
  transmis à Steam par la variable d'environnement `SteamAppId` au lancement : aucun fichier `steam_appid.txt` à livrer,
  aucune donnée sensible dans le code. Pour une sortie commerciale, remplacer par l'App ID du jeu.
- `steam_api64.dll` est fourni par Steamworks.NET et copié dans le build.
- Steam doit être **lancé et connecté**. Sans Steam, la fenêtre Multijoueur l'indique et la connexion directe reste disponible.
- Avec l'App ID 480, Steam affiche « Spacewar » comme jeu en cours et la recherche publique peut montrer d'autres salons de
  test : le jeu ne liste que les salons portant la clé `game=rugwash` et la même version de protocole.

### Procédure de test Steam (deux comptes, deux machines)
1. Machine A (compte 1) et machine B (compte 2, ami du compte 1), Steam connecté, même build.
2. A : Multijoueur → Créer une partie (amis) → vérifier « Salon créé » et le compte 1 dans les membres.
3. A : Inviter des amis → inviter le compte 2. B : accepter l'invitation (jeu fermé puis jeu ouvert) → B apparaît dans les membres.
4. A : Lancer la partie → A et B dans l'atelier, chacun voit la silhouette de l'autre.
5. Nettoyer ensemble le même tapis, livrer depuis B → un seul paiement, résultats chez les deux.
6. B quitte → A continue ; A quitte → B revient au menu (« L'hôte a fermé la partie »), le salon n'apparaît plus dans la recherche.
7. Couper le réseau de B pendant la partie → message de perte de connexion, « Se reconnecter ».

## 5. Limites connues

- **Steam non exécuté dans l'environnement de développement** (client Steam non lancé, un seul compte, une seule
  machine) : salons, invitations, recherche et transport Steam sont implémentés et compilés mais **pas encore testés
  en réel** — procédure ci-dessus.
- Connexion directe testée **entre deux processus sur la même machine** (127.0.0.1), pas entre deux machines.
- Pas de migration d'hôte : si l'hôte quitte, la partie s'arrête pour tous (la progression de l'hôte est sauvegardée).
- Reconnexion manuelle (bouton), pas automatique.
- Silhouettes procédurales (placeholders : tenue de travail, gants, casquette), animation de marche simple ; pas de
  collision entre joueurs.
- Pas de serveur dédié : la carrière qu'un invité annonce à son arrivée provient de sa sauvegarde locale (bornée et
  filtrée par l'hôte) ; pendant la partie, seul l'hôte la modifie.
- Le tableau des commandes d'une session est celui de l'hôte (les commandes terminées de l'invité ne le modifient pas).
  Les missions générées tiennent compte des outils de l'**hôte** (un objectif de séchage n'est proposé que s'il possède
  l'extracteur) ; une mission livrée en ligne compte dans les statistiques de chaque joueur mais n'est pas ajoutée à
  l'historique des contrats.
- Protocole `rugwash-coop-4` (v0.1.2 jalon 3 : carrière avec stock de détergent, offres de missions) : les versions
  précédentes sont refusées avec un message clair.
- 4 joueurs au maximum ; version de protocole identique requise (message clair sinon).
