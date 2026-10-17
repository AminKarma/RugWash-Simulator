using System;
using RugWash.Audio;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Orders;
using RugWash.Player;
using RugWash.UI;
using UnityEngine;

namespace RugWash.Workshop
{
    public enum WasherState : byte
    {
        /// <summary>Machine vide : un tapis de commande peut y être chargé.</summary>
        Free = 0,
        /// <summary>Cycle en cours (temps restant décompté par l'autorité).</summary>
        Washing = 1,
        /// <summary>Cycle terminé : le tapis lavé attend d'être reposé dans une zone de lavage.</summary>
        Done = 2,
    }

    public enum WasherAction : byte
    {
        Load = 1,
        Collect = 2,
        Abandon = 3,
    }

    /// <summary>État de la machine à laver dans la sauvegarde de l'atelier (grande mise à jour).</summary>
    [Serializable]
    public sealed class WasherSaveData
    {
        public int state;
        public string orderId = string.Empty;
        /// <summary>Temps de cycle restant (secondes de jeu) : il ne dépend jamais de l'horloge du système.</summary>
        public float remaining;
        public float cycleSeconds;
        /// <summary>Chronomètre de la commande au moment du chargement (suspendu pendant le lavage).</summary>
        public float orderElapsed;
        /// <summary>Grille du tapis à l'entrée en machine (<see cref="SurfaceStateCodec"/>) et sa saleté initiale.</summary>
        public string grid = string.Empty;
        public float initialDirt;
    }

    /// <summary>Règles pures de la machine à laver (testées en EditMode).</summary>
    public static class WasherRules
    {
        /// <summary>
        /// Plus grand pas compté d'un coup (s) : le cycle avance avec le temps de jeu réellement écoulé, image par image ; un
        /// gel de l'application, un changement d'heure du système ou une absence (jeu fermé) ne le font jamais avancer.
        /// </summary>
        public const float MaxStepSeconds = 0.5f;

        public static float Advance(float remaining, float deltaTime) =>
            Mathf.Max(0f, remaining - Mathf.Clamp(deltaTime, 0f, MaxStepSeconds));

        /// <summary>Temps restant lu dans une sauvegarde : borné au cycle (fichier modifié ou corrompu).</summary>
        public static float Sanitize(float remaining, float cycle) =>
            float.IsNaN(remaining) || float.IsInfinity(remaining) ? cycle : Mathf.Clamp(remaining, 0f, cycle);

        public static float Progress01(float remaining, float cycle) => cycle <= 0f ? 1f : Mathf.Clamp01(1f - remaining / cycle);

        public static string Format(float seconds)
        {
            int total = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }
    }

