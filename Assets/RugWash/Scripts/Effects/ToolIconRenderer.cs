using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RugWash.Effects
{
    /// <summary>
    /// Icônes 3D des outils (grande mise à jour) : une seule caméra, hors de la salle, photographie chaque modèle d'outil (le même
    /// que celui tenu en main, doré au niveau maximal) en vue de trois quarts légèrement plongeante, dans une texture commune
    /// (une case par outil). Rendu au démarrage puis seulement quand un outil change d'aspect (doré) : aucun coût en jeu.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ToolIconRenderer : MonoBehaviour
    {
        [SerializeField] Camera iconCamera;
        [SerializeField] Light iconLight;
        [Tooltip("Calque réservé aux modèles photographiés (vu par la caméra des icônes seulement).")]
        [SerializeField] int iconLayer = 10;
        [SerializeField, Range(64, 256)] int iconSize = 128;
        [Tooltip("Vue de trois quarts : lacet et plongée de la caméra (degrés).")]
        [SerializeField] Vector2 viewAngles = new Vector2(112f, 24f);
        [Tooltip("Matériaux masqués sur les icônes (gants).")]
        [SerializeField] List<Material> hiddenMaterials = new List<Material>();
        [Tooltip("Matériaux ignorés pour le cadrage (tuyaux qui pendent) : l'outil remplit l'icône, le tuyau peut en sortir.")]
        [SerializeField] List<Material> ignoredForFraming = new List<Material>();

        const int Columns = 8, Rows = 2;
        readonly Dictionary<string, int> cells = new Dictionary<string, int>();
        readonly Dictionary<string, Transform> models = new Dictionary<string, Transform>();
        readonly List<string> order = new List<string>();
        RenderTexture atlas, scratch, supersampled;

        public Texture Atlas => atlas;
        /// <summary>Icônes rendues (diagnostic, tests).</summary>
        public int Rendered { get; private set; }
        public event Action Changed;

        public bool IsConfigured(out string missing)
        {
            missing = iconCamera == null ? nameof(iconCamera) : null;
            return missing == null;
        }

        void Awake()
        {
            if (iconCamera != null) iconCamera.enabled = false;
            if (iconLight != null) iconLight.enabled = false;
        }

        void OnDestroy()
        {
            Release(ref atlas);
            Release(ref scratch);
            Release(ref supersampled);
        }

        /// <summary>Déclare un modèle (identifiant de l'outil ou du gadget) ; son icône est rendue à la demande.</summary>
        public void Register(string id, Transform model)
        {
            if (string.IsNullOrEmpty(id) || model == null || models.ContainsKey(id) || order.Count >= Columns * Rows) return;
            models[id] = model;
            cells[id] = order.Count;
            order.Add(id);
        }

        /// <summary>Case de l'icône dans <see cref="Atlas"/> (coordonnées normalisées) ; faux si l'outil n'a pas d'icône.</summary>
        public bool TryGetIcon(string id, out Rect uv)
        {
            uv = default;
            if (atlas == null || string.IsNullOrEmpty(id) || !cells.TryGetValue(id, out int cell)) return false;
            uv = new Rect((cell % Columns) / (float)Columns, 1f - (cell / Columns + 1) / (float)Rows, 1f / Columns, 1f / Rows);
            return true;
        }

        /// <summary>(Re)photographie toutes les icônes (démarrage, outil devenu doré).</summary>
        public void RenderAll()
        {
            if (iconCamera == null || order.Count == 0) return;
            EnsureTargets();
            if (iconLight != null) iconLight.enabled = true;
            foreach (string id in order) RenderIcon(id);
            if (iconLight != null) iconLight.enabled = false;
            Changed?.Invoke();
        }

        void EnsureTargets()
        {
            if (atlas == null)
            {
                atlas = new RenderTexture(iconSize * Columns, iconSize * Rows, 0, RenderTextureFormat.ARGB32) { name = "ToolIcons", hideFlags = HideFlags.DontSave };
                atlas.Create();
                var previous = RenderTexture.active;
                RenderTexture.active = atlas;
                GL.Clear(true, true, Color.clear);
                RenderTexture.active = previous;
            }
            // Rendu à deux fois la taille puis réduit (anticrénelage simple) : la copie dans l'atlas exige une texture sans MSAA.
            if (supersampled == null)
            {
                supersampled = new RenderTexture(iconSize * 2, iconSize * 2, 24, RenderTextureFormat.ARGB32) { name = "ToolIconCapture", hideFlags = HideFlags.DontSave };
                supersampled.Create();
            }
            if (scratch == null)
            {
                scratch = new RenderTexture(iconSize, iconSize, 0, RenderTextureFormat.ARGB32) { name = "ToolIconScratch", hideFlags = HideFlags.DontSave };
                scratch.Create();
            }
        }

        void RenderIcon(string id)
        {
            var source = models[id];
            if (source == null) return;
            // Copie du modèle dans le studio (hors de la salle), sans scripts ni effets, sur le calque des icônes.
            var copy = Instantiate(source.gameObject, transform);
            copy.name = $"Icon_{id}";
            foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(behaviour);
            foreach (var audio in copy.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(audio);
            var renderers = new List<Renderer>();
            foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true))
            {
                bool keep = renderer is MeshRenderer && !Hidden(renderer);
                renderer.enabled = keep;
                renderer.gameObject.layer = iconLayer;
                if (keep) renderers.Add(renderer);
            }
            foreach (var t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = iconLayer;
            copy.SetActive(true);
            copy.transform.SetPositionAndRotation(transform.position, Quaternion.identity);
            copy.transform.localScale = Vector3.one;

            if (renderers.Count > 0)
            {
                // Cadrage sur le corps de l'outil (sans les tuyaux), au plus près de sa silhouette vue par la caméra.
                var framed = new List<Renderer>();
                foreach (var renderer in renderers)
                    if (!Uses(renderer, ignoredForFraming)) framed.Add(renderer);
                if (framed.Count == 0) framed = renderers;
                var bounds = framed[0].bounds;
                foreach (var renderer in framed) bounds.Encapsulate(renderer.bounds);
                Frame(bounds);
                var request = new RenderPipeline.StandardRequest { destination = supersampled };
                if (RenderPipeline.SupportsRenderRequest(iconCamera, request))
                {
                    RenderPipeline.SubmitRenderRequest(iconCamera, request);
                    Graphics.Blit(supersampled, scratch);
                    int cell = cells[id];
                    Graphics.CopyTexture(scratch, 0, 0, 0, 0, iconSize, iconSize, atlas, 0, 0, (cell % Columns) * iconSize, (Rows - 1 - cell / Columns) * iconSize);
                    Rendered++;
                }
            }
            DestroyImmediate(copy);
        }

        /// <summary>
        /// V0.2.5 — copie décorative d'un objet (sans scripts, sons, effets, gants ni tuyaux), posée dans le monde : objet
        /// tombé au sol. Null si l'objet n'a pas de modèle connu.
        /// </summary>
        public GameObject CreateWorldCopy(string id, Transform parent)
        {
            if (string.IsNullOrEmpty(id) || !models.TryGetValue(id, out var source) || source == null) return null;
            var copy = Instantiate(source.gameObject, parent);
            copy.name = $"Dropped_{id}";
            foreach (var system in copy.GetComponentsInChildren<ParticleSystem>(true)) DestroyImmediate(system.gameObject);
            foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(behaviour);
            foreach (var audio in copy.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(audio);
            foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true))
            {
                bool keep = renderer is MeshRenderer && !Hidden(renderer) && !Uses(renderer, ignoredForFraming);
                renderer.enabled = keep;
                if (keep) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            foreach (var t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = Vector3.one;
            copy.SetActive(true);
            return copy;
        }

        bool Hidden(Renderer renderer) => Uses(renderer, hiddenMaterials);

        static bool Uses(Renderer renderer, List<Material> materials)
        {
            foreach (var material in renderer.sharedMaterials)
                if (material != null && materials.Contains(material)) return true;
            return false;
        }

        /// <summary>
        /// Caméra en trois quarts plongeante, cadrée au plus juste : les coins de la boîte englobante sont projetés dans le repère
        /// de la caméra, la distance est celle qui fait tenir leur étendue (marge de 8 %).
        /// </summary>
        void Frame(Bounds bounds)
        {
            var cam = iconCamera;
            cam.cullingMask = 1 << iconLayer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.fieldOfView = 22f;
            cam.aspect = 1f;
            var rotation = Quaternion.Euler(viewAngles.y, viewAngles.x, 0f);
            var inverse = Quaternion.Inverse(rotation);
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                var local = inverse * (corner - bounds.center);
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }
            var centerLocal = (min + max) * 0.5f;
            float half = Mathf.Max(0.01f, Mathf.Max(max.x - min.x, max.y - min.y) * 0.5f) * 1.08f;
            float depth = (max.z - min.z) * 0.5f;
            float distance = half / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) + depth;
            var center = bounds.center + rotation * centerLocal;
            cam.transform.SetPositionAndRotation(center - rotation * Vector3.forward * distance, rotation);
            cam.nearClipPlane = Mathf.Max(0.005f, distance - depth * 3f - 0.5f);
            cam.farClipPlane = distance + depth * 3f + 0.5f;
            if (iconLight != null) iconLight.transform.position = center + rotation * new Vector3(-0.6f, 0.9f, -1.2f) * Mathf.Max(0.4f, distance * 0.5f);
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
