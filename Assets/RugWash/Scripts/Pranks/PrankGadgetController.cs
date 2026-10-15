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
        [SerializeField] Transform blasterModel;
        [Tooltip("Bouche du fusil (bouffée, origine du tir).")]
        [SerializeField] Transform blasterTip;

        readonly float[] readyAt = new float[4];
        readonly PrankRequest[] pending = new PrankRequest[4];
        readonly List<PrankTargetInfo> targets = new List<PrankTargetInfo>();
        readonly List<Vector3> feet = new List<Vector3>();
        LocalActor actor;
        float animTimer, animDuration;
        Quaternion batRest, blasterRest;
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

        public event Action<PrankGadget> EquippedChanged;
        public event Action<string> Message;

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
        }

        void OnEnable()
        {
            if (tools != null) tools.ResumeRequested += Unequip;
            if (upgrades != null) upgrades.Changed += OnUpgradesChanged;
        }

        void OnDisable()
        {
            if (tools != null) tools.ResumeRequested -= Unequip;
            if (upgrades != null) upgrades.Changed -= OnUpgradesChanged;
        }

        void OnUpgradesChanged()
        {
            if (Equipped != PrankGadget.None && !Owns(Equipped)) Unequip();
        }

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
                if (Owns(candidate))
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
        }

        // ------------------------------------------------------------------ Utilisation

        void Update()
        {
            Animate(Time.deltaTime);
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
            float remaining = CooldownRemaining(gadget);
            if (remaining > 0f) return Refuse(gadget, PrankResult.Cooldown, remaining);
            if (settings.costPerUse > 0 && wallet.Balance < settings.costPerUse) return Refuse(gadget, PrankResult.InsufficientFunds, 0f);
            if (!TryTarget(settings, out PrankRequest request, out PrankResult problem)) return Refuse(gadget, problem, 0f);

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
                if (surface != null && surface == rug && rug.IsLoaded)
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
                // Élan puis coup en travers de l'écran, retour en position.
                float swing = t < 0.35f ? Mathf.SmoothStep(0f, 1f, t / 0.35f) : Mathf.SmoothStep(1f, 0f, (t - 0.35f) / 0.65f);
                batModel.localRotation = batRest * Quaternion.Euler(swing * 70f, -swing * 85f, swing * 25f);
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

        /// <summary>Coup de batte (farce) : chute comique, relevé rapide possible.</summary>
        public void ReceiveKnock(Vector3 impulse, float seconds, string from)
        {
            if (knockdown == null) return;
            knockdown.Knockdown(impulse, seconds, $"{from} vous a envoyé au tapis (farce) — {knockdown.QuickRecoverHint}");
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
                    case PrankGadget.Blaster: return "tirer du faux sang (tapis)";
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
                if (settings.costPerUse > 0) state += $" · {settings.costPerUse} € par tir";
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
