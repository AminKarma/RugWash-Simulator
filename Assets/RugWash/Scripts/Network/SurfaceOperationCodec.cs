using System;
using RugWash.Cleaning;
using UnityEngine;

namespace RugWash.Network
{
    /// <summary>Types d'opérations transmissibles (valeurs figées : elles font partie du protocole).</summary>
    public enum SurfaceOpType : byte
    {
        Brush = 1,
        Rinse = 2,
        Vacuum = 3,
        Soap = 4,
        Extract = 5,
        Sweep = 6,
        Deposit = 7,
        /// <summary>Raclette (v0.1.2).</summary>
        Gather = 8,
        /// <summary>Raclette sur un tapis : eau seulement (mission de corrections).</summary>
        Wipe = 9,
    }

    /// <summary>
    /// Requête d'opération d'un invité : surface, numéro de séquence, géométrie et paramètres de l'opération.
    /// Seuls les paramètres sont transmis (jamais les statistiques de sortie) ; les références (table des saletés,
    /// produits, fragilité) sont reprises de la surface de l'hôte à l'arrivée. Une opération inconnue est refusée.
    /// </summary>
    public static class SurfaceOperationCodec
    {
        /// <summary>Écrit une requête complète. Faux si le type d'opération n'est pas transmissible.</summary>
        public static bool TryWrite<T>(ByteWriter writer, byte surfaceId, uint sequence, in SurfaceOpGeometry geometry, ref T operation)
            where T : struct, ICellOperation
        {
            if (!TryGetType(operation, out SurfaceOpType type)) return false;
            writer.Byte(surfaceId);
            writer.UInt(sequence);
            writer.Vector3(geometry.Point);
            writer.Float(geometry.Radius);
            writer.Float(geometry.Hardness);
            writer.Float(geometry.Aspect);
            writer.Vector3(geometry.Axis);
            writer.Float(geometry.Runoff.Dirt);
            writer.Float(geometry.Runoff.Foam);
            writer.Float(geometry.Runoff.Water);
            writer.Bool(geometry.Runoff.DirtIsDry);
            writer.Byte((byte)type);
            WriteParameters(writer, type, operation);
            return true;
        }

        static bool TryGetType<T>(in T operation, out SurfaceOpType type) where T : struct, ICellOperation
        {
            switch ((object)operation)
            {
                case BrushOperation _: type = SurfaceOpType.Brush; return true;
                case RinseOperation _: type = SurfaceOpType.Rinse; return true;
                case VacuumOperation _: type = SurfaceOpType.Vacuum; return true;
                case SoapOperation _: type = SurfaceOpType.Soap; return true;
                case ExtractOperation _: type = SurfaceOpType.Extract; return true;
                case SweepOperation _: type = SurfaceOpType.Sweep; return true;
                case DepositOperation _: type = SurfaceOpType.Deposit; return true;
                case GatherOperation _: type = SurfaceOpType.Gather; return true;
                case WipeWaterOperation _: type = SurfaceOpType.Wipe; return true;
                default: type = 0; return false;
            }
        }

        static void WriteParameters<T>(ByteWriter w, SurfaceOpType type, in T operation) where T : struct, ICellOperation
        {
            object boxed = operation;
            switch (type)
            {
                case SurfaceOpType.Brush:
                    var brush = (BrushOperation)boxed;
                    w.Float(brush.Power); w.Float(brush.DeltaTime); w.Bool(brush.FiberWearEnabled); w.Float(brush.WearPerSecond);
                    w.Float(brush.StubbornBonus); w.Float(brush.LatherPerSecond);
                    break;
                case SurfaceOpType.Rinse:
                    var rinse = (RinseOperation)boxed;
                    w.Float(rinse.Power); w.Float(rinse.WettingPerSecond); w.Float(rinse.DeltaTime);
                    break;
                case SurfaceOpType.Vacuum:
                    var vacuum = (VacuumOperation)boxed;
                    w.Float(vacuum.Power); w.Float(vacuum.DeltaTime); w.Float(vacuum.FiberBonus);
                    break;
                case SurfaceOpType.Soap:
                    var soap = (SoapOperation)boxed;
                    w.Float(soap.FoamPerSecond); w.Float(soap.WettingPerSecond); w.Float(soap.DeltaTime); w.Float(soap.ResiduePerFoam);
                    w.Byte(soap.Product);
                    break;
                case SurfaceOpType.Extract:
                    var extract = (ExtractOperation)boxed;
                    w.Float(extract.Power); w.Float(extract.DeltaTime);
                    break;
                case SurfaceOpType.Sweep:
                    var sweep = (SweepOperation)boxed;
                    w.Float(sweep.Power); w.Float(sweep.DeltaTime); w.Bool(sweep.HardFloor);
                    break;
                case SurfaceOpType.Deposit:
                    var deposit = (DepositOperation)boxed;
                    w.Byte((byte)deposit.Type); w.Float(deposit.Mud); w.Float(deposit.Debris); w.Float(deposit.Foam); w.Float(deposit.Water);
                    w.Float(deposit.Residue);
                    break;
                case SurfaceOpType.Gather:
                    w.Float(((GatherOperation)boxed).Efficiency);
                    break;
                case SurfaceOpType.Wipe:
                    w.Float(((WipeWaterOperation)boxed).Efficiency);
                    break;
            }
        }

