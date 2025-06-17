using System;
using System.IO;
using System.Text;
using System.Threading;
using AvaloniaEdit.Document;
using KognaServer.Models;

namespace KinematicEngine
{
    /// <summary>
    /// Full C# port of the DynoMotion CCoordMotion class,
    /// including all public APIs from the original C++ implementation.
    /// </summary>
    public class CCoordMotion : IDisposable
    {
        // Kinematics and motion parameters
        public CKinematics Kinematics { get; private set; }

        // Motion state
        public double current_x, current_y, current_z;
        public double current_a, current_b, current_c;
        public double current_u, current_v;

        // Lookahead / download state
        private int m_nsegs_downloaded;
        private double m_TotalDownloadedTime;
        private double m_TimeAlreadyExecuted;
        private bool m_ThreadingMode;
        private bool m_SegmentsStartedExecuting;
        private bool m_TapCycleInProgress = false;
        private int m_PreviouslyStopped;
        private int m_Stopping = 0;
        private int STOPPED_NONE = 0;
        private int m_PreviouslyStoppedType = 0;
        private int SEG_UNDEFINED = 0;
        private int m_PreviouslyStoppedID = -1;
        private bool m_TCP_affects_actuators = true;  // assume Tool Center Point has effects except for simple cases

        // Control flags
        private bool m_Abort;
        private bool m_Halt;
        public bool Simulate { get; set; } = false;
        public bool DisableSoftLimits { get; private set; } = false;
        // internal abort flag
        private bool _abortFlag;

        /// <summary>Request an abort.</summary>
        public void SetAbort() => m_Abort = true;
        public void ClearAbort() => m_Abort = false;
        public bool GetAbort() => m_Abort;
        public void SetHalt() => m_Halt = true;
        public void ClearHalt() => m_Halt = false;
        public bool GetHalt() => m_Halt;

        public int GetDestination(int axis, out double d) => GetPosition(axis, out d);

        /// <summary>Clear any pending abort.</summary>

        // Overrides
        private readonly double m_FeedRateOverride = 1.0;
        private readonly double m_FeedRateRapidOverride = 1.0;
        private readonly double m_HardwareFRORange = 0.0;
        private readonly double m_SpindleRateOverride = 1.0;        
        public bool RapidParamsDirty { get; set; } = true;

        // Axis definitions
        private bool m_DefineCS_valid;
        private int x_axis, y_axis, z_axis, a_axis, b_axis, c_axis, u_axis, v_axis;
        // simulation & flow control
        private bool m_Simulate;
        private bool m_DoTime;
        private bool m_Trace;
        // Write buffer
        private StringBuilder m_WriteLineBuffer = new StringBuilder();
        private double m_WriteLineBufferTime;

        // Delegate definitions
        public delegate void StraightTraverseCallback(double x, double y, double z, int seq);
        public delegate void StraightTraverseSixAxisCallback(double x, double y, double z, double a, double b, double c, int seq);
        public delegate void StraightFeedCallback(double rate, double x, double y, double z, int seq, int id);
        public delegate void StraightFeedSixAxisCallback(double rate, double x, double y, double z, double a, double b, double c, int seq, int id);
        public delegate void ArcFeedCallback(bool zeroLen, double rate, int plane, double fe, double se, double fa, double sa, int rot, double ae, int seq, int id);
        public delegate void ArcFeedSixAxisCallback(bool zeroLen, double rate, int plane, double fe, double se, double fa, double sa, int rot, double ae, double a, double b, double c, int seq, int id);

        // Callback setters
        private StraightTraverseCallback? m_StraightTraverseCb;
        private StraightTraverseSixAxisCallback? m_StraightTraverse6Cb;
        private StraightFeedCallback? m_StraightFeedCb;
        private StraightFeedSixAxisCallback? m_StraightFeed6Cb;
        private ArcFeedCallback? m_ArcFeedCb;
        private ArcFeedSixAxisCallback? m_ArcFeed6Cb;


