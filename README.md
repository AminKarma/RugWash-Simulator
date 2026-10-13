# RugWash Simulator

Jeu original de nettoyage de tapis à la première personne (PC Windows), en cours de développement.
Le joueur tient un petit atelier : il inspecte des tapis sales, choisit ses outils, brosse, rince
et voit le motif d'origine réapparaître progressivement.

> **État actuel : démo complète (phases 0 à 6) + roadmap V2 jalons 0 à 5 + update v0.1.2 jalon 1** (game feel, scan, nouveaux
> outils, aire de lavage, robot aspirateur, tapis de soie, nouvelles salissures, produits spécialisés, RugDex, manette,
> **coopératif jusqu'à 4 joueurs** — réseau local testé, Steam implémenté mais pas encore testé en réel, voir § Multijoueur).
> v0.1.2 jalon 1 : outils posés sur le point visé (manche télescopique), modèles détaillés chez les autres joueurs,
> progression personnelle en ligne, salissures limitées à l'aire de lavage, plus de tapis d'entraînement, seuils de l'aire
> (25 % / 40 %), tapis terminé sous 1 % — audit et décisions : [AUDIT_V012.md](Assets/RugWash/Documentation/AUDIT_V012.md).
> v0.1.2 jalon 2 : plus d'à-coups quand la mousse couvre le tapis (simulation lente répartie), mousse au repos ~15× moins
> coûteuse en réseau, budget des effets réglable, session prolongée sans fuite (`VALIDATION.md` § 14).
> Build Windows produit et vérifié automatiquement.
> Rapport de validation : [Assets/RugWash/Documentation/VALIDATION.md](Assets/RugWash/Documentation/VALIDATION.md).
> Les sons sont des **substituts temporaires synthétisés** (voir § Audio).
> Voir [Assets/RugWash/Documentation/PRODUCTION.md](Assets/RugWash/Documentation/PRODUCTION.md) pour le suivi détaillé.

---

## 1. Version et dépendances

| Élément | Valeur |
|---|---|
| Unity | **6000.5.7f1** (fixé dans `ProjectSettings/ProjectVersion.txt`) |
| Pipeline | **URP 17.5.0**, chemin de rendu Forward+ |
| Entrées | **Input System 1.20.0** (backend « Input System Package » uniquement) |
| UI | uGUI 2.5.0 (police intégrée `LegacyRuntime.ttf`) |
| Tests | Unity Test Framework 1.7.0 |
| Réseau | **Netcode for GameObjects 2.13.3**, Unity Transport 2.6.0 (NGO 3.x exige Unity 6000.7) |
| Steam | **Steamworks.NET 2025.164.1** (paquet UPM Git) — App ID de développement 480, voir `MULTIPLAYER.md` |
| Plateforme cible | Windows 64 bits (DX11, DX12, Vulkan) |

Aucun asset tiers : toutes les textures (motifs de tapis, fibres, béton, bois, bandes de sécurité…) sont
**générées procéduralement** par `RugWashTextureGenerator`, et tous les modèles (outils, mobilier, tapis suspendus
et roulés, flexibles, fûts…) par `RugWashProceduralMeshes` / `RugWashToolModels` / `RugWashDecor`.
Ce sont des créations originales.

## 2. Ouvrir et lancer le projet

1. Unity Hub → *Add project from disk* → ce dossier → éditeur **6000.5.7f1**.
2. Ouvrir `Assets/RugWash/Scenes/MainMenu.unity` (scène de démarrage du build), puis **Play**.
   `Workshop.unity` peut aussi être lancée directement : elle charge la sauvegarde existante.

Si une scène est absente ou cassée : menu **RugWash → Setup → Générer la Scène Atelier Complète** /
**Générer le Menu Principal** (scènes entièrement reconstruites et câblées par code).

> Le tapis réel (maillage, saleté) est créé au lancement ; en mode édition, un aperçu propre est affiché.

## 3. Commandes

Le jeu se joue entièrement au clavier/souris **ou** à la manette (Xbox, PlayStation et manettes compatibles avec l'Input
System), et l'on peut passer de l'un à l'autre à tout moment : les indications à l'écran (HUD, invites, boutons « Fermer »,
fenêtre des touches) suivent le dernier périphérique utilisé, le curseur est masqué dans les menus à la manette.

| Action | Clavier / souris | Manette (Xbox · PlayStation) |
|---|---|---|
| Se déplacer | **ZQSD** (AZERTY) / **WASD** (QWERTY) — touches physiques — ou flèches | Stick gauche |
| Courir | Maj gauche (maintenue) | Clic du stick gauche (LS · L3) : la course dure jusqu'à l'arrêt |
| Regarder | Souris | Stick droit |
| Utiliser l'outil | Clic gauche (maintenu) | RT · R2 (maintenue) |
| Outil suivant / précédent | Molette, ou 1–8 (rangée du haut ou pavé numérique) | RB / LB · R1 / L1 (les outils pas encore achetés sont sautés) |
| Interagir (postes, caniveau, comptoir des commandes, boutique, robot aspirateur ou sa base) | E | A · Croix |
| Scanner la saleté (surligne les zones encore sales, puis recharge) | R | Y · Triangle |
| Changer de produit spécialisé (produits en stock, puis détergent standard) | T | X · Carré |
| Inspecter le tapis (avant/après) | F | Vue · Partager |
| Pause | Échap | Menu · Options |
| Fermer la fenêtre / revenir | Échap | B · Rond |
| Menus : se déplacer, valider | Souris, flèches, Entrée | Croix directionnelle ou stick gauche, A · Croix |
| Changer d'onglet (boutique, paramètres) | Clic sur l'onglet | LB / RB · L1 / R1 |
| Diagnostic (éditeur / build de développement) | F3 | — |

Dans les menus, la fenêtre ouverte reçoit le focus (premier élément, ou celui laissé à la fermeture d'une fenêtre ouverte
par-dessus) et les listes défilent jusqu'à l'élément sélectionné.

**Paramètres** (menu principal ou pause → « Paramètres », trois onglets) — chaque option est réellement appliquée :
- **Général** : volumes général / effets / interface, résolution, mode d'affichage (plein écran, plein écran fenêtré,
  fenêtré), niveau de qualité ;
