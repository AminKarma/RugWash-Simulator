# Suivi de production — RugWash Simulator

Statuts : **À faire** · **En cours** · **Implémenté** (code présent, non vérifié par un test exécuté) ·
**Testé** (vérifié par un test automatisé réellement exécuté, ou par une capture examinée) · **Bloqué**.

Dernière mise à jour : fin de la phase 3 — Unity 6000.5.7f1. Tests exécutés : 67/67 EditMode, 22/22 PlayMode.

## Phase 0 — Audit et outillage

| Élément | Statut | Preuve |
|---|---|---|
| Projet Unity créé (dossier initialement vide), version fixée 6000.5.7f1 | Testé | Import et compilation en mode batch |
| Packages alignés sur l'éditeur (URP 17.5, Input System 1.20, uGUI 2.5, Test Framework 1.7) | Testé | `ProjectHealthTest` |
| Input System comme seul backend, espace linéaire, DX11/DX12/Vulkan | Testé | `ProjectHealthTest` |
| URP Forward+, ombres des lumières additionnelles | Implémenté | Réglé par le bootstrapper ; visible sur les captures |
| Scènes MainMenu / Workshop / Results dans les Build Settings | Testé | `ExistingGameScenes_AreInBuildSettings` |
| Pipeline CLI (`Tools/RugWash-CI.ps1`) | Testé | Exécuté de bout en bout |

## Phase 1 — Prototype de nettoyage

