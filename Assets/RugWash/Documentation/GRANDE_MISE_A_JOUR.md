# Grande mise à jour — RugWash Simulator

Ce document décrit la grande mise à jour (phases 1 à 7) : ce qui a été **implémenté**, ce qui a été **vérifié
automatiquement** (tests EditMode / PlayMode exécutés dans Unity 6000.5.7f1, en mode batch), et ce qui reste **à tester à
la main** — en particulier tout ce qui demande deux instances du jeu, un micro, Steam ou un build. Conformément à la
consigne, le build, le benchmark et le test à deux processus n'ont été lancés qu'à la demande, une fois la mise à jour
terminée (§8).

Résultat de la dernière vérification Unity (compilation, régénération des scènes, tests) : voir §8.

---

## 1. Diagnostic initial (phase 1, résumé)

- Une seule sauvegarde (`rugwash_save.json`) mélangeait réglages, apparence et progression ; aucune séparation solo /
  multijoueur ; en ligne, l'économie affichée était celle de l'hôte.
- Inventaire : tous les outils achetés étaient portés en même temps ; aucun rangement.
- Batte : la recharge démarrait même sur un coup dans le vide ; cible cherchée au début du mouvement.
- Une seule zone de lavage, une seule commande à la fois ; pas de machine longue durée.
- Améliorations d'outil à un seul niveau ; aucun retour visuel de « fin de progression ».
- Missions : progression surtout par le nombre de salissures ; tapis choisis au hasard dès le départ ; aucune difficulté
  réglable ; pas de tapis très sale.
- En ligne : ni chat, ni voix, ni modération (expulsion, partie fermée).
- Présentation : niveau de liquide « peint » sur le flacon ; aucune icône d'outil ; textes de la boutique qui débordaient
  (grille à deux colonnes de hauteur fixe) ; V-Sync lié au niveau de qualité ; « Très bas » et « Bas » identiques.

---

## 2. Sauvegardes (phase 2)

**Implémenté**
- **Profil du joueur** (`rugwash_profile.json`) : niveau et expérience, paramètres, apparence, derniers emplacements utilisés.
  Le niveau appartient au joueur, pas à l'emplacement.
- **Six emplacements** : `rugwash_solo_1..3.json` et `rugwash_multi_1..3.json` (format v5 : `mode`, `slotIndex`,
  `playSeconds`, `multiplayerInitialized`, `carried`). Un emplacement solo n'est jamais chargé en ligne, ni l'inverse.
- **Aperçu** de chaque emplacement lu dans le fichier (temps de jeu, dernière session, mode, progression).
- **Emplacement multijoueur** : à la première arrivée chez un hôte, la progression de départ est **copiée une seule fois**
  depuis l'hôte ; ensuite elle est indépendante (achats et récompenses dans la carrière de chacun, tenue et validée par
  l'hôte, sauvegardée chez le joueur). Une reconnexion ne recopie jamais rien.
- **Migration** : l'ancienne `rugwash_save.json` devient « Solo 1 » (renommée `.migrated`).
- Nouveaux champs de l'atelier (grande mise à jour) : `workshop.workSurface2` (aire de la zone 2) et `workshop.washer`
  (machine à laver : commande, temps restant, état du tapis). Champs absents d'une ancienne sauvegarde : valeurs par défaut.

**Vérifié automatiquement** : `SaveSlotsTests`, `SaveSlotsPlayModeTests`, `SaveAndMenuPlayModeTests`, carrière de l'invité
(`BugfixPlayModeTests`), réplique invité des zones et de la machine (`ExtensionPlayModeTests`).
**À tester à la main** : deux instances (copie de départ, indépendance, reconnexion) — voir §9.

---

## 3. Atelier, inventaire, batte, zones de lavage, machine à laver, améliorations (phase 3)

**Inventaire et casier** : 4 emplacements portés (outils et gadgets ; le pipi n'en occupe pas), le reste au casier (contre
le mur du fond). Un objet est soit porté, soit rangé : rien ne se duplique. Sauvegardé (`carried`).