    /// <summary>
    /// Machine à laver de l'atelier (grande mise à jour) : achetée à la boutique (agrandissement requis), installée dans
    /// l'annexe. On y charge le tapis d'une commande en cours : la zone de lavage est aussitôt libérée pour une autre
    /// commande. Après le cycle (15 min par défaut, moins avec le programme rapide), le tapis lavé est reposé dans une
    /// zone libre : poussière, saleté détachée et la plupart des taches sont parties, mais il sort trempé et les taches
    /// tenaces restent à traiter. Un tapis fragile s'use en machine.
    /// <para>Autorité : solo ou hôte (décompte, chargement, sortie) ; en ligne, les invités envoient leurs demandes et
    /// reçoivent l'état. Le temps restant est sauvegardé avec l'atelier et n'avance que pendant le jeu.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WashingMachine : MonoBehaviour, IInteractable
    {
        public const string MachineId = "washer";
        public const string FastProgramId = "washer_fast";

        [SerializeField] OrderManager orders;
        [SerializeField] WashingMachineUI ui;
        [Tooltip("Parties visibles et colliders (cachées tant que la machine n'est pas achetée).")]
        [SerializeField] GameObject body;
        [SerializeField] Transform drum;
        [SerializeField] Transform door;
        [Tooltip("Ouverture du hublot hors lavage (degrés autour de la charnière).")]
        [SerializeField] float doorOpenAngle = 25f;
        [SerializeField] TextMesh display;
        [SerializeField] Renderer statusLamp;
        [SerializeField] AudioSource loopSource;
        [SerializeField] AudioManager audioManager;
        [SerializeField] AudioClip doneClip;

        [Header("Cycle")]
        [Tooltip("Durée d'un cycle (s) : 15 minutes.")]
        [SerializeField, Min(10f)] float cycleSeconds = 900f;
        [Tooltip("Durée avec le programme rapide (s).")]
        [SerializeField, Min(10f)] float fastCycleSeconds = 540f;

        [Header("Lavage")]
        [SerializeField, Range(0f, 1f)] float dustRemoved = 0.95f;
        [Tooltip("Part des taches fixées retirée à réponse nominale à l'eau (modulée par le type de tache et son adhérence).")]
        [SerializeField, Range(0f, 1f)] float stainRemoved = 0.7f;
        [SerializeField, Range(0f, 1f)] float moistureAfter = 0.85f;
        [Tooltip("Usure ajoutée par cycle pour une fragilité de 1 (soie : environ 5 %).")]
        [SerializeField, Range(0f, 0.2f)] float wearPerFragility = 0.06f;

        bool owned, fastProgram;
        WasherState state;
        OrderDefinition order;
        string orderId = string.Empty;
        float remaining, cycle, orderElapsed, initialDirt;
        string grid = string.Empty;
        float spin;
        MaterialPropertyBlock lampBlock;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public WasherState State => state;
        public OrderDefinition Order => order;
        public string OrderId => orderId;
        public float Remaining => remaining;
        public float CycleSeconds => cycle > 0f ? cycle : NextCycleSeconds;
        public float NextCycleSeconds => fastProgram ? fastCycleSeconds : cycleSeconds;
        public float Progress01 => state == WasherState.Free ? 0f : WasherRules.Progress01(remaining, CycleSeconds);
        public bool IsOwned => owned;
        public bool HasFastProgram => fastProgram;
        /// <summary>Saleté retirée par le dernier lavage (diagnostic, tests).</summary>
        public float LastWashRemoved { get; private set; }
        public OrderManager Orders => orders;
        /// <summary>Multijoueur, côté invité : demandes envoyées à l'hôte. Null : solo ou hôte.</summary>
        public IWorkshopRequests Remote { get; set; }
        public bool IsReplica => Remote != null;

        public event Action<string> Message;
        /// <summary>Chargement, fin de cycle, sortie ou achat : l'hôte publie l'état aux invités ; l'interface se rafraîchit.</summary>
        public event Action StateChanged;

        public bool IsConfigured(out string missing)
        {
            missing = orders == null ? nameof(orders) : ui == null ? nameof(ui) : body == null ? nameof(body) : null;
            return missing == null;
        }

        void Awake()
        {
            lampBlock = new MaterialPropertyBlock();
            ApplyVisibility();
            RefreshVisuals();
        }

        /// <summary>Achat (agrandissement + machine) et programme rapide ; en ligne, atelier de l'hôte.</summary>
        public void SetOwned(bool value, bool fast)
        {
            bool changed = owned != value || fastProgram != fast;
            owned = value;
            fastProgram = fast;
            if (!changed) return;
            ApplyVisibility();
            RefreshVisuals();
            StateChanged?.Invoke();
        }

        void ApplyVisibility()
        {
            if (body == null) return;
            foreach (var renderer in body.GetComponentsInChildren<Renderer>(true)) renderer.enabled = owned;
            foreach (var collider in body.GetComponentsInChildren<Collider>(true)) collider.enabled = owned;
        }

        // ------------------------------------------------------------------ Interaction

        public string GetPrompt(PlayerInteractor interactor)
        {
            if (!owned) return string.Empty;
            switch (state)
            {
                case WasherState.Washing: return $"Machine à laver : lavage en cours ({WasherRules.Format(remaining)}) — détails";
                case WasherState.Done: return "Machine à laver : lavage terminé — récupérer le tapis";
                default: return "Machine à laver : charger un tapis de commande";
            }
        }

        public bool CanInteract(PlayerInteractor interactor) => owned && ui != null && !ui.IsOpen;

        public string Interact(PlayerInteractor interactor)
        {
            if (ui != null) ui.Open(this);
            return null;
        }

        /// <summary>Action demandée par l'interface (solo, hôte : exécutée ici ; invité : envoyée à l'hôte).</summary>
        public string Request(WasherAction action, int bay)
        {
            if (Remote != null)
            {
                Remote.RequestWasher((byte)action, bay);
                return "Demande envoyée à l'hôte…";
            }
            string message = Execute(action, bay);
            Message?.Invoke(message);
            return message;
        }

        /// <summary>Décision de l'autorité (solo, hôte) : chargement, sortie ou abandon.</summary>
        public string Execute(WasherAction action, int bay)
        {
            switch (action)
            {
                case WasherAction.Load: return LoadFrom(bay);
                case WasherAction.Collect: return Collect(bay);
                case WasherAction.Abandon: return AbandonContents();
                default: return "Action inconnue";
            }
        }

        string LoadFrom(int bay)
        {
            if (!owned) return "Machine à laver : à acheter sur l'ordinateur (Boutique, onglet Atelier)";
            if (IsReplica) return "Machine à laver : l'hôte décide";
            if (state == WasherState.Washing) return $"Machine à laver : lavage en cours ({WasherRules.Format(remaining)})";
            if (state == WasherState.Done) return "Machine à laver : récupérez d'abord le tapis lavé";
            var rug = orders.RugOf(bay);
            var candidate = orders.OrderOf(bay);
            if (candidate == null || rug == null || rug.Grid == null) return $"{OrderManager.BayLabel(bay)} : aucun tapis de commande à laver";
            string encoded = SurfaceStateCodec.Encode(rug.Grid);
            float initial = rug.Grid.InitialDirt;
            if (!orders.TryHoldForMachine(bay, out var held, out float elapsed)) return "Machine à laver : ce tapis ne peut pas être chargé maintenant";
            order = held;
            orderId = held.id;
            grid = encoded;
            initialDirt = initial;
            orderElapsed = elapsed;
            cycle = NextCycleSeconds;
            remaining = cycle;
            state = WasherState.Washing;
            Changed();
            return $"« {held.title} » en machine : lavage de {WasherRules.Format(cycle)}. {OrderManager.BayLabel(bay)} libre pour une autre commande.";
        }

        string Collect(int preferredBay)
        {
            if (IsReplica) return "Machine à laver : l'hôte décide";
            if (state == WasherState.Washing) return $"Lavage en cours : encore {WasherRules.Format(remaining)}";
            if (state != WasherState.Done || order == null) return "Machine à laver vide";
            int bay = orders.IsBayFree(preferredBay) ? preferredBay : FirstFreeBay();
            if (bay < 0) return "Aucune zone de lavage libre : livrez ou abandonnez d'abord une commande";
            string title = order.title;
            if (!orders.TryPlaceFromMachine(order, orderElapsed, bay, ApplyWash)) return "Machine à laver : le tapis ne peut pas être reposé maintenant";
            Clear();
            if (audioManager != null) audioManager.PlayInterface(audioManager.ConfirmClip);
            return $"Tapis lavé « {title} » posé en {OrderManager.BayLabel(bay).ToLowerInvariant()} : traitez les taches tenaces puis séchez-le.";
        }

        string AbandonContents()
        {
            if (IsReplica) return "Machine à laver : l'hôte décide";
            if (state == WasherState.Free || order == null) return "Machine à laver vide";
            string title = order.title;
            orders.AbandonHeld(order);
            Clear();
            return $"Commande « {title} » abandonnée : elle redevient disponible.";
        }

        int FirstFreeBay()
        {
            for (int i = 0; i < orders.BayCount; i++)
                if (orders.IsBayFree(i)) return i;
            return -1;
        }

        void Clear()
        {
            state = WasherState.Free;
            order = null;
            orderId = string.Empty;
            grid = string.Empty;
            remaining = cycle = orderElapsed = initialDirt = 0f;
            Changed();
        }

        /// <summary>État du tapis à l'entrée en machine, puis lavage complet (sur la grille fraîchement rechargée).</summary>
        void ApplyWash(RugCleaningController rug)
        {
            var target = rug.Grid;
            if (target == null) return;
            if (!string.IsNullOrEmpty(grid) && SurfaceStateCodec.TryDecode(grid, target)) target.SetInitialDirt(initialDirt);
            else Debug.LogWarning("[RugWash] Machine à laver : état du tapis illisible, tapis de la commande relavé depuis son état d'origine.");
            var wash = new MachineWashOperation
            {
                Table = rug.DirtTable,
                DustRemoved = dustRemoved,
                StainRemoved = stainRemoved,
                MoistureAfter = moistureAfter,
                Wear = wearPerFragility * rug.Fragility,
            };
            target.ApplyAll(ref wash);
            LastWashRemoved = wash.Removed;
        }

        void Changed()
        {
            RefreshVisuals();
            StateChanged?.Invoke();
        }

        // ------------------------------------------------------------------ Temps

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (state == WasherState.Washing)
            {
                // Invité : décompte local entre deux mises à jour de l'hôte (l'hôte seul termine le cycle).
                remaining = WasherRules.Advance(remaining, dt);
                if (remaining <= 0f && !IsReplica)
                {
                    state = WasherState.Done;
                    if (audioManager != null) audioManager.PlayInterface(doneClip != null ? doneClip : audioManager.ConfirmClip);
                    Message?.Invoke(order != null ? $"Machine à laver : « {order.title} » est lavé — récupérez-le" : "Machine à laver : lavage terminé");
                    Changed();
                }
                spin = Mathf.Repeat(spin + dt * 300f, 360f);
                if (drum != null) drum.localRotation = Quaternion.Euler(0f, 0f, spin);
            }
            if (display != null && owned && Time.frameCount % 15 == 0) display.text = DisplayText();
            if (loopSource != null)
            {
                bool washing = state == WasherState.Washing && owned;
                if (washing && !loopSource.isPlaying) loopSource.Play();
                else if (!washing && loopSource.isPlaying) loopSource.Stop();
            }
        }