- **Commandes** : sensibilité de la souris, inversion de l'axe vertical (souris et stick), **balancement** de la caméra et des
  outils (0 % = désactivé), **vitesse de la caméra au stick** (30–360 °/s), **zone morte des sticks** (2–50 %),
  **vibrations** (intensité, 0 = désactivées) ;
- **Touches** : reconfiguration **clavier** (avancer, reculer, gauche, droite, courir, interagir, scanner, changer de produit,
  inspecter ; Échap annule) et **manette** (courir, interagir, scanner, produit, inspecter, utiliser l'outil, outil
  suivant / précédent ; Menu annule — les sticks, Menu et B restent fixes). Une touche déjà utilisée est signalée.
  « Touches et boutons par défaut » rétablit tout.

**Vibrations** (manette) : grondement propre à chaque outil pendant son utilisation (réglé dans sa définition) et brèves
impulsions (palier de propreté, objectif atteint, scan, outil verrouillé). Coupées en pause, dans les menus, quand la
manette n'est plus utilisée et à la perte du focus.
Résolution et mode d'affichage n'ont d'effet visible que dans un build (l'éditeur les ignore).

## 4. Outils et boucle de nettoyage

| Touche | Outil | Rôle | Consommable |
|---|---|---|---|
| 1 | Aspirateur | Retire poussière et débris secs ; aspire la saleté détachée **sur fibres sèches** ; sans effet sur les taches incrustées | — (électrique) |
| 2 | Détergent | Dépose mousse + produit ; ne nettoie pas seul mais rend la brosse très efficace sur graisse, boissons, taches incrustées | Détergent 4 L (0,1 L/s) |
| 3 | Brosse | Détache la saleté fixée (devient boue à rincer) ; efficacité selon sec / humide / savonneux et type de saleté | — |
| 4 | Lance de rinçage | Évacue saleté détachée, mousse et (plus lentement) résidus ; mouille | Eau 40 L (1,5 L/s) |
| 5 | Extracteur | Aspire l'eau des fibres avec la saleté et le produit qu'elle contient | Remplit la cuve de récupération (12 L) |
| 6 | Canon à mousse | Projette en **éventail large** une mousse épaisse et diluée (moins de résidus que le détergent pur). Laissée en place, la mousse **détache peu à peu** graisse, boissons et boue ; à brosser puis rincer | Détergent (0,22 L/s) |
| 7 | Balai | Tapis **sec** : chasse poils et débris (à passer avant le produit) ; l'humidité colle les débris, les taches fixées ne bougent pas. Aire de lavage et sol : pousse **eau sale, boue et mousse** vers le caniveau | — |
| 8 | Brosse de détail | Petite zone, **×2,2 sur la graisse et les taches incrustées** — **à acheter** à la boutique (140 €) | — |

Séquence type : **aspirer (ou balayer) → savonner ou mousser (et laisser agir) → brosser → rincer → extraire**,
puis remettre l'aire de lavage au propre (rincer, balayer vers le caniveau).

**La mousse** (détergent ou canon) apparaît exactement là où elle est projetée (disque pour le détergent, éventail pour le
canon), uniquement sur la surface visée. Laissée en place, elle détache lentement les taches qui réagissent au savon puis
retombe en eau ; brossée, elle monte et rend la brosse plus efficace ; le rinçage et l'extracteur l'évacuent. Le cercle de
visée devient une ellipse pour les outils en éventail : il montre toujours la zone exacte traitée.
- Eau : poste d'eau (mur gauche). Détergent : poste produits (mur gauche, bidon vert).
  Cuve pleine : la vider au **caniveau** de l'aire de lavage. Touche E dans les trois cas.
