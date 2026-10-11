# Rapport de validation — RugWash Simulator (phase 6)

Date : 11/10/2026 · Unity 6000.5.7f1 · URP 17.5 · Windows 64 bits
Machine de mesure : NVIDIA GeForce RTX 5060, Intel Core i7-8700K, 32 Go, Direct3D 11.

Toutes les valeurs ci-dessous proviennent d'exécutions réelles (`Tools/RugWash-CI.ps1`) ; rien n'est estimé.

## 1. Synthèse

| Vérification | Résultat |
|---|---|
| Compilation (scripts du jeu, de l'éditeur et des tests) | ✅ 0 erreur, 0 avertissement |
| Tests EditMode (règles métier, sauvegarde, maillages, entrées) | ✅ **84 / 84 réussis** |
| Tests PlayMode (vraies scènes, vrais boutons, rechargements) | ✅ **36 / 36 réussis** |
| Build Windows 64 bits | ✅ réussi, 119,3 Mo, 0 erreur, 0 avertissement |
| Vérification automatique du jeu compilé (11 contrôles) | ✅ **11 / 11 réussis** |
| Objectif 60 FPS en 1080p sur la machine de mesure | ✅ atteint (moyenne ≥ 410 FPS, 1 % le plus lent ≥ 178 FPS) |
| Tests échoués | **aucun** |

## 2. Performance mesurée (build, 1920 × 1080 plein écran fenêtré, qualité Ultra, vSync désactivé)

| Scénario (durée) | FPS moyen | 1 % le plus lent | Frame moyenne | Pire frame |
|---|---|---|---|---|
| Atelier au repos, vue d'ensemble (5 s) | 440 | 178 | 2,3 ms | 7,1 ms |
| Rinçage intensif balayant le tapis (10 s) | 410 | 192 | 2,4 ms | **28,9 ms** |
| Savon puis brossage (8 s) | 449 | 230 | 2,2 ms | 6,7 ms |
| Tour complet de l'atelier (8 s) | 486 | 222 | 2,1 ms | 12,2 ms |

Une première mesure, avant l'ajout de la résolution au rapport, donnait 445 à 566 FPS de moyenne : les écarts entre
exécutions sont normaux.

**Limites de la mesure** :
- Une seule machine, dont la carte graphique est récente. **La cible « PC de milieu de gamme » n'est pas vérifiée
  sur du matériel plus modeste** ; la marge mesurée (environ 7× l'objectif) laisse toutefois de la latitude.
- **Une frame isolée de 28,9 ms** pendant le rinçage (probablement la première utilisation d'un effet ou un ramassage
  mémoire) : invisible à l'œil à cette fréquence, mais non expliquée précisément.
- Sessions longues non mesurées : l'absence de dégradation progressive est vérifiée par conception (pools bornés,
  aucune texture recréée, tests `Effects_StayBounded`, `CleanFeedbackPool_StaysBounded`), pas par une mesure d'une heure.

## 3. Contrôles du jeu compilé (`-rugwash-benchmark`)

| # | Contrôle | Résultat |
|---|---|---|
| 1 | Le menu principal se charge | ✅ |
| 2 | « Continuer » est désactivé sans sauvegarde | ✅ |
| 3 | « Nouvelle partie » charge l'atelier, entièrement câblé | ✅ |
| 4 | Une commande est acceptée (« Le tapis du salon ») | ✅ |
| 5 | La livraison est évaluée et payée une fois (note S, 168 €) | ✅ |
| 6 | La sauvegarde est écrite sur le disque | ✅ |
| 7 | L'écran de résultats s'affiche | ✅ |
| 8 | La résolution 1280 × 720 est réellement appliquée | ✅ |
| 9 | Le mode fenêtré est réellement appliqué | ✅ |
| 10 | Le retour en 1920 × 1080 est appliqué | ✅ |
| 11 | Après retour au menu, « Continuer » est disponible | ✅ |

## 4. Couverture des tests d'acceptation du cahier des charges

**A. Nettoyage** — ✅ testé : saleté initiale > 0, seule la zone visée change, aspirateur sur la poussière, savon → mousse,
brosse sur saletés compatibles, rinçage → mousse retirée, extracteur → humidité, pas de progression infinie,
propreté affichée = données (`CleaningRulesTests`, `ToolRulesPhase2Tests`, tests PlayMode des outils).

**B. Commandes** — ✅ testé : démarrage, évaluation, récompense unique, pas de blocage, commande suivante
(`GameLoopRulesTests`, `GameLoopPlayModeTests`, contrôles 4 à 7 du build).

**C. Économie** — ✅ testé : achat possible, achat impossible refusé sans débit, effet réel, récompense ajoutée une fois.

**D. Sauvegarde** — ✅ testé : progression restaurée, nouvelle installation, fichier corrompu, achats et commandes
persistants, aucune récompense dupliquée (`SaveSystemTests`, `SaveAndMenuPlayModeTests`, contrôles 6 et 11).

**E. Interface et contrôles** — ✅ en grande partie :
- pause, sélection et utilisation des outils, boutons qui agissent, menus non bloqués : testés ;
- verrouillage du curseur : **logique testée** ; verrouillage réel par le système = vérification manuelle ;
- lisibilité du HUD : vérifiée sur captures en 1600 × 900 ; **autres résolutions = vérification manuelle**.

**F. Robustesse** — ✅ : aucune erreur de script dans les scénarios nominaux (`LogAssert.NoUnexpectedReceived`),
références manquantes signalées explicitement (`ValidateReferences`, `IsConfigured`), scènes et transitions vérifiées
dans le build, effets bornés.

## 5. Non exécuté automatiquement (à vérifier à la main)

| Point | Pourquoi |
|---|---|
| Ressenti des contrôles (souris, ZQSD, animations en main) | Jugement humain |
| Écoute des sons (qualité, équilibre) | Les tests vérifient qu'ils jouent et s'arrêtent, pas leur qualité |
| Reconfiguration interactive d'une touche (appui réel) | Saisie clavier réelle ; la conservation est testée |
| HUD et menus en 1280 × 720 et 2560 × 1440 | Captures faites en 1600 × 900 uniquement |
| Performance sur une machine moins puissante | Une seule machine disponible |

Scénarios détaillés : [PRODUCTION.md](PRODUCTION.md), section « Scénarios de vérification manuelle ».

## 6. Anomalies connues (non bloquantes)

- Au démarrage du build, le journal contient une fois :
  `Shader Hidden/Universal Render Pipeline/DBufferClear shader is not supported on this GPU`.
  Il s'agit d'un shader interne d'URP pour les décalques, fonctionnalité que le jeu n'utilise pas. Aucun effet visible.
- Le dossier `RugWash Simulator_BurstDebugInformation_DoNotShip` produit par le build ne doit pas être distribué.

## 7. Verdict

Aucune erreur bloquante connue. La démo est **prête à être présentée comme démo en développement** sur PC Windows,
avec les réserves documentées : sons temporaires, modèles procéduraux, 6 commandes non renouvelables, performance
mesurée sur une seule machine. Le jeu est solo ; aucun mode multijoueur n'est développé.

## 8. Revalidation après le jalon 1 de la roadmap V2 (corrections et game feel)

Exécutions réelles du 11/10/2026, même machine, même chaîne (`Tools/RugWash-CI.ps1`).

| Vérification | Résultat |
|---|---|
| Compilation | ✅ 0 erreur, 0 avertissement |
| Tests EditMode | ✅ **91 / 91** (84 existants + 7 : scan, minuterie, paliers, réglage du balancement) |
| Tests PlayMode | ✅ **50 / 50** (36 existants + 14 : alignement, balancement, retours, scan, sons, captures) |
| Build Windows 64 bits | ✅ 119,5 Mo, 0 erreur, 0 avertissement |
| Jeu compilé : 11 contrôles automatiques | ✅ 11 / 11 |
| Performance 1920 × 1080 (4 scénarios) | ✅ moyenne 444,6 à 496,1 FPS ; 1 % le plus lent ≥ 264 FPS ; pire image 10,8 ms |

Alignement des outils mesuré (5 outils × 7 poses : face, côtés, dos, distances 0,9 à 2 m) : écart sans correction
de 7° (détergent) à 35° (brosse), toujours sous la cible ; **≤ 0,05° après correction**, sans tremblement à l'arrêt
(< 0,6° entre deux images). Le point nettoyé reste sous le réticule à 0,002 près (coordonnées d'écran) en marchant.

Un test existant a été ajusté : `ToolSound_Plays_ThenStopsOnToolChangeAndPause` attend désormais 0,1 s (au lieu d'une
image) avant de vérifier l'arrêt du son de l'outil rangé, car ce son s'éteint maintenant par un fondu de 0,06 s pour
éviter un clic audible. L'arrêt immédiat en pause est inchangé et toujours vérifié.

À vérifier à la main (non automatisable) : ressenti du balancement et de l'alignement, qualité d'écoute des nouveaux
sons, lisibilité du scan selon les goûts du joueur.
