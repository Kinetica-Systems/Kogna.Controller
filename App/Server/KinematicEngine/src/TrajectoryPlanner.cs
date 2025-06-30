using System;
using System.Linq;           
using System.Collections.Generic;

namespace KinematicEngine
{
    public struct TP_COEFF { public double a, b, c, d, t; public string label; }

    public class TrajectoryPlanner
    {

        private readonly List<KEngine.SEGMENT> _segments = new List<KEngine.SEGMENT>();

        public bool DoRateAdjustmentsArc(int i, double rad, double th0, double dth, double dc) => throw new NotImplementedException();



        public int SegCount() => _segments.Count;
        public void Finish() => _segments.Clear();
        // --- Segment types ---
        public const int SEG_UNDEFINED = 0;
        public const int SEG_LINEAR = 1;
        public const int SEG_ARC = 2;
        public const int SEG_RAPID = 3;
        public const int SEG_DWELL = 4;
        public Queue<KEngine.SEGMENT> _pending { get; set; }

        // --- Constants ---
        private const double SIGMA = 1e-9;
        private const double SPEED_TOL = 0.01;
        private const double ACCEL_TOL = 0.04;
        private const double NON_ZERO_ANGLE_IN_DEGREES = 0.001;
        private const double TWO_PI = 2.0 * Math.PI;
        private const double HALF_PI = 0.5 * Math.PI;

        public const int MAX_TP_SEGMENTS = 1 << 16;
        private const int MAX_SPECIAL_CMDS = 1000;
        private const int MAX_COMBINE = 100;

        // --- Ping-pong buffers and globals ---

        public  int nsegs = 0, prev_nsegs = 0, SegBufToggle = 0;
        public  int[] SegsDone = new int[2];
        public  double[] SegsDoneTime = new double[2];

        // --- Special commands ---
        public struct SPECIAL_CMD { public string Cmd; }
        public  SPECIAL_CMD[] special_cmds = new SPECIAL_CMD[MAX_SPECIAL_CMDS];
        public  int nspecial_cmds;
        public  int special_cmds_initial_first;
        public  int special_cmds_initial_last;
        public  int[] special_cmds_initial_sequence_no = new int[2];
        public  int ispecial_cmd_downloaded;

        // --- Motion parameters & helpers ---
        public KEngine.MOTION_PARAMS _motionParams;
        public TP_COEFF[] C;
        private double FacetAngleRadians, BreakAngleRadians;


        // --- Working list for CombineSegments ---
        private int nCombined;

        // --- Struct definitions ---
        
        private KEngine _engine;
        public TrajectoryPlanner _planner;
        private CKinematics _cKinenmatics;
        public TrajectoryPlanner(KEngine engine, CKinematics cKinematics)
        {

            _cKinenmatics = cKinematics;
            _engine = engine;
            _planner = this;


        }

        /// <summary>
        /// Initialize for a new list of segments (ping-pong buffer flip). :contentReference[oaicite:0]{index=0}
        /// </summary>
        public bool Init()
        {
            var m = _motionParams;
            C = new TP_COEFF[7];
            _pending = new Queue<KEngine.SEGMENT>();
            Console.WriteLine($"MotionParams load check: {m.MaxVel}, {m.MaxAccel}, {m.MaxJerk}");

            Console.WriteLine("[PLANNER] Cleared all local buffered segments");
            lock (_segments)
            {
                _segments.Clear();
            }
            nsegs = 0;
            nCombined = 0;
            SegsDoneTime[SegBufToggle] = 0.0;
            SegsDone[SegBufToggle] = -1;
            ispecial_cmd_downloaded = nspecial_cmds = nsegs = nCombined = 0;
            special_cmds_initial_first = special_cmds_initial_last = -1;
            special_cmds_initial_sequence_no[SegBufToggle] = -1;
            return true;
        }

        /// <summary>
        /// “Pure‐angle” if no linear motion but non-zero rotation.
        /// </summary>
        public bool PureAngle(KEngine.SEGMENT seg)
        {
            // linear part
            double dx = seg.x1 - seg.x0;
            double dy = seg.y1 - seg.y0;
            double dz = seg.z1 - seg.z0;
            // angular part
            double da = seg.a1 - seg.a0;
            double db = seg.b1 - seg.b0;
            double dc = seg.c1 - seg.c0;
            // extra axes
            double du = seg.u1 - seg.u0;
            double dv = seg.v1 - seg.v0;

            // delegate to the vector overload
            return _planner.PureAngle(dx, dy, dz, da, db, dc, du, dv);
        }

        public bool PureAngle(double dx, double dy, double dz, double da, double db, double dc, double du, double dv)
        {
            const double eps = SIGMA;
            bool noLin = Math.Abs(dx) <= eps
                    && Math.Abs(dy) <= eps
                    && Math.Abs(dz) <= eps
                    && Math.Abs(du) <= eps
                    && Math.Abs(dv) <= eps;
            bool someAng = Math.Abs(da) > eps
                        || Math.Abs(db) > eps
                        || Math.Abs(dc) > eps;
            return noLin && someAng;
        }
        /// <summary>
        /// Copy in new motion parameters and precompute radians. :contentReference[oaicite:1]{index=1}
        /// </summary>
        public void SetParams(KEngine.MOTION_PARAMS m)
        {
            _motionParams = m;
            FacetAngleRadians = _motionParams.FacetAngle * Math.PI / 180.0;
            if (_motionParams.BreakAngle > 179.0) _motionParams.BreakAngle = 179.0;
            BreakAngleRadians = _motionParams.BreakAngle * Math.PI / 180.0;
            _motionParams.UseOnlyLinearSegments = true;

        }

