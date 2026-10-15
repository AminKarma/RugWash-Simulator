# Update v0.1.2 — audit (jalon 0) et décisions des jalons 1 et 3

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

## 5. Décisions du jalon 3 (ordinateur et économie)

### Kit de départ (2.4)
- Nouvelle partie : **brosse**, **détergent** (réserve de 4 L pleine + 12 L en stock) et **seau d'eau** (nouvel outil,
  touche 9 : rinçage de près, peu puissant, même moteur que la lance). Les autres outils ne sont pas supprimés : ils
  demandent une amélioration « déblocage d'outil » achetée à la boutique (aspirateur 150 €, balai 110 €, lance 220 €,
  extracteur 320 €, canon à mousse 380 € ; la brosse de détail restait déjà à acheter). Les améliorations d'un outil
  exigent cet outil.
- Sauvegardes : format **v2**. Migration v1 → v2 prudente : une partie commencée (argent, réputation, achats ou commandes)
  **garde tous ses outils** (déblocages ajoutés) et reçoit un bidon de 40 L ; une sauvegarde v1 vierge devient une
  nouvelle partie avec le kit. v0 → v1 → v2 enchaînés.

### Détergent consommable (3.1)
- Unité unique : le **litre**. Un bidon de 40 L est un **stock de 40 L consommé progressivement** (pas 10 recharges
  fixes) : le poste détergent remplit la réserve emportée avec ce qui manque, dans la limite du stock (remplissage
  partiel possible). Prix 45 €, stock entreposé ≤ 160 L ; réglages sur `DetergentSupply`.
- Affichage : « 2,4 / 4 L · stock 12 L » (HUD), invite du poste (« … · stock X L »), niveau de liquide dans le contenant
  du pulvérisateur et du canon à mousse (shader `RugWash/Liquid Container`, une valeur `_Fill` envoyée seulement quand
  elle change). Réserve vide : outil bloqué, raison affichée, son de refus `tmp_ui_deny`.
- Progression personnelle : stock et réserve sauvegardés chez chaque joueur ; en ligne, achat d'un bidon et
  remplissage au poste sont demandés à l'hôte, qui débite la carrière du joueur demandeur (stock borné à l'arrivée).

### Ordinateur (2.1)
- L'écran du bureau devient l'**ordinateur de l'atelier**, seul accès aux commandes et aux achats : exactement deux
  icônes, « Missions / Commandes » (tableau existant, amélioré) et « Boutique » (boutique existante, défilante, outils à
  acheter en tête). Le meuble à outils n'ouvre plus la boutique (panneau « MATÉRIEL ») ; la livraison et l'abandon
  restent dans l'application Missions (pas de second guichet).

### Missions infinies (2.2)
- Les 10 contrats du catalogue restent (une fois chacun). En plus, **trois missions générées** : deux accessibles, une
  du palier suivant (objectif visible). Rien n'est généré d'avance : chaque mission livrée est remplacée par la suivante.
- Paliers de réputation : 0, 3, 8, 15, 24, 35… (`t × (t + 2)`), soit une à deux missions réussies de plus par palier.
  Le palier augmente la récompense, le nombre et l'étendue des salissures, la propreté exigée et ouvre les tapis
  précieux (oriental au palier 1, soie au palier 3).
- Pas de mission impossible : objectif de séchage seulement si l'extracteur est acheté, limite d'usure seulement sur un
  tapis fragile, résidus à partir du palier 1 (rinçage possible au seau). Pas de doublon immédiat : catégorie et tapis
  différents des offres affichées et de la dernière mission livrée.
- Déterminisme : identifiant `gen.graine.numéro.palier.outils.variante` ; graine tirée à la création de la partie.
  Sauvegardés : graine, numéro suivant, offres, dernière livrée. Une mission en cours au moment de quitter revient,
  inchangée, comme offre disponible (l'état du tapis n'est pas sauvegardé).
- Réputation en baisse : au plus une offre verrouillée ; les autres sont remplacées par des missions accessibles
  (jamais la commande en cours).
- En ligne : le tableau est celui de l'hôte (outils et réputation de l'hôte) ; les invités régénèrent les offres depuis
  leurs identifiants. Gains et réputation : comme au jalon 1, pour chaque joueur présent, dans sa carrière.

### Actualités (2.3)
- Source : `Resources/RugWashNews.json`, livré avec le jeu, format versionné (`format: 1`, titre, date, version, texte).
  Aucune source distante n'existe : le menu les présente comme des **annonces locales**, sans simuler de connexion.
- Lecture asynchrone et tolérante (fichier absent ou invalide, entrées sans titre ignorées, textes tronqués, balises
  neutralisées) ; au plus trois annonces, les plus récentes d'abord.

