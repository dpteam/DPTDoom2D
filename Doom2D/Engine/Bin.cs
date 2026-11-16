using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Doom2D.Engine
{
    /// <summary>
    /// Little-endian reader for the Doom2D binary formats (maps, saves).
    /// The game's own structures are int/byte/short with pack(1), so the ported
    /// save code reads and writes fields one by one and stays byte-compatible.
    /// </summary>
    public sealed class BinReader
    {
        readonly byte[] data;
        int pos;

        public BinReader(byte[] data) { this.data = data; }
        public BinReader(byte[] data, int offset) { this.data = data; pos = offset; }

        public int Position { get { return pos; } set { pos = value; } }
        public int Length { get { return data.Length; } }
        public int Remaining { get { return data.Length - pos; } }

        public byte Byte() { return data[pos++]; }
        public sbyte SByte() { return (sbyte)data[pos++]; }
        public short Short() { short v = BitConverter.ToInt16(data, pos); pos += 2; return v; }
        public ushort UShort() { ushort v = BitConverter.ToUInt16(data, pos); pos += 2; return v; }
        public int Int() { int v = BitConverter.ToInt32(data, pos); pos += 4; return v; }
        public uint UInt() { uint v = BitConverter.ToUInt32(data, pos); pos += 4; return v; }

        public void ReadInto(byte[] dst) { Buffer.BlockCopy(data, pos, dst, 0, dst.Length); pos += dst.Length; }
        public void ReadInto(byte[] dst, int count) { Buffer.BlockCopy(data, pos, dst, 0, count); pos += count; }
        public byte[] Bytes(int n) { byte[] b = new byte[n]; Buffer.BlockCopy(data, pos, b, 0, n); pos += n; return b; }

        /// <summary>An 8-byte NUL-padded resource name.</summary>
        public string Name8()
        {
            int n = 0;
            while (n < 8 && data[pos + n] != 0) ++n;
            string s = Encoding.GetEncoding(28591).GetString(data, pos, n);
            pos += 8;
            return s;
        }
    }

    /// <summary>Little-endian writer, the mirror of BinReader.</summary>
    public sealed class BinWriter
    {
        readonly MemoryStream stream = new MemoryStream();

        public byte[] ToArray() { return stream.ToArray(); }

        public void Byte(byte v) { stream.WriteByte(v); }
        public void SByte(sbyte v) { stream.WriteByte((byte)v); }
        public void Short(int v) { stream.Write(BitConverter.GetBytes((short)v), 0, 2); }
        public void UShort(int v) { stream.Write(BitConverter.GetBytes((ushort)v), 0, 2); }
        public void Int(int v) { stream.Write(BitConverter.GetBytes(v), 0, 4); }
        public void UInt(uint v) { stream.Write(BitConverter.GetBytes(v), 0, 4); }
        public void Bytes(byte[] b) { stream.Write(b, 0, b.Length); }

        public void Name8(string s)
        {
            byte[] b = new byte[8];
            byte[] src = Encoding.GetEncoding(28591).GetBytes(s ?? "");
            Array.Copy(src, b, Math.Min(8, src.Length));
            stream.Write(b, 0, 8);
        }
    }
}