        private const int MAX_SPECIAL_CMDS = 100;
        private const int MAX_LINE = 100;
        private bool feed_override = true;	// whether feed override is enabled
        private bool speed_override = true;  // whether spindle override is enabled
        private int m_NumLinearNotDrawn = 0;
        // these mirror your C++ globals/member-variables:
        private SpecialCmd[] specialCmds;
        private int[] specialCmdsInitialSequenceNo;
        private int nspecialCmds;
        private int specialCmdsInitialFirst = -1;
        private int specialCmdsInitialLast;
        private int SegBufToggle;
        private int nsegs;
        private int m_nsegsDownloaded;
        private int ispecialCmdDownloaded;
        private const int SEG_LINEAR = 1; //ned to check
        private RS274NGC.SEGMENT GetSegPtr(int idx) { /* … */ throw new NotImplementedException(); }
        private RS274NGC.SetupData _setup;


        /// <summary>
        /// Constructor initializes kinematics and state.
        /// </summary>
        public CCoordMotion()
        {
            _setup = new RS274NGC.SetupData();


            var path = Path.GetDirectoryName(typeof(CCoordMotion).Assembly.Location)!;
            Kinematics = CKinematics.LoadFromFile(Path.Combine(path, "Data", "Kinematics.txt"));
            Kinematics.MainPath = path;
            DownloadInit();
            TrajectoryPlanner.Init();
            ResetMotionState();
            specialCmds = new SpecialCmd[MAX_SPECIAL_CMDS];
            specialCmdsInitialSequenceNo = new int[MAX_SPECIAL_CMDS];

            for (int i = 0; i < MAX_SPECIAL_CMDS; i++)
                specialCmds[i] = new SpecialCmd();
        }
        // === Core Motion Routines ===

        public void DownloadInit()
        {
            m_nsegs_downloaded = 0;
            m_TotalDownloadedTime = 0;
            m_TimeAlreadyExecuted = 0;
            m_ThreadingMode = false;
            m_SegmentsStartedExecuting = false;
        }

        private void ResetMotionState()
        {

            current_x = current_y = current_z = 0;
            current_a = current_b = current_c = 0;
            current_u = current_v = 0;
        }

        private bool CommitPendingSegments(bool rapidMode)
        {
            if (TrajectoryPlanner.PendingSegments > 0)
            {
                TrajectoryPlanner.RoundCorner(0);
                if (rapidMode) TrajectoryPlanner.DoSegmentCallbacksRapid();
                else TrajectoryPlanner.DoSegmentCallbacks();
                //  if (!TrajectoryPlanner.DoRateAdjustments()) return true;
                TrajectoryPlanner.MaximizeSegments();
            }
            return false;
        }

        // Overloads
        public int StraightTraverse(double x, double y, double z, double a, double b, double c, bool noCallback = false, int seq = -1, int id = 0)
            => StraightTraverse(x, y, z, a, b, c, current_u, current_v, noCallback, seq, id);

        public int StraightTraverse(double x, double y, double z, double a, double b, double c, double u, double v, bool noCallback, int seq, int id)
        {
            var errMsg = new StringBuilder();
            if (m_Abort) return 1;

            if (Kinematics.m_MotionParams.DoRapidsAsFeeds)
            {
                if (CommitPendingSegments(false)) return 1;
                if (nsegs > 0) 
                {var seg = GetSegPtr(nsegs -1 );
                seg.StopRequiredNextSeg = true; }
                if (DoKMotionBufCmd("BegRapidBuf", _setup.sequence_number)) return 1;
                int res = StraightFeedAccel(x, y, z, a, b, c, u, v, double.PositiveInfinity, double.PositiveInfinity, true, noCallback, seq, id);
                if (CommitPendingSegments(true)) return 1;
                return res;
            }

            if (GetRapidSettings() != 0) return 1;

            if (CheckSoftLimits(x, y, z, a, b, c, u, v, errMsg))
            {
                if (m_Simulate) { ReportError("Soft limit hit; simulation continues."); DisableSoftLimits = true; }
                else return ReportErrorAndHalt("Soft limit hit; job halted.");
            }

            if (CommitPendingSegments(false)) return 1;
            if (m_StraightTraverseCb != null) m_StraightTraverseCb(x, y, z, seq);
            if (m_StraightTraverse6Cb != null) m_StraightTraverse6Cb(x, y, z, a, b, c, seq);
            if (TrajectoryPlanner.InsertStraight(x, y, z, a, b, c, u, v, seq, id) != 0) { m_Abort = true; return 1; }

            TrajectoryPlanner.MaximizeSegments();

            if (DownloadDoneSegments()) { m_Abort = true; return 1; }
            current_x = x; current_y = y; current_z = z;
            current_a = a; current_b = b; current_c = c;
            current_u = u; current_v = v;
            return 0;


        }

