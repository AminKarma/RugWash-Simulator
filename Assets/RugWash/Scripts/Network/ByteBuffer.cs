using System;
using UnityEngine;

namespace RugWash.Network
{
    /// <summary>Écriture binaire compacte (petit-boutiste) pour les messages réseau, sans allocation par valeur.</summary>
    public sealed class ByteWriter
    {
        byte[] buffer;

        public ByteWriter(int capacity = 256) => buffer = new byte[Mathf.Max(16, capacity)];

        public int Length { get; private set; }
        public byte[] Buffer => buffer;
        public ArraySegment<byte> Segment => new ArraySegment<byte>(buffer, 0, Length);

        public void Reset() => Length = 0;

        public byte[] ToArray()
        {
            var copy = new byte[Length];
            Array.Copy(buffer, copy, Length);
            return copy;
        }

        void Ensure(int extra)
        {
            if (Length + extra <= buffer.Length) return;
            Array.Resize(ref buffer, Mathf.Max(buffer.Length * 2, Length + extra));
        }

        public void Byte(byte value)
        {
            Ensure(1);
            buffer[Length++] = value;
        }

        public void Bool(bool value) => Byte(value ? (byte)1 : (byte)0);

        public void UShort(int value)
        {
            Ensure(2);
            buffer[Length++] = (byte)value;
            buffer[Length++] = (byte)(value >> 8);
        }

        public void UInt(uint value)
        {
            Ensure(4);
            buffer[Length++] = (byte)value;
            buffer[Length++] = (byte)(value >> 8);
            buffer[Length++] = (byte)(value >> 16);
            buffer[Length++] = (byte)(value >> 24);
        }

        public void ULong(ulong value)
        {
            UInt((uint)value);
            UInt((uint)(value >> 32));
        }

        public void Float(float value) => UInt((uint)BitConverter.SingleToInt32Bits(value));

        public void Vector3(Vector3 value)
        {
            Float(value.x);
            Float(value.y);
            Float(value.z);
        }

        public void String(string value)
        {
            value ??= string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetBytes(value);
            UShort(Mathf.Min(bytes.Length, ushort.MaxValue));
            Bytes(bytes, 0, Mathf.Min(bytes.Length, ushort.MaxValue));
        }

        public void Bytes(byte[] source, int offset, int count)
        {
            Ensure(count);
            Array.Copy(source, offset, buffer, Length, count);
            Length += count;
        }
    }

    /// <summary>Lecture d'un message écrit par <see cref="ByteWriter"/>. Un message tronqué lève <see cref="FormatException"/>.</summary>
    public struct ByteReader
    {
        readonly byte[] data;
        readonly int end;

        public ByteReader(byte[] source, int offset = 0, int count = -1)
        {
            data = source ?? Array.Empty<byte>();
            Position = offset;
            end = count < 0 ? data.Length : offset + count;
            if (end > data.Length) throw new FormatException("Message réseau tronqué.");
        }

        public ByteReader(ArraySegment<byte> segment) : this(segment.Array, segment.Offset, segment.Count) { }

        public int Position { get; private set; }
        public int Remaining => end - Position;

        void Need(int count)
        {
            if (Position + count > end) throw new FormatException("Message réseau tronqué.");
        }

        public byte Byte()
        {
            Need(1);
            return data[Position++];
        }

        public bool Bool() => Byte() != 0;

        public int UShort()
        {
            Need(2);
            int value = data[Position] | (data[Position + 1] << 8);
            Position += 2;
            return value;
        }

        public uint UInt()
        {
            Need(4);
            uint value = (uint)(data[Position] | (data[Position + 1] << 8) | (data[Position + 2] << 16) | (data[Position + 3] << 24));
            Position += 4;
            return value;
        }

        public ulong ULong()
        {
            ulong low = UInt();
            ulong high = UInt();
            return low | (high << 32);
        }

        public float Float()
        {
            float value = BitConverter.Int32BitsToSingle((int)UInt());
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new FormatException("Valeur réseau invalide.");
            return value;
        }

        public Vector3 Vector3() => new Vector3(Float(), Float(), Float());

        public string String()
        {
            int length = UShort();
            Need(length);
            string value = System.Text.Encoding.UTF8.GetString(data, Position, length);
            Position += length;
            return value;
        }

        public void Bytes(byte[] target, int offset, int count)
        {
            Need(count);
            Array.Copy(data, Position, target, offset, count);
            Position += count;
        }
    }
}