**Batte** : le geste et le sifflement partent toujours ; la cible est cherchée **au moment de l'impact** (38 % du
mouvement), dans l'axe et à portée ; un coup dans le vide ne déclenche **aucune recharge** et s'enchaîne au rythme du
geste (0,45 s) ; seul un coup qui touche démarre la recharge — **vérifiée par l'autorité** (un client modifié qui ignore sa
recharge est refusé). Rebond et message « Touché ! ». En ligne, le geste est rejoué sur la silhouette (cadence bornée par
l'hôte).

**Agrandissement de l'atelier** (boutique, onglet Atelier, 1 800 €, réputation 12) : la cloison du mur droit disparaît,
l'**annexe** s'ouvre avec une **deuxième zone de lavage complète** (aire, bordures, caniveau, projecteur). Deux commandes,
deux tapis, deux chronomètres en même temps ; la zone courante est celle où se trouve le joueur (HUD, inspection, scan,
livraison). Le tableau de l'ordinateur livre ou abandonne la commande de la zone choisie (bouton « Autre zone »).
En ligne : surfaces 3 et 4 répliquées, commande et chronomètre de chaque zone, demandes de l'invité par zone.

**Machine à laver** (1 400 €, réputation 15, agrandissement requis ; programme rapide 900 €, réputation 25) :
- On y charge le tapis d'une commande : sa zone est **libérée aussitôt** pour une autre commande ; la commande reste en
  cours (« En machine à laver »), son chronomètre est suspendu.
- **Cycle de 15 minutes** de jeu (9 min avec le programme rapide), décompté par l'autorité (solo ou hôte), image par image,
  **jamais plus de 0,5 s par image** : un changement d'heure du système, un gel ou une absence (jeu fermé) ne le font pas
  avancer. Le temps restant est **sauvegardé** avec l'atelier.
- États : libre, lavage (écran du temps restant, tambour qui tourne, voyant orange), terminé (voyant vert).
- Résultat : poussière et saleté détachée presque entièrement parties, la plupart des taches selon leur réponse à l'eau et
  leur adhérence ; mousse et résidus rincés ; le tapis ressort **trempé** et les **taches tenaces restent** ; un tapis
  **fragile s'use** (soie). Le tapis revient dans une zone libre ; la propreté se mesure toujours depuis l'état d'origine.
- En ligne : état diffusé aux invités (variable réseau), demandes de l'invité validées par l'hôte.

**Améliorations d'outil par niveaux** : trois niveaux de puissance pour l'aspirateur, la brosse, la lance, l'extracteur,
le pulvérisateur, le canon à mousse, le balai et la monobrosse (niveau 1 existant ou nouveau, puis ×1,25 et ×1,2 ; prix
×2,2 et ×5 ; réputation 8 à 35). Effet chiffré dans la boutique (« Niveau 2/3 · puissance ×1,4 → ×1,75 »), niveau affiché
au HUD. **Au niveau 3, l'outil devient doré** : pièces du modèle tenu en main en or (gants, poils et liquides inchangés),
nom en or et « ★ » dans la barre d'outils, et **visible par les autres joueurs** (masque calculé par l'hôte depuis la
carrière qu'il tient : non falsifiable par un invité).

**Vérifié automatiquement** : `InventoryRulesTests`, `InventoryPlayModeTests`, `BatPlayModeTests`, `WorkshopExtensionTests`,
`ExtensionPlayModeTests` (zones, livraison par zone, machine : chargement, cycle, sortie, sauvegarde, réplique invité),
`ToolLevelsPlayModeTests`. **À tester à la main** : rendu de l'annexe et de la machine, outil doré vu par un autre joueur.

---

## 4. Missions (phase 4)

- **Progression** : les premières missions (palier 0) confient les **deux plus petits tapis**, peu sales (étendue ×0,7,
  intensité réduite), objectif modeste et paiement réduit ; les plus grands tapis arrivent avec les paliers (rang de
  surface ; au-delà de 7,5 m², palier 4) ; chronomètre du bonus proportionnel à la surface ; paiement selon la surface.
- **Tapis presque noirs** : nouvelle salissure **« Crasse noire incrustée »** (presque noire, très adhérente : détergent
  et long brossage humide) ; thèmes « Sinistre » (palier 4) et « Grand nettoyage » (palier 6, crasse sur tout le motif) ;
  produit **« Décrassant intensif »** ; **grand tapis de hall** (3,8 × 2,6 m) et contrat « Grand tapis sinistré »
  (réputation 20).
- **Difficulté réglable** (Paramètres → Général → Jeu : Détendu / Normal / Exigeant) : étendue et intensité de la saleté,
  objectif (65 à 97 %), temps du bonus, paiement (×0,85 / ×1 / ×1,3). Elle s'applique aux **nouvelles** missions ; elle fait
  partie de l'identifiant de la mission (les invités reproduisent exactement les missions de l'hôte ; les anciens
  identifiants restent lisibles).