        /// <summary>Avance le cycle de <paramref name="seconds"/> (tests : par pas bornés, comme en jeu).</summary>
        public void AdvanceForTests(float seconds)
        {
            while (seconds > 0f && state == WasherState.Washing)
            {
                float step = Mathf.Min(seconds, WasherRules.MaxStepSeconds);
                remaining = WasherRules.Advance(remaining, step);
                seconds -= step;
            }
            if (state == WasherState.Washing && remaining <= 0f && !IsReplica)
            {
                state = WasherState.Done;
                Changed();
            }
        }

        string DisplayText()
        {
            switch (state)
            {
                case WasherState.Washing: return $"LAVAGE\n{WasherRules.Format(remaining)}";
                case WasherState.Done: return "TERMINÉ";
                default: return fastProgram ? "LIBRE\nrapide" : "LIBRE";
            }
        }

        void RefreshVisuals()
        {
            if (display != null)
            {
                display.gameObject.SetActive(owned);
                display.text = DisplayText();
            }
            if (statusLamp != null && lampBlock != null)
            {
                Color color = state == WasherState.Washing ? new Color(1f, 0.65f, 0.15f) : state == WasherState.Done ? new Color(0.3f, 1f, 0.4f) : new Color(0.35f, 0.75f, 1f);
                statusLamp.GetPropertyBlock(lampBlock);
                lampBlock.SetColor(BaseColorId, color);
                lampBlock.SetColor(EmissionId, color * 2f);
                statusLamp.SetPropertyBlock(lampBlock);
            }
            if (door != null) door.localRotation = Quaternion.Euler(0f, state == WasherState.Washing ? 0f : doorOpenAngle, 0f);
        }

