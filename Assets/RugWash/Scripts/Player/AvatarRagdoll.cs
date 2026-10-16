using System.Collections.Generic;
using UnityEngine;

namespace RugWash.Player
{
    /// <summary>
    /// Ragdoll d'une silhouette (v0.1.2) : le corps devient un solide qui bascule selon l'élan reçu (borné), jambes et bras
    /// articulés (joints à débattement limité), collisions avec le décor ; au relevé, les pièces reviennent en douceur à
    /// leur pose. Seul l'état « à terre » et l'élan passent par le réseau ; chaque poste simule sa copie (purement visuel).
    /// Garde-fous : vitesse et rotation bornées, durée maximale, dépénétration lente (pas de corps qui explose).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AvatarRagdoll : MonoBehaviour
    {
        [Tooltip("Racine du corps (tronc, tête, bras) qui bascule.")]
        [SerializeField] Transform body;
        [Tooltip("Membres articulés au corps : jambes puis bras.")]
        [SerializeField] List<Transform> limbs = new List<Transform>();
        [Tooltip("Avant-bras rattachés au bras correspondant pendant la chute (même ordre que les bras de « limbs »).")]
        [SerializeField] List<Transform> attached = new List<Transform>();
        [Tooltip("Vitesse maximale donnée au corps (m/s) : chute exagérée, jamais d'éjection hors de la salle.")]
        [SerializeField, Min(0f)] float maxSpeed = 12f;
        [SerializeField, Min(0f)] float maxSpin = 10f;
        [SerializeField, Range(0.1f, 1.5f)] float blendSeconds = 0.45f;
        [Tooltip("Durée maximale (le KO du fusil dure 5 s) : la silhouette se relève toujours.")]
        [SerializeField, Range(1f, 10f)] float maxSeconds = 8f;
        [Tooltip("Distance maximale au point de chute (m) : au-delà (collision manquée), retour immédiat à la pose.")]
        [SerializeField, Min(1f)] float maxTravel = 12f;

        struct Saved
        {
            public Transform Transform;
            public Transform Parent;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public int Layer;
            public Vector3 FromPosition;
            public Quaternion FromRotation;
        }

        readonly List<Saved> saved = new List<Saved>();
        readonly List<Object> created = new List<Object>();
        Rigidbody bodyRigidbody;
        float timer, blend;
        bool active;
        Vector3 startPosition;

        public bool IsActive => active;
        public bool IsBusy => active || blend > 0f;
        public Rigidbody BodyRigidbody => bodyRigidbody;

        public void Begin(Vector3 impulse)
        {
            if (body == null) return;
            if (active) return;
            if (blend > 0f) FinishBlend();
            saved.Clear();
            Save(body);
            foreach (var limb in limbs) if (limb != null) Save(limb);
            foreach (var child in attached) if (child != null) Save(child);

            const int IgnoreRaycast = 2;
            bodyRigidbody = AddBody(body.gameObject, 45f);
            var bodyCollider = body.gameObject.AddComponent<CapsuleCollider>();
            bodyCollider.center = new Vector3(0f, 1.15f, 0f);
            bodyCollider.height = 0.95f;
            bodyCollider.radius = 0.2f;
            created.Add(bodyCollider);
            body.gameObject.layer = IgnoreRaycast;

            for (int i = 0; i < limbs.Count; i++)
            {
                var limb = limbs[i];
                if (limb == null) continue;
                limb.SetParent(body.parent, true);
                limb.gameObject.layer = IgnoreRaycast;
                var limbBody = AddBody(limb.gameObject, 8f);
                var capsule = limb.gameObject.AddComponent<CapsuleCollider>();
                bool leg = i < 2;
                capsule.direction = leg ? 1 : 2;
                capsule.center = leg ? new Vector3(0f, -0.42f, 0f) : new Vector3(0f, 0f, 0.16f);
                capsule.height = leg ? 0.9f : 0.4f;
                capsule.radius = leg ? 0.08f : 0.06f;
                created.Add(capsule);
                Physics.IgnoreCollision(capsule, bodyCollider);
                var joint = limb.gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody = bodyRigidbody;
                joint.enableProjection = true;
                joint.lowTwistLimit = new SoftJointLimit { limit = -30f };
                joint.highTwistLimit = new SoftJointLimit { limit = 30f };
                joint.swing1Limit = new SoftJointLimit { limit = leg ? 35f : 70f };
                joint.swing2Limit = new SoftJointLimit { limit = leg ? 25f : 60f };
                created.Insert(0, joint);
                if (!leg && i - 2 < attached.Count && attached[i - 2] != null) attached[i - 2].SetParent(limb, true);
            }

            impulse = Vector3.ClampMagnitude(impulse, maxSpeed);
            bodyRigidbody.linearVelocity = impulse;
            startPosition = body.position;
            var horizontal = new Vector3(impulse.x, 0f, impulse.z);
            Vector3 axis = horizontal.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, horizontal.normalized) : body.right;
            bodyRigidbody.angularVelocity = axis * Mathf.Min(maxSpin, 3f + impulse.magnitude * 0.5f);
            active = true;
            timer = 0f;
        }

