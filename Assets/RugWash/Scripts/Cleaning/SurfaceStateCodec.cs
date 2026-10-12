using System;
using System.IO;
using System.IO.Compression;

namespace RugWash.Cleaning
{
    /// <summary>
    /// Encodage compact d'une grille de surface pour la sauvegarde : 10 canaux quantifiés sur un octet, rangés canal par
    /// canal (les zones propres forment de longues suites de zéros), compressés (Deflate) puis en Base64.
    /// Une grille de dimensions différentes est refusée : jamais de lecture partielle.
    /// </summary>
    public static class SurfaceStateCodec
    {
        const byte FormatVersion = 2;
        const int Channels = 10;
        /// <summary>Format 1 (jalon 2) : sans adhérence ni produit ; toujours lisible.</summary>
        const int ChannelsV1 = 8;

        public static string Encode(RugCleaningGrid grid)
        {
            if (grid == null) return string.Empty;
            int count = grid.CellCount;
            var raw = new byte[5 + count * Channels];
            raw[0] = FormatVersion;
            raw[1] = (byte)(grid.Width & 0xFF);
            raw[2] = (byte)(grid.Width >> 8);
            raw[3] = (byte)(grid.Height & 0xFF);
            raw[4] = (byte)(grid.Height >> 8);
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                int i = y * grid.Width + x;
                var cell = grid.GetCell(x, y);
                raw[5 + i] = Quantize(cell.Dust);
                raw[5 + count + i] = Quantize(cell.Stain);
                raw[5 + count * 2 + i] = Quantize(cell.Loosened * 0.5f);
                raw[5 + count * 3 + i] = Quantize(cell.Foam);
                raw[5 + count * 4 + i] = Quantize(cell.Moisture);
                raw[5 + count * 5 + i] = Quantize(cell.Residue);
                raw[5 + count * 6 + i] = (byte)cell.DustType;
                raw[5 + count * 7 + i] = (byte)cell.StainType;
                raw[5 + count * 8 + i] = Quantize(cell.Adhesion);
                raw[5 + count * 9 + i] = cell.Product;
            }

            using var output = new MemoryStream();
            using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
                deflate.Write(raw, 0, raw.Length);
            return Convert.ToBase64String(output.ToArray());
        }

        /// <summary>Restaure la grille. Faux (grille inchangée) si les données sont vides, corrompues ou d'une autre taille.</summary>
        public static bool TryDecode(string encoded, RugCleaningGrid grid)
        {
            if (string.IsNullOrEmpty(encoded) || grid == null) return false;
            byte[] raw;
            try
            {
                using var input = new MemoryStream(Convert.FromBase64String(encoded));
                using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                deflate.CopyTo(output);
                raw = output.ToArray();
            }
            catch (Exception)
            {
                return false;
            }

            int count = grid.CellCount;
            int channels = raw.Length > 0 && raw[0] == 1 ? ChannelsV1 : Channels;
            if (raw.Length != 5 + count * channels || (raw[0] != 1 && raw[0] != FormatVersion)) return false;
            int width = raw[1] | (raw[2] << 8), height = raw[3] | (raw[4] << 8);
            if (width != grid.Width || height != grid.Height) return false;

            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                int i = y * grid.Width + x;
                grid.SetCell(x, y, new RugCell
                {
                    Dust = raw[5 + i] / 255f,
                    Stain = raw[5 + count + i] / 255f,
                    Loosened = raw[5 + count * 2 + i] / 255f * 2f,
                    Foam = raw[5 + count * 3 + i] / 255f,
                    Moisture = raw[5 + count * 4 + i] / 255f,
                    Residue = raw[5 + count * 5 + i] / 255f,
                    DustType = ToType(raw[5 + count * 6 + i]),
                    StainType = ToType(raw[5 + count * 7 + i]),
                    Adhesion = channels > 8 ? raw[5 + count * 8 + i] / 255f : 0f,
                    Product = channels > 9 ? raw[5 + count * 9 + i] : (byte)0,
                });
            }
            grid.RecomputeTotals();
            return true;
        }

        static byte Quantize(float value) => (byte)(Math.Max(0f, Math.Min(1f, value)) * 255f + 0.5f);

        static DirtType ToType(byte value) => value < DirtTypes.Count ? (DirtType)value : DirtType.None;
    }
}
