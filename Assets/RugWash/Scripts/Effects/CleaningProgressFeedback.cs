using System;
using System.Collections;
using RugWash.Audio;
using RugWash.Cleaning;
using RugWash.Orders;
using UnityEngine;

namespace RugWash.Effects
{
    /// <summary>Paliers de propreté franchis (règle pure, testable). Un palier déjà dépassé au chargement n'est pas annoncé.</summary>
    public sealed class CleanlinessMilestones
    {
        public static readonly float[] DefaultThresholds = { 0.25f, 0.5f, 0.75f, 0.9f };

        readonly float[] thresholds;
        int next;

        public CleanlinessMilestones(float[] thresholds = null)
        {
            this.thresholds = (float[])(thresholds ?? DefaultThresholds).Clone();
            Array.Sort(this.thresholds);
        }

        public int Count => thresholds.Length;
        public int ReachedCount => next;

        /// <summary>Repart de l'état actuel du tapis (les paliers déjà atteints sont considérés comme annoncés).</summary>
        public void Reset(float cleanliness01)
        {
            next = 0;
            while (next < thresholds.Length && cleanliness01 >= thresholds[next]) next++;
        }

        /// <summary>Vrai si un ou plusieurs paliers viennent d'être franchis ; <paramref name="reached"/> = le plus haut.</summary>
        public bool TryAdvance(float cleanliness01, out float reached)
        {
            reached = 0f;
            bool advanced = false;
            while (next < thresholds.Length && cleanliness01 >= thresholds[next])
            {
                reached = thresholds[next++];
                advanced = true;
            }
            return advanced;
        }
    }

    /// <summary>
    /// Retours de progression du nettoyage : son et événement à chaque palier de propreté (25, 50, 75, 90 %),
    /// et pluie d'éclats sur le tapis quand l'objectif de la commande est atteint.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CleaningProgressFeedback : MonoBehaviour
    {
        [SerializeField] RugCleaningController rug;
        [SerializeField] OrderManager orders;
        [SerializeField] AudioManager audioManager;
        [SerializeField] AudioClip milestoneClip;
        [SerializeField] CleaningVFXPool celebrationPool;
        [SerializeField, Range(1, 16)] int celebrationBursts = 8;
        [SerializeField] float celebrationInterval = 0.14f;

        readonly CleanlinessMilestones milestones = new CleanlinessMilestones();

        /// <summary>Palier franchi (0..1).</summary>
        public event Action<float> MilestoneReached;

        public int CelebrationsPlayed { get; private set; }

        public bool IsConfigured(out string missing)
        {
            missing = rug == null ? nameof(rug) : orders == null ? nameof(orders) : null;
            return missing == null;
        }

        void OnEnable()
        {
            if (rug != null)
            {
                rug.RugLoaded += OnRugLoaded;
                rug.Finished += OnRugFinished;
            }
            if (orders != null) orders.ObjectiveReached += OnObjectiveReached;
            if (rug != null && rug.Grid != null) milestones.Reset(rug.Cleanliness01);
        }

        void OnDisable()
        {
            if (rug != null)
            {
                rug.RugLoaded -= OnRugLoaded;
                rug.Finished -= OnRugFinished;
            }
            if (orders != null) orders.ObjectiveReached -= OnObjectiveReached;
        }

        /// <summary>Tapis terminé (moins de 1 % de saleté restante) : son de réussite et pluie d'éclats.</summary>
        public event Action RugFinished;

        void OnRugFinished(RugCleaningController finished)
        {
            if (audioManager != null) audioManager.PlayInterface(audioManager.ConfirmClip);
            milestones.Reset(1f);
            if (celebrationPool != null && isActiveAndEnabled) StartCoroutine(Celebrate());
            RugFinished?.Invoke();
        }

        void OnRugLoaded(RugCleaningController loaded) => milestones.Reset(loaded.Cleanliness01);

        void Update()
        {
            if (rug == null || rug.Grid == null) return;
            if (!milestones.TryAdvance(rug.Cleanliness01, out float reached)) return;
            if (audioManager != null) audioManager.PlayInterface(milestoneClip, 0.8f);
            MilestoneReached?.Invoke(reached);
        }

        void OnObjectiveReached(OrderDefinition order)
        {
            if (celebrationPool != null && isActiveAndEnabled) StartCoroutine(Celebrate());
        }

        IEnumerator Celebrate()
        {
            CelebrationsPlayed++;
            var random = new System.Random(CelebrationsPlayed);
            for (int i = 0; i < celebrationBursts; i++)
            {
                var uv = new Vector2(0.12f + 0.76f * (float)random.NextDouble(), 0.12f + 0.76f * (float)random.NextDouble());
                celebrationPool.Spawn(rug.UVToWorld(uv) + rug.transform.up * 0.02f, rug.transform.up);
                yield return new WaitForSeconds(celebrationInterval);
            }
        }
    }
}
