# V0.4.2 — Actualités et patchnotes dynamiques : audit et plan

État : **audit et préparation seulement** (consigne de la V0.4.2 : pas de système distant avant la fin des priorités).
Le contenu local de l'onglet Actualités a été mis à jour (V0.4.2, V0.3.0) ; rien n'est encore chargé depuis Internet.

## 1. Audit de l'existant

- `Scripts/UI/NewsFeed.cs` : lecture pure d'un JSON `{ "format": 1, "items": [ { id, date, version, title, body } ] }`.
  Robuste : fichier vide ou illisible → message d'erreur, jamais d'exception ; entrée invalide ignorée ; titres et textes
  tronqués (80 / 320 caractères) ; au plus 3 annonces, les plus récentes d'abord. Testé en EditMode.
- `Scripts/UI/NewsPanelUI.cs` : charge `Resources/RugWashNews.json` (asynchrone) et affiche les annonces ; le menu les
  présente comme des annonces locales. Aucune connexion réseau, aucune clé.
- Conséquence : changer les actualités demande aujourd'hui une nouvelle compilation et une nouvelle distribution du jeu.

## 2. Solution proposée : fichier public en lecture seule + cache + secours livré

```
[dépôt public « rugwash-news » ou page statique]  --(HTTPS GET, lecture seule)-->  jeu (menu principal)
        news.json (même format que le fichier local)                                 │
        news.json.sig (signature Ed25519, facultative)                               ├─ 1. réponse valide → affichée + mise en cache
                                                                                     ├─ 2. sinon : dernière copie en cache (persistentDataPath)
                                                                                     └─ 3. sinon : Resources/RugWashNews.json (livré avec le jeu)
```

- **Source** : un fichier statique public (GitHub Pages, Cloudflare Pages, ou le « raw » d'un dépôt public dédié).
  Le jeu ne fait que lire : aucune authentification, aucun secret dans le client.
- **Publication** : modifier `news.json` dans le dépôt et pousser ; le jeu l'affiche au prochain lancement du menu.
- **Format** : celui d'aujourd'hui (`format: 1`) plus `updatedAt` et `minGameVersion` (facultatifs). Un format inconnu
  est refusé proprement (secours local).
- **Client** (à écrire) : `NewsRemoteSource` (UnityWebRequest, délai 4 s, une requête par ouverture du menu, en-tête
  `If-None-Match` / ETag), réponse limitée à 64 Ko, analysée par `NewsFeed.Parse` (déjà robuste), balises de texte enrichi
  retirées, liens éventuels limités à une liste blanche de domaines. Le menu s'affiche tout de suite avec le cache ou le
  fichier livré, puis se met à jour si la réponse arrive : jamais d'attente, jamais de plantage.
- **Authenticité (option)** : signature Ed25519 du fichier ; seule la clé **publique** est dans le jeu, la clé privée
  reste sur la machine (ou dans le CI) de celui qui publie. Un fichier modifié par un tiers est ignoré.

## 3. Discord

- Un webhook Discord ne sert qu'à **écrire** dans un salon : il ne permet pas au jeu de lire des actualités.
  Lire un salon demande un jeton de bot : **interdit dans le client** (il serait extrait du jeu).
- Architecture sûre si une publication Discord est voulue : une GitHub Action (ou une petite fonction serverless) se
  déclenche quand `news.json` change et poste le patchnote sur Discord avec un webhook stocké en **secret du CI**.
  Le jeu, lui, continue de lire le fichier public. Aucun webhook, jeton ni clé dans Unity.

## 4. Tests prévus

- EditMode : réponse valide, JSON invalide, format inconnu, fichier trop gros, entrées partielles, textes trop longs,
  balises riches, signature invalide (si activée).
- PlayMode : serveur indisponible (délai) → cache puis fichier livré ; réponse lente qui arrive après l'ouverture du menu.

## 5. Étapes (après stabilisation de la V0.4.2)

1. Créer le dépôt public des actualités et y copier `RugWashNews.json`.
2. `NewsRemoteSource` + cache + secours, branchés dans `NewsPanelUI` (même affichage).
3. Option : signature Ed25519 et Action de publication Discord.
4. Tests ci-dessus, puis validation hors ligne (câble débranché) et en ligne.
