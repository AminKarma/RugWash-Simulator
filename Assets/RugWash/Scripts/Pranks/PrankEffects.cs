using System.Collections.Generic;
using RugWash.Cleaning;
using UnityEngine;

namespace RugWash.Pranks
{
    /// <summary>
    /// Effets des farces dans le monde, reproduits localement sur chaque poste (aucune particule ne passe par le réseau) :
    /// jet de pipi en arc (gouttes jaunes, dessin animé), éclaboussures jaunes ou de faux sang stylisé, bouffée du fusil
    /// de farces et attrapes, sons (jet, élan de la batte, « bonk », tir). Les gouttes sont émises dans des systèmes créés à
    /// la génération de la scène : aucun objet n'est instancié en jeu.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PrankEffects : MonoBehaviour
    {
        [SerializeField] ParticleSystem droplets;
        [SerializeField] ParticleSystem splashYellow;
        [SerializeField] ParticleSystem splashRed;
        [SerializeField] ParticleSystem puff;
        [Tooltip("Sources 3D réutilisées à tour de rôle.")]
        [SerializeField] List<AudioSource> voices = new List<AudioSource>();
        [SerializeField] AudioClip streamClip;
        [SerializeField] AudioClip swingClip;
        [SerializeField] AudioClip bonkClip;
        [SerializeField] AudioClip shotClip;
        [SerializeField] AudioClip splatClip;
        [Tooltip("Durée de vol d'une goutte du jet jusqu'à sa cible (s).")]
        [SerializeField, Range(0.2f, 1f)] float flightSeconds = 0.42f;
        [SerializeField, Min(1f)] float streamRate = 70f;

        struct Stream
        {
            public Vector3 From, To;
            public float Start, Until, Carry;
            public bool Splashed;
        }

        readonly List<Stream> streams = new List<Stream>();
        int nextVoice;

        /// <summary>Jets de pipi en cours (tests, diagnostic).</summary>
        public int ActiveStreams => streams.Count;
        /// <summary>Dernier effet joué (tests).</summary>
        public PrankGadget LastPlayed { get; private set; }

        public bool IsConfigured(out string missing)
        {
            missing = droplets == null ? nameof(droplets) : splashYellow == null ? nameof(splashYellow) : splashRed == null ? nameof(splashRed)
                : puff == null ? nameof(puff) : voices.Count == 0 ? nameof(voices) : null;
            return missing == null;
        }

        /// <summary>
        /// Joue une farce : <paramref name="origin"/> = d'où elle part (hanches pour le jet, main pour la batte et le
        /// fusil), <paramref name="point"/> = ce qu'elle atteint.
        /// </summary>
        public void Play(PrankGadget gadget, Vector3 origin, Vector3 point, bool hitPlayer, float useSeconds)
        {
            LastPlayed = gadget;
            switch (gadget)
            {
                case PrankGadget.Pee:
                    streams.Add(new Stream { From = origin, To = point, Start = Time.time, Until = Time.time + useSeconds });
                    Voice(origin, streamClip, 0.55f);
                    break;
                case PrankGadget.Bat:
                    Voice(origin, swingClip, 0.6f);
                    if (hitPlayer) Voice(point, bonkClip, 0.8f);
                    else Burst(splashRed, point, 10, 1.2f);
                    break;
                case PrankGadget.Blaster:
                    Voice(origin, shotClip, 0.75f);
                    // Petite bouffée vers l'avant, dans l'axe du tir (jamais vers la caméra).
                    Burst(puff, origin, 8, 1.2f, (point - origin).normalized);
                    Burst(splashRed, point, 24, 1.8f);
                    Voice(point, splatClip, 0.45f);
                    break;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || streams.Count == 0) return;
            float now = Time.time;
            for (int i = streams.Count - 1; i >= 0; i--)
            {
                var stream = streams[i];
                if (now >= stream.Until)
                {
                    streams.RemoveAt(i);
                    continue;
                }
                // Gouttes lancées en arc (gravité du système = 1) pour retomber sur le point visé.
                stream.Carry += EffectBudget.Rate(streamRate) * dt;
                int count = Mathf.FloorToInt(stream.Carry);
                stream.Carry -= count;
                if (count > 0 && !droplets.isPlaying) droplets.Play();
                Vector3 delta = stream.To - stream.From;
                float t = flightSeconds;
                var velocity = new Vector3(delta.x / t, (delta.y + 0.5f * 9.81f * t * t) / t, delta.z / t);
                for (int k = 0; k < count; k++)
                {
                    var emit = new ParticleSystem.EmitParams
                    {
                        position = stream.From + Random.insideUnitSphere * 0.01f,
                        velocity = velocity + Random.insideUnitSphere * 0.12f,
                        applyShapeToPosition = false,
                    };
                    droplets.Emit(emit, 1);
                }
                // Les premières gouttes arrivent : éclaboussure à l'impact.
                if (!stream.Splashed && now - stream.Start >= t)
                {
                    stream.Splashed = true;
                    Burst(splashYellow, stream.To, 8, 1f);
                }
                streams[i] = stream;
            }
        }

        static void Burst(ParticleSystem system, Vector3 at, int count, float speed, Vector3 direction = default)
        {
            if (system == null) return;
            int n = Mathf.Max(1, Mathf.RoundToInt(EffectBudget.Rate(count)));
            // Systèmes sans émission propre (débit nul) : ils ne produisent que ce qui est émis ici.
            if (!system.isPlaying) system.Play();
            for (int i = 0; i < n; i++)
            {
                Vector3 spray = Random.onUnitSphere;
                if (direction == Vector3.zero) spray.y = Mathf.Abs(spray.y) * 0.8f + 0.2f;
                else spray = (direction * 2f + spray * 0.5f).normalized;
                system.Emit(new ParticleSystem.EmitParams
                {
                    position = at + Vector3.up * 0.02f,
                    velocity = spray * speed * Random.Range(0.4f, 1f),
                    applyShapeToPosition = false,
                }, 1);
            }
        }

        void Voice(Vector3 at, AudioClip clip, float volume)
        {
            if (clip == null || voices.Count == 0) return;
            var voice = voices[nextVoice];
            nextVoice = (nextVoice + 1) % voices.Count;
            if (voice == null) return;
            voice.transform.position = at;
            voice.Stop();
            voice.clip = clip;
            voice.volume = volume;
            voice.Play();
        }
    }
}