        /// <summary>En-tête d'une requête (sans la consommer entièrement).</summary>
        public readonly struct Header
        {
            public readonly byte SurfaceId;
            public readonly uint Sequence;
            public readonly SurfaceOpGeometry Geometry;
            public readonly SurfaceOpType Type;

            public Header(byte surfaceId, uint sequence, in SurfaceOpGeometry geometry, SurfaceOpType type)
            {
                SurfaceId = surfaceId;
                Sequence = sequence;
                Geometry = geometry;
                Type = type;
            }
        }

        public static Header ReadHeader(ref ByteReader r)
        {
            byte surface = r.Byte();
            uint sequence = r.UInt();
            Vector3 point = r.Vector3();
            float radius = Mathf.Clamp(r.Float(), 0f, 2f);
            float hardness = Mathf.Clamp(r.Float(), 0.5f, 12f);
            float aspect = Mathf.Clamp(r.Float(), 1f, 6f);
            Vector3 axis = r.Vector3();
            var runoff = new RunoffProfile(Mathf.Clamp01(r.Float()), Mathf.Clamp01(r.Float()), Mathf.Clamp(r.Float(), 0f, 10f), r.Bool());
            var type = (SurfaceOpType)r.Byte();
            if (!Enum.IsDefined(typeof(SurfaceOpType), type)) throw new FormatException($"Opération réseau inconnue ({(int)type}).");
            return new Header(surface, sequence, new SurfaceOpGeometry(point, radius, hardness, aspect, axis, runoff), type);
        }

        /// <summary>Point et axe exprimés dans l'espace de la surface (ce qui est transmis).</summary>
        public static SurfaceOpGeometry ToLocal(Transform surface, in SurfaceOpGeometry world) =>
            new SurfaceOpGeometry(surface.InverseTransformPoint(world.Point), world.Radius, world.Hardness, world.Aspect,
                surface.InverseTransformDirection(world.Axis), world.Runoff);

        public static SurfaceOpGeometry ToWorld(Transform surface, in SurfaceOpGeometry local) =>
            new SurfaceOpGeometry(surface.TransformPoint(local.Point), local.Radius, local.Hardness, local.Aspect,
                surface.TransformDirection(local.Axis), local.Runoff);

        /// <summary>Opération décodée, telle qu'appliquée par l'hôte (utile aux statistiques et à la consommation de produit).</summary>
        public struct Applied
        {
            public SurfaceOpType Type;
            public int Touched;
            public byte Product;
            public float DeltaTime;
        }