| Catégorie (ajouts v0.1.2 jalon 3) | Propriétaire | Autorité en ligne | Sauvegardé chez |
|---|---|---|---|
| Stock de détergent en bidons, réserve emportée | Chaque joueur | Hôte (achats et remplissages) | Chaque joueur |
| Offres de missions générées (graine, numéro, offres) | Session (atelier de l'hôte) | Hôte | Hôte |
| Outils débloqués (achats) | Chaque joueur | Hôte | Chaque joueur |

- Version de protocole passée à `rugwash-coop-4` (carrière avec stock de détergent, offres de missions).

## 6. Décisions du jalon 4 (outils et salissures)

Audit : les poils, cheveux, peluches, amas et débris existaient déjà comme salissures sèches avec leurs réponses propres
(aspirateur, balai, brosse) ; les traces de pas existaient sur l'aire de lavage ; le robot aspirateur existait, gratuit.
Ces systèmes sont réutilisés et complétés, pas refaits.

- **Poils et débris (3.2)** : outils adaptés déjà en jeu (balai à sec, brosse, aspirateur) ; ajout d'un accessoire
  d'aspiration (brosse turbo anti-poils, ×2 sur poils, cheveux et peluches, 130 €) qui passe par l'opération d'aspiration
  existante (paramètre transmis en ligne).
- **Raclette (3.3)** : nouvel outil (touche 0, 90 €). Transport réel des données de la surface : ramassage pondéré sous la
  lame (60 % par image), redépôt de la même quantité sur une zone identique juste devant, dans le sens du déplacement du
  point visé ; au caniveau, évacuation. Immobile : rien ; contre la bordure : blocage ; sur un tapis : rien. L'effet visible
  est l'eau elle-même (rendu de l'aire), plus quelques gouttes bornées devant la lame et au caniveau. Le balai garde son
  comportement (testé) ; la raclette est l'outil qui amène vraiment l'eau au caniveau.
- **Monobrosse (3.4)** : nouvel outil (molette ou LB / RB, 650 €), sans consommable. Détache (opération de brossage
  existante, puissance 1,3, rayon 0,3 m) avec une efficacité de 35 % à sec et 100 % sur fibres humides ou savonneuses ;
  fait monter la mousse ×3 par rapport à la brosse ; n'ajoute ni ne retire d'eau ; use deux fois plus les fibres fragiles
  à sec. Marche ralentie à 55 % quand elle tourne. Le modèle est posé sur le point visé comme les autres outils de contact
  (pas de collision physique de la vue à la première personne).
- **Bottes (3.5)** : amélioration (90 €) : 30 % de la boue des semelles transférée (sans l'annuler). Affichée dans la
  boutique (« Boue laissée par vos pas : 100 % → 30 % »).
- **Robot achetable (3.6)** : amélioration « équipement d'atelier » (650 €). Non acheté : robot caché et inerte, sa base
  indique où l'acheter. Sauvegardes v3 : une partie commencée avant ce jalon garde le robot (il était gratuit). En ligne :
  c'est l'achat de l'hôte qui compte (son atelier). Le robot reste sur sa base fixe (pas de placement libre).
- **Traces boueuses (4.4)** : aussi sur le tapis de la commande (boue seulement, ×0,5), une opération par pas (aucun objet,
  rien à chaque image), réduites par les bottes ; jamais sur le sol de la salle.
- **Sauvegarde** : un achat inconnu du catalogue du build (contenu pas encore généré, version plus ancienne) est désormais
  conservé au lieu d'être effacé.
- Onze outils : touches 1 à 9 et 0, le onzième à la molette (et LB / RB).
- Protocole `rugwash-coop-5`.

## 7. Décisions du jalon 5 (base et personnage)

Audit : l'atelier était une salle unique au décor fixe ; le seul équipement d'atelier était le robot (jalon 4) ; la
silhouette réseau (jalon 5 de la roadmap V2) était faite de primitives procédurales sans variantes ; aucune musique ;
aucune physique de personnage (capsule `CharacterController` seulement). Réutilisés : catalogue d'améliorations et
boutique de l'ordinateur, opérations de grille (`ApplyAllTracked`, réplication lente de l'hôte), état d'atelier publié par
l'hôte, sauvegarde versionnée, générateurs procéduraux (maillages, textures, sons).

