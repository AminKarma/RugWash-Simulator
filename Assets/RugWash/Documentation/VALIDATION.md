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
| Tests PlayMode | ✅ **88 / 88** (84 + 4 : deux tapis convergents avec latence simulée et nettoyage simultané, vraie session hôte Netcode, Steam initialisé dès le menu et invitation acceptée en partie solo (sauvegarde puis menu), captures `60`–`61`) |
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

## 13. Update v0.1.2 — jalon 1 (correctifs fondamentaux)

Exécutions réelles du 13/10/2026, même machine. Décisions et conventions : `AUDIT_V012.md`.

| Vérification | Résultat |
|---|---|
| Compilation | ✅ 0 erreur, 0 avertissement |
| Tests EditMode | ✅ **165 / 165** (153 + 12 : carrière personnelle et son format réseau, portée télescopique et pose des outils, conventions et seuils de l'aire de lavage, recontamination plafonnée, fin du tapis) |
| Tests PlayMode | ✅ **98 / 98** (nouvelle suite `V012PlayModeTests` : 9 tests ; tests existants adaptés à l'absence de tapis d'entraînement, au sol inerte et au robot sur l'aire de lavage) |
| Build Windows 64 bits | ✅ 129 Mo, 0 erreur, 0 avertissement |
| Jeu compilé, solo : 11 contrôles | ✅ 11 / 11 |
| **Jeu compilé, deux processus (hôte + invité)** | ✅ **32 / 32** vérifications, **8 passages consécutifs réussis** après correction (voir ci-dessous) |

**Performance (référence avant le jalon 2)**, build 1080p, vSync désactivé, trois mesures du 13/10 : repos 413–436 FPS,
rinçage 373–394, savon + brosse 388–398, tour de l'atelier 472–482 ; 1 % le plus lent ≥ 217 FPS. C'est 8 à 15 % de moins
que les mesures du 12/10 (488 / 448 / 469 / 543) ; aucune des modifications du jalon 1 n'ajoute de coût par image
notable en solo et la baisse touche aussi la vue au repos, mais elle **n'a pas été attribuée** (pas de mesure
comparative avec l'ancien build sur la même session). Le profilage du jalon 2 la confirmera ou l'écartera.

**Scénario à deux processus** : carrières de départ différentes (hôte 300 €, invité 1 500 € et réputation 2) :

| Hôte | Invité |
|---|---|
| Carrière de l'invité reçue et bornée (1 500 €, réputation 2) ; la sienne reste à 300 € | État de chargement affiché ; HUD prêt avec **1 500 €, réputation 2** (sa progression) |
| Livraison payée une fois à l'hôte (168 €) **et** dans la carrière de l'invité (1 668 €) | Récompense reçue (1 668 €) |
| Achat de l'invité payé avec l'argent de l'invité (1 518 €), achats et argent de l'hôte inchangés | Achat reçu ; annonce forgée (999 999 €, tous les achats) **ignorée** |
| Robot démarré à la demande de l'invité (débris sur l'aire) | État du robot reçu |
| Sauvegarde de l'hôte : 468 €, aucun achat de l'invité | Sauvegarde de l'invité : 1 518 €, 1 achat, 1 commande, **aucun état de l'atelier de l'hôte** |
| Carrière de l'invité identique chez l'hôte et chez l'invité ; tapis identique (même empreinte) | |

**Défaut trouvé et corrigé pendant la validation** : 3 passages sur 14 restaient bloqués en « synchronisation » chez
l'invité. Cause : un invité connecté pendant que l'hôte chargeait encore l'atelier recevait les instantanés des surfaces
avant d'avoir enregistré leur réception ; ils étaient perdus et jamais renvoyés. Correction : l'invité redemande l'état
complet dès qu'il peut le recevoir (et toutes les 3 s s'il manque quelque chose) ; l'hôte ne répond qu'à un invité
synchronisé et n'accepte qu'une annonce de carrière par invité. Après correction : 8 / 8 passages réussis. Autre défaut
corrigé : l'objet réseau apparaissait avant l'application de la sauvegarde de l'invité (carrière annoncée vide).

**Vérifications visuelles** (captures `Logs/ci/screens`) : `64_tool_reach_*` (tête de l'outil sur le réticule),
`61`–`63` (silhouette distante : outil détaillé, gant sur la poignée, tête sur le tapis, jet depuis la buse).

**Non testé** : Steam en réel, deux machines distinctes (inchangé) ; perte d'un message réseau autre qu'au démarrage
(seul le cas de l'arrivée pendant le chargement de l'hôte a été reproduit et corrigé).

## 14. Update v0.1.2 — jalon 2 (performance de la mousse et de l'eau)

Exécutions réelles du 13/10/2026. **Contexte de mesure** : le PC servait en même temps à jouer (League of Legends,
Discord, Medal) ; les FPS absolus du jeu compilé sont donc bruités et le benchmark a été arrêté à la demande du joueur.
L'attribution par système (chronomètres internes) reste valable.

**Référence avant optimisation** (jeu compilé, 1080p, deux passages) — coût moyen par image :

| Scénario | Opérations outils | Simulation lente | Encodage textures | Envoi GPU | GPU | Particules (max) |
|---|---|---|---|---|---|---|
| Canon à mousse balayant le tapis | 0,42–0,46 ms | 0,06 ms | 0,08 ms | < 0,01 ms | 2,3–2,6 ms | 84 |
| Mousse au repos (tapis couvert) | — | 0,05 ms | 0,03 ms | < 0,01 ms | 2,3 ms | 66 |
| Rinçage de la mousse | 0,21–0,26 ms | 0,07 ms | 0,05 ms | < 0,01 ms | 2,5–2,6 ms | 271 |

Aucune collecte du GC pendant la mousse au repos (1 par scénario de 10 s au plus) ; particules bornées (≤ 274 vivantes).
**Diagnostic** : en moyenne, le coût est faible ; le problème est la **forme** de la charge. Avec de la mousse partout,
toute la grille était vieillie d'un coup toutes les 0,2 s (≈ 5,5 ms d'un tenant mesurés dans l'éditeur, davantage
sur un processeur modeste), puis entièrement réencodée — d'où des à-coups réguliers « au moment de la mousse » ; en
ligne, ces mêmes changements partaient en plus vers chaque invité 5 fois par seconde. Les particules n'utilisent ni
collision ni instanciation ; un jet émis juste devant la caméra pouvait toutefois couvrir une grande partie de l'écran.

**Après optimisation** (mesures exécutées) :