- Un rinçage trop bref laisse des **résidus de produit** (voile pâle visible, compté dans l'inspection).
- L'extracteur est une **abstraction** : l'humidité retirée est convertie en litres
  (3 L par m² de tapis saturé) et versée dans la cuve.
- **F** : comparaison avant/maintenant et statistiques (saleté retirée, surface propre, humidité, mousse, résidus).
- **R — scan de saleté** : une onde part du joueur et teinte pendant 6 s les zones **réellement** sales (orange, plus
  marqué là où il reste beaucoup de saleté) et les zones propres mais chargées de **résidus** (bleu) ; les zones
  propres ne sont jamais signalées (même seuil que le calcul de propreté). Recharge de 10 s, indiquée en bas à droite.
  Durée, recharge, vitesse de l'onde et couleurs : composant `DirtScanner` sur `[Game]`.
- **Cercle de visée** : vert quand la zone visée est déjà propre, bleu pendant l'utilisation, rouge hors de portée ;
  il « pulse » au premier contact avec le tapis. Les paliers de propreté (25, 50, 75, 90 %) sont annoncés
  (son, message, panneau qui pulse) et l'objectif atteint déclenche une pluie d'éclats sur le tapis.

### Salissures

Chaque type a ses réponses réelles à chaque traitement (`DirtTypeTable.DefaultResponse`, modifiables dans
`ScriptableObjects/DirtTypes`) : aspiration, brossage sec / humide / savonneux, rinçage, balai. Les salissures **sèches**
(poussière, débris, amas, poils, peluches) vivent dans le canal poussière d'une cellule ; les autres sont des **taches
fixées** à détacher puis rincer.

| Salissure | Comportement |
|---|---|
| Poussière légère | S'aspire très bien ; voile mat |
| Petits débris | Aspirateur ou **balai** (×1,2) ; grains sombres |
| Amas de poussière | Moitié moins bien aspirés que la poussière, à brosser ou balayer ; grains gris |
| Poils d'animaux | L'aspirateur seul peine (×0,45) : **brosse à sec** (×0,9) ou **balai** (×1,3) ; brins bruns |
| Peluches et fibres | S'aspirent très bien (×1,3) ; brins clairs |
| Terre, boue humide | Brosse humide ou savonneuse puis rinçage |
| Boue sèche | **Se brise à sec** (brosse sèche ×0,9) puis s'aspire ou se rince ; adhérence 0,2 |
| Taches de boissons | Savon et action de la mousse ; adhérence 0,15 |
| Taches grasses | Seulement au savon ; adhérence 0,25 |
| Taches incrustées | Les plus tenaces (savon ×0,45) ; adhérence 0,5 |

**Adhérence** (0..1, par cellule) : valeur du type, modulée par chaque couche de saleté (`adhesionOffset`, ex. graisse
« bien accrochée » d'une commande). Elle freine brossage et rinçage (jusqu'à −75 %) et l'action de la mousse (jusqu'à
−50 %), et se voit : une tache incrustée a un bord plus net et une teinte plus sombre. Elle est sauvegardée avec les
surfaces de l'atelier.

### Produits spécialisés

