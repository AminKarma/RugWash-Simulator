using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Network;
using RugWash.Orders;
using RugWash.Player;
using RugWash.Pranks;
using RugWash.Save;
using RugWash.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RugWash.Diagnostics
{
    /// <summary>
    /// Vérification automatique du multijoueur entre processus du jeu compilé (connexion directe locale). Lancée par
    /// <c>-rugwash-mp-test host|guest|guest2 -rugwash-mp-port N -rugwash-mp-report fichier.json</c> ; sans ces arguments,
    /// rien n'est créé. Scénario (grande mise à jour : progression propre à chaque joueur) : l'invité arrive pour la première
    /// fois sur son emplacement multijoueur, sa progression de départ est copiée depuis l'hôte, puis chacun a la sienne (achats
    /// et récompenses dans SA carrière, jamais dans celle de l'autre) ; chat textuel dans les deux sens ; l'invité « crashe »
    /// (processus tué) ; le même joueur se reconnecte (guest2, même identité, même dossier) et retrouve SA progression (rien
    /// n'est recopié) ; l'hôte se ferme brutalement. La sauvegarde solo de l'invité n'est jamais touchée. Chaque rôle écrit un
    /// rapport ; l'étape « multiplayer » de RugWash-CI les compare et relit les sauvegardes.
    /// </summary>
    public static class MultiplayerAutotest
    {
        public const string Argument = "-rugwash-mp-test";
        public const int HostStartMoney = 300;
        /// <summary>Progression SOLO de l'invité : jamais utilisée ni modifiée par une partie en ligne (emplacements séparés).</summary>
        public const int GuestStartMoney = 1500;
        public const int GuestStartReputation = 2;
        /// <summary>Achat des sauvegardes personnelles de l'invité : absent de la partie de l'hôte (aucune liaison).</summary>
        public const string GuestOnlyUpgrade = "up_boots";
        /// <summary>Argent de l'emplacement multijoueur personnel de l'invité : jamais lu ni modifié quand il rejoint (V0.2.5).</summary>
        public const int GuestOwnMultiMoney = 777;
        /// <summary>Identité de l'invité (-rugwash-player-key) : sa fiche dans la sauvegarde de l'hôte.</summary>
        public const string GuestKey = "mp-guest";
        /// <summary>Achats personnels de l'invité déjà enregistrés dans la sauvegarde de l'hôte (venue précédente).</summary>
        public static readonly string[] GuestPersonal =
            { "up_tool_vacuum", "up_tool_rinse", "up_tool_extractor", "up_tool_foam", "up_tool_broom", "up_tool_squeegee", "up_prank_pee" };
        /// <summary>Ce que porte l'invité à son arrivée (fiche) : la raclette, achetée et en dernier, tombera sous la batte.</summary>
        public static readonly string[] GuestCarried = { "soap", "brush", "vacuum", "squeegee" };
        /// <summary>Objet perdu par l'invité sous la batte (que l'hôte ne possède pas : il le ramasse puis le revend).</summary>
        public const string DroppedUpgrade = "up_tool_squeegee";
        /// <summary>Achat de l'invité pendant la partie (amélioration personnelle, payée avec l'argent de la session).</summary>
        public const string GuestPurchase = "up_water_1";
        /// <summary>Objet revendu par l'invité (rangé au casier) : moitié du prix de base, dans l'argent de la session.</summary>
        public const string GuestSale = "up_tool_broom";
        /// <summary>Amélioration de l'atelier demandée par l'invité : réservée à l'hôte, refusée par lui.</summary>
        public const string WorkshopUpgrade = "up_machine_duster";
        /// <summary>Emplacement multijoueur hébergé par l'hôte (sauvegarde de la session) ; celui de l'invité doit rester intact.</summary>
        public static readonly SaveSlot Slot = new SaveSlot(SaveMode.Multiplayer, 1);
        public const string ChatFromGuest = "Bonjour depuis l'invité";
        public const string ChatFromHost = "Bienvenue dans l'atelier";
        /// <summary>Apparence choisie par l'invité (menu Personnage) : l'hôte doit la voir sur sa silhouette.</summary>
        public static readonly CharacterLook GuestLook = new CharacterLook { outfit = 3, cap = 2, gloves = 1 };

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

            // Sauvegarde temporaire propre à chaque joueur, jamais celle du vrai joueur. guest2 est le même joueur que guest
            // revenu après un crash : il reprend SON dossier tel que le crash l'a laissé (rien n'est recréé).
            bool reconnecting = role == "guest2";
            string sandbox = Path.Combine(Application.temporaryCachePath, $"mp-save-{(role == "host" ? "host" : "guest")}");
            SaveManager.DirectoryOverride = sandbox;
            SaveSlots.Selected = Slot;
            Player.PlayerInventory.AutomationCarriesOnSelect = true;
            if (!reconnecting)
            {
                if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true);
                Directory.CreateDirectory(sandbox);
                if (role == "host") new SaveFileStore(sandbox, Slot.FileName).Save(Seed(true), out _);
                else
                {
                    // Invité : une partie SOLO et son propre emplacement multijoueur — ni l'un ni l'autre ne doit être lu ou
                    // modifié quand il rejoint l'hôte ; son apparence est dans son profil (commun à ses emplacements).
                    new SaveFileStore(sandbox, SaveManager.DefaultFileName).Save(Seed(false), out _);
                    new SaveFileStore(sandbox, Slot.FileName).Save(GuestOwnMulti(), out _);
                    var profile = new PlayerProfile { character = GuestLook, lastMultiSlot = Slot.Index };
                    new ProfileStore(sandbox).Save(profile, out _);
                }
            }

            var go = new GameObject("[MultiplayerAutotest]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var runner = go.AddComponent<MultiplayerAutotestRunner>();
            runner.Configure(role, port, report, sandbox);
        }

        /// <summary>
        /// Sauvegardes de départ : l'emplacement multijoueur de l'hôte (sauvegarde de la session, avec la fiche de l'invité
        /// d'une venue précédente), et la partie SOLO de l'invité (argent, réputation, achats différents) — elle ne doit
        /// jamais changer pendant une partie en ligne.
        /// </summary>
        public static SaveData Seed(bool host)
        {
            var seeded = new SaveData
            {
                money = host ? HostStartMoney : GuestStartMoney, reputation = host ? 0 : GuestStartReputation,
                upgrades = new List<string>(SaveMigration.LegacyToolUnlocks), detergentStock = SaveMigration.LegacyDetergentStock,
                mode = host ? Slot.ModeKey : SaveSlot.KeyOf(SaveMode.Solo), slotIndex = host ? Slot.Index : 1,
            };
            seeded.upgrades.AddRange(SaveMigration.LegacyWorkshopUnlocks);
            seeded.upgrades.AddRange(SaveMigration.LegacyDecorUnlocks);
            if (host)
            {
                seeded.upgrades.AddRange(new[] { "up_base_2", "up_machine_blower", "up_prank_bat", "up_prank_pee" });
                seeded.members.Add(new SessionMemberData
                {
                    key = GuestKey, name = "Invité", upgrades = new List<string>(GuestPersonal), carried = new List<string>(GuestCarried),
                });
            }
            else
            {
                seeded.upgrades.Add(GuestOnlyUpgrade);
                seeded.character = GuestLook;
            }
            return seeded;
        }

        /// <summary>Emplacement multijoueur personnel de l'invité : il ne sert pas quand il rejoint, et ne doit jamais changer.</summary>
        public static SaveData GuestOwnMulti() => new SaveData
        {
            money = GuestOwnMultiMoney, reputation = 9, upgrades = new List<string> { GuestOnlyUpgrade }, mode = Slot.ModeKey, slotIndex = Slot.Index,
        };

        /// <summary>L'emplacement multijoueur personnel de l'invité est-il exactement celui de départ ?</summary>
        public static bool GuestMultiUntouched(SaveData data, out string detail)
        {
            var seed = GuestOwnMulti();
            detail = data == null ? "illisible" : $"{data.money} €, réputation {data.reputation}, achats {data.upgrades.Count}, fiches {data.members.Count}";
            return data != null && data.money == seed.money && data.reputation == seed.reputation && data.upgrades.Count == 1
                && data.upgrades.Contains(GuestOnlyUpgrade) && data.members.Count == 0 && data.ordersDelivered == 0;
        }

        /// <summary>La partie SOLO de l'invité est-elle exactement celle de départ (argent, réputation, achats, livraisons) ?</summary>
        public static bool GuestSaveUntouched(SaveData data, out string detail)
        {
            var seed = Seed(false);
            if (data == null)
            {
                detail = "illisible";
                return false;
            }
            var expected = new HashSet<string>(seed.upgrades);
            bool same = data.money == seed.money && data.reputation == seed.reputation && expected.SetEquals(data.upgrades)
                && data.completedOrders.Count == 0 && data.ordersDelivered == 0
                && string.IsNullOrEmpty(data.workshop.workSurface);
            detail = $"{data.money} €, réputation {data.reputation}, achats {data.upgrades.Count}, livraisons {data.ordersDelivered}, " +
                     $"aire de lavage « {data.workshop.workSurface.Length} car. »";
            return same;
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

        /// <summary>Diagnostic d'un écart d'empreinte : totaux quantifiés par grandeur, puis empreinte de chaque ligne.</summary>
        public static string HashDetail(RugCleaningGrid grid)
        {
            long dust = 0, stain = 0, loose = 0, foam = 0, moisture = 0, residue = 0, wear = 0;
            var rows = new StringBuilder();
            for (int y = 0; y < grid.Height; y++)
            {
                uint row = 2166136261;
                for (int x = 0; x < grid.Width; x++)
                {
                    var cell = grid.GetCell(x, y);
                    dust += Q(cell.Dust); stain += Q(cell.Stain); loose += Q(cell.Loosened * 0.5f); foam += Q(cell.Foam);
                    moisture += Q(cell.Moisture); residue += Q(cell.Residue); wear += Q(cell.Wear);
                    row = (row ^ (uint)(Q(cell.Dust) + 3 * Q(cell.Stain) + 5 * Q(cell.Loosened * 0.5f) + 7 * Q(cell.Foam) + 11 * Q(cell.Moisture)
                                        + 13 * Q(cell.Residue) + 17 * Q(cell.Wear) + 19 * (int)cell.DustType + 23 * (int)cell.StainType)) * 16777619;
                }
                rows.Append(row % 997).Append(y < grid.Height - 1 ? "," : string.Empty);
            }
            return $"poussière {dust} tache {stain} détaché {loose} mousse {foam} humidité {moisture} résidu {residue} usure {wear} | lignes {rows}";
        }
    }

    sealed class MultiplayerAutotestRunner : MonoBehaviour
    {
        string role;
        ushort port;
        string reportPath;
        string saveFolder;
        readonly List<(string name, bool passed, string detail)> checks = new List<(string, bool, string)>();
        readonly Dictionary<string, string> values = new Dictionary<string, string>();
        readonly List<float> frames = new List<float>();
        bool measuring;
        string fatal;
        bool crashAtEnd;
        MultiplayerSession session;
        GameBootstrap game;
        // Hôte : événements relevés au moment où ils se produisent (le scénario de l'invité avance à son rythme).
        bool robotSeenActive, splashSeen;
        string purchaseSeen;
        int balanceAtDelivery = -1, balanceAtPurchase = -1;

        /// <summary>Mousse imposée (mesure « beaucoup de mousse ») : quantité fixe, humidité au moins égale.</summary>
        struct FoamFill : ICellOperation
        {
            public float Foam;

            public void Apply(ref RugCell cell, float weight)
            {
                cell.Foam = Foam * weight;
                cell.Moisture = Mathf.Max(cell.Moisture, 0.5f * weight);
            }
        }

        static void CoverWithFoam(RugCleaningController surface)
        {
            if (surface == null || surface.Grid == null) return;
            var fill = new FoamFill { Foam = 1f };
            surface.Grid.ApplyRect(new RectInt(0, 0, surface.Grid.Width, surface.Grid.Height), 1f, ref fill);
        }

        /// <summary>Retire la mousse et l'eau ajoutées pour la mesure (sinon l'aire de lavage « sale » resalit le tapis posé).</summary>
        struct FoamAndWaterClear : ICellOperation
        {
            public void Apply(ref RugCell cell, float weight)
            {
                cell.Foam = 0f;
                cell.Moisture = 0f;
            }
        }

        static void ClearFoamAndWater(RugCleaningController surface)
        {
            if (surface == null || surface.Grid == null) return;
            var clear = new FoamAndWaterClear();
            surface.Grid.ApplyRect(new RectInt(0, 0, surface.Grid.Width, surface.Grid.Height), 1f, ref clear);
        }

        /// <summary>
        /// Mission de corrections (2.10) : coût réel de la mousse en ligne. Pendant <paramref name="seconds"/>, relève le pire
        /// temps d'image, les à-coups (image &gt; 2 × la médiane et &gt; 1/60 s) et le pire coût par image de l'écriture
        /// (hôte) et de la lecture (invité) des mises à jour de surfaces, dont les corrections lentes toutes les 2 s.
        /// </summary>
        IEnumerator MeasureHeavyFoam(string suffix, float seconds)
        {
            Diagnostics.PerfStats.Reset();
            Diagnostics.PerfStats.Enabled = true;
            var samples = new List<float>();
            double lastWrite = 0, lastRead = 0, lastEncode = 0;
            float maxWrite = 0f, maxRead = 0f, maxEncode = 0f;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                yield return null;
                samples.Add(Time.unscaledDeltaTime);
                double write = Diagnostics.PerfStats.Milliseconds(Diagnostics.PerfStats.Slot.NetWrite);
                double read = Diagnostics.PerfStats.Milliseconds(Diagnostics.PerfStats.Slot.NetRead);
                double encode = Diagnostics.PerfStats.Milliseconds(Diagnostics.PerfStats.Slot.RendererEncode)
                                + Diagnostics.PerfStats.Milliseconds(Diagnostics.PerfStats.Slot.RendererUpload);
                maxWrite = Mathf.Max(maxWrite, (float)(write - lastWrite));
                maxRead = Mathf.Max(maxRead, (float)(read - lastRead));
                maxEncode = Mathf.Max(maxEncode, (float)(encode - lastEncode));
                lastWrite = write;
                lastRead = read;
                lastEncode = encode;
            }
            Diagnostics.PerfStats.Enabled = false;
            var sorted = new List<float>(samples);
            sorted.Sort();
            float median = sorted.Count > 0 ? sorted[sorted.Count / 2] : 0f, worst = sorted.Count > 0 ? sorted[sorted.Count - 1] : 0f;
            int hitches = 0;
            foreach (float f in samples) if (f > median * 2f && f > 1f / 60f) hitches++;
            values["mousseCouverteImageMedianeMs" + suffix] = Invariant(median * 1000f, "0.00");
            values["mousseCouverteImagePireMs" + suffix] = Invariant(worst * 1000f, "0.00");
            values["mousseCouverteACoups" + suffix] = hitches.ToString();
            values["mousseCouverteEcritureReseauMaxMs" + suffix] = Invariant(maxWrite, "0.00");
            values["mousseCouverteLectureReseauMaxMs" + suffix] = Invariant(maxRead, "0.00");
            values["mousseCouverteRenduSurfacesMaxMs" + suffix] = Invariant(maxEncode, "0.00");
        }

        static readonly Vector2 HostArea = new Vector2(0.3f, 0.5f);
        static readonly Vector2 GuestArea = new Vector2(0.72f, 0.5f);
        static string Invariant(float value, string format) => value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);

        public void Configure(string playerRole, ushort listenPort, string report, string sandbox)
        {
            role = playerRole;
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
            if (game == null || role != "host") return;
            if (game.Robot != null && game.Robot.Mode != Workshop.RobotMode.Docked && balanceAtDelivery >= 0) robotSeenActive = true;
            if (game.Gadgets != null && game.Gadgets.Splash.IsActive) splashSeen = true;
            if (purchaseSeen == null && balanceAtDelivery >= 0)
            {
                var bought = game.Upgrades.Catalog[0];
                if (game.Upgrades.IsOwned(bought))
                {
                    purchaseSeen = bought.id;
                    balanceAtPurchase = game.Wallet.Balance;
                }
            }
        }

        /// <summary>Journal horodaté des connexions (diagnostic des reconnexions dans le journal du joueur).</summary>
        void TraceConnections()
        {
            var manager = session != null ? session.Manager : null;
            if (manager == null) return;
            manager.LogLevel = Unity.Netcode.LogLevel.Developer;
            manager.OnConnectionEvent += (_, data) =>
                Debug.Log($"[RugWash test] {Time.realtimeSinceStartup:0.0} s : {data.EventType} client {data.ClientId}");
        }

        IEnumerator Start()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 120;
            float started = Time.realtimeSinceStartup;
            yield return role == "host" ? RunHost() : role == "guest2" ? RunReconnectedGuest() : RunGuest();
            values["dureeSecondes"] = Invariant(Time.realtimeSinceStartup - started, "0.0");
            WriteReport();
            if (crashAtEnd)
            {
                // Crash simulé : le processus est tué sans fermer la connexion ni sauvegarder (comme un plantage réel).
                System.Diagnostics.Process.GetCurrentProcess().Kill();
                yield break;
            }
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

        /// <summary>Emplacement multijoueur de ce joueur (sa progression en ligne).</summary>
        SaveData ReadOwnSave() => new SaveFileStore(saveFolder, MultiplayerAutotest.Slot.FileName).Load().Data;
        /// <summary>Partie solo de ce joueur : jamais modifiée par le jeu en ligne.</summary>
        SaveData ReadSoloSave() => new SaveFileStore(saveFolder, SaveManager.DefaultFileName).Load().Data;

        /// <summary>Chat : une ligne de ce joueur, reçue (après nettoyage et rediffusion par l'hôte).</summary>
        static bool ChatContains(string sender, string text)
        {
            var workshop = NetworkWorkshop.Instance;
            if (workshop == null) return false;
            foreach (var line in workshop.Chat.Lines)
                if (!line.System && line.Sender == sender && line.Text == text) return true;
            return false;
        }

        IEnumerator JoinHost()
        {
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
            if (game == null) yield break;
            yield return WaitFor(() => NetworkWorkshop.Instance != null && NetworkWorkshop.Instance.GuestReady, 30f);
            yield return new WaitForSeconds(0.3f);
        }

        // ---------------------------------------------------------------- Hôte

        IEnumerator RunHost()
        {
            yield return OpenMenuSession("Hôte");
            if (session == null) yield break;
            Record("Partie hébergée (connexion directe)", session.HostDirect(port), session.Message);
            TraceConnections();
            yield return WaitForWorkshop();
            Record("Atelier réseau prêt chez l'hôte", game != null, session.State.ToString());
            if (game == null) { fatal = "atelier hôte"; yield break; }

            yield return WaitFor(() => AvatarCount() >= 2, 90f);
            Record("L'invité a rejoint l'atelier", AvatarCount() >= 2, $"{AvatarCount()} joueur(s)");
            if (AvatarCount() < 2) { fatal = "invité absent"; yield break; }

            ulong guestId = 0;
            foreach (ulong id in NetworkWorkshop.Instance.NetworkManager.ConnectedClientsIds)
                if (id != NetworkWorkshop.Instance.NetworkManager.LocalClientId) guestId = id;
            Record("Économie de l'hôte inchangée par l'arrivée de l'invité", game.Wallet.Balance == MultiplayerAutotest.HostStartMoney
                && !game.Upgrades.OwnedIds.Contains(MultiplayerAutotest.GuestOnlyUpgrade), $"{game.Wallet.Balance} €, {game.Upgrades.OwnedIds.Count} achat(s)");
            yield return WaitFor(() => NetworkWorkshop.Instance.CareerOf(guestId) != null, 30f);
            var arrival = NetworkWorkshop.Instance.CareerOf(guestId);
            Record("Arrivée de l'invité : sa fiche retrouvée dans la sauvegarde de la session (ses outils), argent et réputation communs",
                arrival != null && arrival.Money == MultiplayerAutotest.HostStartMoney && arrival.Reputation == game.Reputation.Points
                && arrival.Upgrades.Contains("up_tool_squeegee") && arrival.Upgrades.Contains("up_base_2")
                && !arrival.Upgrades.Contains("up_prank_bat") && !arrival.Upgrades.Contains(MultiplayerAutotest.GuestOnlyUpgrade),
                arrival != null ? $"{arrival.Money} €, {arrival.Upgrades.Count} achat(s)" : "vue absente");

            // Chat : message de l'invité reçu (nom de la session, pas celui qu'il annoncerait), puis réponse de l'hôte.
            yield return WaitFor(() => ChatContains("Invité", MultiplayerAutotest.ChatFromGuest), 40f);
            Record("Chat : message de l'invité reçu par l'hôte", ChatContains("Invité", MultiplayerAutotest.ChatFromGuest));
            NetworkWorkshop.Instance.SendChat(MultiplayerAutotest.ChatFromHost);

            var order = game.Orders.Book.Orders[0];
            // Gains suivis à chaque variation du solde : un double paiement serait vu même si un achat suit.
            int positiveChanges = 0, gained = 0;
            game.Wallet.Wallet.Changed += (balance, delta) =>
            {
                if (delta <= 0) return;
                positiveChanges++;
                gained += delta;
            };
            Record("Commande acceptée par l'hôte", game.Orders.TryStart(order) == OrderStartResult.Started, order.title);
            float guestAreaBefore = DustAround(game.Rug.Grid, GuestArea, 8);
            yield return new WaitForSeconds(2f);

            measuring = true;
            yield return UseTool(1, HostArea, 5f);
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

            yield return UseTool(6, new Vector2(0.5f, 0.5f), 3f);
            yield return new WaitForSeconds(0.5f);
            long foamStart = NetworkWorkshop.Instance.SurfaceBytesSent;
            float foamWindow = 4f;
            yield return new WaitForSeconds(foamWindow);
            float foamRate = (NetworkWorkshop.Instance.SurfaceBytesSent - foamStart) / foamWindow;
            values["octetsMousseAuReposParSeconde"] = Invariant(foamRate, "0");
            Record("Mousse au repos : débit réseau faible (simulation reproduite chez l'invité)", foamRate < 40f * 1024f, $"{foamRate / 1024f:0.0} Ko/s");

            // Beaucoup de mousse (tapis, aire de lavage et sol couverts) : coût des corrections lentes chez l'hôte.
            CoverWithFoam(game.Rug);
            CoverWithFoam(game.WorkSurface != null ? game.WorkSurface.Surface : null);
            CoverWithFoam(game.RoomFloor);
            yield return new WaitForSeconds(1f);
            long heavyStart = NetworkWorkshop.Instance.SurfaceBytesSent;
            yield return MeasureHeavyFoam("Hote", 6f);
            values["mousseCouverteOctetsParSeconde"] = Invariant((NetworkWorkshop.Instance.SurfaceBytesSent - heavyStart) / 6f, "0");
            // La fenêtre de l'invité commence un peu plus tard : le retrait de la mousse (grilles entières, raccourci de
            // test) ne doit pas tomber dedans.
            yield return new WaitForSeconds(2f);
            ClearFoamAndWater(game.WorkSurface != null ? game.WorkSurface.Surface : null);
            ClearFoamAndWater(game.RoomFloor);
            game.WorkSurface?.RefreshStatus(force: true);

            // Rendu présentable (raccourci de test) : l'invité demandera la livraison.
            CleanThoroughly(game.Rug.Grid);
            OrderResult delivered = null;
            game.Orders.OrderFinished += result =>
            {
                delivered = result;
                balanceAtDelivery = game.Wallet.Balance;
            };
            yield return WaitFor(() => delivered != null, 60f);
            // L'invité enchaîne aussitôt sur un achat : on retient le plus haut solde vu dans sa carrière (un achat le fait baisser).
            int guestBestMoney = int.MinValue;
            for (float waited = 0f; waited < 4f; waited += Time.unscaledDeltaTime)
            {
                var career = NetworkWorkshop.Instance.CareerOf(guestId);
                if (career != null) guestBestMoney = Mathf.Max(guestBestMoney, career.Money);
                yield return null;
            }
            Record("Livraison demandée par l'invité, validée par l'hôte", delivered != null && delivered.Accepted,
                delivered == null ? "aucune" : $"note {OrderEvaluator.GradeLabel(delivered.Grade)}");
            Record("Récompense versée une seule fois dans la carrière de l'hôte (deux demandes de livraison)",
                delivered != null && positiveChanges == 1 && gained == delivered.Reward && balanceAtDelivery == MultiplayerAutotest.HostStartMoney + delivered.Reward,
                delivered == null ? "-" : $"{positiveChanges} versement(s), {gained} € pour {delivered.Reward} € ; solde à la livraison {balanceAtDelivery} €");
            Record("Argent de la session vu par l'invité : le même que celui de l'hôte (récompense comprise)", delivered != null
                && guestBestMoney == balanceAtDelivery, guestBestMoney != int.MinValue ? $"{guestBestMoney} € pour {balanceAtDelivery} €" : "-");
            values["argentApresLivraison"] = balanceAtDelivery.ToString();
            values["missionsTableau"] = GeneratedOffers(game);
            Record("Trois missions générées proposées sur le tableau de l'hôte", game.Orders.Missions.Offers.Count == 3, values["missionsTableau"]);

            // Débris secs sur l'aire de lavage : le robot (que l'invité va démarrer) a réellement de quoi travailler.
            var pad = game.WorkSurface.Surface.Grid;
            var debris = new DepositOperation { Type = DirtType.Dust, Debris = 0.3f };
            pad.ApplyRect(new RectInt(0, 0, pad.Width, 6), 1f, ref debris);
            pad.ApplyRect(new RectInt(0, pad.Height - 6, pad.Width, 6), 1f, ref debris);

            // Achat demandé par l'invité : payé avec l'argent de la session, rangé dans SA fiche (pas dans les achats de l'hôte).
            var bought = game.Upgrades.Find(MultiplayerAutotest.GuestPurchase);
            bool hostOwnedBefore = game.Upgrades.IsOwned(bought);
            yield return WaitFor(() => { var m = NetworkWorkshop.Instance.MemberOf(guestId); return m != null && m.upgrades.Contains(bought.id); }, 40f);
            var afterBuy = NetworkWorkshop.Instance.CareerOf(guestId);
            var buyer = NetworkWorkshop.Instance.MemberOf(guestId);
            Record("Achat de l'invité payé avec l'argent de la session, rangé dans SA fiche (validé par l'hôte)", buyer != null && buyer.upgrades.Contains(bought.id)
                && game.Wallet.Balance == balanceAtDelivery - bought.price && game.Upgrades.IsOwned(bought) == hostOwnedBefore,
                $"{bought.displayName} : session {balanceAtDelivery} € → {game.Wallet.Balance} €");
            values["argentInviteTenuParHote"] = afterBuy != null ? afterBuy.Money.ToString() : "-";
            int sessionAfterBuy = game.Wallet.Balance;
            yield return new WaitForSeconds(4f);
            var refusedMember = NetworkWorkshop.Instance.MemberOf(guestId);
            Record("Amélioration de l'atelier demandée par l'invité : refusée par l'hôte (réservée à l'hôte), rien n'est débité",
                game.Wallet.Balance == sessionAfterBuy && !game.Upgrades.IsOwned(game.Upgrades.Find(MultiplayerAutotest.WorkshopUpgrade))
                && refusedMember != null && !refusedMember.upgrades.Contains(MultiplayerAutotest.WorkshopUpgrade), $"session {game.Wallet.Balance} €");

            yield return WaitFor(() => robotSeenActive, 40f);
            Record("Robot démarré à la demande de l'invité", robotSeenActive, game.Robot.Mode.ToString());

            NetworkPlayerAvatar guestAvatar = null;
            foreach (var avatar in FindObjectsByType<NetworkPlayerAvatar>())
                if (avatar.OwnerClientId == guestId) guestAvatar = avatar;
            Record("Apparence de l'invité (menu Personnage) vue par l'hôte", guestAvatar != null && guestAvatar.PackedLook == MultiplayerAutotest.GuestLook.Pack(),
                guestAvatar != null ? guestAvatar.PackedLook.ToString() : "-");

            // Farces : l'invité utilise le pipi de l'atelier de l'hôte ; l'hôte répond d'un coup de batte.
            yield return WaitFor(() => splashSeen, 60f);
            Record("Farce de l'invité validée par l'hôte : gouttes sur l'écran de l'hôte", splashSeen);
            PrankResult bat = PrankResult.Unavailable;
            if (guestAvatar != null)
            {
                game.Gadgets.ClearReceived();
                var near = guestAvatar.transform.position - guestAvatar.transform.forward * 1.2f;
                game.PlayerController.Teleport(new Vector3(near.x, 0.06f, near.z), guestAvatar.transform.eulerAngles.y, 10f);
                yield return new WaitForSeconds(0.5f);
                bat = game.Pranks.Execute(game.Gadgets.Actor, new PrankRequest { Gadget = PrankGadget.Bat, Target = PrankTarget.Player, Victim = guestId }, out _);
                yield return WaitFor(() => guestAvatar.IsKnockedDown, 10f);
            }
            Record("Coup de batte de l'hôte : chute de l'invité vue par l'hôte (ragdoll)", bat == PrankResult.Done && guestAvatar != null && guestAvatar.IsKnockedDown,
                bat.ToString());
            // Le coup validé fait tomber le dernier objet acheté que porte l'invité ; il quitte sa fiche et apparaît au sol.
            yield return WaitFor(() => NetworkWorkshop.Instance.Drops.Count > 0, 10f);
            var dropsAfterHit = NetworkWorkshop.Instance.Drops;
            var victimAfterHit = NetworkWorkshop.Instance.MemberOf(guestId);
            Record("Coup de batte : un objet acheté de l'invité tombe au sol et quitte SA fiche (un seul objet, une seule fois)",
                dropsAfterHit.Count == 1 && dropsAfterHit[0].Upgrade.ToString() == MultiplayerAutotest.DroppedUpgrade && victimAfterHit != null
                && !victimAfterHit.upgrades.Contains(MultiplayerAutotest.DroppedUpgrade) && !victimAfterHit.carried.Contains("squeegee"),
                $"{dropsAfterHit.Count} objet(s) au sol");
            if (dropsAfterHit.Count > 0)
            {
                // Un coéquipier (l'hôte) le ramasse : il devient le sien ; une seconde demande ne trouve plus rien.
                var drop = dropsAfterHit[0];
                game.PlayerController.Teleport(new Vector3(drop.Position.x, 0.06f, drop.Position.z - 0.8f), 0f, 20f);
                yield return new WaitForSeconds(0.3f);
                string first = NetworkWorkshop.Instance.TryPickup(drop.Id);
                string second = NetworkWorkshop.Instance.TryPickup(drop.Id);
                var squeegee = game.Upgrades.Find(MultiplayerAutotest.DroppedUpgrade);
                Record("Objet au sol ramassé par un coéquipier (une seule fois) : il devient le sien", game.Upgrades.IsOwned(squeegee)
                    && NetworkWorkshop.Instance.Drops.Count == 0 && second != first, $"« {first} » puis « {second} »");
                // Puis il le revend (rangé au casier d'abord) : moitié du prix de base, une seule fois, dans l'argent de la session.
                game.ToolController.Inventory.Store("squeegee");
                int beforeSale = game.Wallet.Balance;
                var sold = game.Shop.TrySell(squeegee);
                var again = game.Shop.TrySell(squeegee);
                Record("Objet ramassé puis revendu : 50 % du prix de base, versé une fois (seconde revente refusée)",
                    sold == Economy.SaleResult.Sold && again == Economy.SaleResult.NotOwned && game.Wallet.Balance == beforeSale + squeegee.price / 2 && !game.Upgrades.IsOwned(squeegee),
                    $"{sold} puis {again} : {beforeSale} € → {game.Wallet.Balance} €");
            }
            yield return WaitFor(() => guestAvatar == null || !guestAvatar.IsKnockedDown, 10f);
            Record("L'invité se relève (état « à terre » terminé)", guestAvatar != null && !guestAvatar.IsKnockedDown);
            // L'invité revend un objet rangé au casier : il quitte SA fiche (validé ici).
            yield return WaitFor(() => { var m = NetworkWorkshop.Instance.MemberOf(guestId); return m != null && !m.upgrades.Contains(MultiplayerAutotest.GuestSale); }, 40f);
            var seller = NetworkWorkshop.Instance.MemberOf(guestId);
            Record("Revente par l'invité validée par l'hôte : l'objet quitte SA fiche", seller != null && !seller.upgrades.Contains(MultiplayerAutotest.GuestSale),
                seller != null ? $"{seller.upgrades.Count} achat(s) personnel(s)" : "-");
            values["empreinteTapis"] = MultiplayerAutotest.Hash(game.Rug.Grid).ToString();
            values["empreinteTapisDetail"] = MultiplayerAutotest.HashDetail(game.Rug.Grid);

            // Crash de l'invité (processus tué) puis reconnexion du même joueur : jamais deux personnages pour lui.
            int moneyBeforeCrash = game.Wallet.Balance;
            int ownedBeforeCrash = game.Upgrades.OwnedIds.Count;
            float crashWait = Time.realtimeSinceStartup;
            yield return WaitFor(() => guestAvatar == null, 60f);
            values["secondesAvantRetraitDuPersonnageDeLInvite"] = Invariant(Time.realtimeSinceStartup - crashWait, "0.0");
            yield return WaitFor(() => AvatarCount() >= 2, 120f);
            yield return new WaitForSeconds(3f);
            Record("Crash puis reconnexion de l'invité : un seul personnage par joueur (aucun fantôme)", AvatarCount() == 2 && guestAvatar == null,
                $"{AvatarCount()} joueur(s), anciennes connexions fermées par reconnexion : {session.StaleConnectionsEvicted}");
            Record("Reconnexion : ni argent ni achat dupliqué chez l'hôte", game.Wallet.Balance == moneyBeforeCrash
                && game.Upgrades.OwnedIds.Count == ownedBeforeCrash, $"{game.Wallet.Balance} €, {game.Upgrades.OwnedIds.Count} achat(s)");
            ulong returnedId = 0;
            foreach (ulong id in NetworkWorkshop.Instance.NetworkManager.ConnectedClientsIds)
                if (id != NetworkWorkshop.Instance.NetworkManager.LocalClientId) returnedId = id;
            yield return WaitFor(() => NetworkWorkshop.Instance.CareerOf(returnedId) != null, 20f);
            var returned = NetworkWorkshop.Instance.CareerOf(returnedId);
            Record("Reconnexion : la fiche de l'invité retrouvée dans la sauvegarde de la session (achat présent, objets perdus et revendus absents)",
                returned != null && returned.Upgrades.Contains(bought.id) && !returned.Upgrades.Contains(MultiplayerAutotest.DroppedUpgrade)
                && !returned.Upgrades.Contains(MultiplayerAutotest.GuestSale) && returned.Money == game.Wallet.Balance,
                returned != null ? $"{returned.Money} €" : "-");
            values["argentInviteApresReconnexion"] = returned != null ? returned.Money.ToString() : "-";
            Record("Partie de l'hôte toujours en cours après le crash de l'invité", session.InSession && session.State == SessionState.InGame,
                session.State.ToString());

            values["octetsSurfacesEnvoyes"] = NetworkWorkshop.Instance.SurfaceBytesSent.ToString();
            values["fpsMoyenNettoyage"] = Invariant(AverageFps(), "0");
            game.SaveSystem.SaveNow("fin du test multijoueur");
            var data = ReadOwnSave();
            var savedGuest = data != null ? data.members.Find(m => m.key == MultiplayerAutotest.GuestKey) : null;
            Record("Sauvegarde de la session (emplacement de l'hôte) : argent commun, fiche de l'invité à jour, plus rien au sol", data != null
                && data.money == game.Wallet.Balance && data.upgrades.Contains(bought.id) == hostOwnedBefore && data.completedOrders.Contains(order.id)
                && data.mode == MultiplayerAutotest.Slot.ModeKey && savedGuest != null && savedGuest.upgrades.Contains(bought.id)
                && !savedGuest.upgrades.Contains(MultiplayerAutotest.GuestSale) && data.workshop.drops.Count == 0,
                data != null ? $"{data.money} €, achats {data.upgrades.Count}, fiches {data.members.Count}, au sol {data.workshop.drops.Count}" : "illisible");
            values["argentFinalHote"] = game.Wallet.Balance.ToString();
            values["fichierSauvegardeHote"] = Path.Combine(saveFolder, MultiplayerAutotest.Slot.FileName);
            // Laisse l'invité revenu vérifier ce qu'il voit, puis fermeture brutale de l'hôte (crash simulé).
            yield return new WaitForSeconds(6f);
            crashAtEnd = true;
        }

        // ---------------------------------------------------------------- Invité

        IEnumerator RunGuest()
        {
            yield return OpenMenuSession("Invité");
            if (session == null) yield break;
            yield return JoinHost();
            TraceConnections();
            Record("Connecté et synchronisé avec l'hôte", game != null && NetworkWorkshop.Instance.GuestReady, session.State.ToString());
            if (game == null) { fatal = "atelier invité"; yield break; }
            Record("Le tapis est une réplique (l'hôte fait autorité)", game.Rug.IsReplica);
            Record("État de chargement affiché à l'arrivée (contrôles coupés)", NetworkWorkshop.Instance.ShowedLoading);
            string expectedMoney = Economy.WalletManager.Format(MultiplayerAutotest.HostStartMoney);
            Record("Partie de l'hôte : argent et réputation de la session (sauvegarde choisie par l'hôte)",
                !game.GameState.Synchronizing && !game.Hud.IsLoading && game.Wallet.Balance == MultiplayerAutotest.HostStartMoney
                && game.Reputation.Points == 0 && game.Hud.EconomyText.Contains(expectedMoney) && game.SaveSystem.SessionGuest,
                $"HUD « {game.Hud.EconomyText.Replace('\n', ' ')} » {GameplayState()}");
            Record("Ses outils retrouvés dans la sauvegarde de l'hôte (fiche), sans rien de ses propres sauvegardes",
                game.Upgrades.OwnedIds.Contains("up_tool_squeegee") && game.Upgrades.OwnedIds.Contains("up_base_2")
                && !game.Upgrades.OwnedIds.Contains("up_prank_bat") && !game.Upgrades.OwnedIds.Contains(MultiplayerAutotest.GuestOnlyUpgrade)
                && Carries(game, "squeegee"), $"{game.Upgrades.OwnedIds.Count} achat(s)");
            bool ownMulti = MultiplayerAutotest.GuestMultiUntouched(ReadOwnSave(), out string ownDetail);
            Record("Emplacement multijoueur personnel de l'invité ni chargé ni modifié", ownMulti, ownDetail);
            NetworkWorkshop.Instance.SendChat(MultiplayerAutotest.ChatFromGuest);
            yield return WaitFor(() => ChatContains("Hôte", MultiplayerAutotest.ChatFromHost), 60f);
            Record("Chat : réponse de l'hôte reçue par l'invité", ChatContains("Hôte", MultiplayerAutotest.ChatFromHost));

            yield return WaitFor(() => AvatarCount() >= 2, 20f);
            NetworkPlayerAvatar hostAvatar = null;
            foreach (var avatar in FindObjectsByType<NetworkPlayerAvatar>())
                if (!avatar.IsOwner) hostAvatar = avatar;
            Record("Silhouette de l'hôte visible", hostAvatar != null, hostAvatar != null ? hostAvatar.DisplayName : "-");

            yield return WaitFor(() => game.Orders.ActiveOrder != null, 40f);
            Record("Commande de l'hôte reçue (état partagé)", game.Orders.ActiveOrder != null, game.Orders.ActiveOrder != null ? game.Orders.ActiveOrder.title : "-");
            yield return new WaitForSeconds(1f);

            // Mesure « beaucoup de mousse » côté invité, en parallèle du scénario (dès que l'hôte a tout couvert).
            StartCoroutine(GuestHeavyFoam());

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

            var target = game.Orders.ActiveOrder.objectives.targetCleanliness;
            yield return WaitFor(() => game.Rug.Cleanliness01 >= target, 60f);
            Record("Nettoyage de l'hôte reçu (tapis présentable)", game.Rug.Cleanliness01 >= target, $"{game.Rug.Cleanliness01:P1}");
            game.Orders.Deliver();
            game.Orders.Deliver();
            yield return WaitFor(() => game.Results.IsVisible, 30f);
            Record("Résultats de la livraison affichés chez l'invité", game.Results.IsVisible && game.Orders.LastResult != null,
                game.Orders.LastResult != null ? OrderEvaluator.GradeLabel(game.Orders.LastResult.Grade) : "-");
            int reward = game.Orders.LastResult != null ? game.Orders.LastResult.Reward : 0;
            yield return WaitFor(() => game.Wallet.Balance == MultiplayerAutotest.HostStartMoney + reward, 20f);
            Record("Récompense dans l'argent de la session (une fois)", reward > 0 && game.Wallet.Balance == MultiplayerAutotest.HostStartMoney + reward,
                $"{game.Wallet.Balance} € (départ {MultiplayerAutotest.HostStartMoney} + {reward})");
            game.Results.BackToWorkshop();
            yield return null;

            var upgrade = game.Upgrades.Find(MultiplayerAutotest.GuestPurchase);
            int moneyBeforePurchase = game.Wallet.Balance;
            var request = game.Shop.TryPurchase(upgrade);
            yield return WaitFor(() => game.Upgrades.IsOwned(upgrade) && game.Wallet.Balance == moneyBeforePurchase - upgrade.price, 30f);
            Record("Achat validé par l'hôte, payé avec l'argent de la session", request == Economy.PurchaseResult.Requested
                && game.Upgrades.IsOwned(upgrade) && game.Wallet.Balance == moneyBeforePurchase - upgrade.price,
                $"{upgrade.displayName} : {moneyBeforePurchase} € → {game.Wallet.Balance} €");
            values["argentApresAchatVuParInvite"] = game.Wallet.Balance.ToString();

            // Amélioration de l'atelier : refusée sans demande par la boutique de l'invité, et refusée par l'hôte si elle arrive.
            var workshopItem = game.Upgrades.Find(MultiplayerAutotest.WorkshopUpgrade);
            var local = game.Shop.TryPurchase(workshopItem);
            string refusal = null;
            game.Shop.RemoteResultReceived += (message, success) => refusal = success ? null : message;
            NetworkWorkshop.Instance.RequestUpgrade(MultiplayerAutotest.WorkshopUpgrade);
            yield return WaitFor(() => refusal != null, 15f);
            Record("Amélioration de l'atelier : réservée à l'hôte (bouton de l'invité refusé, demande refusée par l'hôte)",
                local == Economy.PurchaseResult.HostOnly && refusal != null && !game.Upgrades.IsOwned(workshopItem), $"{local} ; « {refusal ?? "aucune réponse"} »");

            game.Robot.Toggle();
            yield return WaitFor(() => game.Robot.Mode != Workshop.RobotMode.Docked, 30f);
            Record("Robot démarré via l'hôte, état reçu", game.Robot.Mode != Workshop.RobotMode.Docked, game.Robot.Mode.ToString());

            var blower = game.FindMachine("blower");
            Record("Décor et machines de l'atelier de l'hôte affichés chez l'invité", game.Decor.Tier == 2 && blower != null && blower.IsOwned,
                $"palier {game.Decor.Tier}, souffleur {(blower != null && blower.IsOwned ? "présent" : "absent")}");

            // Farce : pipi sur l'hôte avec le gadget de l'atelier (portée mesurée chez l'hôte depuis la silhouette de l'invité).
            if (hostAvatar != null)
            {
                var front = hostAvatar.transform.position + hostAvatar.transform.forward * 1.2f;
                game.PlayerController.Teleport(new Vector3(front.x, 0.06f, front.z), hostAvatar.transform.eulerAngles.y + 180f, 10f);
                yield return new WaitForSeconds(1.5f);
                int answers = game.Gadgets.ResultsReceived;
                game.Gadgets.Equip(PrankGadget.Pee);
                NetworkWorkshop.Instance.RequestPrank(new PrankRequest
                {
                    Gadget = PrankGadget.Pee, Target = PrankTarget.Player, Victim = hostAvatar.OwnerClientId,
                    Point = hostAvatar.transform.position + Vector3.up * PrankRules.BodyHeight, Direction = hostAvatar.transform.position - front,
                });
                yield return WaitFor(() => game.Gadgets.ResultsReceived > answers, 15f);
                Record("Farce de l'invité (pipi de l'atelier) validée par l'hôte", game.Gadgets.ResultsReceived > answers && game.Gadgets.LastResult == PrankResult.Done,
                    game.Gadgets.LastResult.ToString());
                game.Gadgets.Unequip();
            }
            else Record("Farce de l'invité (pipi de l'atelier) validée par l'hôte", false, "silhouette de l'hôte absente");
            yield return WaitFor(() => game.Knockdown.IsDown, 40f);
            Record("Coup de batte de l'hôte reçu : chute de l'invité", game.Knockdown.IsDown);
            bool seenOnFloor = false;
            yield return WaitFor(() =>
            {
                seenOnFloor |= NetworkWorkshop.Instance.Drops.Count > 0;
                return !game.Upgrades.OwnedIds.Contains(MultiplayerAutotest.DroppedUpgrade) && seenOnFloor;
            }, 15f);
            Record("Coup de batte : l'invité perd un objet acheté (il quitte son inventaire et apparaît au sol)",
                !game.Upgrades.OwnedIds.Contains(MultiplayerAutotest.DroppedUpgrade) && !Carries(game, "squeegee") && seenOnFloor,
                $"vu au sol : {seenOnFloor}");
            yield return WaitFor(() => !game.Knockdown.IsDown, 10f);
            Record("Relevé après la chute (contrôles rendus)", !game.Knockdown.IsDown && !game.PlayerController.ControlLocked);

            // Revente d'un objet rangé au casier : demande à l'hôte, moitié du prix de base dans l'argent de la session.
            var broom = game.Upgrades.Find(MultiplayerAutotest.GuestSale);
            yield return new WaitForSeconds(2f);
            int moneyBeforeSale = game.Wallet.Balance;
            var sale = game.Shop.TrySell(broom);
            yield return WaitFor(() => !game.Upgrades.IsOwned(broom), 20f);
            yield return new WaitForSeconds(1f);
            Record("Revente demandée à l'hôte : l'objet quitte l'inventaire de l'invité, la moitié du prix revient à la session",
                sale == Economy.SaleResult.Requested && !game.Upgrades.IsOwned(broom) && game.Wallet.Balance >= moneyBeforeSale + broom.price / 2,
                $"{sale} : {moneyBeforeSale} € → {game.Wallet.Balance} €");

            yield return new WaitForSeconds(4f);
            values["empreinteTapis"] = MultiplayerAutotest.Hash(game.Rug.Grid).ToString();
            values["empreinteTapisDetail"] = MultiplayerAutotest.HashDetail(game.Rug.Grid);
            values["missionsTableau"] = GeneratedOffers(game);
            values["predictionsEnAttente"] = NetworkWorkshop.Instance.ClientSync.Predictor.PendingCount.ToString();
            values["octetsOperationsEnvoyes"] = NetworkWorkshop.Instance.OperationBytesSent.ToString();
            values["fpsMoyenNettoyage"] = Invariant(AverageFps(), "0");

            // V0.2.5 : rien de la partie n'est écrit chez l'invité ; ses deux sauvegardes restent celles d'avant.
            bool multiKept = MultiplayerAutotest.GuestMultiUntouched(ReadOwnSave(), out string multiDetail);
            Record("Emplacement multijoueur personnel de l'invité toujours intact (la partie est chez l'hôte)", multiKept, multiDetail);
            bool untouched = MultiplayerAutotest.GuestSaveUntouched(ReadSoloSave(), out string detail);
            Record("Partie solo de l'invité inchangée pendant la session en ligne", untouched, detail);
            // Crash simulé : le processus est tué en pleine partie (guest2 = le même joueur qui revient).
            crashAtEnd = true;
        }

        // ---------------------------------------------------------------- Le même invité, revenu après son crash

        IEnumerator GuestHeavyFoam()
        {
            yield return WaitFor(() => game.Rug.Grid != null && game.Rug.Grid.AverageFoam > 0.8f, 120f);
            if (game.Rug.Grid == null || game.Rug.Grid.AverageFoam <= 0.8f) yield break;
            yield return new WaitForSeconds(1f);
            yield return MeasureHeavyFoam("Invite", 6f);
        }

        IEnumerator RunReconnectedGuest()
        {
            bool multiIntact = MultiplayerAutotest.GuestMultiUntouched(ReadOwnSave(), out string multiDetail);
            Record("Après le crash : emplacement multijoueur personnel intact", multiIntact, multiDetail);
            bool intact = MultiplayerAutotest.GuestSaveUntouched(ReadSoloSave(), out string detail);
            Record("Après le crash : partie solo intacte", intact, detail);
            yield return OpenMenuSession("Invité");
            if (session == null) yield break;
            Debug.Log($"[RugWash test] {Time.realtimeSinceStartup:0.0} s : reconnexion vers 127.0.0.1:{port}");
            yield return JoinHost();
            Debug.Log($"[RugWash test] {Time.realtimeSinceStartup:0.0} s : état {session.State}, message « {session.Message} »");
            Record("Reconnexion à la partie de l'hôte", game != null && NetworkWorkshop.Instance != null && NetworkWorkshop.Instance.GuestReady, session.State.ToString());
            if (game == null) { fatal = "reconnexion"; yield break; }
            yield return new WaitForSeconds(3f);
            Record("Reconnexion : un seul personnage par joueur (aucun fantôme vu)", AvatarCount() == 2, $"{AvatarCount()} joueur(s)");
            Record("Reconnexion : sa fiche retrouvée chez l'hôte (achat présent ; objets perdu et revendu absents)",
                game.Upgrades.IsOwned(game.Upgrades.Find(MultiplayerAutotest.GuestPurchase)) && !game.Upgrades.OwnedIds.Contains(MultiplayerAutotest.GuestOnlyUpgrade)
                && !game.Upgrades.OwnedIds.Contains(MultiplayerAutotest.DroppedUpgrade) && !game.Upgrades.OwnedIds.Contains(MultiplayerAutotest.GuestSale),
                $"{game.Wallet.Balance} €, {game.Upgrades.OwnedIds.Count} achat(s)");
            values["argentVuApresReconnexion"] = game.Wallet.Balance.ToString();

            // L'hôte se ferme brutalement : retour au menu avec un message, sauvegarde personnelle intacte.
            yield return WaitFor(() => !session.InSession && SceneManager.GetActiveScene().name == MultiplayerSession.MainMenuScene, 60f);
            Record("Hôte fermé brutalement : retour au menu avec un message", !session.InSession
                && SceneManager.GetActiveScene().name == MultiplayerSession.MainMenuScene && !string.IsNullOrEmpty(session.Message), session.Message);
            multiIntact = MultiplayerAutotest.GuestMultiUntouched(ReadOwnSave(), out multiDetail);
            Record("Après la fin de la session : emplacement multijoueur personnel de l'invité toujours intact", multiIntact, multiDetail);
            intact = MultiplayerAutotest.GuestSaveUntouched(ReadSoloSave(), out detail);
            Record("Après la fin de la session : partie solo de l'invité intacte", intact, detail);
        }

        /// <summary>Objet porté par ce joueur (inventaire à 4 emplacements).</summary>
        static bool Carries(GameBootstrap game, string id)
        {
            foreach (string carried in game.ToolController.Inventory.Carried)
                if (carried == id) return true;
            return false;
        }

        /// <summary>Missions générées du tableau (identifiants triés) : identiques chez l'hôte et chez l'invité.</summary>
        static string GeneratedOffers(GameBootstrap game)
        {
            var ids = new List<string>();
            foreach (var order in game.Orders.Book.Orders)
                if (MissionKey.IsGenerated(order.id)) ids.Add(order.id);
            ids.Sort(StringComparer.Ordinal);
            return string.Join(",", ids);
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
            json.Append($"  \"role\": \"{role}\",\n");
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