        /// <summary>
        /// Lit les paramètres qui suivent l'en-tête et applique l'opération à la surface, sans la retransmettre.
        /// Les valeurs sont bornées (un message trafiqué ne peut pas « nettoyer » plus qu'un outil réel).
        /// </summary>
        public static Applied ReadAndApply(ref ByteReader r, in Header header, RugCleaningController surface, bool allowRunoff,
            Func<byte, byte> productFilter = null)
        {
            // La géométrie transmise est locale à la surface (voir ToLocal).
            var g = ToWorld(surface.transform, header.Geometry);
            var result = new Applied { Type = header.Type };
            switch (header.Type)
            {
                case SurfaceOpType.Brush:
                {
                    var op = new BrushOperation
                    {
                        Table = surface.DirtTable, Products = surface.Products,
                        Power = Clamp(r.Float(), 0f, 5f), DeltaTime = Delta(r.Float()), FiberWearEnabled = r.Bool(),
                        WearPerSecond = Clamp(r.Float(), 0f, 1f), StubbornBonus = Clamp(r.Float(), 0f, 5f), LatherPerSecond = Clamp(r.Float(), 0f, 5f),
                    };
                    result.DeltaTime = op.DeltaTime;
                    result.Touched = surface.ApplyLocal(g, allowRunoff, ref op);
                    break;
                }
                case SurfaceOpType.Rinse:
                {
                    var op = new RinseOperation { Table = surface.DirtTable, Power = Clamp(r.Float(), 0f, 5f), WettingPerSecond = Clamp(r.Float(), 0f, 5f), DeltaTime = Delta(r.Float()) };
                    result.DeltaTime = op.DeltaTime;
                    result.Touched = surface.ApplyLocal(g, allowRunoff, ref op);
                    break;
                }
                case SurfaceOpType.Vacuum:
                {
                    var op = new VacuumOperation
                    {
                        Table = surface.DirtTable, Power = Clamp(r.Float(), 0f, 5f), DeltaTime = Delta(r.Float()), FiberBonus = Clamp(r.Float(), 0f, 4f),
                    };
                    result.DeltaTime = op.DeltaTime;
                    result.Touched = surface.ApplyLocal(g, allowRunoff, ref op);
                    break;
                }
                case SurfaceOpType.Soap:
                {
                    var op = new SoapOperation
                    {
                        FoamPerSecond = Clamp(r.Float(), 0f, 10f), WettingPerSecond = Clamp(r.Float(), 0f, 5f), DeltaTime = Delta(r.Float()),
                        ResiduePerFoam = Clamp(r.Float(), 0f, 2f), Product = r.Byte(),
                    };
                    if (productFilter != null) op.Product = productFilter(op.Product);
                    result.Product = op.Product;
                    result.DeltaTime = op.DeltaTime;
                    result.Touched = surface.ApplyLocal(g, allowRunoff, ref op);
                    break;
                }
                case SurfaceOpType.Extract:
                {
                    var op = new ExtractOperation { Power = Clamp(r.Float(), 0f, 5f), DeltaTime = Delta(r.Float()) };
                    result.DeltaTime = op.DeltaTime;
                    result.Touched = surface.ApplyLocal(g, allowRunoff, ref op);
                    break;
                }
                case SurfaceOpType.Sweep:
                {
                    var op = new SweepOperation { Table = surface.DirtTable, Power = Clamp(r.Float(), 0f, 5f), DeltaTime = Delta(r.Float()), HardFloor = r.Bool() };
                    result.DeltaTime = op.DeltaTime;
                    result.Touched = surface.ApplyLocal(g, allowRunoff, ref op);
                    break;
                }
                case SurfaceOpType.Deposit:
                {
                    var type = (DirtType)r.Byte();
                    if ((int)type >= DirtTypes.Count) type = DirtType.Soil;
                    var op = new DepositOperation
                    {
                        Type = type, Mud = Clamp(r.Float(), 0f, 1f), Debris = Clamp(r.Float(), 0f, 1f), Foam = Clamp(r.Float(), 0f, 1f), Water = Clamp(r.Float(), 0f, 1f),
                        Residue = Clamp(r.Float(), 0f, 1f),
                    };
                    result.Touched = surface.ApplyLocal(g, allowRunoff, ref op);
                    break;
                }
                case SurfaceOpType.Gather:
                {
                    // Raclette sur un tapis : sans effet (les fibres ne se raclent pas), même pour un message trafiqué.
                    var op = new GatherOperation { Efficiency = Clamp(r.Float(), 0f, 1f) };
                    if (surface.Kind != SurfaceKind.Rug) result.Touched = surface.ApplyLocal(g, allowRunoff, ref op);
                    break;
                }
                case SurfaceOpType.Wipe:
                {
                    // Raclette sur un tapis : l'eau seulement (opération incapable de toucher à autre chose).
                    var op = new WipeWaterOperation { Efficiency = Clamp(r.Float(), 0f, 1f) };
                    if (surface.Kind == SurfaceKind.Rug) result.Touched = surface.ApplyLocal(g, allowRunoff, ref op);
                    break;
                }
            }
            return result;
        }

        static float Clamp(float value, float min, float max) => Mathf.Clamp(value, min, max);

        /// <summary>Pas de temps d'une opération : borné à 0,1 s (une frame lente), comme côté outil.</summary>
        static float Delta(float value) => Mathf.Clamp(value, 0f, 0.1f);
    }
}
