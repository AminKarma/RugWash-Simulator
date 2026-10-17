using System.Collections;
using System.Collections.Generic;
using RugWash.Cleaning;
using UnityEngine;
using UnityEngine.Rendering;

namespace RugWash.Effects
{
    /// <summary>
    /// Captures « avant / après » du tapis vu de dessus, rendues par une caméra orthographique dédiée
    /// (calque Rug uniquement). L'image « avant » est prise à chaque chargement de tapis, pour chaque zone de lavage
    /// (grande mise à jour) ; un tapis parti en machine à laver garde son image « avant » jusqu'à son retour.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RugSnapshotService : MonoBehaviour
    {
        [SerializeField] RugCleaningController rug;
        [Tooltip("Tapis des autres zones de lavage (agrandissement de l'atelier).")]
        [SerializeField] List<RugCleaningController> extraRugs = new List<RugCleaningController>();
        [SerializeField] Camera snapshotCamera;
        [SerializeField, Range(128, 2048)] int textureWidth = 768;

        sealed class Entry
        {
            public RugCleaningController Rug;
            public RenderTexture Before;
            public bool HasBefore;
            /// <summary>Retour de la machine à laver : l'image « avant » d'origine est conservée.</summary>
            public bool KeepBefore;
            public System.Action<RugCleaningController> OnLoaded;
        }

        readonly List<Entry> entries = new List<Entry>();
        readonly Dictionary<string, RenderTexture> parked = new Dictionary<string, RenderTexture>();
        RenderTexture after;
        Entry current;

        /// <summary>Image « avant » du dernier tapis capturé « après » (zone 1 par défaut).</summary>
        public RenderTexture Before => Current != null ? Current.Before : null;
        public RenderTexture After => after;
        public bool HasBefore => Current != null && Current.HasBefore;

        Entry Current => current ?? (entries.Count > 0 ? entries[0] : null);

        public bool IsConfigured(out string missing)
        {
            missing = rug == null ? nameof(rug) : snapshotCamera == null ? nameof(snapshotCamera) : extraRugs.Contains(null) ? nameof(extraRugs) : null;
            return missing == null;
        }

        void Awake()
        {
            if (!IsConfigured(out string missing))
            {
                Debug.LogError($"[RugWash] RugSnapshotService : référence manquante '{missing}'.", this);
                enabled = false;
                return;
            }
            snapshotCamera.enabled = false;
            Register(rug);
            foreach (var extra in extraRugs) Register(extra);
        }

        void Register(RugCleaningController surface)
        {
            if (surface == null || Find(surface) != null) return;
            var entry = new Entry { Rug = surface };
            entry.OnLoaded = _ => OnRugLoaded(entry);
            surface.RugLoaded += entry.OnLoaded;
            entries.Add(entry);
        }

        void Start()
        {
            foreach (var entry in entries)
                if (entry.Rug.Grid != null) OnRugLoaded(entry);
        }

        void OnDestroy()
        {
            foreach (var entry in entries)
            {
                if (entry.Rug != null) entry.Rug.RugLoaded -= entry.OnLoaded;
                Release(ref entry.Before);
            }
            foreach (var texture in parked.Values)
            {
                var parkedTexture = texture;
                Release(ref parkedTexture);
            }
            parked.Clear();
            Release(ref after);
        }

        Entry Find(RugCleaningController surface)
        {
            foreach (var entry in entries)
                if (entry.Rug == surface) return entry;
            return null;
        }

        void OnRugLoaded(Entry entry)
        {
            if (entry.KeepBefore)
            {
                entry.KeepBefore = false;
                return;
            }
            entry.HasBefore = false;
            if (isActiveAndEnabled) StartCoroutine(CaptureBeforeNextFrame(entry));
        }

        IEnumerator CaptureBeforeNextFrame(Entry entry)
        {
            yield return null;
            entry.HasBefore = Capture(entry.Rug, ref entry.Before);
        }

        /// <summary>Image « avant » du tapis de cette zone (null si aucune).</summary>
        public RenderTexture BeforeOf(RugCleaningController surface) => Find(surface)?.Before;

        /// <summary>Capture l'état actuel du tapis de la zone 1 dans la texture « après ».</summary>
        public bool CaptureAfter() => CaptureAfter(rug);

        /// <summary>Capture l'état actuel de ce tapis dans la texture « après » ; <see cref="Before"/> désigne alors le sien.</summary>
        public bool CaptureAfter(RugCleaningController surface)
        {
            var entry = Find(surface) ?? Current;
            if (entry == null) return false;
            current = entry;
            return Capture(entry.Rug, ref after);
        }

        /// <summary>Le tapis part en machine : son image « avant » est mise de côté pour sa commande.</summary>
        public void Park(RugCleaningController surface, string orderId)
        {
            var entry = Find(surface);
            if (entry == null || string.IsNullOrEmpty(orderId)) return;
            Forget(orderId);
            if (entry.HasBefore && entry.Before != null) parked[orderId] = entry.Before;
            else Release(ref entry.Before);
            entry.Before = null;
            entry.HasBefore = false;
        }

        /// <summary>Le tapis revient de la machine : son image « avant » d'origine est reprise (pas de nouvelle capture).</summary>
        public void Unpark(RugCleaningController surface, string orderId)
        {
            var entry = Find(surface);
            if (entry == null || string.IsNullOrEmpty(orderId) || !parked.TryGetValue(orderId, out var texture)) return;
            parked.Remove(orderId);
            Release(ref entry.Before);
            entry.Before = texture;
            entry.HasBefore = true;
            entry.KeepBefore = true;
        }

        /// <summary>Commande abandonnée en machine : son image « avant » est libérée.</summary>
        public void Forget(string orderId)
        {
            if (string.IsNullOrEmpty(orderId) || !parked.TryGetValue(orderId, out var texture)) return;
            parked.Remove(orderId);
            Release(ref texture);
        }

        bool Capture(RugCleaningController surface, ref RenderTexture target)
        {
            if (surface == null || surface.Definition == null) return false;
            surface.DirtRenderer.UploadNow();

            Vector2 size = surface.Definition.sizeMeters;
            int width = textureWidth;
            int height = Mathf.Max(16, Mathf.RoundToInt(width * size.y / size.x));
            if (target == null || target.width != width || target.height != height)
            {
                Release(ref target);
                target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
                {
                    name = "RugSnapshot",
                    antiAliasing = 2,
                    hideFlags = HideFlags.DontSave,
                };
                target.Create();
            }

            Transform cam = snapshotCamera.transform;
            cam.SetPositionAndRotation(surface.transform.TransformPoint(new Vector3(0f, 2.5f, 0f)),
                surface.transform.rotation * Quaternion.Euler(90f, 0f, 0f));
            snapshotCamera.orthographic = true;
            snapshotCamera.orthographicSize = size.y * 0.5f * 1.04f;
            snapshotCamera.aspect = width / (float)height;

            var request = new RenderPipeline.StandardRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(snapshotCamera, request))
            {
                Debug.LogWarning("[RugWash] Capture avant/après non supportée par le pipeline actif.", this);
                return false;
            }
            RenderPipeline.SubmitRenderRequest(snapshotCamera, request);
            return true;
        }

        static void Release(ref RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            Core.UnityObjectUtility.SafeDestroy(texture);
            texture = null;
        }
    }
}