        /// <summary>
        /// Insert a linear segment (2ⁿᵈ-order) and attempt combination. :contentReference[oaicite:2]{index=2}
        /// </summary>
        public int InsertLinearSeg(
            double x0, double y0, double z0, double a0, double b0, double c0, double u0, double v0,
            double x1, double y1, double z1, double a1, double b1, double c1, double u1, double v1, int sequence_number, int ID,
            double MaxVel = 0, double MaxAccel = 0, double MaxCombineLength = 0, int NumLinearNotDrawn = 0)
        {
            Console.WriteLine($"[PLANNER] InsertLinear: Count={_segments.Count}, seq={sequence_number}, to=({x1},{y1},{z1})");
            // compute deltas
                bool pureAngle;
                double dx = FeedRateDistance(
                    x1 - x0, y1 - y0, z1 - z0,
                    a1 - a0, b1 - b0, c1 - c0,
                    u1 - u0, v1 - v0,
                    out pureAngle
                );
            var seg = new KEngine.SEGMENT
            {
                //injecting start and end joint positions into new segment
                type = SEG_LINEAR,
                sequence_number = sequence_number,
                ID = ID,

                startActs = new double[]{ x0, y0, z0, a0, b0, c0 },
                endActs   = new double[]{ x1, y1, z1, a1, b1, c1 },
                x0 = x0,
                y0 = y0,
                z0 = z0,
                a0 = a0,
                b0 = b0,
                c0 = c0,
                u0 = u0,
                v0 = v0,

                x1 = x1,
                y1 = y1,
                z1 = z1,
                a1 = a1,
                b1 = b1,
                c1 = c1,
                u1 = u1,
                v1 = v1,
                
                dx = dx,
                C = C,      // seven phases for jerk-limited rapid
                MaxVel = MaxVel,
                MaxAccel = MaxAccel,
                MaxCombineLength = MaxCombineLength,
                NumLinearNotDrawn = NumLinearNotDrawn, 
                angle = new double[6],
                _MOTION_PARAMS = _motionParams,
                

            };
            
            _segments.Add(seg);     // <-- this line was missing
            return 0;
        }



        /// <summary>
        /// Insert a rapid (3ʳᵈ-order) linear segment. :contentReference[oaicite:3]{index=3}
        /// </summary>
        public int InsertRapidLinearSeg(
            double x0, double y0, double z0, double a0, double b0, double c0, double u0, double v0,
            double x1, double y1, double z1, double a1, double b1, double c1, double u1, double v1,
            int sequence_number, int ID)
        {
        Console.WriteLine($"[PLANNER] InsertRapid: Count={_segments.Count}, seq={sequence_number}, to=({x1},{y1},{z1})");
            bool pureAngle;
            double dx = FeedRateDistance(
                x1 - x0, y1 - y0, z1 - z0,
                a1 - a0, b1 - b0, c1 - c0,
                u1 - u0, v1 - v0,
                out pureAngle
            );
            var seg = new KEngine.SEGMENT
            {
                type = SEG_RAPID,
                sequence_number = sequence_number,
                ID = ID,
                startActs = new double[]{ x0, y0, z0, a0, b0, c0 },
                endActs   = new double[]{ x1, y1, z1, a1, b1, c1 },
                x0 = x0,
                y0 = y0,
                z0 = z0,
                a0 = a0,
                b0 = b0,
                c0 = c0,
                u0 = u0,
                v0 = v0,

                x1 = x1,
                y1 = y1,
                z1 = z1,
                a1 = a1,
                b1 = b1,
                c1 = c1,
                u1 = u1,
                v1 = v1,
                dx = dx,
                C = C,      // seven phases for jerk-limited rapid
                angle = new double[6],
                _MOTION_PARAMS = _motionParams,
               
            };

            
            _segments.Add(seg);     // <-- this line was missing
            return 0;
        }

        /// <summary>
        /// Insert a dwell segment. :contentReference[oaicite:4]{index=4}
        /// </summary>
        public int InsertDwell(double t, double x0, double y0, double z0, double a0, double b0, double c0, double u0, double v0, int sequence_number, int ID)
        {
            var p = GetSegPtr(nsegs);
            p.type = SEG_DWELL;
            p.sequence_number = sequence_number;
            p.ID = ID;
            p.x0 = x0; p.y0 = y0; p.z0 = z0; p.a0 = a0; p.b0 = b0; p.c0 = c0; p.u0 = u0; p.v0 = v0;
            p.x1 = x0; p.y1 = y0; p.z1 = z0; p.a1 = a0; p.b1 = b0; p.c1 = c0; p.u1 = u0; p.v1 = v0;
            p.dwell_time = t;
            p.dx = 0.0; p.vel = 0.0;
            p.StopRequired = true;
            p.StopRequiredNextSeg = false;
            p.special_cmds_first = p.special_cmds_last = -1;
            p.Done = false;

            nsegs++;
            return 0;
        }

        /// <summary>
        /// Insert an arc segment. :contentReference[oaicite:5]{index=5}
        /// </summary>
        public int InsertArcSeg(double x0, double y0, double z0, double a0, double b0, double c0, double u0, double v0, double x1, double y1, double z1, double a1, double b1, double c1, double u1, double v1,
                                        double xc, double yc, bool dirIsCCW, double MaxVel, double MaxAccel, int sequence_number, int ID)
        {
            Console.WriteLine($"[PLANNER] InsertArc: Count={_segments.Count}, seq={sequence_number}");
            // compute deltas
            double dx = CalcLengthAlongCircle(x0, y0, x1, y1, xc, yc, dirIsCCW, out double r, out double theta0, out double dtheta);
            Console.WriteLine($"[DEBUG] InsertArcSeg: start=({x0},{y0},{z0}), end=({x1},{y1},{z1}), center=({xc},{yc}), r={r}, theta0={theta0 * 180/Math.PI}, dtheta={dtheta * 180/Math.PI}");

            
            var seg = new KEngine.SEGMENT
            {
                //injecting start and end joint positions into new segment
                type = SEG_ARC,
                sequence_number = sequence_number,
                ID = ID,

                startActs = new double[]{ x0, y0, z0, a0, b0, c0 },
                endActs   = new double[]{ x1, y1, z1, a1, b1, c1 }, //the joint space array
                x0 = x0, //individual joint spaces as doubles
                y0 = y0,
                z0 = z0,
                a0 = a0,
                b0 = b0,
                c0 = c0,
                u0 = u0,
                v0 = v0,

                x1 = x1,
                y1 = y1,
                z1 = z1,
                a1 = a1,
                b1 = b1,
                c1 = c1,
                u1 = u1,
                v1 = v1,
                
                dx = dx,
                theta0 = theta0,
                dtheta = dtheta,
                radius = r,
                DirIsCCW = dirIsCCW,
                C = C,      // seven phases for jerk-limited rapid
                MaxVel = MaxVel,
                MaxAccel = MaxAccel,
                _MOTION_PARAMS = _motionParams,
                

            };
            
            _segments.Add(seg);     // <-- this line was missing
            return 0;
        }