| Fonctionnalité | Statut | Preuve |
|---|---|---|
| Grille de nettoyage par cellules (résolution configurable) | Testé | `CleaningRulesTests` |
| Génération de saleté procédurale déterministe (5 motifs, 7 types) | Testé | `DirtGeneration_IsDeterministicForSameSeed`, `GeneratedRug_StartsWithDirt` |
| Outil n'affecte que la zone visée | Testé | `Tool_AffectsOnlyCellsInsideRadius` + PlayMode `Brush_AimedAtRug…` |
| Brosse (détache, sensible à l'humidité et au type) | Testé | `Brush_*` (EditMode) + PlayMode |
| Lance de rinçage (évacue, mouille, consomme l'eau) | Testé | `Rinse_*` (EditMode) + PlayMode |
| Réservoir d'eau + poste de remplissage (E) | Testé | `Rinse_WithEmptyTank_IsBlocked_UntilRefilled` |
| Pas de progression infinie sur zone propre | Testé | `CleanArea_GivesNoInfiniteProgress` |
| Propreté affichée = données de la grille | Testé | `Cleanliness_MatchesUnderlyingData` |
| Rendu : masque de saleté révélant le motif d'origine | Testé (visuel) | Captures `03`, `04`, `05` examinées |
| Mise à jour de texture localisée et limitée en fréquence | Testé | `DirtyRect_CoversOnlyModifiedCells` |
| 3 tapis (oriental à médaillon, classique poils courts, poils longs) | Implémenté | Données + textures ; changement testé (`SwitchingRug…`) ; seul l'oriental est examiné visuellement |
| Contrôleur FPS (accélération, gravité, collisions, ZQSD/WASD) | Testé (manuel) | Validé manuellement par l'utilisateur en fin de phase 1 |
| Sélection d'outils (1-5, molette) | Implémenté | `SelectSlot` testé ; touches réelles non simulées |
| Pause (Échap) : temps figé, outils arrêtés, curseur libéré | Testé (logique) | `Pause_FreezesTime…` ; verrouillage réel du curseur = **vérification manuelle** (impossible en mode batch) |
| Inspection avant/après (F) | Testé | `Inspection_Opens…` + captures |
| HUD (outil, eau, propreté, invites, aide) | Implémenté | Capture `06_hud` |
| Effets : jet d'eau, éclaboussures, débris de brossage (systèmes persistants, bornés) | Testé | `Effects_StayBounded_DuringLongUse` |
| Overlay de diagnostic F3 (éditeur / build dev uniquement) | Implémenté | — |
| Génération procédurale complète de la scène | Testé | Étape `scene` + `WorkshopScene_IsFullyWired` |

## Phase 2 — Outils et effets

| Fonctionnalité | Statut | Preuve |
|---|---|---|
| Aspirateur (poussière/débris ; saleté détachée sur fibres sèches ; sans effet sur taches incrustées) | Testé | `Vacuum_*` (EditMode) + `Vacuum_RemovesDust_AtTarget` (PlayMode) |
| Pulvérisateur de détergent (mousse, résidus, consommation, blocage à vide) | Testé | `Soap_*` (EditMode + PlayMode) |
| Détergent rendant la brosse efficace sur graisse | Testé | `Soap_MakesBrushEffectiveOnGrease` |
| Rinçage : mousse retirée plus vite que les résidus | Testé | `Rinse_RemovesFoamFasterThanResidue` |
| Extracteur (humidité → cuve, emporte saleté détachée et résidus, sans effet à sec, blocage cuve pleine) | Testé | `Extractor_*` (EditMode + PlayMode) |
| Séquence complète aspirer → savonner → brosser → rincer → extraire = cellule propre et sèche | Testé | `FullCleaningSequence_ReachesCleanDryState_OnGrease` |
| Postes : eau, produits (détergent), caniveau (vidange de la cuve) | Testé | `Rinse_WithEmptyTank…`, `SoapStation_Refills_Detergent`, `Extractor_…DrainEmptiesIt` |
| Sons en boucle par outil, coupés au changement d'outil et en pause | Testé | `ToolSound_Plays_ThenStopsOnToolChangeAndPause` |
| Sons d'interface, remplissage, vidange, éclat de zone propre | Implémenté | Clips assignés ; écoute non vérifiable automatiquement |
| Éclat (particules + son) quand des cellules deviennent propres, pool borné | Testé | `CleanFeedbackPool_StaysBounded` |
| Effets : jet d'eau étiré, éclaboussures, brume de savon, bulles, poussière aspirée, gouttes extraites | Testé (visuel) | Captures `02`, `02a`, `02b` examinées |
| Sol de l'aire de lavage mouillé par l'usage réel, puis séchage | Testé | `FloorGetsWet_WhenRinsing_ThenDries` + captures |
| Rendu des résidus de produit (voile pâle) | Testé (visuel) | Capture `05_snapshot_after` |

## Phase 3 — Boucle de jeu

| Fonctionnalité | Statut | Preuve |
|---|---|---|
| 6 commandes (client, tapis, saleté propre, difficulté, récompense, objectifs, réputation requise) | Testé | Données + `LockedOrders_CannotBeAccepted`, capture `09` |
| États Disponible / En cours / Terminée ; une seule commande active | Testé | `AvailableOrder_CanBeStarted_OnlyOneAtATime` |
| Évaluation centralisée (saleté, résidus, humidité, temps) sans pénaliser les critères non demandés | Testé | `PerfectJob…`, `ResidueAndMoisture_…OnlyWhenEnabled`, `SGrade_RequiresAllObjectives`, `SlowJob…` |
| Refus du client sous 40/100 : aucun paiement, commande à nouveau disponible | Testé | `BarelyCleanedRug_IsRefused…`, `DirtyDelivery_IsRefused…` (PlayMode) |
| Récompense versée une seule fois ; commande terminée non refaisable | Testé | `CompletedOrder_PaysOnce…`, `FullOrder_…PaysOnce…` (PlayMode) |
| Nouvelle commande possible après la précédente ; abandon depuis la pause | Testé | `RefusedOrder_…NextOrderCanStart`, `AbandonFromPause…` |
| Réputation gagnée / perdue selon la note, débloque des commandes | Testé | `FullOrder…` (réputation > 0), `LockedOrder_RequiresReputation` |
| Portefeuille (jamais négatif, débit seulement si valide) | Testé | `Wallet_RefusesOverspend…` |
| Boutique : 9 améliorations, prix, effet réel affiché, refus sans fonds, prérequis | Testé | `Purchase_*`, `Shop_RefusesWithoutFunds_ThenPurchaseAppliesRealEffects`, capture `10` |
| Améliorations : capacités des réservoirs, puissance des outils, consommation de détergent | Testé | `Upgrades_ProduceAnnouncedEffects` + vérification dans la scène |
| Tableau des commandes (accepter / livrer avec note estimée / abandonner) | Testé | Boutons invoqués dans `FullOrder…`, capture `09` |
| Écran de résultats (note, critères, paiement, avant/après, retour / autre commande) | Testé | Boutons invoqués dans les tests, capture `11` |
| HUD : solde, réputation, commande active et chronomètre | Testé (visuel) | Captures `09`–`11` |

## Phases suivantes

| Fonctionnalité | Statut |
|---|---|
| Menu principal, paramètres (volume, sensibilité, résolution, qualité, touches), résultats, sauvegarde JSON (phase 4) | À faire |
| Qualité visuelle : modèles d'outils et décor, SSAO, animations (phase 5) | À faire |
| Build Windows vérifié, mesure de performance 1080p (phase 6) | À faire |

## Limites connues (phase 1)

- **Sons temporaires** : tous les sons sont synthétisés (`Audio/Generated/tmp_*.wav`). Ils sont fonctionnels
  mais ne sont pas des sons définitifs. Aucun enregistrement réel n'est présent.
- **Pas de sauvegarde** : argent, réputation, achats et commandes terminées sont perdus en quittant (phase 4).
- **Résultats affichés dans l'atelier** (fenêtre), pas dans `Results.unity`, qui reste une scène provisoire :
  cela évite de transférer l'état entre scènes avant l'existence de la sauvegarde.
- Le catalogue contient 6 commandes non renouvelables : une fois toutes terminées, il ne reste que l'entraînement
  (génération de nouvelles commandes à prévoir).
- **Pas d'écran de paramètres** : la sensibilité souris est réglable dans l'Inspector uniquement.
- **MainMenu.unity et Results.unity sont des scènes provisoires** (texte « Écran prévu en phase 4 »).
- **Modèles provisoires** : outils tenus en main, mobilier et panneaux sont assemblés à partir de formes
  simples texturées. À remplacer en phase 5.
- **Séchage naturel du tapis non simulé** : seule l'extraction réduit l'humidité (le sol de l'aire de lavage, lui, sèche).
- **Extracteur = abstraction** : l'eau aspirée est calculée depuis l'humidité retirée (3 L/m² saturé).
- Modèles des 5 outils tenus en main : assemblages de formes simples (provisoires, phase 5).
- Les tapis à poils longs utilisent une normale plus marquée, pas de rendu de poils en volume.
- **Performance non mesurée** : la cible 60 FPS en 1080p n'est pas vérifiée. L'overlay F3 permet la mesure.
- Verrouillage réel du curseur, contrôle souris/clavier et lisibilité du HUD à plusieurs résolutions :
  à vérifier manuellement dans l'éditeur (voir scénarios ci-dessous).

## Scénarios de vérification manuelle (phase 1)

1. Lancer `Workshop.unity` : le curseur est capturé, la caméra suit la souris, ZQSD/WASD déplacent.
2. Marcher contre un mur / le poste d'eau : pas de traversée. Monter sur le tapis : pas de blocage.
3. Touche 4, viser le tapis, clic gauche maintenu : jet, éclaboussures, tapis assombri (humide), eau qui baisse.
4. Touche 3, brosser la zone humide : la saleté devient boue sombre ; touche 4 : la zone s'éclaircit, le motif réapparaît.
5. Vider le réservoir : message « Réservoir d'eau vide » ; aller au poste d'eau (mur gauche), E : réservoir plein.
6. F : panneau avant/maintenant, curseur libre ; F ou Échap pour fermer.
6 bis. Touches 1 à 5 : chaque outil a un son distinct ; détergent (2) puis brosse (3) sur une tache de graisse ;
   extracteur (5) jusqu'à remplir la cuve, puis E sur le caniveau ; E au bidon vert pour recharger le détergent.
7. Échap : menu pause, curseur libre, le jeu est figé ; « Tapis suivant » charge un autre tapis.
8. F3 : overlay de diagnostic (FPS, propreté, envois de texture).
9. Changer la résolution de la vue Game (1280×720, 1920×1080, 2560×1440) : HUD lisible, rien ne se superpose.

7 bis. Comptoir de réception (E) → « Accepter » sur « Le tapis du salon » ; nettoyer ; revenir au comptoir →
   « Livrer la commande » → écran de résultats, solde et réputation mis à jour → « Retour à l'atelier ».
7 ter. Boutique (meuble du fond, E) : achat refusé sans fonds ; après une commande, acheter « Réservoir d'eau 60 L »
   puis vérifier dans le HUD que la lance affiche « / 60 L ».

## Points à revoir avant un mode coopératif (non développé)

- Les outils modifient directement la grille du tapis visé : en réseau, il faudra transmettre des
  « intentions de nettoyage » (point, rayon, outil, durée) validées par l'autorité, et répliquer la grille
  (ou ses deltas par rectangle modifié — déjà calculés par `TryConsumeDirtyRect`).
- `PlayerEquipment` (consommables) est séparé des données du tapis. Portefeuille, réputation et améliorations
  sont des composants d'atelier distincts du joueur : en coop, décider s'ils sont partagés (atelier) ou par joueur.
- Les règles d'évaluation sont centralisées (`OrderEvaluator`) et pures : à exécuter côté autorité uniquement.
- `GameStateManager` gère pause et curseur localement ; en coop, la pause ne devra plus figer le temps global.
- Les effets (particules) sont pilotés localement par l'outil : à déclencher aussi pour les joueurs distants.
