# Rapport — mission « Bugfix & optimisation » (RugWash Simulator)

Travail réalisé dans le projet Unity existant (Unity 6000.5.7f1), sans nouvelle fonctionnalité hors de ce que la mission
demandait. Toutes les mesures citées ont été **exécutées** sur une seule machine : RTX 5060, i7-8700K, 32 Go, Windows 11,
Direct3D 11, 1920×1080. Détail des exécutions : `VALIDATION.md` § 20. Multijoueur : `MULTIPLAYER.md`.

Méthode suivie (phases A à D) : audit et mesure avant modification (phase A, résumé ci-dessous) ; corrections par ordre de
priorité, sauvegardes et multijoueur d'abord (B) ; un test automatique par correction, puis la suite complète (C) ;
mesures avant/après et documentation (D).

## Audit initial (phase A)

| # | Constat vérifié avant modification | Cause |
|---|---|---|
| 3 | Un invité jouait avec **sa propre** carrière (validée par l'hôte) et la sauvegardait | Conception du jalon 1 v0.1.2, contraire à la règle de la mission |
| 2.4 | Crash détecté au bout de 30 s ; aucune identité de joueur : revenir dans ce délai créait un 2ᵉ personnage | Délai par défaut d'Unity Transport, pas d'éviction |
| 2.3 | Protocole comparé, mais version du jeu absente du message ; salon Steam non vérifié avant la connexion | — |
| 2.1 | Aspirateur enfoncé de **16 cm** dans le sol de près, monobrosse de 7 mm | Manche allongé d'~1 m même de près, rotation de visée bornée à 70° |
| 2.2 | Émetteur sur la buse (0 cm), mais arme et caméra bougées **après** l'émission ; chez les autres, jet posé sur la main, trop lent | Ordre d'exécution ; effet distant non rattaché à la buse |
| 2.8 | Raclette : ni savon restant ni débris ramassés, rien retiré hors caniveau, bloquée au bord, sans effet sur l'eau d'un tapis | Opération de ramassage incomplète |
| 2.5/2.9 | Fusil jamais sur un joueur ; chute bornée à 6 m/s et 3 s ; aucun objet transporté dans le jeu | Choix du jalon 6 |
| 2.6 | Barre d'outils : les 11 outils, achetés ou non | — |
| 2.7 | Onglet « Matériel » mêlant outils et améliorations | — |
| 2.10/2.11 | Pas d'écart avec ou sans mousse au benchmark solo ; à mesurer en multijoueur et selon les réglages | À mesurer |

## A. Bugs corrigés

| Bug | Symptôme | Cause | Correction | Fichiers | Test |
|---|---|---|---|---|---|
| **Économie de session** (3) | L'invité utilisait et sauvegardait sa propre carrière | Règle du jalon 1 | L'économie de la session est celle de la sauvegarde de l'hôte (argent, réputation, achats, produits, détergent), diffusée aux invités (≤ 4 fois/s, complète à l'arrivée) ; achats et consommations des invités appliqués chez l'hôte, payés par l'atelier ; récompense versée une fois, chez l'hôte ; l'invité n'écrit que ses préférences ; protocole `rugwash-coop-8` | `NetworkWorkshop.cs`, `NetworkWorkshop.Pranks.cs`, `SaveManager.cs`, `NetworkContent.cs` | `Guest_NeverWritesTheHostsSessionEconomy_…` (PlayMode), scénario à deux processus (§ 20.2) |
| **Fantôme / reconnexion** (2.4) | Personnage resté 30 s après un crash ; doublon si le joueur revenait vite | Délai d'expiration par défaut ; pas d'identité | Délai 10 s ; identité stable (compte Steam, sinon tirée une fois par installation) dans les données de connexion ; ancienne connexion du même joueur fermée à son retour | `MultiplayerSession.cs`, `SessionRules.cs` | `BugfixSessionTests`, deux processus : silhouette retirée en 9,7 s, reconnexion sans doublon |
| **Hôte sourd après un crash** (trouvé en testant 2.4) | Après le crash d'un invité, plus aucune connexion acceptée | Bogue d'Unity Transport sous Windows : tampons de réception perdus après des erreurs ICMP ([discussions.unity.com](https://discussions.unity.com/t/unity-transport-6-6-0-udp-host-stops-receiving-after-abrupt-client-disconnect-on-windows/1736023), comportement Windows analogue : [o3de#20149](https://github.com/o3de/o3de/issues/20149)) | Paquet intégré `Packages/com.unity.transport` (6.5.0) : le tampon d'une réception échouée est rendu (une ligne, commentée) | `Packages/com.unity.transport/Runtime/UDPNetworkInterface.cs` | Deux processus : reconnexion après crash ✅ (échouait avant) |
| **Versions** (2.3) | Message sans version du jeu ; salon Steam rejoint sans vérification | — | Version du jeu et du protocole dans les données de connexion (format compatible) et dans le message des deux côtés ; version du salon Steam lue avant de rejoindre ; aucune mise à jour automatique | `SessionRules.cs`, `MultiplayerSession.cs` | `BugfixSessionTests` (le chemin Steam n'a pas pu être exécuté) |
| **Tête dans le sol** (2.1) | Tête des outils de contact sous la surface de près | Allongement télescopique à la première personne | Plus d'allongement à la première personne (l'outil pivote vers le point visé) ; les silhouettes des autres gardent le manche télescopique | `CleaningToolBase.cs`, `RugWashSceneBuilder.cs` | `ContactTools_LocalHeadNeverSinksBelowTheSurface_…` (5 outils, 3 hauteurs d'yeux, plusieurs poses, pendant l'utilisation), `RemoteAvatar_HoldsTheSameDetailedTool_…` |
| **Jet d'eau** (2.2) | Jet décalé de la buse en bougeant ; chez les autres, jet parti de la main, trop court | Balancement appliqué après l'émission ; effet distant non rattaché | Balancement de la caméra et de l'arme calculé avant la visée (ordre d'exécution) ; jet distant rattaché à la buse de l'outil tenu, vitesse calée sur la distance | `CameraMotion.cs`, `ViewModelRig.cs`, `NetworkPlayerAvatar.cs` | `SprayEffects_LeaveFromTheToolMuzzle_…`, `RemoteAvatar_…` (jet rattaché à la buse) |
| **Raclette** (2.8) | Savon et débris laissés, bloquée au bord, rien sur un tapis | Ramassage incomplet | Ramasse eau, mousse, savon restant, boue détachée et débris secs non fixés ; retrait réel proportionnel à la distance raclée ; dépôt au-delà du bord évacué ; sur un tapis : **seulement l'eau**, jamais la saleté (aussi en réseau) | `CleaningOperations.cs`, `SqueegeeTool.cs`, `SurfaceOperationCodec.cs` | `Gather_TakesWaterFoamSoapLooseMudAndDebris_…` (EditMode), `Squeegee_PushesWaterAlongTheStroke_…_AndOnlyWipesWaterFromARug` |
| **Fusil** (2.5) | Ne visait jamais un joueur | Choix du jalon 6 | KO cartoon de 5 s : gadget rangé (le jeu n'a pas d'objet transporté à lâcher), contrôles coupés, ragdoll avec un élan fort, tête qui « éclate » chez les autres (cachée + gerbe de particules cartoon, aucun gore réaliste), tache de faux sang stylisée sous le joueur, relevé automatique, pas de relevé anticipé, pas de KO enchaîné ni sur un joueur à terre, joueur parti ignoré, validé par l'hôte, même résultat chez tous | `PrankTuning.cs`, `PrankRules.cs`, `PrankService.cs`, `PrankGadgetController.cs`, `PlayerKnockdown.cs`, `NetworkPlayerAvatar.cs`, `NetworkWorkshop.Pranks.cs`, constructeurs | `Blaster_…_AndKnocksAPlayerOutForFiveSeconds`, `BlasterKnockout_…` (×2), `Authority_…` |
| **Ragdoll** (2.9) | Chutes peu marquées | Élans bornés bas | Élans exagérés (batte 6 m/s, fusil 9 m/s + 3 m/s vers le haut) ; garde-fous : vitesse ≤ 12 m/s, rotation ≤ 10 rad/s, durée ≤ 8 s, déplacement ≤ 12 m (sinon retour au point de départ), valeurs reçues du réseau bornées | `AvatarRagdoll.cs`, `PlayerKnockdown.cs`, `PrankTuning.cs` | `RemoteAvatar_Ragdolls_ThenReturnsToItsPose`, `BlasterKnockout_…` |
| **Barre d'outils** (2.6) | Outils non achetés affichés | — | Seulement les outils possédés, reconstruite à chaque achat ou changement d'équipement | `ToolSelectionUI.cs` | `Toolbar_ShowsOnlyOwnedTools_…`, `WorkshopV2…` |
| **Boutique** (2.7) | Outils et améliorations mélangés | — | Onglets séparés « Outils » et « Améliorations » | `ShopUI.cs`, `RugWashSceneBuilder.cs` | `ContentV3…` (chaque article dans un seul onglet), `Shop_RefusesWithoutFunds_…` |
| **Mousse en multijoueur** (2.10) | À-coups avec beaucoup de mousse (hôte 13,5 ms d'écriture dans une image ; invité 9,3 ms de lecture + 7,6 ms de réencodage) | Correction lente envoyée d'un bloc (grilles entières) ; rendu réencodant un rectangle englobant toute la grille ; aire de lavage sale (ruissellement de la mousse) resalissant les quatre bords du tapis d'un coup toutes les 0,4 s, d'où un renvoi du tapis entier | Correction envoyée par tranches de lignes (≤ 4 096 cellules par envoi) ; rendu ligne par ligne ; un bord recontaminé à la fois (même quantité moyenne) | `SurfaceReplicators.cs`, `NetworkWorkshop.cs`, `RugCleaningGrid.cs`, `RugDirtRenderer.cs`, `WorkSurface.cs` | `SlowCorrection_WithLotsOfFoam_…`, `DirtyRows_…`, `DirtyWashArea_RecontaminatesOneBorderAtATime_…`, deux processus (après : écriture de l'hôte 3,3 ms, lecture de l'invité 3,1 ms, débit 41 → 11 Ko/s, aucun à-coup) |
| **Niveaux de qualité sans effet** (2.11) | « Moyen » coûtait presque autant qu'Ultra | Un seul pipeline pour les six niveaux | Un pipeline par palier (Bas, Moyen, Élevé ; Ultra inchangé), réglages choisis d'après la mesure | `RugWashProjectBootstrapper.cs`, `Settings/Rendering/*`, `QualitySettings.asset` | `QualityTierTests`, benchmarks § 20.5 |

**Bugs trouvés pendant la recherche complémentaire (section 4)**, méthode : relecture ciblée de chaque système touché
(abonnements et désabonnements d'événements, durée de vie des drapeaux de session, chemins « déjà à terre » et « joueur
parti », messages affichés), mesures instrumentées, scénario à deux processus, puis suite complète.

| Bug | Cause | Correction | Test |
|---|---|---|---|
| Le fusil pouvait remettre KO un joueur déjà à terre | Seule la batte vérifiait l'état « à terre » | Toutes les farces de chute le vérifient | `Authority_…` |
| L'aide du fusil disait « tapis » seulement | Texte du jalon 6 | « tirer (tapis, ou coéquipier : KO 5 s) » | — |
| Outil rangé encore en surbrillance avec un gadget en main | Pas d'événement à la sortie d'un gadget | Événement `SuspendedChanged` | `Toolbar_…` |
| Réglages du fusil jamais migrés dans l'asset existant | Version initialisée à la valeur courante : un ancien asset se croyait déjà à jour | Version initialisée à 1 | `Blaster_…` (dans la vraie scène) |

## B. Améliorations

- Outils de mesure : modes de benchmark `foam`, `graphics`, `quick`, niveau de qualité, échelle de rendu, affinité CPU,
  build Development et analyse automatique des captures du **Profiler Unity** (`RugWashProfileAnalysis`) ; temps CPU du fil
  principal et du fil de rendu, à-coups, 95e centile, allocations ; garde « GPU déjà occupé ».
- Scénario multijoueur étendu : crash puis reconnexion, fermeture brutale de l'hôte, sauvegardes relues, mesure de la
  mousse en ligne.
- Quatre paliers de rendu réels (voir D).

## C. Sauvegardes et multijoueur

Règle appliquée : pendant une session hébergée, l'invité utilise l'argent, la réputation, les outils et les améliorations
de la sauvegarde de l'hôte ; ses achats s'appliquent à l'atelier de l'hôte ; sa sauvegarde personnelle n'est jamais
modifiée (seules ses préférences : réglages, apparence) ; en quittant, il retrouve sa progression ; la sauvegarde de
l'hôte enregistre la session ; une reconnexion ne duplique rien ; un crash ne corrompt aucune sauvegarde.

Vérifié (deux processus, § 20.2) : économie de l'hôte affichée et utilisée par l'invité ; achat de l'invité payé par
l'atelier ; achat impossible refusé sans débit ; récompense unique ; sauvegarde de l'invité identique avant, pendant, après
son crash et après la session (1 500 €, réputation 2, 8 achats) ; crash de l'invité : silhouette retirée, reconnexion sans
doublon ni achat en double ; hôte tué : invité ramené au menu avec un message, sauvegarde de l'hôte relue intacte (318 €).

## D. Performances

**Configuration** : RTX 5060, i7-8700K, 32 Go, 1920×1080. **Protocole** : `VALIDATION.md` § 20.3.

- **Mousse** : Profiler Unity et mesures avec et sans mousse (solo) : aucun surcoût GPU (shader opaque à coût constant,
  particules sans effet mesurable) ; coût CPU ~0,35 ms/image avec trois surfaces couvertes. Cause réelle des
  ralentissements : multijoueur (tableau § 20.4) — corrigée.
- **Réglages** : à 1080p, cette machine tient ~195–210 FPS à tous les niveaux. Sous charge GPU ×4 (approximation d'une carte
  ~4 fois moins puissante) : dans les scènes de mousse, Ultra 122–126 FPS (GPU 7,8–8,1 ms), Élevé 152–160 FPS (6,2–6,5 ms), Moyen 185–189 FPS (5,2–5,3 ms), Bas ~200 FPS ; sans limitation, le CPU n'est pas le facteur limitant (2 fils CPU suffisent ici).
- **Limites** : aucun PC moins puissant n'a été utilisé ; charge GPU ×4 et limitation à 2 fils CPU sont des approximations ;
  aucune garantie de FPS sur d'autres machines.

## E. Bugs et limites restants

- À la première personne, la tête des outils de contact ne touche plus le tapis (elle reste au-dessus, l'outil pivote vers
  la cible) : c'est le compromis accepté par la mission pour supprimer l'intersection avec le sol. Chez les autres joueurs,
  la tête reste posée sur la cible.
- Deux invités lancés sur le **même PC** partagent la même identité de joueur (cas de test ; `-rugwash-player-key`).
- Le correctif d'Unity Transport est une copie locale du paquet : à supprimer quand Unity livrera la correction.
- Les compteurs de rendu (lots, SetPass) restent indisponibles dans les builds mesurés.
- Une nouvelle installation démarre en Ultra ; aucune détection automatique du matériel.

## F. Non fait / non testé

- Aucun test sur un vrai PC moins puissant, ni sur deux machines, ni via Internet, ni avec plus de deux joueurs.
- Steam en réel (vérification de version du salon, transport Steam) : non exécuté (pas de client Steam lancé).
- Reconnexion automatique : non demandée, toujours manuelle (bouton).
- « Lâcher les objets transportés » au KO : le jeu n'a pas de système d'objets transportés ; seul le gadget en main est rangé.

## G. Confirmation du périmètre

Aucune nouvelle fonctionnalité de jeu hors de la mission : les ajouts sont ceux qu'elle demandait (KO du fusil et effets
cartoon, onglets de boutique, règle d'économie de session, reconnexion) et des outils de mesure. Mousse, effets et logique de
nettoyage conservés (mêmes résultats, testés). Rien n'a été commité ni poussé.