        /// <summary>
        /// Calculate trip states for a segment (2ⁿᵈ‐order or dispatch to rapid/dwell). :contentReference[oaicite:6]{index=6}</summary>
        public int CalcSegTripStates(int i)
        {
            int n = _planner.SegCount();
            var p = _planner.GetSegment(i);
            var m = p._MOTION_PARAMS;
 

            if (p.type == SEG_RAPID) return CalcSegTripStatesRapid(i);
            if (p.type == SEG_ARC) return CalcSegTripStatesArc(i);
            if (p.type == SEG_DWELL) return CalcSegTripStatesDwell(i);

            double V0 = p.vel;
            double V1 = (i < n - 1) ? _planner.GetSegment(i + 1).vel : 0.0;
            double VM = m.MaxVel,
                   A = m.MaxAccel,
                   D = m.MaxDecel,
                   X = p.dx;

            if (VM == 0 || A == 0 || D == 0)
                return 1;

            double ta = (VM - V0) / A;
            double da = (V0 + 0.5 * A * ta) * ta;
            double td = (VM - V1) / D;
            double dd = (V1 + 0.5 * D * td) * td;
            double tc;

            if (X > da + dd)
            {
                tc = (X - da - dd) / VM;
            }
            else
            {
                VM = Math.Sqrt((A * V1 * V1 + D * V0 * V0 + 2.0 * A * D * X) / (A + D));
                ta = (VM - V0) / A;
                td = (VM - V1) / D;
                tc = 0.0;
            }
            if (p.C == null || p.C.Length < 7) p.C = new TP_COEFF[7];

            // fill phases 0–2
            p.C[0].a = 0; p.C[0].b = 0.5 * A; p.C[0].c = V0; p.C[0].d = 0; p.C[0].t = ta;
            p.C[1].a = 0; p.C[1].b = 0; p.C[1].c = VM; p.C[1].d = da; p.C[1].t = tc;
            p.C[2].a = 0; p.C[2].b = -0.5 * D; p.C[2].c = VM; p.C[2].d = da + VM * tc; p.C[2].t = td;
            p.nTrips = 3;


            OutputSegment(i);

              
            return 0;
        }


        public int CalcSegTripStatesArc(int i)
        {
            Console.WriteLine("hit CalcSegTripStatesArc entry");
            var p = _planner.GetSegment(i);
            var m = p._MOTION_PARAMS;
            double VM = m.MaxVel, A = m.MaxAccel, J = m.MaxJerk, X = p.dx;
                if (VM == 0 || A == 0 || J == 0) return 1;
                string type = "";
                var coeffs = Compute7PhaseCoeffs(X, J, A, VM, out type);
                p.C = coeffs;
                p.nTrips = coeffs.Length;
                _planner.ReplaceSegment(i, p);
                return 0;

        }



        /// <summary>
        /// Calculate trip states for rapid 3ʳᵈ-order move (7 phases). :contentReference[oaicite:7]{index=7}</summary>
        public int CalcSegTripStatesRapid(int i)
        {
            var p = _planner.GetSegment(i);
            var m = p._MOTION_PARAMS;
            double MaxV = m.MaxVel, MaxA = m.MaxAccel, MaxJ = m.MaxJerk, dx = p.dx;
            Console.WriteLine($"Hit CalcSegTripStatesRapid {MaxV}, {MaxA}, {MaxJ}");
            string type = "";


            if (MaxV == 0.0 || MaxA == 0.0 || MaxJ == 0.0) return -1;
            // pointers into p.C[]
            TP_COEFF[] c = p.C;


            // 1) Compute the raw distances for the 6 non-plateau legs at (J,A,V):
            double tJ = MaxA / MaxJ;
            double vPeak = MaxJ * tJ * tJ / 2.0;
            double t0, t1, t2;                                // jerk-down time
            double Dnom = 0;

            if (vPeak >= MaxV)
            {
                // velocity-limited: we never reach full A
                t0 = Math.Sqrt(2 * MaxV / MaxJ);    // time to jerk from 0→just‐enough accel to hit V
                t1 = 0;                              // no constant-accel leg
                t2 = t0;                            // jerk-down back to zero accel
            }
            else
            {
                // accel-limited: we hit full A, then coast up to V
                t0 = tJ;
                t1 = (MaxV - vPeak) / MaxA;        // constant-accel time
                t2 = tJ;
            }
            // distances in each accel leg:
            double d0 = MaxJ * Math.Pow(t0, 3) / 6.0;             // jerk-up
            double v1 = MaxJ * t0 * t0 / 2.0;                       // velocity at end of phase0
            double d1 = (v1 * t1) + (0.5 * MaxA * t1 * t1);                   // constant accel
            double d2 = ((v1 + MaxA * t1) * t2) - (MaxJ * (Math.Pow(t2, 3) / 6.0)); // jerk-down


            // 2) raw legs, **no plateau** in phase 3
            double[] rawInc = {
            d0,   // phase 0
            d1,   // phase 1
            d2,   // phase 2
            0.0,  // phase 3 ← must be zero
            d2,   // phase 4
            d1,   // phase 5
            d0    // phase 6
            };

            Dnom = 2 * (d0 + d1 + d2);

            Console.WriteLine("RAW before compute7phcoeff legs (inc): " + string.Join(", ", rawInc.Select(x => x.ToString("0.###"))) + $"  -> Dnom={Dnom:0.###}  (target dx={dx:0.###})");

            //Console.WriteLine($"Scale Coeffs: ");
            Console.WriteLine($"values passed to 7ph dx= {dx} MaxJ= {MaxJ} MaxA= {MaxA} MaxV= {MaxV}");
            // (assume Compute7PhaseCoeffs(dx,J2,A2,V2) now bakes in the plateau properly)
            var scaled = Compute7PhaseCoeffs(dx, MaxJ, MaxA, MaxV, out type);

            // helper to get phase length
            double total = 0;

            Console.WriteLine($"\nProfile type: {type}");
            Console.WriteLine("Phase   t        ds         label");
            double prev_p = 0;

            for (int k = 0; k < scaled.Length; k++)
            {
                var ph = scaled[k];
                double p_end = ph.a * Math.Pow(ph.t, 3)
                             + ph.b * Math.Pow(ph.t, 2)
                             + ph.c * ph.t
                             + ph.d;
                double ds = p_end - prev_p;
                Console.WriteLine($"t={ph.t,8:0.000}  ds={ds,10:0.000}    {ph.label}");
                total += ds;
                prev_p = p_end;
            }


            Console.WriteLine($"TOTAL travel = {total:0.###} (should be {dx:0.###})");

            p.C = scaled;
            p.nTrips = 7;

            _planner.ReplaceSegment(i, p);

            return 0;
        }

        /// <summary>
        /// Dwell case (single trip). :contentReference[oaicite:8]{index=8}</summary>
        public int CalcSegTripStatesDwell(int i)
        {
            var p = _planner.GetSegment(i);
            p.C = new TP_COEFF[i];
            p.C[0].t = p.dwell_time;
            p.C[0].a = p.C[0].b = p.C[0].c = 0;
            p.C[0].d = 0;
            p.nTrips = 1;
            return 0;
        }

