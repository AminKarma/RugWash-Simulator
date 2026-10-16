using System;
using System.Collections.Generic;
using RugWash.Cleaning;
using UnityEngine;

namespace RugWash.Network
{
    /// <summary>Types de messages de surface (valeurs figées : elles font partie du protocole).</summary>
    public static class SurfaceMessage
    {
        public const byte Delta = 1;
        public const byte Snapshot = 2;
        /// <summary>Surface retirée (aucun tapis posé) : l'invité retire aussi la sienne.</summary>
        public const byte Empty = 3;
    }

    /// <summary>Identité d'une surface chargée : définition et éventuelle commande (saleté de la commande).</summary>
    public readonly struct SurfaceIdentity
    {
        public readonly string DefinitionId;
        public readonly string OrderId;

        public SurfaceIdentity(string definitionId, string orderId)
        {
            DefinitionId = definitionId ?? string.Empty;
            OrderId = orderId ?? string.Empty;
        }
    }

    /// <summary>
    /// Côté hôte : applique les opérations reçues des invités sur les surfaces qui font autorité, puis diffuse les cellules
    /// modifiées (rectangles) et, après un rechargement de tapis, un instantané complet. Les accusés indiquent à chaque
    /// invité quelles opérations sont déjà comprises dans les valeurs envoyées.
    /// </summary>
    public sealed class SurfaceSyncHost
    {
        sealed class Entry
        {
            public byte Id;
            public RugCleaningController Surface;
            public int Epoch;
            public bool NeedsSnapshot = true;
            public Action<RugCleaningController> OnLoaded;
            /// <summary>Correction lente en cours : lignes restant à envoyer (par tranches).</summary>
            public RectInt SlowPending;
            public bool SlowActive;
        }

        readonly List<Entry> entries = new List<Entry>();
        readonly Func<byte, SurfaceIdentity> identify;

        public SurfaceAckTable Acks { get; } = new SurfaceAckTable();
        public int OperationsApplied { get; private set; }
        public int RejectedMessages { get; private set; }

        public SurfaceSyncHost(Func<byte, SurfaceIdentity> identify) => this.identify = identify;

        public void Register(byte id, RugCleaningController surface)
        {
            var entry = new Entry { Id = id, Surface = surface };
            entry.OnLoaded = _ => { entry.Epoch = (entry.Epoch + 1) & 0xFFFF; entry.NeedsSnapshot = true; };
            surface.RugLoaded += entry.OnLoaded;
            surface.RugUnloaded += entry.OnLoaded;
            entries.Add(entry);
        }

        public void Unregister()
        {
            foreach (var entry in entries)
                if (entry.Surface != null)
                {
                    entry.Surface.RugLoaded -= entry.OnLoaded;
                    entry.Surface.RugUnloaded -= entry.OnLoaded;
                }
            entries.Clear();
        }

        public RugCleaningController Get(byte id)
        {
            foreach (var entry in entries)
                if (entry.Id == id) return entry.Surface;
            return null;
        }

        public int EpochOf(byte id)
        {
            foreach (var entry in entries)
                if (entry.Id == id) return entry.Epoch;
            return -1;
        }

        /// <summary>
        /// Applique une opération envoyée par un invité. Un message invalide est ignoré (compté) sans rien modifier :
        /// un invité ne peut que demander ce qu'un outil réel ferait.
        /// </summary>
        public bool TryApply(ulong clientId, ArraySegment<byte> data, out SurfaceOperationCodec.Applied applied, Func<byte, byte> productFilter = null)
        {
            applied = default;
            try
            {
                var reader = new ByteReader(data);
                var header = SurfaceOperationCodec.ReadHeader(ref reader);
                var surface = Get(header.SurfaceId);
                if (surface == null || surface.Grid == null)
                {
                    RejectedMessages++;
                    return false;
                }
                applied = SurfaceOperationCodec.ReadAndApply(ref reader, header, surface, allowRunoff: true, productFilter);
                Acks.Record(clientId, header.Sequence);
                OperationsApplied++;
                return true;
            }
            catch (FormatException)
            {
                RejectedMessages++;
                return false;
            }
        }

