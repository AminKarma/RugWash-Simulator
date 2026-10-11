# Audit — Roadmap V2 (jalon 0)

Date : 11/10/2026 · Unity 6000.5.7f1 · URP 17.5 · Input System 1.20 · 72 scripts, ~14 300 lignes C#.

## 1. État du projet (réellement vérifié)

| Domaine | Existant | Remarques |
|---|---|---|
| Nettoyage | `RugCleaningGrid` (données pures, 9 canaux par cellule), opérations struct, 5 outils dérivés de `CleaningToolBase` | Source de vérité unique, testée (84 tests EditMode) |
| Rendu tapis | `RugDirtRenderer` → 3 textures de contrôle → shader `RugWash/Rug Surface` | Mises à jour localisées, ≤ 30 envois/s |
| Saletés | 7 types (`DirtType`) avec réponses configurables (`DirtTypeDefinition`) | Extensible sans nouveau système |
| Tapis | 3 `RugDefinition` (classique, oriental, poils longs) | |
| Commandes / économie | `OrderBook` + `OrderEvaluator` purs, `OrderManager`, portefeuille, réputation, 9 améliorations | Autorité locale |
| Sauvegarde | JSON v1 versionné, atomique, copie de secours, migration | Paramètres inclus |
| Entrées | Input System, actions créées en code (`RugWashInputActions`), 7 actions reconfigurables | Clavier/souris uniquement |
| Animation en main | `ViewModelRig` : balancement souris, balancier de marche, montée à l'équipement | Pas de mouvement de caméra |
| Audio | 15 sons procéduraux `tmp_*`, `AudioManager` (catégories, pause) | |
| Réseau | **Aucun** paquet réseau ni Steam | NGO 2.13.1 fourni avec l'éditeur |

## 2. Bug d'alignement des outils — cause identifiée

**Symptôme** : l'outil semble viser vers le bas de l'écran alors que le nettoyage a lieu sous le réticule.

**Analyse** :
- Le raycast part bien du centre exact de la caméra (`ViewportPointToRay(0.5, 0.5)`) : le point nettoyé, l'anneau de visée et le réticule sont cohérents entre eux.
- En revanche, les modèles tenus en main ont une orientation **fixe dans le repère de la caméra**, choisie à la main
  (ex. brosse : `Euler(10, -14, 0)`, extrémité à `(0, -0.09, 0.4)`) et **jamais orientée vers le point visé**.
- Calcul pour la brosse, cible à 2 m sous le réticule : l'axe poignée → tête pointe environ **33° sous la cible**.
  Les quatre autres outils ont la même orientation fixe ; leur écart réel est mesuré par le test d'alignement du jalon 1.
- **Mesure (jalon 1, 7 poses par outil)** — écart sans correction : brosse 31–35°, extracteur 30–33°, aspirateur 27–30°,
  lance 9–13°, détergent 7–8° (toujours sous la cible). Après correction : ≤ 0,05° pour les 35 cas.
- Le jet d'eau était, lui, réorienté vers la cible depuis la buse : il « sortait de travers » d'une lance mal orientée.

**Correction retenue** (cause traitée, pas de décalage arbitraire) : chaque outil connaît son extrémité active (`aimTip`) ;
à chaque frame, la rotation minimale qui amène l'axe pivot → extrémité sur la direction pivot → point visé est calculée,
exprimée dans le repère du support (pour ne pas réagir aux rotations de caméra), lissée et bornée. L'erreur résiduelle
est mesurée (`AimErrorDegrees`) et testée.

## 3. Hypothèses « un seul joueur » (pour le multijoueur, jalon 5)

| Point | Impact | Traitement prévu |
|---|---|---|
| `GameStateManager` met le temps global en pause (`Time.timeScale = 0`) | Incompatible avec une partie partagée | Pause locale (menu + entrées coupées) en multijoueur, sans figer le temps |
| Les outils appellent directement `rug.ApplyOperation` | Chaque client modifierait sa propre grille | Envoyer des **intentions** (outil, point, rayon, durée) à l'hôte, qui applique et réplique la grille par rectangles modifiés (déjà calculés par `TryConsumeDirtyRect`) |
| `OrderManager`, portefeuille, réputation locaux | Récompenses calculées par chaque client | Évaluation et paiement **uniquement côté hôte** (`OrderEvaluator` est pur) |
| `GameBootstrap` câble un seul joueur, HUD lié à ce joueur | Un seul joueur local | Joueur local instancié par client ; joueurs distants sans caméra ni entrées |
| Sauvegarde locale | Qui possède la progression ? | Progression de l'hôte = progression de l'atelier (choix à documenter au jalon 5) |
| Effets (particules, sons) pilotés par l'outil local | À reproduire chez les autres | État « outil actif + utilisation » répliqué ; effets générés localement |

## 4. Architecture réseau envisagée (jalon 5)

- **Netcode for GameObjects 2.13.1** (fourni avec Unity 6.5, maintenu par Unity) : synchronisation des joueurs, RPC, variables réseau.
- **Steamworks.NET** pour l'initialisation Steam, les lobbies, les invitations et « rejoindre un ami ».
- **Transport Steam** : un transport NGO basé sur Steam Networking Sockets (relais Steam, fonctionne à travers Internet
  sans ouverture de ports). Candidat : le transport communautaire `SteamNetworkingSocketsTransport`
  (dépôt `multiplayer-community-contributions`). **Compatibilité avec NGO 2.13 à vérifier** avant intégration ;
  en cas d'incompatibilité, écriture d'un transport minimal sur `SteamNetworkingSockets`.
- Unity Transport (UTP) conservé pour les tests locaux à plusieurs instances (connexion directe 127.0.0.1).

## 5. Risques identifiés

1. **Tests Steam** : nécessitent le client Steam ouvert, un App ID (480 « Spacewar » pour le développement) et
   **deux comptes Steam sur deux machines** pour valider invitations et connexion à distance. Depuis cet environnement,
   je peux tester plusieurs clients locaux, mais pas un second compte Steam : ces tests devront être faits par vous.
2. **Réplication de la grille** : jusqu'à 192 × 128 cellules × 9 valeurs ; il faudra compresser (octets, rectangles modifiés).
3. **Manette** : aucune manette n'est branchée sur la machine de test ; les tests automatisés utiliseront une manette
   virtuelle de l'Input System, le ressenti restera à vérifier à la main.
4. **Dépendances externes** (Steamworks.NET, transport Steam) : versions à figer et licences à documenter.

## 6. Ordre de travail

Jalon 1 (corrections et game feel) : **terminé** — implémenté, compilé, testé (91 EditMode, 50 PlayMode), build et
benchmark revalidés ; détail dans `PRODUCTION.md` (section Roadmap V2) et `VALIDATION.md` (section 8).
Jalon 2 (outils et atelier) : **terminé** — canon à mousse, système de mousse, balai, brosse de détail, aire de lavage
et traces de pas, robot aspirateur ; détail dans `PRODUCTION.md` et `VALIDATION.md` (section 9). Le plan de travail et le
sol réutilisent la grille, les opérations, les outils et le rendu du tapis (aucun système parallèle).
Les jalons suivants seront traités dans l'ordre de la roadmap, chacun compilé, testé et documenté avant le suivant.
