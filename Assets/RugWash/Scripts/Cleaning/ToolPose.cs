using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>Calcul pur de la portée d'un outil à manche télescopique (testé en EditMode).</summary>
    public static class ToolReach
    {
        /// <summary>
        /// Allongement <c>e</c> du manche, le long de son axe <paramref name="shaftAxis"/> (unitaire, repère du modèle), tel que
        /// l'extrémité active passe à <paramref name="distance"/> de la main : |tipRest + e·axe| = distance. Le résultat est
        /// borné ; si la distance est inatteignable (plus courte que l'écart latéral de l'extrémité), le plus proche possible.
        /// </summary>
        public static float SolveExtension(Vector3 tipRest, Vector3 shaftAxis, float distance, float minExtension, float maxExtension)
        {
            float b = Vector3.Dot(tipRest, shaftAxis);
            float discriminant = b * b - (tipRest.sqrMagnitude - distance * distance);
            float extension = discriminant < 0f ? -b : -b + Mathf.Sqrt(discriminant);
            return Mathf.Clamp(extension, minExtension, maxExtension);
        }

        /// <summary>
        /// Point que l'extrémité active doit atteindre. Outil de contact (brosse, aspirateur…) : le point visé s'il est à
        /// portée, sinon le point du rayon à la portée maximale (l'outil se tend sans tricher sur la distance). Buse (lance,
        /// pulvérisateur…) : le point visé, quelle que soit sa distance (le jet fait le reste).
        /// </summary>
        public static Vector3 Target(bool contact, bool hasHit, Vector3 hitPoint, float hitDistance, Ray ray, float range)
        {
            if (contact) return hasHit && hitDistance <= range ? hitPoint : ray.GetPoint(hasHit ? Mathf.Min(hitDistance, range) : range);
            return hasHit ? hitPoint : ray.GetPoint(range + 2f);
        }
    }

    /// <summary>
    /// Pose d'un outil tenu en main, partagée par le joueur local (vue à la première personne) et les silhouettes des autres
    /// joueurs (mêmes modèles détaillés) :
    /// <list type="number">
    /// <item>portée : sur un outil de contact, le manche s'allonge (tête déplacée le long du manche, manche étiré) pour que
    /// l'extrémité active soit exactement à la distance du point visé ;</item>
    /// <item>orientation : rotation autour de la main qui amène l'axe main → extrémité active sur le point visé, lissée
    /// et bornée (pas de torsion extrême).</item>
    /// </list>
    /// Résultat : l'extrémité active se pose sur le point traité, à toute distance (dans la portée), sous tout angle de
    /// caméra et quelle que soit la hauteur des yeux — aucun décalage fixe.
    /// </summary>
    public sealed class ToolPose
    {
        readonly Transform model;
        readonly Transform tip;
        readonly Transform head;
        readonly Transform shaft;
        readonly Vector3 restPosition;
        readonly Quaternion restRotation;
        readonly Vector3 headRest;
        readonly Vector3 shaftRestScale;
        readonly float shaftLength;
        readonly float maxExtension;

        public float MaxAngle = 70f;
        public float Sharpness = 16f;

        public Quaternion AimLocal { get; private set; } = Quaternion.identity;
        public float Extension { get; private set; }
        /// <summary>Écart angulaire résiduel entre l'axe main → extrémité et la direction du point visé (degrés).</summary>
        public float AimErrorDegrees { get; private set; }
        /// <summary>Même écart sans correction (pose fixe d'origine), pour mesure.</summary>
        public float UnalignedErrorDegrees { get; private set; }
        public Vector3 RestPosition => restPosition;
        public Quaternion RestRotation => restRotation;
        public Transform Tip => tip;
        public bool HasReach => head != null && shaft != null && shaftLength > 1e-4f;

        /// <param name="model">Racine du modèle (pivot = main), dans sa pose de repos au moment de l'appel.</param>
        /// <param name="head">Groupe « tête » (enfant direct du modèle) déplacé le long du manche ; null = outil sans portée.</param>
        /// <param name="shaft">Manche (enfant direct du modèle), étiré le long de son axe Z local.</param>
        public ToolPose(Transform model, Transform tip, Transform head = null, Transform shaft = null, float shaftLength = 0f, float maxExtension = 0f)
        {
            this.model = model;
            this.tip = tip;
            this.head = head;
            this.shaft = shaft;
            this.shaftLength = shaftLength;
            this.maxExtension = Mathf.Max(0f, maxExtension);
            restPosition = model.localPosition;
            restRotation = model.localRotation;
            if (head != null) headRest = head.localPosition;
            if (shaft != null) shaftRestScale = shaft.localScale;
        }

        /// <summary>Remet le modèle, la tête et le manche dans leur pose de repos.</summary>
        public void ResetToRest()
        {
            model.localPosition = restPosition;
            model.localRotation = restRotation;
            if (!HasReach) return;
            head.localPosition = headRest;
            shaft.localScale = shaftRestScale;
        }

        public void ResetState()
        {
            AimLocal = Quaternion.identity;
            Extension = 0f;
        }

        /// <summary>
        /// Calcule et applique la portée (tête, manche), puis calcule la rotation de visée (<see cref="AimLocal"/>, non
        /// appliquée : l'appelant compose d'abord l'animation propre à l'outil, puis appelle <see cref="Apply"/>).
        /// Le modèle doit être dans sa pose de repos (<see cref="ResetToRest"/>).
        /// </summary>
        public void Solve(Vector3 target, float deltaTime, bool snap)
        {
            Transform parent = model.parent;
            if (tip == null || parent == null) return;
            Vector3 pivot = parent.TransformPoint(restPosition);
            float blend = snap || deltaTime <= 0f ? 1f : 1f - Mathf.Exp(-Sharpness * deltaTime);

            if (HasReach)
            {
                Vector3 axis = (shaft.localRotation * Vector3.forward).normalized;
                Vector3 tipRest = model.InverseTransformPoint(tip.position);
                float scale = Mathf.Max(1e-4f, model.lossyScale.x);
                float wanted = ToolReach.SolveExtension(tipRest, axis, Vector3.Distance(target, pivot) / scale, -0.5f * shaftLength, maxExtension);
                Extension = Mathf.Lerp(Extension, wanted, blend);
                head.localPosition = headRest + axis * Extension;
                shaft.localScale = new Vector3(shaftRestScale.x, shaftRestScale.y, shaftRestScale.z * (shaftLength + Extension) / shaftLength);
            }

            Vector3 current = tip.position - pivot;
            Vector3 desired = target - pivot;
            if (current.sqrMagnitude < 1e-8f || desired.sqrMagnitude < 1e-8f) return;
            UnalignedErrorDegrees = Vector3.Angle(current, desired);
            Quaternion local = Quaternion.Inverse(parent.rotation) * Quaternion.FromToRotation(current, desired) * parent.rotation;
            float angle = Quaternion.Angle(Quaternion.identity, local);
            if (angle > MaxAngle) local = Quaternion.Slerp(Quaternion.identity, local, MaxAngle / angle);
            AimLocal = blend >= 1f ? local : Quaternion.Slerp(AimLocal, local, blend);
            Quaternion applied = parent.rotation * AimLocal * Quaternion.Inverse(parent.rotation);
            AimErrorDegrees = Vector3.Angle(applied * current, desired);
        }

        /// <summary>Applique la rotation de visée autour de la main, par-dessus la pose (animée) actuelle du modèle.</summary>
        public void Apply()
        {
            model.localPosition = restPosition + AimLocal * (model.localPosition - restPosition);
            model.localRotation = AimLocal * model.localRotation;
        }
    }
}
