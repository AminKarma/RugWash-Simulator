using System;
using System.Collections.Generic;
using RugWash.Cleaning;
using UnityEngine;

namespace RugWash.Pranks
{
    /// <summary>
    /// Autorité des farces (solo ou hôte) : vérifie l'achat, la recharge, le coût, la portée, la cible, le plafond de
    /// saleté par commande et la protection des joueurs déjà visés, puis applique l'effet. Un invité ne fait que demander :
    /// sa demande arrive ici avec sa position côté hôte et sa carrière tenue par l'hôte. Aucune farce ne touche à la
    /// progression d'un joueur (argent, achats, réputation) ; les taches restent nettoyables.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PrankService : MonoBehaviour
    {
        [SerializeField] PrankTuning tuning;
        [Tooltip("Tapis de la commande (taches des farces visant le tapis).")]
        [SerializeField] RugCleaningController rug;
        [Tooltip("Aire de lavage : taches de faux sang sous un joueur touché par le fusil (optionnel).")]
        [SerializeField] RugCleaningController workSurface;
        [Tooltip("Zones de lavage supplémentaires (agrandissement) : leurs tapis et leurs aires.")]
        [SerializeField] List<RugCleaningController> extraRugs = new List<RugCleaningController>();
        [SerializeField] List<RugCleaningController> extraWorkSurfaces = new List<RugCleaningController>();

        readonly Dictionary<ulong, float[]> readyAt = new Dictionary<ulong, float[]>();
        readonly Dictionary<ulong, float> globalReadyAt = new Dictionary<ulong, float>();
        readonly Dictionary<ulong, float> splashProtectedUntil = new Dictionary<ulong, float>();
        readonly Dictionary<ulong, float> knockProtectedUntil = new Dictionary<ulong, float>();
        /// <summary>Saleté ajoutée par les farces à chaque tapis posé (remise à zéro à chaque tapis).</summary>
        readonly Dictionary<RugCleaningController, float> addedTo = new Dictionary<RugCleaningController, float>();
        readonly List<RugCleaningController> rugs = new List<RugCleaningController>();
        int seed;

        public PrankTuning Tuning => tuning;
        public RugCleaningController Rug => rug;
        /// <summary>Joueurs visables (partie en ligne, hôte) ; null en solo.</summary>
        public IPrankVictims Victims { get; set; }
        /// <summary>V0.2.5 : objets perdus par un joueur touché par la batte (partie en ligne, hôte) ; null en solo.</summary>
        public IItemLoss ItemLoss { get; set; }
        /// <summary>Saleté ajoutée par les farces au tapis de la zone 1 (remise à zéro à chaque tapis).</summary>
        public float AddedToRug => AddedTo(rug);

        /// <summary>Saleté ajoutée par les farces à ce tapis depuis qu'il est posé.</summary>
        public float AddedTo(RugCleaningController surface) => surface != null && addedTo.TryGetValue(surface, out float added) ? added : 0f;
        /// <summary>Horloge des recharges (secondes) ; remplaçable par les tests.</summary>
        public Func<float> Clock { get; set; } = () => Time.time;
        public event Action<PrankOutcome> Executed;
        /// <summary>V0.4.2 : explosion de dynamite appliquée (une seule fois par lancer) : point, joueurs mis KO, poussière.</summary>
        public event Action<DynamiteBlast> Detonated;

        /// <summary>V0.4.2 : explosion en attente (mèche) puis appliquée une seule fois.</summary>
        public struct DynamiteBlast
        {
            public int Id;
            public ulong Actor;
            public string ActorName;
            public Vector3 Point;
            public float At;
            /// <summary>Après l'explosion : joueurs mis KO et poussière ajoutée (tous tapis confondus).</summary>
            public List<ulong> Victims;
            public float DustAdded;
            public int RugsDusted;
        }

        readonly List<DynamiteBlast> pendingBlasts = new List<DynamiteBlast>();
        readonly List<ulong> nearPlayers = new List<ulong>();
        int nextBlastId;
        /// <summary>Explosions appliquées depuis le début (tests, diagnostic).</summary>
        public int Detonations { get; private set; }
        public int PendingBlasts => pendingBlasts.Count;

        public bool IsConfigured(out string missing)
        {
            missing = tuning == null ? nameof(tuning) : rug == null ? nameof(rug) : null;
            return missing == null;
        }

        void Awake()
        {
            if (rug != null) rugs.Add(rug);
            foreach (var extra in extraRugs)
                if (extra != null && !rugs.Contains(extra)) rugs.Add(extra);
        }

        void OnEnable()
        {
            foreach (var surface in rugs) surface.RugLoaded += OnRugLoaded;
        }

        void OnDisable()
        {
            foreach (var surface in rugs) surface.RugLoaded -= OnRugLoaded;
        }

        void OnRugLoaded(RugCleaningController loaded) => addedTo[loaded] = 0f;

        /// <summary>Tapis de commande posé sous ce point (zones de lavage), ou null.</summary>
        RugCleaningController RugAt(Vector3 point)
        {
            foreach (var surface in rugs)
                if (surface.IsLoaded && surface.TryWorldToUV(point, out _)) return surface;
            return null;
        }

        /// <summary>Temps de recharge restant pour ce joueur et ce gadget (s).</summary>
        public float CooldownRemaining(ulong actor, PrankGadget gadget)
        {
            float now = Clock();
            float ready = readyAt.TryGetValue(actor, out var times) ? times[(int)gadget] : 0f;
            float global = globalReadyAt.TryGetValue(actor, out float g) ? g : 0f;
            return Mathf.Max(0f, Mathf.Max(ready, global) - now);
        }

        /// <summary>Oublie un joueur parti (recharges et protections).</summary>
        public void Forget(ulong clientId)
        {
            readyAt.Remove(clientId);
            globalReadyAt.Remove(clientId);
            splashProtectedUntil.Remove(clientId);
            knockProtectedUntil.Remove(clientId);
        }

        public PrankResult Execute(IPrankActor actor, in PrankRequest request, out PrankOutcome outcome)
        {
            outcome = default;
            var gadget = tuning != null ? tuning.Get(request.Gadget) : null;
            if (gadget == null || actor == null || actor.IsDown) return PrankResult.Unavailable;
            float now = Clock();
            if (!readyAt.TryGetValue(actor.Id, out var times)) readyAt[actor.Id] = times = new float[PrankTuning.GadgetSlots];
            globalReadyAt.TryGetValue(actor.Id, out float global);
            var check = PrankRules.CheckUse(actor.Owns(request.Gadget), now, times[(int)request.Gadget], global, actor.Money, gadget.costPerUse);
            // Batte : un coup dans le vide ne passe jamais par ici (rien à valider) ; un coup qui touche démarre la recharge
            // ici, chez l'autorité : un client modifié qui ignore sa recharge locale est refusé (Cooldown).
            if (check != PrankResult.Done) return check;

            Vector3 point;
            Vector3 victimFeet = default;
            RugCleaningController targetRug = null;
            if (request.Target == PrankTarget.Area)
            {
                // V0.4.2 : dynamite lancée vers un point de l'atelier, à portée de lancer depuis les yeux de l'auteur (chez l'autorité).
                if (request.Gadget != PrankGadget.Dynamite) return PrankResult.InvalidTarget;
                point = request.Point;
                if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z) || point.y < -1f || point.y > 6f) return PrankResult.InvalidTarget;
                if (!PrankRules.InRange(actor.Eye, point, gadget.range, tuning.rangeTolerance)) return PrankResult.OutOfRange;
            }
            else if (request.Gadget == PrankGadget.Dynamite) return PrankResult.InvalidTarget;
            else if (request.Target == PrankTarget.Player)
            {
                if (!gadget.affectsPlayers) return PrankResult.InvalidTarget;
                if (Victims == null || request.Victim == actor.Id || !Victims.TryGetFeet(request.Victim, out victimFeet)) return PrankResult.NoTarget;
                point = victimFeet + Vector3.up * PrankRules.BodyHeight;
                if (!PrankRules.InRange(actor.Eye, point, gadget.range + 0.5f, tuning.rangeTolerance)) return PrankResult.OutOfRange;
                var protection = request.Gadget == PrankGadget.Pee ? splashProtectedUntil : knockProtectedUntil;
                // Jamais un joueur déjà à terre (batte comme fusil : pas de KO enchaîné ni de chute rallongée).
                if ((protection.TryGetValue(request.Victim, out float until) && now < until)
                    || (request.Gadget != PrankGadget.Pee && Victims.IsDown(request.Victim)))
                    return PrankResult.TargetProtected;
            }
            else
            {
                if (!gadget.affectsRug) return PrankResult.InvalidTarget;
                targetRug = RugAt(request.Point);
                if (targetRug == null) return PrankResult.NoTarget;
                if (targetRug.IsFinished) return PrankResult.RugFinished;
                point = request.Point;
                if (!PrankRules.InRange(actor.Eye, point, gadget.range, tuning.rangeTolerance)) return PrankResult.OutOfRange;
                if (PrankRules.CapRemaining(AddedTo(targetRug), targetRug.Grid.InitialDirt, tuning.rugCapFraction, tuning.rugCapMinimum) <= 0f)
                    return PrankResult.CapReached;
            }