        Rigidbody AddBody(GameObject target, float mass)
        {
            var rigidbody = target.AddComponent<Rigidbody>();
            rigidbody.mass = mass;
            rigidbody.linearDamping = 0.4f;
            rigidbody.angularDamping = 1.5f;
            rigidbody.maxDepenetrationVelocity = 2f;
            rigidbody.maxAngularVelocity = maxSpin * 2f;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            created.Add(rigidbody);
            return rigidbody;
        }

        void Save(Transform target) => saved.Add(new Saved
        {
            Transform = target, Parent = target.parent, LocalPosition = target.localPosition, LocalRotation = target.localRotation,
            Layer = target.gameObject.layer,
        });

        /// <summary>Fin de la chute : la physique s'arrête et les pièces reviennent à leur pose en douceur.</summary>
        public void End()
        {
            if (!active) return;
            active = false;
            foreach (var item in created)
                if (item != null) DestroyImmediate(item);
            created.Clear();
            bodyRigidbody = null;
            for (int i = 0; i < saved.Count; i++)
            {
                var entry = saved[i];
                if (entry.Transform == null) continue;
                entry.Transform.SetParent(entry.Parent, true);
                entry.Transform.gameObject.layer = entry.Layer;
                entry.FromPosition = entry.Transform.localPosition;
                entry.FromRotation = entry.Transform.localRotation;
                saved[i] = entry;
            }
            blend = blendSeconds;
        }

        /// <summary>Avance la chute ou le retour à la pose (appelé par la présentation de la silhouette).</summary>
        public void Tick(float deltaTime)
        {
            if (active)
            {
                timer += deltaTime;
                // Garde-fous : durée bornée, et retour à la pose si le corps traverse le sol ou part trop loin.
                bool lost = body != null && (body.position.y < startPosition.y - 1.5f || (body.position - startPosition).sqrMagnitude > maxTravel * maxTravel);
                if (timer >= maxSeconds || lost) End();
                return;
            }
            if (blend <= 0f) return;
            blend = Mathf.Max(0f, blend - deltaTime);
            float k = 1f - blend / blendSeconds;
            k = k * k * (3f - 2f * k);
            foreach (var entry in saved)
            {
                if (entry.Transform == null) continue;
                entry.Transform.localPosition = Vector3.Lerp(entry.FromPosition, entry.LocalPosition, k);
                entry.Transform.localRotation = Quaternion.Slerp(entry.FromRotation, entry.LocalRotation, k);
            }
        }

        void FinishBlend()
        {
            blend = 0f;
            foreach (var entry in saved)
            {
                if (entry.Transform == null) continue;
                entry.Transform.localPosition = entry.LocalPosition;
                entry.Transform.localRotation = entry.LocalRotation;
            }
        }

        void OnDisable()
        {
            End();
            FinishBlend();
        }
    }
}