- **Machines (5.1)** : trois « équipements d'atelier » achetés à l'onglet **Atelier** de la boutique, cachés avant l'achat,
  actionnés par E, chacun avec une seule fonction mesurable et jamais un nettoyage complet :
  - *Plaque vibrante de dépoussiérage* (350 €, réputation 3) : un cycle de 8 s **par commande** ; fait tomber 55 % de la
    poussière, des poils et des débris **secs** (rien sur un tapis mouillé, rien sur les taches ni la boue détachée).
  - *Souffleur de séchage* (500 €, réputation 8) : marche / arrêt ; retire 0,03 d'humidité par seconde (les tapis sèchent
    seuls de 0 à 0,02 / s selon le modèle) ; ne retire ni saleté, ni résidus, ni mousse.
  - *Rampe de pré-mouillage* (750 €, réputation 15) : un cycle de 8 s par commande ; humidifie à 55 % et fait tremper
    20 % des taches (selon leur réponse à l'eau) en saleté détachée — **rien n'est retiré** : il faut brosser et rincer.
  - Intégration : nécessite le tapis d'une commande en cours (un cycle par commande), réputation requise vérifiée par la
    même règle d'achat partout (`ShopRules`, carrière de chaque joueur chez l'hôte), sauvegarde (achats), réseau (l'hôte
    décide et publie possession + marche ; chaque poste reproduit l'effet, l'hôte corrige par la réplication lente ; un
    invité demande l'activation à l'hôte). Pas de « machine à laver » qui nettoierait le tapis : elle contredirait la règle
    « jamais un nettoyage complet » et le cœur du jeu.
- **Paliers de décor (5.2)** : cinq paliers séparés des machines (améliorations décoratives à part, onglet Atelier) :
  0 *Cagibi* (départ : cartons, fenêtres condamnées par des planches, ampoule nue, taches au mur, lumière à 60 %),
  1 *Atelier rangé* (400 €, rép. 3), 2 *Atelier professionnel* (1 200 €, rép. 15), 3 *Loft industriel* (3 000 €, rép. 35),
  4 *Penthouse — vue sur Los Angeles* (7 500 €, rép. 63 : baies panoramiques au fond et à droite, skyline de Los Angeles
  au crépuscule générée procéduralement, finitions dorées, palmier d'intérieur). Paliers achetés dans l'ordre ; chaque
  palier ajoute ou retire de vrais objets (groupes activés selon le palier) et règle l'éclairage. La surface au sol ne
  change pas : l'aire de lavage, les outils, le robot et les machines gardent leurs positions (repères des tests, des
  sauvegardes et du réseau) — c'est la qualité et le contenu de l'espace qui évoluent, pas sa taille.
  Migration v3 → v4 : une partie commencée garde l'atelier « rangé » qu'elle avait (palier 1) ; nouvelle partie : cagibi.
- **Appartenance en ligne (5.2)** : le décor et les machines visibles sont ceux de **l'atelier de l'hôte** (la session) ;
  chaque joueur achète pour **sa** carrière (chez l'hôte, qui valide) et retrouve ses achats quand il héberge. La carrière
  individuelle n'est jamais écrasée.
- **Personnalisation (5.3)** : bouton « Personnage » du menu principal. Choix limités à ce que la silhouette procédurale
  permet réellement, indiqué dans la fenêtre : tenue (6 couleurs), casquette (4 couleurs ou « Aucune »), gants
  (3 couleurs). Aperçu 3D rendu par une caméra dédiée (rotation automatique, boutons, LB / RB). Sauvegardé dans le profil
  du joueur (pas dans la progression de l'atelier), appliqué aux gants de la vue à la première personne, répliqué aux
  autres joueurs (une valeur compacte possédée par chaque joueur).
- **Ragdoll (5.4)** : chute contrôlée réglable (élan maximal 6 m/s, durée 1 à 3 s, chute 0,35 s, relevé 0,6 s). Joueur
  local : contrôles et outils coupés, caméra au sol inclinée, capsule de collision inchangée (impossible de passer sous le
  sol ou de rester coincé), relevé garanti. Autres joueurs : vrai ragdoll de la silhouette (corps et membres articulés,
  vitesse et rotation bornées, durée maximale 6 s, retour en douceur à la pose). Réseau : seuls l'état « à terre » et
  l'élan sont transmis (aucun os). Déclencheur de ce jalon : glissade en **courant** sur une mousse épaisse (≥ 0,45,
  ≥ 4 m/s, une fois toutes les 30 s au plus, désactivable). Les coups de batte (jalon 6) appelleront la même chute.
- **Musique (5.5)** : deux boucles composées par le générateur audio du projet (menu 30 s, atelier 24 s), aucune musique
  externe. Un seul lecteur persistant (deux sources pour le fondu enchaîné de 1,5 s), gain maximal 0,45 (sous les sons de
  jeu), atténuée à 40 % en pause, curseur « Musique » dans les réglages (0 = muette).
- Sauvegarde v4 (`character`, `musicVolume`), protocole `rugwash-coop-6`.

## 8. Décisions du jalon 6 (farces entre coéquipiers)

Audit : rien n'existait pour les farces. Réutilisés : catalogue d'améliorations et boutique de l'ordinateur (nouvel
onglet **Farces**), opérations de grille (une tache de farce est une vraie salissure de la grille, répliquée comme les
autres cellules), chute contrôlée et ragdoll du jalon 5, demandes invité → hôte et carrière de chaque invité tenue par
l'hôte, générateurs procéduraux (modèles, sons, texture).