Achetés à la boutique (onglet **Produits**), stockés en litres, sélectionnés avec **T**. Le produit actif est **ajouté**
au détergent (pulvérisateur et canon à mousse) et consommé à son débit propre ; un flacon vide repasse au détergent
standard (message à l'écran). Effets réels, uniquement sur les salissures ciblées et tant que la mousse est présente :

| Produit | Prix / flacon | Cibles | Brossage | Action au repos | Résidus | Agressivité |
|---|---|---|---|---|---|---|
| Dégraissant | 90 € / 2 L | Taches grasses | ×2,5 | ×3 | ×1,3 | 0,8 |
| Détachant enzymatique | 110 € / 1,5 L | Boissons, taches incrustées | ×2 | ×4 | ×1 | 0 |
| Shampooing soie et laine | 70 € / 2 L | Poussière, amas, terre, boue, boissons | ×1,3 | ×1,2 | **×0,5** | 0 |
| Décolleur de boue | 80 € / 2 L | Boue sèche, boue, terre | ×2,2 | ×2 | ×1,1 | 0,3 |

Un produit **agressif** laissé en mousse sur un tapis **fragile** use ses fibres. Le texte de la boutique est généré à
partir de ces valeurs (`ProductTable.Describe`) : il n'annonce rien que le code ne fasse. Stock maximal par produit.

### Tapis de soie (luxe)

« Tapis de soie de Hereke » (3,2 × 2,2 m, 9 mm, motif généré, reflet soyeux) — **fragilité 0,85** :
- brosser **à sec** use les fibres (deux fois plus sur une zone déjà propre ; la brosse de détail est plus abrasive) ;
  sous la mousse ou sur fibres humides, pas d'usure ;
- un produit agressif (dégraissant, décolleur de boue) laissé en mousse les use aussi ;
- l'usure se voit (couleurs ternies, éclat et relief perdus), le HUD avertit « Attention : fibres abîmées » et la commande
  « Tapis de soie du grand salon » (Résidence Valmont, 950 €, réputation 8, ★★★★) exige **usure ≤ 4 %**, propreté 90 %,
  résidus ≤ 8 %, humidité ≤ 25 % (critère « Fibres abîmées », poids 15 %). Le tableau des commandes signale la fragilité.

## 4 ter. Commandes, économie et boutique

1. **Comptoir de réception** (coin avant droit, E) : liste des commandes avec client, tapis, difficulté (★),
   récompense, objectifs et état (*Disponible*, *En cours*, *Terminée*, *Verrouillée* par la réputation).
   « Accepter » charge le tapis de la commande (saleté propre à la commande) et lance le chronomètre.
2. Nettoyer, puis revenir au comptoir : « Livrer la commande » (une estimation de la note est affichée en direct)
   ou « Abandonner ». La pause propose aussi « Recommencer la commande » et « Abandonner la commande ».
3. **Évaluation** (`OrderEvaluator`, règles centralisées et testées) — uniquement sur les données de la grille :
   - saleté retirée / objectif (poids 70 %) ; résidus de produit et humidité (15 % chacun) **seulement si la
     commande les exige** ; bonus de rapidité (+15 % de la récompense de base) si la note est au moins B ;
   - notes S (≥ 95 et tous les objectifs), A (≥ 85), B (≥ 70), C (≥ 55), D (≥ 40) ;
     **sous 40 : le client refuse**, aucun paiement, la commande redevient disponible ;
   - paiement = récompense × (S 1,25 · A 1 · B 0,8 · C 0,55 · D 0,3) ; réputation S +3, A +2, B +1, C 0, D/refus −1.
4. **Une récompense n'est versée qu'une fois** : une commande terminée ne peut être ni relivrée ni refaite.
5. **Écran de résultats** : note, critères mesurés (✔/✘), paiement, réputation, images avant/après,
   « Retour à l'atelier » ou « Choisir une autre commande ».
6. **Boutique** (meuble « Matériel & boutique », E) : 9 améliorations avec prix et **effet réel**
   (valeur actuelle → après achat) : réservoirs d'eau / détergent / cuve, buse économe (−30 % de produit),
   puissance aspirateur, brosse, lance, extracteur. Achat refusé (solde inchangé) si fonds insuffisants ou
   amélioration précédente manquante.

   Onglet **Produits** : produits spécialisés (prix par flacon, stock actuel / maximum ; achat refusé si fonds
   insuffisants ou si un flacon entier ne tient plus dans le stock).
7. **Nouvelles commandes** (jalon 3) : pension canine (poils, amas, boissons), atelier de couture (peluches, débris,
   graisse accrochée, résidus ≤ 15 %), bureau de chantier (boue sèche, amas, terre, résidus ≤ 20 %), grand salon
   (tapis de soie, voir ci-dessus). Le tableau est une liste défilante (molette, barre à droite).

## 4 sexies. RugDex

Collection des tapis (menu principal ou pause → « RugDex »). Les entrées viennent du catalogue
`ScriptableObjects/RugCatalog` (aucune entrée codée en dur) :
- tapis **découvert** dès qu'il est chargé dans l'atelier (avec une commande) ; message « Nouveau tapis dans le RugDex » ;
- fiche : visuel, numéro, nom, catégorie (type de velours), dimensions et épaisseur, fragilité des fibres, salissures
  rencontrées (couches du tapis et de ses commandes), commandes associées, nombre de livraisons et meilleure note
  (commandes acceptées uniquement), description ;
- tapis **inconnu** : silhouette sombre et condition réelle de découverte (commande à accepter, réputation requise) ;
- complétion « x / y tapis (p %) ». Pas de rareté ni de bonus : rien de tel n'existe dans le jeu.

Contenu modifiable sans code : `ScriptableObjects/Orders` (commandes), `ScriptableObjects/Upgrades` (prix, valeurs),
`ScriptableObjects/Products` (produits) et `ScriptableObjects/RugCatalog` (RugDex).

**Créer une commande** : `Create → RugWash → Commande`, renseigner tapis, couches de saleté et graine,
difficulté, récompense, réputation requise, objectifs ; l'ajouter à la liste `orders` de `[Game] → OrderManager`.

## 4 quinquies. Aire de lavage (zone de nettoyage) et robot aspirateur

- **Pas de tapis d'entraînement** (v0.1.2) : l'atelier démarre sans tapis ; un tapis n'apparaît qu'en acceptant une
  commande au comptoir et repart à la livraison ou à l'abandon.
- **Plan de travail de l'aire de lavage** — la **seule zone de nettoyage** de la salle : ce qui ruisselle du tapis s'y dépose
  réellement, autour du tapis et du côté où l'on travaille — eau sale et mousse évacuées par le rinçage, débris chassés par
  le balai. Il se nettoie avec les mêmes outils (lance, balai qui pousse l'eau et la boue, extracteur) ; l'eau claire sèche
  seule. Le HUD affiche en permanence « Aire de lavage : x % propre ».
- **Seuils** (convention : saleté = part de la surface souillée, propreté = 1 − saleté) : sous **25 % de propreté**, aucune
  nouvelle commande ne démarre (le comptoir et le HUD expliquent pourquoi) ; au-delà de **40 % de saleté**, le tapis posé
  dessus se salit (bordure recontaminée à la pose, puis en continu tant que l'aire reste trop sale).
- **Tapis terminé** : quand il reste moins de 1 % de la saleté initiale, le reliquat est retiré — propreté 100 %,
  « Tapis terminé », objectif atteint ; séchage et résidus restent notés à la livraison.
- **Traces de pas** : marcher dans la boue, l'eau ou la mousse charge les semelles, qui laissent des empreintes de plus en
  plus légères sur l'aire de lavage. Ni les tapis ni le sol de la salle ne sont salis par les pas.
- **Sol de la salle** : hors zone de nettoyage, il ne se salit jamais (ni poussière, ni eau, ni mousse) et n'est pas une
  cible des outils.
- **Robot aspirateur** (base dans le coin arrière gauche de l'aire de lavage) : **E** sur le robot ou sa base pour le
  démarrer ou l'arrêter. Il aspire réellement poussière, débris et traces sèches de l'aire (pas l'eau), choisit les zones
  sales par longues lignes, contourne bordures, objets et le tapis posé (grille d'occupation recalculée à chaque tapis +
  chemin A*), ne quitte jamais l'aire, s'arrête devant le joueur, rentre seul se recharger puis repart.
  Voyant : vert = nettoyage, bleu = retour, ambre = charge, blanc = prêt, rouge clignotant = obstacle.
- **Réglages** centralisés : `ScriptableObjects/CleaningTuning` (mousse, ruissellement, seuils de l'aire, fin du tapis, effets
  visuels : densité, durée de vie et taille maximale à l'écran des particules, distance des effets des autres joueurs),
  `Surface_WorkSurface` (taille, résolution, séchage), composant `RobotVacuum` (vitesse, rayon, puissance).

## 4 septies. Multijoueur coopératif

Jusqu'à 4 joueurs dans le même atelier, sur les mêmes tapis. Détails (architecture, configuration Steam, procédure de
test, limites) : [Assets/RugWash/Documentation/MULTIPLAYER.md](Assets/RugWash/Documentation/MULTIPLAYER.md).

- **Menu principal → Multijoueur** :
  - **Steam (Internet)** : créer un salon (amis ou public), inviter des amis par Steam, rechercher et rejoindre une partie, lancer
    la partie. Steam (lancé et connecté avant le jeu) est initialisé dès le menu principal : une invitation acceptée pendant
    une partie solo sauvegarde la partie puis rejoint le salon.
  - **Réseau local** : « Héberger sur ce PC » ou « Rejoindre » une adresse IPv4:port (port UDP 7777 à autoriser chez l'hôte).
- **Progression personnelle** (v0.1.2) : chaque joueur garde **sa** carrière (argent, réputation, achats, produits, RugDex,
  commandes livrées), sauvegardée chez lui. L'hôte la valide pendant la partie (achats payés avec l'argent de l'invité,
  récompense complète versée à chaque joueur présent) ; elle n'est jamais remplacée par celle de l'hôte. L'atelier
  (tapis, aire de lavage, robot, tableau des commandes) est celui de l'hôte et n'est sauvegardé que chez lui.
- **Arrivée d'un invité** : « Connexion à l'atelier de l'hôte — synchronisation… » (contrôles coupés) jusqu'à réception de
  l'atelier et de sa carrière validée ; le HUD affiche ensuite directement ses propres valeurs.
- **Autres joueurs** : même modèle d'outil détaillé que le sien, posé sur le point qu'ils visent, jet depuis leur buse.
- Tout ce qui compte est validé par l'hôte : nettoyage (les invités voient leur effet immédiatement, puis se recalent sur
  l'hôte), commandes, livraison et paiement (une seule fois), achats, robot. Chaque joueur garde sa caméra, ses contrôles et
  ses réservoirs. La pause ne fige pas la partie. La liste des joueurs (et la latence) s'affiche en haut à droite.
- « Quitter la partie » (pause) : un invité revient au menu ; l'hôte ferme la partie pour tous. Après une déconnexion, le
  menu affiche la raison et propose « Se reconnecter ».

## 4 bis. Audio

Les 33 sons (outils et **contact avec le tapis** propres à chaque outil, prise en main, interface, remplissage,
vidange, éclat de zone propre, scan, paliers, objectif, paiement, ambiance, pas, robot aspirateur) sont **générés par synthèse**
(`RugWashAudioGenerator`) dans `Assets/RugWash/Audio/Generated/tmp_*.wav` : aucune licence tierce. Ce sont des
**substituts temporaires** clairement préfixés `tmp_`, à remplacer en changeant le clip assigné.
Inventaire complet, règles de lecture (catégories, pause, anti-empilement) et licences :
[`Documentation/AUDIO.md`](Assets/RugWash/Documentation/AUDIO.md).

## 4 quater. Rendu et présentation

- URP Forward+, **SSAO** (occlusion ambiante), ombres douces du projecteur principal, sonde de réflexion,
  étalonnage (ACES, balance des blancs chaude, saturation, bloom, vignette).
- Outils tenus en main animés : balancement à la souris, balancier de marche, inertie lors des changements de
  direction, montée de l'outil (avec son) à chaque changement (`ViewModelRig`), plus l'animation propre à chaque outil
  (brossage, vibration, recul) et un petit choc au premier contact avec le tapis.
- **Alignement sur la cible** : chaque outil est orienté pour que son extrémité active (tête, buse) pointe vers le
  point visé par le réticule (`CleaningToolBase.aimTip`, rotation lissée et bornée). Sans cette correction, les
  outils visaient de 7° (détergent) à 35° (brosse) sous la cible ; l'écart mesuré est désormais ≤ 0,05°.
- **Caméra** : léger balancier de marche et inclinaison en pas chassé (`CameraMotion`), réglables avec le
  balancement des outils ; le rayon de visée part toujours du centre de la caméra (testé pendant la marche).
- Débris de brossage teintés par la couleur réelle de la saleté détachée ; eau d'éclaboussure trouble tant qu'elle
  emporte de la saleté, claire sur une zone propre.
- Atelier habillé : tapis à sécher, tapis roulés en attente, poste d'eau avec flexible enroulé, fût de détergent,
  étagères, établi à tiroirs, panneau d'outils, comptoir avec écran, poutres, conduits, horloge à l'heure réelle,
  affiche « Les 5 étapes » (aide en jeu), marquage au sol.

## 5. Architecture

```
Assets/RugWash/
  Scripts/                 (assembly RugWash.Runtime)
    Core/        GameBootstrap (racine de composition), GameStateManager (états explicites, curseur), InputDeviceMonitor
                 (périphérique actif), utilitaires
    Cleaning/    Grille de nettoyage, opérations, générateur de saleté, outils, définitions (ScriptableObjects),
                 produits spécialisés (ProductDefinition, ProductLibrary → ProductTable, ProductStockBook)
    RugDex/      RugCatalog (catalogue), RugDexBook (progression, testée), RugDexService (découvertes), RugDexUI
    Audio/       AudioManager (volumes, pause, sons d'interface, voix ponctuelles réutilisées)
    Effects/     RugDirtRenderer (grille → textures de contrôle), capture avant/après, indicateur de visée,
                 CleaningAudioController (boucle par outil), CleaningVFXPool, WaterEffectController (sol mouillé)
    Player/      Entrées (clavier/souris et manette, réponse des sticks, libellés de touches), contrôleur FPS, outils du joueur,
                 interactions, équipement
    UI/          HUD, barre d'outils, menu pause, inspection, navigation sans souris (UINavigator, NavigationScope),
                 libellés de touches dynamiques (InputPromptText)
    Orders/      OrderDefinition, OrderBook (états), OrderEvaluator (note), OrderManager (cycle de commande)
    Economy/     Wallet, WalletManager, ReputationManager, UpgradeDefinition, EquipmentStats, ShopRules,
                 UpgradeManager (applique les effets), ShopManager
    Save/        SaveData (format versionné), SaveMigration, SaveFileStore (écriture atomique, secours),
                 SaveManager (chargement / sauvegarde), SettingsManager (application des paramètres)
    Network/     Multijoueur : MultiplayerSession (session, NetworkManager, Steam / connexion directe), SteamPlatform,
                 SteamSocketsTransport, NetworkWorkshop (atelier partagé), NetworkPlayerAvatar, réplication des surfaces
    Diagnostics/ Overlay F3, mesure des temps de frame, vérifications automatiques du build (solo et multijoueur)
  Editor/                  (assembly RugWash.Editor) bootstrap projet, génération de textures/contenu/scène, CI
  Shaders/                 RugSurface (URP PBR), TargetRing, ParticleSoft
  ScriptableObjects/       Rugs/, Tools/, DirtTypes/ — données modifiables dans l'Inspector
  Tests/Editor, Tests/PlayMode
```

Principes :
- **Aucun singleton global.** `GameBootstrap` reçoit des références explicites (câblées par le
  générateur de scène) et injecte les dépendances (`Bind(...)`). Chaque composant vérifie ses
  références (`IsConfigured`) et signale explicitement ce qui manque.
- **États de jeu explicites** (`MainMenu`, `Workshop`, `ActiveOrder`, `Results`, `Paused`) + compteur
  de fenêtres modales. Règles de curseur/temps pures dans `GameStateRules` (testées).
- **Données séparées du rendu.** `RugCleaningGrid` (C# pur) est la source de vérité ; le shader ne fait
  qu'afficher. Le pourcentage affiché est calculé depuis la grille.

## 6. Système de nettoyage

- Grille par tapis (`cellsPerMeter`, 64 par défaut → 192×128 cellules pour 3 m × 2 m).
- Chaque cellule : poussière, saleté fixée (+ type), saleté détachée, mousse, humidité, résidus, usure.
- Les outils appliquent des **opérations struct** (`BrushOperation`, `RinseOperation`) sur un disque
  (rayon en mètres, atténuation `1 − (d/r)^dureté`). Les quantités transférées sont bornées par la
  saleté présente : **aucune progression sur une zone propre**.
- Chaque type de saleté a des réponses (`DirtResponse`) : aspiration, brossage sec/humide/savon, rinçage.
- **Propreté** = part de la saleté initiale retirée, tenue à jour incrémentalement (recalcul complet à l'inspection).

### Rendu
`RugDirtRenderer` encode la grille dans quatre textures RGBA32 (`ControlA` = poussière, tache, détaché,
mousse ; `ControlB` = couleur de la tache, humidité ; `ControlC` = résidus de produit, usure, adhérence ; `ControlD` =
couleur et aspect de la salissure sèche — voile, grains ou brins). Seul le **rectangle modifié** est réencodé,
directement dans la mémoire de la texture, et l'envoi GPU est limité à 30/s. Le shader
`RugWash/Rug Surface` superpose saleté, boue, poussière, humidité et mousse **au motif d'origine**
(jamais remplacé) ; un bruit de détail découpe les seuils pour une frontière propre/sale organique.
Compatible tout GPU DX11 (pas de compute shader).

## 7. Ajouter du contenu

**Un tapis** : `Create → RugWash → Tapis` dans `ScriptableObjects/Rugs`. Renseigner taille, épaisseur,
texture de motif (couleur, *Clamp*), normale de fibres (tuilable), couches de saleté (type, motif,
couverture, intensité, échelle, graine). L'ajouter à la liste `availableRugs` de `[Game] → GameBootstrap`.

**Un type de saleté** : ajouter une valeur à l'enum `DirtType` (sans renuméroter, mettre à jour `DirtTypes.Count`), créer
`Create → RugWash → Type de saleté` (réponses, couleur, adhérence, sensibilité aux produits), l'ajouter à `DirtTypeLibrary`.

**Un produit** : `Create → RugWash → Produit` (cibles, multiplicateurs, résidus, agressivité, débit, flacon, prix, stock
maximal), l'ajouter à `ScriptableObjects/ProductLibrary`. Il apparaît dans la boutique et se sauvegarde sans code.

**Une entrée du RugDex** : ajouter le `RugDefinition` (avec sa description) à la liste `rugs` de `ScriptableObjects/RugCatalog`.

**Un outil** :
1. Créer `Create → RugWash → Outil` (portée, rayon, dureté, puissance, ressource, emplacement).
2. Écrire une opération `struct XxxOperation : ICellOperation` (règle de gameplay, testable).
3. Dériver `CleaningToolBase` : `ApplyToRug` appelle `rug.ApplyOperation(...)` et retourne le nombre de
   cellules devenues propres. Eau et détergent sont consommés automatiquement selon `resource` ;
   surcharger `CanOperate` / `ConsumeResources` / `UpdateEffects` pour un comportement spécifique.
   Son : ajouter un `CleaningAudioController` et l'assigner à `toolAudio`.
4. L'ajouter à la liste `tools` de `PlayerToolController` (le générateur de scène le fait pour les outils existants).

**Une commande** : voir § 4 ter.

## 8. Sauvegarde

Fichier JSON local : `%USERPROFILE%\AppData\LocalLow\RugWash Studio\RugWash Simulator\rugwash_save.json`
(`Application.persistentDataPath`).

```json
{
  "version": 1, "profileId": "profile0", "savedAtUtc": "2026-10-11T04:10:00Z",
  "money": 418, "reputation": 3,
  "upgrades": ["up_water_1"], "completedOrders": ["order_salon"],
  "ordersDelivered": 1, "totalEarned": 168,
  "settings": { "masterVolume": 0.9, "effectsVolume": 0.8, "interfaceVolume": 0.7, "mouseSensitivity": 0.1,
                "invertY": false, "qualityLevel": -1, "resolutionWidth": 0, "resolutionHeight": 0,
                "fullScreenMode": -1, "bindingOverrides": "" }
}
```

- **Quand** : après une livraison, après un achat, à la fermeture de la fenêtre des paramètres, au retour au menu
  principal et à la fermeture du jeu. Jamais à chaque frame.
- **Écriture sûre** : fichier temporaire écrit, relu et validé avant de remplacer la sauvegarde ; la sauvegarde
  précédente devient `rugwash_save.json.bak`. Des données invalides (solde négatif, identifiant vide…) ne sont
  jamais écrites.
- **Récupération** : fichier illisible → copie de secours restaurée ; les deux illisibles → nouvelle partie,
  sans blocage. Les fichiers endommagés sont conservés (`*.corrupt-<date>`).
- **Versions** : `SaveMigration` convertit les anciens formats (v0 → v1) ; une sauvegarde d'une version plus
  récente du jeu n'est ni chargée ni écrasée.
- **Pas de double récompense** : le paiement et le passage de la commande à « Terminée » sont sauvegardés ensemble ;
  au chargement, le solde est restauré tel quel et la commande ne peut pas être refaite.
- **Commande en cours non sauvegardée** : quitter ou revenir au menu pendant une commande l'abandonne (elle reste disponible).
- **Atelier** (section `workshop`) : mode, batterie et position du robot (il reprend là où il était s'il travaillait),
  état du plan de travail et du sol (grilles quantifiées, compressées puis encodées en Base64 ; une grille de taille
  différente ou illisible est ignorée). Ajout compatible : une sauvegarde antérieure se charge avec un atelier propre et
  le robot sur sa base.
- **Manette** (dans `settings`) : `gamepadLookSpeed`, `stickDeadZone`, `vibration` ; les boutons reconfigurés sont dans
  `bindingOverrides` avec les touches (les index des liaisons clavier existantes sont inchangés : les touches déjà
  reconfigurées restent valables). Ancienne sauvegarde : valeurs par défaut.
- **Produits** (`products` : identifiant + litres, `activeProduct`) et **RugDex** (`rugDex` : identifiant, découvert,
  livraisons, meilleure note). Champs ajoutés sans changer de version : une ancienne sauvegarde se charge sans produit et
  avec le RugDex vide (le tapis présent est alors découvert). Identifiants inconnus ignorés, valeurs bornées.
- Surfaces de l'atelier : format v2 (adhérence et produit par cellule) ; le format v1 se relit.
- Pas de sauvegarde cloud, de synchronisation ni de protection contre la modification du fichier.

## 9. Tests

```bash
powershell -ExecutionPolicy Bypass -File "Tools/RugWash-CI.ps1" -Step all
```

Étapes : `compile`, `bootstrap`, `scene` (atelier + menu principal), `test` (EditMode, sans GPU),
`playmode` (avec GPU, produit des captures dans `Logs/ci/screens`), `build` (exécutable Windows),
`benchmark` (lance le jeu compilé en mode vérification) et `multiplayer` (deux exemplaires du jeu compilé, hôte et invité,
en connexion directe locale : scénario coopératif automatique, rapports comparés). `all` enchaîne les cinq premières étapes.
État actuel : 170 tests EditMode et 100 tests PlayMode, tous réussis, et 32 / 32 vérifications multijoueur (scénario du jalon 1) (détail : `Assets/RugWash/Documentation/VALIDATION.md`). Les tests utilisent un dossier de
sauvegarde temporaire et ne touchent jamais la sauvegarde réelle. Résultats NUnit XML dans `Logs/ci/`.
Fermer l'éditeur Unity avant (le mode batch refuse un projet déjà ouvert).

Dans l'éditeur : *Window → General → Test Runner*.

## 10. Build Windows

**Dans l'éditeur** : menu **RugWash → Build → Build Windows (64 bits)**, ou *File → Build Profiles → Windows*
(scènes : `MainMenu` puis `Workshop`).

**En ligne de commande** (Unity fermé) :

```bash
powershell -ExecutionPolicy Bypass -File "Tools/RugWash-CI.ps1" -Step build
```

Résultat : `Builds/Windows/RugWashSimulator.exe` (≈ 120 Mo, Mono, Direct3D 11/12 et Vulkan).
Pour distribuer, copier tout le dossier `Builds/Windows` **sauf** `RugWash Simulator_BurstDebugInformation_DoNotShip`.

**Vérification automatique du build** : `-Step benchmark` lance l'exécutable en 1920×1080 avec l'argument
`-rugwash-benchmark <rapport.json>`. Le jeu enchaîne seul : menu → nouvelle partie → commande → nettoyage → livraison
→ sauvegarde → changement de résolution et de mode d'affichage → retour au menu, mesure les temps de frame dans
quatre scénarios (vSync désactivé), écrit `Logs/ci/benchmark.json` puis se ferme. La sauvegarde utilisée est
temporaire : celle du joueur n'est jamais modifiée. Sans cet argument, le build se comporte normalement.

## 11. Limites connues

Voir la section « Limites » de [PRODUCTION.md](Assets/RugWash/Documentation/PRODUCTION.md).
