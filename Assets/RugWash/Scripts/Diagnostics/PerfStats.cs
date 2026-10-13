using System.Diagnostics;

namespace RugWash.Diagnostics
{
    /// <summary>
    /// Chronomètres légers des systèmes de nettoyage (opérations sur les grilles, simulation lente, encodage et envoi des
    /// textures, ruissellement, réplication réseau). Désactivés par défaut : un appel coûte alors un seul test booléen.
    /// Le benchmark du jeu compilé les active pour attribuer le temps de calcul de chaque scénario (mousse, eau…).
    /// Fonctionne aussi dans un build sans profileur (horloge haute résolution).
    /// </summary>
    public static class PerfStats
    {
        public enum Slot
        {
            /// <summary>Opérations des outils sur les grilles (disque, éventail).</summary>
            GridOps,
            /// <summary>Simulation lente des surfaces : mousse qui travaille puis retombe, séchage.</summary>
            SurfaceAging,
            /// <summary>Encodage des cellules modifiées dans les textures de contrôle.</summary>
            RendererEncode,
            /// <summary>Envoi des textures de contrôle au GPU.</summary>
            RendererUpload,
            /// <summary>Dépôt du ruissellement sur l'aire de lavage.</summary>
            Runoff,
            /// <summary>Hôte : écriture des mises à jour de surfaces (cellules, compression).</summary>
            NetWrite,
            /// <summary>Invité : lecture des mises à jour de surfaces (décompression, rejeu).</summary>
            NetRead,
            Count,
        }

        static readonly long[] ticks = new long[(int)Slot.Count];
        static readonly int[] calls = new int[(int)Slot.Count];
        static readonly long[] cells = new long[(int)Slot.Count];

        public static bool Enabled;

        /// <summary>Horodatage de début (0 si désactivé).</summary>
        public static long Begin() => Enabled ? Stopwatch.GetTimestamp() : 0;

        public static void End(Slot slot, long start, int processed = 0)
        {
            if (start == 0) return;
            ticks[(int)slot] += Stopwatch.GetTimestamp() - start;
            calls[(int)slot]++;
            cells[(int)slot] += processed;
        }

        public static void Reset()
        {
            for (int i = 0; i < ticks.Length; i++)
            {
                ticks[i] = 0;
                calls[i] = 0;
                cells[i] = 0;
            }
        }

        public static double Milliseconds(Slot slot) => ticks[(int)slot] * 1000.0 / Stopwatch.Frequency;
        public static int Calls(Slot slot) => calls[(int)slot];
        /// <summary>Cellules (ou octets pour le réseau) traitées.</summary>
        public static long Processed(Slot slot) => cells[(int)slot];
    }
}
