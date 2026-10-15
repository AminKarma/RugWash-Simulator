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
        [Tooltip("Tapis de la commande (seule surface où les farces déposent une tache).")]
        [SerializeField] RugCleaningController rug;

        readonly Dictionary<ulong, float[]> readyAt = new Dictionary<ulong, float[]>();
        readonly Dictionary<ulong, float> globalReadyAt = new Dictionary<ulong, float>();
        readonly Dictionary<ulong, float> splashProtectedUntil = new Dictionary<ulong, float>();
        readonly Dictionary<ulong, float> knockProtectedUntil = new Dictionary<ulong, float>();
        int seed;

        public PrankTuning Tuning => tuning;
        public RugCleaningController Rug => rug;
        /// <summary>Joueurs visables (partie en ligne, hôte) ; null en solo.</summary>
        public IPrankVictims Victims { get; set; }
        /// <summary>Saleté ajoutée par les farces au tapis actuel (remise à zéro à chaque tapis).</summary>
        public float AddedToRug { get; private set; }
        /// <summary>Horloge des recharges (secondes) ; remplaçable par les tests.</summary>
        public Func<float> Clock { get; set; } = () => Time.time;
        public event Action<PrankOutcome> Executed;

        public bool IsConfigured(out string missing)
        {
            missing = tuning == null ? nameof(tuning) : rug == null ? nameof(rug) : null;
            return missing == null;
        }

        void OnEnable()
        {
            if (rug != null) rug.RugLoaded += OnRugLoaded;
        }

        void OnDisable()
        {
            if (rug != null) rug.RugLoaded -= OnRugLoaded;
        }

        void OnRugLoaded(RugCleaningController _) => AddedToRug = 0f;

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
            if (!readyAt.TryGetValue(actor.Id, out var times)) readyAt[actor.Id] = times = new float[4];
            globalReadyAt.TryGetValue(actor.Id, out float global);
            var check = PrankRules.CheckUse(actor.Owns(request.Gadget), now, times[(int)request.Gadget], global, actor.Money, gadget.costPerUse);
            if (check != PrankResult.Done) return check;

            Vector3 point;
            Vector3 victimFeet = default;
            if (request.Target == PrankTarget.Player)
            {
                if (!gadget.affectsPlayers) return PrankResult.InvalidTarget;
                if (Victims == null || request.Victim == actor.Id || !Victims.TryGetFeet(request.Victim, out victimFeet)) return PrankResult.NoTarget;
                point = victimFeet + Vector3.up * PrankRules.BodyHeight;
                if (!PrankRules.InRange(actor.Eye, point, gadget.range + 0.5f, tuning.rangeTolerance)) return PrankResult.OutOfRange;
                var protection = request.Gadget == PrankGadget.Pee ? splashProtectedUntil : knockProtectedUntil;
                if ((protection.TryGetValue(request.Victim, out float until) && now < until)
                    || (request.Gadget == PrankGadget.Bat && Victims.IsDown(request.Victim)))
                    return PrankResult.TargetProtected;
            }
            else
            {
                if (!gadget.affectsRug) return PrankResult.InvalidTarget;
                if (rug == null || !rug.IsLoaded || !rug.TryWorldToUV(request.Point, out _)) return PrankResult.NoTarget;
                if (rug.IsFinished) return PrankResult.RugFinished;
                point = request.Point;
                if (!PrankRules.InRange(actor.Eye, point, gadget.range, tuning.rangeTolerance)) return PrankResult.OutOfRange;
                if (PrankRules.CapRemaining(AddedToRug, rug.Grid.InitialDirt, tuning.rugCapFraction, tuning.rugCapMinimum) <= 0f)
                    return PrankResult.CapReached;
            }

            if (gadget.costPerUse > 0 && !actor.TrySpend(gadget.costPerUse)) return PrankResult.InsufficientFunds;
            times[(int)request.Gadget] = now + gadget.cooldownSeconds;
            globalReadyAt[actor.Id] = now + tuning.globalCooldownSeconds;

            outcome = new PrankOutcome { Gadget = request.Gadget, Actor = actor.Id, Origin = request.Origin, Point = point };
            if (request.Target == PrankTarget.Player)
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
                    Vector3 push = victimFeet - actor.Eye;
                    push.y = 0f;
                    push = (push.sqrMagnitude > 1e-4f ? push.normalized : Vector3.forward) * tuning.knockImpulse + Vector3.up;
                    knockProtectedUntil[request.Victim] = now + tuning.knockProtectionSeconds;
                    Victims.Knock(request.Victim, push, tuning.knockSeconds, actor.Name);
                }
            }
            else
            {
                outcome.Added = Stain(gadget, point);
                AddedToRug += outcome.Added;
            }
            Executed?.Invoke(outcome);
            return PrankResult.Done;
        }

        /// <summary>Dépose la ou les taches du gadget autour du point (repère du tapis) ; retourne la saleté ajoutée.</summary>
        float Stain(PrankTuning.Gadget gadget, Vector3 point)
        {
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
