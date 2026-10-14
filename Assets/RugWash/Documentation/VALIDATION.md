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