        /// <summary>
        /// Écrit la mise à jour d'une surface : instantané après un rechargement, sinon cellules modifiées depuis le dernier
        /// envoi. La simulation lente (mousse qui retombe, séchage), reproduite par chaque invité, n'est ajoutée que si
        /// <paramref name="includeSlow"/> (correction à basse fréquence). Faux si rien n'est à envoyer.
        /// </summary>
        public bool TryWriteUpdate(int index, ByteWriter writer, bool includeSlow = true)
        {
            var entry = entries[index];
            if (entry.Surface.Grid == null)
            {
                if (!entry.NeedsSnapshot) return false;
                entry.NeedsSnapshot = false;
                WriteSnapshot(entry, writer);
                return true;
            }
            if (entry.NeedsSnapshot)
            {
                entry.NeedsSnapshot = false;
                entry.SlowActive = false;
                entry.Surface.Grid.TryConsumeNetworkDirtyRect(out _);
                entry.Surface.Grid.TryConsumeSlowNetworkRect(out _);
                WriteSnapshot(entry, writer);
                return true;
            }
            bool fast = entry.Surface.Grid.TryConsumeNetworkDirtyRect(out RectInt rect);
            if (includeSlow && entry.Surface.Grid.TryConsumeSlowNetworkRect(out RectInt slow))
            {
                if (!fast) rect = slow;
                else
                {
                    int xMin = Mathf.Min(rect.xMin, slow.xMin), yMin = Mathf.Min(rect.yMin, slow.yMin);
                    rect = new RectInt(xMin, yMin, Mathf.Max(rect.xMax, slow.xMax) - xMin, Mathf.Max(rect.yMax, slow.yMax) - yMin);
                }
                fast = true;
            }
            if (!fast) return false;
            writer.Byte(SurfaceMessage.Delta);
            writer.Byte(entry.Id);
            writer.UShort(entry.Epoch);
            Acks.Write(writer);
            SurfaceCellCodec.Write(writer, entry.Surface.Grid, rect);
            return true;
        }

        /// <summary>
        /// Mission de corrections (2.10) : démarre une correction lente de la surface (zone modifiée par la seule simulation
        /// lente depuis la précédente). Elle est ensuite envoyée par tranches de lignes (<see cref="TryWriteSlowChunk"/>) au
        /// lieu d'un seul message : avec beaucoup de mousse, la zone couvrait les grilles entières et leur compression prenait
        /// plus de 10 ms dans une seule image chez l'hôte (lecture et réencodage autant chez les invités).
        /// </summary>
        public void BeginSlowCorrection(int index)
        {
            var entry = entries[index];
            var grid = entry.Surface.Grid;
            if (grid == null || entry.NeedsSnapshot || !grid.TryConsumeSlowNetworkRect(out RectInt slow)) return;
            if (!entry.SlowActive)
            {
                entry.SlowPending = slow;
                entry.SlowActive = true;
                return;
            }
            // Cycle précédent pas fini (envois très lents) : la nouvelle zone s'y ajoute.
            var pending = entry.SlowPending;
            int xMin = Mathf.Min(pending.xMin, slow.xMin), yMin = Mathf.Min(pending.yMin, slow.yMin);
            entry.SlowPending = new RectInt(xMin, yMin, Mathf.Max(pending.xMax, slow.xMax) - xMin, Mathf.Max(pending.yMax, slow.yMax) - yMin);
        }

        /// <summary>
        /// Écrit la tranche suivante de la correction lente en cours (au plus <paramref name="cellBudget"/> cellules, au moins
        /// une ligne), avec les valeurs actuelles de l'hôte. Retourne le nombre de cellules écrites (0 : rien en cours).
        /// </summary>
        public int TryWriteSlowChunk(int index, ByteWriter writer, int cellBudget)
        {
            var entry = entries[index];
            var grid = entry.Surface.Grid;
            if (!entry.SlowActive || grid == null || entry.NeedsSnapshot) return 0;
            var pending = entry.SlowPending;
            int rows = Mathf.Clamp(cellBudget / Mathf.Max(1, pending.width), 1, pending.height);
            var chunk = new RectInt(pending.xMin, pending.yMin, pending.width, rows);
            entry.SlowPending = new RectInt(pending.xMin, pending.yMin + rows, pending.width, pending.height - rows);
            if (entry.SlowPending.height <= 0) entry.SlowActive = false;
            writer.Byte(SurfaceMessage.Delta);
            writer.Byte(entry.Id);
            writer.UShort(entry.Epoch);
            Acks.Write(writer);
            SurfaceCellCodec.Write(writer, grid, chunk);
            return chunk.width * chunk.height;
        }

        /// <summary>Une correction lente est en cours d'envoi pour cette surface.</summary>
        public bool SlowCorrectionPending(int index) => entries[index].SlowActive;

        public int SurfaceCount => entries.Count;

        /// <summary>Instantané complet d'une surface (invité qui arrive en cours de partie).</summary>
        public void WriteSnapshot(int index, ByteWriter writer) => WriteSnapshot(entries[index], writer);

        void WriteSnapshot(Entry entry, ByteWriter writer)
        {
            var grid = entry.Surface.Grid;
            if (grid == null)
            {
                writer.Byte(SurfaceMessage.Empty);
                writer.Byte(entry.Id);
                writer.UShort(entry.Epoch);
                Acks.Write(writer);
                return;
            }
            var identity = identify != null ? identify(entry.Id) : default;
            writer.Byte(SurfaceMessage.Snapshot);
            writer.Byte(entry.Id);
            writer.UShort(entry.Epoch);
            Acks.Write(writer);
            writer.String(identity.DefinitionId);
            writer.String(identity.OrderId);
            writer.Float(grid.InitialDirt);
            SurfaceCellCodec.Write(writer, grid, new RectInt(0, 0, grid.Width, grid.Height));
        }
    }