**Vérifié automatiquement** : `MissionProgressionTests`, `EconomyV012Tests`, `ContentRulesTests`.
Note : à la première ouverture d'une ancienne sauvegarde, les offres affichées sont régénérées avec les nouvelles règles.

---

## 5. En ligne : chat, voix, expulsion, partie ouverte/fermée, Premium (phase 5)

- **Chat textuel** : Entrée pour écrire, Entrée pour envoyer, Échap pour annuler ; lignes en bas à gauche (elles
  s'effacent). Le texte est nettoyé chez l'émetteur **et** chez l'hôte (caractères de contrôle, emojis hors police,
  longueur 120), la cadence est limitée par l'hôte (0,6 s entre deux messages, 5 par 10 s), le nom affiché est celui de la
  session (un invité ne peut pas parler au nom d'un autre). Arrivées, départs et commandes s'y inscrivent aussi.
- **Chat vocal de proximité** : ce n'est **pas** un service vocal natif de Steam. Le micro est capté par l'API Microphone
  de Unity, encodé en μ-law (G.711, 16 kHz, 16 ko/s par joueur qui parle), relayé par l'hôte et **joué depuis la tête de la
  silhouette** (source 3D : on entend moins fort de loin, plus du tout au-delà de 16 m). Micro : « appuyer pour parler (V) »
  (par défaut), « toujours ouvert » (détection de voix) ou « coupé » ; volume des voix des autres (0 = coupées) ; voix
  d'un joueur coupée pour soi dans la fenêtre « Joueurs ». Indicateur « parle » sur le nom de la silhouette, dans la
  liste des joueurs du HUD et dans la fenêtre « Joueurs ».
- **Expulsion** (hôte seulement, menu pause → Joueurs → Expulser, avec confirmation) : vérifiée par la session de l'hôte
  (jamais lui-même, seulement un joueur connecté) ; le joueur reçoit « Vous avez été expulsé de la partie par l'hôte » et
  **ne peut plus rejoindre cette partie** (même identité).
- **Partie ouverte / fermée** (hôte) : fermée, l'hôte refuse toute nouvelle connexion (« La partie est fermée par
  l'hôte… ») et le salon Steam n'est plus rejoignable.
- **Premium uniquement** : la vérification de propriété est **réelle et faite par l'hôte via Steam** (ticket
  d'authentification du joueur validé par Steam, puis licence du DLC Premium vérifiée par `UserHasLicenseForApp`, réponse
  attendue au plus 12 s). **Limite** : le jeu utilise l'App ID de test 480 et **aucun DLC Premium n'existe** ; l'option est
  donc **grisée et expliquée** dans le menu Multijoueur. Elle s'activera quand un DLC sera publié et son App ID renseigné
  (`NetworkContent.premiumAppId`). Aucune vérification factice.
- Protocole réseau : `rugwash-coop-11` (incompatible avec les versions précédentes, refus clair).

**Vérifié automatiquement** : règles pures (`SocialRulesTests` : nettoyage et cadence du chat, codec μ-law, détection de
voix, tampon de gigue, refus partie fermée / joueur expulsé, ticket dans les données de connexion), solo sans chat ni micro
(`SocialPlayModeTests`). **Non vérifiable sans deux instances, un micro et Steam : à tester à la main** (§9).

---

## 6. Présentation (phase 6)

- **Flacon vert translucide** : le pulvérisateur a une paroi en plastique translucide (shader « Translucent Plastic » :
  reflet, bords plus opaques) et un **liquide en volume** (shader « Liquid Container » réécrit) : son niveau suit la
  réserve réelle, sa **surface reste horizontale** quand l'outil penche et **ballotte** quand il bouge (ressort amorti).
  Une passe chacun, sans texture (URP). Même principe pour la bouteille du canon à mousse.