        // ------------------------------------------------------------------ Sauvegarde et réseau

        public WasherSaveData Capture() => new WasherSaveData
        {
            state = (int)state,
            orderId = orderId,
            remaining = remaining,
            cycleSeconds = cycle,
            orderElapsed = orderElapsed,
            grid = grid,
            initialDirt = initialDirt,
        };

        /// <summary>
        /// Reprend l'état sauvegardé (après les commandes et les missions) : la commande en machine redevient active.
        /// Une donnée invalide (commande inconnue ou déjà livrée, état hors bornes) laisse la machine vide.
        /// </summary>
        public void Restore(WasherSaveData data)
        {
            if (data == null || IsReplica) return;
            var saved = (WasherState)Mathf.Clamp(data.state, 0, 2);
            if (saved == WasherState.Free || string.IsNullOrEmpty(data.orderId))
            {
                if (state != WasherState.Free) Clear();
                return;
            }
            var restored = orders.Resolve(data.orderId);
            if (restored == null || !orders.RestoreHeld(restored))
            {
                Debug.LogWarning($"[RugWash] Machine à laver : commande « {data.orderId} » introuvable ou terminée, machine vidée.");
                Clear();
                return;
            }
            order = restored;
            orderId = restored.id;
            cycle = data.cycleSeconds > 0f ? Mathf.Min(data.cycleSeconds, cycleSeconds) : NextCycleSeconds;
            remaining = saved == WasherState.Done ? 0f : WasherRules.Sanitize(data.remaining, cycle);
            state = remaining <= 0f ? WasherState.Done : WasherState.Washing;
            orderElapsed = Mathf.Max(0f, data.orderElapsed);
            grid = data.grid ?? string.Empty;
            initialDirt = Mathf.Max(0f, data.initialDirt);
            Changed();
        }

        /// <summary>Invité : état décidé par l'hôte (temps restant recalé à chaque mise à jour).</summary>
        public void ApplyRemote(WasherState remoteState, float remoteRemaining, float remoteCycle, string remoteOrderId)
        {
            var remoteOrder = string.IsNullOrEmpty(remoteOrderId) ? null : orders.Resolve(remoteOrderId);
            bool changed = remoteState != state || remoteOrder != order;
            state = remoteOrder == null ? WasherState.Free : remoteState;
            order = remoteOrder;
            orderId = remoteOrder != null ? remoteOrder.id : string.Empty;
            remaining = Mathf.Max(0f, remoteRemaining);
            cycle = Mathf.Max(0f, remoteCycle);
            orders.ApplyRemoteHeld(order);
            if (changed) Changed();
        }
    }
}