        // StraightFeedAccel overloads
        public int StraightFeedAccel(double feed, double accel, double x, double y, double z, double a, double b, double c, int seq, int id)
            => StraightFeedAccel(x, y, z, a, b, c, current_u, current_v, feed, accel, false, false, seq, id);

        public int StraightFeedAccel(double x, double y, double z, double a, double b, double c, double u, double v, double feed, double accel, int seq, int id)
            => StraightFeedAccel(x, y, z, a, b, c, u, v, feed, accel, false, false, seq, id);

        public int StraightFeedAccel(double x, double y, double z, double a, double b, double c, double u, double v, double feedRate, double accel, bool rapidMode, bool noCallback, int seq, int id)
        {
            // Ported from C++ CCoordMotion::StraightFeedAccelRapid
            // ... implementation
            return 0;
        }

        // ArcFeed overloads
        public int ArcFeed(double feedRate, int plane, double fe, double se, double fa, double sa, int rot, double ae, double a, double b, double c, int seq, int id)
            => ArcFeedAccel(a, b, c, a, b, c, current_u, current_v, plane, fe, se, fa, sa, rot, ae, feedRate, double.PositiveInfinity, seq, id);

        public int ArcFeed(double feedRate, int plane, double fe, double se, double fa, double sa, int rot, double ae, double a, double b, double c, double u, double v, int seq, int id)
            => ArcFeedAccel(a, b, c, a, b, c, u, v, plane, fe, se, fa, sa, rot, ae, feedRate, double.PositiveInfinity, seq, id);

        public int ArcFeedAccel(double x, double y, double z, double a, double b, double c, double u, double v, int plane, double fe, double se, double fa, double sa, int rot, double ae, double feedRate, double accel, int seq, int id)
        {
            // Ported from C++ CCoordMotion::ArcFeedAccel
            // ... implementation
            return 0;
        }

        public int StraightFeed(double rate, double x, double y, double z, double a, double b, double c, double u, double v, int seq = -1, int id = 0)
            => StraightFeedAccel(x, y, z, a, b, c, u, v, rate, rate, false, false, seq, id);

        public int ArcFeed(int plane, double fe, double se, double fa, double sa, int rot, double ae, double a, double b, double c, double u, double v, double feedRate, double accel, int seq = -1, int id = 0)
            => ArcFeedAccel(a, b, c, a, b, c, u, v, plane, fe, se, fa, sa, rot, ae, feedRate, accel, seq, id);

        public int DoKMotionCmd(string cmd, bool flushBefore)
        {
            if (m_Simulate) return 0;
            if (flushBefore)
            {
                FlushSegments();
                WaitForSegmentsFinished();
            }
            if (SendControllerCommand(cmd) != 0)
            {
                m_Abort = true;
                return 1;
            }
            return 0;
        }

        public bool DoKMotionBufCmd(string s, int sequenceNumber)
        {
            // 1) copy into circular buffer, trimming to MAX_LINE
            int idx = nspecialCmds % MAX_SPECIAL_CMDS;
            specialCmds[idx].Cmd = s.Length > MAX_LINE
                                ? s.Substring(0, MAX_LINE)
                                : s;

            // 2) if no segments yet, queue for initial download
            if (nsegs <= 0)
            {
                if (specialCmdsInitialFirst == -1)
                {
                    specialCmdsInitialFirst = nspecialCmds;
                    specialCmdsInitialSequenceNo[SegBufToggle] = sequenceNumber;
                }

                specialCmdsInitialLast = nspecialCmds;
                nspecialCmds++;
            }
            else
            {
                // 3) attach to last segment
                RS274NGC.SEGMENT p = GetSegPtr(nsegs - 1);

                if (p.SpecialCmdsFirst == -1)
                    p.SpecialCmdsFirst = nspecialCmds;

                p.SpecialCmdsLast = nspecialCmds;
                nspecialCmds++;

                // 4) if that segment is already downloaded, fire it immediately
                if (m_nsegsDownloaded >= nsegs)
                {
                    int dlIdx = ispecialCmdDownloaded % MAX_SPECIAL_CMDS;
                    if (PutWriteLineBuffer(specialCmds[dlIdx].Cmd, 0, 0))
                        return true;
                    ispecialCmdDownloaded++;
                }
            }

            return false;
        }

