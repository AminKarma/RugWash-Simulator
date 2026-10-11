# Audit — Phase 0

**Constat initial** : dossier projet vide, aucun éditeur Unity installé. Le projet a été créé
de zéro puis ouvert avec Unity **6000.5.7f1** (installé par l'utilisateur).

| Point | Résultat |
|---|---|
| Version Unity | 6000.5.7f1 (`ProjectSettings/ProjectVersion.txt`) |
| Pipeline | URP 17.5.0 (choisi faute de pipeline existant ; Forward+) |
| Packages | URP 17.5.0, Input System 1.20.0, uGUI 2.5.0, Test Framework 1.7.0, Mathematics 1.4.0, VS IDE 2.0.26 — alignés sur les versions intégrées à l'éditeur |
| Entrées | Input System seul (`activeInputHandler: 1`) |
| Scènes | MainMenu / Workshop / Results enregistrées ; Workshop générée par code |
| Réutilisable | Outillage Phase 0 (bootstrapper, générateur de scène, CI) conservé et étendu |

**Particularités découvertes en exécutant réellement Unity**
- Unity 6.5 : `Unity.Mathematics` est fourni par le module moteur `UnityEngine.MathematicsModule`.
- `EditorSceneManager.NewScene(Single)` décharge les assets créés juste avant : le contenu est
  chargé après la création de la scène.
- Mode batch : aucune fenêtre n'a le focus, `Cursor.lockState` reste `None` → le verrouillage réel
  du curseur se vérifie manuellement.
- PowerShell 5.1 : `Start-Process -Wait` attend aussi les processus enfants de Unity, et `ExitCode`
  reste vide sans capture préalable de `Handle` (corrigé dans `Tools/RugWash-CI.ps1`).

**Risques techniques identifiés**
- Pas d'audio ni de modèles 3D : substituts procéduraux documentés.
- Performance non mesurée en conditions réelles (pas de build ni de mesure 1080p à ce stade).
- Rendu des poils longs volontairement simplifié (pas de shell rendering).
