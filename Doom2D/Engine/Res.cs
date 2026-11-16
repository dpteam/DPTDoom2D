using System;

namespace Doom2D.Engine
{
    /// <summary>
    /// memory.c — M_lock / M_unlock.
    ///
    /// The original hands out raw pointers to WAD resources and keeps a refcount
    /// so the same sprite is not decoded twice. Here a Locked object plays the
    /// role of that pointer: it carries the resource id (which the C code reads
    /// back through ((int*)p)[-1]) and the bytes.
    /// </summary>
    public static class Res
    {
        public sealed class Locked
        {
            public readonly int Id;
            public readonly byte[] Data;

            VgaImage image;
            public VgaImage Image
            {
                get
                {
                    if (image == null) image = VgaImage.FromRaw(Data, Wad.GetResName(Id));
                    return image;
                }
            }

            public int Length { get { return Data.Length; } }

            internal Locked(int id, byte[] data)
            {
                Id = id;
                Data = data;
            }
        }

        static Locked[] resp = new Locked[Wad.MaxEntries];
        static int[] refs = new int[Wad.MaxEntries];

        /// <summary>M_lock: id may carry 0x8000 (mirrored sprite); -1 means NULL.</summary>
        public static Locked Lock(int id)
        {
            if (id == -1 || id == 0xFFFF) return null;

            id &= ~0x8000;
            if (id < 0 || id >= Wad.MaxEntries)
                throw new Fatal("M_lock: странный номер ресурса");

            if (refs[id] == 0 && resp[id] == null)
                resp[id] = new Locked(id, Wad.LoadRes(id));

            ++refs[id];
            return resp[id];
        }

        public static void Unlock(Locked p)
        {
            if (p == null) return;
            if (p.Id < 0 || p.Id >= Wad.MaxEntries) return;
            if (refs[p.Id] == 0) return;
            --refs[p.Id];
        }

        /// <summary>M_lock for something that is a vgaimg (sprite or picture).</summary>
        public static VgaImage Img(int id)
        {
            Locked l = Lock(id);
            return l == null ? null : l.Image;
        }

        /// <summary>Drops everything: used when a new map starts (free_chunks in the C code).</summary>
        public static void Reset()
        {
            resp = new Locked[Wad.MaxEntries];
            refs = new int[Wad.MaxEntries];
        }
    }
}