    /// <summary>
    /// Côté invité : transmet les opérations locales à l'hôte (et les garde pour le rejeu), puis applique les cellules et
    /// instantanés reçus. Une surface rechargée par l'hôte (nouvelle époque) est d'abord rechargée localement avec la même
    /// définition et la même commande, puis remplacée par les valeurs de l'hôte.
    /// </summary>
    public sealed class SurfaceSyncClient : ISurfaceReplication
    {
        sealed class Entry
        {
            public byte Id;
            public RugCleaningController Surface;
            public int Epoch = -1;
        }

        readonly List<Entry> entries = new List<Entry>();
        readonly ByteWriter writer = new ByteWriter(128);
        readonly Action<ArraySegment<byte>> sendToHost;
        readonly Func<byte, SurfaceIdentity, bool> loadSurface;

        public SurfacePredictor Predictor { get; } = new SurfacePredictor();
        public ulong LocalClientId { get; set; }
        public int MessagesApplied { get; private set; }
        public int RejectedMessages { get; private set; }

        /// <param name="sendToHost">Envoi fiable et ordonné d'un message à l'hôte.</param>
        /// <param name="loadSurface">Recharge localement une surface (définition, commande) avant un instantané d'une nouvelle époque.</param>
        public SurfaceSyncClient(Action<ArraySegment<byte>> sendToHost, Func<byte, SurfaceIdentity, bool> loadSurface)
        {
            this.sendToHost = sendToHost;
            this.loadSurface = loadSurface;
        }

        public void Register(byte id, RugCleaningController surface)
        {
            entries.Add(new Entry { Id = id, Surface = surface });
            surface.Replication = this;
        }

        public void Unregister()
        {
            foreach (var entry in entries)
                if (entry.Surface != null && entry.Surface.Replication == this) entry.Surface.Replication = null;
            entries.Clear();
            Predictor.Clear();
        }

        public bool IsReady(byte id)
        {
            var entry = Find(id);
            return entry != null && entry.Epoch >= 0;
        }

        Entry Find(byte id)
        {
            foreach (var entry in entries)
                if (entry.Id == id) return entry;
            return null;
        }

        Entry Find(RugCleaningController surface)
        {
            foreach (var entry in entries)
                if (entry.Surface == surface) return entry;
            return null;
        }

        public void Forward<T>(RugCleaningController surface, in SurfaceOpGeometry geometry, ref T operation) where T : struct, ICellOperation
        {
            var entry = Find(surface);
            if (entry == null || entry.Epoch < 0) return;
            uint sequence = Predictor.NextSequence();
            writer.Reset();
            // Coordonnées locales à la surface : indépendantes de sa position dans la scène de chaque joueur.
            var local = SurfaceOperationCodec.ToLocal(surface.transform, geometry);
            if (!SurfaceOperationCodec.TryWrite(writer, entry.Id, sequence, local, ref operation)) return;
            Predictor.Add(sequence, entry.Id, writer.ToArray());
            sendToHost?.Invoke(writer.Segment);
        }

        /// <summary>Applique un message de surface de l'hôte. Faux si le message est ignoré (époque périmée, invalide).</summary>
        public bool Receive(ArraySegment<byte> data)
        {
            try
            {
                var reader = new ByteReader(data);
                byte kind = reader.Byte();
                var entry = Find(reader.Byte());
                int epoch = reader.UShort();
                if (entry == null) return Reject();
                uint ack = SurfaceAckTable.ReadAckFor(ref reader, LocalClientId);

                if (kind == SurfaceMessage.Snapshot)
                {
                    var identity = new SurfaceIdentity(reader.String(), reader.String());
                    float initialDirt = reader.Float();
                    if (epoch != entry.Epoch)
                    {
                        if (loadSurface != null && !loadSurface(entry.Id, identity)) return Reject();
                        Predictor.Clear(entry.Id);
                        entry.Epoch = epoch;
                    }
                    if (entry.Surface.Grid == null) return Reject();
                    Predictor.Acknowledge(ack);
                    var rect = SurfaceCellCodec.Read(ref reader, entry.Surface.Grid);
                    entry.Surface.Grid.SetInitialDirt(initialDirt);
                    Predictor.Replay(entry.Id, entry.Surface, rect);
                }
                else if (kind == SurfaceMessage.Empty)
                {
                    if (epoch != entry.Epoch)
                    {
                        if (loadSurface != null && !loadSurface(entry.Id, new SurfaceIdentity(string.Empty, string.Empty))) return Reject();
                        Predictor.Clear(entry.Id);
                        entry.Epoch = epoch;
                    }
                    Predictor.Acknowledge(ack);
                }
                else if (kind == SurfaceMessage.Delta)
                {
                    if (epoch != entry.Epoch || entry.Surface.Grid == null) return false;
                    Predictor.Acknowledge(ack);
                    var rect = SurfaceCellCodec.Read(ref reader, entry.Surface.Grid);
                    Predictor.Replay(entry.Id, entry.Surface, rect);
                }
                else return Reject();
                MessagesApplied++;
                return true;
            }
            catch (FormatException)
            {
                return Reject();
            }
        }

        bool Reject()
        {
            RejectedMessages++;
            return false;
        }
    }
}