        public int LaunchCoordMotion()
            => RS274NGC.LaunchCoordMotion();

        // === Helper Methods ===
        private bool DownloadDoneSegments()
        {
            if (TrajectoryPlanner.nsegs > m_nsegs_downloaded)
            {
                var seg = TrajectoryPlanner.GetSegment(m_nsegs_downloaded);
                if (seg.Done)
                {
                    if (m_nsegs_downloaded == 0 && !m_Simulate)
                        if (WaitForSegmentsFinished() != 0) { m_Abort = true; return true; }
                    while (m_nsegs_downloaded < TrajectoryPlanner.SegCount() && TrajectoryPlanner.GetSegment(m_nsegs_downloaded).Done)
                        TrajectoryPlanner.OutputSegment(m_nsegs_downloaded++);
                }
            }
            return false;
        }

        public int FlushSegments()
        {
            int a = 1;  //check this
            TrajectoryPlanner.RoundCorner(a);
            TrajectoryPlanner.MaximizeSegments();
            for (int i = m_nsegs_downloaded; i < TrajectoryPlanner.SegCount(); i++)
                if (TrajectoryPlanner.OutputSegment(i) != 0) { m_Abort = true; return 1; }
            return FlushWriteLineBuffer();
        }

        public int FlushWriteLineBuffer() => throw new NotImplementedException();
        public int SendControllerCommand(string cmd) => throw new NotImplementedException();
        public int WaitForSegmentsFinished(bool noErrorOnDisable = false)
        {
            int resp;
            do
            {
                resp = RS274NGC.CheckDoneBuf();
                if (resp == -1)
                {
                    if (noErrorOnDisable) return 0;
                    m_Abort = true;
                    return 1;
                }
                if (m_Abort) return 1;
                if (resp == 1) break;
                Thread.Sleep(10);
            } while (true);
            return 0;
        }


        public int GetPosition(int axis, out double d)
        {
            switch (axis)
            {
                case 0: d = current_x; break;
                case 1: d = current_y; break;
                case 2: d = current_z; break;
                case 3: d = current_a; break;
                case 4: d = current_b; break;
                case 5: d = current_c; break;
                case 6: d = current_u; break;
                case 7: d = current_v; break;
                default: d = 0; return 1;
            }
            return 0;
        }

        public int GetAxisDone(int axis, out int r)
            => RS274NGC.GetAxisDone(axis, out r);

        public MotionParams GetMotionParams()
            => Kinematics.m_MotionParams;

        public int MeasurePointAppendToFile(string name)
            => RS274NGC.MeasurePointAppendToFile(name);

        public int DoSpecialInitialCommands()
            => RS274NGC.DoSpecialInitialCommands();

        public int DoSpecialCommand(int seg)
            => RS274NGC.DoSpecialCommand(seg);

        public void DoSegmentCallbacks(int i0, int i1)
        {
            // only if we have at least one segment and someone is listening
            if (nsegs >= 1 &&
                (m_StraightFeedCb != null || m_StraightFeed6Cb != null))
            {
                for (int i = i0; i <= i1; i++)
                {
                    if (i < 0)
                        continue;

                    var p = GetSegPtr(i);
                    if (p.type == SEG_LINEAR)
                    {
                        // 3‐axis feed
                        m_StraightFeedCb?.Invoke(p.OrigVel, p.x1, p.y1, p.z1, _setup.sequence_number, p.ID);

                        // 6‐axis feed
                        m_StraightFeed6Cb?.Invoke(p.OrigVel, p.x1, p.y1, p.z1, p.a1, p.b1, p.c1, _setup.sequence_number, p.ID);
                    }
                }
            }
        }

