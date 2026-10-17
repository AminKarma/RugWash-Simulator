using System;
using System.Collections;
using System.Collections.Generic;
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
    /// et pluie d'éclats sur le tapis quand l'objectif de la commande est atteint. Grande mise à jour : chaque zone de
    /// lavage a ses paliers (deux tapis en même temps).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CleaningProgressFeedback : MonoBehaviour
    {
        [SerializeField] RugCleaningController rug;
        [Tooltip("Tapis des autres zones de lavage (agrandissement de l'atelier).")]
        [SerializeField] List<RugCleaningController> extraRugs = new List<RugCleaningController>();
        [SerializeField] OrderManager orders;
        [SerializeField] AudioManager audioManager;
        [SerializeField] AudioClip milestoneClip;
        [SerializeField] CleaningVFXPool celebrationPool;
        [SerializeField, Range(1, 16)] int celebrationBursts = 8;
        [SerializeField] float celebrationInterval = 0.14f;

        sealed class Tracked
        {
            public RugCleaningController Rug;
            public readonly CleanlinessMilestones Milestones = new CleanlinessMilestones();
            public Action<RugCleaningController> OnLoaded, OnFinished;
        }

        readonly List<Tracked> tracked = new List<Tracked>();

        /// <summary>Palier franchi (0..1).</summary>
        public event Action<float> MilestoneReached;

        public int CelebrationsPlayed { get; private set; }

        public bool IsConfigured(out string missing)
        {
            missing = rug == null ? nameof(rug) : orders == null ? nameof(orders) : extraRugs.Contains(null) ? nameof(extraRugs) : null;
            return missing == null;
        }

        void Awake()
        {
            Track(rug);
            foreach (var extra in extraRugs) Track(extra);
        }

        void Track(RugCleaningController surface)
        {
            if (surface == null) return;
            foreach (var entry in tracked)
                if (entry.Rug == surface) return;
            var item = new Tracked { Rug = surface };
            item.OnLoaded = loaded => item.Milestones.Reset(loaded.Cleanliness01);
            item.OnFinished = finished => OnRugFinished(item);
            tracked.Add(item);
        }

        void OnEnable()
        {
            foreach (var item in tracked)
            {
                item.Rug.RugLoaded += item.OnLoaded;
                item.Rug.Finished += item.OnFinished;
                if (item.Rug.Grid != null) item.Milestones.Reset(item.Rug.Cleanliness01);
            }
            if (orders != null) orders.ObjectiveReached += OnObjectiveReached;
        }

        void OnDisable()
        {
            foreach (var item in tracked)
            {
                item.Rug.RugLoaded -= item.OnLoaded;
                item.Rug.Finished -= item.OnFinished;
            }
            if (orders != null) orders.ObjectiveReached -= OnObjectiveReached;
        }

        /// <summary>Tapis terminé (moins de 1 % de saleté restante) : son de réussite et pluie d'éclats.</summary>
        public event Action RugFinished;

        void OnRugFinished(Tracked item)
        {
            if (audioManager != null) audioManager.PlayInterface(audioManager.ConfirmClip);
            item.Milestones.Reset(1f);
            if (celebrationPool != null && isActiveAndEnabled) StartCoroutine(Celebrate(item.Rug));
            RugFinished?.Invoke();
        }

        void Update()
        {
            foreach (var item in tracked)
            {
                if (item.Rug.Grid == null) continue;
                if (!item.Milestones.TryAdvance(item.Rug.Cleanliness01, out float reached)) continue;
                if (audioManager != null) audioManager.PlayInterface(milestoneClip, 0.8f);
                MilestoneReached?.Invoke(reached);
            }
        }

        void OnObjectiveReached(OrderDefinition order)
        {
            var target = orders != null ? orders.RugOf(orders.BayOf(order)) : null;
            if (celebrationPool != null && isActiveAndEnabled) StartCoroutine(Celebrate(target != null ? target : rug));
        }

        IEnumerator Celebrate(RugCleaningController surface)
        {
            CelebrationsPlayed++;
            var random = new System.Random(CelebrationsPlayed);
            for (int i = 0; i < celebrationBursts; i++)
            {
                if (surface == null || surface.Definition == null) yield break;
                var uv = new Vector2(0.12f + 0.76f * (float)random.NextDouble(), 0.12f + 0.76f * (float)random.NextDouble());
                celebrationPool.Spawn(surface.UVToWorld(uv) + surface.transform.up * 0.02f, surface.transform.up);
                yield return new WaitForSeconds(celebrationInterval);
            }
        }
    }
}
