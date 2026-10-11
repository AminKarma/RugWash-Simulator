using System.Collections;
using RugWash.Cleaning;
using UnityEngine;
using UnityEngine.Rendering;

namespace RugWash.Effects
{
    /// <summary>
    /// Captures « avant / après » du tapis vu de dessus, rendues par une caméra orthographique dédiée
    /// (calque Rug uniquement). L'image « avant » est prise à chaque chargement de tapis.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RugSnapshotService : MonoBehaviour
    {
        [SerializeField] RugCleaningController rug;
        [SerializeField] Camera snapshotCamera;
        [SerializeField, Range(128, 2048)] int textureWidth = 768;

        RenderTexture before, after;

        public RenderTexture Before => before;
        public RenderTexture After => after;
        public bool HasBefore { get; private set; }

        public bool IsConfigured(out string missing)
        {
            missing = rug == null ? nameof(rug) : snapshotCamera == null ? nameof(snapshotCamera) : null;
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
            rug.RugLoaded += OnRugLoaded;
        }

        void Start()
        {
            if (rug.Grid != null) OnRugLoaded(rug);
        }

        void OnDestroy()
        {
            if (rug != null) rug.RugLoaded -= OnRugLoaded;
            Release(ref before);
            Release(ref after);
        }

        void OnRugLoaded(RugCleaningController loaded)
        {
            HasBefore = false;
            if (isActiveAndEnabled) StartCoroutine(CaptureBeforeNextFrame());
        }

        IEnumerator CaptureBeforeNextFrame()
        {
            yield return null;
            HasBefore = Capture(ref before);
        }

        /// <summary>Capture l'état actuel du tapis dans la texture « après ».</summary>
        public bool CaptureAfter() => Capture(ref after);

        bool Capture(ref RenderTexture target)
        {
            if (rug.Definition == null) return false;
            rug.DirtRenderer.UploadNow();

            Vector2 size = rug.Definition.sizeMeters;
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
            cam.SetPositionAndRotation(rug.transform.TransformPoint(new Vector3(0f, 2.5f, 0f)),
                rug.transform.rotation * Quaternion.Euler(90f, 0f, 0f));
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
