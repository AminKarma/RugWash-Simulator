# Sons — inventaire, usage et licences

## Licence

**Tous les sons du projet sont générés par synthèse** par `Editor/RugWashAudioGenerator.cs` (code écrit pour ce projet :
bruit filtré, oscillateurs, enveloppes). Aucun enregistrement, échantillon ni banque de sons tiers n'est utilisé :
il n'y a **aucune licence externe** à respecter ni à créditer.

Ce sont des **substituts temporaires** (préfixe `tmp_`), fonctionnels et distincts, destinés à être remplacés par des
enregistrements définitifs. Pour remplacer un son : déposer le fichier dans `Audio/`, puis changer le clip assigné
(champ indiqué ci-dessous) ; aucune ligne de code à modifier. Si des sons tiers sont ajoutés, compléter ce document
(auteur, source, licence, obligation d'attribution).

Régénération : menu *RugWash → Setup → Regénérer les Sons Temporaires* (les fichiers existants ne sont pas écrasés
par l'initialisation automatique).

## Règles de lecture

- **Catégories** : *Effets* (outils, contacts, postes, pas, ambiance, scan, machines), *Interface* (clics, paliers,
  notifications) et *Musique* (v0.1.2), plus le volume général — réglables dans les paramètres et sauvegardés.
- **Musique** (v0.1.2) : un seul lecteur persistant (`MusicPlayer`, conservé d'une scène à l'autre, jamais dupliqué) avec
  deux sources pour un fondu enchaîné de 1,5 s entre le menu et l'atelier. Gain maximal 0,45 à 100 % (toujours sous les
  sons de jeu), atténuée à 40 % en pause (pas coupée net), muette à 0 % (curseur « Musique »).
- **Pause** : tous les sons du jeu sont suspendus (`AudioListener.pause`) ; les sons d'interface continuent.
- **Boucles sans couture** : la fin générée est fondue dans le début (0,12 s).
- **Pas d'empilement** : un même son ponctuel n'est pas rejoué à moins de 0,06 s d'intervalle (`AudioManager`),
  les éclats de zone propre sont limités par le pool d'effets, et les sons positionnés utilisent 8 voix réutilisées.
- **Outils** : boucle de fonctionnement + boucle de contact dosée par le contact réel avec le tapis (silencieuse
  quand l'outil vise le vide) ; fondu de sortie de 0,06 s au changement d'outil (pas de clic), arrêt immédiat en pause.

## Inventaire

| Fichier (`Audio/Generated/`) | Rôle | Catégorie | Assigné à |
|---|---|---|---|
| `tmp_vacuum_loop` | Moteur de l'aspirateur | Effets | Outil Vacuum → `CleaningAudioController.loop` |
| `tmp_vacuum_contact_loop` | Aspiration étouffée sur les fibres | Effets | Vacuum → `contactLoop` |
| `tmp_soap_spray_loop` | Pulvérisation de détergent | Effets | Soap → `loop` |
| `tmp_foam_contact_loop` | Bulles de mousse qui crépitent | Effets | Soap → `contactLoop` |
| `tmp_brush_loop` | Mouvement de la brosse | Effets | Brush → `loop` |
| `tmp_brush_contact_loop` | Frottement des poils dans les fibres | Effets | Brush → `contactLoop` |
| `tmp_rinse_loop` | Jet d'eau de la lance | Effets | Rinse → `loop` |
| `tmp_rinse_contact_loop` | Eau qui frappe le tissu | Effets | Rinse → `contactLoop` ; rampe de pré-mouillage → `loopSource` |
| `tmp_extractor_loop` | Moteur de l'extracteur | Effets | Extractor → `loop` |
| `tmp_extractor_contact_loop` | Eau aspirée hors des fibres | Effets | Extractor → `contactLoop` |
| `tmp_foam_cannon_loop` | Canon à mousse (air comprimé et mousse) | Effets | FoamCannon → `loop` (contact : `tmp_foam_contact_loop`) |
| `tmp_broom_loop` | Allers-retours du balai | Effets | Broom → `loop` |
| `tmp_broom_contact_loop` | Poils raides qui raclent un sol mouillé | Effets | Broom → `contactLoop` ; Squeegee → `contactLoop` |
| `tmp_squeegee_loop` | Lame en caoutchouc qui glisse sur le sol mouillé, petits couinements (v0.1.2) | Effets | Squeegee → `loop` |
| `tmp_rotary_loop` | Moteur de la monobrosse : ronflement grave et engrenages (v0.1.2) | Effets | Rotary → `loop` |
| `tmp_rotary_contact_loop` | Disque qui frotte les fibres, modulé par la rotation (v0.1.2) | Effets | Rotary → `contactLoop` |
| `tmp_robot_hum_loop` | Moteur du robot aspirateur (son 3D) | Effets | `RobotVacuum.motorSource` |
| `tmp_robot_bump` | Robot arrêté par un obstacle | Effets | `RobotVacuum.bumpClip` |
| `tmp_robot_start` / `tmp_robot_stop` | Démarrage / arrêt du robot | Effets | `RobotVacuum.startClip` / `stopClip` |
| `tmp_robot_docked` | Robot de retour sur sa base | Effets | `RobotVacuum.dockedClip` |
| `tmp_tool_equip` | Prise en main d'un outil | Effets | `ViewModelRig.equipClip` |
| `tmp_refill` | Remplissage (eau, détergent) | Effets | Postes `ResourceStation` |
| `tmp_drain` | Vidange de la cuve au caniveau | Effets | Caniveau `ResourceStation` |
| `tmp_clean_sparkle` | Une zone devient propre | Effets | `CleaningVFXPool.spawnClip` |
| `tmp_scan_pulse` | Lancement du scan de saleté | Effets | `DirtScanner.scanClip` |
| `tmp_scan_denied` | Scan indisponible (en cours / recharge) | Interface | `DirtScanner.deniedClip` |
| `tmp_milestone` | Palier de propreté (25, 50, 75, 90 %) | Interface | `CleaningProgressFeedback.milestoneClip` |
| `tmp_objective_reached` | Objectif de la commande atteint | Interface | `AudioManager.objectiveClip` |
| `tmp_reward` | Commande livrée et payée | Interface | `OrderManager.rewardClip` |
| `tmp_ui_click` | Clic sur un bouton | Interface | `AudioManager.uiClick` |
| `tmp_ui_confirm` | Confirmation (achat) | Interface | `AudioManager.uiConfirm` |
| `tmp_ui_deny` | Refus : appui sur « utiliser » sans effet possible (réserve de détergent vide…) — deux bips graves descendants (v0.1.2) | Interface | `AudioManager.uiDeny` (joué par `PlayerToolController`) |
| `tmp_workshop_ambient_loop` | Ambiance de l'atelier | Effets | `AmbientAudio.loop` |
| `tmp_footstep_1..3` | Pas (selon la distance parcourue) | Effets | `FootstepAudio.steps` |
| `tmp_music_menu_loop` | Musique du menu : nappe douce et arpège lent, 30 s en boucle (v0.1.2) | Musique | `MainMenuController.menuMusic` |
| `tmp_music_workshop_loop` | Musique de l'atelier : rythme léger, basse et nappe, 24 s en boucle (v0.1.2) | Musique | `GameBootstrap.workshopMusic` |
| `tmp_blower_loop` | Souffle du ventilateur de séchage (son 3D) (v0.1.2) | Effets | Machine « blower » → `loopSource` |
| `tmp_plate_loop` | Vibration de la plaque de dépoussiérage (son 3D) (v0.1.2) | Effets | Machine « duster » → `loopSource` |