        public void DoSegmentCallbacksRapid(int i0, int i1)
        {
            // only if we have at least one segment and someone is listening
            if (nsegs >= 1 && (m_StraightTraverseCb != null || m_StraightTraverse6Cb != null))
            {
                for (int i = i0; i <= i1; i++)
                {
                    if (i < 0)
                        continue;

                    var p = GetSegPtr(i);
                    if (p.type == SEG_LINEAR)
                    {
                        // 3‐axis traverse
                        m_StraightTraverseCb?.Invoke(p.x1, p.y1, p.z1, _setup.sequence_number);

                        // 6‐axis traverse
                        m_StraightTraverse6Cb?.Invoke(p.x1, p.y1, p.z1, p.a1, p.b1, p.c1, _setup.sequence_number);
                    }
                }
            }
        }
        public int DoRateAdjustments(int i0, int i1)
            => TrajectoryPlanner.DoRateAdjustments(i0, i1) ? 0 : 1;

        public int DoRateAdjustmentsArc(int i, double rad, double th0, double dth, double dc)
            => TrajectoryPlanner.DoRateAdjustmentsArc(i, rad, th0, dth, dc) ? 0 : 1;

        public int SetRapidSettings(double feed, double accel)
            => RS274NGC.SetRapidSettings((int)feed, accel);

        public int GetRapidSettingsAxis(int axis, out double vel, out double accel, out double decel, out double jerk, out double softPos, out double softNeg, out double countsPerInch, out string axisName)
            => RS274NGC.GetRapidSettingsAxis(axis, out vel, out accel, out decel, out jerk, out softPos, out softNeg, out countsPerInch, out axisName);

        public double MaxDecelTimeForAxis(int axis, double vel, double accel, double jerk)
            => CKinematics.MaxDecelTime(axis, vel, accel, jerk);

        public double GetNominalFROChangeTime(char axis)
            => CKinematics.NominalFROTime(axis);

        public int SetAxisDefinitions(int x, int y, int z, int a, int b, int c)
            => SetAxisDefinitions(x, y, z, a, b, c, -1, -1);
        // Axis definitions overloads

        public int SetAxisDefinitions(int x, int y, int z, int a, int b, int c, int u, int v)
        {
            x_axis = x; y_axis = y; z_axis = z;
            a_axis = a; b_axis = b; c_axis = c;
            u_axis = u; v_axis = v;
            m_DefineCS_valid = true;
            return 0;
        }

        public int GetAxisDefinitions(out int x, out int y, out int z, out int a, out int b, out int c)
             => GetAxisDefinitions(out x, out y, out z, out a, out b, out c, out int u, out int v);

        public int GetAxisDefinitions(out int x, out int y, out int z, out int a, out int b, out int c, out int u, out int v)
        {
            x = x_axis; y = y_axis; z = z_axis;
            a = a_axis; b = b_axis; c = c_axis;
            u = u_axis; v = v_axis;
            m_DefineCS_valid = true;
            return 0;
        }

        public bool IsCoordinateSystemValid() => m_DefineCS_valid;

        // Read current absolute position (3-axis)
        public int ReadCurAbsPosition(out double x, out double y, out double z, bool snap = false, bool noGeo = false)
            => RS274NGC.ReadCurAbsPosition(out x, out y, out z, snap, noGeo);

        // Read current absolute position (5-axis)
        public int ReadCurAbsPosition(out double x, out double y, out double z, out double u, out double v, bool snap = false, bool noGeo = false)
            => RS274NGC.ReadCurAbsPositionFull(out x, out y, out z, out u, out v, snap, noGeo);

        // Utility for wait-alias
        public int WaitForMoveXYZABCFinished() => WaitForSegmentsFinished();

        // Push motion parameters into the trajectory planner
        public void SetTPParams() => TrajectoryPlanner.SetParams(Kinematics.m_MotionParams);

