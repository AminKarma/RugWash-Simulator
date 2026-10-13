# Update v0.1.2 — audit (jalon 0) et décisions du jalon 1

Ce document résume l'état des lieux réalisé avant le jalon 1, les problèmes confirmés, les décisions prises et les
conventions retenues. Les résultats de tests réellement exécutés sont consignés dans `VALIDATION.md` (section 13).

## 1. Systèmes existants réutilisés

| Domaine | Système | Fichiers |
|---|---|---|
| Nettoyage | Grille de cellules (vérité du gameplay) partagée par le tapis, l'aire de lavage et le sol | `Cleaning/RugCleaningGrid.cs`, `RugCleaningController.cs`, `CleaningOperations.cs` |
| Outils | Classe de base, alignement sur la cible, effets locaux | `Cleaning/CleaningToolBase.cs` et les 8 outils |
| Aire de lavage | Ruissellement, état chiffré, recontamination | `Cleaning/WorkSurface.cs`, `WorkSurfaceRules.cs` |
| Commandes | Tableau, évaluation, paiement unique | `Orders/OrderManager.cs`, `OrderBook.cs`, `OrderEvaluator.cs` |
| Économie | Portefeuille, réputation, améliorations, produits | `Economy/*`, `Cleaning/ProductStockBook.cs` |
| Sauvegarde | JSON versionné (v1), migration, validation | `Save/*` |
| Réseau | NGO 2.13.3, hôte autoritaire, réplication des surfaces, Steam | `Network/*` |
| Robot | Grille d'occupation, chemin, base, batterie | `Workshop/*` |

## 2. Problèmes confirmés (reproduits dans le code ou les captures)

| Point | Constat | Cause |
|---|---|---|
| 1.4 Alignement | La tête des outils de contact reste en bas de l'écran, loin du réticule (captures `20_tool_aligned_*`, `61_coop_workshop`) | L'outil ne faisait que **pivoter** autour de la main ; sa longueur (0,5 à 0,9 m) est bien inférieure à la portée de nettoyage (2,1 à 2,4 m) : il pointait vers la cible sans jamais l'atteindre |
| 1.3 Jet | Joueur local : le jet partait déjà de la buse. Joueurs distants : effets sur un point fixe de la main | Silhouettes sans modèle d'outil réel ni point visé |
| 1.7 / 1.8 | Silhouettes en cylindres, outils provisoires, aucun suivi de la visée | Prototype du jalon 5 V2 |
| 1.1 / 1.2 | L'invité affichait et dépensait **l'argent, la réputation, les achats et les produits de l'hôte** ; à l'arrivée il voyait d'abord son propre atelier (aire de lavage sauvegardée, tapis d'entraînement) puis celui de l'hôte | Économie partagée par variables réseau ; atelier local restauré avant les instantanés |
| 1.6 | Le « sol » couvre toute la salle (12 × 10 m) : poussière et poils initiaux, eau/mousse des outils, traces de pas | Surface de sol salissable partout |
| 1.9 | Un tapis d'entraînement était chargé hors commande (démarrage, fin de commande, menu pause, RugDex) | `OrderManager.practiceRug`, bouton « tapis suivant » |
| 1.10 | Recontamination dès 1 % de surface souillée ; aucun seuil pour accepter une commande | Règle d'origine |
| Phase 6 | Propreté = 1 − saleté restante / saleté initiale avec un retrait asymptotique : bloquée à 99 % | Aucun seuil de fin |

## 3. Décisions du jalon 1

### Conventions de l'aire de lavage (1.10)
- **Saleté** de l'aire = part de la surface souillée (cellule avec saleté ≥ 0,05 ou mousse ≥ 0,08 ; l'eau claire ne compte
  pas). **Propreté** = 1 − saleté. Les deux ne sont jamais mélangées ; l'affichage arrondit la propreté vers le bas.
- Nouvelle commande possible si **propreté ≥ 25 %** (sinon refus expliqué au comptoir, ligne « Aire de lavage à
  nettoyer », indicateur du HUD).
