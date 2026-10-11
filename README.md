# RugWash Simulator

Jeu original de nettoyage de tapis à la première personne (PC Windows), en cours de développement.
Le joueur tient un petit atelier : il inspecte des tapis sales, choisit ses outils, brosse, rince
et voit le motif d'origine réapparaître progressivement.

> **État actuel : démo complète (phases 0 à 6).** Build Windows produit et vérifié automatiquement.
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

| Action | Touche |
|---|---|
| Se déplacer | **ZQSD** (AZERTY) / **WASD** (QWERTY) — touches physiques, aucun réglage requis — ou flèches |
| Courir | Maj gauche |
| Regarder | Souris |
| Utiliser l'outil | Clic gauche (maintenu) |
| Choisir un outil | 1–5 (rangée du haut ou pavé numérique) / molette |
| Interagir (postes, caniveau, comptoir des commandes, boutique) | E |
| Inspecter le tapis (avant/après) | F |
| Pause | Échap |
| Diagnostic (éditeur / build de développement) | F3 |

**Paramètres** (menu principal ou pause → « Paramètres ») — chaque option est réellement appliquée :
volumes général / effets / interface, sensibilité et inversion de la souris, résolution, mode d'affichage
(plein écran, plein écran fenêtré, fenêtré), niveau de qualité, et **reconfiguration des touches**
(avancer, reculer, gauche, droite, courir, interagir, inspecter ; Échap annule la saisie ; « Touches par défaut »).
Résolution et mode d'affichage n'ont d'effet visible que dans un build (l'éditeur les ignore).

## 4. Outils et boucle de nettoyage

| Touche | Outil | Rôle | Consommable |
|---|---|---|---|
| 1 | Aspirateur | Retire poussière et débris secs ; aspire la saleté détachée **sur fibres sèches** ; sans effet sur les taches incrustées | — (électrique) |
| 2 | Détergent | Dépose mousse + produit ; ne nettoie pas seul mais rend la brosse très efficace sur graisse, boissons, taches incrustées | Détergent 4 L (0,1 L/s) |
| 3 | Brosse | Détache la saleté fixée (devient boue à rincer) ; efficacité selon sec / humide / savonneux et type de saleté | — |
| 4 | Lance de rinçage | Évacue saleté détachée, mousse et (plus lentement) résidus ; mouille | Eau 40 L (1,5 L/s) |
| 5 | Extracteur | Aspire l'eau des fibres avec la saleté et le produit qu'elle contient | Remplit la cuve de récupération (12 L) |

Séquence type : **aspirer → savonner → brosser → rincer → extraire**.
- Eau : poste d'eau (mur gauche). Détergent : poste produits (mur gauche, bidon vert).
  Cuve pleine : la vider au **caniveau** de l'aire de lavage. Touche E dans les trois cas.
