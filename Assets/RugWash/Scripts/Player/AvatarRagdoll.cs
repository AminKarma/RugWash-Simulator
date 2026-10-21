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
        [Tooltip("V0.4.2 : morceaux de membres (bras, jambes) lancés par la dynamite, cachés le reste du temps, même ordre que « limbs ».")]
        [SerializeField] List<Transform> scatterPieces = new List<Transform>();

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
        // V0.4.2 : dispersion des membres (dynamite) : morceaux lancés, membres réels cachés.
        bool scattered;
        readonly List<Rigidbody> pieceBodies = new List<Rigidbody>();
        readonly List<Vector3> pieceFromPosition = new List<Vector3>();
        readonly List<Quaternion> pieceFromRotation = new List<Quaternion>();
        readonly List<Renderer> hiddenLimbRenderers = new List<Renderer>();
        // V0.4.2 : glissade sur la mousse, animée sans physique (voir BeginSlip).
        bool sliding;
        Vector3 slideDirection, hipInBody;

        public bool IsActive => active;
        public bool IsBusy => active || blend > 0f;
        public Rigidbody BodyRigidbody => bodyRigidbody;
        /// <summary>Glissade en cours (chute cartoon animée, le corps suit la position réelle du joueur).</summary>
        public bool IsSliding => sliding;
        /// <summary>V0.4.2 : membres dispersés (dynamite) jusqu'à la fin du retour à la pose.</summary>
        public bool LimbsScattered => scattered;
        /// <summary>Morceaux de membres actuellement visibles (tests).</summary>
        public int ScatterPiecesShown
        {
            get
            {
                int n = 0;
                foreach (var piece in scatterPieces) if (piece != null && piece.gameObject.activeSelf) n++;
                return n;
            }
        }
        /// <summary>Plus grand écart (m) entre un morceau lancé et son membre (tests).</summary>
        public float ScatterSpread
        {
            get
            {
                float best = 0f;
                for (int i = 0; i < scatterPieces.Count && i < limbs.Count; i++)
                    if (scatterPieces[i] != null && scatterPieces[i].gameObject.activeSelf && limbs[i] != null)
                        best = Mathf.Max(best, Vector3.Distance(scatterPieces[i].position, limbs[i].position));
                return best;
            }
        }

        /// <summary>
        /// V0.4.2 : explosion de dynamite. Chute physique exagérée du corps (comme le fusil), et les membres « se dispersent »
        /// dans l'atelier : les vrais membres sont cachés, des morceaux à leur image sont lancés depuis leur place. Purement
        /// visuel et temporaire : au relevé, les morceaux reviennent se fixer et les membres réapparaissent (le personnage
        /// réseau, son squelette et son identité ne sont jamais touchés).
        /// </summary>
        public void BeginScatter(Vector3 impulse)
        {
            Begin(impulse);
            if (!active || scatterPieces.Count == 0) return;
            scattered = true;
            hiddenLimbRenderers.Clear();
            foreach (var limb in limbs)
                if (limb != null)
                    foreach (var renderer in limb.GetComponentsInChildren<Renderer>())
                        if (renderer.enabled && !IsPiece(renderer.transform))
                        {
                            renderer.enabled = false;
                            hiddenLimbRenderers.Add(renderer);
                        }
            Vector3 center = body.position + Vector3.up * 1f;
            for (int i = 0; i < scatterPieces.Count && i < limbs.Count; i++)
            {
                var piece = scatterPieces[i];
                var limb = limbs[i];
                if (piece == null || limb == null) continue;
                piece.SetParent(body.parent, true);
                piece.SetPositionAndRotation(limb.position, limb.rotation);
                piece.gameObject.SetActive(true);
                var rigidbody = piece.GetComponent<Rigidbody>();
                if (rigidbody == null) rigidbody = AddBody(piece.gameObject, 4f);
                else created.Add(rigidbody);
                Vector3 outward = limb.position - center;
                outward.y = 0f;
                if (outward.sqrMagnitude < 1e-4f) outward = Quaternion.Euler(0f, i * 90f, 0f) * Vector3.forward;
                // Dans tous les sens (cartoon), un peu plus dans le sens du souffle ; vitesse bornée comme le corps.
                Vector3 velocity = outward.normalized * (4f + i * 0.6f) + Vector3.up * (3.5f + (i % 2) * 1.2f) + impulse * 0.35f;
                rigidbody.linearVelocity = Vector3.ClampMagnitude(velocity, maxSpeed);
                rigidbody.angularVelocity = new Vector3(6f - i * 3f, 4f + i, i * 2f - 3f);
                pieceBodies.Add(rigidbody);
            }
        }

        bool IsPiece(Transform t)
        {
            foreach (var piece in scatterPieces) if (piece != null && t.IsChildOf(piece)) return true;
            return false;
        }
        /// <summary>Hanches du corps (monde) : point qui doit avancer sans à-coup pendant une glissade.</summary>
        public Vector3 HipPosition => body == null ? transform.position : body.TransformPoint(sliding ? hipInBody : RestHip());

        /// <summary>Hanches au repos dans le repère du corps : haut des jambes, centré (même point que pendant une glissade).</summary>
        Vector3 RestHip()
        {
            var leg = limbs.Count > 0 ? limbs[0] : null;
            Vector3 hip = leg != null ? body.InverseTransformPoint(leg.position) : new Vector3(0f, 0.93f, 0f);
            hip.x = 0f;
            return hip;
        }
        /// <summary>Écart horizontal entre le corps et la racine de la silhouette (m) : reste court pendant une glissade.</summary>
        public float BodyOffsetFromRoot
        {
            get
            {
                if (body == null || body.parent == null) return 0f;
                Vector3 offset = body.position - body.parent.position;
                return new Vector2(offset.x, offset.z).magnitude;
            }
        }

        /// <summary>
        /// V0.4.2 : glissade sur la mousse vue par les autres joueurs. Pas de physique : le corps bascule sur le dos, jambes
        /// en l'air et bras qui moulinent, en restant attaché à la silhouette. Celle-ci suit la position réelle du joueur qui
        /// continue de glisser chez lui : la glissade se poursuit à l'identique, sans corps arrêté par le frottement ni retour
        /// en arrière au relevé (le ragdoll physique, lui, s'arrêtait sur place pendant que le joueur glissait encore).
        /// </summary>
        public void BeginSlip(Vector3 impulse)
        {
            if (body == null) return;
            if (active) return;
            if (blend > 0f) FinishBlend();
            saved.Clear();
            Save(body);
            foreach (var limb in limbs) if (limb != null) Save(limb);
            Vector3 horizontal = new Vector3(impulse.x, 0f, impulse.z);
            Vector3 local = body.parent != null ? body.parent.InverseTransformDirection(horizontal) : horizontal;
            local.y = 0f;
            slideDirection = local.sqrMagnitude > 1e-4f ? local.normalized : Vector3.forward;
            // Hanches : point de pivot de la chute (haut des jambes), exprimé dans le repère du corps.
            hipInBody = RestHip();
            startPosition = body.position;
            sliding = true;
            active = true;
            timer = 0f;
            PoseSlip(0f);
        }

        void PoseSlip(float t)
        {
            var bodySaved = saved[0];
            const float FallSeconds = 0.3f;
            float f = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / FallSeconds));
            float landed = Mathf.Max(0f, t - FallSeconds);
            float settle = Mathf.Exp(-3f * landed);
            Vector3 axis = Vector3.Cross(Vector3.up, slideDirection);
            // Sur le dos (bascule vers l'arrière), petit rebond cartoon à l'impact.
            float bounce = Mathf.Abs(Mathf.Sin(landed * 11f)) * settle;
            float angle = 82f * f - 6f * bounce * f;
            Quaternion rotation = Quaternion.AngleAxis(-angle, axis) * bodySaved.LocalRotation;
            Vector3 hipStart = bodySaved.LocalPosition + bodySaved.LocalRotation * hipInBody;
            Vector3 hipEnd = new Vector3(hipStart.x, 0.2f, hipStart.z) + slideDirection * 0.35f;
            Vector3 hip = Vector3.Lerp(hipStart, hipEnd, f) + Vector3.up * (0.05f * bounce);
            body.localRotation = rotation;
            body.localPosition = hip - rotation * hipInBody;
            Vector3 worldAxis = body.parent != null ? body.parent.TransformDirection(axis) : axis;
            Vector3 worldSlide = body.parent != null ? body.parent.TransformDirection(slideDirection) : slideDirection;
            for (int i = 0; i < limbs.Count; i++)
            {
                var limb = limbs[i];
                if (limb == null || i + 1 >= saved.Count) continue;
                limb.localRotation = saved[i + 1].LocalRotation;
                float side = i % 2 == 0 ? 1f : -1f;
                if (i < 2)
                {
                    // Jambes en l'air, en ciseaux qui s'amortissent.
                    float kick = (35f + side * 14f * Mathf.Sin(t * 15f) * (0.3f + 0.7f * settle)) * f;
                    limb.rotation = Quaternion.AngleAxis(-kick, worldAxis) * limb.rotation;
                }
                else
                {
                    // Bras qui moulinent pour se rattraper.
                    float flail = Mathf.Sin(t * 17f + side) * 55f * (0.35f + 0.65f * settle) * f;
                    limb.rotation = Quaternion.AngleAxis(flail, worldAxis) * Quaternion.AngleAxis(side * 50f * f, worldSlide) * limb.rotation;
                }
            }
        }

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
            sliding = false;
            foreach (var item in created)
                if (item != null) DestroyImmediate(item);
            created.Clear();
            bodyRigidbody = null;
            pieceBodies.Clear();
            if (scattered)
            {
                // Les morceaux reviennent à leur membre pendant le retour à la pose (voir Tick).
                pieceFromPosition.Clear();
                pieceFromRotation.Clear();
                foreach (var piece in scatterPieces)
                {
                    pieceFromPosition.Add(piece != null ? piece.position : Vector3.zero);
                    pieceFromRotation.Add(piece != null ? piece.rotation : Quaternion.identity);
                }
            }
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
            if (active && sliding)
            {
                timer += deltaTime;
                if (timer >= maxSeconds) End();
                else PoseSlip(timer);
                return;
            }
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
            if (scattered)
            {
                for (int i = 0; i < scatterPieces.Count && i < limbs.Count && i < pieceFromPosition.Count; i++)
                    if (scatterPieces[i] != null && limbs[i] != null)
                        scatterPieces[i].SetPositionAndRotation(Vector3.Lerp(pieceFromPosition[i], limbs[i].position, k),
                            Quaternion.Slerp(pieceFromRotation[i], limbs[i].rotation, k));
                if (blend <= 0f) Reassemble();
            }
        }

        /// <summary>Fin de la dispersion : morceaux cachés et rangés, vrais membres de nouveau visibles.</summary>
        void Reassemble()
        {
            if (!scattered) return;
            scattered = false;
            foreach (var piece in scatterPieces)
            {
                if (piece == null) continue;
                piece.gameObject.SetActive(false);
                piece.SetParent(transform, false);
            }
            foreach (var renderer in hiddenLimbRenderers) if (renderer != null) renderer.enabled = true;
            hiddenLimbRenderers.Clear();
        }

        void FinishBlend()
        {
            blend = 0f;
            Reassemble();
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
