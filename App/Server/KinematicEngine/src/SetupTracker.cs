using System;
using System.Runtime.InteropServices;


namespace KognaServer.Server.KinematicEngine
{
    public partial class RS274NGC
    {
        /// <summary>
        /// Tracks “setup” state changes so the interpreter can rewind or advance.
        /// </summary>
        public class SetupTracker
        {
            private const int MAX_TRACKER = 500_000;

            // One change record (word XOR)
            private struct Change
            {
                public long Data;    // XOR mask
                public int Offset;  // 64-bit word index
                public bool First;   // marks first word of a state
            }

            // Raw ring-buffer of changes
            private readonly Change[] Buffer = new Change[MAX_TRACKER];

            // Baseline and real-time copies of the full SetupData
            private SetupData cur_state = null!;
            private SetupData realtime_state = null!;

            // Ring-buffer indices & counters
            private int nChanges, nApplied, index, realtime_index;
            private bool m_FirstCall = true;

            /// <summary>
            /// Clears the entire history, setting both states to <paramref name="initial"/>. 
            /// </summary>
            public void ClearHistory(SetupData initial)
            {
                cur_state = initial;
                realtime_state = initial;
                nChanges = nApplied = index = realtime_index = 0;
                m_FirstCall = false;
            }

            /// <summary>
            /// Snapshot–compare–XOR the first N 64-bit words of newState against cur_state,
            /// plus any parameter-change words, appending Change records to Buffer.
            /// </summary>
            public int InsertState(SetupData newState)
            {
                var newBytes = CopyToBytes(newState);

                if (m_FirstCall)
                {
                    ClearHistory(newState);
                    return 0;
                }

                // Grab the old baseline as bytes
                var oldBytes = CopyToBytes(cur_state);

                // Treat each as a Span<long> for 64-bit XOR’ing:
                var newWords = MemoryMarshal.Cast<byte, long>(newBytes.AsSpan());
                var oldWords = MemoryMarshal.Cast<byte, long>(oldBytes.AsSpan());

                // 1) Diff everything up to the parameters block
                int paramOffsetBytes = Marshal.OffsetOf<SetupData>("parameters")
                                            .ToInt32();
                int wordCount = paramOffsetBytes / sizeof(long);

                bool isFirst = true;
                for (int w = 0; w < wordCount; w++)
                {
                    long diff = newWords[w] ^ oldWords[w];
                    if (diff != 0)
                    {
                        Buffer[index++] = new Change
                        {
                            Data = diff,
                            Offset = w,
                            First = isFirst
                        };
                        if (index >= MAX_TRACKER) index = 0;
                        nChanges++;
                        isFirst = false;
                        oldWords[w] = newWords[w];
                    }
                }

                // 2) Diff only the changed parameters
                for (int i = 0; i < newState.n_ParamChanges; i++)
                {
                    int pIdx = newState.ParamChanges[i];
                    int wOff = (paramOffsetBytes / sizeof(long)) + pIdx;
                    long diff = newWords[wOff] ^ oldWords[wOff];
                    if (diff != 0)
                    {
                        Buffer[index++] = new Change
                        {
                            Data = diff,
                            Offset = wOff,
                            First = isFirst
                        };
                        if (index >= MAX_TRACKER) index = 0;
                        nChanges++;
                        oldWords[wOff] = newWords[wOff];
                        isFirst = false;
                    }
                }

                // 3) Advance the baseline
                cur_state = newState;
                return 0;
            }

            /// <summary>
            /// Roll back <paramref name="target"/> to the state at <paramref name="sequence_number"/>.
            /// </summary>
            public int RestoreState(int sequence_number, ref SetupData target)
            {
                // Start from the last saved baseline
                target = cur_state;
                var tgtBytes = CopyToBytes(target);
                var tgtWords = MemoryMarshal.Cast<byte, long>(tgtBytes.AsSpan());

                int changesLeft = nChanges;
                int idx = index;

                // Step backward through the ring buffer, XOR’ing out diffs
                while (true)
                {
                    if (target.sequence_number == sequence_number)
                    {
                        CopyFromBytes(target, tgtBytes);
                        return 0;
                    }
                    if (changesLeft-- <= 0)
                        return 1;  // no more history

                    idx = (idx - 1 + MAX_TRACKER) % MAX_TRACKER;
                    var c = Buffer[idx];
                    tgtWords[c.Offset] ^= c.Data;
                }
            }

            /// <summary>
            /// Advance <see cref="realtime_state"/> forward (or backward) to <paramref name="sequence_number"/>.
            /// </summary>
            public int AdvanceState(int sequence_number)
            {
                var rtBytes = CopyToBytes(realtime_state);
                var rtWords = MemoryMarshal.Cast<byte, long>(rtBytes.AsSpan());

                if (realtime_state.sequence_number <= sequence_number)
                {
                    // Step forward
                    while (true)
                    {
                        if (realtime_state.sequence_number >= sequence_number)
                        {
                            CopyFromBytes(realtime_state, rtBytes);
                            return 0;
                        }
                        if (nApplied >= nChanges)
                            return 1;  // no more diffs

                        var c = Buffer[realtime_index];
                        rtWords[c.Offset] ^= c.Data;
                        nApplied++;
                        realtime_index = (realtime_index + 1) % MAX_TRACKER;
                    }
                }
                else
                {
                    // Step backward
                    while (true)
                    {
                        if (realtime_state.sequence_number == sequence_number)
                        {
                            CopyFromBytes(realtime_state, rtBytes);
                            return 0;
                        }
                        if (nApplied <= 0)
                            return 1;

                        realtime_index = (realtime_index - 1 + MAX_TRACKER) % MAX_TRACKER;
                        var c = Buffer[realtime_index];
                        rtWords[c.Offset] ^= c.Data;
                        nApplied--;
                    }
                }
            }

            // --- Low-level pin/copy helpers (no unsafe) ---

            private static byte[] CopyToBytes(SetupData obj)
            {
                int size = Marshal.SizeOf<SetupData>();
                var bytes = new byte[size];
                var handle = GCHandle.Alloc(obj, GCHandleType.Pinned);
                try
                {
                    Marshal.Copy(handle.AddrOfPinnedObject(), bytes, 0, size);
                }
                finally
                {
                    handle.Free();
                }
                return bytes;
            }

            private static void CopyFromBytes(SetupData obj, byte[] bytes)
            {
                var handle = GCHandle.Alloc(obj, GCHandleType.Pinned);
                try
                {
                    Marshal.Copy(bytes, 0, handle.AddrOfPinnedObject(), bytes.Length);
                }
                finally
                {
                    handle.Free();
                }
            }
        }
    }
}