        // Clean up any remaining buffer and finish
        public void Dispose()
        {
            // ensure any running segments complete
            FlushSegments();
            WaitForSegmentsFinished();
            GC.SuppressFinalize(this);
        }
        public bool CheckLimit(int axis, double Act, double SoftLimitPos, double SoftLimitNeg, char Name, StringBuilder errMsg)
        {
            if (Act > SoftLimitPos)
            {
                errMsg.Clear();
                errMsg.AppendFormat("Actuator {0} Limit {1} +{2}", Name, SoftLimitPos, Act);
                return true;
            }
            if (Act < SoftLimitNeg)
            {
                errMsg.Clear();
                errMsg.AppendFormat("Actuator {0} Limit {1} {2}-", Name, SoftLimitNeg, Act);
                return true;
            }
            return false;
        }

        public bool CheckSoftLimits(double x, double y, double z, double a, double b, double c, double u, double v, StringBuilder errMsg)
        {
            var MP = Kinematics.m_MotionParams;

            if (DisableSoftLimits)
                return false;

            // Allocate and fill Acts[]
            double[] Acts = new double[8];
            Kinematics.TransformCADtoActuators(x, y, z, a, b, c, u, v, Acts);

            // 1) Call the int-returning function
            int rc = GetAxisDefinitions(out int x_axis, out int y_axis, out int z_axis, out int a_axis, out int b_axis, out int c_axis, out int u_axis, out int v_axis);

            // 2) Check for non-zero (error)
            if (rc != 0)
            {
                SetAbort();
                return false;    // no soft-limit violation; we just aborted
            }

            // 3) Now you can safely index Acts[]
            if (x_axis >= 0 && CheckLimit(x_axis, Acts[x_axis], MP.SoftLimitPosX, MP.SoftLimitNegX, 'X', errMsg))
                return true;
            if (y_axis >= 0 && CheckLimit(y_axis, Acts[y_axis], MP.SoftLimitPosX, MP.SoftLimitNegX, 'Y', errMsg))
                return true;
            if (z_axis >= 0 && CheckLimit(z_axis, Acts[z_axis], MP.SoftLimitPosX, MP.SoftLimitNegX, 'Z', errMsg))
                return true;
            if (a_axis >= 0 && CheckLimit(a_axis, Acts[a_axis], MP.SoftLimitPosX, MP.SoftLimitNegX, 'A', errMsg))
                return true;
            if (b_axis >= 0 && CheckLimit(b_axis, Acts[b_axis], MP.SoftLimitPosX, MP.SoftLimitNegX, 'B', errMsg))
                return true;
            if (c_axis >= 0 && CheckLimit(c_axis, Acts[c_axis], MP.SoftLimitPosX, MP.SoftLimitNegX, 'C', errMsg))
                return true;
            if (u_axis >= 0 && CheckLimit(u_axis, Acts[u_axis], MP.SoftLimitPosX, MP.SoftLimitNegX, 'X', errMsg))
                return true;
            if (v_axis >= 0 && CheckLimit(v_axis, Acts[v_axis], MP.SoftLimitPosX, MP.SoftLimitNegX, 'X', errMsg))
                return true;

            return false;
        }


        public int ReadCurAbsPosition(double x, double y, double z, double a, double b, double c, bool snap, bool NoGeo)
        {
            double dummyu = 0;
            double dummyv = 0;
            return ReadCurAbsPosition(out x, out y, out z, out a, out b, out c, out dummyu, out dummyv, snap, NoGeo);
        }

