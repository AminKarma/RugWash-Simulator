using RugWash.Audio;
using UnityEngine;

namespace RugWash.Effects
{
    /// <summary>
    /// Pool d'effets ponctuels (ex. éclat lorsqu'une zone devient propre). Les instances sont créées
    /// une seule fois ; une demande est ignorée si toutes sont occupées ou si la cadence minimale
    /// n'est pas respectée : le nombre d'effets simultanés est toujours borné.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CleaningVFXPool : MonoBehaviour
    {
        [SerializeField] ParticleSystem template;
        [SerializeField, Range(1, 32)] int size = 8;
        [SerializeField] float minInterval = 0.12f;
        [SerializeField] AudioManager audioManager;
        [SerializeField] AudioClip spawnClip;
        [SerializeField, Range(0f, 1f)] float clipVolume = 0.5f;

        ParticleSystem[] instances;
        float lastSpawn = float.NegativeInfinity;

        public int Size => instances?.Length ?? 0;

        public int ActiveCount
        {
            get
            {
                if (instances == null) return 0;
                int count = 0;
                foreach (var instance in instances)
                    if (instance.IsAlive(true)) count++;
                return count;
            }
        }

        void Awake()
        {
            if (template == null)
            {
                Debug.LogError("[RugWash] CleaningVFXPool : 'template' manquant.", this);
                enabled = false;
                return;
            }
            template.gameObject.SetActive(false);
            instances = new ParticleSystem[size];
            for (int i = 0; i < size; i++)
            {
                var instance = Instantiate(template, transform);
                instance.name = $"{template.name}_{i}";
                instance.gameObject.SetActive(true);
                instance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                instances[i] = instance;
            }
        }

        /// <summary>Joue un effet au point donné. Retourne faux si refusé (cadence ou pool saturé).</summary>
        public bool Spawn(Vector3 position, Vector3 normal)
        {
            if (!enabled || instances == null || Time.time - lastSpawn < minInterval) return false;
            foreach (var instance in instances)
            {
                if (instance.IsAlive(true)) continue;
                instance.transform.SetPositionAndRotation(position, Quaternion.LookRotation(normal));
                instance.Play(true);
                lastSpawn = Time.time;
                if (audioManager != null) audioManager.PlayAt(spawnClip, position, clipVolume);
                return true;
            }
            return false;
        }
    }
}
