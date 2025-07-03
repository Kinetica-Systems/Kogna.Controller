using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;


namespace KinematicEngine
{
    /// <summary>
    /// Full C# port of the DynoMotion CCoordMotion class,
    /// including all public APIs from the original C++ implementation.
    /// </summary>
    public class CCoordMotion : IDisposable
    {
        private double current_x = 0;
        private double current_y = 0;   // a reachable home‐offset
        private double current_z = 0;
        private double current_a = 0;
        private double current_b = 0;
        private double current_c = 0;
        // Motion state
        public double current_u, current_v;

        // Lookahead / download state
        private int _lastSeqNo = 0;
        private int m_nsegs_downloaded;
        private double m_TotalDownloadedTime;
        private double m_TimeAlreadyExecuted;
        private bool m_ThreadingMode;
        private bool m_SegmentsStartedExecuting;
        //private bool m_TapCycleInProgress = false;
        //private int m_PreviouslyStopped;
        //private int m_Stopping = 0;
        //private int STOPPED_NONE = 0;
        //private int m_PreviouslyStoppedType = 0;
        //private int SEG_UNDEFINED = 0;
        //private int m_PreviouslyStoppedID = -1;
        //private bool m_TCP_affects_actuators = true;  // assume Tool Center Point has effects except for simple cases


        // Control flags
        private bool m_Abort;
        private bool m_Halt;
        //public bool Simulate { get; set; } = false;
        public bool DisableSoftLimits { get; private set; } = false;
        public bool AxisDisabled { get; set; }

        /// <summary>Request an abort.</summary>
        public void SetAbort() => m_Abort = true;
        public void ClearAbort() => m_Abort = false;
        public bool GetAbort() => m_Abort;
        //public void SetHalt() => m_Halt = true;
        //public void ClearHalt() => m_Halt = false;
        //public bool GetHalt() => m_Halt;

        public int GetDestination(int axis, out double d)
        {
            GetPosition(axis, out d);
            return 0;
        }
        /// <summary>Clear any pending abort.</summary>

        // Overrides
        //private readonly double m_FeedRateOverride = 1.0;
       // private readonly double m_FeedRateRapidOverride = 1.0;
       // private readonly double m_HardwareFRORange = 0.0;
       // private readonly double m_SpindleRateOverride = 1.0;
        public bool RapidParamsDirty { get; set; } = true;

        // Axis definitions
        private bool m_DefineCS_valid;
        private int x_axis, y_axis, z_axis, a_axis, b_axis, c_axis, u_axis, v_axis;
        // simulation & flow control
        private bool m_Simulate;
        private bool m_DoTime;
       // private bool m_Trace;
        // Write buffer
       // private StringBuilder m_WriteLineBuffer = new StringBuilder();
        //private double m_WriteLineBufferTime;

        // Delegate definitions
        public delegate void StraightTraverseCallback(double x, double y, double z, int seq);
        public delegate void StraightTraverseSixAxisCallback(double x, double y, double z, double a, double b, double c, int seq);
        public delegate void StraightFeedCallback(double rate, double x, double y, double z, int seq, int id);
        public delegate void StraightFeedSixAxisCallback(double rate, double x, double y, double z, double a, double b, double c, int seq, int id);
        //public delegate void ArcFeedCallback(bool zeroLen, double rate, int plane, double fe, double se, double fa, double sa, int rot, double ae, int seq, int id);
        //public delegate void ArcFeedSixAxisCallback(bool zeroLen, double rate, int plane, double fe, double se, double fa, double sa, int rot, double ae, double a, double b, double c, int seq, int id);

        // Callback setters
        private StraightTraverseCallback? m_StraightTraverseCb = null!;
        private StraightTraverseSixAxisCallback? m_StraightTraverse6Cb = null!;
        private StraightFeedCallback? m_StraightFeedCb = null!;
        private StraightFeedSixAxisCallback? m_StraightFeed6Cb = null!;
        //private ArcFeedCallback? m_ArcFeedCb = null!;
        //private ArcFeedSixAxisCallback? m_ArcFeed6Cb = null!;


        private const int MAX_SPECIAL_CMDS = 100;
        private const int MAX_LINE = 100;
        //private bool feed_override = true;	// whether feed override is enabled
        //private bool speed_override = true;  // whether spindle override is enabled
        //private int m_NumLinearNotDrawn = 0;
        // these mirror your C++ globals/member-variables:
        private SpecialCmd[] specialCmds;
        private int[] specialCmdsInitialSequenceNo;
        private int nspecialCmds;
        private int specialCmdsInitialFirst = -1;
        private int specialCmdsInitialLast;
        private int SegBufToggle;
        private int nsegs;
        private int m_nsegsDownloaded = 0;
        private int ispecialCmdDownloaded;
        private const int SEG_LINEAR = 1; //ned to check
        private KEngine.SEGMENT GetSegPtr(int idx) { /* … */ throw new NotImplementedException(); }
        private RS274NGC.SetupData _setup;
        private Kinematics6AxisFanuc _kinematics;
        private TrajectoryPlanner  _planner;
        public KEngine.MOTION_PARAMS  _motionParams;