- **Icônes 3D des outils** : une seule caméra hors de la salle photographie chaque modèle (le même que celui tenu en main,
  doré compris) en trois quarts plongeant, dans un atlas commun ; rendu au démarrage et seulement quand un outil devient
  doré. Affichées dans la barre d'outils, le casier et la boutique (outils et leurs améliorations).
- **Boutique** : une colonne pleine largeur, chaque ligne **à la hauteur de son texte** (plus de texte qui déborde), icône
  à gauche quand il y en a une.
- **Textes 3D du décor** (panneaux, affiches, afficheur de la machine à laver, étiquettes des machines) : ils ne se voient
  plus à travers les murs (shader « World Text » testé en profondeur, à la place du matériau intégré des polices qui
  dessinait par-dessus tout). Les noms au-dessus des joueurs gardent volontairement l'ancien comportement.
- Retouches relevées sur les captures : machine à laver tournée vers la pièce, en-tête de l'ordinateur et rappel des
  commandes du HUD qui ne passent plus sous les autres textes, nom de l'outil sur une ligne (niveau affiché dès le premier
  niveau acheté).
- **Options graphiques** (nouvel onglet « Graphismes ») : qualité (4 préréglages), synchronisation verticale
  (**indépendante** du préréglage), limite d'images (30 à 240 ou illimitée ; affichée « Réglée par la synchronisation » et
  sans effet tant que la synchronisation est active), post-traitement, description de ce que change chaque préréglage.
  Tout est sauvegardé dans le profil.
- **Ray tracing** : **non proposé**. Le jeu utilise l'Universal Render Pipeline, qui n'offre pas d'effets en lancer de
  rayons (réservés à HDRP). L'onglet affiche l'état (« votre carte le prend en charge, mais… » / « non pris en charge »),
  sans bouton.

**Vérifié automatiquement** : `PresentationPlayModeTests` (icônes, lignes de boutique, ballottement, préréglages, V-Sync,
post-traitement, sauvegarde), `EconomyV012PlayModeTests` (niveau du flacon). **À voir à l'écran** : aspect du flacon, des
icônes, de l'or.

---

## 7. Optimisation et qualité (phase 7)

