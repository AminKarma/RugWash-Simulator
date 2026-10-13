using RugWash.Cleaning;
using RugWash.Workshop;
using UnityEngine;

namespace RugWash.Save
{
    /// <summary>
    /// Fait le lien entre la sauvegarde et l'atelier : état du robot aspirateur, du plan de travail et du sol.
    /// Appelé par <see cref="SaveManager"/> au chargement et à chaque sauvegarde.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorkshopStateService : MonoBehaviour
    {
        [SerializeField] RugCleaningController workSurface;
        [SerializeField] RugCleaningController floor;
        [SerializeField] RobotVacuum robot;

        /// <summary>Sol de la salle (hors zone de nettoyage).</summary>
        public RugCleaningController RoomFloor => floor;

        public bool IsConfigured(out string missing)
        {
            missing = workSurface == null ? nameof(workSurface) : floor == null ? nameof(floor) : robot == null ? nameof(robot) : null;
            return missing == null;
        }

        public void Capture(WorkshopSaveData data)
        {
            if (data == null) return;
            if (workSurface != null && workSurface.Grid != null) data.workSurface = SurfaceStateCodec.Encode(workSurface.Grid);
            // Sol hors zone de nettoyage : rien à conserver (il ne se salit plus).
            data.floor = floor != null && floor.Grid != null && floor.AcceptsDirt ? SurfaceStateCodec.Encode(floor.Grid) : string.Empty;
            if (robot == null) return;
            var state = robot.CaptureState();
            data.hasRobot = true;
            data.robotMode = state.mode;
            data.robotBattery = state.battery;
            data.robotResume = state.resumeAfterCharge;
            data.robotX = state.x;
            data.robotZ = state.z;
            data.robotYaw = state.yaw;
        }

        /// <summary>Restaure l'atelier. Une donnée absente ou illisible laisse l'élément dans son état initial.</summary>
        public void Restore(WorkshopSaveData data)
        {
            if (data == null) return;
            if (workSurface != null && workSurface.Grid != null && !string.IsNullOrEmpty(data.workSurface)
                && !SurfaceStateCodec.TryDecode(data.workSurface, workSurface.Grid))
                Debug.LogWarning("[RugWash] État du plan de travail illisible : plan de travail propre.");
            // Ancienne sauvegarde (v0.1.1) : le sol de la salle pouvait être sale ; il est hors zone de nettoyage désormais,
            // son état sauvegardé est ignoré (sol propre).
            if (floor != null && floor.Grid != null && floor.AcceptsDirt && !string.IsNullOrEmpty(data.floor)
                && !SurfaceStateCodec.TryDecode(data.floor, floor.Grid))
                Debug.LogWarning("[RugWash] État du sol illisible : sol dans son état initial.");
            if (robot != null && data.hasRobot)
                robot.ApplyState(new RobotState
                {
                    mode = data.robotMode,
                    battery = data.robotBattery,
                    resumeAfterCharge = data.robotResume,
                    x = data.robotX,
                    z = data.robotZ,
                    yaw = data.robotYaw,
                });
        }
    }
}
