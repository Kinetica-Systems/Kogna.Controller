using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace KinematicEngine
{
    /// <summary>
    /// A compact unmanaged snapshot of the parts of SetupData we diff.
    /// Only primitive fields (and fixed‐size arrays) may live here.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SetupSnapshot
    {
        public int sequence_number;
        // TODO: add any other scalar fields you need here
        // -- e.g. public double feed_rate; public int motion_mode; etc.

        // parameters array (fixed‐size)
        public const int MAX_PARAMETERS = 5400;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_PARAMETERS)]
        public double[] parameters;

        public int n_ParamChanges;
        public const int MAX_PARAM_CHANGES = 100;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_PARAM_CHANGES)]
        public int[] ParamChanges;
        

        // ctor to allocate the fixed‐size arrays
        public SetupSnapshot(
            int sequence_number,
            double[] parameters,
            int n_ParamChanges,
            int[] ParamChanges
        /* TODO: other fields */
        )
        {
            this.sequence_number = sequence_number;
            this.parameters = parameters;
            this.n_ParamChanges = n_ParamChanges;
            this.ParamChanges = ParamChanges;
            // TODO: init other fields
        }
    }

    public partial class RS274NGC
    {
        /// <summary>
        /// Tracks "setup" state changes so the interpreter can rewind or advance.
        /// </summary>
        public class SetupTracker
        {
            private const int MAX_TRACKER = 500_000;

            public struct Change
            {
                public long Data;    // XOR mask
                public int  Offset;  // 64‐bit word index
                public bool First;   // marks first word of a state
            }

            // ring‐buffer of XOR diffs
            readonly Change[] Buffer = new Change[MAX_TRACKER];

            // baseline and real‐time snapshots
            SetupSnapshot cur_state, realtime_state;

            // buffer pointers/counters
            int nChanges, nApplied, index, realtime_index;
            bool firstCall = true;

            /// <summary>
            /// Clears history and seeds with initial SetupData.
            /// </summary>
            public void ClearHistory(SetupData initial)
            {
                var snap = ToSnapshot(initial);
                cur_state = realtime_state = snap;
                nChanges = nApplied = index = realtime_index = 0;
                firstCall = false;
            }

            /// <summary>
            /// Record a new SetupData state, storing only XOR‐diffs.
            /// </summary>
            public int InsertState(SetupData curSetup)
            {
                var newSnap = ToSnapshot(curSetup);
                if (firstCall)
                {
                    cur_state = realtime_state = newSnap;
                    firstCall = false;
                    return 0;
                }

                int baseOffset = Marshal.OffsetOf<SetupSnapshot>(
                    nameof(SetupSnapshot.parameters)
                ).ToInt32();
                int wordCount = baseOffset / sizeof(long);

                // marshal structs to byte[]
                var nb = StructToBytes(newSnap);
                var ob = StructToBytes(cur_state);
                var s  = MemoryMarshal.Cast<byte,long>(nb.AsSpan());
                var d  = MemoryMarshal.Cast<byte,long>(ob.AsSpan());
                var C  = new Change { First = true };

                // 1) diff first portion
                for (int i = 0; i < wordCount; i++)
                {
                    long X = s[i] ^ d[i];
                    if (X != 0)
                    {
                        C.Data   = X;
                        C.Offset = i;
                        Buffer[index++] = C;
                        if (index >= MAX_TRACKER) index = 0;
                        nChanges++;
                        C.First = false;
                        d[i] = s[i];
                    }
                }

                // 2) diff only interpreter‐flagged parameters
                for (int j = 0; j < newSnap.n_ParamChanges; j++)
                {
                    int pIdx    = newSnap.ParamChanges[j];
                    int bytePos = baseOffset + pIdx * sizeof(double);
                    int wIdx    = bytePos / sizeof(long);

                    long X = s[wIdx] ^ d[wIdx];
                    if (X != 0)
                    {
                        C.Data   = X;
                        C.Offset = wIdx;
                        Buffer[index++] = C;
                        if (index >= MAX_TRACKER) index = 0;
                        nChanges++;
                        C.First = false;
                        d[wIdx] = s[wIdx];
                    }
                }

                cur_state = newSnap;
                return 0;
            }

            /// <summary>
            /// Roll back <paramref name="target"/> to the sequence exactly.
            /// </summary>
            public int RestoreState(int sequence_number, ref SetupData target)
            {
                // reset to baseline
                FromSnapshot(target, cur_state);

                int count = nChanges;
                int idx   = index;

                // step backward
                while (count-- > 0)
                {
                    idx = (idx - 1 + MAX_TRACKER) % MAX_TRACKER;
                    ApplyChange(ref target, Buffer[idx]);
                    if (target.sequence_number == sequence_number)
                        return 0;
                }

                return 1; // no match
            }

            /// <summary>
            /// Advance real-time to exactly <paramref name="sequence_number"/>.
            /// </summary>
            public int AdvanceState(int sequence_number, ref SetupData target)
            {
                // reset to baseline
                FromSnapshot(target, cur_state);

                int applied = 0;
                int idx     = realtime_index;

                // step forward
                while (applied < nChanges)
                {
                    ApplyChange(ref target, Buffer[idx]);
                    if (target.sequence_number == sequence_number)
                    {
                        realtime_index = idx;
                        return 0;
                    }
                    idx = (idx + 1) % MAX_TRACKER;
                    applied++;
                }

                return 1; // no match
            }

            // --------------------------------------------------
            // Internal helpers

            private void ApplyChange(ref SetupData dst, Change c)
            {
                // snapshot, mutate, restore
                var snap  = ToSnapshot(dst);
                var bytes = StructToBytes(snap);
                var words = MemoryMarshal.Cast<byte,long>(bytes.AsSpan());

                words[c.Offset] ^= c.Data;

                var newSnap = BytesToStruct(bytes);
                FromSnapshot(dst, newSnap);
            }

            private static SetupSnapshot ToSnapshot(SetupData src)
            {
                return new SetupSnapshot(
                    src.sequence_number,
                    (double[])src.parameters.Clone(),
                    src.n_ParamChanges,
                    (int[])src.ParamChanges.Clone()
                    /* TODO: pass other fields */
                );
            }

            private static void FromSnapshot(SetupData dst, in SetupSnapshot s)
            {
                dst.sequence_number = s.sequence_number;
                dst.n_ParamChanges  = s.n_ParamChanges;
                Array.Copy(s.parameters, dst.parameters, s.parameters.Length);
                Array.Copy(s.ParamChanges, dst.ParamChanges, s.ParamChanges.Length);
                // TODO: copy back other fields
            }

            private static byte[] StructToBytes(in SetupSnapshot snap)
            {
                int size = Marshal.SizeOf<SetupSnapshot>();
                byte[] data = new byte[size];
                var h = GCHandle.Alloc(data, GCHandleType.Pinned);
                try { Marshal.StructureToPtr(snap, h.AddrOfPinnedObject(), false); }
                finally { h.Free(); }
                return data;
            }

            private static SetupSnapshot BytesToStruct(byte[] bytes)
            {
                var h = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                try { return Marshal.PtrToStructure<SetupSnapshot>(h.AddrOfPinnedObject())!; }
                finally { h.Free(); }
            }
        }
    }
}
