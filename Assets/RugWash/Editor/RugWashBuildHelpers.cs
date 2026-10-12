using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RugWash.Editor
{
    /// <summary>Utilitaires de construction procédurale de scène (maillages, UI, particules, câblage).</summary>
    public static class RugWashBuildHelpers
    {
        // ------------------------------------------------------------------ Câblage sérialisé

        /// <summary>Assigne des champs [SerializeField] via SerializedObject (équivalent du glisser-déposer Inspector).</summary>
        public static void Wire(Object target, params (string property, Object value)[] references)
        {
            var serialized = new SerializedObject(target);
            foreach (var (property, value) in references)
                Find(serialized, property, target).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void WireList<T>(Object target, string property, IList<T> values) where T : Object
        {
            var serialized = new SerializedObject(target);
            var list = Find(serialized, property, target);
            list.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetInt(Object target, string property, int value)
        {
            var serialized = new SerializedObject(target);
            Find(serialized, property, target).intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetString(Object target, string property, string value)
        {
            var serialized = new SerializedObject(target);
            Find(serialized, property, target).stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetFloat(Object target, string property, float value)
        {
            var serialized = new SerializedObject(target);
            Find(serialized, property, target).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static SerializedProperty Find(SerializedObject serialized, string property, Object target) =>
            serialized.FindProperty(property)
            ?? throw new InvalidOperationException($"[RugWash] Champ '{property}' introuvable sur {target.GetType().Name}.");

        // ------------------------------------------------------------------ Maillages

        /// <summary>Stocke les maillages générés dans un seul asset (sinon ils seraient perdus ou dupliqués dans la scène).</summary>
        public sealed class MeshStore
        {
            readonly string path;
            readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
            Mesh root;

            public MeshStore(string assetPath)
            {
                path = assetPath;
                AssetDatabase.DeleteAsset(path);
            }

            public Mesh Add(string key, Func<Mesh> build)
            {
                if (meshes.TryGetValue(key, out var existing)) return existing;
                var mesh = build();
                mesh.name = key;
                if (root == null)
                {
                    AssetDatabase.CreateAsset(mesh, path);
                    root = mesh;
                }
                else AssetDatabase.AddObjectToAsset(mesh, root);
                meshes[key] = mesh;
                return mesh;
            }

            public Mesh Box(Vector3 size) => Add($"Box_{size.x:0.###}x{size.y:0.###}x{size.z:0.###}", () => BuildBox(size));
        }

        /// <summary>Boîte centrée dont les UV sont exprimées en mètres : les textures gardent la même échelle quelle que soit la taille.</summary>
        public static Mesh BuildBox(Vector3 size)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<Vector4>();
            var tris = new List<int>();
            Vector3 h = size * 0.5f;

            void Face(Vector3 normal, Vector3 right, Vector3 up, float width, float height)
            {
                int i = v.Count;
                Vector3 center = Vector3.Scale(normal, h);
                Vector3 r = right * (width * 0.5f), u = up * (height * 0.5f);
                v.Add(center - r - u); v.Add(center + r - u); v.Add(center + r + u); v.Add(center - r + u);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(width, 0)); uv.Add(new Vector2(width, height)); uv.Add(new Vector2(0, height));
                for (int k = 0; k < 4; k++)
                {
                    n.Add(normal);
                    t.Add(new Vector4(right.x, right.y, right.z, -1f));
                }
                tris.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }

            Face(Vector3.up, Vector3.right, Vector3.forward, size.x, size.z);
            Face(Vector3.down, Vector3.right, Vector3.back, size.x, size.z);
            Face(Vector3.forward, Vector3.left, Vector3.up, size.x, size.y);
            Face(Vector3.back, Vector3.right, Vector3.up, size.x, size.y);
            Face(Vector3.left, Vector3.back, Vector3.up, size.z, size.y);
            Face(Vector3.right, Vector3.forward, Vector3.up, size.z, size.y);

            var mesh = new Mesh();
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.SetTangents(t);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static GameObject CreateBox(MeshStore store, string name, Transform parent, Vector3 localPosition, Vector3 size,
            Material material, bool collider = true, bool isStatic = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.AddComponent<MeshFilter>().sharedMesh = store.Box(size);
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (collider) go.AddComponent<BoxCollider>().size = size;
            if (isStatic) MakeStatic(go);
            return go;
        }

        public static GameObject CreatePrimitive(PrimitiveType type, string name, Transform parent, Vector3 localPosition,
            Vector3 localScale, Material material, bool keepCollider = false, Quaternion? rotation = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = rotation ?? Quaternion.identity;
            go.transform.localScale = localScale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        public static void MakeStatic(GameObject go) => GameObjectUtility.SetStaticEditorFlags(go,
            StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.BatchingStatic |
            StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);

        public static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
        }

        public static void DisableShadows(GameObject go)
        {
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        // ------------------------------------------------------------------ Particules

        public struct ParticleConfig
        {
            public Vector2 Lifetime, Speed, Size;
            public Color Color;
            public float Gravity;
            public int MaxParticles;
            public ParticleSystemShapeType Shape;
            public float Angle, Radius;
            public float SizeGrowth;
            public bool Loop;
            public bool PlayOnAwake;
            public float Rate;
            /// <summary>Si &gt; 0 : particules étirées selon la vitesse (jets, gouttes).</summary>
            public float Stretch;
            /// <summary>Si &gt; 0 : émission unique de N particules (effet ponctuel, non bouclé).</summary>
            public int Burst;
        }

        public static ParticleSystem CreateParticles(string name, Transform parent, Material material, ParticleConfig config)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = config.Loop;
            main.duration = config.Loop ? 1f : 1.2f;
            main.playOnAwake = config.PlayOnAwake;
            main.startLifetime = new ParticleSystem.MinMaxCurve(config.Lifetime.x, config.Lifetime.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(config.Speed.x, config.Speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(config.Size.x, config.Size.y);
            main.startColor = config.Color;
            main.gravityModifier = config.Gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = config.MaxParticles;

            var emission = ps.emission;
            emission.rateOverTime = config.Rate;
            if (config.Burst > 0)
            {
                main.loop = false;
                main.duration = 0.2f;
                main.playOnAwake = false;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)config.Burst) });
            }

            var shape = ps.shape;
            shape.shapeType = config.Shape;
            shape.angle = config.Angle;
            shape.radius = config.Radius;

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;

            if (config.SizeGrowth > 0f)
            {
                var size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, config.SizeGrowth));
            }

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = config.Stretch > 0f ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            if (config.Stretch > 0f)
            {
                renderer.velocityScale = config.Stretch;
                renderer.lengthScale = 1f;
            }
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        // ------------------------------------------------------------------ Interface

        public static Font UIFont => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            return rect;
        }

        public static RectTransform Stretch(string name, Transform parent, float margin = 0f) =>
            CreateRect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-margin * 2f, -margin * 2f));

        public static Image AddImage(RectTransform rect, Sprite sprite, Color color, bool sliced = true, bool raycast = false)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null && sliced ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        public static Text AddText(RectTransform rect, string content, int size, TextAnchor alignment, Color color,
            FontStyle style = FontStyle.Normal, bool shadow = true)
        {
            var text = rect.gameObject.AddComponent<Text>();
            text.font = UIFont;
            text.text = content;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            if (shadow)
            {
                var s = rect.gameObject.AddComponent<Shadow>();
                s.effectColor = new Color(0f, 0f, 0f, 0.6f);
                s.effectDistance = new Vector2(1.5f, -1.5f);
            }
            return text;
        }

        public static Image CreateFillBar(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size,
            Sprite rounded, Sprite white, Color background, Color fill, out RectTransform barRoot)
        {
            barRoot = CreateRect(name, parent, anchor, anchor, pivot, position, size);
            AddImage(barRoot, rounded, background);
            var fillRect = Stretch("Fill", barRoot, 3f);
            var image = AddImage(fillRect, white, fill, sliced: false);
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = (int)Image.OriginHorizontal.Left;
            image.fillAmount = 1f;
            return image;
        }

        public static Button CreateButton(string name, Transform parent, string label, Sprite rounded, Vector2 size)
        {
            var rect = CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var image = AddImage(rect, rounded, Color.white, raycast: true);
            var layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = size.y;
            layout.preferredWidth = size.x;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = new Color(0.16f, 0.2f, 0.24f, 0.95f);
            colors.highlightedColor = new Color(0.2f, 0.5f, 0.64f, 1f);
            // Sélection (manette, clavier) : ambre, distincte du survol à la souris et visible sans curseur.
            colors.selectedColor = FocusColor;
            colors.fadeDuration = 0.05f;
            colors.pressedColor = new Color(0.12f, 0.36f, 0.48f, 1f);
            colors.disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
            button.colors = colors;
            AddText(Stretch("Label", rect), label, 30, TextAnchor.MiddleCenter, Color.white);
            return button;
        }

        public static Slider CreateSlider(string name, Transform parent, Sprite rounded, Sprite white, Vector2 size, Color fill)
        {
            var root = CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);

            var background = CreateRect("Background", root, new Vector2(0f, 0.35f), new Vector2(1f, 0.65f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            AddImage(background, rounded, new Color(0f, 0f, 0f, 0.55f));

            var fillArea = CreateRect("Fill Area", root, new Vector2(0f, 0.35f), new Vector2(1f, 0.65f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-20f, 0f));
            var fillRect = CreateRect("Fill", fillArea, Vector2.zero, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 0f));
            AddImage(fillRect, white, fill, sliced: false);

            var handleArea = CreateRect("Handle Slide Area", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-20f, 0f));
            var handle = CreateRect("Handle", handleArea, Vector2.zero, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22f, 0f));
            var handleImage = AddImage(handle, rounded, Color.white, raycast: true);

            var slider = root.gameObject.AddComponent<Slider>();
            slider.fillRect = fillRect;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            var colors = slider.colors;
            colors.highlightedColor = new Color(0.8f, 0.93f, 1f, 1f);
            colors.selectedColor = FocusColor;
            colors.fadeDuration = 0.05f;
            slider.colors = colors;
            return slider;
        }

        /// <summary>Couleur de l'élément sélectionné à la manette ou au clavier.</summary>
        public static readonly Color FocusColor = new Color(0.86f, 0.58f, 0.16f, 1f);

        /// <summary>Onglet : la page affichée (bouton inactif) prend une teinte « page courante » au lieu du gris désactivé.</summary>
        public static void StyleTab(Button tab)
        {
            var colors = tab.colors;
            colors.disabledColor = new Color(0.12f, 0.42f, 0.55f, 1f);
            tab.colors = colors;
        }

        public static VerticalLayoutGroup AddVerticalLayout(GameObject go, int spacing, RectOffset padding, TextAnchor alignment)
        {
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding;
            layout.childAlignment = alignment;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            return layout;
        }
    }
}