        private KEngine _kEngine;
        private double[] _lastActs;
        private double[] _currentActs;

        
        public CCoordMotion(Kinematics6AxisFanuc Kinematics, TrajectoryPlanner Planner, RS274NGC.SetupData Setup, KEngine kEngine)
        {
            //  _setup = new RS274NGC.SetupData();
            var path = Path.GetDirectoryName(typeof(CCoordMotion).Assembly.Location)!;
            _setup = Setup;
            _planner = Planner;
            _kinematics = Kinematics;
            _kEngine = kEngine;
            _kinematics.MainPath = path;
            _lastActs = new double[8];
            _currentActs = new double[8];    
            DownloadInit();
            ResetMotionState();

            specialCmds = new SpecialCmd[MAX_SPECIAL_CMDS];
            specialCmdsInitialSequenceNo = new int[MAX_SPECIAL_CMDS];

            for (int i = 0; i < MAX_SPECIAL_CMDS; i++)

                specialCmds[i] = new SpecialCmd();



            // string[] axisFiles = new[] {"X.table", "Y.table", "Z.table", "A.table", "B.table", "C.table", "U.table", "V.table"} .Select(f => Path.Combine(Kinematics.MainPath, f)) .ToArray();


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
            if (_planner.PendingSegments > 0)
            {
                _planner.RoundCorner(0);
                if (rapidMode) _planner.DoSegmentCallbacksRapid();
                else _planner.DoSegmentCallbacks();
                //  if (!TrajectoryPlanner.DoRateAdjustments()) return true;
                _planner.MaximizeSegments();
            }
            return false;
        }
       
        public int StraightFeedAccel(double x0, double y0, double z0, double a0, double b0, double c0, double u0, double v0, double x1, double y1, double z1, double a1, double b1, double c1, double u1, double v1, double feedRate, double accel, bool rapidMode, int seq, int id)
        {
            if (_kinematics == null) throw new InvalidOperationException("_kinematics is null");
            if (_planner   == null) throw new InvalidOperationException("_planner   is null");
            if (_lastActs  == null) throw new InvalidOperationException("_lastActs  is null");

            // 2) Enqueue into the trajectory planner
            if (rapidMode)
            {
                // G0: pure rapid
                _planner.InsertRapidLinearSeg(x0, y0, z0, a0, b0, c0, 0, 0, x1, y1, z1, a1, b1, c1, 0, 0, seq, id);
            }
            else
            {
                // G1: feed with acceleration control
                // Pass feedRate and accel as your MaxVel / MaxAccel
                _planner.InsertLinearSeg(x0, y0, z0, a0, b0, c0, 0, 0, x1, y1, z1, a1, b1, c1, 0, 0, seq, id, feedRate, accel);
            }

            _planner.DoRateAdjustments(0, _planner.SegCount());

            return 0;
        }


        public int ArcFeedAccel(double x0, double y0, double z0, double a0, double b0, double c0, double u0, double v0, double x1, double y1, double z1, double a1, double b1, double c1, double u1, double v1, double i1, double j1, bool DirIsCCW, double feedRate, double accel, int seq, int id)
        {
            if (_kinematics == null) throw new InvalidOperationException("_kinematics is null");
            if (_planner   == null) throw new InvalidOperationException("_planner   is null");
            if (_lastActs  == null) throw new InvalidOperationException("_lastActs  is null");

            double xc = x0 + i1;
            double yc = y0 + j1;
            // 2) Enqueue into the trajectory planner
            if (!DirIsCCW) //if its CW
            {
                Console.WriteLine($"hit G2 exit from coordmotion");
                // G2: CW Arc
                _planner.InsertArcSeg(x0, y0, z0, a0, b0, c0, 0, 0, x1, y1, z1, a1, b1, c1, 0, 0, xc, yc, false, feedRate, accel, seq, id);

            }
            else // if CCW arc
            {
                Console.WriteLine($"hit G3 exit from coordmotion");
                // G3: CCW Arc
                _planner.InsertArcSeg(x0, y0, z0, a0, b0, c0, 0, 0, x1, y1, z1, a1, b1, c1, 0, 0, xc, yc, true, feedRate, accel, seq, id);
            }
            
            _planner.DoRateAdjustments(0, _planner.SegCount());

            return 0;
        }

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
                KEngine.SEGMENT p = GetSegPtr(nsegs - 1);

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
            if (_planner.nsegs > m_nsegs_downloaded)
            {
                var seg = _planner.GetSegment(m_nsegs_downloaded);
                if (seg.Done)
                {
                    if (m_nsegs_downloaded == 0 && !m_Simulate)
                        if (WaitForSegmentsFinished() != 0) { m_Abort = true; return true; }
                    while (m_nsegs_downloaded < _planner.SegCount() && _planner.GetSegment(m_nsegs_downloaded).Done)
                        _planner.OutputSegment(m_nsegs_downloaded++);
                }
            }
            return false;
        }