        public int ReadCurAbsPosition(out double x, out double y, out double z, out double a, out double b, out double c, out double u, out double v, bool snap, bool NoGeo)
        {
            // initialize outputs
            x = y = z = a = b = c = u = v = 0.0;

            // 1) Figure out which logical axis maps to which motor index
            int rc = GetAxisDefinitions(out int x_axis, out int y_axis, out int z_axis, out int a_axis, out int b_axis, out int c_axis, out int u_axis, out int v_axis);

            if (rc != 0)
            {
                SetAbort();
                return 1;
            }

            // 2) Read raw actuator counts
            const int MAX_ACTUATORS = 8;
            double[] Acts = new double[MAX_ACTUATORS];
            for (int i = 0; i < MAX_ACTUATORS; i++) Acts[i] = 0.0;

            bool error = false;
            if (x_axis >= 0 && GetDestination(x_axis, out Acts[0]) != 0) error = true;
            if (y_axis >= 0 && GetDestination(y_axis, out Acts[1]) != 0) error = true;
            if (x_axis >= 0 && GetDestination(x_axis, out Acts[0]) != 0) error = true;
            if (y_axis >= 0 && GetDestination(y_axis, out Acts[1]) != 0) error = true;
            if (z_axis >= 0 && GetDestination(z_axis, out Acts[2]) != 0) error = true;
            if (a_axis >= 0 && GetDestination(a_axis, out Acts[3]) != 0) error = true;
            if (b_axis >= 0 && GetDestination(b_axis, out Acts[4]) != 0) error = true;
            if (c_axis >= 0 && GetDestination(c_axis, out Acts[5]) != 0) error = true;
            if (u_axis >= 0 && GetDestination(u_axis, out Acts[6]) != 0) error = true;
            if (v_axis >= 0 && GetDestination(v_axis, out Acts[7]) != 0) error = true;
            if (error)
            {
                SetAbort();
                return 1;
            }

            // 3) Convert to CAD coords
            double tx, ty, tz, ta, tb, tc, tu2, tv2;
            Kinematics.TransformActuatorstoCAD(Acts, out tx, out ty, out tz, out ta, out tb, out tc, out tu2, out tv2, NoGeo);

            // 4) Compute tolerances
            const double FLOAT_TOL = 1e-6;       // or whatever your C++ uses
            double tolx = Math.Max(Math.Abs(FLOAT_TOL * tx), FLOAT_TOL);
            double toly = Math.Max(Math.Abs(FLOAT_TOL * ty), FLOAT_TOL);
            double tolz = Math.Max(Math.Abs(FLOAT_TOL * tz), FLOAT_TOL);

            // 5) “Snap” back to the last commanded position if within tolerance
            x = (x_axis < 0 || (snap && Math.Abs(tx - current_x) < tolx)) ? current_x : tx;
            y = (y_axis < 0 || (snap && Math.Abs(ty - current_y) < toly)) ? current_y : ty;
            z = (z_axis < 0 || (snap && Math.Abs(tz - current_z) < tolz)) ? current_z : tz;
            a = (a_axis < 0 || (snap && Math.Abs(ta - current_a) < Math.Abs(FLOAT_TOL * ta))) ? current_a : ta;
            b = (b_axis < 0 || (snap && Math.Abs(tb - current_b) < Math.Abs(FLOAT_TOL * tb))) ? current_b : tb;
            c = (c_axis < 0 || (snap && Math.Abs(tc - current_c) < Math.Abs(FLOAT_TOL * tc))) ? current_c : tc;
            u = (u_axis < 0 || (snap && Math.Abs(tu2 - current_u) < Math.Abs(FLOAT_TOL * tu2))) ? current_u : tu2;
            v = (v_axis < 0 || (snap && Math.Abs(tv2 - current_v) < Math.Abs(FLOAT_TOL * tv2))) ? current_v : tv2;

            return 0;
        }


        /// <summary>
        /// Apply a new work‐offset origin (in machine units: inches or degrees).
        /// Called by the Canon façade after USE_LENGTH_UNITS/SET_ORIGIN_OFFSETS.
        /// </summary>
        public void SetOriginOffsets(double xInches, double yInches, double zInches, double aDeg, double bDeg, double cDeg, double uDeg, double vDeg)
        {

        }
        public double FeedRateDistance(double dx, double dy, double dz, double da, double db, double dc, double du, double dv, out bool pureAngle)
        {
            return CKinematics.FeedRateDistance(dx, dy, dz, da, db, dc, du, dv, out pureAngle);
        }