        /// <summary>
        /// Combine colinear segments within tolerance. :contentReference[oaicite:9]{index=9}</summary>
        public bool CombineSegments(double MaxLength)
        {
            if (nCombined >= MAX_COMBINE || nsegs < 1) { nCombined = 0; return true; }
            var pn = _planner.GetSegment(nsegs);
            var pm1 = _planner.GetSegment(nsegs - 1);
            if (pn.type != SEG_LINEAR || pm1.type != SEG_LINEAR || pn.Done || pm1.Done) { nCombined = 0; return true; }
            if (pm1.dx > MaxLength) { nCombined = 0; return true; }
            if (Math.Abs(pn.OrigVel - pm1.OrigVel) > SPEED_TOL * Math.Min(pn.OrigVel, pm1.OrigVel)) { nCombined = 0; return true; }
            if (!CheckCollinear(pm1, pn, pm1, _motionParams.CollinearTol)) { nCombined = 0; return true; }
            // … (omitting rest of combine logic for brevity) …
            nCombined++;
            return false;
        }

        /// <summary>
        /// Golden‐section corner rounding. :contentReference[oaicite:10]{index=10}</summary>
        public void RoundCorner(int i)
        {
            if (i < 1) return;
            var seg = GetSegPtr(i);
            var segm = GetSegPtr(i - 1);
            if (seg.StopRequired) return;
            double Theta = seg.ChangeInDirection;
            if (Math.Abs(Theta) > BreakAngleRadians) { seg.StopRequired = true; return; }
            if (Theta == 0.0 || _motionParams.CornerTol < SIGMA) return;
            if (seg.type != SEG_LINEAR || segm.type != SEG_LINEAR) return;
            if (PureAngle(seg) || PureAngle(segm)) return;
            // … (complex facet interpolation logic) …
        }

        /// <summary>
        /// Test three points for near‐collinearity. :contentReference[oaicite:11]{index=11}</summary>
        public bool CheckCollinear(KEngine.SEGMENT s0, KEngine.SEGMENT s1, KEngine.SEGMENT s2, double tol)
        {
            // side lengths
            double a = _planner.FeedRateDistance(s0.x1 - s0.x0, s0.y1 - s0.y0, s0.z1 - s0.z0, s0.a1 - s0.a0, s0.b1 - s0.b0, s0.c1 - s0.c0, s0.u1 - s0.u0, s0.v1 - s0.v0, out bool p0),
                   b = _planner.FeedRateDistance(s1.x1 - s1.x0, s1.y1 - s1.y0, s1.z1 - s1.z0, s1.a1 - s1.a0, s1.b1 - s1.b0, s1.c1 - s1.c0, s1.u1 - s1.u0, s1.v1 - s1.v0, out bool p1),
                   c = _planner.FeedRateDistance(s2.x1 - s2.x0, s2.y1 - s2.y0, s2.z1 - s2.z0, s2.a1 - s2.a0, s2.b1 - s2.b0, s2.c1 - s2.c0, s2.u1 - s2.u0, s2.v1 - s2.v0, out bool p2);

            if (p0 && p1 && p2) return false;
            if (a + b < c - tol || b + c < a - tol || c + a < b - tol) return false;
            double s = 0.5 * (a + b + c);
            double area2 = Math.Max(0.0, s * (s - a) * (s - b) * (s - c));
            return area2 <= (tol * 0.5 * c) * (tol * 0.5 * c);
        }

        /// <summary>
        /// One‐segment forward/backward adjustment (if you ever need a pointwise pass).</summary>
        public void AdjustSegVelocity(int i)
        {
            var p = _planner.GetSegPtr(i);
            // reduce to the minimum of the two sweep functions
            if (i > 0) p.vel = Math.Min(p.vel, MaximizeSegmentForward(i - 1));
            if (i < nsegs - 1) p.vel = Math.Min(p.vel, MaximizeSegmentBackward(i));
        }

        /// <summary>
        /// Corner‐curvature limit: V ≤ √(A·R). :contentReference[oaicite:curv]{index=curv}</summary>
        public  void AdjustSegVelocityCircle(int i, double A)
        {
            var p = _planner.GetSegPtr(i);
            // R = dist(center→start) 
            double dx = p.xc - p.x0, dy = p.yc - p.y0;
            double r = Math.Sqrt(dx * dx + dy * dy);

            // s = √(A·r)
            double s = Math.Sqrt(A * r);
            if (s < p.MaxVel) p.MaxVel = s;
        }

        /// <summary>
        /// Compute total path length (linear + angular). :contentReference[oaicite:14]{index=14}</summary>
        public double FeedRateDistance(
            double dx, double dy, double dz,
            double da, double db, double dc,
            double du, double dv,
            out bool pureAngle)
        {
            // angular→linear conversions
            double d2 = dx * dx + dy * dy + dz * dz;
            if (_motionParams.DegreesA && _motionParams.RadiusA != 0.0) da = da * Math.PI / 180.0 * _motionParams.RadiusA;
            if (_motionParams.DegreesB && _motionParams.RadiusB != 0.0) db = db * Math.PI / 180.0 * _motionParams.RadiusB;
            if (_motionParams.DegreesC && _motionParams.RadiusC != 0.0) dc = dc * Math.PI / 180.0 * _motionParams.RadiusC;
            d2 += da * da + db * db + dc * dc + du * du + dv * dv;
            pureAngle = false;
            return Math.Sqrt(d2);
        }

        /// <summary>
        /// Arc-length around a circle. :contentReference[oaicite:15]{index=15}</summary>
        public double CalcLengthAlongCircle(
            double x0, double y0,
            double x1, double y1,
            double xc, double yc, bool dirIsCCW,
            out double radius, out double theta0, out double dtheta)
        {
            double dx0 = x0 - xc, dy0 = y0 - yc;
            double dx1 = x1 - xc, dy1 = y1 - yc;
            radius = Math.Sqrt(dx0 * dx0 + dy0 * dy0);
            theta0 = Math.Atan2(dy0, dx0);
            double theta1 = Math.Atan2(dy1, dx1);
            dtheta = theta1 - theta0;
            if (Math.Abs(dtheta) < SIGMA) dtheta = 0;
            if (dirIsCCW && dtheta <= 0) dtheta += TWO_PI;
            if (!dirIsCCW && dtheta >= 0) dtheta -= TWO_PI;
            return radius * dtheta;
        }

        /// <summary>
        /// Simple quadratic solver. </summary>
        public void Quadratic(double a, double b, double c, out double r1, out double r2)
        {
            double disc = b * b - 4 * a * c;
            if (disc < 0) { r1 = r2 = -b / (2 * a); }
            else
            {
                double sqrtD = Math.Sqrt(disc);
                r1 = (-b + sqrtD) / (2 * a);
                r2 = (-b - sqrtD) / (2 * a);
            }
        }