        public int FlushSegments()
        {
            int a = 1;  //check this
            _planner.RoundCorner(a);
            _planner.MaximizeSegments();
            for (int i = m_nsegs_downloaded; i < _planner.SegCount(); i++)
                if (_planner.OutputSegment(i) != 0) { m_Abort = true; return 1; }
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


        public double GetPosition(int axis, out double pos)
        {
            switch (axis)
            {
                case 1: pos = current_x; break;
                case 2: pos = current_y; break;
                case 3: pos = current_z; break;
                case 4: pos = current_a; break;
                case 5: pos = current_b; break;
                case 6: pos = current_c; break;
                case 7: pos = current_u; break;
                case 8: pos = current_v; break;
                default: pos = 0; break;

            }
        return pos;
    }


        public int GetAxisDone(int axis, out int r)
            => RS274NGC.GetAxisDone(axis, out r);

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
            => _planner.DoRateAdjustments(i0, i1) ? 0 : 1;

        public int DoRateAdjustmentsArc(int i, double rad, double th0, double dth, double dc)
            => _planner.DoRateAdjustmentsArc(i, rad, th0, dth, dc) ? 0 : 1; 

        public int SetRapidSettings(double feed, double accel)
            => RS274NGC.SetRapidSettings((int)feed, accel);

        public int GetRapidSettingsAxis(int axis, out double vel, out double accel, out double decel, out double jerk, out double softPos, out double softNeg, out double countsPerInch, out string axisName)
            => RS274NGC.GetRapidSettingsAxis(axis, out vel, out accel, out decel, out jerk, out softPos, out softNeg, out countsPerInch, out axisName);

        public double MaxDecelTimeForAxis(int axis, double vel, double accel, double jerk)
            => CKinematics.MaxDecelTime(axis, vel, accel, jerk);

        public double GetNominalFROChangeTime(char axis)
            => CKinematics.NominalFROTime(axis);

        public int SetAxisDefinitions(int x, int y, int z, int a, int b, int c, int u, int v)
        {
            x_axis = x; y_axis = y; z_axis = z;
            a_axis = a; b_axis = b; c_axis = c;
            u_axis = u; v_axis = v;
            m_DefineCS_valid = true;
            return 0;
        }

        public int GetAxisDefinitions(out int x, out int y, out int z, out int a, out int b, out int c)
        {
            x = x_axis; y = y_axis; z = z_axis;
            a = a_axis; b = b_axis; c = c_axis;
            m_DefineCS_valid = true;
            return 0;
        }

        public bool IsCoordinateSystemValid() => m_DefineCS_valid;

        // Utility for wait-alias
        public int WaitForMoveXYZABCFinished() => WaitForSegmentsFinished();

        // Push motion parameters into the trajectory planner
        public void SetTPParams() => _planner.SetParams(_kEngine._MOTION_PARAMS);

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
            var MP = _kEngine._MOTION_PARAMS;

            if (DisableSoftLimits)
                return false;

            // Allocate and fill Acts[]
            double[] cartesian = { x, y, z, a, b, c };
            double[] Acts = _kinematics.TransformCADtoActuators(cartesian);

            // 1) Call the int-returning function
            int rc = GetAxisDefinitions(out int x_axis, out int y_axis, out int z_axis, out int a_axis, out int b_axis, out int c_axis);

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
            if (u_axis >= 0 && CheckLimit(u_axis, Acts[u_axis], MP.SoftLimitPosX, MP.SoftLimitNegX, 'U', errMsg))
                return true;
            if (v_axis >= 0 && CheckLimit(v_axis, Acts[v_axis], MP.SoftLimitPosX, MP.SoftLimitNegX, 'V', errMsg))
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
            int rc = GetAxisDefinitions(out int x_axis, out int y_axis, out int z_axis, out int a_axis, out int b_axis, out int c_axis);

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

            double[] cartesian = _kinematics.TransformActuatorsToCAD(Acts);
            double tx = cartesian[0], ty = cartesian[1], tz = cartesian[2];
            double ta = cartesian[3], tb = cartesian[4], tc = cartesian[5];

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
        public int DWELL(double seconds, int sequenceNumber, RS274NGC.SetupData setupData)
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
                _planner.MaximizeSegments();

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
        /// <summary>
        /// Returns the next sequence number (starts at 1).
        /// </summary>
        public int GetNextSequenceNumber()
        {
            return ++_lastSeqNo;
        }
    
    }
    
}
