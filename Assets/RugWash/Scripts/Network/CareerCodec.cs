using System;
using RugWash.Economy;

namespace RugWash.Network
{
    /// <summary>
    /// Format réseau de la progression vue par un joueur (version 3, V0.2.5 : objets portés et réserve de détergent). Lecture
    /// bornée : un message tronqué ou trafiqué lève <see cref="FormatException"/> et est ignoré.
    /// </summary>
    public static class CareerCodec
    {
        public const byte Version = 3;
        public const int MaxEntries = 64;

        public static byte[] Encode(PlayerCareer career)
        {
            var writer = new ByteWriter(256);
            Write(writer, career);
            return writer.ToArray();
        }

        public static void Write(ByteWriter writer, PlayerCareer career)
        {
            writer.Byte(Version);
            writer.UInt((uint)Math.Max(0, career.Money));
            writer.UInt((uint)Math.Max(0, career.Reputation));
            writer.Byte((byte)Math.Min(MaxEntries, career.Upgrades.Count));
            int written = 0;
            foreach (string id in career.Upgrades)
            {
                if (written++ >= MaxEntries) break;
                writer.String(id);
            }
            writer.Byte((byte)Math.Min(MaxEntries, career.Products.Count));
            written = 0;
            foreach (var pair in career.Products)
            {
                if (written++ >= MaxEntries) break;
                writer.String(pair.Key);
                writer.Float(pair.Value);
            }
            writer.String(career.ActiveProduct ?? string.Empty);
            writer.Float(Math.Max(0f, career.DetergentStock));
            writer.Byte((byte)Math.Min(MaxEntries, career.Carried.Count));
            for (int i = 0; i < career.Carried.Count && i < MaxEntries; i++) writer.String(career.Carried[i] ?? string.Empty);
            writer.Float(career.SoapReserve);
        }

        public static PlayerCareer Decode(byte[] data)
        {
            if (data == null) throw new FormatException("carrière absente");
            var reader = new ByteReader(data);
            return Read(ref reader);
        }

        public static PlayerCareer Read(ref ByteReader reader)
        {
            if (reader.Byte() != Version) throw new FormatException("version de carrière inconnue");
            var career = new PlayerCareer
            {
                Money = (int)Math.Min(reader.UInt(), int.MaxValue),
                Reputation = (int)Math.Min(reader.UInt(), int.MaxValue),
            };
            int upgrades = reader.Byte();
            if (upgrades > MaxEntries) throw new FormatException("trop d'améliorations");
            for (int i = 0; i < upgrades; i++) career.Upgrades.Add(reader.String());
            int products = reader.Byte();
            if (products > MaxEntries) throw new FormatException("trop de produits");
            for (int i = 0; i < products; i++)
            {
                string id = reader.String();
                float liters = reader.Float();
                if (float.IsNaN(liters) || float.IsInfinity(liters)) throw new FormatException("stock invalide");
                career.Products[id] = liters;
            }
            career.ActiveProduct = reader.String();
            career.DetergentStock = reader.Float();
            int carried = reader.Byte();
            if (carried > MaxEntries) throw new FormatException("trop d'objets portés");
            for (int i = 0; i < carried; i++) career.Carried.Add(reader.String());
            career.SoapReserve = reader.Float();
            if (float.IsNaN(career.DetergentStock) || float.IsNaN(career.SoapReserve)) throw new FormatException("réserve invalide");
            return career;
        }
    }
}