| Vérification | Résultat |
|---|---|
| Tests EditMode | ✅ **170 / 170** (165 + 5 : simulation répartie équivalente et sans rattrapage, coût par image, zone réseau lente, budget d'effets) |
| Tests PlayMode | ✅ **100 / 100** (98 + 2 : mousse au repos en ligne, session prolongée) |
| Simulation lente, tapis 205 × 141 couvert de mousse (éditeur) | passage complet **5,5 ms d'un coup** toutes les 0,2 s → réparti **0,47 ms par image** en moyenne, 0,69 ms au 95ᵉ centile |
| Réseau, 3 s de mousse au repos | **434,6 Ko → 29,0 Ko** (correction toutes les 2 s) ; après correction, tapis identiques à la quantification près |
| Session prolongée (4 tapis : mousse, rinçage, extraction, aspiration, rechargements) | objets 893 → 893, systèmes de particules 24 → 24, textures, maillages et matériaux inchangés, mémoire gérée +0,0 Mo, pic de 266 particules pour un plafond de 3 180 |
| Jeu compilé, session prolongée (référence) | 3 commandes enchaînées : 895 → 895 objets, textures, maillages et matériaux inchangés, +0,6 Mo |

**Non exécuté** : benchmark du jeu compilé après optimisation et scénario à deux processus avec la nouvelle phase
« mousse au repos » (ils ouvrent des fenêtres du jeu ; à lancer quand le PC est libre :
`Tools/RugWash-CI.ps1 -Step build`, puis `-Step benchmark` et `-Step multiplayer`).

## 15. Update v0.1.2 — jalon 3 (ordinateur et économie)

Exécutions réelles du 14/10/2026, même machine, en mode batch uniquement (le PC servait à jouer : aucune fenêtre du jeu
n'a été ouverte). Décisions : `AUDIT_V012.md` § 5.

| Vérification | Résultat |
|---|---|
| Compilation (scripts, contenu, scènes) | ✅ 0 erreur, 0 avertissement |
| Tests EditMode | ✅ **189 / 189** (170 + 19 `EconomyV012Tests` : paliers, clés et génération déterministe des missions, aucune mission impossible, offres sans doublon immédiat, restauration à l'identique, baisse de réputation, actualités (lecture, données invalides, fichier livré), migration v1 → v2, sauvegarde v2 assainie, bidons de détergent ; format de carrière vérifié avec le stock de détergent ; 4 tests existants adaptés : sauvegardes v1 désormais migrées, canon à mousse à acheter, outils conservés par la migration v0) |
| Tests PlayMode | ✅ **106 / 106** (100 + 6 `EconomyV012PlayModeTests` : kit de départ et achat d'un outil, détergent (remplissage depuis le stock, partiel, refus, achat d'un bidon, sauvegarde, redémarrage), niveau de liquide et son de refus, ordinateur à deux icônes seul accès, missions remplacées et identiques après redémarrage, actualités du menu ; tests existants adaptés : ordinateur à la manette, tableau et boutique triés, 9 outils, profil vierge pour « Nouvelle partie ») |
| Build Windows, jeu compilé (solo, deux processus) | **Non exécutés** : ils ouvrent des fenêtres du jeu |

**Défauts trouvés et corrigés pendant la validation**

- Tableau des missions : après la livraison d'une mission accessible, la règle prévue pour une baisse de réputation
  retirait aussi l'offre « à viser » (deux missions générées au lieu d'une). Règle corrigée : au plus une offre
  verrouillée ; les autres sont remplacées (test `Board_ReplacesDeliveredMission_…`).
- Niveau de liquide : à fréquence d'image élevée, le lissage s'arrêtait dès que le pas d'une image passait sous le seuil,
  et le niveau restait figé loin de la réserve réelle (0,30 affiché pour 0,25). L'écart est maintenant mesuré à la cible.
- Boutique : titre long (« Lance de rinçage haute pression ») passant par-dessus sa description ; l'amélioration
  « Bidon de détergent 6 L » (réserve) renommée « Réserve de détergent 6 L » pour ne pas être confondue avec le bidon de 40 L.
- Affiche « Les 5 étapes » : elle commençait par « Aspirer », impossible avec le kit de départ ; devenue
  « Savonner, brosser, rincer, sécher, livrer (PC) ».

**Vérifications visuelles** (captures `Logs/ci/screens`) : `39_computer` (deux icônes distinctes, résumés réels),
`37_shop` (outils à acheter en tête, liste défilante), `09_order_board` (missions en tête, contrats, offres verrouillées),
`12_main_menu` (actualités locales), `18_back_wall` (affiche).

**Non exécuté** (à lancer quand le PC est libre : `Tools/RugWash-CI.ps1 -Step build`, puis `-Step benchmark` et
`-Step multiplayer`) : scénario à deux processus avec la nouvelle vérification « missions du tableau identiques chez
l'hôte et l'invité » et les demandes de détergent de l'invité (achat de bidon, remplissage) ; benchmark du jeu compilé
adapté au kit de départ. Steam en réel : inchangé (non testé).

## 16. Update v0.1.2 — jalon 4 (outils et salissures)

Exécutions réelles du 14/10/2026, en mode batch uniquement (aucune fenêtre du jeu). L'éditeur Unity ayant été ouvert
pendant une partie du travail, la compilation a d'abord été vérifiée hors éditeur (SDK .NET sur des copies des projets
générés par Unity : 0 erreur), puis toute la chaîne a été relancée une fois l'éditeur fermé. Décisions : `AUDIT_V012.md` § 6.

