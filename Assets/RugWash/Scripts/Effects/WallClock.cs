using System;
using UnityEngine;

namespace RugWash.Effects
{
    /// <summary>Horloge murale indiquant l'heure réelle du PC (décor animé).</summary>
    public sealed class WallClock : MonoBehaviour
    {
        [SerializeField] Transform hourHand;
        [SerializeField] Transform minuteHand;
        [SerializeField] Transform secondHand;

        int lastSecond = -1;

        void Update()
        {
            var now = DateTime.Now;
            if (now.Second == lastSecond) return;
            lastSecond = now.Second;
            Apply(now);
        }

        public void Apply(DateTime time)
        {
            float minutes = time.Minute + time.Second / 60f;
            float hours = time.Hour % 12 + minutes / 60f;
            // Aiguilles tournant dans le plan du cadran (axe Z local), sens horaire vu de face.
            if (hourHand != null) hourHand.localRotation = Quaternion.Euler(0f, 0f, -hours * 30f);
            if (minuteHand != null) minuteHand.localRotation = Quaternion.Euler(0f, 0f, -minutes * 6f);
            if (secondHand != null) secondHand.localRotation = Quaternion.Euler(0f, 0f, -time.Second * 6f);
        }
    }
}
