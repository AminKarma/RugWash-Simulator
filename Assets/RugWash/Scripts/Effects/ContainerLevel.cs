using RugWash.Cleaning;
using RugWash.Player;
using UnityEngine;

namespace RugWash.Effects
{
    /// <summary>
    /// Niveau de liquide visible dans un contenant de l'outil (pulvérisateur, bouteille du canon à mousse) : la valeur du
    /// réservoir réel du joueur est transmise au shader « RugWash/Liquid Container » (propriété _Fill), lissée, et seulement
    /// quand elle change (aucun coût au repos). Grande mise à jour : le liquide est un volume dont la surface reste à plat
    /// (plan horizontal du monde) et ballotte un peu quand l'outil bouge (_Wobble, ressort amorti).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ContainerLevel : MonoBehaviour
    {
        static readonly int FillId = Shader.PropertyToID("_Fill");
        static readonly int WobbleId = Shader.PropertyToID("_Wobble");

        [SerializeField] Renderer target;
        [SerializeField] PlayerEquipment equipment;
        [SerializeField] ToolResource resource = ToolResource.Soap;
        [SerializeField, Range(1f, 30f)] float smoothing = 10f;
        [Header("Ballottement")]
        [Tooltip("Inclinaison de la surface par m/s² d'accélération de l'outil.")]
        [SerializeField, Range(0f, 0.2f)] float wobblePerAcceleration = 0.025f;
        [SerializeField, Range(1f, 30f)] float wobbleStiffness = 14f;
        [Tooltip("Vitesse du ballottement conservée après une seconde (amortissement).")]
        [SerializeField, Range(0.001f, 1f)] float wobbleDamping = 0.05f;
        [SerializeField, Range(0f, 1f)] float maxWobble = 0.35f;

        MaterialPropertyBlock block;
        float shown = -1f;
        Vector3 lastPosition, lastVelocity;
        Vector2 wobble, wobbleSpeed;
        bool tracking;

        public float ShownFill => shown;
        public Vector2 Wobble => wobble;

        void LateUpdate()
        {
            if (target == null || equipment == null) return;
            var tank = equipment.Get(resource);
            if (tank == null) return;
            float wanted = Mathf.Clamp01(tank.Fraction01);
            bool moved = UpdateWobble(Time.unscaledDeltaTime);
            // Au repos (niveau atteint, liquide immobile) : rien à envoyer. L'écart se mesure à la cible, pas au pas de l'image :
            // à fréquence d'image élevée, un pas minuscule ne doit pas figer le niveau loin de la valeur réelle.
            if (shown >= 0f && Mathf.Abs(wanted - shown) < 0.0005f && !moved) return;
            float next = shown < 0f ? wanted : Mathf.Lerp(shown, wanted, 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime));
            if (Mathf.Abs(wanted - next) < 0.0005f) next = wanted;
            shown = next;
            block ??= new MaterialPropertyBlock();
            target.GetPropertyBlock(block);
            block.SetFloat(FillId, shown);
            block.SetVector(WobbleId, new Vector4(wobble.x, wobble.y, 0f, 0f));
            target.SetPropertyBlock(block);
        }

        /// <summary>Ressort amorti piloté par l'accélération horizontale du contenant ; vrai tant que le liquide bouge.</summary>
        bool UpdateWobble(float dt)
        {
            Vector3 position = target.transform.position;
            if (!tracking || dt <= 1e-5f)
            {
                tracking = true;
                lastPosition = position;
                lastVelocity = Vector3.zero;
                return false;
            }
            Vector3 velocity = (position - lastPosition) / dt;
            Vector3 acceleration = Vector3.ClampMagnitude((velocity - lastVelocity) / dt, 30f);
            lastPosition = position;
            lastVelocity = velocity;
            // Le liquide « retarde » : il penche à l'opposé de l'accélération, puis oscille (ressort) et se calme (amorti).
            Vector2 push = -new Vector2(acceleration.x, acceleration.z) * wobblePerAcceleration;
            wobbleSpeed += (push - wobble) * (wobbleStiffness * wobbleStiffness * dt);
            wobbleSpeed *= Mathf.Pow(wobbleDamping, dt);
            wobble = Vector2.ClampMagnitude(wobble + wobbleSpeed * dt, maxWobble);
            bool moving = wobble.sqrMagnitude > 1e-6f || wobbleSpeed.sqrMagnitude > 1e-6f;
            if (!moving) wobble = wobbleSpeed = Vector2.zero;
            return moving;
        }
    }
}