| Vérification | Résultat |
|---|---|
| Compilation, contenu, scènes (batch Unity) | ✅ 0 erreur, 0 avertissement |
| Tests EditMode | ✅ **198 / 198** (189 + 9 `ToolsV012Tests` : ramassage de la raclette, transport sans création ni perte de matière, surplus des cellules saturées, accessoire anti-poils, bottes / accessoire / robot dans les statistiques d'équipement, monobrosse selon les fibres, migration v2 → v3, format réseau, touches 1-9 et 0 ; tests existants adaptés : sauvegardes migrées en v3) |
| Tests PlayMode | ✅ **112 / 112** (106 + 6 `ToolsV012PlayModeTests` : raclette jusqu'au caniveau et sans effet sur un tapis, monobrosse, bottes et traces boueuses sur le tapis, robot à acheter, accessoire mesuré, captures ; tests existants adaptés : onze outils) |
| Build Windows, jeu compilé (solo, deux processus) | **Non exécutés** : ils ouvrent des fenêtres du jeu |

**Défauts trouvés et corrigés pendant la validation**

- Raclette : en concentrant l'eau devant la lame, des cellules dépassaient la saturation et la grille écrêtait le surplus
  (34 % de l'eau disparaissait au premier geste). Le dépôt mesure désormais ce qui déborde et la raclette le reporte plus
  loin devant elle (vague), jusqu'au caniveau ou à la bordure.
- Raclette : l'animation inclinait le modèle de 8° ; sur un manche télescopique de 2 m, la lame s'enfonçait de 16 cm dans
  le sol et devenait invisible (constaté sur capture, mesuré par un relevé de position). Animation limitée à un va-et-vient
  le long du manche ; un test vérifie que la lame reste posée sur l'aire.
- HUD : lignes d'état de la raclette et de la monobrosse trop longues (elles recouvraient le nom de l'outil) ; raccourcies.
- Test du jalon 3 rendu robuste : le HUD se rafraîchit toutes les 0,1 s, le test attend désormais un rafraîchissement.

**Vérifications visuelles** (captures `Logs/ci/screens`) : `65_squeegee_push` (vague d'eau boueuse devant la lame),
`65b_squeegee_front` (lame posée sur l'aire), `66_squeegee_drain` (eau poussée au caniveau), `67_rotary` (moteur, jupe,
disque rotatif à repères sur un tapis mouillé).

**Non exécuté** (à lancer quand le PC est libre : `Tools/RugWash-CI.ps1 -Step build`, puis `-Step benchmark` et
`-Step multiplayer`) : scénario à deux processus (raclette d'un invité, robot acheté ou non chez l'hôte, protocole
`rugwash-coop-5`) et benchmark du jeu compilé. Steam en réel : inchangé (non testé).

## 17. Update v0.1.2 — jalon 5 (base et personnage)

Exécutions réelles du 14/10/2026, en mode batch uniquement (aucune fenêtre du jeu). Compilation d'abord vérifiée hors
éditeur (SDK .NET), puis chaîne complète `compile`, `bootstrap`, `scene`, `test`, `playmode` relancée sur le code final.
Décisions : `AUDIT_V012.md` § 7.

| Vérification | Résultat |
|---|---|
| Compilation, contenu, scènes (batch Unity) | ✅ 0 erreur |
| Tests EditMode | ✅ **207 / 207** (198 + 9 `BaseV012Tests` : souffleur (humidité seulement), plaque (poussière sèche seulement), rampe (humidifie et fait tremper sans rien retirer), réputation requise à l'achat (solo et hôte), palier de décor, migration v3 → v4, format réseau de l'apparence, règle de glissade, gain de la musique ; test existant adapté : migration v0 avec l'atelier rangé) |
| Tests PlayMode | ✅ **119 / 119** (112 + 7 `BaseV012PlayModeTests` : musique (lecteur unique, fondu, pause, volume), machines (achat, réputation, effets mesurés, un cycle par commande), paliers (cagibi au départ, ordre, penthouse, sauvegarde, onglet Atelier), personnalisation (menu, aperçu, sauvegarde, gants à la première personne), chute du joueur local, ragdoll d'une silhouette, captures ; tests existants adaptés : onglets de la boutique, bouton « Personnage » du menu à la manette) |
| Build Windows, jeu compilé (solo, deux processus) | **Non exécutés** : ils ouvrent des fenêtres du jeu |

**Défauts trouvés et corrigés pendant la validation**

- Penthouse : la vue sur Los Angeles n'apparaissait pas — la vitre posée devant utilisait le matériau `M_Glass` de
  l'atelier, opaque (constaté sur capture). Vitre retirée : la vue est un panneau non éclairé derrière les montants.
- Cagibi : les « taches d'humidité » étaient des boîtes opaques arrondies, lues comme des panneaux sombres (capture).
  Remplacées par une texture générée (contour irrégulier découpé par l'alpha, auréole, coulure), puis éclaircie : la
  première version, trop sombre, ressemblait à des trous dans le mur.
- Réseau : l'état « à terre » n'est plus écrit si la silhouette n'est plus sur le réseau (relevé forcé pendant un départ).
- Tests : comparaison de couleurs avec tolérance (les couleurs relues depuis les matériaux diffèrent au dix-millième).

**Vérifications visuelles** (captures `Logs/ci/screens`) : `70_tier_0` (cartons, fenêtres condamnées, ampoule nue,
taches), `70_tier_1` à `70_tier_3` (étagère, enseigne, coin salon et suspensions), `70_tier_4` et `70b_tier_4_view`
(baies panoramiques : skyline de Los Angeles au crépuscule, palmiers, tours éclairées), `71_machines` (plaque, souffleur
en marche, rampe au plafond), `72_character` (fenêtre « Personnage », aperçu 3D, choix et limites affichées).

**Non exécuté** (à lancer quand le PC est libre : `Tools/RugWash-CI.ps1 -Step build`, puis `-Step benchmark` et
`-Step multiplayer`) : scénario à deux processus (machines et décor de l'hôte vus par un invité, activation demandée par
un invité, apparence et chute répliquées, protocole `rugwash-coop-6`) et benchmark du jeu compilé. Le scénario automatique
à deux processus ne couvre pas encore les éléments du jalon 5. Steam en réel : inchangé (non testé).

## 18. Update v0.1.2 — jalon 6 (farces entre coéquipiers)

Exécutions réelles des 14 et 15/10/2026, en mode batch uniquement (aucune fenêtre du jeu). Compilation vérifiée hors
éditeur (SDK .NET), puis chaîne `compile`, `bootstrap`, `scene`, `test`, `playmode` sur le code final.
Décisions : `AUDIT_V012.md` § 8.

| Vérification | Résultat |
|---|---|
| Compilation, contenu, scènes (batch Unity) | ✅ 0 erreur |
| Tests EditMode | ✅ **216 / 216** (207 + 9 `PrankV012Tests` : salissures de farce nettoyables, tache plafonnée par cellule, conditions d'usage, portée et plafond par tapis, motif des éclaboussures, visée d'une silhouette, gouttes à l'écran, réglages et protections, relevé rapide, onglet et emplacements des gadgets) |
| Tests PlayMode | ✅ **127 / 127** (119 + 8 `PrankV012PlayModeTests` : achat et sélection, pipi sur le tapis puis nettoyage, fusil-jouet (coût, recharge, jamais sur un joueur), autorité et garde-fous, chute et relevé rapide, gouttes à l'écran, farce d'un invité appliquée à l'hôte dans une vraie session locale, captures ; test existant adapté : onglets de la boutique) |
| Build Windows, jeu compilé (solo, deux processus) | **Non exécutés** : ils ouvrent des fenêtres du jeu |

**Défauts trouvés et corrigés pendant la validation** (captures)

- Gouttes à l'écran trop denses et trop opaques (la vue était couverte) : moins de gouttes, plus petites, centre dégagé,
  intensité 55 %, voile plus léger.
- Bouffée du fusil-jouet émise dans toutes les directions, en grosses boules devant la caméra : petite, brève, dans l'axe
  du tir.
- Jet de pipi trop épais : gouttes affinées.
- Descriptions de l'onglet Farces trop longues pour leurs lignes : raccourcies.
- Test : sur un tapis neuf (propreté 0 %), une farce ne peut pas faire baisser le pourcentage ; le test mesure la saleté
  réellement ajoutée.

**Vérifications visuelles** (captures `Logs/ci/screens`) : `73_bat` (batte en main), `73b_blaster` et `73c_blaster_shot`
(fusil-jouet bleu et blanc à embout orange, éclaboussures rouges stylisées), `74_pee_stream` (jet en arc, auréole jaune),
`74b_prank_stains` (taches sur le tapis), `75_screen_splash` (gouttes surtout vers les bords), `76_shop_pranks` (onglet
Farces).

**Non exécuté** (à lancer quand le PC est libre : `Tools/RugWash-CI.ps1 -Step build`, puis `-Step benchmark` et
`-Step multiplayer`) : farces entre deux processus (demande d'un invité, gouttes et chute chez un invité, effets vus par
les autres, protocole `rugwash-coop-7`) ; le scénario automatique à deux processus ne couvre pas encore les farces.
Steam en réel : inchangé (non testé).

## 19. Update v0.1.2 — jalon 7 (intégration et tests finaux)

Exécutions réelles du 15/10/2026 sur le code final : chaîne `compile`, `bootstrap`, `scene`, `test`, `playmode`, puis
`build`, `benchmark` (jeu compilé, plein écran) et `multiplayer` (deux exemplaires du jeu compilé, hôte et invité,
connexion directe sur la même machine).

| Vérification | Résultat |
|---|---|
| Compilation, contenu, scènes | ✅ 0 erreur |
| Tests EditMode | ✅ **223 / 223** (216 + 7 `IntegrationV012Tests` : sauvegardes v0, v1, v2, v3 migrées sans perte, nouvelle partie, version future refusée, v4 relue à l'identique) |
| Tests PlayMode | ✅ **127 / 127** |
| Build Windows | ✅ |
| Benchmark du jeu compilé (RTX 5060, i7-8700K, 1920×1080, Ultra) | ✅ **15 / 15** contrôles ; 192 à 205 FPS de moyenne selon le scénario, 1 % bas 85 à 100 FPS, 0 à 5 passages du ramasse-miettes ; session de 3 commandes : objets 1239 → 1239, particules 36 → 36, mémoire gérée +0,5 Mo |
| Coopératif à deux processus | ✅ **43 / 43** (20 hôte, 20 invité, 3 comparaisons : tapis identique, carrière de l'invité identique, missions identiques) |

**Scénario multijoueur étendu** (jalon 7) : en plus des vérifications des jalons 1 à 4 (arrivée et HUD, nettoyage
partagé, mousse au repos à 3,8 Ko/s, livraison payée une fois, carrière de l'invité, achats, robot, carrière forgée
ignorée, sauvegardes), il vérifie maintenant :
- l'atelier de l'hôte chez l'invité (palier de décor 2 et souffleur affichés, sans être dans la carrière de l'invité) ;
- l'apparence choisie par l'invité visible sur sa silhouette chez l'hôte ;
- un pipi de l'invité sur l'hôte : demande validée par l'hôte (portée mesurée depuis la silhouette de l'invité),
  gouttes sur l'écran de l'hôte ;
- un coup de batte de l'hôte sur l'invité : chute chez l'invité, ragdoll vu par l'hôte, relevé et contrôles rendus.

**Défaut trouvé et corrigé** : le test PlayMode du pipi visait un point fixe, au bord du tapis le plus petit, et
mesurait la tache avec la saleté déjà présente ; il vise maintenant le centre réel du tapis et compare avec la quantité
ajoutée mesurée par l'autorité (il réussissait seul, échouait selon l'ordre des tests).

### Critères d'acceptation de l'update v0.1.2

| Critère | Statut | Preuve |
|---|---|---|
| HUD correct dès l'arrivée dans une session | ✅ | multijoueur « HUD initialisé dès l'arrivée… » |
| Outils distants avec les modèles détaillés | ✅ | `VALIDATION.md` § 13, PlayMode |
| Progression personnelle jamais remplacée par celle de l'hôte | ✅ | multijoueur (carrières, sauvegarde de l'invité) |
| Le jet d'eau part de l'outil ; extrémité alignée sur la cible | ✅ | § 13 (PlayMode) |
| Mousse sans croissance incontrôlée | ✅ | benchmark (objets et particules stables), § 14 |
| Salissures de sol limitées à la zone autorisée | ✅ | § 13 |
| Plus de tapis d'entraînement | ✅ | § 13, benchmark « kit de départ » |
| Zone ≥ 25 % propre avant une commande ; tapis salis au-delà de 40 % | ✅ | § 13 |
| Tapis fini sous 1 % | ✅ | § 13, benchmark |
| Ordinateur : Missions et Boutique | ✅ | § 15 |
| Missions progressives avec la réputation | ✅ | § 15, multijoueur (missions identiques) |
| Nouvelle partie : brosse, détergent, seau | ✅ | benchmark, `IntegrationV012Tests` |
| Détergent consommé, réserve 4 L remplie par bidons de 40 L | ✅ | § 15 |
| Poils / débris : outils adaptés | ✅ | § 16 |
| Raclette, monobrosse, bottes, robot : fonction réelle | ✅ | § 16 |
| Robot à acheter, limité à la zone | ✅ | § 16, multijoueur |
| Farces : salissures / effets temporaires et nettoyables | ✅ | § 18 |
| Farces très chères, avec recharges, sans détruire la progression | ✅ | § 18 |
| Améliorations de la base sauvegardées selon la bonne propriété | ✅ | § 17, multijoueur (atelier de l'hôte) |
| Personnalisation sauvegardée par joueur et visible en multijoueur | ✅ | § 17, multijoueur (apparence) |
| Ragdoll terminé correctement, joueur jamais bloqué | ✅ | § 17, § 18, multijoueur (relevé) |
| Musique : volume dédié, pas de superposition | ✅ | § 17 |
| Le projet compile ; tests consignés honnêtement | ✅ | ce document |

**Non testé** : Steam en réel (salons, invitations, transport Steam), deux machines distinctes via Internet, plus de deux
joueurs simultanés, reconnexion automatique entre deux processus (la reconnexion est manuelle, par bouton).

## 20. Mission de corrections et d'optimisation (bugfix, sans nouvelle fonctionnalité)

Exécutions réelles du 15 au 16/10/2026, sur une seule machine : **NVIDIA GeForce RTX 5060, Intel Core i7-8700K
(6 cœurs / 12 fils, 3,7 GHz), 32 Go**, Windows 11, Direct3D 11, 1920×1080 plein écran fenêtré. Aucun autre PC n'a été
utilisé : les résultats « PC moins puissant » ci-dessous sont des **approximations par limitation de ressources**, pas des
mesures sur ces machines. Rapport complet (constats, causes, corrections) : `RAPPORT_CORRECTIFS.md`.

### 20.1 Tests automatisés

| Vérification | Résultat |
|---|---|
| Compilation, contenu, scènes | ✅ 0 erreur |
| Tests EditMode | ✅ **229 / 229** |
| Tests PlayMode | ✅ **133 / 133** (suite complète) ; après le dernier correctif (bords de recontamination), 12 / 12 tests ciblés dont le nouveau `DirtyWashArea_RecontaminatesOneBorderAtATime_…` |
| Build Windows | ✅ |
| Coopératif à deux processus (hôte, invité, puis invité « revenu » après un crash) | ✅ **52 / 52** (21 hôte, 20 invité, 6 invité revenu, 5 comparaisons : tapis identique, économie identique après l'achat, missions identiques, économie reçue par l'invité revenu, sauvegarde de l'hôte relue après sa fermeture brutale) |
| Benchmark du jeu compilé (protocole complet, Ultra) | ✅ **15 / 15** contrôles ; 197 à 205 FPS de moyenne selon le scénario, 1 % bas 123 à 145 FPS ; session de 3 commandes : objets 1241 → 1241, particules 36 → 36, mémoire gérée +0,6 Mo |

Nouveaux tests de la mission : `BugfixSessionTests` (données de connexion compatibles, message de versions, ancienne
connexion du même joueur), `QualityTierTests` (un pipeline par niveau, niveaux bas réellement moins coûteux, Ultra
inchangé), `BugfixPlayModeTests` (sauvegarde de l'invité jamais écrite avec l'économie de l'hôte, KO du fusil de 5 s sans
relevé anticipé ni second KO, tête qui éclate puis revient chez la silhouette distante, barre d'outils), `PerformanceRulesTests`
(`DirtyRows_…`), `PerformancePlayModeTests` (`SlowCorrection_WithLotsOfFoam_…`), et les tests existants adaptés aux
comportements demandés (raclette, fusil, tête des outils, onglets de la boutique, barre d'outils).

### 20.2 Sauvegardes et multijoueur (scénario à deux processus)

Rôles : un hôte (300 €, 11 achats dont palier de décor 2, souffleur, batte, pipi), un invité (1 500 €, réputation 2,
8 achats personnels, dont des bottes que l'hôte n'a pas), puis le même invité relancé après un crash (même identité).

| Vérification | Résultat |
|---|---|
| L'invité voit et utilise l'argent, la réputation et l'équipement de l'hôte (pas les siens) | ✅ |
| Achat de l'invité appliqué à l'atelier de l'hôte, payé avec l'argent de l'atelier ; achat impossible refusé sans débit | ✅ |
| Récompense de livraison versée une seule fois, dans l'économie de l'hôte | ✅ |
| Sauvegarde personnelle de l'invité inchangée pendant la session, après son crash et après la fin de la session | ✅ (1 500 €, réputation 2, 8 achats) |
| Crash de l'invité (processus tué) : silhouette retirée par l'hôte | ✅ (9,7 s) |
| Reconnexion du même joueur : un seul personnage, ni argent ni achat en double, économie reçue telle quelle | ✅ |
| Hôte fermé brutalement : l'invité revient au menu avec un message ; sauvegarde de l'hôte relue intacte | ✅ |

**Défaut trouvé pendant ces tests** : après le crash d'un invité, l'hôte n'acceptait plus aucune connexion (le joueur ne
pouvait pas revenir). Cause : bogue d'Unity Transport sous Windows (tampons de réception perdus après des erreurs ICMP) ;
correctif local décrit dans `MULTIPLAYER.md` § 6. Reconnexion vérifiée après correctif.

### 20.3 Protocole de benchmark (reproductible)

Jeu compilé (`-Step build`), plein écran 1920×1080, synchronisation verticale coupée, sauvegarde isolée dans un dossier
temporaire, GPU vérifié inoccupé avant chaque mesure (mesure annulée au-delà de 35 % d'utilisation). Commandes :

```
./Tools/RugWash-CI.ps1 -Step benchmark                                      # protocole complet (contrôles + scénarios)
./Tools/RugWash-CI.ps1 -Step benchmark -BenchmarkMode foam                  # diagnostic de la mousse
./Tools/RugWash-CI.ps1 -Step benchmark -BenchmarkMode graphics              # un réglage graphique désactivé à la fois
./Tools/RugWash-CI.ps1 -Step benchmark -BenchmarkMode quick -Quality 2      # scénarios représentatifs, niveau « Medium »
./Tools/RugWash-CI.ps1 -Step benchmark -BenchmarkMode quick -Quality 2 -RenderScale 2   # charge GPU ×4 (approximation)
./Tools/RugWash-CI.ps1 -Step benchmark -BenchmarkMode quick -Quality 2 -CpuAffinity 3   # 2 fils CPU (approximation)
./Tools/RugWash-CI.ps1 -Step build -Development ; ./Tools/RugWash-CI.ps1 -Step benchmark -Development -BenchmarkMode foam
                                                                            # captures du Profiler Unity + analyse
```

Chaque scénario rapporte : FPS moyen, 1 % bas, temps d'image moyen et 95e centile, à-coups (image > 2 × la médiane),
temps GPU, temps CPU du fil principal et du fil de rendu (FrameTimingManager), temps par système du jeu, particules
vivantes, collectes et croissance du tas géré. Les compteurs de rendu du profileur (lots, SetPass) restent indisponibles
dans ces builds (valeur -1 dans les rapports). Les FPS de cette machine varient de quelques pour cent d'une exécution à
l'autre : les comparaisons sont faites coup sur coup, deux fois pour les niveaux de qualité.

### 20.4 Mousse : diagnostic (2.10)

**Solo, build livré, Ultra** (même vue, mousse remise avant chaque mesure ; FPS / 1 % bas / GPU ms / fil principal ms) :

| Scénario | Avant correction | Après correction |
|---|---|---|
| Aucune mousse, vue d'ensemble | 195 / 135 / 4,4 / 2,1 | 197 / 129 / 4,4 / 2,0 |
| Peu (15 % du tapis) | 199 / 154 / 4,3 / 2,1 | 203 / 142 / 4,2 / 2,0 |
| Beaucoup (tapis, aire de lavage et sol couverts) | 196 / 153 / 4,0 / 2,4 | 200 / 120 / 4,0 / 2,3 |
| Beaucoup, vue rapprochée (tapis plein écran) | 195 / 122 / 4,2 / 2,3 | 203 / 136 / 4,2 / 2,2 |
| Beaucoup + canon à mousse | 196 / 126 / 3,7 / 2,8 | 193 / 124 / 3,9 / 2,8 |
| Idem, rendu des particules masqué (diagnostic) | 194 / 139 / 3,8 / 2,8 | 198 / 129 / 3,8 / 2,6 |
| Canon à bout portant (particules près de la caméra) | 189 / 131 / 4,0 / 2,8 | 198 / 134 / 3,9 / 2,7 |
| Rinçage d'un tapis couvert (ruissellement) | 188 / 126 / 4,1 / 2,7 | 196 / 132 / 4,0 / 2,5 |

**Profiler Unity** (build Development, captures par scénario analysées par `RugWashProfileAnalysis`) : le jeu attend la
carte graphique (`DXGI.WaitOnSwapChain` 1,5 à 2,3 ms par image) ; la mousse **ne change pas le temps GPU** (le shader du
tapis calcule la mousse à coût constant, sans transparence ni surdessin ; masquer les particules ne change rien) ; son coût
est côté CPU : `RugDirtRenderer.LateUpdate` 0,18 à 0,25 ms et `RugCleaningController.Update` 0,15 ms par image avec les
trois surfaces couvertes, `PlayerToolController.Update` 0,37 ms en pulvérisant ; allocations 0,1 à 0,2 Ko par image, aucune
collecte du ramasse-miettes pendant les mesures.

**Multijoueur (deux processus, mousse sur le tapis, l'aire de lavage et le sol)** — la cause réelle des ralentissements :

| Mesure (6 s) | Avant correction | Après correction |
|---|---|---|
| Hôte : pire coût d'écriture réseau dans une image | **13,5 ms** | **3,3 ms** |
| Hôte : pire image / médiane, à-coups | 18,8 / 8,3 ms, 2 | 11,5 / 8,3 ms, **0** |
| Invité : pire coût de lecture réseau dans une image | **9,3 ms** | **3,1 ms** |
| Hôte : pire réencodage des surfaces dans une image | 6,6 ms | 2,4 ms |
| Invité : pire réencodage des surfaces dans une image | **7,6 ms** | **3,3 ms** |
| Invité : pire image / médiane, à-coups | 19,5 / 8,3 ms, 1 | 8,8 / 8,3 ms, **0** |
| Débit réseau (mousse couvrant tout) | 41,3 Ko/s | 10,8 Ko/s |

Causes : (1) toutes les 2 s, l'hôte envoyait d'un bloc la correction de la simulation lente — avec beaucoup de mousse, les
grilles entières (~60 000 cellules), compressées dans une seule image ; l'invité les lisait et réencodait d'un bloc ;
(2) le rendu réencodait un rectangle englobant : quand la simulation lente repasse du bas au haut d'une grille, ce
rectangle couvrait toute la grille ; (3) une aire de lavage sale (le ruissellement d'un tapis moussé rincé y dépose eau
sale et mousse) resalit le tapis posé toutes les 0,4 s sur ses quatre bords à la fois : le rectangle modifié couvrait tout
le tapis, renvoyé en entier aux invités. Corrections : correction lente envoyée par tranches de lignes (4 096 cellules au
plus par envoi, toutes surfaces confondues ; un cycle complet en ~1 s, toujours une fois toutes les 2 s) ; suivi des lignes
modifiées pour le rendu ; un bord recontaminé à la fois, à tour de rôle (même quantité moyenne). Même résultat (tests :
l'invité converge exactement, le tapis se salit toujours), mousse et effets inchangés. Mesures intermédiaires (hôte, pire
écriture) : 13,5 ms → 7,8 ms après les tranches → 3,3 ms après les bords.

### 20.5 Niveaux de qualité et PC moins puissants (2.11)

**Constat** : les six niveaux de qualité utilisaient le même pipeline URP (MSAA 4×, occlusion ambiante pleine résolution,
ombres 2048, HDR) ; « Moyen » ne réduisait presque rien. **Balayage des réglages** (Ultra, scénario de rinçage d'un tapis
couvert de mousse, 1080p) : SSAO −0,55 ms GPU, ombres du projecteur −0,38 ms, échelle de rendu ×0,75 −0,8 ms, MSAA 4× −0,12 ms,
bloom −0,3 ms, post-traitement complet −0,4 ms (sur 4,2 ms). À 1080p, cette carte est si peu chargée qu'elle abaisse ses
fréquences : les écarts y sont faibles ; la charge ×4 ci-dessous est plus parlante.

Paliers créés (Ultra = pipeline d'origine, inchangé) : **Bas** (Very Low, Low) : sans MSAA, sans occlusion ambiante, sans
ombre de projecteur, échelle de rendu 0,85 ; **Moyen** : sans MSAA, occlusion à mi-résolution (échantillons et flou réduits),
ombres 1024 ; **Élevé** : MSAA 2×, occlusion à mi-résolution (qualité d'Ultra), ombres 2048 ; **Ultra** (Very High, Ultra).

Résultats (FPS moyen / 1 % bas / GPU ms, moyenne de deux exécutions pour 1080p) :

| Niveau | Repos | Rinçage tapis moussé | Canon à mousse | Mousse, vue rapprochée | Tour de l'atelier |
|---|---|---|---|---|---|
| Bas (1080p) | 203 / 147 / 3,6 | 204 / 148 / 3,1 | 206 / 130 / 2,9 | 211 / 158 / 3,2 | 207 / 145 / 3,1 |
| Moyen (1080p) | 199 / 134 / 4,0 | 200 / 141 / 3,6 | 204 / 147 / 3,4 | 202 / 146 / 3,7 | 203 / 151 / 3,4 |
| Élevé (1080p) | 193 / 112 / 4,3 | 194 / 124 / 3,9 | 193 / 110 / 3,9 | 196 / 114 / 4,1 | 192 / 117 / 3,7 |
| Ultra (1080p) | 195 / 144 / 4,4 | 200 / 147 / 3,9 | 204 / 148 / 3,7 | 203 / 150 / 4,1 | 201 / 150 / 3,7 |
| Moyen, charge GPU ×4 | 200 / 134 / 4,9 | 186 / 110 / 5,3 | 189 / 112 / 5,2 | 185 / 119 / 5,3 | 207 / 140 / 4,6 |
| Élevé, charge GPU ×4 | 192 / 121 / 5,2 | 154 / 99 / 6,4 | 160 / 104 / 6,2 | 152 / 98 / 6,5 | 197 / 118 / 4,9 |
| Ultra, charge GPU ×4 | 162 / 102 / 6,2 | 125 / 89 / 7,9 | 126 / 91 / 7,8 | 122 / 92 / 8,1 | 188 / 106 / 5,2 |
| Moyen, 2 fils CPU (fil principal ms) | 226 / 158 / 2,3 | 223 / 141 / 2,9 | 230 / 162 / 3,0 | 230 / 158 / 2,6 | 226 / 136 / 2,4 |
| Ultra, 2 fils CPU (fil principal ms) | 227 / 162 / 2,3 | 222 / 150 / 2,9 | 225 / 148 / 3,0 | 230 / 159 / 2,5 | 229 / 164 / 2,5 |

Avant la révision du palier Élevé (occlusion pleine résolution), il coûtait autant qu'Ultra sous charge ×4 (126 FPS,
7,8 ms GPU dans les scènes de mousse).

**Lecture** : le jeu n'est limité ni par le CPU (2 fils suffisent ici) ni par la mousse ; il l'est par le coût GPU par
pixel (occlusion ambiante, MSAA, ombres, post-traitement). **Cible proposée pour « Moyen »** : 60 FPS stables en 1080p sur
une carte d'environ un quart des performances de cette RTX 5060 (ordre de grandeur d'une GTX 1650). Sous la charge ×4,
« Moyen » reste à 185 à 189 FPS de moyenne dans les scènes de mousse, ce qui laisse une marge, **mais ce n'est pas une mesure
sur une GTX 1650** : charge ×4 ne reproduit ni la bande passante mémoire, ni le pilote, ni un CPU plus lent. À vérifier sur
une vraie machine moins puissante (voir limites).

**Limites** : une seule machine ; charge GPU ×4 et affinité CPU sont des approximations ; le niveau par défaut d'une
nouvelle installation reste Ultra (le joueur choisit « Moyen » dans Paramètres) ; aucune détection automatique du matériel.

## 21. Grande mise à jour (phases 1 à 7)

Exécutions réelles du 17 octobre 2026, même machine que § 20, Unity 6000.5.7f1 en mode batch (`Tools/RugWash-CI.ps1`).
Build, benchmark et test à deux processus lancés à la demande une fois la mise à jour terminée (§ 21.1). Détail des
fonctionnalités, limites et tests manuels : `GRANDE_MISE_A_JOUR.md`.

### 21.1 Tests automatisés

| Vérification | Résultat |
|---|---|
| Compilation (runtime, éditeur, tests) | ✅ 0 erreur |
| Contenu et scènes régénérés (atelier, menu principal), câblage vérifié par `ValidateReferences` | ✅ |
| Tests EditMode | ✅ **260 / 260** |
| Tests PlayMode | ✅ **156 / 156** |
| Build Windows | ✅ |
| Benchmark du jeu compilé (Ultra, 1920 × 1080) | ✅ **15 / 15** ; 133 à 151 images/s en moyenne, mémoire stable (+0,6 Mo) |
| Coopératif à deux processus (`MultiplayerAutotest`) | ✅ **62 / 62** (après correction d'un contrôle du test, voir `GRANDE_MISE_A_JOUR.md` § 8) |

Nouveaux tests : `SaveSlotsTests`, `SaveSlotsPlayModeTests` (phase 2) ; `InventoryRulesTests`, `InventoryPlayModeTests`,
`BatPlayModeTests`, `WorkshopExtensionTests`, `ExtensionPlayModeTests`, `ToolLevelsPlayModeTests` (phase 3) ;
`MissionProgressionTests` (phase 4) ; `SocialRulesTests`, `SocialPlayModeTests` (phase 5) ; `GraphicsSettingsTests`,
`PresentationPlayModeTests`, `UpdateCapturePlayModeTests` (captures, phases 6 et 7). Tests existants adaptés aux
comportements demandés (onglet Atelier à 11 articles, RugDex à 5 tapis, contrat « sinistre » au-dessus de la commande de
luxe, liste des commandes à la manette, onglet des touches, flacon translucide, recharge de la batte vérifiée par
l'autorité).

### 21.2 Ce qui n'est pas vérifiable automatiquement ici

- **Deux instances** : copie de départ et indépendance des progressions, chat, voix, expulsion, partie fermée, zones et
  machine à laver en ligne, outil doré vu sur une silhouette. `MultiplayerAutotest` couvre la progression (copie unique,
  carrières séparées, reconnexion) et le chat ; le reste demande deux joueurs.
- **Micro** (chat vocal) et **Steam** (salons, Premium : aucun DLC déclaré, option indisponible).
- **Coût de rendu** des préréglages inférieurs à Ultra : non mesuré (le benchmark tourne en Ultra). Les réglages de chaque palier sont vérifiés
  (`GraphicsSettingsTests`).
- **Rendu à l'œil** : captures `Logs/ci/screens/40_…` à `54_…` (annexe, machine à laver, icônes, boutique, casier, flacon,
  outil doré, paramètres graphiques, tapis presque noir).

## 22. V0.2.5

Exécutions réelles du 18 octobre 2026, même machine que § 20, Unity 6000.5.7f1 en mode batch (`Tools/RugWash-CI.ps1`).
**Aucun build, aucun benchmark, aucun test à deux processus** n'a été lancé (consigne). Détail : `V0_2_5.md`.

| Vérification | Résultat |
|---|---|
| Compilation (runtime, éditeur, tests) | ✅ 0 erreur |
| Contenu et scènes régénérés (fût translucide, sons préchargés), câblage vérifié par `ValidateReferences` | ✅ |
| Tests EditMode | ✅ **265 / 265** |
| Tests PlayMode | ✅ **164 / 164** (+ 2 diagnostics manuels ignorés : `FirstUseDiagnosticsPlayModeTests`) |
| Build Windows, benchmark, coopératif à deux processus | **Non lancés** (consigne) |

Nouveaux tests : `SessionEconomyRulesTests` (revente, achats communs ou personnels, atelier réservé à l'hôte, vue réseau v3),
`SaveSlotsTests.SaveV5_MigratesToV6…`, `V025PlayModeTests` (revente, outils dorés mesurés, lance dorée, transition depuis un
résumé, fût de 40 L, atelier côté invité, sons préchargés), captures `80_…` à `85_…`. Adaptés : `BugfixPlayModeTests`
(l'invité n'écrit plus ses emplacements), `MissionProgressionTests` et `GraphicsSettingsTests` (difficulté supprimée).

Diagnostic du premier nettoyage (dans l'éditeur, relevé des marqueurs du profileur, pas un benchmark) : avant, 20 à 30 ms de
`SoundManager.LoadFMODSound` au premier usage de chaque outil (pire image des 20 premières : 33 à 38 ms) ; après, plus de
chargement de son en partie (pire image : 4 à 5 ms). Niveaux d'outil mesurés sur un même tapis : aspirateur doré +12 % de
saleté retirée par rapport au niveau 2 (avant +5 %), lance dorée +21 %.

Non vérifiable ici : deux instances (sauvegarde de session, objets perdus et ramassés, transition depuis un résumé, atelier
refusé en ligne), Steam, coût réel dans le jeu compilé.

## 23. V0.3.0 (refonte graphique)

Exécutions réelles du 19 octobre 2026, même machine, Unity 6000.5.7f1 en mode batch (`Tools/RugWash-CI.ps1`).
**Aucun build, aucun benchmark, aucun test à deux processus** n'a été lancé (consigne). Détail : `V0_3_0.md`.

| Vérification | Résultat |
|---|---|
| Compilation (runtime, éditeur, tests) | ✅ 0 erreur |
| Contenu et scènes régénérés (matériaux, accessoires, modèles Blender, tapis, interface), câblage vérifié à la génération | ✅ |
| Tests EditMode | ✅ **265 / 265** |
| Tests PlayMode | ✅ **168 / 168** (+ 2 diagnostics manuels ignorés) — dont 4 nouveaux tests de captures `V030CapturePlayModeTests` |
| Captures relues à l'œil | ✅ atelier (6 vues), tapis sale / à moitié nettoyé / mousse, HUD, 11 outils (en action et au repos), ordinateur, commandes, boutique, casier, pause, résultats, menu principal, personnage, paramètres, multijoueur, avatars distants (brosse, lance), machines, paliers d'atelier |
| Build Windows, benchmark, coopératif à deux processus | **Non lancés** (consigne) |

Corrigé pendant la vérification : chevauchement du solde et du titre (commandes, boutique, RugDex) après le passage des
titres à gauche ; bandeau vide en tête de la fenêtre Pause (contour pris dans la disposition) ; « revente … € » coupé dans la
boutique (colonne d'état élargie) ; ligne de sauvegarde du menu Multijoueur qui recouvrait les boutons du salon.

Seconde passe (MCP Blender : machines, robot, seau, raclette, monobrosse, gant revu, canapé et plantes des paliers) :
contenu et scènes régénérés, **EditMode 265 / 265, PlayMode 168 / 168** (+ 2 diagnostics ignorés), captures relues
(machines, robot en charge et en nettoyage, paliers 3 et 4, seau, raclette, monobrosse).

Non vérifiable ici : coût réel en images par seconde dans le jeu compilé, rendu sur d'autres cartes graphiques, avatars
distants en vraie partie à deux processus.

### 23.1 Performance V0.3.0 contre V0.2.5 (jeu compilé)

Mesures du 19 octobre 2026 à la demande du joueur : build Windows de chaque version (V0.2.5 = commit `0d14e79`, compilé
dans une copie de travail séparée), benchmark complet en 1920 × 1080, Ultra, **en alternance** V0.2.5 → V0.3.0 → V0.2.5 →
V0.3.0 (même machine, RTX 5060, i7-8700K). Les 15 contrôles fonctionnels passent dans les quatre exécutions.

**Les FPS sont plafonnés à 180 sur cette machine** (le benchmark désactive pourtant la synchronisation verticale ; les mesures
de la grande mise à jour montaient à 192–205 FPS : plafond extérieur au jeu, pilote ou écran). Le temps GPU par image, non
plafonné, sert donc de comparaison (moyenne des deux exécutions de chaque version) :

| Scénario | GPU V0.2.5 | GPU V0.3.0 | Écart |
|---|---|---|---|
| Atelier au repos (vue d'ensemble) | 4,55 ms | 3,46 ms | −1,08 ms |
| Tour complet de l'atelier | 4,17 ms | 3,84 ms | −0,32 ms |
| Vue d'ensemble, sans / peu / beaucoup de mousse | 4,09–4,32 ms | 3,33–3,56 ms | −0,73 à −0,76 ms |
| Rinçage, savon et brosse, canon (tapis visé) | 4,16–4,40 ms | 4,51–4,69 ms | +0,23 à +0,35 ms |
| Tapis en plein écran (mousse ou non), mousse au repos | 4,23–4,43 ms | 4,63–4,87 ms | +0,40 à +0,46 ms |
| Session prolongée (3 commandes) | 4,42 ms | 4,50 ms | +0,08 ms |

Fil principal du processeur : identique ou légèrement plus bas en V0.3.0 (1,98 à 2,90 ms contre 2,13 à 2,86 ms).
Session de 3 commandes : objets, particules, textures et maillages constants, mémoire gérée +0,6 Mo.

**Lecture** : la refonte coûte **0,2 à 0,5 ms de GPU quand le tapis remplit l'écran** (shader des tapis : masque de surface et
velours, motifs plus détaillés) et en **économise 0,7 à 1,1 ms en vue d'ensemble**. Avec le plafond de 180 images par seconde
(5,56 ms par image), une image qui dépasse le budget tombe à 90 FPS : le 1 % le plus lent descend à 90 FPS dans 9 scénarios
sur 17 en V0.3.0 contre 3 en V0.2.5, toujours de près du tapis. Sans ce plafond, ou à 60 et 144 Hz, la marge reste très
large (≈ 4,9 ms au pire pour 16,7 ms et 6,9 ms disponibles). Mesures d'une seule machine, bruitées de quelques pour cent.

### 23.2 Modèles Blender (MCP) exportés en FBX et intégrés au jeu
Exécutions du 19 octobre 2026 (Unity 6000.5.7f1, mode batch, éditeur fermé) :

- `V030FbxPrototypeTests` : les 36 FBX de `Models/FBX` (9 outils, 12 accessoires, 4 tapis, 4 techniciens, 11 éléments
  d'atelier) s'importent, à l'échelle réelle (lance 0,80 m, casier 1,80 m, bidon 0,55 m, techniciens 1,80 m), sans
  matériau manquant ; techniciens avec maillage à squelette (23 os) ; collisions des éléments d'atelier ; six matériaux
  d'état des tapis (`M_RugState_*`). Galerie `Logs/ci/screens/v30_fbx_*`. **Réussi.**
- Intégration en jeu (`RugWashToolModels`, `RugWashDecor`, `RugWashExtensionBuilder`, `RugWashFbxModels`) : lance de rinçage,
  seau, raclette, monobrosse, brosse, batte, casier (porte animée), machine à laver (hublot, tambour, écran, voyant), caisse
  à outils. `Capture_V030_Tools` et `Capture_V030_BlenderModels` : **réussis**, captures relues.
- Suites complètes : EditMode **265/265** ; PlayMode **170 réussis, 0 échec, 2 ignorés** (diagnostics `Explicit`). Une
  première exécution PlayMode a échoué sur 2 tests (sommets des têtes d'outil illisibles) ; corrigé (maillages lisibles pour
  `Tools/` et `Props/`), puis toute la suite PlayMode a été relancée. La suite EditMode n'a pas été relancée après ce
  réglage d'import.
- Non lancés : build, benchmark, test multijoueur à deux processus.