            if (gadget.costPerUse > 0 && !actor.TrySpend(gadget.costPerUse)) return PrankResult.InsufficientFunds;
            times[(int)request.Gadget] = now + gadget.cooldownSeconds;
            globalReadyAt[actor.Id] = now + tuning.globalCooldownSeconds;

            outcome = new PrankOutcome { Gadget = request.Gadget, Actor = actor.Id, Origin = request.Origin, Point = point };
            if (request.Target == PrankTarget.Area)
            {
                // Une explosion par lancer validé, après le vol et la mèche (même délai que l'effet joué par chaque poste).
                pendingBlasts.Add(new DynamiteBlast { Id = ++nextBlastId, Actor = actor.Id, ActorName = actor.Name, Point = point, At = now + gadget.useSeconds });
            }
            else if (request.Target == PrankTarget.Player)
            {
                outcome.HitPlayer = true;
                outcome.Victim = request.Victim;
                if (request.Gadget == PrankGadget.Pee)
                {
                    splashProtectedUntil[request.Victim] = now + tuning.splashProtectionSeconds;
                    Victims.Splash(request.Victim, tuning.splashIntensity, tuning.splashSeconds, tuning.splashFadeSeconds, actor.Name);
                }
                else
                {
                    // Batte : chute comique ; fusil : KO cartoon de 5 s, élan plus fort et un peu de hauteur (bornés à l'arrivée).
                    bool blast = request.Gadget == PrankGadget.Blaster;
                    Vector3 push = victimFeet - actor.Eye;
                    push.y = 0f;
                    push = (push.sqrMagnitude > 1e-4f ? push.normalized : Vector3.forward) * (blast ? tuning.blasterImpulse : tuning.knockImpulse)
                           + Vector3.up * (blast ? tuning.blasterLift : 1f);
                    float seconds = blast ? tuning.blasterKnockSeconds : tuning.knockSeconds;
                    knockProtectedUntil[request.Victim] = now + Mathf.Max(tuning.knockProtectionSeconds, seconds + 10f);
                    Victims.Knock(request.Victim, push, seconds, actor.Name, request.Gadget);
                    // Coup validé (batte, V0.4.2 : fusil aussi) : le joueur touché lâche l'objet acheté qu'il tient (sinon le
                    // dernier qu'il porte) ; il tombe au sol, une seule fois, décidé par l'autorité.
                    if (tuning.batItemsLost > 0) ItemLoss?.LoseItems(request.Victim, victimFeet, push, tuning.batItemsLost);
                    // Faux sang stylisé au sol, sous le joueur touché (tapis ou aire de lavage, comme toute salissure).
                    if (blast) outcome.Added = StainUnder(gadget, victimFeet);
                }
            }
            else
            {
                outcome.Added = Stain(gadget, point, targetRug);
                addedTo[targetRug] = AddedTo(targetRug) + outcome.Added;
            }
            Executed?.Invoke(outcome);
            return PrankResult.Done;
        }

        void Update() => Tick();

        /// <summary>V0.4.2 : applique les explosions dont la mèche est consumée (une seule fois chacune).</summary>
        public void Tick()
        {
            if (pendingBlasts.Count == 0) return;
            float now = Clock();
            for (int i = 0; i < pendingBlasts.Count; i++)
            {
                if (now < pendingBlasts[i].At) continue;
                var blast = pendingBlasts[i];
                pendingBlasts.RemoveAt(i--);
                Detonate(blast);
            }
        }

        /// <summary>
        /// Explosion : une seule détection des joueurs (distance de leurs pieds au point, dans le rayon), les joueurs déjà à
        /// terre ou protégés sont épargnés (pas de KO enchaîné), l'auteur aussi ; chaque joueur touché est mis KO 5 s et lâche
        /// l'objet qu'il tient ; les tapis posés dans le rayon reçoivent de la poussière selon leur distance réelle (dans la
        /// limite des farces par tapis).
        /// </summary>
        void Detonate(DynamiteBlast blast)
        {
            Detonations++;
            float now = Clock();
            float radius = tuning.dynamiteRadius;
            blast.Victims = new List<ulong>();
            if (Victims != null)
            {
                nearPlayers.Clear();
                Victims.CollectPlayers(nearPlayers);
                foreach (ulong id in nearPlayers)
                {
                    if (id == blast.Actor || !Victims.TryGetFeet(id, out Vector3 feet)) continue;
                    Vector3 body = feet + Vector3.up * (PrankRules.BodyHeight * 0.5f);
                    float distance = Vector3.Distance(body, blast.Point);
                    if (distance > radius) continue;
                    if ((knockProtectedUntil.TryGetValue(id, out float until) && now < until) || Victims.IsDown(id)) continue;
                    Vector3 away = feet - blast.Point;
                    away.y = 0f;
                    float strength = Mathf.Lerp(1f, 0.45f, distance / radius);
                    Vector3 push = (away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward) * tuning.dynamiteImpulse * strength
                                   + Vector3.up * tuning.dynamiteLift * strength;
                    knockProtectedUntil[id] = now + Mathf.Max(tuning.knockProtectionSeconds, tuning.dynamiteKnockSeconds + 10f);
                    Victims.Knock(id, push, tuning.dynamiteKnockSeconds, blast.ActorName, PrankGadget.Dynamite);
                    ItemLoss?.LoseItems(id, feet, push, 1);
                    blast.Victims.Add(id);
                }
            }
            foreach (var surface in rugs)
            {
                if (surface == null || !surface.IsLoaded || surface.IsFinished) continue;
                float remaining = PrankRules.CapRemaining(AddedTo(surface), surface.Grid.InitialDirt, tuning.rugCapFraction, tuning.rugCapMinimum);
                if (remaining <= 0f) continue;
                // Projeté sur le tapis : seules les cases à moins du rayon du point reçoivent de la poussière, décroissante.
                Vector3 onRug = blast.Point;
                onRug.y = surface.transform.position.y;
                float before = surface.Grid.TotalDirt;
                var dust = new DepositOperation { Type = DirtType.Dust, Debris = tuning.dynamiteDust };
                if (surface.ApplyOperation(onRug, radius, 1.4f, ref dust) <= 0) continue;
                float added = Mathf.Max(0f, surface.Grid.TotalDirt - before);
                if (added <= 0f) continue;
                addedTo[surface] = AddedTo(surface) + added;
                blast.DustAdded += added;
                blast.RugsDusted++;
            }
            Detonated?.Invoke(blast);
        }

        /// <summary>Taches sous un joueur touché : sur le tapis (dans la limite par tapis) ou sur l'aire de lavage.</summary>
        float StainUnder(PrankTuning.Gadget gadget, Vector3 feet)
        {
            var under = RugAt(feet);
            if (under != null && !under.IsFinished
                && PrankRules.CapRemaining(AddedTo(under), under.Grid.InitialDirt, tuning.rugCapFraction, tuning.rugCapMinimum) > 0f)
            {
                float added = Stain(gadget, feet, under);
                addedTo[under] = AddedTo(under) + added;
                return added;
            }
            if (Stainable(workSurface, feet)) Stain(gadget, feet, workSurface);
            else
                foreach (var area in extraWorkSurfaces)
                    if (Stainable(area, feet))
                    {
                        Stain(gadget, feet, area);
                        break;
                    }
            return 0f;
        }

        static bool Stainable(RugCleaningController area, Vector3 point) =>
            area != null && area.IsLoaded && area.AcceptsDirt && area.TryWorldToUV(point, out _);

        /// <summary>Dépose la ou les taches du gadget autour du point (repère du tapis) ; retourne la saleté ajoutée.</summary>
        float Stain(PrankTuning.Gadget gadget, Vector3 point, RugCleaningController surface)
        {
            var rug = surface;
            seed++;
            float added = 0f;
            Vector3 right = rug.transform.right, forward = rug.transform.forward;
            for (int i = 0; i < Mathf.Max(1, gadget.splats); i++)
            {
                Vector2 offset = PrankRules.SplatOffset(seed, i, gadget.splatSpread);
                Vector3 at = point + right * offset.x + forward * offset.y;
                if (!rug.TryWorldToUV(at, out _)) continue;
                // Éclaboussures : la première est la plus grosse, les autres de plus en plus petites.
                float radius = gadget.stainRadius * (i == 0 ? 1f : Mathf.Lerp(0.85f, 0.45f, i / (float)Mathf.Max(1, gadget.splats)));
                var operation = new PrankStainOperation
                {
                    Type = gadget.stainType, Amount = gadget.stainAmount, Moisture = gadget.stainMoisture, CellCap = tuning.cellStainCap,
                };
                // Faux sang : bords nets (taches de dessin animé) ; pipi : auréole plus douce.
                float hardness = gadget.stainType == DirtType.FakeBlood ? 5f : 2.5f;
                var geometry = new SurfaceOpGeometry(at, radius, hardness, 1f, Vector3.zero, default);
                rug.ApplyLocal(geometry, allowRunoff: false, ref operation);
                added += operation.Added;
            }
            return added;
        }
    }
}
