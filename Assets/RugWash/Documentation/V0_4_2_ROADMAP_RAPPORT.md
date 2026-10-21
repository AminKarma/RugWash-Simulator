# V0.4.2 — Rapport pour la roadmap

À reporter par toi dans la roadmap Notion. Pour chaque point : **ce qui a réellement été fait** (« État ») et **où le
vérifier** (« Preuve »). Coche la case quand tu as validé toi-même ; sinon note ce qu'il faut reprendre.

Légende de l'état : **Fait** · **Partiel** (une partie seulement, détaillée) · **Bloqué** (dépend d'un élément extérieur)
· **Non testé** (fait, mais une vérification demandée n'a pas pu être lancée).

- Exécutions réelles des tests : `VALIDATION.md` § 24. Plan des actualités : `V0_4_2_NEWS_PLAN.md`.
- Tests : EditMode **270/270** ; PlayMode **186/189** sur la suite complète (2 ignorés : diagnostics de premier usage ;
  1 échec du robot, corrigé puis 6/6 tests du robot relancés) ; multijoueur à deux processus **97/97** (hôte + un invité,
  même machine).
- Captures : `Logs/ci/screens/v42_*` (glissade, dynamite, membres dispersés puis rassemblés, couleur de peau).
- Protocole réseau : `rugwash-coop-13` (une version antérieure ne peut pas rejoindre une partie V0.4.2).

Testeur : ____________________ Date : ____/____/______ Version / commit testé : ____________________

---

## 1 — Audit et plan

- [ ] **Audit du projet avant de coder (scènes, scripts, prefabs, réseau, sauvegardes, outils, inventaires, UI)**
  État : Fait. Causes réelles trouvées avant correction pour chaque bug du jalon 2 (voir ci-dessous).
  Remarques : ______________________________________________

## 2 — Bugs et multijoueur

- [ ] **2.1 Objets au sol synchronisés, visibles par un joueur arrivé en cours de partie**
  État : Fait. Cause : les modèles des objets étaient déclarés après l'apparition réseau de l'atelier ; un arrivant
  recevait des objets invisibles (ramassables). Modèles déclarés dès le chargement + objets recréés quand ils sont prêts.
  Ramassage par l'hôte seul (premier arrivé), suppression synchronisée, aucun doublon à la reconnexion.
  Preuve : multijoueur « Arrivée en cours de partie : objet tombé avant son arrivée visible au sol », « … ramassé, il
  devient le sien (une seule fois) », « Objet tombé pendant l'absence : … plus rien au sol ».
  Remarques : ______________________________________________
- [ ] **2.2 Voix de l'hôte**
  État : Fait (hôte ↔ invité). Cause : chez un invité, Netcode ne donne pas les silhouettes des autres joueurs
  (`GetPlayerNetworkObject` réservé à l'hôte) ; la voix (et les effets de farce) ne trouvait jamais à qui la jouer.
  Registre des silhouettes sur chaque poste ; même système vocal pour tous.
  **Non testé : invité ↔ invité** (le test automatique ne lance qu'un hôte et un invité).
  Preuve : multijoueur « Voix : l'hôte entend l'invité », « Voix : l'invité entend l'hôte ».
  Remarques : ______________________________________________
- [ ] **2.3 Barres d'eau et de produit**
  État : Fait. Cause : marge fixe de 3 px dans des barres de 5 à 7 px (remplissage de 0 à 1 px). Marge adaptée, barres
  un peu plus hautes, barre = réserve réelle à chaque image (plus de lissage en retard sur le texte), bornée 0–100 %.
  Preuve : `HudBars_FillIsVisible_AndMatchesTheRealReserve`.
  Remarques : ______________________________________________
- [ ] **2.4 Fusil : lâcher l'objet tenu**
  État : Fait. Même mécanisme que la batte ; l'objet **tenu** tombe (sinon le dernier objet acheté porté), une fois,
  décidé par l'hôte. La batte suit la même règle.
  Preuve : multijoueur « Fusil à farces : KO de l'invité, il lâche l'objet qu'il tient », « Fusil reçu : … la lance reste ».
  Remarques : ______________________________________________
- [ ] **2.5 Glissade sur le savon vue par les autres**
  État : Fait. Chute cartoon (sur le dos, jambes en l'air, bras qui moulinent) qui suit la vraie position du joueur qui
  glisse : la glissade continue, ni téléportation ni corps bloqué ; relevé en douceur. Glissade locale inchangée.
  Preuve : `RemoteSlip_CartoonFallFollowsTheSlidingAvatar_ThenStandsBackUp`, multijoueur « Glissade de l'invité … »,
  capture `v42_slip_down.png`.
  Remarques : ______________________________________________

## 3 — Améliorations et progression des outils

- [ ] **3.1 Une carte évolutive par outil**
  État : Fait. Onglet Améliorations : une carte par outil (niveau actuel, prochaine amélioration et son état disponible /
  verrouillée / achetée, prix, effet réel avant → après, améliorations achetées, nombre restant à découvrir).
  Preuve : `UpgradeCards_OnePerTool_BuyAdvancesTheCard_AndExtrasAreReal`.
  Remarques : ______________________________________________
- [ ] **3.2 Nouvelles améliorations réelles**
  État : Fait : raclette large, seau à bec verseur, poils en laiton (brosse de détail), brosse large, suceur large
  (aspirateur), buse éventail (lance), suceur d'extraction large, plateau de 43 cm (monobrosse). Chacune modifie la
  largeur ou la puissance réellement utilisée par l'outil ; sauvegardées et synchronisées comme les autres achats.
  Prix : 80 à 450 €. Preuve : même test (effets mesurés).
  Remarques : ______________________________________________
- [ ] **3.3 Bottes dans l'onglet Outils, avec icône**
  État : Fait. Icône photographiée depuis le modèle 3D des bottes du décor, même style que les autres.
  Preuve : `Boots_AreInTheToolsTab_WithTheirModelIcon`.
  Remarques : ______________________________________________

## 4 — Commandes, inventaire et paramètres

- [ ] **4.1 Touche pour lâcher l'objet tenu**
  État : Fait. Q (croix bas à la manette), remappable dans les paramètres ; validé par l'hôte, objet au sol pour tous ;
  refusé si rien d'acheté en main ou si le joueur est à terre. **En solo** : message (« en partie en ligne seulement »),
  les objets au sol n'existent qu'en ligne.
  Preuve : `DropKey_IsRebindable`, multijoueur « Touche « Lâcher » … ».
  Remarques : ______________________________________________
- [ ] **4.2 Brosse de détail au départ**
  État : Fait. La brosse de détail remplace la brosse standard dans le kit de départ ; la brosse standard s'achète (80 €).
  Sauvegarde v7 : une partie déjà commencée garde la brosse standard (fiches des invités comprises), aucune remise à
  zéro, aucun doublon. Preuve : `NewGame_DetailBrushIsInTheStartingKit`, `ExistingSave_KeepsTheStandardBrush_…`.
  Remarques : ______________________________________________
- [ ] **4.3 5e emplacement achetable**
  État : Fait. « Ceinture porte-outils » (1 200 €, réputation 6), onglet Outils ; emplacement affiché verrouillé avec son
  prix ; la capacité réelle passe à 5 (touche 5), sauvegardée, vérifiée par l'hôte pour chaque joueur.
  Preuve : `FifthSlot_LockedWithPrice_ThenBought_CarriesFive_AndIsSaved`, multijoueur « 5e emplacement : … ».
  Remarques : ______________________________________________
- [ ] **4.4 Périphériques audio**
  État : **Partiel**. Entrée (micro) : choix « par défaut de Windows » ou un micro branché, appliqué au chat vocal,
  sauvegardé, débranchement détecté (nouvel essai toutes les 2 s, retour au micro par défaut).
  **Bloqué : sortie audio** — le moteur audio de Unity ne permet pas de choisir la sortie ; le jeu joue sur la sortie par
  défaut de Windows et reprend le son si elle change (indiqué dans les paramètres, aucun faux menu).
  **Non testé : débranchement physique d'un micro.** Preuve : `AudioDevices_…`, `InputDevice_…` (EditMode).
  Remarques : ______________________________________________

## 5 — Robot aspirateur de la zone 2

- [ ] **Réservé à la zone 2, plus rapide, toutes les salissures, sans traverser les murs ni rester bloqué**
  État : Fait. Base et aire de la zone 2 (agrandissement requis) ; 0,55 m/s mesurés (0,32 avant) ; il frotte les taches
  (après les avoir humidifiées), aspire poussière et débris, ramasse eau, boue, mousse et savon, avec les règles des
  outils. Grille d'obstacles limitée à son aire. Mesures (même dépôt, aire témoin sans robot) : poussière 120 → 48,
  boue 150 → 30, savon 180 → 15, taches 150 → 34 (témoin inchangé).
  Preuve : `Robot_Zone2_CleansEveryDirtTypeOfItsPad_Faster_StaysOnIt` + 5 tests du robot.
  Remarques : ______________________________________________

## 6 — Dynamite

- [ ] **6.1 Fonctionnement**
  État : Fait. Achetée onglet Farces (5 200 €, réputation 18, 80 € par bâton), lancée ; une seule explosion par lancer
  (décidée par l'hôte), rayon 3,2 m ; joueurs touchés KO 5 s, ils lâchent l'objet tenu, puis se relèvent. Le lanceur et
  les joueurs déjà à terre sont épargnés. Preuve : `Dynamite_OneExplosion_…`, multijoueur « Dynamite : … ».
  Remarques : ______________________________________________
- [ ] **6.2 Effets et membres dispersés**
  État : Fait. Mèche qui crépite, boule de feu, fumée, éclats, anneau de poussière, flash bref, secousse dosée, ralenti
  local de 0,35 s (jamais chez l'hôte quand des invités jouent) ; chute exagérée et membres dispersés puis rassemblés
  (morceaux temporaires : le personnage réseau et son squelette ne sont pas touchés).
  Preuve : `Dynamite_LocalThrow_…`, `Dynamite_RemoteAvatar_LimbsScatter_ThenReassemble`, captures `v42_dynamite_*`.
  Remarques : ______________________________________________
- [ ] **6.3 Poussière sur les tapis proches**
  État : Fait. Selon la distance réelle, système de saleté existant, une fois par explosion, limite des farces par tapis.
  Remarques : ______________________________________________
- [ ] **6.4 Synchronisation réseau**
  État : Fait (hôte → invité testé). Lancer, explosion, joueurs touchés, KO, objet lâché, membres dispersés, poussière.
  **Non testé : plusieurs invités touchés à la fois** (un seul invité dans le test).
  Remarques : ______________________________________________

## 7 — Couleur de peau

- [ ] **Teintes naturelles, peau seulement, sauvegardée, synchronisée**
  État : Fait. 7 teintes (dont celle d'origine) ; seul le matériau de peau change (tenue, gants, cheveux, yeux intacts) ;
  gardée dans le profil ; vue par les autres, aussi après reconnexion.
  Preuve : `SkinColor_OnlyTheSkinChanges_SavedAndRestored_SentOnline`, multijoueur « Couleur de peau de l'invité vue par
  l'hôte », « Reconnexion : même apparence ».
  Remarques : ______________________________________________

## 8 — Actualités et patchnotes

- [ ] **8.1 Onglet Actualités à jour**
  État : Fait (annonces locales V0.4.2 et V0.3.0).
  Remarques : ______________________________________________
- [ ] **8.2 Patchnotes dynamiques**
  État : **Audit et plan seulement** (consigne) : `V0_4_2_NEWS_PLAN.md` (fichier public en lecture seule, cache, secours
  livré, aucune clé ni webhook dans le jeu ; Discord par une action côté serveur).
  Remarques : ______________________________________________

## Fin de version

- [ ] Compilation sans erreur — Fait.
- [ ] Nouvelle partie et sauvegarde existante — Fait (tests de migration et de nouvelle partie).
- [ ] Hôte et au moins un client, arrivée tardive — Fait (deux processus sur la même machine ; **pas sur deux PC**).
- [ ] Aucun objet, composant ou prefab cassé — Fait (suites EditMode et PlayMode complètes).

Validée : ☐ Oui ☐ Non — Remarques : ______________________________________________
