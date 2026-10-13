using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>
    /// Simulation lente répartie sur les images. Avant : toute la grille était vieillie d'un coup toutes les
    /// <see cref="CleaningTuning.agingInterval"/> secondes — plusieurs millisecondes d'un seul tenant quand la mousse couvre le
    /// tapis, puis un réencodage complet des textures et, en ligne, l'envoi de toute la grille. Maintenant, quelques lignes
    /// sont vieillies à chaque image (la grille entière une fois par intervalle) et chaque ligne reçoit exactement le temps
    /// écoulé depuis son dernier passage : même résultat, coût par image petit et constant. Classe pure (testée en EditMode).
    /// </summary>
    public sealed class SurfaceAgingScheduler
    {
        float[] rowStamp;
        float clock, budget;
        int cursor;
        bool idle = true;

        /// <summary>Lignes vieillies lors du dernier appel.</summary>
        public int RowsLastAdvance { get; private set; }

        /// <summary>Repart de zéro (nouveau tapis).</summary>
        public void Reset()
        {
            idle = true;
            budget = 0f;
            cursor = 0;
            RowsLastAdvance = 0;
        }

        /// <summary>
        /// Avance de <paramref name="deltaTime"/> et vieillit les lignes dues. <paramref name="template"/> porte les réglages de
        /// l'opération ; ses compteurs (<see cref="SurfaceAgingOperation.Loosened"/>, <see cref="SurfaceAgingOperation.FoamLost"/>)
        /// reçoivent la somme de l'appel. Sans rien à simuler (ni mousse ni eau qui sèche), rien n'est calculé ; à la reprise,
        /// aucune ligne ne rattrape le temps passé au repos. Retourne le nombre de cellules modifiées.
        /// </summary>
        public int Advance(RugCleaningGrid grid, float deltaTime, float period, bool hasWork, ref SurfaceAgingOperation template,
            bool slowReplication)
        {
            RowsLastAdvance = 0;
            if (grid == null || deltaTime <= 0f) return 0;
            clock += deltaTime;
            int height = grid.Height;
            if (rowStamp == null || rowStamp.Length != height)
            {
                rowStamp = new float[height];
                idle = true;
            }
            if (!hasWork)
            {
                idle = true;
                return 0;
            }
            if (idle)
            {
                for (int y = 0; y < height; y++) rowStamp[y] = clock;
                idle = false;
                budget = 0f;
            }

            budget = Mathf.Min(height, budget + height * deltaTime / Mathf.Max(0.01f, period));
            int rows = (int)budget;
            budget -= rows;
            int changed = 0;
            float loosened = 0f, foamLost = 0f;
            for (int i = 0; i < rows; i++)
            {
                int y = cursor;
                cursor = cursor + 1 >= height ? 0 : cursor + 1;
                var operation = template;
                operation.DeltaTime = clock - rowStamp[y];
                operation.Loosened = 0f;
                operation.FoamLost = 0f;
                rowStamp[y] = clock;
                changed += grid.ApplyRowTracked(y, ref operation, slowReplication);
                loosened += operation.Loosened;
                foamLost += operation.FoamLost;
            }
            template.Loosened += loosened;
            template.FoamLost += foamLost;
            RowsLastAdvance = rows;
            return changed;
        }
    }
}
