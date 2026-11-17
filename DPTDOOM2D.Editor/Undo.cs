//
// DPT.DOOM 2D — undo for the editor.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System.Collections.Generic;
using DPTDOOM2D.Game;

namespace DPTDOOM2D.Editor
{
    /// <summary>
    /// One undo step: everything a stroke could have changed.
    ///
    /// A snapshot, not a list of edits. The three layers are 30 KB together and a
    /// stroke touches at most a few hundred cells, so a diff would be smaller — but
    /// a snapshot is correct for things, triggers and music for free, and it cannot
    /// get out of step with the map. Forty steps is 1.2 MB, which is nothing next to
    /// the WAD this program has already loaded.
    ///
    /// The original editor had no undo at all, which is why a mis-typed wall could
    /// cost an evening. That is one place where being faithful is not worth it.
    /// </summary>
    internal sealed class MapSnapshot
    {
        private readonly byte[] back;
        private readonly byte[] type;
        private readonly byte[] front;
        private readonly List<MapThing> things;
        private readonly List<MapSwitch> switches;
        private readonly string music;
        private readonly int sky;

        public MapSnapshot(MapFile map)
        {
            back = (byte[])map.Back.Clone();
            type = (byte[])map.Type.Clone();
            front = (byte[])map.Front.Clone();

            things = new List<MapThing>(map.Things.Count);
            for (int i = 0; i < map.Things.Count; ++i) things.Add(map.Things[i].Clone());

            switches = new List<MapSwitch>(map.Switches.Count);
            for (int i = 0; i < map.Switches.Count; ++i) switches.Add(map.Switches[i].Clone());

            music = map.Music;
            sky = map.Sky;
        }

        /// <summary>Puts the snapshot back into the map, in place.</summary>
        public void Restore(MapFile map)
        {
            System.Buffer.BlockCopy(back, 0, map.Back, 0, back.Length);
            System.Buffer.BlockCopy(type, 0, map.Type, 0, type.Length);
            System.Buffer.BlockCopy(front, 0, map.Front, 0, front.Length);

            map.Things.Clear();
            for (int i = 0; i < things.Count; ++i) map.Things.Add(things[i].Clone());

            map.Switches.Clear();
            for (int i = 0; i < switches.Count; ++i) map.Switches.Add(switches[i].Clone());

            map.Music = music;
            map.Sky = sky;
        }
    }

    /// <summary>A bounded stack of snapshots.</summary>
    internal sealed class UndoStack
    {
        /// <summary>Steps kept. Beyond this the oldest is dropped: an editor that
        /// grows without limit on a 100x100 map is a memory leak with a nice name.</summary>
        public const int Depth = 40;

        private readonly List<MapSnapshot> steps = new List<MapSnapshot>();

        public int Count { get { return steps.Count; } }

        public void Push(MapFile map)
        {
            steps.Add(new MapSnapshot(map));

            if (steps.Count > Depth) steps.RemoveAt(0);
        }

        /// <summary>Restores the newest snapshot. False when there is nothing to undo.</summary>
        public bool Undo(MapFile map)
        {
            if (steps.Count == 0) return false;

            int last = steps.Count - 1;
            steps[last].Restore(map);
            steps.RemoveAt(last);

            return true;
        }

        public void Clear()
        {
            steps.Clear();
        }
    }
}