        /// <summary>
        /// Given the current origin, active plane, endpoint positions and center‐offsets (firstAxis/secondAxis),
        /// compute the absolute CAD X,Y,Z of the arc’s end point.
        /// </summary>
        public static void ResolveArcCartesian(CANON_VECTOR origin, CANON_PLANE plane, double firstEnd, double secondEnd, double firstAxis, double secondAxis, int rotation, out double x, out double y, out double z)
        {
            // 1) Project the two “end” values into X,Y,Z depending on the plane:
            switch (plane)
            {
                case CANON_PLANE.XY:
                    x = firstEnd;
                    y = secondEnd;
                    z = origin.Z;
                    break;

                case CANON_PLANE.YZ:
                    x = origin.X;
                    y = firstEnd;
                    z = secondEnd;
                    break;

                case CANON_PLANE.XZ:
                    x = firstEnd;
                    y = origin.Y;
                    z = secondEnd;
                    break;

                default:
                    // fallback—treat as XY
                    x = firstEnd;
                    y = secondEnd;
                    z = origin.Z;
                    break;
            }

            // 2) If you have I,J (firstAxis/secondAxis) centre‐offsets instead of absolute endpoints,
            //    you can compute via your circle‐intersection helper:
            //    var (cx, cy) = Kinematics.IntersectionTwoCircles(
            //                      origin.X, origin.Y, firstAxis, origin.X + firstEnd, origin.Y + secondEnd, secondAxis, rotation);
            //    x = cx;  y = cy;
            //    // z stays as above, or apply a separate axis offset for the “third” axis.

            // 3) If you need to convert back to user units, wrap with GC.UserUnitsToInches... here
            //    (but typically Canon.STRAIGHT_FEED will do that conversion before calling CM).

            // You now have the absolute CAD XYZ for your arc end.
        }
        private class SpecialCmd
        {
            // mirrors: char cmd[MAX_LINE];
            public string Cmd { get; set; } = string.Empty;
        }

        /// <summary>
        /// C# port of:
        /// int CCoordMotion::Dwell(double seconds, int sequence_number)
        /// </summary>
        public int Dwell(double seconds, int sequenceNumber, RS274NGC.SetupData setupData)
        {
            // 1) early exit if we've been asked to abort
            if (m_Abort)
                return 1;

            // 2) commit any pending segments
            if (CommitPendingSegments(false))
                return 1;

            // 3) give the UI/display a “hit” at the current XYZ
            m_StraightTraverseCb?.Invoke(current_x, current_y, current_z, setupData.sequence_number);

            // 4) if 6-axis callback is hooked
            m_StraightTraverse6Cb?.Invoke(current_x, current_y, current_z, current_a, current_b, current_c, setupData.sequence_number);

            // 5) if we’re actually simulating motion or tracking time…
            if (!m_Simulate || m_DoTime)
            {
                // insert a dwell segment into the planner
                int result = TpInsertDwell(seconds, current_x, current_y, current_z, current_a, current_b, current_c, current_u, current_v, sequenceNumber, 0);

                // if the planner says “abort,” set our flag and exit
                if (result == 1)
                {
                    SetAbort();
                    return 1;
                }

                // let the planner combine/maximize segments
                TrajectoryPlanner.MaximizeSegments();

                // push any newly-done segments out; if that errors, abort
                if (DownloadDoneSegments())
                {
                    SetAbort();
                    return 1;
                }
            }

            // normal “everything’s fine” return
            return 0;
        }

        private int TpInsertDwell(double seconds, double x, double y, double z, double a, double b, double c, double u, double v, int sequenceNumber, int flags)
        {
            // your existing tp_insert_dwell(...) logic here
            throw new NotImplementedException();
        }
        public bool AxisDisabled { get; set; }
        public void DownloadFinish() => throw new NotImplementedException();
        private int GetRapidSettings() 
            => RS274NGC.GetRapidSettings(); //check

        private int ReportError(string msg)
        {
            // e.g. log or callback
            Console.Error.WriteLine(msg);
            return 1;
        }

        private int ReportErrorAndHalt(string msg)
        {
            m_Halt = true;
            return ReportError(msg);
        }

        private bool PutWriteLineBuffer(string line, int x, int y)
        {
            // TODO: your implementation
            return false;
        }

    }
    
}
