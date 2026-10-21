using System;
using System.Collections.Generic;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Economy;
using RugWash.Player;
using RugWash.UI;
using UnityEngine;

namespace RugWash.Pranks
{
    /// <summary>Silhouette d'un autre joueur que l'on peut viser (fournie par la partie en ligne).</summary>
    public struct PrankTargetInfo
    {
        public ulong ClientId;
        public Vector3 Feet;
    }

    /// <summary>
    /// Gadgets de farce du joueur local (v0.1.2) : G (croix haut) sort le gadget suivant parmi ceux achetés, puis range ;
    /// une touche d'outil revient aux outils. Clic gauche (RT) : une farce par appui, vers le tapis visé ou un coéquipier
    /// visé. En solo ou chez l'hôte, la farce est validée et appliquée par <see cref="PrankService"/> ; un invité la
    /// demande à l'hôte et n'en voit les effets qu'après validation. Reçoit aussi les farces subies (gouttes à l'écran,
    /// chute comique).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PrankGadgetController : MonoBehaviour
    {
        [SerializeField] PrankTuning tuning;
        [SerializeField] PlayerInputReader input;
        [SerializeField] Camera playerCamera;
        [SerializeField] PlayerToolController tools;
        [SerializeField] GameStateManager gameState;
        [SerializeField] UpgradeManager upgrades;
        [SerializeField] WalletManager wallet;
        [SerializeField] PrankService service;
        [SerializeField] PrankEffects effects;
        [SerializeField] PlayerKnockdown knockdown;
        [SerializeField] ScreenSplash splash;
        [SerializeField] Audio.AudioManager audioManager;
        [SerializeField] RugCleaningController rug;
        [SerializeField] LayerMask aimMask = ~0;
        [Header("Modèles tenus en main")]
        [SerializeField] Transform batModel;
        /// <summary>Modèles tenus en main (icônes 3D des gadgets, grande mise à jour).</summary>
        public Transform BatModel => batModel;
        public Transform BlasterModel => blasterModel;
        [SerializeField] Transform blasterModel;
        [Tooltip("Bouche du fusil (bouffée, origine du tir).")]
        [SerializeField] Transform blasterTip;
        [Tooltip("V0.4.2 : bâton de dynamite tenu en main (caché tant qu'il n'est pas sorti).")]
        [SerializeField] Transform dynamiteModel;
        public Transform DynamiteModel => dynamiteModel;
        [Tooltip("V0.4.2 : secousse de la caméra à l'explosion (optionnelle).")]
        [SerializeField] CameraMotion cameraMotion;
        [Tooltip("Inventaire (grande mise à jour) : batte et fusil doivent être portés ; le pipi, jamais limité. Optionnel.")]
        [SerializeField] PlayerInventory inventory;

        readonly float[] readyAt = new float[PrankTuning.GadgetSlots];
        readonly PrankRequest[] pending = new PrankRequest[PrankTuning.GadgetSlots];
        readonly List<PrankTargetInfo> targets = new List<PrankTargetInfo>();
        readonly List<Vector3> feet = new List<Vector3>();
        LocalActor actor;
        float animTimer, animDuration;
        Quaternion batRest, blasterRest, dynamiteRest;
        Vector3 dynamiteRestPosition;
        Vector3 blasterRestPosition;

        public PrankGadget Equipped { get; private set; }
        public PrankTuning Tuning => tuning;
        public PrankService Service => service;
        public PrankEffects Effects => effects;
        /// <summary>Gouttes à l'écran du joueur local.</summary>
        public ScreenSplash Splash => splash;
        /// <summary>Multijoueur (invité) : demandes envoyées à l'hôte. Null en solo et chez l'hôte.</summary>
        public IWorkshopRequests Remote { get; set; }
        /// <summary>Silhouettes des autres joueurs (partie en ligne) ; null en solo.</summary>
        public Func<IReadOnlyList<PrankTargetInfo>> RemotePlayers { get; set; }
        /// <summary>Identifiant réseau du joueur local (0 en solo).</summary>
        public ulong LocalId { get; set; }
        public string LocalName { get; set; } = "Vous";
        /// <summary>Dernier résultat d'une farce de ce joueur (tests).</summary>
        public PrankResult LastResult { get; private set; }
        public PrankRequest LastRequest { get; private set; }
        /// <summary>Réponses reçues de l'autorité (diagnostic : farce en ligne validée ou refusée).</summary>
        public int ResultsReceived { get; private set; }

        public event Action<PrankGadget> EquippedChanged;
        public event Action<string> Message;
        /// <summary>Un coup de batte part (même dans le vide) : les autres joueurs voient le geste (réseau).</summary>
        public event Action Swung;

        // Batte (grande mise à jour) : le mouvement part toujours ; la cible est cherchée à l'impact.
        float nextSwingAt, impactTimer, bounce;
        bool impactPending;
        /// <summary>Coups de batte partis, et coups partis dans le vide (sans cible : aucune recharge).</summary>
        public int Swings { get; private set; }
        public int MissedSwings { get; private set; }
        /// <summary>Un coup de batte est en cours (son impact n'est pas encore résolu).</summary>
        public bool SwingPending => impactPending;

        public bool IsConfigured(out string missing)
        {
            missing = tuning == null ? nameof(tuning) : input == null ? nameof(input) : playerCamera == null ? nameof(playerCamera)
                : tools == null ? nameof(tools) : gameState == null ? nameof(gameState) : upgrades == null ? nameof(upgrades)
                : wallet == null ? nameof(wallet) : service == null ? nameof(service) : effects == null ? nameof(effects)
                : splash == null ? nameof(splash) : rug == null ? nameof(rug) : null;
            return missing == null;
        }

        void Awake()
        {
            actor = new LocalActor(this);
            if (batModel != null)
            {
                batRest = batModel.localRotation;
                batModel.gameObject.SetActive(false);
            }
            if (blasterModel != null)
            {
                blasterRest = blasterModel.localRotation;
                blasterRestPosition = blasterModel.localPosition;
                blasterModel.gameObject.SetActive(false);
            }
            if (dynamiteModel != null)
            {
                dynamiteRest = dynamiteModel.localRotation;
                dynamiteRestPosition = dynamiteModel.localPosition;
                dynamiteModel.gameObject.SetActive(false);
            }
        }

        void OnEnable()
        {
            if (tools != null)
            {
                tools.ResumeRequested += Unequip;
                tools.GadgetRequested += OnGadgetRequested;
            }
            if (upgrades != null) upgrades.Changed += OnUpgradesChanged;
            if (inventory != null) inventory.Changed += OnUpgradesChanged;
            if (effects != null) effects.Exploded += OnExplosion;
        }

        void OnDisable()
        {
            if (tools != null)
            {
                tools.ResumeRequested -= Unequip;
                tools.GadgetRequested -= OnGadgetRequested;
            }
            if (upgrades != null) upgrades.Changed -= OnUpgradesChanged;
            if (inventory != null) inventory.Changed -= OnUpgradesChanged;
            if (effects != null) effects.Exploded -= OnExplosion;
            if (slowMotionActive) EndSlowMotion();
        }

        void OnGadgetRequested(PrankGadget gadget)
        {
            if (gadget != Equipped) Equip(gadget);
        }

        void OnUpgradesChanged()
        {
            if (Equipped != PrankGadget.None && !IsAtHand(Equipped)) Unequip();
        }

        /// <summary>
        /// Gadget utilisable maintenant : acheté et porté (inventaire). Le pipi n'occupe jamais d'emplacement : acheté, il est
        /// toujours disponible.
        /// </summary>
        public bool IsAtHand(PrankGadget gadget) => Owns(gadget) && (inventory == null || inventory.IsCarried(gadget));

        public bool Owns(PrankGadget gadget)
        {
            var settings = tuning != null ? tuning.Get(gadget) : null;
            return settings != null && upgrades != null && upgrades.OwnsGadget(settings.upgradeId);
        }

        /// <summary>Au moins un gadget de farce acheté.</summary>
        public bool HasAnyGadget
        {
            get
            {
                foreach (var gadget in PrankTuning.All)
                    if (Owns(gadget)) return true;
                return false;
            }
        }

        /// <summary>Recharge restante d'un gadget pour ce joueur (s).</summary>
        public float CooldownRemaining(PrankGadget gadget) => Mathf.Max(0f, readyAt[(int)gadget] - Time.time);

        // ------------------------------------------------------------------ Sélection

        /// <summary>Gadget suivant parmi ceux achetés, puis retour aux outils.</summary>
        public void CycleNext()
        {
            int start = (int)Equipped;
            for (int step = 1; step <= PrankTuning.All.Length; step++)
            {
                int index = start + step;
                if (index > PrankTuning.All.Length) break;
                var candidate = (PrankGadget)index;
                if (IsAtHand(candidate) || (PlayerInventory.AutomationCarriesOnSelect && Owns(candidate)))
                {
                    Equip(candidate);
                    return;
                }
            }
            if (Equipped != PrankGadget.None) Unequip();
            else Message?.Invoke("Aucun gadget de farce : ordinateur, Boutique, onglet Farces");
        }

        public bool Equip(PrankGadget gadget)
        {
            if (gadget == PrankGadget.None)
            {
                Unequip();
                return true;
            }
            if (!Owns(gadget))
            {
                Message?.Invoke(PrankRules.Describe(PrankResult.NotOwned, Name(gadget)));
                return false;
            }
            if (!IsAtHand(gadget) && !(inventory != null && inventory.CarryGadgetForAutomation(gadget)))
            {
                Message?.Invoke($"{Name(gadget)} est au casier : prenez-le au casier");
                return false;
            }
            Equipped = gadget;
            tools.Suspended = true;
            ShowModels();
            EquippedChanged?.Invoke(gadget);
            return true;
        }

        public void Unequip()
        {
            if (Equipped == PrankGadget.None) return;
            Equipped = PrankGadget.None;
            animTimer = 0f;
            impactPending = false;
            ShowModels();
            tools.Suspended = false;
            EquippedChanged?.Invoke(PrankGadget.None);
        }

        void ShowModels()
        {
            if (batModel != null)
            {
                batModel.gameObject.SetActive(Equipped == PrankGadget.Bat);
                batModel.localRotation = batRest;
            }
            if (blasterModel != null)
            {
                blasterModel.gameObject.SetActive(Equipped == PrankGadget.Blaster);
                blasterModel.localRotation = blasterRest;
                blasterModel.localPosition = blasterRestPosition;
            }
            if (dynamiteModel != null)
            {
                dynamiteModel.gameObject.SetActive(Equipped == PrankGadget.Dynamite);
                dynamiteModel.localRotation = dynamiteRest;
                dynamiteModel.localPosition = dynamiteRestPosition;
            }
        }

        // ------------------------------------------------------------------ Utilisation

        void Update()
        {
            if (slowMotionActive && Time.unscaledTime >= slowMotionUntil) EndSlowMotion();
            Animate(Time.deltaTime);
            if (impactPending)
            {
                impactTimer -= Time.deltaTime;
                if (impactTimer <= 0f) ResolveBatImpact();
            }
            if (gameState == null || !gameState.IsGameplayActive) return;
            if (input.GadgetPressed) CycleNext();
            if (Equipped == PrankGadget.None || (knockdown != null && knockdown.IsDown)) return;
            if (input.UsePressed) TryUse();
        }

        /// <summary>Tente la farce du gadget en main vers ce qui est visé. Faux si elle n'a pas pu partir.</summary>
        public bool TryUse()
        {
            var gadget = Equipped;
            var settings = tuning.Get(gadget);
            if (settings == null) return false;
            if (gadget == PrankGadget.Bat) return Swing(settings);
            float remaining = CooldownRemaining(gadget);
            if (remaining > 0f) return Refuse(gadget, PrankResult.Cooldown, remaining);
            if (settings.costPerUse > 0 && wallet.Balance < settings.costPerUse) return Refuse(gadget, PrankResult.InsufficientFunds, 0f);
            PrankRequest request;
            PrankResult problem;
            bool aimed = gadget == PrankGadget.Dynamite ? TryTargetThrow(settings, out request, out problem) : TryTarget(settings, out request, out problem);
            if (!aimed) return Refuse(gadget, problem, 0f);

            StartAnimation(settings);
            LastRequest = request;
            if (Remote != null)
            {
                // Invité : recharge comptée tout de suite (pas de rafale pendant l'aller-retour), annulée si l'hôte refuse.
                pending[(int)gadget] = request;
                readyAt[(int)gadget] = Time.time + settings.cooldownSeconds;
                Remote.RequestPrank(request);
                return true;
            }
            var result = service.Execute(actor, request, out PrankOutcome outcome);
            OnResult(gadget, result, outcome);
            return result == PrankResult.Done;
        }

        /// <summary>
        /// Coup de batte : le mouvement et son sifflement partent toujours, même sans personne devant (coups dans le vide
        /// enchaînables, au rythme du mouvement). La cible est cherchée au moment de l'impact, dans l'axe de visée et à portée :
        /// l'animation, la portée et la zone touchée coïncident. La recharge ne démarre que si le coup touche.
        /// </summary>
        bool Swing(PrankTuning.Gadget settings)
        {
            if (Time.time < nextSwingAt || impactPending) return false;
            nextSwingAt = Time.time + Mathf.Max(tuning.batSwingInterval, settings.useSeconds);
            StartAnimation(settings);
            bounce = 0f;
            impactPending = true;
            impactTimer = settings.useSeconds * tuning.batImpactFraction;
            Swings++;
            effects.PlaySwing(EffectOrigin(PrankGadget.Bat));
            Swung?.Invoke();
            return true;
        }

        void ResolveBatImpact()
        {
            impactPending = false;
            var settings = tuning.Get(PrankGadget.Bat);
            if (Equipped != PrankGadget.Bat || settings == null || (knockdown != null && knockdown.IsDown)) return;
            if (!TryTarget(settings, out PrankRequest request, out _) )
            {
                // Dans le vide : rien à valider, aucune recharge, aucun message.
                MissedSwings++;
                LastResult = PrankResult.NoTarget;
                return;
            }
            float remaining = CooldownRemaining(PrankGadget.Bat);
            if (remaining > 0f)
            {
                // Coup qui atteint une cible pendant la recharge : sans effet (le geste reste visible).
                LastResult = PrankResult.Cooldown;
                Message?.Invoke($"{Name(PrankGadget.Bat)} : coup sans effet, recharge encore {Mathf.CeilToInt(remaining)} s");
                return;
            }
            LastRequest = request;
            if (Remote != null)
            {
                pending[(int)PrankGadget.Bat] = request;
                readyAt[(int)PrankGadget.Bat] = Time.time + settings.cooldownSeconds;
                Remote.RequestPrank(request);
                return;
            }
            var result = service.Execute(actor, request, out PrankOutcome outcome);
            OnResult(PrankGadget.Bat, result, outcome);
        }

        /// <summary>Zones de lavage (grande mise à jour) : tapis des autres zones, reconnus par le gestionnaire de commandes.</summary>
        public Orders.OrderManager Orders { get; set; }

        bool IsOrderRug(RugCleaningController surface) => Orders != null && Orders.RugOf(Orders.BayOf(surface)) == surface;

        bool TryTarget(PrankTuning.Gadget settings, out PrankRequest request, out PrankResult problem)
        {
            var ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            request = new PrankRequest { Gadget = settings.kind, Origin = ray.origin, Direction = ray.direction };
            problem = PrankResult.NoTarget;
            float rugDistance = float.MaxValue;
            bool rugHit = false;
            Vector3 rugPoint = default;
            if (Physics.Raycast(ray, out RaycastHit hit, 8f, aimMask, QueryTriggerInteraction.Ignore))
            {
                var surface = hit.collider.GetComponentInParent<RugCleaningController>();
                // Tapis d'une commande (zone 1 ou 2) ; l'aire et le sol ne sont pas des cibles de farce.
                if (surface != null && surface.IsLoaded && surface.Kind == SurfaceKind.Rug && (surface == rug || IsOrderRug(surface)))
                {
                    rugHit = true;
                    rugDistance = hit.distance;
                    rugPoint = hit.point;
                }
            }

            var players = RemotePlayers?.Invoke();
            if (players != null && players.Count > 0)
            {
                feet.Clear();
                foreach (var player in players) feet.Add(player.Feet);
                int index = PrankRules.PickPlayer(ray, feet, Mathf.Min(rugDistance, settings.range + 0.5f), tuning.playerHitRadius);
                if (index >= 0)
                {
                    if (!settings.affectsPlayers)
                    {
                        problem = PrankResult.InvalidTarget;
                        return false;
                    }
                    request.Target = PrankTarget.Player;
                    request.Victim = players[index].ClientId;
                    request.Point = players[index].Feet + Vector3.up * PrankRules.BodyHeight;
                    return true;
                }
            }
            if (!rugHit || !settings.affectsRug) return false;
            if (rugDistance > settings.range)
            {
                problem = PrankResult.OutOfRange;
                return false;
            }
            request.Target = PrankTarget.Rug;
            request.Point = rugPoint;
            return true;
        }

        /// <summary>
        /// V0.4.2 : point de chute de la dynamite : ce que vise le joueur (sol, tapis, mur…) à portée de lancer, sinon le sol
        /// sous le point le plus lointain atteignable. Aucun joueur n'est visé : l'explosion touche toute la zone.
        /// </summary>
        bool TryTargetThrow(PrankTuning.Gadget settings, out PrankRequest request, out PrankResult problem)
        {
            var ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            request = new PrankRequest { Gadget = settings.kind, Target = PrankTarget.Area, Origin = ray.origin, Direction = ray.direction };
            problem = PrankResult.NoTarget;
            Vector3 point = Physics.Raycast(ray, out RaycastHit hit, settings.range, aimMask, QueryTriggerInteraction.Ignore)
                ? hit.point - ray.direction * 0.15f
                : ray.GetPoint(settings.range);
            if (!Physics.Raycast(point + Vector3.up * 0.2f, Vector3.down, out RaycastHit ground, 8f, aimMask, QueryTriggerInteraction.Ignore)) return false;
            request.Point = ground.point + Vector3.up * 0.05f;
            return true;
        }

        // ------------------------------------------------------------------ Explosion ressentie (V0.4.2)

        /// <summary>
        /// Ralenti bref permis sur ce poste (faux chez l'hôte d'une partie en ligne avec des invités : l'autorité ne ralentit
        /// jamais la partie des autres). Vrai par défaut (solo).
        /// </summary>
        public Func<bool> AllowSlowMotion { get; set; }
        bool slowMotionActive;
        float slowMotionUntil;
        /// <summary>Ralentis joués (tests).</summary>
        public int SlowMotions { get; private set; }
        public bool SlowMotionActive => slowMotionActive;

        /// <summary>Explosion jouée près de ce joueur : secousse de la caméra dosée par la distance, ralenti bref si on est dans le souffle.</summary>
        void OnExplosion(Vector3 point)
        {
            if (playerCamera == null || tuning == null) return;
            float distance = Vector3.Distance(playerCamera.transform.position, point);
            float radius = tuning.dynamiteRadius;
            if (distance > radius * 3f) return;
            float strength = 1f - Mathf.Clamp01(distance / (radius * 3f));
            if (cameraMotion != null) cameraMotion.Shake(0.05f * strength + 0.015f, 0.35f + 0.35f * strength);
            if (distance <= radius * 1.5f && (AllowSlowMotion == null || AllowSlowMotion())) BeginSlowMotion();
        }

        void BeginSlowMotion()
        {
            // Jamais en pause ni pendant un autre ralenti ; durée en temps réel, le temps normal revient toujours.
            if (slowMotionActive || !Mathf.Approximately(Time.timeScale, 1f) || (gameState != null && gameState.IsPaused)) return;
            slowMotionActive = true;
            slowMotionUntil = Time.unscaledTime + 0.35f;
            Time.timeScale = 0.35f;
            SlowMotions++;
        }

        void EndSlowMotion()
        {
            slowMotionActive = false;
            if (Mathf.Approximately(Time.timeScale, 0.35f)) Time.timeScale = 1f;
        }

        bool Refuse(PrankGadget gadget, PrankResult result, float remaining)
        {
            LastResult = result;
            var settings = tuning.Get(gadget);
            Message?.Invoke(PrankRules.Describe(result, Name(gadget), remaining, settings != null ? settings.costPerUse : 0));
            if (audioManager != null) audioManager.PlayInterface(audioManager.DenyClip, 0.8f);
            return false;
        }

        /// <summary>Réponse de l'autorité (locale, ou de l'hôte pour un invité).</summary>
        public void OnResult(PrankGadget gadget, PrankResult result, in PrankOutcome outcome)
        {
            var settings = tuning.Get(gadget);
            if (settings == null) return;
            ResultsReceived++;
            if (result != PrankResult.Done)
            {
                readyAt[(int)gadget] = 0f;
                float remaining = Remote == null && service != null ? service.CooldownRemaining(LocalId, gadget) : 0f;
                Refuse(gadget, result, remaining);
                return;
            }
            LastResult = result;
            readyAt[(int)gadget] = Time.time + settings.cooldownSeconds;
            effects.Play(gadget, EffectOrigin(gadget), outcome.Point, outcome.HitPlayer, settings.useSeconds);
            if (gadget == PrankGadget.Bat)
            {
                // Retour d'impact : la batte rebondit, et le joueur sait qu'il a touché.
                bounce = 1f;
                Message?.Invoke(outcome.HitPlayer ? "Touché !" : "Coup de batte sur le tapis");
            }
        }

        /// <summary>Invité : l'hôte a répondu à la dernière demande pour ce gadget.</summary>
        public void OnRemoteResult(PrankGadget gadget, PrankResult result, bool hitPlayer)
        {
            var request = pending[(int)gadget];
            OnResult(gadget, result, new PrankOutcome { Gadget = gadget, Point = request.Point, HitPlayer = hitPlayer, Actor = LocalId });
        }

        /// <summary>D'où part l'effet chez le joueur local : hanches pour le jet, bouche du fusil, main pour la batte.</summary>
        Vector3 EffectOrigin(PrankGadget gadget)
        {
            var body = tools.transform;
            switch (gadget)
            {
                case PrankGadget.Pee: return body.position + Vector3.up * 0.9f + body.forward * 0.25f;
                case PrankGadget.Blaster when blasterTip != null: return blasterTip.position;
                case PrankGadget.Dynamite when dynamiteModel != null: return dynamiteModel.position;
                default: return playerCamera.transform.position + playerCamera.transform.right * 0.3f + playerCamera.transform.forward * 0.5f;
            }
        }

        void StartAnimation(PrankTuning.Gadget settings)
        {
            animDuration = settings.useSeconds;
            animTimer = animDuration;
        }

        void Animate(float dt)
        {
            if (animTimer <= 0f || dt <= 0f) return;
            animTimer = Mathf.Max(0f, animTimer - dt);
            float t = 1f - animTimer / Mathf.Max(0.01f, animDuration);
            if (Equipped == PrankGadget.Bat && batModel != null)
            {
                // Armé en arrière, coup rapide en travers de l'écran (impact vers 38 %), puis retour en position. Un coup
                // qui touche rebondit (la tête de la batte repart en arrière d'un coup sec).
                float strike = tuning != null ? tuning.batImpactFraction : 0.38f;
                float swing = t < strike ? -0.25f * Mathf.Sin(t / strike * Mathf.PI) + Mathf.SmoothStep(0f, 1f, t / strike)
                    : Mathf.SmoothStep(1f, 0f, (t - strike) / (1f - strike));
                bounce = Mathf.MoveTowards(bounce, 0f, dt * 6f);
                swing -= bounce * 0.35f;
                batModel.localRotation = batRest * Quaternion.Euler(swing * 70f, -swing * 95f, swing * 30f);
            }
            else if (Equipped == PrankGadget.Dynamite && dynamiteModel != null)
            {
                // Lancer : le bras part en arrière puis vers l'avant ; le bâton disparaît le temps qu'un autre sorte.
                float wind = t < 0.25f ? Mathf.SmoothStep(0f, 1f, t / 0.25f) : Mathf.SmoothStep(1f, -0.6f, Mathf.Clamp01((t - 0.25f) / 0.15f));
                dynamiteModel.localRotation = dynamiteRest * Quaternion.Euler(-70f * wind, 0f, 0f);
                dynamiteModel.localPosition = dynamiteRestPosition + new Vector3(0f, 0.08f * wind, -0.1f * wind);
                bool thrown = t > 0.4f && t < 0.95f;
                foreach (var renderer in dynamiteModel.GetComponentsInChildren<Renderer>(true)) renderer.enabled = !thrown;
            }
            else if (Equipped == PrankGadget.Blaster && blasterModel != null)
            {
                // Recul, puis la pompe est ramenée (va-et-vient de l'arme).
                float kick = Mathf.Exp(-t * 12f) * (t < 0.05f ? t / 0.05f : 1f);
                float pump = Mathf.Sin(Mathf.Clamp01((t - 0.35f) / 0.5f) * Mathf.PI);
                blasterModel.localPosition = blasterRestPosition + new Vector3(0f, 0.02f * kick, -0.07f * kick - 0.03f * pump);
                blasterModel.localRotation = blasterRest * Quaternion.Euler(-12f * kick, 0f, 0f);
            }
        }

        // ------------------------------------------------------------------ Farces subies

        /// <summary>Un coéquipier a fait pipi sur ce joueur : gouttes à l'écran (durée limitée, fondu).</summary>
        public void ReceiveSplash(float intensity, float seconds, float fadeSeconds, string from)
        {
            splash.Splash(intensity, seconds, fadeSeconds);
            Message?.Invoke($"{from} vous a fait une farce : gouttes sur la vue pendant {Mathf.CeilToInt(seconds)} s");
        }

        /// <summary>
        /// Coup reçu (farce) : batte = chute comique, relevé rapide possible ; fusil = KO cartoon, relevé automatique après
        /// quelques secondes. Le gadget en main est rangé (rien n'est perdu ni dupliqué : les gadgets sont des achats).
        /// </summary>
        public void ReceiveKnock(Vector3 impulse, float seconds, string from, PrankGadget cause = PrankGadget.Bat)
        {
            if (knockdown == null) return;
            Unequip();
            if (cause == PrankGadget.Blaster)
                knockdown.Knockdown(impulse, seconds, $"{from} vous a mis KO (fusil de farces) — debout dans {Mathf.CeilToInt(seconds)} s", KnockCause.Blaster);
            else if (cause == PrankGadget.Dynamite)
                knockdown.Knockdown(impulse, seconds, $"BOUM ! {from} vous a fait sauter (dynamite de farces) — debout dans {Mathf.CeilToInt(seconds)} s", KnockCause.Dynamite);
            else
                knockdown.Knockdown(impulse, seconds, $"{from} vous a envoyé au tapis (farce) — {knockdown.QuickRecoverHint}", KnockCause.Bat);
        }

        /// <summary>Fin de partie, replacement du joueur : plus aucun effet de farce en cours.</summary>
        public void ClearReceived()
        {
            if (splash != null) splash.Clear();
        }

        // ------------------------------------------------------------------ Interface

        public string Name(PrankGadget gadget) => tuning != null && tuning.Get(gadget) != null ? tuning.Get(gadget).displayName : "Gadget";

        public string ActionVerb
        {
            get
            {
                switch (Equipped)
                {
                    case PrankGadget.Pee: return "faire pipi (tapis ou coéquipier)";
                    case PrankGadget.Bat: return "frapper (tapis ou coéquipier)";
                    case PrankGadget.Blaster: return "tirer (tapis, ou coéquipier : KO 5 s)";
                    case PrankGadget.Dynamite: return "lancer (explosion : KO 5 s dans la zone)";
                    default: return "utiliser";
                }
            }
        }

        /// <summary>Ligne d'état du HUD : recharge et coût.</summary>
        public string StatusLine
        {
            get
            {
                var settings = tuning != null ? tuning.Get(Equipped) : null;
                if (settings == null) return string.Empty;
                float remaining = CooldownRemaining(Equipped);
                string state = remaining > 0f ? $"Recharge : {Mathf.CeilToInt(remaining)} s" : "Prête";
                if (settings.costPerUse > 0) state += $" · {settings.costPerUse} € par {(Equipped == PrankGadget.Dynamite ? "bâton" : "tir")}";
                return $"Farce · {state}";
            }
        }

        public float ReadyFraction
        {
            get
            {
                var settings = tuning != null ? tuning.Get(Equipped) : null;
                if (settings == null) return -1f;
                return 1f - Mathf.Clamp01(CooldownRemaining(Equipped) / settings.cooldownSeconds);
            }
        }

        sealed class LocalActor : IPrankActor
        {
            readonly PrankGadgetController owner;
            public LocalActor(PrankGadgetController owner) => this.owner = owner;
            public ulong Id => owner.LocalId;
            public string Name => owner.LocalName;
            public Vector3 Eye => owner.playerCamera.transform.position;
            public bool IsDown => owner.knockdown != null && owner.knockdown.IsDown;
            public bool Owns(PrankGadget gadget) => owner.Owns(gadget);
            public int Money => owner.wallet.Balance;
            public bool TrySpend(int amount) => owner.wallet.Wallet.TrySpend(amount);
        }

        /// <summary>Auteur local, pour les tests et l'hôte.</summary>
        public IPrankActor Actor => actor ??= new LocalActor(this);
    }
}
