using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>Où et comment une opération touche une surface (disque ou éventail, ruissellement).</summary>
    public readonly struct SurfaceOpGeometry
    {
        public readonly Vector3 Point;
        public readonly float Radius;
        public readonly float Hardness;
        public readonly float Aspect;
        public readonly Vector3 Axis;
        public readonly RunoffProfile Runoff;

        public SurfaceOpGeometry(Vector3 point, float radius, float hardness, float aspect, Vector3 axis, in RunoffProfile runoff)
        {
            Point = point;
            Radius = radius;
            Hardness = hardness;
            Aspect = aspect;
            Axis = axis;
            Runoff = runoff;
        }
    }

    /// <summary>
    /// Surface répliquée (multijoueur, côté invité) : chaque opération locale est aussi transmise à l'hôte, qui fait
    /// autorité. L'invité l'applique tout de suite (prédiction) puis se recale sur les cellules renvoyées par l'hôte.
    /// </summary>
    public interface ISurfaceReplication
    {
        void Forward<T>(RugCleaningController surface, in SurfaceOpGeometry geometry, ref T operation) where T : struct, ICellOperation;
    }
}
