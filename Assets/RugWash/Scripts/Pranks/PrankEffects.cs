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
        [Header("Dynamite (V0.4.2)")]
        [Tooltip("Bâtons lancés (réserve réutilisée, cachés au repos).")]
        [SerializeField] List<Transform> thrownSticks = new List<Transform>();
        [SerializeField] ParticleSystem fuseSparks;
        [SerializeField] ParticleSystem fireball;
        [SerializeField] ParticleSystem smoke;
        [SerializeField] ParticleSystem debris;
        [SerializeField] ParticleSystem dustRing;
        [Tooltip("Lumière du flash (brève, maîtrisée).")]
        [SerializeField] Light flash;
        [SerializeField] AudioClip fuseClip;
        [SerializeField] AudioClip boomClip;
        [SerializeField, Range(0.05f, 0.4f)] float flashSeconds = 0.16f;
        [SerializeField, Range(1f, 20f)] float flashIntensity = 9f;
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

        struct Throw
        {
            public Transform Stick;
            public Vector3 From, To;
            public float Start, Land, Boom;
        }

        readonly List<Throw> throws = new List<Throw>();
        float flashStart = -10f;
        /// <summary>V0.4.2 : une explosion de dynamite est jouée ici (point) : secousse et ralenti du joueur local.</summary>
        public event System.Action<Vector3> Exploded;
        /// <summary>Explosions jouées sur ce poste (tests).</summary>
        public int ExplosionsPlayed { get; private set; }
        public int ActiveThrows => throws.Count;
        public bool FlashOn => flash != null && flash.enabled;
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
                    // Le sifflement est joué au départ du mouvement (PlaySwing) ; ici, l'impact seulement.
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
                case PrankGadget.Dynamite:
                    // Vol en cloche jusqu'au point de chute, mèche qui crépite, puis l'explosion au délai commun (useSeconds).
                    var stick = FreeStick();
                    float flight = Mathf.Min(useSeconds * 0.6f, 0.7f);
                    throws.Add(new Throw { Stick = stick, From = origin, To = point, Start = Time.time, Land = Time.time + flight, Boom = Time.time + useSeconds });
                    if (stick != null) stick.gameObject.SetActive(true);
                    Voice(origin, fuseClip, 0.5f);
                    break;
            }
        }

        Transform FreeStick()
        {
            foreach (var stick in thrownSticks)
                if (stick != null && !stick.gameObject.activeSelf) return stick;
            return null;
        }

        /// <summary>
        /// V0.4.2 : l'explosion, dans la direction artistique du jeu (cartoon) : boule de feu orange, fumée grise qui monte,
        /// éclats, anneau de poussière au sol, flash bref, gros « boum ». Effets locaux, aucune particule par le réseau.
        /// </summary>
        public void Explode(Vector3 point)
        {
            ExplosionsPlayed++;
            Burst(fireball, point + Vector3.up * 0.35f, 26, 3.2f);
            Burst(smoke, point + Vector3.up * 0.5f, 18, 1.1f);
            Burst(debris, point + Vector3.up * 0.2f, 30, 6f);
            Ring(dustRing, point, 36, 4.5f);
            if (flash != null)
            {
                flash.transform.position = point + Vector3.up * 0.8f;
                flash.enabled = true;
                flashStart = Time.unscaledTime;
            }
            Voice(point, boomClip, 1f);
            Exploded?.Invoke(point);
        }

        static void Ring(ParticleSystem system, Vector3 at, int count, float speed)
        {
            if (system == null) return;
            int n = Mathf.Max(1, Mathf.RoundToInt(EffectBudget.Rate(count)));
            if (!system.isPlaying) system.Play();
            for (int i = 0; i < n; i++)
            {
                float angle = i * Mathf.PI * 2f / n;
                var direction = new Vector3(Mathf.Cos(angle), 0.08f, Mathf.Sin(angle));
                system.Emit(new ParticleSystem.EmitParams { position = at + Vector3.up * 0.05f, velocity = direction * speed * Random.Range(0.7f, 1f), applyShapeToPosition = false }, 1);
            }
        }

        void UpdateThrows()
        {
            float now = Time.time;
            for (int i = throws.Count - 1; i >= 0; i--)
            {
                var item = throws[i];
                if (item.Stick != null)
                {
                    float t = Mathf.Clamp01((now - item.Start) / Mathf.Max(0.05f, item.Land - item.Start));
                    float height = Mathf.Min(1.6f, 0.25f * Vector3.Distance(item.From, item.To) + 0.4f);
                    var position = Vector3.Lerp(item.From, item.To, t) + Vector3.up * (4f * height * t * (1f - t));
                    item.Stick.SetPositionAndRotation(position, Quaternion.Euler(t * 540f, 30f, t * 220f));
                    if (fuseSparks != null)
                    {
                        if (!fuseSparks.isPlaying) fuseSparks.Play();
                        fuseSparks.Emit(new ParticleSystem.EmitParams { position = position + Vector3.up * 0.12f, velocity = Random.onUnitSphere * 0.8f + Vector3.up, applyShapeToPosition = false }, 1);
                    }
                }
                if (now < item.Boom) continue;
                if (item.Stick != null) item.Stick.gameObject.SetActive(false);
                throws.RemoveAt(i);
                Explode(item.To);
            }
        }

        void UpdateFlash()
        {
            if (flash == null || !flash.enabled) return;
            float t = (Time.unscaledTime - flashStart) / flashSeconds;
            if (t >= 1f)
            {
                flash.enabled = false;
                return;
            }
            flash.intensity = flashIntensity * (1f - t) * (1f - t);
        }

        void Update()
        {
            UpdateFlash();
            if (throws.Count > 0) UpdateThrows();
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

        /// <summary>Batte : sifflement du mouvement, joué à chaque coup — aussi dans le vide.</summary>
        public void PlaySwing(Vector3 origin)
        {
            SwingsPlayed++;
            Voice(origin, swingClip, 0.6f);
        }

        /// <summary>Coups de batte joués (tests, diagnostic).</summary>
        public int SwingsPlayed { get; private set; }

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
