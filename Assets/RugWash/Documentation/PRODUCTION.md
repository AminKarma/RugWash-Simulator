# Suivi de production — RugWash Simulator

Statuts : **À faire** · **En cours** · **Implémenté** (code présent, non vérifié par un test exécuté) ·
**Testé** (vérifié par un test automatisé réellement exécuté, ou par une capture examinée) · **Bloqué**.

Dernière mise à jour : fin de la phase 6 — Unity 6000.5.7f1. Tests exécutés : 84/84 EditMode, 36/36 PlayMode, 11/11 contrôles dans le build. Rapport complet : [VALIDATION.md](VALIDATION.md).

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

## Phase 4 — Interface et sauvegarde

| Fonctionnalité | Statut | Preuve |
|---|---|---|
| Menu principal (atelier en arrière-plan, Nouvelle partie, Continuer, Paramètres, Quitter) | Testé | `MainMenu_FullCycle…`, capture `12` |
| « Continuer » seulement si une progression existe ; confirmation avant d'écraser une partie | Testé | `MainMenu_FullCycle…`, `MainMenu_NewGame_AfterConfirmation…` |
| Paramètres : volumes, sensibilité, inversion Y, qualité — appliqués immédiatement | Testé | `Settings_ChangedInUI_AreAppliedSavedAndRestored` |
| Paramètres : résolution et mode d'affichage | Implémenté | `Screen.SetResolution` ; sans effet dans l'éditeur, **à vérifier dans un build** |
| Reconfiguration des touches (7 actions, annulation par Échap, réinitialisation) | Testé (logique) | `KeyBinding_Override…`, `BindingOverrides_SurviveRecreatedActionAssets` ; saisie interactive = vérification manuelle |
| Sauvegarde JSON versionnée (argent, réputation, achats, commandes terminées, statistiques, paramètres, profil) | Testé | `SaveSystemTests`, `DeliveredOrder_IsSaved…`, `Purchase_PersistsAcrossReload` |
| Nouvelle installation sans sauvegarde | Testé | `NewInstall_*` |
| Fichier corrompu : copie de secours, ou nouvelle partie sans blocage ; fichiers endommagés conservés | Testé | `CorruptedMain_*`, `CorruptedSave_DoesNotBlockStartup` |
| Écriture atomique, copie de secours, données invalides jamais écrites | Testé | `SecondSave_KeepsPreviousAsBackup`, `InvalidData_IsNeverWritten…` |
| Migration v0 → v1 ; sauvegarde plus récente ni chargée ni écrasée | Testé | `VersionZero_IsMigrated`, `NewerVersion_IsNotLoaded_NorOverwritten` |
| Pas de récompense dupliquée après rechargement | Testé | `DeliveredOrder_IsSaved_RestoredAfterReload_WithoutDuplicateReward` |
| Pause : Paramètres, Menu principal (commande en cours abandonnée), sauvegarde avant de quitter | Testé | `ReturnToMenu_DuringOrder…`, `MainMenu_FullCycle…` |
| Tests isolés de la vraie sauvegarde (dossier temporaire) | Testé | `TestSaveSandbox` utilisé par toutes les classes PlayMode |

## Phase 5 — Qualité visuelle