- Un rinçage trop bref laisse des **résidus de produit** (voile pâle visible, compté dans l'inspection).
- L'extracteur est une **abstraction** : l'humidité retirée est convertie en litres
  (3 L par m² de tapis saturé) et versée dans la cuve.
- **F** : comparaison avant/maintenant et statistiques (saleté retirée, surface propre, humidité, mousse, résidus).

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

Contenu modifiable sans code : `ScriptableObjects/Orders` (commandes) et `ScriptableObjects/Upgrades` (prix, valeurs).

**Créer une commande** : `Create → RugWash → Commande`, renseigner tapis, couches de saleté et graine,
difficulté, récompense, réputation requise, objectifs ; l'ajouter à la liste `orders` de `[Game] → OrderManager`.

## 4 bis. Audio

Les sons (outils en boucle, interface, remplissage, vidange, éclat de zone propre, ambiance d'atelier,
pas, annonce « objectif atteint ») sont **générés par
synthèse** (`RugWashAudioGenerator`) dans `Assets/RugWash/Audio/Generated/tmp_*.wav`. Ce sont des
**substituts temporaires** clairement préfixés `tmp_`, à remplacer par des enregistrements : il suffit de
changer le clip assigné (`CleaningAudioController`, `AudioManager`, postes). `AudioManager` gère les volumes
général / effets / interface (exposés pour l'écran de paramètres de la phase 4) et coupe tout en pause.

## 4 quater. Rendu et présentation

- URP Forward+, **SSAO** (occlusion ambiante), ombres douces du projecteur principal, sonde de réflexion,
  étalonnage (ACES, balance des blancs chaude, saturation, bloom, vignette).
- Outils tenus en main animés : balancement à la souris, balancier de marche, montée de l'outil à chaque changement
  (`ViewModelRig`), plus l'animation propre à chaque outil (brossage, vibration, recul).
- Atelier habillé : tapis à sécher, tapis roulés en attente, poste d'eau avec flexible enroulé, fût de détergent,
  étagères, établi à tiroirs, panneau d'outils, comptoir avec écran, poutres, conduits, horloge à l'heure réelle,
  affiche « Les 5 étapes » (aide en jeu), marquage au sol.

## 5. Architecture

```
Assets/RugWash/
  Scripts/                 (assembly RugWash.Runtime)
    Core/        GameBootstrap (racine de composition), GameStateManager (états explicites), utilitaires
    Cleaning/    Grille de nettoyage, opérations, générateur de saleté, outils, définitions (ScriptableObjects)
    Audio/       AudioManager (volumes, pause, sons d'interface, voix ponctuelles réutilisées)
    Effects/     RugDirtRenderer (grille → textures de contrôle), capture avant/après, indicateur de visée,
                 CleaningAudioController (boucle par outil), CleaningVFXPool, WaterEffectController (sol mouillé)
    Player/      Entrées, contrôleur FPS, outils du joueur, interactions, équipement
    UI/          HUD, barre d'outils, menu pause, inspection
    Orders/      OrderDefinition, OrderBook (états), OrderEvaluator (note), OrderManager (cycle de commande)
    Economy/     Wallet, WalletManager, ReputationManager, UpgradeDefinition, EquipmentStats, ShopRules,
                 UpgradeManager (applique les effets), ShopManager
    Save/        SaveData (format versionné), SaveMigration, SaveFileStore (écriture atomique, secours),
                 SaveManager (chargement / sauvegarde), SettingsManager (application des paramètres)
    Diagnostics/ Overlay F3, mesure des temps de frame
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
`RugDirtRenderer` encode la grille dans trois textures RGBA32 (`ControlA` = poussière, tache, détaché,
mousse ; `ControlB` = couleur de la saleté, humidité ; `ControlC` = résidus de produit, usure). Seul le **rectangle modifié** est réencodé,
directement dans la mémoire de la texture, et l'envoi GPU est limité à 30/s. Le shader
`RugWash/Rug Surface` superpose saleté, boue, poussière, humidité et mousse **au motif d'origine**
(jamais remplacé) ; un bruit de détail découpe les seuils pour une frontière propre/sale organique.
Compatible tout GPU DX11 (pas de compute shader).

## 7. Ajouter du contenu

**Un tapis** : `Create → RugWash → Tapis` dans `ScriptableObjects/Rugs`. Renseigner taille, épaisseur,
texture de motif (couleur, *Clamp*), normale de fibres (tuilable), couches de saleté (type, motif,
couverture, intensité, échelle, graine). L'ajouter à la liste `availableRugs` de `[Game] → GameBootstrap`.

**Un type de saleté** : ajouter une valeur à l'enum `DirtType` (sans renuméroter), créer
`Create → RugWash → Type de saleté`, l'ajouter à `DirtTypeLibrary`.

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
- Pas de sauvegarde cloud, de synchronisation ni de protection contre la modification du fichier.

## 9. Tests

```bash
powershell -ExecutionPolicy Bypass -File "Tools/RugWash-CI.ps1" -Step all
```

Étapes : `compile`, `bootstrap`, `scene` (atelier + menu principal), `test` (EditMode, sans GPU),
`playmode` (avec GPU, produit des captures dans `Logs/ci/screens`), `build` (exécutable Windows) et
`benchmark` (lance le jeu compilé en mode vérification). `all` enchaîne les cinq premières étapes. Les tests utilisent un dossier de
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
