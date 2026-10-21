using System;
using System.Collections.Generic;
using UnityEngine;

namespace RugWash.Player
{
    /// <summary>
    /// V0.3.0 : personnage à squelette (modélisé dans Blender, Art/Blender/Characters/Ouvrier.blend) piloté par la silhouette
    /// existante. L'animation reste celle du jeu (jambes balancées selon la vitesse, bras à deux segments refermés sur l'outil,
    /// tête qui suit le tangage, ragdoll à terre) : chaque os suit l'orientation de la pièce de la silhouette qui lui correspond
    /// (décalage mesuré au repos), sans Animator concurrent. Les genoux fléchissent quand la jambe passe derrière (marche,
    /// course) ; la tête peut être masquée (KO cartoon du fusil de farces).
    /// </summary>
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class AvatarSkeleton : MonoBehaviour
    {
        [Serializable]
        public struct Link
        {
            public Transform bone;
            public Transform driver;
            [Tooltip("Rotation de l'os dans le repère de la pièce pilote, au repos.")]
            public Quaternion offset;
            public bool rotate;
            [Tooltip("L'os est aussi placé sur ce point de la pièce pilote (coude, poignet : bras étirés).")]
            public bool move;
            public Vector3 point;
            [Tooltip("Part de la rotation pilote appliquée (cou et tête se partagent le tangage).")]
            [Range(0f, 1f)] public float weight;
            public Quaternion restLocal;
            [Tooltip("Axe de la longueur de l'os (repère local) : l'os s'allonge comme la pièce pilote (échelle Z), bras étirés.")]
            public Vector3 stretchAxis;
        }

        [Serializable]
        public struct Knee
        {
            public Transform bone;
            [Tooltip("Pièce de la silhouette dont le balancement (rotation autour de X) commande la flexion.")]
            public Transform leg;
            public Quaternion rest;
            [Tooltip("Axe de flexion dans le repère local de l'os.")]
            public Vector3 axis;
        }

        [Serializable]
        public struct Finger
        {
            public Transform bone;
            public Quaternion rest;
            [Tooltip("Axe de flexion (repère local de l'os) : la phalange se replie vers la paume.")]
            public Vector3 axis;
            public bool left;
            [Tooltip("Flexion maximale (degrés) quand la main est refermée.")]
            public float maxAngle;
        }

        [SerializeField] List<Link> links = new List<Link>();
        [SerializeField] List<Finger> fingers = new List<Finger>();
        [SerializeField] List<Knee> knees = new List<Knee>();
        [SerializeField] Transform headBone;
        [Tooltip("Flexion du genou par degré de balancement de la jambe vers l'arrière.")]
        [SerializeField, Range(0f, 3f)] float kneeFactor = 1.4f;
        [SerializeField, Range(0f, 120f)] float maxKnee = 70f;

        public bool HeadHidden { get; set; }
        [Tooltip("V0.4.2 : os des membres (bras, avant-bras, mains, cuisses) cachés pendant la dispersion cartoon de la dynamite.")]
        [SerializeField] List<Transform> limbBones = new List<Transform>();
        /// <summary>V0.4.2 : membres cachés (remplacés par des morceaux qui volent dans l'atelier), le temps du KO.</summary>
        public bool LimbsHidden { get; set; }
        bool limbsWereHidden;
        /// <summary>Fermeture des mains (0 ouverte, 1 poing serré), lissée ; pilotée par la présentation de l'avatar.</summary>
        public float LeftGrip { get; set; } = 0.25f;
        public float RightGrip { get; set; } = 0.25f;
        float leftShown = 0.25f, rightShown = 0.25f;
        public int FingerCount => fingers.Count;
        public int LinkCount => links.Count;
        public Transform HeadBone => headBone;

#if UNITY_EDITOR
        /// <summary>Construction (éditeur) : relie un os à sa pièce pilote dans leur pose de repos actuelle.</summary>
        public void AddLink(Transform bone, Transform driver, float weight = 1f)
        {
            if (bone == null || driver == null) return;
            links.Add(new Link
            {
                bone = bone, driver = driver, offset = Quaternion.Inverse(driver.rotation) * bone.rotation, rotate = true, weight = weight,
                restLocal = bone.localRotation,
            });
        }

        /// <summary>Construction (éditeur) : l'os suit la position d'un point de la pièce pilote (repère local de la pièce).</summary>
        public void AddAnchor(Transform bone, Transform driver, bool rotate, Vector3 stretchAxis = default)
        {
            if (bone == null || driver == null) return;
            links.Add(new Link
            {
                bone = bone, driver = driver, offset = Quaternion.Inverse(driver.rotation) * bone.rotation, rotate = rotate, move = true,
                point = driver.InverseTransformPoint(bone.position), weight = 1f, restLocal = bone.localRotation, stretchAxis = stretchAxis,
            });
        }

        /// <summary>Construction (éditeur) : os orienté comme sa pièce pilote et allongé avec elle le long de <paramref name="stretchAxis"/>.</summary>
        public void AddStretchLink(Transform bone, Transform driver, Vector3 stretchAxis)
        {
            AddLink(bone, driver);
            var link = links[links.Count - 1];
            link.stretchAxis = stretchAxis;
            links[links.Count - 1] = link;
        }

        public void AddKnee(Transform bone, Transform leg, Vector3 worldAxis)
        {
            if (bone == null || leg == null) return;
            knees.Add(new Knee { bone = bone, leg = leg, rest = bone.localRotation, axis = bone.InverseTransformDirection(worldAxis).normalized });
        }

        public void SetHeadBone(Transform bone) => headBone = bone;
#if UNITY_EDITOR
        /// <summary>Construction (éditeur) : os des membres cachés pendant la dispersion (dynamite).</summary>
        public void SetLimbBones(IEnumerable<Transform> bones)
        {
            limbBones.Clear();
            foreach (var bone in bones) if (bone != null) limbBones.Add(bone);
        }
#endif

        /// <summary>
        /// Construction (éditeur) : phalange qui se replie de <paramref name="maxAngle"/> degrés vers <paramref name="palmWorld"/>
        /// (direction de la paume au repos), autour de l'axe perpendiculaire au doigt.
        /// </summary>
        public void AddFinger(Transform bone, Transform next, Vector3 palmWorld, bool left, float maxAngle)
        {
            if (bone == null || next == null) return;
            Vector3 along = (next.position - bone.position).normalized;
            Vector3 axisWorld = Vector3.Cross(along, palmWorld).normalized;
            if (axisWorld.sqrMagnitude < 1e-6f) return;
            fingers.Add(new Finger { bone = bone, rest = bone.localRotation, axis = bone.InverseTransformDirection(axisWorld).normalized, left = left, maxAngle = maxAngle });
        }
#endif

        void LateUpdate()
        {
            for (int i = 0; i < links.Count; i++)
            {
                var link = links[i];
                if (link.bone == null || link.driver == null) continue;
                if (link.move) link.bone.position = link.driver.TransformPoint(link.point);
                if (!link.rotate) continue;
                var target = link.driver.rotation * link.offset;
                if (link.weight < 0.999f && link.bone.parent != null)
                    target = Quaternion.Slerp(link.bone.parent.rotation * link.restLocal, target, link.weight);
                link.bone.rotation = target;
                if (link.stretchAxis != Vector3.zero)
                    link.bone.localScale = Vector3.one + link.stretchAxis * (link.driver.localScale.z - 1f);
            }
            for (int i = 0; i < knees.Count; i++)
            {
                var knee = knees[i];
                if (knee.bone == null || knee.leg == null) continue;
                float swing = Mathf.DeltaAngle(0f, knee.leg.localEulerAngles.x);
                float bend = Mathf.Clamp(swing * kneeFactor, 0f, maxKnee);
                knee.bone.localRotation = knee.rest * Quaternion.AngleAxis(bend, knee.axis);
            }
            float k = 1f - Mathf.Exp(-12f * Time.deltaTime);
            leftShown = Mathf.Lerp(leftShown, LeftGrip, k);
            rightShown = Mathf.Lerp(rightShown, RightGrip, k);
            for (int i = 0; i < fingers.Count; i++)
            {
                var finger = fingers[i];
                if (finger.bone == null) continue;
                float grip = finger.left ? leftShown : rightShown;
                finger.bone.localRotation = finger.rest * Quaternion.AngleAxis(finger.maxAngle * grip, finger.axis);
            }
            if (headBone != null) headBone.localScale = HeadHidden ? Vector3.one * 0.001f : Vector3.one;
            // Membres dispersés : écrasés à leur attache (rien ne s'étire) ; rendus à leur taille au relevé.
            if (LimbsHidden || limbsWereHidden)
                foreach (var bone in limbBones)
                    if (bone != null) bone.localScale = LimbsHidden ? Vector3.one * 0.001f : Vector3.one;
            limbsWereHidden = LimbsHidden;
        }
    }
}