- **Quatre préréglages qui changent vraiment le rendu** : Très bas (rendu 70 %, aucune ombre, HDR coupé, textures en
  demi-résolution, sans occlusion ni MSAA), Bas (85 %, sans ombres de projecteur ni occlusion), Moyen (ombres 1024,
  occlusion à mi-résolution), Ultra (pipeline d'origine : ombres 2048, occlusion complète, MSAA 4×). Une ancienne
  sauvegarde réglée sur « Élevé » ou « Très élevé » passe à Ultra (même coût mesuré). Repli : sans niveau choisi, celui du
  projet (Ultra).
- **Coût nul quand inutilisé** : annexe non rendue avant l'achat (cloison), machine à laver cachée et sans calcul avant
  l'achat, icônes rendues une fois, micro jamais ouvert hors ligne, chat reconstruit seulement à l'arrivée d'une ligne,
  HUD : alerte d'aire réécrite seulement si son état affiché change.
- **Mesures** (benchmark du jeu compilé, préréglage Ultra, 1920 × 1080, RTX 5060 / i7-8700K) : 133 à 151 images/s en
  moyenne selon le scénario, mémoire gérée stable (+0,6 Mo sur trois commandes, aucun objet en plus). Les préréglages
  inférieurs n'ont pas été mesurés séparément (le benchmark tourne en Ultra).

---

## 8. Tests automatisés

Exécutés dans Unity 6000.5.7f1 (mode batch) via `Tools/RugWash-CI.ps1` : `compile`, `scene` (régénération des scènes et
câblage vérifié), `test` (EditMode), `playmode` (PlayMode).

| Vérification | Résultat |
|---|---|
| Compilation (runtime, éditeur, tests) | ✅ 0 erreur |
| Scènes régénérées et câblées (`ValidateReferences`) | ✅ |
| EditMode | ✅ **260 / 260** |
| PlayMode (captures comprises) | ✅ **156 / 156** |

Dernière exécution : 17 octobre 2026 (détail : `VALIDATION.md` § 21).

Lancés ensuite à la demande (17 octobre 2026) :

| Vérification | Résultat |
|---|---|
| Build Windows | ✅ |
| Benchmark du jeu compilé | ✅ **15 / 15** vérifications |
| Coopératif à deux processus (`MultiplayerAutotest` : hôte, invité, reconnexion après crash) | ✅ **62 / 62** vérifications |

Le premier passage multijoueur avait échoué sur une seule vérification : l'hôte lisait la carrière de l'invité 4 s après
la livraison, alors que l'invité venait déjà d'acheter un article (468 € → 318 €). C'était un défaut du test, pas du jeu
(côté invité, la récompense était bien constatée). Le contrôle retient désormais le plus haut solde vu pendant cette
fenêtre. Restent manuels : voix avec un vrai micro, Steam, kick et partie fermée, zone 2 et machine à laver en ligne.

---

## 9. Tests manuels à faire

Solo
1. Menu → Jouer : trois emplacements solo, aperçu (temps de jeu, date) ; créer, continuer, effacer (confirmation).
2. Casier : ranger / prendre ; 4 objets portés au plus ; touches 1–4 ; le pipi reste disponible sans emplacement.
3. Batte : coups dans le vide enchaînés sans recharge ; coup sur le tapis → recharge, rebond, « coup sans effet » pendant
   la recharge.
4. Acheter l'agrandissement : la cloison disparaît, l'annexe est éclairée ; accepter deux commandes, vérifier les deux
   zones (HUD « Zone 1 / Zone 2 », inspection, scan), livrer chacune depuis l'ordinateur (« Autre zone »).
5. Machine à laver : charger le tapis d'une zone, accepter une autre commande dans la zone libérée ; quitter et relancer
   pendant le lavage (temps restant conservé, n'avance pas jeu fermé) ; récupérer le tapis (trempé, taches tenaces).
6. Monter un outil au niveau 3 : modèle doré, « ★ » dans la barre, icône dorée dans le casier et la boutique.
7. Missions : début de partie (petits tapis peu sales), difficulté Exigeant (paiement et objectif plus hauts).
8. Paramètres → Graphismes : chaque préréglage (différence visible et de fluidité), V-Sync on/off, limite d'images,
   post-traitement ; relancer : tout est conservé. Ray tracing : message, aucun bouton.
9. Boutique : aucun texte ne déborde, icônes des outils.

En ligne (deux instances : réseau local et Steam)
10. Première arrivée sur un emplacement multijoueur : progression copiée depuis l'hôte ; achats de l'un sans effet chez
    l'autre ; reconnexion : rien n'est recopié.
11. Chat : messages dans les deux sens, accents, message trop long tronqué, envoi trop rapide refusé.
12. Voix (micro réel) : appuyer pour parler (V), toujours ouvert, coupé ; volume selon la distance ; « parle » affiché ;
    couper la voix d'un joueur.
13. Expulsion : confirmation, motif chez le joueur expulsé, impossible de revenir ; partie fermée : connexion refusée,
    salon Steam non rejoignable ; réouverture.
14. Zone 2 et machine à laver en ligne : actions de l'invité validées par l'hôte, états identiques des deux côtés.
15. Outil doré d'un joueur visible sur sa silhouette chez l'autre.
16. Premium : option grisée avec son explication (aucun DLC déclaré).

Build et performance (à lancer seulement sur demande) : `-Step build`, `-Step benchmark`, `-Step multiplayer`.

---

## 10. Limites connues

- Voix : qualité téléphonique (μ-law 16 kHz), sans annulation d'écho ni réduction de bruit ; latence de l'ordre de
  250–400 ms (tampon de lecture d'Unity) ; premier micro de Windows uniquement ; pas de voix à la manette (touche V).
- Chat : pas de saisie à la manette (lecture seulement).
- Premium : indisponible tant qu'aucun DLC Steam n'est publié (App ID de test 480).
- Machine à laver : l'image « avant » d'un tapis resté en machine à la fermeture du jeu est reprise à la sortie (pas
  l'image d'origine).
- Les machines d'appoint (souffleur, plaque, rampe) et le robot restent liés à la zone 1.
- Ray tracing : impossible avec URP (aucun faux réglage proposé).
