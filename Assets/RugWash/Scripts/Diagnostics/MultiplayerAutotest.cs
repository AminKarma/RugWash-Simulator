using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Network;
using RugWash.Orders;
using RugWash.Save;
using RugWash.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RugWash.Diagnostics
{
    /// <summary>
    /// Vérification automatique du multijoueur entre deux processus du jeu compilé (hôte et invité, connexion directe
    /// locale). Lancée par <c>-rugwash-mp-test host|guest -rugwash-mp-port N -rugwash-mp-report fichier.json</c> ;
    /// sans ces arguments, rien n'est créé. Chaque rôle écrit un rapport (contrôles, empreinte du tapis, débits) puis
    /// quitte ; l'étape « multiplayer » de RugWash-CI compare les deux rapports.
    /// </summary>
    public static class MultiplayerAutotest
    {
        public const string Argument = "-rugwash-mp-test";
        public const int HostStartMoney = 300;
        public const int GuestStartMoney = 1500;
        public const int GuestStartReputation = 2;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, Argument);
            if (index < 0 || index + 1 >= args.Length) return;
            string role = args[index + 1];
            ushort port = 7795;
            string report = Path.Combine(Application.persistentDataPath, $"mp-{role}.json");
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-rugwash-mp-port") ushort.TryParse(args[i + 1], out port);
                if (args[i] == "-rugwash-mp-report") report = args[i + 1];
            }

            // Sauvegarde temporaire propre à chaque rôle : jamais celle du joueur. Chaque rôle part d'une carrière différente
            // (argent, réputation) pour vérifier que la progression de l'un n'est jamais copiée chez l'autre.
            string sandbox = Path.Combine(Application.temporaryCachePath, $"mp-save-{role}");
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true);
            Directory.CreateDirectory(sandbox);
            SaveManager.DirectoryOverride = sandbox;
            var seeded = new SaveData { money = role == "host" ? HostStartMoney : GuestStartMoney, reputation = role == "host" ? 0 : GuestStartReputation };
            new SaveFileStore(sandbox, SaveManager.DefaultFileName).Save(seeded, out _);

            var go = new GameObject("[MultiplayerAutotest]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var runner = go.AddComponent<MultiplayerAutotestRunner>();
            runner.Configure(role == "host", port, report, sandbox);
        }

        /// <summary>Empreinte (FNV-1a) des valeurs quantifiées de la grille, identique chez l'hôte et l'invité si les tapis concordent.</summary>
        public static uint Hash(RugCleaningGrid grid)
        {
            uint hash = 2166136261;
            void Mix(byte value)
            {
                hash ^= value;
                hash *= 16777619;
            }
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var cell = grid.GetCell(x, y);
                Mix(Q(cell.Dust)); Mix(Q(cell.Stain)); Mix(Q(cell.Loosened * 0.5f)); Mix(Q(cell.Foam)); Mix(Q(cell.Moisture));
                Mix(Q(cell.Residue)); Mix(Q(cell.Wear)); Mix((byte)cell.DustType); Mix((byte)cell.StainType);
            }
            return hash;
        }

        static byte Q(float v) => (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);
    }

    sealed class MultiplayerAutotestRunner : MonoBehaviour
    {
        bool isHost;
        ushort port;
        string reportPath;
        string saveFolder;
        readonly List<(string name, bool passed, string detail)> checks = new List<(string, bool, string)>();
        readonly Dictionary<string, string> values = new Dictionary<string, string>();
        readonly List<float> frames = new List<float>();
        bool measuring;
        string fatal;
        MultiplayerSession session;
        GameBootstrap game;

        public void Configure(bool host, ushort listenPort, string report, string sandbox)
        {
            isHost = host;
            port = listenPort;
            reportPath = report;
            saveFolder = sandbox;
        }

        void Record(string name, bool passed, string detail = "") => checks.Add((name, passed, detail ?? string.Empty));

        IEnumerator WaitFor(Func<bool> condition, float timeout)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        }

        void Update()
        {
            if (measuring) frames.Add(Time.unscaledDeltaTime);
        }

        IEnumerator Start()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 120;
            float started = Time.realtimeSinceStartup;
            yield return isHost ? RunHost() : RunGuest();
            values["dureeSecondes"] = (Time.realtimeSinceStartup - started).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            WriteReport();
            Application.Quit(fatal == null && checks.TrueForAll(c => c.passed) ? 0 : 1);
        }

        IEnumerator OpenMenuSession(string name)
        {
            yield return WaitFor(() => FindAnyObjectByType<MainMenuController>() != null, 30f);
            var menu = FindAnyObjectByType<MainMenuController>();
            if (menu == null)
            {
                fatal = "menu principal introuvable";
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
            menu.Multiplayer.Open();
            session = menu.Multiplayer.Session;
            session.LocalName = name;
            menu.Multiplayer.Close();
        }

        IEnumerator WaitForWorkshop()
        {
            yield return WaitFor(() => NetworkWorkshop.Instance != null && NetworkWorkshop.Instance.IsSpawned && NetworkWorkshop.Instance.Game != null, 60f);
            game = NetworkWorkshop.Instance != null ? NetworkWorkshop.Instance.Game : null;
        }

        static int AvatarCount() => FindObjectsByType<NetworkPlayerAvatar>().Length;

        void Aim(Vector2 uv)
        {
            Vector3 target = game.Rug.UVToWorld(uv);
            float pitch = Mathf.Atan2(1.68f - target.y, 1.1f) * Mathf.Rad2Deg;
            game.PlayerController.Teleport(new Vector3(target.x, 0.06f, target.z - 1.1f), 0f, pitch);
        }

        IEnumerator UseTool(int slot, Vector2 uv, float seconds)
        {
            Aim(uv);
            game.ToolController.SelectSlot(slot);
            yield return null;
            game.ToolController.AutomationUseOverride = true;
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                Aim(new Vector2(uv.x + 0.06f * Mathf.Sin(Time.realtimeSinceStartup * 3f), uv.y + 0.08f * Mathf.Cos(Time.realtimeSinceStartup * 2f)));
                yield return null;
            }
            game.ToolController.AutomationUseOverride = false;
            yield return null;
        }

        static float DustAround(RugCleaningGrid grid, Vector2 uv, int half)
        {
            var c = grid.UVToCell(uv);
            float total = 0f;
            for (int y = Mathf.Max(0, c.y - half); y <= Mathf.Min(grid.Height - 1, c.y + half); y++)
            for (int x = Mathf.Max(0, c.x - half); x <= Mathf.Min(grid.Width - 1, c.x + half); x++)
                total += grid.GetCell(x, y).Dirt;
            return total;
        }

        static readonly Vector2 HostArea = new Vector2(0.28f, 0.5f);
        static readonly Vector2 GuestArea = new Vector2(0.72f, 0.5f);

        // ---------------------------------------------------------------- Hôte

        IEnumerator RunHost()
        {
            yield return OpenMenuSession("Hôte");
            if (session == null) yield break;
            Record("Partie hébergée (connexion directe)", session.HostDirect(port), session.Message);
            yield return WaitForWorkshop();
            Record("Atelier réseau prêt chez l'hôte", game != null, session.State.ToString());
            if (game == null) { fatal = "atelier hôte"; yield break; }

            yield return WaitFor(() => AvatarCount() >= 2, 90f);
            Record("L'invité a rejoint l'atelier", AvatarCount() >= 2, $"{AvatarCount()} joueur(s)");
            if (AvatarCount() < 2) { fatal = "invité absent"; yield break; }

            ulong guestId = 0;
            foreach (ulong id in NetworkWorkshop.Instance.NetworkManager.ConnectedClientsIds)
                if (id != NetworkWorkshop.Instance.NetworkManager.LocalClientId) guestId = id;
            yield return WaitFor(() => NetworkWorkshop.Instance.CareerOf(guestId) != null, 30f);
            var guestCareer = NetworkWorkshop.Instance.CareerOf(guestId);
            Record("Carrière personnelle de l'invité reçue et validée par l'hôte", guestCareer != null && guestCareer.Money == MultiplayerAutotest.GuestStartMoney
                && guestCareer.Reputation == MultiplayerAutotest.GuestStartReputation, guestCareer != null ? $"{guestCareer.Money} €, réputation {guestCareer.Reputation}" : "-");
            Record("Progression de l'hôte non remplacée par celle de l'invité", game.Wallet.Balance == MultiplayerAutotest.HostStartMoney,
                $"{game.Wallet.Balance} €");

            var order = game.Orders.Book.Orders[0];
            int ownedAtStart = game.Upgrades.OwnedIds.Count;
            // Gains suivis à chaque variation du solde : un double paiement serait vu même si un achat suit.
            int positiveChanges = 0, gained = 0;
            game.Wallet.Wallet.Changed += (balance, delta) =>
            {
                if (delta <= 0) return;
                positiveChanges++;
                gained += delta;
            };
            Record("Commande acceptée par l'hôte", game.Orders.TryStart(order) == OrderStartResult.Started, order.title);
            // Mesure immédiate : l'invité ne peut pas encore avoir nettoyé ce tapis tout neuf.
            float guestAreaBefore = DustAround(game.Rug.Grid, GuestArea, 8);
            yield return new WaitForSeconds(2f);

            measuring = true;
            yield return UseTool(1, HostArea, 5f);
            // L'hôte se déplace : l'invité doit voir sa silhouette bouger.
            for (float t = 0f; t < 2f; t += Time.deltaTime)
            {
                game.PlayerController.Teleport(new Vector3(-2f + t, 0.06f, -1f), t * 90f, 10f);
                yield return null;
            }
            measuring = false;

            yield return WaitFor(() => DustAround(game.Rug.Grid, GuestArea, 8) < guestAreaBefore * 0.9f, 40f);
            float guestAreaAfter = DustAround(game.Rug.Grid, GuestArea, 8);
            Record("Nettoyage de l'invité appliqué sur le tapis de l'hôte", guestAreaAfter < guestAreaBefore * 0.9f,
                $"saleté zone invité {guestAreaBefore:0.0} → {guestAreaAfter:0.0}");
            values["operationsInvite"] = NetworkWorkshop.Instance.HostSync.OperationsApplied.ToString();
            values["messagesRefuses"] = NetworkWorkshop.Instance.HostSync.RejectedMessages.ToString();

            // Rendu présentable (raccourci de test) : l'invité demandera la livraison.
            CleanThoroughly(game.Rug.Grid);
            OrderResult delivered = null;
            game.Orders.OrderFinished += result => delivered = result;
            yield return WaitFor(() => delivered != null, 60f);
            var afterDelivery = NetworkWorkshop.Instance.CareerOf(guestId);
            yield return new WaitForSeconds(4f);
            Record("Livraison demandée par l'invité, validée par l'hôte", delivered != null && delivered.Accepted,
                delivered == null ? "aucune" : $"note {OrderEvaluator.GradeLabel(delivered.Grade)}");
            Record("Récompense versée une seule fois (deux demandes de livraison)", delivered != null && positiveChanges == 1 && gained == delivered.Reward,
                delivered == null ? "-" : $"{positiveChanges} versement(s), {gained} € pour {delivered.Reward} €");
            values["argentHote"] = game.Wallet.Balance.ToString();
            Record("Récompense versée aussi dans la carrière de l'invité (une fois)", delivered != null && afterDelivery != null
                && afterDelivery.Money == MultiplayerAutotest.GuestStartMoney + delivered.Reward,
                afterDelivery != null && delivered != null ? $"{afterDelivery.Money} € (départ {MultiplayerAutotest.GuestStartMoney} + {delivered.Reward})" : "-");

            // Débris secs sur l'aire de lavage : le robot (que l'invité va démarrer) a réellement de quoi travailler.
            var pad = game.WorkSurface.Surface.Grid;
            var debris = new DepositOperation { Type = DirtType.Dust, Debris = 0.3f };
            pad.ApplyRect(new RectInt(0, 0, pad.Width, 6), 1f, ref debris);
            pad.ApplyRect(new RectInt(0, pad.Height - 6, pad.Width, 6), 1f, ref debris);

            int hostMoneyBeforePurchase = game.Wallet.Balance;
            var upgradeId = game.Upgrades.Catalog[0].id;
            yield return WaitFor(() => NetworkWorkshop.Instance.CareerOf(guestId) != null && NetworkWorkshop.Instance.CareerOf(guestId).Upgrades.Contains(upgradeId), 40f);
            var afterPurchase = NetworkWorkshop.Instance.CareerOf(guestId);
            Record("Achat de l'invité validé par l'hôte et payé avec l'argent de l'invité (hôte inchangé)",
                afterPurchase != null && afterPurchase.Upgrades.Contains(upgradeId) && game.Upgrades.OwnedIds.Count == ownedAtStart
                && game.Wallet.Balance == hostMoneyBeforePurchase,
                afterPurchase != null ? $"invité {afterPurchase.Money} €, hôte {game.Wallet.Balance} €, achats hôte {game.Upgrades.OwnedIds.Count}" : "-");
            values["carriereInviteVueParHote"] = afterPurchase != null ? afterPurchase.Money.ToString() : "-";
            values["argentApresAchat"] = game.Wallet.Balance.ToString();

            yield return WaitFor(() => game.Robot.Mode != Workshop.RobotMode.Docked, 40f);
            Record("Robot démarré à la demande de l'invité", game.Robot.Mode != Workshop.RobotMode.Docked, game.Robot.Mode.ToString());
            game.Robot.Toggle();

            yield return WaitFor(() => AvatarCount() < 2, 90f);
            yield return new WaitForSeconds(1f);
            Record("Départ de l'invité géré (hôte toujours en partie)", AvatarCount() == 1 && session.InSession && session.State == SessionState.InGame,
                $"{AvatarCount()} joueur(s), {session.State}");
            values["empreinteTapis"] = MultiplayerAutotest.Hash(game.Rug.Grid).ToString();
            values["octetsSurfacesEnvoyes"] = NetworkWorkshop.Instance.SurfaceBytesSent.ToString();
            values["fpsMoyenNettoyage"] = AverageFps().ToString("0", System.Globalization.CultureInfo.InvariantCulture);

            game.SaveSystem.SaveNow("fin du test multijoueur");
            var result = new SaveFileStore(saveFolder, SaveManager.DefaultFileName).Load();
            var data = result.Data;
            bool loaded = data != null && File.Exists(Path.Combine(saveFolder, SaveManager.DefaultFileName));
            Record("Progression de l'atelier sauvegardée chez l'hôte", loaded && data.money == game.Wallet.Balance && data.completedOrders.Contains(order.id)
                && !data.upgrades.Contains(upgradeId), loaded ? $"{data.money} €, achats {data.upgrades.Count}" : "illisible");
            session.Leave();
            yield return WaitFor(() => SceneManager.GetActiveScene().name == "MainMenu", 20f);
        }

        // ---------------------------------------------------------------- Invité

        IEnumerator RunGuest()
        {
            yield return OpenMenuSession("Invité");
            if (session == null) yield break;
            // L'hôte peut démarrer après l'invité : nouvelles tentatives pendant 60 s.
            float end = Time.realtimeSinceStartup + 60f;
            while (NetworkWorkshop.Instance == null && Time.realtimeSinceStartup < end)
            {
                if (!session.InSession) session.JoinDirect("127.0.0.1", port);
                float attempt = Time.realtimeSinceStartup + 6f;
                while (NetworkWorkshop.Instance == null && session.InSession && Time.realtimeSinceStartup < attempt) yield return null;
                if (NetworkWorkshop.Instance == null) yield return new WaitForSeconds(1f);
            }
            yield return WaitForWorkshop();
            yield return WaitFor(() => NetworkWorkshop.Instance != null && NetworkWorkshop.Instance.ClientSync != null
                && NetworkWorkshop.Instance.ClientSync.IsReady(NetworkWorkshop.RugId), 20f);
            Record("Connecté et synchronisé avec l'hôte", game != null && NetworkWorkshop.Instance.ClientSync != null && NetworkWorkshop.Instance.ClientSync.IsReady(NetworkWorkshop.RugId),
                session.State.ToString());
            if (game == null) { fatal = "atelier invité"; yield break; }
            Record("Le tapis est une réplique (l'hôte fait autorité)", game.Rug.IsReplica);
            Record("État de chargement affiché à l'arrivée (contrôles coupés)", NetworkWorkshop.Instance.ShowedLoading);
            yield return WaitFor(() => NetworkWorkshop.Instance.GuestReady, 30f);
            yield return new WaitForSeconds(0.3f);
            string expectedMoney = Economy.WalletManager.Format(MultiplayerAutotest.GuestStartMoney);
            Record("HUD initialisé dès l'arrivée avec la progression personnelle (pas celle de l'hôte)",
                NetworkWorkshop.Instance.GuestReady && !game.GameState.Synchronizing && !game.Hud.IsLoading
                && game.Wallet.Balance == MultiplayerAutotest.GuestStartMoney && game.Reputation.Points == MultiplayerAutotest.GuestStartReputation
                && game.Hud.EconomyText.Contains(expectedMoney),
                $"HUD « {game.Hud.EconomyText.Replace('\n', ' ')} » {GameplayState()}");

            yield return WaitFor(() => AvatarCount() >= 2, 20f);
            NetworkPlayerAvatar hostAvatar = null;
            foreach (var avatar in FindObjectsByType<NetworkPlayerAvatar>())
                if (!avatar.IsOwner) hostAvatar = avatar;
            Record("Silhouette de l'hôte visible", hostAvatar != null, hostAvatar != null ? hostAvatar.DisplayName : "-");

            yield return WaitFor(() => game.Orders.ActiveOrder != null, 40f);
            Record("Commande de l'hôte reçue (état partagé)", game.Orders.ActiveOrder != null, game.Orders.ActiveOrder != null ? game.Orders.ActiveOrder.title : "-");
            yield return new WaitForSeconds(1f);

            Vector3 hostStart = hostAvatar != null ? hostAvatar.transform.position : Vector3.zero;
            float before = DustAround(game.Rug.Grid, GuestArea, 8);
            measuring = true;
            yield return UseTool(1, GuestArea, 5f);
            measuring = false;
            float after = DustAround(game.Rug.Grid, GuestArea, 8);
            Record("Nettoyage de l'invité visible chez lui (prédiction)", after < before * 0.9f, $"{before:0.0} → {after:0.0} {GameplayState()}");
            values["operationsEnvoyees"] = NetworkWorkshop.Instance.ClientSync.Predictor.LastSequence.ToString();

            yield return new WaitForSeconds(2.5f);
            float moved = hostAvatar != null ? Vector3.Distance(hostStart, hostAvatar.transform.position) : 0f;
            Record("Déplacement de l'hôte vu par l'invité", moved > 0.5f, $"{moved:0.00} m");
            float hostAreaDust = DustAround(game.Rug.Grid, HostArea, 8);

            var target = game.Orders.ActiveOrder.objectives.targetCleanliness;
            yield return WaitFor(() => game.Rug.Cleanliness01 >= target, 60f);
            Record("Nettoyage de l'hôte reçu (tapis présentable)", game.Rug.Cleanliness01 >= target, $"{game.Rug.Cleanliness01:P1}");
            game.Orders.Deliver();
            game.Orders.Deliver();
            yield return WaitFor(() => game.Results.IsVisible, 30f);
            Record("Résultats de la livraison affichés chez l'invité", game.Results.IsVisible && game.Orders.LastResult != null,
                game.Orders.LastResult != null ? OrderEvaluator.GradeLabel(game.Orders.LastResult.Grade) : "-");
            int reward = game.Orders.LastResult != null ? game.Orders.LastResult.Reward : 0;
            yield return WaitFor(() => game.Wallet.Balance == MultiplayerAutotest.GuestStartMoney + reward, 20f);
            Record("Récompense reçue dans la carrière personnelle de l'invité", reward > 0 && game.Wallet.Balance == MultiplayerAutotest.GuestStartMoney + reward,
                $"{game.Wallet.Balance} € (départ {MultiplayerAutotest.GuestStartMoney} + {reward})");
            values["argentVuParInvite"] = game.Wallet.Balance.ToString();
            game.Results.BackToWorkshop();
            yield return null;

            var upgrade = game.Upgrades.Catalog[0];
            int moneyBeforePurchase = game.Wallet.Balance;
            var request = game.Shop.TryPurchase(upgrade);
            yield return WaitFor(() => game.Upgrades.IsOwned(upgrade), 30f);
            Record("Achat demandé à l'hôte puis reçu, payé avec l'argent de l'invité", request == Economy.PurchaseResult.Requested && game.Upgrades.IsOwned(upgrade)
                && game.Wallet.Balance == moneyBeforePurchase - upgrade.price, $"{upgrade.displayName} : {moneyBeforePurchase} € → {game.Wallet.Balance} €");
            // Client modifié : nouvelle annonce de carrière avec une fortune et tous les achats. L'hôte l'ignore.
            int honest = game.Wallet.Balance;
            var forged = new Economy.PlayerCareer { Money = 999999, Reputation = 999 };
            foreach (var any in game.Upgrades.Catalog) forged.Upgrades.Add(any.id);
            NetworkWorkshop.Instance.SendForgedCareerForDiagnostics(forged);
            yield return new WaitForSeconds(2f);
            Record("Annonce de carrière forgée en cours de partie ignorée par l'hôte", game.Wallet.Balance == honest
                && game.Upgrades.OwnedIds.Count == 1, $"{game.Wallet.Balance} €, {game.Upgrades.OwnedIds.Count} achat(s)");
            values["argentInvite"] = game.Wallet.Balance.ToString();
            values["argentApresAchatVuParInvite"] = game.Wallet.Balance.ToString();

            game.Robot.Toggle();
            yield return WaitFor(() => game.Robot.Mode != Workshop.RobotMode.Docked, 30f);
            Record("Robot démarré via l'hôte, état reçu", game.Robot.Mode != Workshop.RobotMode.Docked, game.Robot.Mode.ToString());

            // État stable : empreinte du tapis à comparer avec celle de l'hôte.
            yield return new WaitForSeconds(4f);
            values["empreinteTapis"] = MultiplayerAutotest.Hash(game.Rug.Grid).ToString();
            values["predictionsEnAttente"] = NetworkWorkshop.Instance.ClientSync.Predictor.PendingCount.ToString();
            values["octetsOperationsEnvoyes"] = NetworkWorkshop.Instance.OperationBytesSent.ToString();
            values["fpsMoyenNettoyage"] = AverageFps().ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            values["zoneHoteNettoyee"] = hostAreaDust.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

            session.Leave();
            yield return WaitFor(() => SceneManager.GetActiveScene().name == "MainMenu" && FindAnyObjectByType<MainMenuController>() != null, 20f);
            Record("Retour au menu après le départ", SceneManager.GetActiveScene().name == "MainMenu" && !session.InSession);
            var data = new SaveFileStore(saveFolder, SaveManager.DefaultFileName).Load().Data;
            bool exists = data != null;
            Record("Progression personnelle de l'invité sauvegardée chez lui (argent, achat, commande livrée ; rien de l'atelier de l'hôte)",
                exists && data.money.ToString() == values["argentInvite"] && data.upgrades.Contains(upgrade.id) && data.completedOrders.Count == 1
                && string.IsNullOrEmpty(data.workshop.workSurface),
                exists ? $"{data.money} €, achats {data.upgrades.Count}, commandes {data.completedOrders.Count}, aire de lavage « {data.workshop.workSurface.Length} car. »" : "illisible");
        }

        /// <summary>Diagnostic : pourquoi le joueur contrôle (ou non) son personnage.</summary>
        string GameplayState() =>
            $"[prêt {NetworkWorkshop.Instance?.GuestReady}, synchro {game.GameState.Synchronizing}, chargement {game.Hud.IsLoading}, " +
            $"actif {game.GameState.IsGameplayActive}, fenêtres {game.GameState.OpenModals}, état {game.GameState.State}, " +
            $"outil {(game.ToolController.ActiveTool != null ? game.ToolController.ActiveTool.name : "-")}, " +
            $"visée {(game.ToolController.CurrentAim.Rug != null ? game.ToolController.CurrentAim.Rug.name : "-")}]";

        float AverageFps()
        {
            if (frames.Count == 0) return 0f;
            float total = 0f;
            foreach (var f in frames) total += f;
            return frames.Count / Mathf.Max(0.001f, total);
        }

        static void CleanThoroughly(RugCleaningGrid grid)
        {
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var cell = grid.GetCell(x, y);
                cell.Dust = cell.Stain = cell.Loosened = cell.Foam = cell.Moisture = cell.Residue = 0f;
                cell.DustType = cell.StainType = DirtType.None;
                cell.Product = 0;
                cell.Adhesion = 0f;
                grid.SetCell(x, y, cell);
            }
        }

        void WriteReport()
        {
            var json = new StringBuilder();
            json.Append("{\n");
            json.Append($"  \"role\": \"{(isHost ? "host" : "guest")}\",\n");
            json.Append($"  \"fatal\": {(fatal == null ? "null" : $"\"{Escape(fatal)}\"")},\n");
            json.Append("  \"values\": {");
            int v = 0;
            foreach (var pair in values)
                json.Append(v++ == 0 ? "\n" : ",\n").Append($"    \"{Escape(pair.Key)}\": \"{Escape(pair.Value)}\"");
            json.Append("\n  },\n  \"checks\": [\n");
            for (int i = 0; i < checks.Count; i++)
                json.Append($"    {{ \"name\": \"{Escape(checks[i].name)}\", \"passed\": {(checks[i].passed ? "true" : "false")}, \"detail\": \"{Escape(checks[i].detail)}\" }}{(i < checks.Count - 1 ? "," : "")}\n");
            json.Append("  ]\n}\n");
            try
            {
                File.WriteAllText(reportPath, json.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogError($"[RugWash] Rapport multijoueur non écrit : {e.Message}");
            }
        }

        static string Escape(string text) => (text ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