- **Présentation** : farces de dessin animé, présentées comme telles dans la boutique et le HUD (« Farce · … »). Aucun
  contenu explicite : le pipi est un jet de gouttes jaunes en arc qui part des hanches (aucune anatomie modélisée), le
  « sang » est du **faux sang de théâtre** rouge cerise en taches rondes à bords nets.
- **Fusil à pompe (4.2)** : réalisé en **version jouet** comme le permet la consigne : fusil de farces et attrapes en
  plastique bleu et blanc, **embout orange**, qui ne projette que du faux sang de théâtre et **ne vise jamais un
  joueur** (aucun effet, refus explicite). Pas de système de combat, pas de dégâts.
- **Gadgets** (achetés pour la carrière du joueur, réputation requise, nettement plus chers que les outils : 90 à 650 €) :
  - *Soda géant (pipi)* — 1 500 €, réputation 5, recharge 45 s. Tapis : auréole jaune et humide (rayon 0,26 m) ;
    coéquipier : gouttes à l'écran.
  - *Batte de baseball* — 4 000 €, réputation 12, recharge 20 s. Tapis : petite tache de faux sang (0,09 m) ;
    coéquipier : chute comique de 1,4 s (ragdoll vu par les autres), relevé immédiat possible avec **Interagir** après
    0,5 s, puis 15 s de protection.
  - *Fusil de farces et attrapes* — 6 500 €, réputation 20, **60 € par tir**, recharge 12 s : neuf éclaboussures de
    faux sang dispersées sur 0,35 m autour du point visé.
- **Commandes** : **G** (croix haut) sort le gadget suivant parmi ceux achetés, puis range ; une touche d'outil ou la
  molette revient aux outils ; clic gauche / RT : une farce par appui. Touche reconfigurable (« Gadget de farce »).
- **Salissures** : deux nouveaux types, *Pipi (farce)* et *Faux sang (farce)*, fixés dans les fibres : l'aspirateur n'y
  fait rien, détergent + brosse les détachent, le rinçage les évacue. Ils comptent dans la propreté et l'évaluation
  comme toute saleté (la propreté de référence ne change pas : une farce fait baisser le pourcentage).
- **Gouttes à l'écran** : opacité plafonnée à 85 % (65 % par défaut), gouttes surtout vers les bords, 6 s dont 2,5 s de
  fondu ; une nouvelle salve ne s'empile pas ; effacées à la fin de la partie en ligne et par le jeu.
- **Garde-fous (autorité : solo ou hôte)** : achat, recharge par gadget + 3 s entre deux farces quelconques, coût
  débité par l'autorité, portée mesurée depuis la position du joueur **chez l'hôte** (tolérance 0,75 m), cible valide
  (tapis de la commande, joueur connu et synchronisé, jamais soi-même), protection des victimes (20 s après des gouttes,
  15 s après un coup, jamais un joueur à terre), **plafond par tapis** (15 % de sa saleté initiale, au moins 6 unités ;
  au plus une farce au-delà), **tapis terminé épargné**, tache plafonnée par cellule (85 %). Aucune farce ne touche à
  l'argent, aux achats ni à la réputation d'un autre joueur.
- **Réseau** : l'invité envoie seulement gadget, cible et point visé ; l'hôte rejoue toutes les vérifications avec la
  carrière qu'il tient pour cet invité et la position de sa silhouette ; il répond au demandeur (effets joués à la
  validation) ; les taches arrivent par la réplication des cellules ; gouttes et chute ne sont envoyées qu'au joueur
  visé (valeurs bornées à la réception) ; les autres postes rejouent jet, tir et sons localement (aucune particule
  transmise). Silhouettes : batte ou fusil en main (emplacements 12 et 13), main libre pour le pipi. Protocole
  `rugwash-coop-7`.
- **Réglages centralisés** : `ScriptableObjects/PrankTuning.asset` (portée, recharge, coût par usage, taches, gouttes,
  chute, protections, plafonds) ; prix et réputation dans les améliorations du catalogue (`up_prank_*`).
