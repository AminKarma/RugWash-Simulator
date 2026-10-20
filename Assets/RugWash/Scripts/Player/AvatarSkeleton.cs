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

        [SerializeField] List<Link> links = new List<Link>();
        [SerializeField] List<Knee> knees = new List<Knee>();
        [SerializeField] Transform headBone;
        [Tooltip("Flexion du genou par degré de balancement de la jambe vers l'arrière.")]
        [SerializeField, Range(0f, 3f)] float kneeFactor = 1.4f;
        [SerializeField, Range(0f, 120f)] float maxKnee = 70f;

        public bool HeadHidden { get; set; }
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
            if (headBone != null) headBone.localScale = HeadHidden ? Vector3.one * 0.001f : Vector3.one;
        }
    }
}
