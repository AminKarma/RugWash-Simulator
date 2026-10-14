using RugWash.Cleaning;
using RugWash.Player;
using UnityEngine;

namespace RugWash.Effects
{
    /// <summary>
    /// Niveau de liquide visible dans un contenant de l'outil (pulvérisateur, bouteille du canon à mousse) : la valeur du
    /// réservoir réel du joueur est transmise au shader « RugWash/Liquid Container » (propriété _Fill), lissée, et seulement
    /// quand elle change (aucun coût au repos).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ContainerLevel : MonoBehaviour
    {
        static readonly int FillId = Shader.PropertyToID("_Fill");

        [SerializeField] Renderer target;
        [SerializeField] PlayerEquipment equipment;
        [SerializeField] ToolResource resource = ToolResource.Soap;
        [SerializeField, Range(1f, 30f)] float smoothing = 10f;

        MaterialPropertyBlock block;
        float shown = -1f;

        public float ShownFill => shown;

        void LateUpdate()
        {
            if (target == null || equipment == null) return;
            var tank = equipment.Get(resource);
            if (tank == null) return;
            float wanted = Mathf.Clamp01(tank.Fraction01);
            // Au repos (niveau atteint) : rien à envoyer. L'écart se mesure à la cible, pas au pas de l'image : à fréquence
            // d'image élevée, un pas minuscule ne doit pas figer le niveau loin de la valeur réelle.
            if (shown >= 0f && Mathf.Abs(wanted - shown) < 0.0005f) return;
            float next = shown < 0f ? wanted : Mathf.Lerp(shown, wanted, 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime));
            if (Mathf.Abs(wanted - next) < 0.0005f) next = wanted;
            shown = next;
            block ??= new MaterialPropertyBlock();
            target.GetPropertyBlock(block);
            block.SetFloat(FillId, shown);
            target.SetPropertyBlock(block);
        }
    }
}
