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

## 9. Revalidation après le jalon 2 (outils et atelier)

Exécutions réelles du 11-12/10/2026, même machine. **Attention** : pendant ces mesures, d'autres applications 3D tournaient
sur la machine (jeu et logiciel de discussion ouverts), ce qui fait varier les FPS d'une exécution à l'autre ; la
comparaison fiable est donc faite **coup sur coup** entre le build du jalon 1 et celui du jalon 2.

| Vérification | Résultat |
|---|---|
| Compilation | ✅ 0 erreur, 0 avertissement |
| Tests EditMode | ✅ **112 / 112** (91 + 21 : mousse, balai, brosse de détail, aire de lavage, sauvegarde de l'atelier, chemin A*, robot) |
| Tests PlayMode | ✅ **64 / 64** (50 + 14 ; le test du « sol mouillé » global est remplacé par celui de l'aire de lavage) |
| Build Windows 64 bits | ✅ 120,6 Mo, 0 erreur, 0 avertissement |
| Jeu compilé : 11 contrôles automatiques | ✅ 11 / 11 |

**Performance comparée (1920 × 1080, mêmes conditions, deux tours alternés)** :

| Scénario | Jalon 1 (tour 1 / tour 2) | Jalon 2 (tour 1 / tour 2) |
|---|---|---|
| Atelier au repos | 418,7 / 439,0 FPS | 269,4 / 410,4 FPS |
| Rinçage intensif | 407,8 / 413,2 FPS | 380,4 / 373,2 FPS |
| Savon puis brossage | 427,5 / 438,6 FPS | 382,2 / 392,5 FPS |
| Tour complet | 455,8 / 467,6 FPS | 398,6 / 441,6 FPS |

Coût du jalon 2 : environ 5 à 10 % (sol et aire de lavage rendus par le shader des surfaces nettoyables, robot, simulation
lente). 1 % le plus lent ≥ 200 FPS au second tour. Le premier passage du premier scénario varie fortement d'une exécution à
l'autre (chargement, machine chargée). Une première mesure isolée du jalon 2 à ~138 FPS avait été obtenue machine chargée ;
des builds A/B (sol, aire de lavage et robot masqués tour à tour) puis les mesures alternées ci-dessus ont montré qu'il ne
s'agissait pas d'une régression du jeu. La dernière exécution complète de la chaîne (machine toujours chargée) donne
259 à 269 FPS de moyenne, 1 % le plus lent ≥ 138 FPS.

À vérifier à la main : ressenti des nouveaux outils, lisibilité des empreintes, comportement du robot sur une longue
session, performance sur une machine sans autre application ouverte.

## 10. Revalidation après le jalon 3 (contenu et RugDex)

Exécutions réelles du 12/10/2026, même machine (Discord ouvert pendant les mesures).

| Vérification | Résultat |
|---|---|
| Compilation | ✅ 0 erreur, 0 avertissement |
| Tests EditMode | ✅ **132 / 132** (112 + 20 : salissures, adhérence, produits, stock, usure, RugDex, sauvegarde) |
| Tests PlayMode | ✅ **76 / 76** (64 + 12, dont la galerie de captures `40` à `48`) |
| Build Windows 64 bits | ✅ réussi |
| Jeu compilé : 11 contrôles automatiques | ✅ 11 / 11 |
| Performance | ✅ pas de régression : comparaison coup sur coup avec un build du jalon 2 (tableau ci-dessous). Un passage isolé avait dépassé le délai de 5 min pendant que la machine était chargée ; il ne s'est reproduit sur aucun des sept passages suivants (cause non identifiée, à surveiller). |

**Performance comparée (1920 × 1080, mêmes conditions, deux tours alternés J2 / J3 / J2 / J3, 12/10/2026)** :

| Scénario | Jalon 2 (tour 1 / tour 2) | Jalon 3 (tour 1 / tour 2) |
|---|---|---|
| Atelier au repos | 447,5 / 476,0 FPS | 465,0 / 463,3 FPS |
| Rinçage intensif | 435,1 / 429,3 FPS | 431,6 / 411,7 FPS |
| Savon puis brossage | 416,1 / 427,5 FPS | 452,3 / 441,8 FPS |
| Tour complet | 487,4 / 489,6 FPS | 501,8 / 468,5 FPS |

1 % le plus lent ≥ 239 FPS (jalon 2) et ≥ 245 FPS (jalon 3) ; 11 / 11 contrôles à chaque passage. Les écarts restent dans
le bruit de mesure. Les passages plus lents relevés plus tôt (186–239 FPS) l'avaient été machine chargée.

Nouveau rendu à surveiller : 4ᵉ texture de contrôle (`ControlD`, teinte et aspect des salissures sèches) et quatre
échantillons supplémentaires dans le shader des surfaces (brins et grains).

## 11. Revalidation après le jalon 4 (manette)

Exécutions réelles du 12/10/2026, même machine.

| Vérification | Résultat |
|---|---|
| Compilation | ✅ 0 erreur, 0 avertissement |
| Tests EditMode | ✅ **143 / 143** (132 + 11 : sticks, liaisons, libellés, détection du périphérique, curseur, vibrations, paramètres) |
| Tests PlayMode | ✅ **84 / 84** (76 + 8 : parcours complet à la manette virtuelle, pause et paramètres, reconfiguration, vibrations, course et retour à la souris, défilement de liste, menu principal, captures `50` à `54`) |
| Build Windows 64 bits | ✅ 124 Mo, 0 erreur, 0 avertissement |
| Jeu compilé : 11 contrôles automatiques | ✅ 11 / 11 |
| Performance (1920 × 1080) | ✅ 513,7 / 463,9 / 482,3 / 533,1 FPS (atelier au repos, rinçage intensif, savon puis brossage, tour complet), 1 % le plus lent ≥ 283 FPS — dans la plage des mesures coup sur coup du jalon 3 |

Correctif révélé par les tests : les sélecteurs ‹ › d'un onglet des paramètres jamais affiché ne réagissaient pas
(branchement fait dans `Awake`, qui ne s'exécute qu'à la première activation) ; ils se branchent désormais dès l'abonnement.

Non vérifié : une vraie manette. Tous les tests manette utilisent un périphérique virtuel de l'Input System (mêmes
actions, même module d'interface) ; le ressenti des sticks, des vibrations et les noms des boutons sur une manette
PlayStation réelle restent à essayer à la main.

## 12. Revalidation après le jalon 5 (multijoueur coopératif et Steamworks)

Exécutions réelles du 12/10/2026, même machine.

| Vérification | Résultat |
|---|---|
| Compilation | ✅ 0 erreur, 0 avertissement (NGO 2.13.3, Unity Transport 2.6.0, Steamworks.NET 2025.164.1) |
| Tests EditMode | ✅ **153 / 153** (143 + 10 : tampons, codec des opérations et des cellules, valeurs trafiquées bornées, accusés, admission, adresses, messages de déconnexion, invitation au lancement, temps partagé) |
| Tests PlayMode | ✅ **87 / 87** (84 + 3 : deux tapis convergents avec latence simulée et nettoyage simultané, vraie session hôte Netcode, captures `60`–`61`) |
| Build Windows 64 bits | ✅ 127 Mo, 0 erreur, 0 avertissement |
| Jeu compilé, solo : 11 contrôles | ✅ 11 / 11 — 488 / 448 / 469 / 543 FPS, 1 % ≥ 292 FPS (pas de régression) |
| **Jeu compilé, deux processus (hôte + invité, connexion directe locale)** | ✅ **24 / 24** vérifications (détail ci-dessous) |

**Scénario coopératif à deux processus** (`RugWash-CI.ps1 -Step multiplayer`) — chaque exemplaire écrit un rapport, le
script les compare :

| Hôte | Invité |
|---|---|
| Partie hébergée, atelier réseau prêt | Connecté et synchronisé ; tapis en réplique (l'hôte fait autorité) |
| L'invité a rejoint l'atelier (2 joueurs) | Silhouette de l'hôte visible ; déplacement de l'hôte vu (≈ 1,3 m) |
| Commande acceptée par l'hôte | Commande reçue (état partagé) |
| Nettoyage de l'invité appliqué sur le tapis de l'hôte (597 opérations, 0 refusée) | Nettoyage visible immédiatement chez lui (prédiction), 0 opération en attente à la fin |
| Livraison demandée **deux fois** par l'invité : validée une fois, **un seul versement** (168 €) | Résultats affichés (note S) |
| Achat demandé par l'invité exécuté par l'hôte | Achat reçu (« Réservoir d'eau 60 L ») |
| Robot démarré à la demande de l'invité | État du robot reçu |
| Départ de l'invité géré (hôte toujours en partie) | Retour au menu ; **progression locale intacte** (rien de l'hôte sauvegardé) |
| Progression de l'atelier sauvegardée chez l'hôte | — |
| **Tapis identique chez les deux** : même empreinte des valeurs quantifiées | |

Débit mesuré : invité → hôte 37,6 Ko pour 597 opérations (≈ 63 octets par opération) ; hôte → invité 249 Ko de cellules sur
toute la partie, instantanés et tapis entièrement réécrit compris. 119 FPS des deux côtés (plafond du test à 120).

**Non testé** (environnement : une machine, client Steam non lancé, un seul compte) : salons, recherche, invitations et
transport Steam ; deux machines distinctes ; Internet. Procédure à suivre : `MULTIPLAYER.md` § 4.
