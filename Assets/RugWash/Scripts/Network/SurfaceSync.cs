using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using RugWash.Cleaning;
using UnityEngine;

namespace RugWash.Network
{
    /// <summary>
    /// Cellules d'un rectangle de grille, quantifiées sur un octet par valeur (11 canaux) et rangées canal par canal ;
    /// compressées (Deflate) au-delà de quelques centaines d'octets. C'est ce que l'hôte envoie aux invités.
    /// </summary>
    public static class SurfaceCellCodec
    {
        public const int Channels = 11;
        const int CompressAbove = 384;

        public static void Write(ByteWriter writer, RugCleaningGrid grid, RectInt rect)
        {
            rect = Clip(rect, grid);
            writer.UShort(rect.x);
            writer.UShort(rect.y);
            writer.UShort(rect.width);
            writer.UShort(rect.height);
            int count = rect.width * rect.height;
            var raw = new byte[count * Channels];
            int i = 0;
            for (int y = rect.yMin; y < rect.yMax; y++)
            for (int x = rect.xMin; x < rect.xMax; x++, i++)
            {
                var cell = grid.GetCell(x, y);
                raw[i] = Q(cell.Dust);
                raw[count + i] = Q(cell.Stain);
                raw[count * 2 + i] = Q(cell.Loosened * 0.5f);
                raw[count * 3 + i] = Q(cell.Foam);
                raw[count * 4 + i] = Q(cell.Moisture);
                raw[count * 5 + i] = Q(cell.Residue);
                raw[count * 6 + i] = Q(cell.Wear);
                raw[count * 7 + i] = Q(cell.Adhesion);
                raw[count * 8 + i] = (byte)cell.DustType;
                raw[count * 9 + i] = (byte)cell.StainType;
                raw[count * 10 + i] = cell.Product;
            }
            if (raw.Length > CompressAbove)
            {
                using var output = new MemoryStream();
                using (var deflate = new DeflateStream(output, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
                    deflate.Write(raw, 0, raw.Length);
                writer.Bool(true);
                var packed = output.ToArray();
                writer.UInt((uint)packed.Length);
                writer.Bytes(packed, 0, packed.Length);
            }
            else
            {
                writer.Bool(false);
                writer.UInt((uint)raw.Length);
                writer.Bytes(raw, 0, raw.Length);
            }
        }

        /// <summary>Réécrit les cellules du rectangle. Retourne le rectangle appliqué ; lève FormatException si le message est invalide.</summary>
        public static RectInt Read(ref ByteReader reader, RugCleaningGrid grid)
        {
            var rect = new RectInt(reader.UShort(), reader.UShort(), reader.UShort(), reader.UShort());
            bool compressed = reader.Bool();
            int length = (int)reader.UInt();
            if (rect.width <= 0 || rect.height <= 0 || rect.xMax > grid.Width || rect.yMax > grid.Height)
                throw new FormatException("Zone de grille hors limites.");
            if (length < 0 || length > reader.Remaining) throw new FormatException("Message de grille tronqué.");
            int count = rect.width * rect.height;
            var payload = new byte[length];
            reader.Bytes(payload, 0, length);
            byte[] raw;
            if (compressed)
            {
                raw = new byte[count * Channels];
                using var input = new MemoryStream(payload);
                using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                int read = 0;
                while (read < raw.Length)
                {
                    int n = deflate.Read(raw, read, raw.Length - read);
                    if (n <= 0) throw new FormatException("Données de grille incomplètes.");
                    read += n;
                }
            }
            else raw = payload;
            if (raw.Length != count * Channels) throw new FormatException("Taille de données de grille incohérente.");

            int i = 0;
            for (int y = rect.yMin; y < rect.yMax; y++)
            for (int x = rect.xMin; x < rect.xMax; x++, i++)
            {
                grid.SetCell(x, y, new RugCell
                {
                    Dust = F(raw[i]),
                    Stain = F(raw[count + i]),
                    Loosened = F(raw[count * 2 + i]) * 2f,
                    Foam = F(raw[count * 3 + i]),
                    Moisture = F(raw[count * 4 + i]),
                    Residue = F(raw[count * 5 + i]),
                    Wear = F(raw[count * 6 + i]),
                    Adhesion = F(raw[count * 7 + i]),
                    DustType = Type(raw[count * 8 + i]),
                    StainType = Type(raw[count * 9 + i]),
                    Product = raw[count * 10 + i],
                });
            }
            return rect;
        }

        static RectInt Clip(RectInt rect, RugCleaningGrid grid)
        {
            int xMin = Mathf.Clamp(rect.xMin, 0, grid.Width), yMin = Mathf.Clamp(rect.yMin, 0, grid.Height);
            int xMax = Mathf.Clamp(rect.xMax, xMin, grid.Width), yMax = Mathf.Clamp(rect.yMax, yMin, grid.Height);
            return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        static byte Q(float v) => (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);
        static float F(byte b) => b / 255f;
        static DirtType Type(byte b) => b < DirtTypes.Count ? (DirtType)b : DirtType.None;
    }

    /// <summary>
    /// Hôte : dernier numéro de séquence appliqué pour chaque invité. Joint à chaque envoi de cellules, il indique aux
    /// invités quelles opérations prédites sont déjà comprises dans les valeurs reçues.
    /// </summary>
    public sealed class SurfaceAckTable
    {
        readonly Dictionary<ulong, uint> applied = new Dictionary<ulong, uint>();

        public int Count => applied.Count;

        public void Record(ulong clientId, uint sequence)
        {
            if (!applied.TryGetValue(clientId, out uint last) || sequence > last) applied[clientId] = sequence;
        }

        public uint LastApplied(ulong clientId) => applied.TryGetValue(clientId, out uint last) ? last : 0u;

        public void Remove(ulong clientId) => applied.Remove(clientId);

        public void Write(ByteWriter writer)
        {
            writer.Byte((byte)Mathf.Min(applied.Count, 255));
            int written = 0;
            foreach (var pair in applied)
            {
                if (written++ >= 255) break;
                writer.ULong(pair.Key);
                writer.UInt(pair.Value);
            }
        }

        /// <summary>Lit la table et retourne l'accusé concernant <paramref name="localClientId"/> (0 si absent).</summary>
        public static uint ReadAckFor(ref ByteReader reader, ulong localClientId)
        {
            int count = reader.Byte();
            uint ack = 0;
            for (int i = 0; i < count; i++)
            {
                ulong id = reader.ULong();
                uint sequence = reader.UInt();
                if (id == localClientId) ack = sequence;
            }
            return ack;
        }
    }

    /// <summary>
    /// Invité : opérations envoyées mais pas encore confirmées par l'hôte. Quand des cellules arrivent, elles remplacent
    /// les valeurs locales, puis les opérations non confirmées sont rejouées sur cette seule zone : l'effet de chaque
    /// opération est compté une fois, la prédiction ne « recule » pas quand l'hôte est en retard.
    /// </summary>
    public sealed class SurfacePredictor
    {
        /// <summary>Au-delà, les plus anciennes sont oubliées (hôte muet) : la grille finira alignée sur l'hôte.</summary>
        public const int MaxPending = 512;

        readonly List<(uint sequence, byte surface, byte[] packet)> pending = new List<(uint, byte, byte[])>();
        uint nextSequence;

        public int PendingCount => pending.Count;
        public uint LastSequence => nextSequence;

        public uint NextSequence() => ++nextSequence;

        public void Add(uint sequence, byte surfaceId, byte[] packet)
        {
            if (pending.Count >= MaxPending) pending.RemoveAt(0);
            pending.Add((sequence, surfaceId, packet));
        }

        /// <summary>Oublie les opérations comprises dans les valeurs de l'hôte (séquence ≤ accusé).</summary>
        public void Acknowledge(uint acknowledged)
        {
            int keep = 0;
            while (keep < pending.Count && pending[keep].sequence <= acknowledged) keep++;
            if (keep > 0) pending.RemoveRange(0, keep);
        }

        /// <summary>Rejoue sur <paramref name="rect"/> les opérations non confirmées visant cette surface.</summary>
        public int Replay(byte surfaceId, RugCleaningController surface, RectInt rect)
        {
            if (surface.Grid == null || pending.Count == 0) return 0;
            int replayed = 0;
            surface.Grid.SetClip(rect);
            try
            {
                foreach (var entry in pending)
                {
                    if (entry.surface != surfaceId) continue;
                    var reader = new ByteReader(entry.packet);
                    var header = SurfaceOperationCodec.ReadHeader(ref reader);
                    SurfaceOperationCodec.ReadAndApply(ref reader, header, surface, allowRunoff: false);
                    replayed++;
                }
            }
            finally
            {
                surface.Grid.ClearClip();
            }
            return replayed;
        }

        public void Clear(byte? surfaceId = null)
        {
            if (surfaceId == null) pending.Clear();
            else pending.RemoveAll(entry => entry.surface == surfaceId.Value);
        }
    }
}