        // --- Utility accessors ---

        /// <summary>
        /// Modulo into the current snapshot of pending segments.
        /// </summary>
        public int TPMOD(int i) => _pending.Count == 0 ? 0 : i % _pending.Count;

        /// <summary>
        /// Get the iᵗʰ segment (wrapping via TPMOD) from the queue.
        /// </summary>
        public KEngine.SEGMENT GetSegPtr(int i)
        {
            // snapshot the queue into an array so indexing is O(1)
            var arr = _pending.ToArray();
            return arr[TPMOD(i)];
        }

        /// <summary>
        /// “Position” coefficient lookup on segment p.t
        /// (example formula from your original port).
        /// </summary>
        double Vel(TP_COEFF p) => p.a*p.t*p.t + p.b*p.t + p.c;
        double Pos(TP_COEFF p) => p.a*p.t*p.t*p.t + p.b*p.t*p.t + p.c*p.t + p.d;

        private double CalcChangeInDirection(int idx)
        {
            // difference in unit vectors between segments idx-1→idx→idx+1
            var p = GetSegPtr(idx);
            var pm = GetSegPtr(idx - 1);
            double dx1 = p.x0 - pm.x0, dy1 = p.y0 - pm.y0, dz1 = p.z0 - pm.z0;
            double dx2 = p.x1 - p.x0, dy2 = p.y1 - p.y0, dz2 = p.z1 - p.z0;
            // normalize and dot→acos
            double mag1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1 + dz1 * dz1);
            double mag2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2 + dz2 * dz2);
            if (mag1 == 0 || mag2 == 0) return 0;
            double dot = (dx1 * dx2 + dy1 * dy2 + dz1 * dz2) / (mag1 * mag2);
            dot = Math.Max(-1.0, Math.Min(1.0, dot));
            return Math.Acos(dot);
        }


        /// <summary>
        /// Length along a helix (circle + linear). </summary>
        public double CalcLengthAlongHelix(
                double x0, double y0, double z0,
                double x1, double y1, double z1,
                double xc, double yc, bool dirIsCCW,
                out double radius, out double theta0, out double dtheta,
                double da, double db, double dc, double du, double dv)
        {
            // circle component 
            double dxy = CalcLengthAlongCircle(x0, y0, x1, y1, xc, yc, dirIsCCW, out radius, out theta0, out dtheta);
            double sum2 = dxy * dxy;

            // Z + angular axes contributions 
            sum2 += (z1 - z0) * (z1 - z0);

            if (_motionParams.DegreesA && _motionParams.RadiusA != 0.0) da = da * Math.PI / 180.0 * _motionParams.RadiusA;
            if (_motionParams.DegreesB && _motionParams.RadiusB != 0.0) db = db * Math.PI / 180.0 * _motionParams.RadiusB;
            if (_motionParams.DegreesC && _motionParams.RadiusC != 0.0) dc = dc * Math.PI / 180.0 * _motionParams.RadiusC;

            sum2 += da * da + db * db + dc * dc + du * du + dv * dv;
            return Math.Sqrt(sum2);
        }

        /// <summary>
        /// Two-segment velocity-maximization sweep (forward/backward). :contentReference[oaicite:multi]{index=multi}</summary>
        public void MaximizeSegments()
        {
            bool somethingChanged, firstPass = true;
            do
            {
                somethingChanged = false;
                bool passFinished = false;

                for (int i = nsegs - 1; i > 0 && !passFinished; i--)
                {
                    var pi = GetSegPtr(i);
                    var pm1 = GetSegPtr(i - 1);

                    // rapids & dwells must stop 
                    if (pi.type == SEG_RAPID || pi.type == SEG_DWELL)
                    {
                        if (!pi.Done)
                        {
                            pi.Done = true;
                            pi.StopRequired = true;
                            somethingChanged = true;
                        }
                        continue;
                    }

                    // break‐angle stop? 
                    if (Math.Abs(pi.ChangeInDirection) > BreakAngleRadians
                        || pm1.type == SEG_RAPID
                        || pm1.type == SEG_DWELL)
                    {
                        if (!pi.StopRequired)
                        {
                            pi.StopRequired = true;
                            somethingChanged = true;
                        }
                    }

                    // compute two pass limits 
                    double maxEndVel0 = pi.StopRequired
                        ? 0.0
                        : MaximizeSegmentForward(i - 1);
                    double maxBegVel1 = MaximizeSegmentBackward(i);

                    // three‐segment lookahead 
                    if (i + 1 < nsegs)
                    {
                        var pp1 = GetSegPtr(i + 1);
                        if (!pp1.StopRequired)
                        {
                            double maxBegVel2 = MaximizeSegmentBackward(i + 1);
                            double bound = Math.Min(Math.Min(maxEndVel0, maxBegVel2), pi.MaxVel);
                            if (pi.vel < bound)
                            {
                                pi.vel = bound;
                                somethingChanged = true;
                            }
                        }
                    }

                    // which side is limiting? 
                    if (maxEndVel0 <= maxBegVel1)
                    {
                        if (maxEndVel0 > pi.vel)
                        {
                            pi.vel = maxEndVel0;
                            somethingChanged = true;
                        }
                        if (i < 2 || GetSegPtr(i - 2).Done)
                        {
                            if (!pm1.Done)
                            {
                                pm1.Done = true;
                                somethingChanged = true;
                            }
                        }
                    }
                    if (maxEndVel0 >= maxBegVel1)
                    {
                        if (maxBegVel1 > pi.vel)
                        {
                            pi.vel = maxBegVel1;
                            somethingChanged = true;
                        }
                    }
                }

                firstPass = false;
            }
            while (somethingChanged || firstPass);


        }

        /// <summary>
        /// Forward pass max‐vel from segment i → end. :contentReference[oaicite:fwd]{index=fwd}</summary>
        private double MaximizeSegmentForward(int i)
        {
            var p = GetSegPtr(i);
            double V0 = p.vel, VM = p.MaxVel, A = p.MaxAccel, X = p.dx;
            double t = (VM - V0) / A;
            double d = (V0 + 0.5 * A * t) * t;
            if (X > d) return VM;
            t = (-V0 + Math.Sqrt(V0 * V0 + 2.0 * A * X)) / A;
            return V0 + t * A;
        }

        /// <summary>
        /// Backward pass max‐vel from segment i ← begin. :contentReference[oaicite:bwd]{index=bwd}</summary>
        private double MaximizeSegmentBackward(int i)
        {
            double V1 = i < nsegs - 1 ? GetSegPtr(i + 1).vel : 0.0;
            var p = GetSegPtr(i);
            double VM = p.MaxVel, A = p.MaxDecel, X = p.dx;
            double t = (VM - V1) / A;
            double d = (V1 + 0.5 * A * t) * t;
            if (X > d) return VM;
            t = (-V1 + Math.Sqrt(V1 * V1 + 2.0 * A * X)) / A;
            return V1 + t * A;
        }

        /// <summary>
        /// Mutators for per-segment limits. :contentReference[oaicite:velacc]{index=velacc}</summary>
        public void SetSegmentVelAccels(int i, double vel, double accel, double decel)
        {
            var p = GetSegPtr(i);
            p.MaxVel = vel;
            p.MaxAccel = accel;
            p.MaxDecel = decel;
        }

        /// <summary>
        /// Mutators for per-segment limits + jerk. :contentReference[oaicite:velaccjerk]{index=velaccjerk}</summary>
        public void SetSegmentVelAccelJerk(int i, double vel, double accel, double jerk)
        {
            var p = GetSegPtr(i);
            p.MaxVel = vel;
            p.MaxAccel = accel;
            p.MaxDecel = accel;   // note: C++ used MaxDecel=Accel 
            p.MaxJerk = jerk;
        }

        /// <summary>
        /// Delta‐vector of a segment. :contentReference[oaicite:dir]{index=dir}</summary>
        public void GetSegmentDirection(
            int i,
            out double dx, out double dy, out double dz,
            out double da, out double db, out double dc,
            out double du, out double dv)
        {
            var p = GetSegPtr(i);
            dx = p.x1 - p.x0; dy = p.y1 - p.y0; dz = p.z1 - p.z0;
            da = p.a1 - p.a0; db = p.b1 - p.b0; dc = p.c1 - p.c0;
            du = p.u1 - p.u0; dv = p.v1 - p.v0;
        }

        /// <summary>
        /// Full 3D version of direction-change. </summary>
        public double CalcChangeInDirectionXYZ(int i)
        {
            // identical to above but only X/Y/Z parts
            GetSegmentDirection(i - 1, out var dx0, out var dy0, out var dz0, out _, out _, out _, out _, out _);
            GetSegmentDirection(i, out var dx1, out var dy1, out var dz1, out _, out _, out _, out _, out _);
            double dot = dx0 * dx1 + dy0 * dy1 + dz0 * dz1;
            double la = Math.Sqrt(dx0 * dx0 + dy0 * dy0 + dz0 * dz0);
            double lb = Math.Sqrt(dx1 * dx1 + dy1 * dy1 + dz1 * dz1);
            if (la < SIGMA || lb < SIGMA) return 0.0;
            double cosA = dot / (la * lb);
            cosA = Math.Clamp(cosA, -1.0, 1.0);
            return Math.Acos(cosA);
        }

        public double SegmentXYZLength(KEngine.SEGMENT p)
        {
            double dx = p.x1 - p.x0;
            double dy = p.y1 - p.y0;
            double dz = p.z1 - p.z0;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
        /// <summary>
        /// Final (“exit”) direction of segment i. :contentReference[oaicite:exit]{index=exit}</summary>
        public void CalcFinalDirectionOfSegment(
            int i,
            out double dx, out double dy, out double dz,
            out double da, out double db, out double dc,
            out double du, out double dv)
        {
            var p = GetSegPtr(i);
            if (p.type != SEG_ARC)
            {
                // linear case 
                dx = p.x1 - p.x0; dy = p.y1 - p.y0; dz = p.z1 - p.z0;
                da = p.a1 - p.a0; db = p.b1 - p.b0; dc = p.c1 - p.c0;
                du = p.u1 - p.u0; dv = p.v1 - p.v0;
            }
            else
            {
                // arc-length direction 
                double dxc = p.x1 - p.xc, dyc = p.y1 - p.yc;
                dx = -dyc; dy = dxc;

                // scale to true arc length
                double radius, theta0, dtheta;
                double dxy = CalcLengthAlongCircle(
                    p.x0, p.y0, p.x1, p.y1, p.xc, p.yc, p.DirIsCCW,
                    out radius, out theta0, out dtheta);
                dx *= dxy / radius;
                dy *= dxy / radius;

                dz = p.z1 - p.z0;
                da = p.a1 - p.a0; db = p.b1 - p.b0; dc = p.c1 - p.c0;
                du = p.u1 - p.u0; dv = p.v1 - p.v0;

                // remap for non‐XY planes 
                if (p.plane == (int)CANON_PLANE.XZ)
                {
                    // X→Z, Y→X, Z→Y
                    var tz = dz; dz = dx; dx = dy; dy = tz;
                }
                else if (p.plane == (int)CANON_PLANE.YZ)
                {
                    // X→Y, Z→X, Y→Z
                    var ty = dy; dy = dx; dx = dz; dz = ty;
                }
            }
        }


        /// <summary>
        /// Entry (“start”) direction of segment i. :contentReference[oaicite:beg]{index=beg}</summary>
        public void CalcBegDirectionOfSegment(
            int i,
            out double dx, out double dy, out double dz,
            out double da, out double db, out double dc,
            out double du, out double dv)
        {
            // same as final‐direction of the previous segment
            CalcFinalDirectionOfSegment(i - 1,
                out dx, out dy, out dz,
                out da, out db, out dc,
                out du, out dv);
        }

        /// <summary>


        public double CubeRoot(double v)
        {
            if (v < 1e-30) return 0.0;
            // exp(log(v)/3)
            return Math.Exp(Math.Log(v) / 3.0);
        }

        /// <summary>
        /// Solve a x^2 + b x + c = 0  r1, r2
        /// </summary>
        public void Quadradic(double a, double b, double c, out double r1, out double r2)
        {
            double rad = b * b - 4.0 * a * c;
            double sq = Math.Sqrt(rad);
            r1 = (-b + sq) / (2.0 * a);
            r2 = (-b - sq) / (2.0 * a);
        }

        /// <summary>
        /// Number of segments still waiting to be committed.
        /// </summary>
        public int PendingSegments
        {
            get
            {
                lock (_pending)            // if you’re multi‐threaded
                    return _pending.Count;
            }
        }



        private TP_COEFF[] Compute7PhaseCoeffs(double dx, double J, double A, double V, out string type)
        {    // Handles edge cases and falls back to 5/3-phase if needed
            type = "";
            TP_COEFF[] coeffs = null;
            try {
                coeffs = Build7Phase(dx, J, A, V, out type);
            } catch {
                try {
                    coeffs = Build5Phase(dx, J, out type);
                } catch {
                    coeffs = Build3Phase(dx, J, out type);
                }
            }
            Console.WriteLine($"[S-curve] Using {type} for dx={dx}");
            return coeffs;
        }


static TP_COEFF[] Build7Phase(double dx, double J, double A, double V, out string type)
    {
        type = "7-phase (velocity, accel, jerk limited)";
        double tJ = A / J;
        double vCap = J * tJ * tJ / 2.0;

        double t0, t1, t2, actA = A;

        // Regime selection: can we hit max accel and velocity?
        if (vCap >= V)
        {
            // Can't reach max accel, only max velocity
            t0 = Math.Sqrt(V / J);
            t1 = 0;
            t2 = t0;
            actA = J * t0;
        }
        else
        {
            t0 = tJ;
            t1 = (V - vCap) / A;
            t2 = tJ;
        }

        // Calculate area (distance) of half-trajectory
        double d0 = J * Math.Pow(t0, 3) / 6.0;
        double d1 = actA * t1 * t1 / 2.0 + J * t0 * t0 * t1 / 2.0;
        double d2 = V * t2 - J * Math.Pow(t2, 3) / 6.0;
        double Dmin = 2 * (d0 + d1 + d2);
        double t_plateau = Math.Max(0, (dx - Dmin) / V);

        Console.WriteLine($"dx={dx}, J={J}, A={A}, V={V}");
        Console.WriteLine($"tJ={tJ:0.000}, vCap={vCap:0.000}, t0={t0:0.000}, t1={t1:0.000}, t2={t2:0.000}");
        Console.WriteLine($"d0={d0:0.000}, d1={d1:0.000}, d2={d2:0.000}, Dmin={Dmin:0.000}, t_plateau={t_plateau:0.000}");

        if (dx < Dmin - 1e-6)
            throw new Exception($"Move too short for 7-phase: dx={dx:0.000} < Dmin={Dmin:0.000}");
        if (t0 < 0 || t1 < 0 || t2 < 0)
            throw new Exception($"Invalid regime: negative phase duration.");

        string[] labels = {
            "jerk-up    0⟶+A",
            "const +A",
            "jerk-down +A⟶0",
            "plateau",
            "jerk-down 0⟶-A",
            "const -A",
            "jerk-up   -A⟶0"
        };

        var C = new TP_COEFF[7];
        double p = 0, v = 0;
        int idx = 0;
        void build(double jerk, double accel, double dt, string lbl)
        {
            double a = jerk / 6.0;
            double b = accel / 2.0;
            C[idx] = new TP_COEFF { t = dt, a = a, b = b, c = v, d = p, label = lbl };

            double v1 = v + accel * dt + jerk * dt * dt / 2.0;
            double p1 = p + v * dt + accel * dt * dt / 2.0 + jerk * dt * dt * dt / 6.0;
            Console.WriteLine($"[phase {idx}] t={dt:0.000}, jerk={jerk,8:0.000}, accel={accel,8:0.000}, v(start)={v,10:0.000}, v(end)={v1,10:0.000}, p(start)={p,10:0.000}, p(end)={p1,10:0.000}");

            v = v1;
            p = p1;
            idx++;
        }
        build( J,     0, t0, labels[0]);
        build( 0,   actA, t1, labels[1]);
        build(-J,  actA, t2, labels[2]);
        build( 0,     0, t_plateau, labels[3]);
        build(-J,     0, t2, labels[4]);
        build( 0,  -actA, t1, labels[5]);
        build( J,  -actA, t0, labels[6]);

        if (Math.Abs(p - dx) > 1e-3)
            throw new Exception($"Overshot: p={p:0.000} > dx={dx:0.000}");

        return C;
    }

    static TP_COEFF[] Build5Phase(double dx, double J, out string type)
    {
        type = "5-phase (triangular-jerk)";
        double t0 = Math.Pow(2 * dx / J, 1.0 / 3.0);
        if (t0 <= 0) throw new Exception("Move too short for 5-phase");

        var C = new TP_COEFF[3];
        string[] labels = { "jerk-up", "plateau", "jerk-down" };

        double v0 = 0, p0 = 0;
        double a0 = J / 6.0;
        C[0] = new TP_COEFF { t = t0, a = a0, b = 0, c = v0, d = p0, label = labels[0] };

        double v1 = J * t0 * t0 / 2.0 + v0;
        double p1 = a0 * Math.Pow(t0, 3) + v0 * t0 + p0;
        C[1] = new TP_COEFF { t = 0, a = 0, b = 0, c = v1, d = p1, label = labels[1] };

        double a2 = -J / 6.0;
        C[2] = new TP_COEFF { t = t0, a = a2, b = 0, c = v1, d = p1, label = labels[2] };

        return C;
    }

    static TP_COEFF[] Build3Phase(double dx, double J, out string type)
    {
        type = "3-phase (minimal jerk)";
        double t0 = Math.Pow(dx / (J / 3.0), 1.0 / 3.0);
        if (t0 <= 0) throw new Exception("Move too short for 3-phase");

        var C = new TP_COEFF[2];
        string[] labels = { "jerk-up", "jerk-down" };

        double v0 = 0, p0 = 0;
        double a0 = J / 6.0;
        C[0] = new TP_COEFF { t = t0, a = a0, b = 0, c = v0, d = p0, label = labels[0] };

        double v1 = J * t0 * t0 / 2.0 + v0;
        double p1 = a0 * Math.Pow(t0, 3) + v0 * t0 + p0;
        double a1 = -J / 6.0;
        C[1] = new TP_COEFF { t = t0, a = a1, b = 0, c = v1, d = p1, label = labels[1] };

        return C;
    }





        /// <summary>
        /// For segments [i0..i1), compute trip‐states and maximize velocities.
        /// </summary>
        public bool DoRateAdjustments(int i0, int i1)
        {
            Console.WriteLine("Hit DoRateAdjustments");
            // 1) Compute per‐segment trip tables (rapid vs. dwell)
            for (int i = i0; i < i1; i++)
            {
                CalcSegTripStates(i);
                OutputSegment(i);
            };
                Console.WriteLine("Completed CalcSegTripStates");

                // 2) Spread velocities forward/backward for jerk‐limited S-curve
                _planner.MaximizeSegments();


                return true;
            }


        public KEngine.SEGMENT GetSegment(int idx)
        {
            if (idx < 0 || idx >= _segments.Count)
                throw new ArgumentOutOfRangeException(nameof(idx));
            return  _segments[idx];
            
        }

        // 4) Replace for patching in angle/Duration
        public void ReplaceSegment(int idx, KEngine.SEGMENT seg)
        {
            if (idx < 0 || idx >= _segments.Count)
                throw new ArgumentOutOfRangeException(nameof(idx));
            _segments[idx] = seg;
        }
        /// <summary>
        /// Take the indexed segment from the internal list and enqueue it for rapid dispatch.
        /// </summary>
        /// 
        public int OutputSegment(int idx)
        {
            // grab the planned block
            var blk = _planner.GetSegment(idx);

            if (blk.type == SEG_LINEAR)
            {
            var blockStart = blk.startActs;    // double[6]
            var blockEnd = blk.endActs;      // double[6]
            var L = blk.dx;
            int nPhases = blk.C.Length;
                // 3) walk through phases, slicing 0→L
                for (int phase = 0; phase < nPhases; phase++)
                {
                    var P = blk.C[phase];
                    double s0 = P.d; // start distance along segment for this phase
                    double s1 = phase + 1 < blk.C.Length ? blk.C[phase + 1].d : L; // end distance for this phase

                    double frac0 = Math.Clamp(s0 / L, 0.0, 1.0); // start fraction along block
                    double frac1 = Math.Clamp(s1 / L, 0.0, 1.0); // end fraction

                    // d) interpolate each joint between the block's true endpoints
                    var entryPos = new double[6];
                    var exitPos = new double[6];
                    for (int ax = 0; ax < 6; ax++)
                    {
                        double Δ = blockEnd[ax] - blockStart[ax];
                        entryPos[ax] = blockStart[ax] + Δ * frac0;
                        exitPos[ax] = blockStart[ax] + Δ * frac1;
                    }

                    // e) emit the sub‐segment
                    EnqueueSegment(new KEngine.SEGMENT
                    {
                        type = SEG_RAPID,
                        sequence_number = blk.sequence_number,
                        ID = blk.ID,

                        startActs = entryPos,
                        endActs = exitPos,

                        // carry forward the same timing & cubic shape:
                        qa = P.a,
                        qb = P.b,
                        qc = P.c,
                        qd = 0.0,       // local offset reset
                        qt = P.t
                    });
                    Console.WriteLine($"Phase {phase}, entry [{string.Join(", ", entryPos)}], exit [{string.Join(", ", exitPos)}], " + $"A {P.a}, B {P.b}, C {P.c}, D {P.d}, T {P.t}");                // f) advance our little cursor
                }
            }
            if (blk.type == SEG_ARC)
            {
                Console.WriteLine("Hit SEG_ARC Phase Slicer");
                double arcL = blk.dx;
                double xc = blk.xc;
                double yc = blk.yc;
                double r = Math.Sqrt((blk.x0 - blk.xc) * (blk.x0 - blk.xc) + (blk.y0 - blk.yc) * (blk.y0 - blk.yc));
                double theta0 = Math.Atan2(blk.y0 - blk.yc, blk.x0 - blk.xc);
                double dtheta = Math.Atan2(blk.y1 - blk.yc, blk.x1 - blk.xc) - theta0;
                if (blk.DirIsCCW && dtheta <= 0) dtheta += 2 * Math.PI;
                if (!blk.DirIsCCW && dtheta >= 0) dtheta -= 2 * Math.PI;

                for (int phase = 0; phase < blk.C.Length; phase++)
                {
                    var P = blk.C[phase];
                    double s0 = P.d;
                    double s1 = phase + 1 < blk.C.Length ? blk.C[phase + 1].d : arcL;

                    double frac0 = Math.Clamp(s0 / arcL, 0.0, 1.0);
                    double frac1 = Math.Clamp(s1 / arcL, 0.0, 1.0);

                    double theta_entry = theta0 + frac0 * dtheta;
                    double theta_exit = theta0 + frac1 * dtheta;

                    double x0 = xc + r * Math.Cos(theta_entry);
                    double y0 = yc + r * Math.Sin(theta_entry);
                    double x1 = xc + r * Math.Cos(theta_exit);
                    double y1 = yc + r * Math.Sin(theta_exit);

                    // Z and other axes: interpolate linearly as before
                    double z0 = blk.z0 + (blk.z1 - blk.z0) * frac0;
                    double z1 = blk.z0 + (blk.z1 - blk.z0) * frac1;
                    double[] entryPos = { x0, y0, z0, 0, 0, 0 };
                    double[] exitPos = { x1, y1, z1, 0, 0, 0 };

                    EnqueueSegment(new KEngine.SEGMENT
                    {
                        type = SEG_ARC,
                        sequence_number = blk.sequence_number,
                        ID = blk.ID,
                        startActs = entryPos,
                        endActs = exitPos,
                        qa = P.a,
                        qb = P.b,
                        qc = P.c,
                        qd = 0.0,
                        qt = P.t
                    });

                    Console.WriteLine($"Arc Phase {phase}: frac0={frac0:0.###} frac1={frac1:0.###} | " + $"theta0={theta_entry * 180/Math.PI:0.##}deg theta1={theta_exit * 180/Math.PI:0.##}deg | " + $"entry=({x0:0.###},{y0:0.###},{z0:0.###}) exit=({x1:0.###},{y1:0.###},{z1:0.###}) | " + $"A={P.a:0.##} B={P.b:0.##} C={P.c:0.##} D={P.d:0.##} T={P.t:0.###}");
                }
            }
                return 0;
            
        }


 
        /// <summary>
        /// Enqueue a segment for later dispatch.
        /// Call this wherever you used to write directly into your segment‐buffers.
        /// </summary>
        public void EnqueueSegment(in KEngine.SEGMENT seg)
        {
            lock (_pending)
            {
                _pending.Enqueue(seg);
            }
            Console.WriteLine("Hit Queue");
        }

        /// <summary>
        /// Dequeue and dispatch all pending segments in “feed” mode.
        /// </summary>
        public void DoSegmentCallbacks()
        {
            lock (_pending)
            {
                while (_pending.Count > 0)
                {
                    var seg = _pending.Dequeue();
                    // TODO: replace this with however you actually push a
                    // feed‐rate segment into your interpreter or hardware:
                    RS274NGC.GCodeInterpreter.OnFeedSegment(seg);
                }
            }
        }

        /// <summary>
        /// Dequeue and dispatch all pending segments in “rapid” mode.
        /// </summary>
        public void DoSegmentCallbacksRapid()
        {
            lock (_pending)
            {
                while (_pending.Count > 0)
                {
                    var seg = _pending.Dequeue();
                    // TODO: replace this with your rapid‐traverse handler:
                    RS274NGC.GCodeInterpreter.OnRapidSegment(seg);
                }
            }
        }

    }
    public partial class RS274NGC
    {
        
        
    public int PendingSegments { get; private set; }



    }

}

