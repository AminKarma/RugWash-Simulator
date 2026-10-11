using UnityEngine;

namespace RugWash.Diagnostics
{
    /// <summary>Mesure des temps de frame sur une fenêtre glissante (sans allocation).</summary>
    public sealed class FrameTimeSampler
    {
        readonly float[] samples;
        int count, next;

        public FrameTimeSampler(int capacity = 120) => samples = new float[Mathf.Max(1, capacity)];

        public void Add(float frameSeconds)
        {
            samples[next] = frameSeconds;
            next = (next + 1) % samples.Length;
            if (count < samples.Length) count++;
        }

        public int Count => count;

        public float AverageSeconds
        {
            get
            {
                if (count == 0) return 0f;
                float sum = 0f;
                for (int i = 0; i < count; i++) sum += samples[i];
                return sum / count;
            }
        }

        public float WorstSeconds
        {
            get
            {
                float worst = 0f;
                for (int i = 0; i < count; i++) worst = Mathf.Max(worst, samples[i]);
                return worst;
            }
        }

        public float AverageFps => AverageSeconds > 0f ? 1f / AverageSeconds : 0f;
    }

    /// <summary>Échantillonne le temps de frame réel (non affecté par la pause).</summary>
    [DisallowMultipleComponent]
    public sealed class PerformanceMonitor : MonoBehaviour
    {
        public FrameTimeSampler Sampler { get; } = new FrameTimeSampler(120);

        void Update() => Sampler.Add(Time.unscaledDeltaTime);
    }
}