| Élément | Statut | Preuve |
|---|---|---|
| Bibliothèque de maillages procéduraux (boîtes arrondies, révolutions, tubes, tissu, poils) | Testé | `VisualContentTests` (bornes, normales sortantes, orientation des triangles) |
| Modèles détaillés des 5 outils (remplacent les cubes) | Testé (visuel) | Planche `14_tools_sheet` examinée |
| Habillage : postes d'eau et produits, étagères garnies, établi à tiroirs, panneau d'outils, comptoir, tapis à sécher et roulés, poutres, conduits, porte, marquage au sol | Testé (visuel) | Captures `15` à `18` |
| Affiche « Les 5 étapes » (aide en jeu) et horloge à l'heure réelle | Testé | `WallClock_ShowsGivenTime` + captures |
| Bordures de l'aire de lavage en bandes de sécurité (texture générée) | Testé (visuel) | Captures `01`, `12` |
| SSAO, lumière du jour, plafonniers détaillés, étalonnage (ACES, balance des blancs, saturation, bloom, vignette) | Testé | `Renderer_HasScreenSpaceAmbientOcclusion` + captures |
| Animations des outils tenus : balancement souris, balancier de marche, montée à chaque changement | Testé (logique) | `ToolSwitch_PlaysEquipAnimation_ThenSettles` ; ressenti = vérification manuelle |
| Ambiance sonore d'atelier, bruits de pas (selon la distance parcourue), annonce « objectif atteint » unique | Testé | `Ambience_Plays…`, `Footsteps_FollowDistance_NotTime`, `ObjectiveReached_IsAnnouncedOnce` |
| Chevauchements corrigés (panneaux sur les fenêtres, tuyau sur le panneau du poste d'eau) | Testé (visuel) | Captures `16`, `18` |

## Phase 6 — Validation et livraison

| Élément | Statut | Preuve |
|---|---|---|
| Build Windows 64 bits en ligne de commande et depuis le menu | Testé | 119,3 Mo, 0 erreur, 0 avertissement |
| Vérification automatique du jeu compilé (menu → commande → sauvegarde → résolution → menu) | Testé | 11/11 contrôles |
| Mesure de performance en 1080p (4 scénarios) | Testé | ≥ 410 FPS de moyenne sur la machine de mesure |
| Résolution et mode d'affichage dans un build | Testé | Contrôles 8 à 10 |
| Rapport de validation, instructions de build, limites | Implémenté | `VALIDATION.md`, README |
| Performance sur matériel plus modeste | À faire | Une seule machine disponible |

## Roadmap V2 — Jalon 0 (audit)

Rapport : [AUDIT_V2.md](AUDIT_V2.md) — état réel du projet, cause du bug d'alignement, hypothèses « un seul joueur »
à lever pour la coopération, architecture réseau envisagée, risques.

## Roadmap V2 — Jalon 1 (corrections et game feel)

| Élément | Statut | Preuve |
|---|---|---|
| 1.1 Alignement : chaque outil pointe son extrémité active vers le point visé (cause traitée : pose fixe jamais orientée) | Testé | `EveryTool_PointsAtTheReticleTarget_FromAllAnglesAndDistances` (5 outils × 7 poses : écart ≤ 0,05°, contre 7° à 35° avant) ; `ToolAlignment_IsStable_NoJitterWhenStill` ; captures `19_*` (avant) / `20_*` (après) |
| 1.1 Le point nettoyé reste exactement sous le réticule, y compris de côté | Testé | `Brush_CleansUnderTheReticle_FromASideAngle`, contrôle de projection dans le test d'alignement |
| 1.2 Balancement caméra (balancier, inclinaison en pas chassé) et outils (souris, marche, inertie aux changements de direction) | Testé | `Walking_MovesCameraAndTool_ButReticleStillMatchesCleaningPoint` (clavier virtuel) |
| 1.2 Réglage « Balancement » (0 % = désactivé), appliqué immédiatement, sauvegardé | Testé | `MotionIntensityZero_DisablesCameraAndToolSway`, `MotionIntensity_SetInSettings_IsAppliedSavedAndRestored`, `MotionIntensity_DefaultsToOne_IsClamped_AndOldSavesKeepDefault` |
| 1.3 Changement d'outil : montée + son de prise en main | Testé | `ToolSwitch_PlaysEquipSound_AndRaisesTool` |
| 1.3 Contact avec le tapis : choc de l'outil, impulsion du cercle, couche sonore de contact (uniquement en contact) | Testé | `ContactWithRug_PlaysContactLayer_PulsesRing_AndOnlyWhileTouching` |
| 1.3 Cercle vert sur une zone déjà propre (lecture de la grille) | Testé | `TargetRing_TurnsCleanColor_OverACleanZone` |
| 1.3 Débris teintés par la saleté réelle, eau trouble puis claire | Testé (visuel) | Captures `21_brush_debris`, `22_rinse_muddy` |
| 1.3 Paliers de propreté 25/50/75/90 % (son, message, panneau qui pulse), barres lissées, pluie d'éclats à l'objectif, son de paiement | Testé | `Milestones_…` (EditMode), `CleanlinessMilestone_PlaysSound_PulsesHud_AndShowsToast`, `ObjectiveReached_PlaysCelebration` |
| 1.4 Scan de saleté (R, reconfigurable) : onde depuis le joueur, données réelles, zones propres jamais signalées, résidus en bleu, durée 6 s / recharge 10 s configurables, refus signalé (son + message) | Testé | `DirtScan_…`, `ScanTimer_…` (EditMode), `Scan_KeyR_HighlightsRealDirt_ThenRechargesAndRefusesMeanwhile`, `Scan_OnSpotlessRug_FlagsNothing_AndIsBlockedInPause`, captures `22b`–`24` |
| 1.5 Sons distincts : contact propre à chaque outil (5), prise en main, scan, refus, palier, paiement ; anti-empilement ; fondu anti-clic au rangement ; catégories respectées | Testé (lecture) | `ToolSound_Plays_ThenStopsOnToolChangeAndPause`, tests ci-dessus ; inventaire et licences : [AUDIO.md](AUDIO.md) |
| Qualité d'écoute des nouveaux sons | À vérifier à la main | Les tests vérifient qu'ils jouent et s'arrêtent, pas leur qualité |
| Ressenti du balancement et de l'alignement en jeu réel | À vérifier à la main | Jugement humain |

## Roadmap V2 — Jalon 2 (outils et atelier)

| Élément | Statut | Preuve |
|---|---|---|
| 2.1 Canon à mousse (touche 6) : sélectionnable, consomme le détergent, éventail large, mousse diluée, réglages dans sa définition, sons propres | Testé | `FoamCannon_SpraysAWideFan_AndConsumesDetergent`, `SoapOperation_ResiduePerFoam_…`, capture `30` |
| 2.2 Mousse : dépôt précis (grille, surface visée uniquement), répartition selon l'outil (disque / éventail, cercle de visée elliptique), action au repos sur les taches réactives au savon, retombée progressive en eau, montée au brossage, évacuation par rinçage et extraction, bulles animées dans le shader (aucun objet ajouté) | Testé | `Foam_LeftInPlace_…` (EditMode et PlayMode), `Brushing_WhipsFoam_…`, `Ellipse_…`, captures `30`, `31` |
| 2.3 Balai (touche 7) et brosse de détail (touche 8) : comportements distincts (débris secs / sol dur ; graisse et taches incrustées), sons et effets au contact, boutique (déblocage, largeur) | Testé | `Sweep_…` (2), `Brushing_…StubbornStains`, `DetailBrush_IsLockedUntilBought_ThenSelectable`, `DetailBrush_BeatsTheRegularBrushOnGrease`, `Upgrades_UnlockTool_AndWidenIt`, captures `33`, `37` |
| 2.4 Aire de lavage : ruissellement réel du tapis (eau sale, mousse, débris) du côté traité, nettoyage avec les outils, état « propre » mesuré, HUD et comptoir informés, jamais bloquant (bordures du tapis suivant recontaminées), traces de pas | Testé | `RinsingNearAnEdge_…`, `Broom_ClearsTheWashArea_…`, `DirtyWashArea_NeverBlocksANewOrder_…`, `CleanWashArea_DoesNotTouchTheNextRug`, `WalkingThroughMud_LeavesFootprintsOnTheFloor`, `WorkSurface_…`, `Runoff_…`, `Contamination_…`, captures `32`, `34`, `38` |
| 2.5 Robot aspirateur : modèle et base, déplacement autonome sur grille d'occupation (murs, meubles, objets au sol) avec chemin A*, jamais dans une case bloquée, arrêt devant le joueur, nettoyage réel du sol, voyant d'état, sons, E pour démarrer / arrêter, retour en charge et reprise, état sauvegardé | Testé | `Pathfinder_…` (2), `Robot_…` (EditMode 2, PlayMode 3), `RobotAndWashArea_AreSavedAndRestored`, captures `35`, `36`, `36b` |
| Ancien effet global « sol mouillé » remplacé par les vraies données de l'aire de lavage | Testé | `WashArea_GetsWet_WhenRinsing_ThenDries` (remplace `FloorGetsWet_WhenRinsing_ThenDries`) |
| Ressenti des nouveaux outils, lisibilité des empreintes, crédibilité du robot sur une longue session | À vérifier à la main | Jugement humain |

## Roadmap V2 — Jalon 3 (contenu et RugDex)

| Élément | Statut | Preuve |
|---|---|---|
| 3.1 Tapis de soie (luxe) : motif et matériau distincts (reflet soyeux, franges claires), fragilité 0,85 (usure au brossage à sec et par produit agressif, protégée par la mousse et l'humidité), usure visible et mesurée, avertissement HUD, critère « Fibres abîmées » dans l'évaluation, commande la plus exigeante (950 €, réputation 8, ★★★★), avertissement sur le tableau | Testé | `Content_LuxuryRug_IsDistinct_…`, `FragileRug_DryBrushingWears_…` (EditMode et PlayMode), `Products_ReallyChangeBrushingDwellAndWear`, `Evaluator_WearObjective_CountsOnlyWhenRequired`, `LuxuryOrder_IsLockedByReputation_ThenLoadsTheSilkRug`, `PalaceDelivery_WithDamagedFibers_…`, captures `40`, `41` |
| 3.2 Salissures : amas de poussière, boue sèche, poils d'animaux, peluches et fibres ajoutés aux types existants (poussière légère, petits débris, terre, boue humide, boissons, graisse, taches incrustées), chacun avec réponses propres, couleur, adhérence configurable (type + couche), sensibilité aux produits, aspect distinct au rendu (voile, grains, brins) ; intégrés à la grille, à la propreté, à la note et au scan | Testé | `NewDirtTypes_AreStoredInTheRightChannel_AndBehaveDifferently`, `EveryDirtType_HasAColorAndAName`, `Adhesion_…`, `Library_CanOverride…`, `DryDirt_HasADistinctLook_OnlyForLooseTypes`, `NewDirtTypes_ArePresentInRealOrders`, captures `42`, `42b` (gros plan : brins de poils, grains d'amas), `43` |
| 3.3 Produits spécialisés (4) : cibles, multiplicateurs de brossage et d'action au repos, résidus, agressivité, débit, flacon, prix, stock maximal ; boutique (onglet Produits), inventaire, sélection T, consommation réelle, épuisement → détergent standard, sauvegarde ; description générée depuis les valeurs | Testé | `ProductTable_…`, `Products_ReallyChange…`, `Soap_RecordsTheProduct_…`, `ProductDescription_IsGeneratedFromRealValues`, `ProductStock_…` (2), `ShopRules_ProductPurchase_…`, `Products_BoughtSelectedWithT_…`, `Product_RunsOut_…`, `ShopProductsTab_…`, capture `44` |
| 3.4 RugDex : catalogue de données, découverte au chargement, fiche (visuel, catégorie, dimensions, fragilité, salissures, commandes, livraisons, meilleure note, description), entrées inconnues avec condition réelle, complétion, accès depuis le menu principal et la pause, sauvegarde ; aucune rareté ni bonus inventés | Testé | `RugDex_…` (EditMode 2, PlayMode 2), `Products_AndRugDex_AreSavedAndRestored`, `OldSave_LoadsWithEmptyProductsAndRugDex`, captures `45`, `46` |
| 4 nouvelles commandes (pension canine, atelier de couture, bureau de chantier, grand salon) ; tableau des commandes en liste défilante avec barre | Testé | `OrderBoard_ListsAllOrders_InAScrollableList`, captures `47`, `48` |
| Sauvegarde : champs `products`, `activeProduct`, `rugDex` (ajout compatible) ; surfaces de l'atelier en format v2 (adhérence, produit) | Testé | `OldSave_…`, `SurfaceState_KeepsAdhesionAndProduct`, `Products_AndRugDex_AreSavedAndRestored` |
| Équilibrage (prix des produits, difficulté du tapis de soie), lisibilité des poils et peluches en jeu, ressenti de l'usure | À vérifier à la main | Jugement humain |

## Roadmap V2 — Jalon 4 (manette)

| Élément | Statut | Preuve |
|---|---|---|
| Déplacement (stick gauche, course au clic du stick qui s'arrête à l'immobilité), caméra au stick droit (degrés par seconde, indépendante du framerate), zone morte radiale et courbe de réponse | Testé | `StickResponse_…`, `Gamepad_MovesLooksSelectsCleansAndTakesAnOrder`, `SprintToggle_AndDeviceSwitch_BackToMouse` |
| Outils : RB / LB pour changer (outils verrouillés sautés), RT pour utiliser ; interaction A, scan Y, produit X, inspection Vue, pause Menu, retour B | Testé | `EveryGameplayAction_IsReachableWithAGamepad`, `DefaultGamepadBindings_AreUnique`, `Gamepad_MovesLooks…` (aspirateur à la gâchette, comptoir au bouton A) |
| Menus sans souris : fenêtre ouverte = focus (premier élément, ou « Fermer » en dernier recours), sélection restaurée à la fermeture d'une fenêtre ouverte par-dessus, sélection impossible sur une fenêtre recouverte, sélection vidée en jeu, défilement automatique des listes, focus ambre bien visible, onglets LB / RB (boutique, paramètres), fiche du RugDex qui suit le focus | Testé | `Gamepad_PauseSettingsTabsAndBack_RestoreTheSelection`, `OrderBoard_ScrollsToTheSelectedRow`, `MainMenu_IsFullyUsableWithAGamepad`, captures `45`, `51`–`54` |
| Commandes, résultats, RugDex, boutique, pause, paramètres, menu principal (nouvelle partie, RugDex) au complet à la manette | Testé | mêmes tests (le menu principal lance réellement l'atelier au bouton A) |
| Indications dynamiques selon le périphérique actif (HUD, invites « [A] », boutons « Fermer (B) », fenêtre des touches), noms Xbox ou PlayStation, retour au clavier/souris au mouvement de la souris ; curseur masqué dans les menus à la manette | Testé | `GamepadLabels_FollowTheControllerFamily`, `DeviceScheme_IsDetectedFromTheDevice`, `Cursor_IsHiddenInMenus_…`, `SprintToggle_AndDeviceSwitch_BackToMouse`, capture `50` |
| Paramètres manette (vitesse caméra 30–360 °/s, zone morte 2–50 %, vibrations 0–100 %), sauvegardés (champs ajoutés, anciennes sauvegardes compatibles) | Testé | `GamepadSettings_AreClamped_AndOldSettingsGetDefaults`, `Gamepad_PauseSettings…` (valeurs réellement appliquées) |
| Reconfiguration des boutons de manette (colonne dédiée ; sticks, Menu et B fixes ; doublons signalés), index des touches clavier inchangés | Testé | `RebindableRows_…`, `KeyboardBindingIndices_AreUnchanged_…`, `Labels_FollowARebind`, `Gamepad_RebindsScan_ToTheDpad_AndThePromptFollows` |
| Vibrations : grondement propre à chaque outil (réglé dans sa définition), impulsions (paliers, objectif, scan, outil verrouillé), coupées hors jeu, hors manette, vibrations désactivées, perte du focus | Testé | `Rumble_OnlyWithAGamepad_…`, `Rumble_FollowsTheToolInUse_AndStopsInMenusOrWhenDisabled` |
| Correctif : les sélecteurs ‹ › d'un onglet de paramètres jamais affiché branchent leurs boutons (révélé par le test existant des paramètres) | Testé | `Settings_ChangedInUI_AreAppliedSavedAndRestored` |
| Essai sur une vraie manette (Xbox, DualShock / DualSense), ressenti des sticks et des vibrations, lisibilité à distance d'un téléviseur | À vérifier à la main | Les tests utilisent une manette virtuelle de l'Input System ; aucun matériel n'a été branché |

## Roadmap V2 — Jalon 5 (multijoueur coopératif et Steamworks)

Architecture, configuration et procédure de test Steam : `MULTIPLAYER.md`. « Testé (2 processus) » = vérifié par l'étape
`multiplayer` (deux exemplaires du jeu compilé, hôte et invité, connexion directe sur la même machine).

| Élément | Statut | Preuve |
|---|---|---|
| 5.1 Choix techniques documentés : NGO 2.13.3 (3.x incompatible avec Unity 6000.5), Unity Transport, Steamworks.NET 2025.164.1, transport Steam Networking Sockets écrit pour NGO 2.x | Fait | `MULTIPLAYER.md` § 1 |
| 5.2 Créer / rejoindre une partie en réseau local, état de connexion, départ, refus (version, partie complète), perte de l'hôte, retour au menu avec la raison, « Se reconnecter » | Testé (2 processus) + PlayMode | `HostSession_…`, `Approval_…`, `DisconnectMessages_…`, étape `multiplayer` |
| 5.2.1 Steam : salon (amis/public, 4 places, données de version), recherche filtrée, membres du salon (distincts des joueurs connectés), invitations (fenêtre Steam, invitation acceptée jeu ouvert ou fermé « +connect_lobby »), lancement par l'hôte, connexion des membres via le transport Steam, salon fermé au départ de l'hôte | **Implémenté, non testé en réel** | Compilé ; sans client Steam, message clair et connexion directe disponible (`HostSession_…`). Procédure à deux comptes dans `MULTIPLAYER.md` § 4 |
| 5.3 Joueurs : silhouette par joueur (position, orientation, tête, outil en main, effets et son d'outil, nom), caméra et entrées strictement locales, apparition par l'hôte après synchronisation, retrait au départ | Testé (2 processus) | « Silhouette de l'hôte visible », « Déplacement de l'hôte vu par l'invité », « Départ de l'invité géré », capture `61` |
| 5.4 Nettoyage : hôte autoritaire, opérations des invités transmises (bornées, invalides ignorées), prédiction et rejeu des opérations non confirmées, cellules modifiées diffusées (rectangles compressés), instantané à chaque tapis et pour un arrivant ; mousse, eau, extraction, aire de lavage et sol passent par le même chemin ; deux joueurs sur la même zone | Testé (2 processus) + PlayMode | `TwoPlayersCleaningTheSameRug_…` (latence simulée : chaque opération appliquée une fois, pas de recul, tapis identiques), « Tapis identique chez l'hôte et l'invité (empreinte) », `Operations_…`, `CellCodec_…`, `ForgedOperationValues_…` |
| 5.5 Commandes : état partagé, demandes des invités, évaluation et paiement uniquement chez l'hôte (une fois, même si la livraison est demandée deux fois), résultats affichés chez tous ; argent et réputation de l'atelier de l'hôte, partagés | Testé (2 processus) | « Récompense versée une seule fois (deux demandes de livraison) », « Résultats de la livraison affichés chez l'invité » |
| 5.6 Atelier : boutique (achats demandés à l'hôte), produits (stock partagé, consommé chez l'hôte), robot (simulé chez l'hôte, demandes des invités), aire de lavage et sol | Testé (2 processus) sauf produits | « Achat demandé à l'hôte puis reçu », « Robot démarré via l'hôte » ; consommation de produit par un invité : non couverte par un test automatique |
| 5.7 Fiabilité : aucune sauvegarde de progression chez un invité, progression de l'hôte sauvegardée, pause sans arrêt du temps partagé, messages réseau tronqués ou trafiqués refusés | Testé | « Progression locale de l'invité intacte », « Progression de l'atelier sauvegardée chez l'hôte », `SharedTime_…`, `ByteBuffer_…` |
| 5.8 Deux machines distinctes via Internet, comptes Steam différents, invitations Steam | **Non testé** | Une seule machine et aucun client Steam lancé dans l'environnement de développement |

## Update v0.1.2 — Jalon 1 (correctifs fondamentaux)

Audit, décisions et conventions : `AUDIT_V012.md`. Résultats d'exécution : `VALIDATION.md` § 13.

| Élément | Statut | Preuve |
|---|---|---|
| 1.1 HUD à l'arrivée en ligne : état de chargement (contrôles coupés, aucune valeur provisoire) jusqu'à réception des surfaces et de la carrière validée, puis valeurs personnelles ; atelier local de l'invité non restauré ; état complet redemandé par l'invité (arrivée pendant le chargement de l'hôte) | Testé (2 processus, 8 passages consécutifs) | « État de chargement affiché à l'arrivée », « HUD initialisé dès l'arrivée avec la progression personnelle » |
| 1.2 Progression personnelle : carrière par joueur (argent, réputation, achats, produits), bornée par l'hôte à l'arrivée, achats/consommations/récompenses appliqués par l'hôte, sauvegardée chez chaque joueur ; annonce répétée ignorée ; atelier de l'hôte jamais écrit chez l'invité | Testé (2 processus) + EditMode | `CareerRulesTests` (5), « Achat … payé avec l'argent de l'invité (hôte inchangé) », « Récompense … dans la carrière de l'invité », « Annonce de carrière forgée … ignorée », « Carrière identique chez l'hôte et l'invité » |
| 1.3 Jet depuis la buse (lance, détergent, mousse), suit la cible en bougeant et en tournant ; idem chez les autres joueurs | Testé (PlayMode) | `SprayEffects_LeaveFromTheToolMuzzle_…`, `RemoteAvatar_…` (jet distant à < 1 cm de la buse) |
| 1.4 Alignement : cause racine (rotation seule, outil plus court que la portée) corrigée par un manche télescopique + orientation ; tête à < 5 cm du point visé, 6 poses × 3 hauteurs d'yeux × 5 outils de contact | Testé (PlayMode + EditMode) | `ContactTools_HeadLandsOnTheAimedPoint_…`, `ToolReachTests` (4), captures `64_tool_reach_*` |
| 1.5 Performance mousse/eau | **Non traité** (jalon 2) | Mesures de référence consignées (`VALIDATION.md` § 13) |
| 1.6 Salissures limitées à l'aire de lavage : sol de la salle inerte (saleté initiale, outils, pas, réseau, ancienne sauvegarde) | Testé (PlayMode) | `RoomFloor_IsOutsideTheCleaningZone_…`, `WalkingThroughMud_…`, `OldSave_WithADirtyRoomFloor_…` |
| 1.6 → 3.6 (avancé) Robot limité à l'aire de lavage, tapis posé = obstacle, ancienne position hors zone → base | Testé (PlayMode + 2 processus) | `Robot_StartsFromItsDock_CleansTheWashArea_AvoidsTheRug_…`, robot démarré par l'invité ; achat du robot : jalon 4 |
| 1.7 Silhouettes : mêmes modèles détaillés, posés sur le point visé reconstitué localement (aucune donnée réseau en plus) | Testé (PlayMode, silhouette hors réseau) | `RemoteAvatar_HoldsTheSameDetailedTool_…`, captures `61`, `62`, `63` |
| 1.8 Personnages : silhouette en tenue de travail, casquette, gants, bras à deux segments sur la poignée, marche ; gants aussi en vue subjective | Fait — **placeholders procéduraux** | Captures `61`–`64` ; vérifié : main droite sur la poignée (< 3 cm) |
| 1.9 Plus de tapis d'entraînement (démarrage, fin/abandon de commande, menu pause, RugDex, sauvegarde) | Testé (PlayMode) | `Workshop_StartsWithoutRug_…`, `RugDex_FromPauseMenu_…` |
| 1.10 Aire de lavage : propreté ≥ 25 % pour une commande (refus expliqué), recontamination au-delà de 40 % de saleté (pose + continue, plafonnée), indicateur permanent, défaut sûr | Testé (EditMode + PlayMode) | `ZoneConvention_…`, `Contamination_…`, `BorderContamination_…`, `DirtyWashArea_BelowTwentyFivePercentClean_…`, `RugOnADirtyWashArea_…`, `WashAreaAbove/BelowFortyPercent…` |
| Phase 6 Tapis terminé sous 1 % : reliquat retiré, 100 %, réussite affichée, note calculée à 100 %, jamais recontaminé ensuite | Testé (EditMode + PlayMode) | `RugFinish_…`, `Rug_IsFinishedBelowOnePercentRemaining_…` |

## Update v0.1.2 — Jalon 2 (performance et fiabilité)

Diagnostic et mesures : `VALIDATION.md` § 14.

| Élément | Statut | Preuve |
|---|---|---|
| 1.5 Diagnostic instrumenté (chronomètres par système, désactivés par défaut : opérations des outils, simulation lente, encodage et envoi des textures, ruissellement, réseau ; temps GPU, collectes du GC, particules vivantes) ; scénarios mousse et session prolongée ajoutés au benchmark du jeu compilé | Fait | `PerfStats`, `BuildBenchmark` (scénarios « Canon à mousse », « Mousse au repos », « Rinçage de la mousse », « Session prolongée ») |
| 1.5 Cause des à-coups : toute la grille vieillie d'un coup toutes les 0,2 s quand la mousse la couvre (5,5 ms d'un tenant dans l'éditeur), puis réencodée et, en ligne, renvoyée aux invités | Corrigé : simulation répartie sur les images (même résultat, 0,47 ms par image en moyenne), cellules sans mousse ni eau sautées | `SpreadAging_…` (3 tests EditMode) |
| 1.5 Réseau : la mousse qui retombe n'est plus diffusée 15 fois par seconde ; chaque poste la reproduit, l'hôte corrige toutes les 2 s (réglable) | Testé (PlayMode) : 434,6 Ko → 29,0 Ko pour 3 s de mousse au repos, états identiques après correction | `FoamAtRest_IsReproducedByTheGuest_…`, `SlowReplication_…` |
| 1.5 Budget des effets (densité, durée de vie, taille maximale à l'écran contre le surdessin d'un jet devant la caméra, distance d'affichage des effets des autres joueurs) — purement visuel | Fait + testé | `EffectBudget_…`, `CleaningTuning` (section « Effets visuels ») |
| 1.5 Pooling : aucun objet créé pendant le nettoyage (effets créés à la génération, pool d'éclats, lignes d'interface réutilisées) | Vérifié | Revue du code ; session prolongée : 893 → 893 objets |
| Session prolongée : objets, systèmes de particules, textures, maillages, matériaux et mémoire stables | Testé (PlayMode + jeu compilé, mesure de référence) | `LongCleaningSession_…` ; benchmark : 895 → 895 objets, +0,6 Mo |
| Validation multijoueur avec mousse (deux processus) | **Préparée, non exécutée** | Phase « mousse au repos » ajoutée au scénario ; non lancée pendant que le joueur utilisait le PC |
| Mesure du jeu compilé après optimisation | **Non exécutée** | Benchmark arrêté à la demande du joueur (PC utilisé pour jouer) ; mesures de référence avant optimisation consignées |
| Correctif : version de protocole réellement appliquée à l'asset réseau (restée « rugwash-coop-1 » au jalon 1) | Corrigé | Le constructeur impose `NetworkContent.CurrentProtocol` (`rugwash-coop-3`) |

## Update v0.1.2 — Jalon 3 (ordinateur et économie)

Décisions : `AUDIT_V012.md` § 5. Résultats d'exécution : `VALIDATION.md` § 15.

| Élément | Statut | Preuve |
|---|---|---|
| 2.1 Ordinateur de l'atelier : exactement deux icônes cliquables (Missions / Commandes, Boutique) avec résumé tiré des données du jeu ; seul accès aux commandes et aux achats (boutique retirée du meuble à outils) ; souris, clavier, manette, B / Échap | Testé (PlayMode) | `Computer_HasExactlyTwoIcons_…`, `Gamepad_MovesLooksSelectsCleansAndTakesAnOrder` (A → ordinateur → A sur Missions → A accepte) |
| 2.1 Boutique : liste défilante, outils à acheter en tête ; tableau trié (en cours, missions, contrats, verrouillées, terminées) | Testé (PlayMode) | `NewGame_StartsWith…` (outils en tête, achat de l'aspirateur), `FullOrder_…`, `LockedOrders_…` |
| 2.2 Missions générées sans fin : trois offres (deux accessibles, une à viser), remplacement à la livraison, paliers de réputation, difficulté et récompense croissantes, aucune mission impossible, pas de doublon immédiat | Testé (EditMode + PlayMode) | `EconomyV012Tests` (12 tests missions), `Missions_AreOfferedFirst_ReplacedOnDelivery_…` |
| 2.2 Déterminisme et sauvegarde : graine, numéro, offres ; mêmes missions après un redémarrage | Testé (EditMode + PlayMode) | `Board_RestoresSavedOffers_Identically`, `Missions_…_AndIdenticalAfterRestart` |
| 2.2 En ligne : offres de l'hôte régénérées chez l'invité depuis leurs identifiants | **Préparé, non exécuté** (deux processus) | Vérification « Missions du tableau identiques chez l'hôte et l'invité » ajoutée à `-Step multiplayer` ; non lancée pendant que le joueur utilisait le PC |
| 2.3 Actualités du menu principal : annonces locales versionnées, chargement asynchrone, données invalides tolérées, présentées comme locales | Testé (EditMode + PlayMode) | `News_…` (3 tests), `MainMenu_ShowsLocalNews_WithoutBlocking` |
| 2.4 Kit de départ : brosse, détergent, seau d'eau (nouvel outil) ; autres outils à acheter ; migration v1 → v2 qui conserve les outils d'une partie commencée | Testé (EditMode + PlayMode) | `NewGame_StartsWithBrushDetergentAndBucketOnly`, `Migration_V1…` (2), `VersionZero_IsMigrated` |
| 3.1 Détergent consommable : réserve de 4 L, bidon de 40 L = stock consommé progressivement (remplissage partiel), achat, prix, stock, sauvegarde | Testé (EditMode + PlayMode) | `Detergent_ReserveRefillsFromStock_…`, `DetergentCan_RespectsFundsAndStorage` |
| 3.1 Niveau de liquide visible (shader, valeur envoyée seulement quand elle change), réserve vide bloquée avec raison et son de refus | Testé (PlayMode) | `EmptyReserve_PlaysTheDenySound_AndTheLiquidLevelFollowsTheReserve` |
| 3.1 En ligne : achat de bidon et remplissage validés par l'hôte sur la carrière du joueur | Implémenté (format de carrière v2 testé en EditMode, stock de détergent compris) ; **deux processus non exécutés** | `CareerCodec_RoundTrips_…`, RPC `DetergentCanRpc` / `DetergentRefillRpc` |
| Banc d'essai du jeu compilé adapté au kit de départ | **Non exécuté** (ouvre une fenêtre du jeu) | `BuildBenchmark` : vérifie le kit puis débloque les outils mesurés |

## Update v0.1.2 — Jalon 4 (outils et salissures)

Décisions : `AUDIT_V012.md` § 6. Résultats d'exécution : `VALIDATION.md` § 16.

| Élément | Statut | Preuve |
|---|---|---|
| 3.2 Poils, cheveux, débris : méthodes d'enlèvement propres (existantes) + accessoire anti-poils de l'aspirateur (×2, mesuré) | Testé (EditMode + PlayMode) | `FiberAccessory_PicksUpHairFaster_AndOnlyHair`, `FiberAccessory_DoublesHairPickup_OfTheVacuum` |
| 3.3 Raclette : eau, mousse et boue poussées dans le sens réel du geste jusqu'au caniveau, sans effet sur un tapis, sans création de matière ; gouttes bornées ; opération transmise en ligne | Testé (EditMode + PlayMode, captures) | `Squeegee_Transport_…`, `Gather_…`, `Squeegee_PushesWaterAlongTheStroke_IntoTheDrain_…` |
| 3.4 Monobrosse : modèle, disque animé, son, effets définis (détache, mousse, pas d'eau, usure à sec), efficacité selon les fibres, marche ralentie, achat | Testé (EditMode + PlayMode, capture) | `RotaryScrubber_IsWeakOnDryFibers_…`, `Rotary_ScrubsWetFibersFar_…` |
| 3.5 Bottes : transfert de boue réduit à 30 %, effet affiché à la boutique | Testé (EditMode + PlayMode) | `Upgrades_BootsAccessoryAndRobot_…`, `MuddySoles_MarkTheRug_AndBootsReduceTheTransfer` |
| 3.6 Robot aspirateur à acheter (caché avant l'achat, base explicative, sauvegarde, migration, état de l'hôte en ligne) | Testé (EditMode + PlayMode ; en ligne non exécuté) | `Robot_MustBeBought_ThenRuns_…`, `Migration_V2WithProgress_KeepsTheRobot_…` |
| 4.4 Traces boueuses sur le tapis (en plus de l'aire), une par pas, réduites par les bottes, jamais sur le sol de la salle | Testé (PlayMode) | `MuddySoles_MarkTheRug_…` |
| Sauvegarde : achats inconnus du catalogue conservés | Implémenté | `SaveManager.Capture` |
| Tests Unity (batch) | Testé | EditMode 198 / 198, PlayMode 112 / 112 (`VALIDATION.md` § 16) |
| En ligne : raclette d'un invité, robot de l'hôte, protocole `rugwash-coop-5` | **Implémenté, deux processus non exécutés** | Opération « ramassage » transmise et bornée (`SqueegeeAndFiberBonus_AreTransmissible_AndClamped`) |
| Jeu compilé (benchmark) | **Non exécuté** (ouvre une fenêtre du jeu) | — |

## Update v0.1.2 — Jalon 5 (base et personnage)

Décisions : `AUDIT_V012.md` § 7. Résultats d'exécution : `VALIDATION.md` § 17.

| Élément | Statut | Preuve |
|---|---|---|
| 5.1 Machines fonctionnelles : plaque de dépoussiérage, souffleur de séchage, rampe de pré-mouillage — prix, réputation requise, cachées avant l'achat, E pour les actionner, effet mesuré et jamais un nettoyage complet, un cycle par commande, sauvegarde (achats) | Testé (EditMode + PlayMode, capture) | `Blower_RemovesMoistureOnly`, `DustPlate_ShakesDryDustOnly`, `PreWet_WetsAndSoaks_ButRemovesNothing`, `Machines_NeedPurchaseAndReputation_…`, capture `71_machines` |
| 5.1 En ligne : machines de l'atelier de l'hôte, activation demandée par un invité, effet reproduit et corrigé | **Implémenté, deux processus non exécutés** | `NetworkWorkshop` (état d'atelier, `RequestMachine`) |
| 5.2 Paliers de décor : cagibi → atelier rangé → professionnel → loft → penthouse vue sur Los Angeles ; prix, réputation, ordre, sauvegarde, migration v3 → v4 ; objets et éclairage réellement modifiés | Testé (EditMode + PlayMode, captures) | `WorkshopTier_IsTheHighestOwned`, `Migration_V3WithProgress_KeepsTheTidyWorkshop`, `Decor_StartsInTheCagibi_…`, captures `70_tier_0..4`, `70b_tier_4_view` |
| 5.2 Appartenance en ligne : décor et machines de l'hôte (session), achats dans la carrière de chaque joueur | Implémenté (règle d'achat testée) | `Purchases_RespectRequiredReputation` (même règle chez l'hôte), `MULTIPLAYER.md` |
| 5.3 Personnalisation : menu « Personnage », aperçu 3D qui tourne, tenue / casquette (ou aucune) / gants, limites affichées, sauvegarde par joueur, gants à la première personne | Testé (EditMode + PlayMode, capture) | `CharacterLook_RoundTrips_…`, `Character_IsCustomizedInTheMenu_…`, capture `72_character` |
| 5.3 Réplication de l'apparence aux autres joueurs | **Implémenté, deux processus non exécutés** | `NetworkPlayerAvatar` (valeur `look` possédée par chaque joueur) |
| 5.4 Ragdoll : chute réglable (élan, durée), contrôles coupés, caméra au sol, relevé garanti, pas de passage sous le sol ; ragdoll de la silhouette (bornes de vitesse, durée maximale, retour à la pose) ; glissade en courant sur la mousse | Testé (EditMode + PlayMode) | `Slip_OnlyWhenSprintingFastOnThickFoam`, `Knockdown_LocksControls_…`, `RemoteAvatar_Ragdolls_ThenReturnsToItsPose` |
| 5.4 Ragdoll en ligne (état « à terre » + élan, aucun os) ; coups de batte | **Implémenté, deux processus non exécutés** ; batte : jalon 6 | `NetworkPlayerAvatar` (`knockedDown`, `knockImpulse`) |
| 5.5 Musique : boucles générées (menu, atelier), lecteur unique persistant, fondu enchaîné, atténuée en pause, curseur « Musique », sous les sons de jeu | Testé (EditMode + PlayMode) | `Music_StaysUnderGameplaySounds`, `Music_IsASinglePlayer_…` |
| Tests Unity (batch) | Testé | EditMode 207 / 207, PlayMode 119 / 119 (`VALIDATION.md` § 17) |
| Jeu compilé (build, benchmark, deux processus) | **Non exécuté** (ouvre des fenêtres du jeu) | — |

## Update v0.1.2 — Jalon 6 (farces entre coéquipiers)

Décisions : `AUDIT_V012.md` § 8. Résultats d'exécution : `VALIDATION.md` § 18.

| Élément | Statut | Preuve |
|---|---|---|
| 4.1 Pipi sur un tapis : tache jaune humide, vraie salissure nettoyable (détergent, brosse, rinçage) | Testé (EditMode + PlayMode, captures) | `PrankStain_…`, `Pee_OnTheRug_…`, captures `74_pee_stream`, `74b_prank_stains` |
| 4.1 Pipi sur un joueur : gouttes à l'écran, partielles, durée limitée, fondu, effacées (fin de partie) ; validées par l'hôte | Testé (EditMode + PlayMode, capture) | `ScreenSplash_…`, `Splash_OnTheLocalScreen_…`, `Host_AppliesAGuestsPrank…`, capture `75_screen_splash` |
| 4.2 Fusil à pompe en **version jouet** (embout orange, faux sang de théâtre stylisé, jamais sur un joueur), coût par tir, recharge | Testé (PlayMode, captures) | `Blaster_SplattersStylizedFakeBlood_…`, captures `73b_blaster`, `73c_blaster_shot` |
| 4.3 Batte : petite tache de faux sang sur un tapis ; chute comique d'un joueur (ragdoll du jalon 5), relevé rapide, protection, jamais un joueur à terre | Testé (EditMode + PlayMode, capture) | `QuickRecover_…`, `Bat_KnocksTheLocalPlayerDown_…`, `Authority_…`, capture `73_bat` |
| 4.5 Prix élevés (≥ 2 × l'outil le plus cher), réputation, réglages centralisés (`PrankTuning`), recharges, délai global, plafond par tapis, tapis terminé épargné, progression des autres intacte | Testé (EditMode + PlayMode) | `Tuning_Protects_…`, `CheckUse_…`, `Range_Cap_…`, `Gadgets_MustBeBought_…`, `Authority_…` |
| Interface : onglet Farces, gadget / recharge / coût au HUD, touche G reconfigurable, manette (croix haut, RT, A) | Testé (PlayMode, capture) | `Gadgets_MustBeBought_…`, capture `76_shop_pranks` |
| Réseau : demande de l'invité validée par l'hôte (carrière, position chez l'hôte), effets diffusés, gouttes / chute au seul joueur visé, protocole `rugwash-coop-7` | Testé (PlayMode + deux processus au jalon 7) | `Host_AppliesAGuestsPrankToTheHostPlayer_…`, étape `multiplayer` (`VALIDATION.md` § 19) |
| Tests Unity (batch) | Testé | EditMode 216 / 216, PlayMode 127 / 127 (`VALIDATION.md` § 18) |
| Jeu compilé (build, benchmark, deux processus) | Exécuté au jalon 7 | `VALIDATION.md` § 19 |

## Update v0.1.2 — Jalon 7 (intégration et tests finaux)

| Élément | Statut | Preuve |
|---|---|---|
| Régression complète solo (EditMode, PlayMode) | Testé | EditMode 223 / 223, PlayMode 127 / 127 |
| Sauvegardes anciennes et nouvelles : v0 à v3 migrées sans perte, nouvelle partie = kit seul, version future refusée, v4 relue à l'identique | Testé (EditMode) | `IntegrationV012Tests` |
| Build Windows | Testé | étape `build` |
| Performance du jeu compilé (1920×1080, Ultra) : ~200 FPS de moyenne, 1 % bas 85-100 FPS, mémoire et nombre d'objets stables sur 3 commandes | Testé | étape `benchmark` (15 / 15), `VALIDATION.md` § 19 |
| Coopératif à deux processus étendu : jalons 1 à 4 + atelier de l'hôte (décor, machines), apparence, farces (pipi de l'invité validé par l'hôte, coup de batte de l'hôte, chute vue des deux côtés, relevé) | Testé | étape `multiplayer` (43 / 43) |
| Économie (récompense unique, carrière de l'invité, achats, coûts des farces) | Testé (deux processus + PlayMode) | idem |
| Reconnexion | Bouton « Se reconnecter » (jalon 5 V2), messages de déconnexion testés en EditMode ; **pas de scénario automatique de reconnexion entre deux processus** | `DisconnectMessages_SayWhatHappened` |
| Plusieurs machines, Internet, Steam, plus de deux clients | **Non testé** (une seule machine, pas de client Steam lancé) | — |

## Mission de corrections et d'optimisation (bugfix)

Rapport complet : `RAPPORT_CORRECTIFS.md`. Exécutions : `VALIDATION.md` § 20.

| Élément | Statut | Preuve |
|---|---|---|
| 3 Économie de session = sauvegarde de l'hôte ; sauvegarde de l'invité jamais modifiée (préférences seulement) ; achats de l'invité payés par l'atelier ; récompense unique | Testé (PlayMode + deux processus) | `Guest_NeverWritesTheHostsSessionEconomy_…`, étape `multiplayer` |
| 2.4 Crash détecté en 10 s, identité de joueur, ancienne connexion fermée au retour ; correctif d'Unity Transport (hôte sourd après un crash) | Testé (EditMode + deux processus : crash, reconnexion, hôte tué) | `BugfixSessionTests`, étape `multiplayer` |
| 2.3 Version du jeu et du protocole dans la connexion et le message, salon Steam vérifié avant de rejoindre | Testé (EditMode) ; Steam non exécuté | `BugfixSessionTests` |
| 2.1 Plus d'allongement télescopique à la première personne (tête jamais sous la surface) ; silhouettes distantes télescopiques | Testé (PlayMode) | `ContactTools_LocalHeadNeverSinksBelowTheSurface_…`, `RemoteAvatar_…` |
| 2.2 Jet depuis la buse en bougeant (ordre d'exécution) ; jet distant rattaché à la buse | Testé (PlayMode) | `SprayEffects_…`, `RemoteAvatar_…` |
| 2.5 / 2.9 Fusil : KO cartoon de 5 s, tête qui éclate, faux sang stylisé, relevé automatique, garde-fous ; élans exagérés bornés | Testé (EditMode + PlayMode) | `Blaster_…`, `BlasterKnockout_…`, `RemoteAvatar_Ragdolls_…` |
| 2.6 Barre d'outils : outils possédés seulement | Testé (PlayMode) | `Toolbar_…`, `WorkshopV2…` |
| 2.7 Boutique : onglets Outils et Améliorations | Testé (PlayMode) | `ContentV3…`, `Shop_…` |
| 2.8 Raclette : savon, résidus et débris ramassés, retrait réel, eau seulement sur un tapis | Testé (EditMode + PlayMode) | `Gather_…`, `Squeegee_…` |
| 2.10 Mousse : diagnostic au Profiler Unity ; à-coups en multijoueur corrigés (correction lente par tranches, rendu ligne par ligne, un bord recontaminé à la fois) | Testé (mesures avant/après, PlayMode, EditMode, deux processus) | `VALIDATION.md` § 20.4, `SlowCorrection_…`, `DirtyRows_…`, `DirtyWashArea_RecontaminatesOneBorderAtATime_…` |
| 2.11 Paliers de rendu réels (Bas, Moyen, Élevé ; Ultra inchangé) ; protocole de benchmark reproductible | Mesuré sur une seule machine (charge GPU ×4 et 2 fils CPU en approximation) | `QualityTierTests`, `VALIDATION.md` § 20.5 |
| PC moins puissants réels, deux machines, Internet, Steam | **Non testé** | — |

## Hors périmètre de la démo (documenté)

| Élément | Statut |
|---|---|
| Sons enregistrés définitifs | À faire (substituts synthétisés `tmp_*`) |
| Modèles 3D réalisés par un artiste | À faire (modèles procéduraux) |
| Génération de nouvelles commandes après les 10 du catalogue | À faire |
| Mode coopératif | Développé (jalon 5) — Steam à valider en réel, voir `MULTIPLAYER.md` |
| Migration d'hôte, reconnexion automatique | Non prévues (l'hôte qui part ferme la partie ; reconnexion par bouton) |
| Icônes de boutons de manette (images) | À faire : les indications sont textuelles (« A », « RT », « Croix »…) |
| Sélection directe d'un outil à la manette | Non prévue : RB / LB font défiler les 8 outils |

## Limites connues (phase 1)

- **Sons temporaires** : tous les sons sont synthétisés (`Audio/Generated/tmp_*.wav`). Ils sont fonctionnels
  mais ne sont pas des sons définitifs. Aucun enregistrement réel n'est présent.
- **Commande en cours non sauvegardée** : quitter ou revenir au menu pendant une commande l'abandonne.
- **Résultats affichés dans l'atelier** (fenêtre) : la scène provisoire `Results.unity` a été supprimée
  et retirée du build (choix documenté : pas de transfert d'état entre scènes, retour immédiat au travail).
- **Pas de sauvegarde cloud** ni de protection contre la modification manuelle du fichier.
- **Tous les outils sont disponibles dès le départ** : aucun système de déblocage d'outils n'existe, donc rien
  n'est sauvegardé à ce sujet.
- Le catalogue contient 6 commandes non renouvelables : une fois toutes terminées, il ne reste que l'entraînement
  (génération de nouvelles commandes à prévoir).
- **Résolution et mode d'affichage** ne sont pas observables dans l'éditeur : à vérifier dans un build (phase 6).
- **Modèles procéduraux** : outils et mobilier sont générés par code (formes arrondies, révolutions, tubes).
  Ils remplacent les cubes du prototype mais restent stylisés ; des modèles 3D réalisés par un artiste
  amélioreraient encore le rendu.
- **Police de l'interface** : police intégrée de Unity (`LegacyRuntime.ttf`), sans identité graphique propre.
- **Séchage naturel du tapis non simulé** : seule l'extraction réduit l'humidité (le sol de l'aire de lavage, lui, sèche).
- **Extracteur = abstraction** : l'eau aspirée est calculée depuis l'humidité retirée (3 L/m² saturé).
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
10. Menu principal → Nouvelle partie → livrer une commande → Échap → Menu principal → « Continuer » affiche le solde
    → Continuer : solde, réputation et commande terminée retrouvés.
11. Paramètres → « Modifier » sur Interagir → appuyer sur G → fermer ; en jeu, G ouvre le comptoir ; relancer le jeu :
    G est conservé ; « Touches par défaut » rétablit E.

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
