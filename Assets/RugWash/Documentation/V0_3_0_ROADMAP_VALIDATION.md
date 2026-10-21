# V0.3.0 — Fiche de validation de la roadmap (refonte graphique)

À remplir par toi. Pour chaque point, j'ai noté **ce qui a réellement été fait** (« État ») et **où le vérifier**
(« Preuve »). Coche la case quand tu valides après avoir regardé toi-même ; sinon note ce qu'il faut reprendre.

Légende de l'état : **Fait** · **Partiel** (une partie seulement, détaillée) · **Non fait**.

- Captures de contrôle : `Logs/ci/screens/` (préfixes `v30_*`, `v30_fbx_*`, `v30_avatar_*`).
- Exécutions réelles des tests : `VALIDATION.md` § 23 à 23.5. Détail des changements : `V0_3_0.md`.
- Build testé : `Builds/Windows/RugWashSimulator.exe` (build du 21/10/2026, 03:13, fin de la V0.3.0).

Testeur : ____________________ Date : ____/____/______ Version / commit testé : ____________________

PC de test : processeur ____________ carte graphique ____________ résolution ____________ qualité ____________

---

## 1 — Audit et direction artistique

- [ ] **Inspecter scènes, prefabs, modèles, matériaux, textures, éclairages, shaders, interfaces**
  État : Fait. Preuve : `V0_3_0.md` § 1 (tableau d'audit par zone).
  Remarques : ______________________________________________
- [ ] **Repérer les assets provisoires, incohérents ou dépassés**
  État : Fait. Preuve : `V0_3_0.md` § 1 (matériaux unis, silhouette en boîtes, police Arial, éclairage terne…).
  Remarques : ______________________________________________
- [ ] **Direction artistique (palette, matériaux, formes, stylisation, éclairage, règles)**
  État : Fait. Preuve : `V0_3_0.md` § 2 « atelier artisanal moderne », `RugWashArtDirection`, `UITheme`.
  Remarques : ______________________________________________
- [ ] **Cible qualité / performance réaliste**
  État : Fait (21/10). Cible : 144 FPS en 1080p Ultra sur la machine de test, 60 FPS en 1080p Moyen sur un PC d'entrée de
  gamme (GPU classe GTX 1650 ou CPU deux fils). Mesuré : 175 à 180 FPS en Ultra ; approximations d'entrée de gamme 119,5 et
  168 FPS au pire (`V0_3_0.md` § 5, `VALIDATION.md` § 23.5). Limite : pas de vrai PC d'entrée de gamme testé.
  Remarques : ______________________________________________
- [ ] **Ensemble de référence / scène pilote avant généralisation**
  État : Partiel. Galeries de contrôle (studio FBX `v30_fbx_*`, vues de l'atelier `v30_01` à `v30_10`) ; pas de scène
  pilote séparée validée avant d'appliquer la direction partout.
  Remarques : ______________________________________________

Terminé lorsque la direction est claire et validée sur une zone représentative : ☐ Oui ☐ Non

## 2 — Objets, outils et équipements

- [ ] **Outils, buses, batte, brosses, pulvérisateurs, seaux, bidons, machines, équipements**
  État : Fait (21/10), avec exceptions justifiées.
  - Modèles Blender en jeu : lance de rinçage, seau, raclette, monobrosse, brosse, batte, **extracteur, brosse de détail**,
    casier, machine à laver, caisse à outils ; décor de l'annexe (sèche-linge, établi, panneau d'outils, suspension,
    aspirateur à eau, bidons, pompe, lance-brosse, pulvérisateur, pistolet, éponge, chiffons).
  - Gardés volontairement : pulvérisateur de détergent (flacon translucide qui montre le niveau réel), postes eau et
    détergent, rampe de pré-mouillage, fusil à farces (procéduraux, matériaux refaits).
  Preuve : `v30_tool_*`, `v30_toolrest_*`, `v30_fbx_tools_*`, `v30_fbx_ingame_*`.
  Remarques : ______________________________________________
- [ ] **Silhouettes, proportions, détails, matériaux, usure, reflets**
  État : Fait (sur les objets remplacés ci-dessus). Remarques : ______________________________________________
- [ ] **Items achetables / ramassables immédiatement reconnaissables**
  État : Partiel. Icônes 3D des outils dans la barre d'objets, la boutique et le casier (`v30_22_shop`, `v30_23_locker`).
  Remarques : ______________________________________________
- [ ] **Points d'attache, animations, collisions, hitboxes, comportements préservés**
  État : Fait pour les outils remplacés (buse, tête et manche télescopiques, gants, finition dorée, porte du casier,
  hublot et tambour de la machine) ; vérifié par les tests PlayMode (`VALIDATION.md` § 23.2).
  À tester en jeu : ☐ jet de la lance ☐ seau ☐ raclette ☐ monobrosse (disque qui tourne) ☐ brosse ☐ batte
  ☐ casier (porte) ☐ machine à laver (achat, cycle, porte) — Remarques : ____________________
- [ ] **Pas de surmodélisation, détails visibles en jeu**
  État : Fait. Outils 2 000 à 13 000 triangles, accessoires 600 à 9 000. Remarques : ____________________

## 3 — Tapis, saleté et matériaux

- [ ] **Modèles et textures des tapis classiques, orientaux, poils longs**
  État : Fait (textures) : motifs rendus nœud par nœud, résolution doublée, nouveau tapis de hall (`V0_3_0.md` § 3.4).
  Les maillages de jeu (grille de nettoyage) sont inchangés.
  Remarques : ______________________________________________
- [ ] **Fibres, motifs, bordures, reliefs crédibles et lisibles**
  État : Fait : normale de nœuds alignée sur le motif, velours des fibres dans le shader. Remarques : ____________________
- [ ] **États sale / propre et transition de nettoyage**
  État : Fait (21/10). Saleté et nettoyage en jeu : système existant (non cassé). Le tapis qui sèche dans l'atelier utilise
  l'état « mouillé » (`v30_fbx_ingame_drying_wet`) ; les autres états restent dans la galerie (`v30_fbx_states_*`).
  Remarques : ______________________________________________
- [ ] **Mousse, eau, humidité, résidus**
  État : Partiel : le velours s'éteint sous l'eau et la mousse ; pas de refonte des particules ni de la mousse.
  Preuve : `v30_08_rug_half_clean`, `v30_10_foam_close`. Remarques : ____________________
- [ ] **Lisibilité sous différents éclairages et angles**
  État : Fait (21/10) : `Capture_V030_RugLighting` (vue plongeante, angle rasant, contre-jour, zone 2 sous la lumière de
  l'annexe ; `v30_light_*`). Remarques : ____________________

## 4 — Personnages et animations

- [ ] **Personnages visibles (modèles, vêtements, matériaux, proportions)**
  État : Fait : deux ouvriers de la référence (salopette, combinaison) modélisés dans Blender, intégrés à l'avatar réseau
  et à l'aperçu du menu « Personnage » (`V0_3_0.md` § 3.5).
  Preuve : `v30_avatar_*_front`, `_face`, `_back`, `v30_31_character`.
  Ressemblance à la référence : ☐ Bonne ☐ À reprendre — Remarques : ____________________
- [ ] **Mains / éléments de la vue première personne**
  État : Fait : gant modélisé dans Blender sur tous les outils. Remarques : ____________________
- [ ] **Animations, poses, transitions, ombres, autres joueurs en multijoueur**
  État : Fait (21/10). Squelette de 42 os, animation procédurale du jeu enrichie : jambes et genoux, bras balancés en
  marchant, foulée plus ample en course, bras refermés sur l'outil, doigts serrés sur l'outil tenu, tête, ragdoll, KO.
  Pas de clips (le jeu n'a pas d'Animator). Vérifié par captures et tests, puis à deux processus (67 contrôles) ; pas sur
  deux PC.
  À tester en ligne : ☐ l'autre joueur apparaît ☐ marche ☐ tient l'outil ☐ couleurs / tenue choisies
  ☐ chute (batte) ☐ KO (fusil) — Remarques : ____________________
- [ ] **Rigs, points d'attache, contrôleurs, synchronisation réseau préservés**
  État : Fait : mêmes pièces pilotes, mêmes données réseau (aucun changement de protocole). Remarques : ____________________
- [ ] **Amélioration intégrable plutôt que remplacement cassant**
  État : Fait (l'ancien système d'animation est conservé). Remarques : ____________________

## 5 — Atelier et environnement

- [ ] **Sols, murs, plafonds, portes, étagères, éclairages, machines, ordinateur, mobilier, décor**
  État : Fait (21/10) : 13 surfaces PBR, 28 accessoires réels, casier, machine à laver, caisse à outils, affiche Canva,
  enseigne RugWash, décor Blender de l'annexe (sèche-linge, établi, panneau d'outils, suspension éclairée, accessoires).
  Gardé volontairement : le bâti procédural (sol, murs, cloisons, aire de lavage), qui porte collisions, lumière et surfaces
  nettoyables. Preuve : `v30_01` à `v30_06`, `v30_fbx_ingame_*`. Remarques : ______________________________________________
- [ ] **Profondeur visuelle (matériaux, lumière, ombres, composition)**
  État : Fait. Remarques : ____________________
- [ ] **Lisibilité des zones interactives et objets importants**
  État : Partiel : panneaux de zone conservés, interaction contextuelle près du viseur. Remarques : ____________________
- [ ] **Décor harmonisé sans gêner les déplacements**
  État : Fait (accessoires hors des allées). À vérifier en jeu : ☐ pas de blocage ☐ pas d'objet qui flotte
  Remarques : ____________________
- [ ] **Collisions, navigation, interactions, visibilité en vue première personne**
  État : Partiel : tests PlayMode, captures, et test du décor de l'annexe (aire de lavage 2, passage et machine dégagés) ;
  pas de tour complet à la main. Remarques : ____________________

## 6 — Refonte du HUD et des menus

- [ ] **HUD, menus, inventaire, casier, boutiques, missions, résultats, sauvegardes**
  État : Fait : charte appliquée à toutes les fenêtres ; HUD minimaliste ; boutique et casier allégés (`V0_3_0.md` § 3.6).
  Preuve : `v30_09_hud`, `v30_20` à `v30_25`, `v30_30` à `v30_32`.
  Remarques : ______________________________________________
- [ ] **Canva ou outil de design si utile**
  État : Fait (20/10) : affiche murale « Les 5 étapes » conçue dans Canva (design `DAHYhkCBAAU`, mêmes étapes que le jeu,
  charte du jeu), exportée en PNG et posée à la place de l'affiche procédurale (`Textures/Decor/Poster_Steps_Canva.png`).
  Les éléments d'interface restent dessinés par script (nets et teintables). Preuve : `v30_canva_poster`.
  Remarques : ____________________
- [ ] **Typographie, palette, hiérarchie, icônes, composants cohérents**
  État : Fait : police Inter, `UITheme`, icônes `Textures/UI/Icon_*`. Remarques : ____________________
- [ ] **Lisibilité, alignements, marges, états survol / actif / désactivé, transitions**
  État : Fait (survol, appui, sélection safran, désactivé grisé ; pulsation de la propreté ; fondu du rappel des touches).
  Remarques : ____________________
- [ ] **Fonctions, textes utiles, contrôles, résolutions préservés**
  État : Fait (20/10) : `Capture_V030_Resolutions` en 1280 × 720, 1920 × 1080, 2560 × 1440, 1680 × 1050 (16:10),
  1280 × 1024 (5:4) et 3440 × 1440 (21:9) : cartes du HUD entièrement à l'écran et sans chevauchement, boutique, casier et
  pause capturés (`v30_res_*`). Défaut trouvé et corrigé : le statut de l'outil débordait du panneau. En 5:4, le statut du
  détergent (réserve, stock, produit) reste petit.
  À tester : ☐ 1280 × 720 ☐ 2560 × 1440 ☐ ultra-large ☐ manette — Remarques : ____________________
- [ ] **Pas d'images statiques à la place des boutons et textes**
  État : Fait. Remarques : ____________________

Le HUD est-il assez minimaliste ? ☐ Oui ☐ Trop chargé ☐ Il manque une info : ____________________

## 7 — Matériaux, éclairage et post-traitement

- [ ] **Matériaux PBR harmonisés (albédo, métal, lissé)**
  État : Fait (`RugWashSurfaceLibrary`, micro-détails). Remarques : ____________________
- [ ] **Éclairage, sources, ombres, séparation des plans**
  État : Fait (projecteur de l'aire, plafonniers, lumière du jour, rais de lumière). Remarques : ____________________
- [ ] **Post-traitement seulement s'il est utile**
  État : Fait : étalonnage léger (exposition, contraste, virage partiel, vignette douce). Remarques : ____________________
- [ ] **Image claire, sans effets exagérés, noirs bouchés ni reflets distrayants**
  Ton avis : ☐ OK ☐ Trop sombre ☐ Trop clair ☐ Reflets gênants — Remarques : ____________________
- [ ] **Cohérence entre scènes, menus et atelier**
  État : Fait (même charte et même étalonnage). Remarques : ____________________

## 8 — Intégration, performance et compatibilité

- [ ] **Textures (tailles, formats, compression, mipmaps), meshes, matériaux partagés, shaders**
  État : Fait (`V0_3_0.md` § 5). Remarques : ____________________
- [ ] **Réutilisation des matériaux et ressources communes**
  État : Fait : les FBX intégrés reprennent les matériaux de l'atelier (`RugWashFbxModels`). Remarques : ____________________
- [ ] **Pas de textures énormes, matériaux dupliqués, shaders coûteux**
  État : Partiel : 2048 px au plus ; deux ouvriers d'environ 60 000 triangles chacun (lourds à plusieurs joueurs, à surveiller).
  Remarques : ____________________
- [ ] **Draw calls, overdraw UI, transparences, éclairages coûteux**
  État : Partiel : coût GPU mesuré sur le build final (3,1 à 4,9 ms en Ultra, `VALIDATION.md` § 23.5), comme avant les
  derniers ajouts ; draw calls non disponibles dans les builds (compteurs du profileur à -1) ; coût de plusieurs ouvriers
  affichés non mesuré. FPS ressentis : ____________ Remarques : ____________________
- [ ] **Nettoyage, outils, sauvegardes, multijoueur non affectés**
  État : Fait (21/10) : EditMode 265/265 ; PlayMode dans l'éditeur 171 réussis, 1 échec propre à l'éditeur (robot, à
  revérifier en mode batch), 2 ignorés ; multijoueur à deux processus 67/67 (`VALIDATION.md` § 23.5). À tester : ☐ nettoyage complet d'une commande ☐ sauvegarde puis rechargement ☐ partie en ligne
  Remarques : ____________________
- [ ] **Erreurs d'importation / références manquantes corrigées**
  État : Fait (galerie FBX : aucun matériau manquant ; tests sans erreur). Remarques : ____________________

## 9 — Contrôle qualité final

- [ ] **Scènes principales et interfaces à différentes résolutions** — État : Fait (six résolutions, voir phase 6).
  Remarques : ____________________
- [ ] **Interactions et outils après remplacement des modèles** — État : Fait par les tests ; à confirmer en jeu.
  Remarques : ____________________
- [ ] **Vue première personne, animations, collisions, points d'attache** — État : Fait par les tests et les captures.
  Remarques : ____________________
- [ ] **Joueurs et objets en session multijoueur** — État : Fait (20/10 sur `ae2770d`, puis 21/10 sur le build final) : test à deux processus
  (hôte, invité, invité relancé après crash), **67 contrôles réussis, 0 échec**, aucune exception dans les journaux (tapis,
  argent et missions identiques, apparence de l'invité vue par l'hôte, silhouette de l'hôte visible, reconnexion sans
  fantôme, sauvegardes intactes). Limite : contrôles par données, pas d'image de l'autre joueur ; pas de test sur deux PC.
  Remarques : ____________________
- [ ] **Anciennes fonctionnalités accessibles** — État : Fait par les tests (boutique, casier, missions, pause, RugDex,
  paramètres, personnage, machine à laver, agrandissement, farces). Remarques : ____________________
- [ ] **Récapitulatif (changements, assets remplacés, outils, limites, reste à faire, contrôles réels)**
  État : Fait : `V0_3_0.md`, `VALIDATION.md` § 23 à 23.5 et cette fiche (en-tête de `V0_3_0.md` corrigé : builds et
  benchmarks lancés à ta demande).

---

## Définition de terminé

| Critère | Ton verdict |
|---|---|
| Direction artistique cohérente dans tout le jeu | ☐ Oui ☐ Non |
| Objets et personnages visiblement améliorés | ☐ Oui ☐ Non |
| Tapis et matériaux plus convaincants | ☐ Oui ☐ Non |
| HUD et menus refaits proprement | ☐ Oui ☐ Non |
| Performances et systèmes existants stables | ☐ Oui ☐ Non |

Hors périmètre respecté (aucune mécanique ajoutée, sauvegarde / progression / réseau non réécrits) : ☐ Oui ☐ Non

**Verdict V0.3.0 :** ☐ Validée ☐ Validée avec réserves ☐ À reprendre

À reprendre en priorité :

1. ______________________________________________
2. ______________________________________________
3. ______________________________________________