- Au-delà de **40 % de saleté** (propreté < 60 %) : le tapis posé se salit — bordure recontaminée à la pose (0,05 à 0,35
  de saleté par cellule selon l'excès), puis saleté ajoutée en continu tant que l'aire reste trop sale (0,015/s à
  saleté 100 %, proportionnel à l'excès), plafonnée à 0,35 par cellule, jamais sur un tapis terminé.
- Valeurs réglables dans `CleaningTuning` (`zoneMinCleanlinessToStart`, `zoneDirtyThreshold`, `zoneContaminationPerSecond`).
- Donnée absente ou illisible : l'aire est considérée propre (aucun blocage définitif).

### Fin du tapis (phase 6)
- Terminé quand il reste **strictement moins de 1 %** de la saleté initiale (`rugFinishedRemaining`). Le reliquat
  (cellules isolées, traces invisibles, arrondis) est alors retiré : propreté exactement 100 %, objectif atteint, réussite
  affichée (message, son, éclats). L'eau, la mousse, les résidus et l'usure sont conservés : la note de livraison les
  évalue toujours avec le système existant. Décidé par l'hôte ; les invités reçoivent les cellules (même résultat).

### Zone de nettoyage (1.6) et robot
- Seule l'aire de lavage (et le tapis posé dessus) accumule poussière, eau, mousse et résidus. Le sol de la salle est
  inerte : plus de saleté initiale, pas une cible des outils, pas de traces de pas, opérations ignorées (y compris
  reçues du réseau), état ignoré dans une ancienne sauvegarde.
- **Changement d'ordre assumé** : le robot aspirait le sol de la salle ; sans saleté possible il n'avait plus de rôle.
  Sa limitation à la zone de nettoyage (demandée en 3.6, jalon 4) est donc avancée : base dans le coin arrière gauche de
  l'aire, grille d'occupation limitée à l'aire, tapis posé traité comme obstacle (grille recalculée à chaque pose/retrait),
  ancienne position hors zone → retour à la base. L'achat du robot reste au jalon 4.

### Tapis d'entraînement (1.9)
- Supprimé du flux : un tapis n'apparaît qu'avec une commande et repart à la livraison ou à l'abandon. Le bouton
  « tapis suivant » et la mention dans le RugDex sont retirés. Le chargement générique d'un tapis (`LoadRug`) est
  conservé (commandes, réseau, tests).

### Outils (1.3, 1.4, 1.7)
- Outils de contact (aspirateur, brosse, extracteur, balai, brosse de détail) : **manche télescopique** — la tête glisse
  le long du manche, le manche s'étire, et l'outil pivote autour de la main : l'extrémité active se pose sur le point
  visé quelle que soit la distance (dans la portée), l'angle et la hauteur des yeux. Au-delà de la portée, la tête
  s'arrête au point du rayon situé à la portée maximale (« Trop loin »).
- Buses (lance, détergent, canon à mousse) : orientation seule ; le jet part de la bouche de la buse.
- La même règle (`ToolPose`) et les mêmes modèles détaillés servent à la vue à la première personne et aux silhouettes ;
  les silhouettes reconstituent le point visé à partir des états déjà transmis (position, lacet, tangage, outil, usage).
  Aucune donnée réseau supplémentaire.
- Gants de travail sur les poignées, silhouettes en tenue de travail : **placeholders procéduraux**, pas des assets finaux.

### Progression personnelle et autorité (1.1, 1.2)

| Catégorie | Propriétaire | Autorité en ligne | Sauvegardé chez |
|---|---|---|---|
| Surfaces (tapis, aire, sol), commande active, tableau des commandes, robot | Session (atelier de l'hôte) | Hôte | Hôte |
| Argent, réputation, améliorations (outils achetés), produits spécialisés | Chaque joueur | Hôte (carrière de chaque invité tenue par l'hôte pendant la partie) | Chaque joueur |
| RugDex, commandes livrées (historique), statistiques | Chaque joueur | Local | Chaque joueur |
| Paramètres | Chaque joueur | Local | Chaque joueur |

- À l'arrivée, l'invité annonce la carrière lue dans **sa** sauvegarde ; l'hôte la borne (montants, identifiants connus,
  capacités), puis applique lui-même achats, consommations de produits et récompenses, et renvoie le résultat. Un
  invité ne peut pas se déclarer de l'argent ou un outil en cours de partie. Limite assumée (pas de serveur dédié) :
  la sauvegarde locale de l'invité fait foi à son arrivée, comme en solo.
- Livraison : évaluée une fois par l'hôte ; **chaque joueur présent** reçoit la récompense et la variation de réputation
  complètes dans sa carrière.
- Le tableau des commandes de la session est celui de l'hôte (déverrouillé par la réputation de l'hôte, affichée chez
  l'invité) ; la commande livrée entre dans l'historique de chaque participant.
- Arrivée d'un invité : état « synchronisation » (contrôles coupés, HUD en chargement, aucune valeur provisoire) jusqu'à
  réception de toutes les surfaces et de la carrière validée ; l'atelier sauvegardé de l'invité n'est pas restauré.
- Version de protocole passée à `rugwash-coop-2` (format incompatible avec la v0.1.1).

## 4. Hors jalon 1 (rappel de l'ordre)
Jalon 2 (performance mousse/eau), 3 (ordinateur, missions infinies, détergent consommable), 4 (outils, raclette,
monobrosse, bottes, achat du robot), 5 (base, personnalisation, ragdoll, musique), 6 (troll), 7 (intégration).
