using System;
using System.Linq;           
using System.Collections.Generic;

namespace KognaServer.Server.KinematicEngine
{
    public struct TP_COEFF { public double t, a, b, c, d; }

    public static class TrajectoryPlanner
    {
        // --- Segment types ---
        public const int SEG_UNDEFINED = 0;
        public const int SEG_LINEAR = 1;
        public const int SEG_ARC = 2;
        public const int SEG_RAPID = 3;
        public const int SEG_DWELL = 4;
        private static readonly Queue<SEGMENT> _pending = new Queue<SEGMENT>();

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

        public static int nsegs = 0, prev_nsegs = 0, SegBufToggle = 0;
        public static int[] SegsDone = new int[2];
        public static double[] SegsDoneTime = new double[2];

        // --- Special commands ---
        public struct SPECIAL_CMD { public string Cmd; }
        public static SPECIAL_CMD[] special_cmds = new SPECIAL_CMD[MAX_SPECIAL_CMDS];
        public static int nspecial_cmds;
        public static int special_cmds_initial_first;
        public static int special_cmds_initial_last;
        public static int[] special_cmds_initial_sequence_no = new int[2];
        public static int ispecial_cmd_downloaded;

        // --- Motion parameters & helpers ---
        public static MOTION_PARAMS MP = new MOTION_PARAMS();
        private static double FacetAngleRadians, BreakAngleRadians;

        // --- Working list for CombineSegments ---
        private static int nCombined;

        // --- Struct definitions ---
        public struct MOTION_PARAMS
        {
            public double BreakAngle, CollinearTol, CornerTol, FacetAngle, TPLookahead;
            public double RadiusA, RadiusB, RadiusC;
            public double MaxAccelX, MaxAccelY, MaxAccelZ, MaxAccelA, MaxAccelB, MaxAccelC, MaxAccelU, MaxAccelV;
            public double MaxVelX, MaxVelY, MaxVelZ, MaxVelA, MaxVelB, MaxVelC, MaxVelU, MaxVelV;
            public double MaxRapidJerkX, MaxRapidJerkY, MaxRapidJerkZ, MaxRapidJerkA, MaxRapidJerkB, MaxRapidJerkC, MaxRapidJerkU, MaxRapidJerkV;
            public double MaxRapidAccelX, MaxRapidAccelY, MaxRapidAccelZ, MaxRapidAccelA, MaxRapidAccelB, MaxRapidAccelC, MaxRapidAccelU, MaxRapidAccelV;
            public double MaxRapidVelX, MaxRapidVelY, MaxRapidVelZ, MaxRapidVelA, MaxRapidVelB, MaxRapidVelC, MaxRapidVelU, MaxRapidVelV;
            public double SoftLimitNegX, SoftLimitNegY, SoftLimitNegZ, SoftLimitNegA, SoftLimitNegB, SoftLimitNegC, SoftLimitNegU, SoftLimitNegV;
            public double SoftLimitPosX, SoftLimitPosY, SoftLimitPosZ, SoftLimitPosA, SoftLimitPosB, SoftLimitPosC, SoftLimitPosU, SoftLimitPosV;
            public double CountsPerInchX, CountsPerInchY, CountsPerInchZ, CountsPerInchA, CountsPerInchB, CountsPerInchC, CountsPerInchU, CountsPerInchV;
            public double MaxLinearLength, MaxAngularChange;
            public bool ArcsToSegs, DegreesA, DegreesB, DegreesC;
            public bool UseOnlyLinearSegments, DoRapidsAsFeeds;
            public double MaxRapidFRO;
            public bool TCP_Active;
            public double TCP_X, TCP_Y, TCP_Z;
        }



        /// <summary>
        /// Initialize for a new list of segments (ping-pong buffer flip). :contentReference[oaicite:0]{index=0}
        /// </summary>
        public static void Init()
        {
            lock (_pending)
            {
                _pending.Clear();
            }
            nsegs = 0;
            nCombined = 0;
            SegsDoneTime[SegBufToggle] = 0.0;
            SegsDone[SegBufToggle] = -1;
            ispecial_cmd_downloaded = nspecial_cmds = nsegs = nCombined = 0;
            special_cmds_initial_first = special_cmds_initial_last = -1;
            special_cmds_initial_sequence_no[SegBufToggle] = -1;
        }

        /// <summary>
        /// “Pure‐angle” if no linear motion but non-zero rotation.
        /// </summary>
        public static bool PureAngle(SEGMENT seg)
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
            return PureAngle(dx, dy, dz, da, db, dc, du, dv);
        }

        public static bool PureAngle(
            double dx, double dy, double dz,
            double da, double db, double dc,
            double du, double dv)
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
        public static void SetParams(MOTION_PARAMS m)
        {
            MP = m;
            FacetAngleRadians = MP.FacetAngle * Math.PI / 180.0;
            if (MP.BreakAngle > 179.0) m.BreakAngle = 179.0;
            BreakAngleRadians = MP.BreakAngle * Math.PI / 180.0;
        }

        /// <summary>
        /// Insert a linear segment (2ⁿᵈ-order) and attempt combination. :contentReference[oaicite:2]{index=2}
        /// </summary>
        public static int InsertLinearSeg(
            double x0, double y0, double z0, double a0, double b0, double c0, double u0, double v0,
            double x1, double y1, double z1, double a1, double b1, double c1, double u1, double v1,
            double MaxVel, double MaxAccel, double MaxCombineLength,
            int sequence_number, int ID, int NumLinearNotDrawn)
        {
            // compute deltas
            double dx = x1 - x0, dy = y1 - y0, dz = z1 - z0;
            double da = a1 - a0, db = b1 - b0, dc = c1 - c0;
            double du = u1 - u0, dv = v1 - v0;

            // add into the list
            var p = GetSegPtr(nsegs);
            p.type = SEG_LINEAR;
            p.sequence_number = sequence_number;
            p.ID = ID;
            p.x0 = x0; p.y0 = y0; p.z0 = z0; p.a0 = a0; p.b0 = b0; p.c0 = c0; p.u0 = u0; p.v0 = v0;
            p.x1 = x1; p.y1 = y1; p.z1 = z1; p.a1 = a1; p.b1 = b1; p.c1 = c1; p.u1 = u1; p.v1 = v1;
            bool pureAngle;
            p.dx = FeedRateDistance(dx, dy, dz, da, db, dc, du, dv, out pureAngle);
            p.OrigVel = p.MaxVel = MaxVel;
            p.OrigAccel = MaxAccel;
            p.vel = 0.0;
            p.ChangeInDirection = CalcChangeInDirection(nsegs);
            p.StopRequired = nsegs > 0 && GetSegPtr(nsegs - 1).StopRequiredNextSeg;
            p.StopRequiredNextSeg = false;
            p.special_cmds_first = p.special_cmds_last = -1;
            p.Done = false;

            // try to combine with the previous
            if (CombineSegments(MaxCombineLength))

            {
                nsegs++;
                return 0;
            }
            else
            {
                // Combination failed → keep segment
                nsegs++;
                return 0;
            }
        }

        /// <summary>
        /// Insert a rapid (3ʳᵈ-order) linear segment. :contentReference[oaicite:3]{index=3}
        /// </summary>
        public static int InsertRapidLinearSeg(
            double x0, double y0, double z0, double a0, double b0, double c0, double u0, double v0,
            double x1, double y1, double z1, double a1, double b1, double c1, double u1, double v1,
            int sequence_number, int ID)
        {
            double dx = x1 - x0, dy = y1 - y0, dz = z1 - z0;
            double da = a1 - a0, db = b1 - b0, dc = c1 - c0;
            double du = u1 - u0, dv = v1 - v0;

            var p = GetSegPtr(nsegs);
            p.type = SEG_RAPID;
            p.sequence_number = sequence_number;
            p.ID = ID;
            p.x0 = x0; p.y0 = y0; p.z0 = z0; p.a0 = a0; p.b0 = b0; p.c0 = c0; p.u0 = u0; p.v0 = v0;
            p.x1 = x1; p.y1 = y1; p.z1 = z1; p.a1 = a1; p.b1 = b1; p.c1 = c1; p.u1 = u1; p.v1 = v1;
            bool pureAngle;
            p.dx = FeedRateDistance(dx, dy, dz, da, db, dc, du, dv, out pureAngle);
            p.vel = 0.0;
            p.StopRequired = true;
            p.StopRequiredNextSeg = false;
            p.special_cmds_first = p.special_cmds_last = -1;
            p.Done = false;

            nsegs++;
            return 0;
        }

        /// <summary>
        /// Insert a dwell segment. :contentReference[oaicite:4]{index=4}
        /// </summary>
        public static int InsertDwell(
            double t, double x0, double y0, double z0, double a0, double b0, double c0, double u0, double v0,
            int sequence_number, int ID)
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
        public static int InsertArcSeg(
            int plane,
            double x0, double y0, double z0, double a0, double b0, double c0, double u0, double v0,
            double x1, double y1, double z1, double a1, double b1, double c1, double u1, double v1,
            double xc, double yc, bool dirIsCCW,
            double MaxVel, double MaxAccel, double MaxDecel, double MaxLength,
            int sequence_number, int ID)
        {
            double dx = CalcLengthAlongCircle(x0, y0, x1, y1, xc, yc, dirIsCCW, out double radius, out double theta0, out double dtheta);
            var p = GetSegPtr(nsegs);
            p.type = SEG_ARC;
            p.plane = plane;
            p.sequence_number = sequence_number;
            p.ID = ID;
            p.x0 = x0; p.y0 = y0; p.z0 = z0; p.a0 = a0; p.b0 = b0; p.c0 = c0; p.u0 = u0; p.v0 = v0;
            p.x1 = x1; p.y1 = y1; p.z1 = z1; p.a1 = a1; p.b1 = b1; p.c1 = c1; p.u1 = u1; p.v1 = v1;
            p.xc = xc; p.yc = yc;
            p.DirIsCCW = dirIsCCW;
            p.dx = dx;
            p.MaxVel = p.OrigVel = MaxVel;
            p.MaxAccel = p.OrigAccel = MaxAccel;
            p.MaxDecel = MaxDecel;
            p.vel = 0.0;
            p.ChangeInDirection = CalcChangeInDirection(nsegs);
            p.StopRequired = nsegs > 0 && GetSegPtr(nsegs - 1).StopRequiredNextSeg;
            p.StopRequiredNextSeg = false;
            p.special_cmds_first = p.special_cmds_last = -1;
            p.Done = false;

            nsegs++;
            return 0;
        }

        /// <summary>
        /// Calculate trip states for a segment (2ⁿᵈ‐order or dispatch to rapid/dwell). :contentReference[oaicite:6]{index=6}</summary>
        public static int CalcSegTripStates(int i)
        {
            var p = GetSegPtr(i);
            if (p.type == SEG_RAPID) return CalcSegTripStatesRapid(i);
            if (p.type == SEG_DWELL) return CalcSegTripStatesDwell(i);

            double V0 = p.vel,
                   V1 = i < nsegs - 1 ? GetSegPtr(i + 1).vel : 0.0,
                   VM = p.MaxVel,
                   A = p.MaxAccel,
                   D = p.MaxDecel,
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

            // fill phases 0–2
            p.C[0].a = 0; p.C[0].b = 0.5 * A; p.C[0].c = V0; p.C[0].d = 0; p.C[0].t = ta;
            p.C[1].a = 0; p.C[1].b = 0; p.C[1].c = VM; p.C[1].d = da; p.C[1].t = tc;
            p.C[2].a = 0; p.C[2].b = -0.5 * D; p.C[2].c = VM; p.C[2].d = da + VM * tc; p.C[2].t = td;
            p.nTrips = 3;
            return 0;
        }

        /// <summary>
        /// Calculate trip states for rapid 3ʳᵈ-order move (7 phases). :contentReference[oaicite:7]{index=7}</summary>
        public static int CalcSegTripStatesRapid(int i)
        {
            var p = GetSegPtr(i);
            double MaxV = p.MaxVel, MaxA = p.MaxAccel, MaxJ = p.MaxJerk;
            // adjust MaxA if jerk-limited
            MaxA = Math.Min(MaxA, Math.Sqrt(MaxV * MaxJ));

            if (MaxV == 0.0 || MaxA == 0.0 || MaxJ == 0.0) return -1;

            // pointers into p.C[]
            TP_COEFF[] c = p.C;
            // times for each of 7 states
            c[0].t = MaxA / MaxJ; c[0].a = MaxJ / 6.0; c[0].b = 0; c[0].c = 0; c[0].d = 0;
            c[1].t = c[0].t; c[1].a = 0; c[1].b = 0.5 * MaxJ * c[0].t; c[1].c = Vel(c[0]); c[1].d = Pos(c[0]);
            c[2].t = c[0].t; c[2].a = -MaxJ / 6.0; c[2].b = MaxJ * c[0].t / 2.0; c[2].c = Vel(c[1]); c[2].d = Pos(c[1]);
            // state 3: constant velocity
            c[3].t = (p.dx - (c[0].d + c[1].d + c[2].d)) / MaxV;
            c[3].a = 0; c[3].b = 0; c[3].c = MaxV; c[3].d = Pos(c[2]);
            // decel symmetrical to accel
            c[4].t = c[1].t; c[4].a = -MaxJ / 6.0; c[4].b = MaxJ * c[1].t / 2.0; c[4].c = Vel(c[3]); c[4].d = Pos(c[3]);
            c[5].t = c[1].t; c[5].a = 0; c[5].b = -0.5 * MaxJ * c[1].t; c[5].c = Vel(c[4]); c[5].d = Pos(c[4]);
            c[6].t = c[0].t; c[6].a = MaxJ / 6.0; c[6].b = -MaxJ * c[1].t / 2.0; c[6].c = Vel(c[5]); c[6].d = Pos(c[5]);
            p.nTrips = 7;
            return 0;
        }

        /// <summary>
        /// Dwell case (single trip). :contentReference[oaicite:8]{index=8}</summary>
        public static int CalcSegTripStatesDwell(int i)
        {
            var p = GetSegPtr(i);
            p.C = new TP_COEFF[1];
            p.C[0].t = p.dwell_time;
            p.C[0].a = p.C[0].b = p.C[0].c = 0;
            p.C[0].d = 0;
            p.nTrips = 1;
            return 0;
        }

        /// <summary>
        /// Combine colinear segments within tolerance. :contentReference[oaicite:9]{index=9}</summary>
        public static bool CombineSegments(double MaxLength)
        {
            if (nCombined >= MAX_COMBINE || nsegs < 1) { nCombined = 0; return true; }
            var pn = GetSegPtr(nsegs);
            var pm1 = GetSegPtr(nsegs - 1);
            if (pn.type != SEG_LINEAR || pm1.type != SEG_LINEAR || pn.Done || pm1.Done) { nCombined = 0; return true; }
            if (pm1.dx > MaxLength) { nCombined = 0; return true; }
            if (Math.Abs(pn.OrigVel - pm1.OrigVel) > SPEED_TOL * Math.Min(pn.OrigVel, pm1.OrigVel)) { nCombined = 0; return true; }
            if (!CheckCollinear(pm1, pn, pm1, MP.CollinearTol)) { nCombined = 0; return true; }
            // … (omitting rest of combine logic for brevity) …
            nCombined++;
            return false;
        }

        /// <summary>
        /// Golden‐section corner rounding. :contentReference[oaicite:10]{index=10}</summary>
        public static void RoundCorner(int i)
        {
            if (i < 1) return;
            var seg = GetSegPtr(i);
            var segm = GetSegPtr(i - 1);
            if (seg.StopRequired) return;
            double Theta = seg.ChangeInDirection;
            if (Math.Abs(Theta) > BreakAngleRadians) { seg.StopRequired = true; return; }
            if (Theta == 0.0 || MP.CornerTol < SIGMA) return;
            if (seg.type != SEG_LINEAR || segm.type != SEG_LINEAR) return;
            if (PureAngle(seg) || PureAngle(segm)) return;
            // … (complex facet interpolation logic) …
        }

        /// <summary>
        /// Test three points for near‐collinearity. :contentReference[oaicite:11]{index=11}</summary>
        public static bool CheckCollinear(SEGMENT s0, SEGMENT s1, SEGMENT s2, double tol)
        {
            // side lengths
            double a = FeedRateDistance(s0.x1 - s0.x0, s0.y1 - s0.y0, s0.z1 - s0.z0, s0.a1 - s0.a0, s0.b1 - s0.b0, s0.c1 - s0.c0, s0.u1 - s0.u0, s0.v1 - s0.v0, out bool p0),
                   b = FeedRateDistance(s1.x1 - s1.x0, s1.y1 - s1.y0, s1.z1 - s1.z0, s1.a1 - s1.a0, s1.b1 - s1.b0, s1.c1 - s1.c0, s1.u1 - s1.u0, s1.v1 - s1.v0, out bool p1),
                   c = FeedRateDistance(s2.x1 - s2.x0, s2.y1 - s2.y0, s2.z1 - s2.z0, s2.a1 - s2.a0, s2.b1 - s2.b0, s2.c1 - s2.c0, s2.u1 - s2.u0, s2.v1 - s2.v0, out bool p2);

            if (p0 && p1 && p2) return false;
            if (a + b < c - tol || b + c < a - tol || c + a < b - tol) return false;
            double s = 0.5 * (a + b + c);
            double area2 = Math.Max(0.0, s * (s - a) * (s - b) * (s - c));
            return area2 <= (tol * 0.5 * c) * (tol * 0.5 * c);
        }

        /// <summary>
        /// One‐segment forward/backward adjustment (if you ever need a pointwise pass).</summary>
        public static void AdjustSegVelocity(int i)
        {
            var p = GetSegPtr(i);
            // reduce to the minimum of the two sweep functions
            if (i > 0) p.vel = Math.Min(p.vel, MaximizeSegmentForward(i - 1));
            if (i < nsegs - 1) p.vel = Math.Min(p.vel, MaximizeSegmentBackward(i));
        }

        /// <summary>
        /// Corner‐curvature limit: V ≤ √(A·R). :contentReference[oaicite:curv]{index=curv}</summary>
        public static void AdjustSegVelocityCircle(int i, double A)
        {
            var p = GetSegPtr(i);
            // R = dist(center→start) 
            double dx = p.xc - p.x0, dy = p.yc - p.y0;
            double r = Math.Sqrt(dx * dx + dy * dy);

            // s = √(A·r)
            double s = Math.Sqrt(A * r);
            if (s < p.MaxVel) p.MaxVel = s;
        }

        /// <summary>
        /// Compute total path length (linear + angular). :contentReference[oaicite:14]{index=14}</summary>
        public static double FeedRateDistance(
            double dx, double dy, double dz,
            double da, double db, double dc,
            double du, double dv,
            out bool pureAngle)
        {
            // angular→linear conversions
            double d2 = dx * dx + dy * dy + dz * dz;
            if (MP.DegreesA && MP.RadiusA != 0.0) da = da * Math.PI / 180.0 * MP.RadiusA;
            if (MP.DegreesB && MP.RadiusB != 0.0) db = db * Math.PI / 180.0 * MP.RadiusB;
            if (MP.DegreesC && MP.RadiusC != 0.0) dc = dc * Math.PI / 180.0 * MP.RadiusC;
            d2 += da * da + db * db + dc * dc + du * du + dv * dv;
            pureAngle = false;
            return Math.Sqrt(d2);
        }

        /// <summary>
        /// Arc-length around a circle. :contentReference[oaicite:15]{index=15}</summary>
        public static double CalcLengthAlongCircle(
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
        public static void Quadratic(double a, double b, double c, out double r1, out double r2)
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
        public static int TPMOD(int i)
            => _pending.Count == 0 ? 0 : i % _pending.Count;

        /// <summary>
        /// Get the iᵗʰ segment (wrapping via TPMOD) from the queue.
        /// </summary>
        public static SEGMENT GetSegPtr(int i)
        {
            // snapshot the queue into an array so indexing is O(1)
            var arr = _pending.ToArray();
            return arr[TPMOD(i)];
        }

        /// <summary>
        /// “Position” coefficient lookup on segment p.t
        /// (example formula from your original port).
        /// </summary>
        private static double Pos(TP_COEFF p)
        {
            // convert the double “t” into an integer index however you need:
            int idx = (int)Math.Floor(p.t);  // or (int)p.t, or Round, depending on semantics

            // now lookup the segment
            var seg = GetSegPtr(idx);

            // compute the polynomial at the fractional t
            return seg.a * p.t * p.t * p.t
                + seg.b * p.t * p.t
                + seg.c * p.t
                + seg.d;
        }
        private static double Vel(TP_COEFF p)
        {
            // only uses p.t and the coefficients, no segment‐lookup needed here
            return 3 * p.a * p.t * p.t
                + 2 * p.b * p.t
                + p.c;
        }
        private static double CalcChangeInDirection(int idx)
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
        public static double CalcLengthAlongHelix(
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

            if (MP.DegreesA && MP.RadiusA != 0.0) da = da * Math.PI / 180.0 * MP.RadiusA;
            if (MP.DegreesB && MP.RadiusB != 0.0) db = db * Math.PI / 180.0 * MP.RadiusB;
            if (MP.DegreesC && MP.RadiusC != 0.0) dc = dc * Math.PI / 180.0 * MP.RadiusC;

            sum2 += da * da + db * db + dc * dc + du * du + dv * dv;
            return Math.Sqrt(sum2);
        }

        /// <summary>
        /// Two-segment velocity-maximization sweep (forward/backward). :contentReference[oaicite:multi]{index=multi}</summary>
        public static void MaximizeSegments()
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
        private static double MaximizeSegmentForward(int i)
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
        private static double MaximizeSegmentBackward(int i)
        {
            double V1 = (i < nsegs - 1 ? GetSegPtr(i + 1).vel : 0.0);
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
        public static void SetSegmentVelAccels(int i, double vel, double accel, double decel)
        {
            var p = GetSegPtr(i);
            p.MaxVel = vel;
            p.MaxAccel = accel;
            p.MaxDecel = decel;
        }

        /// <summary>
        /// Mutators for per-segment limits + jerk. :contentReference[oaicite:velaccjerk]{index=velaccjerk}</summary>
        public static void SetSegmentVelAccelJerk(int i, double vel, double accel, double jerk)
        {
            var p = GetSegPtr(i);
            p.MaxVel = vel;
            p.MaxAccel = accel;
            p.MaxDecel = accel;   // note: C++ used MaxDecel=Accel 
            p.MaxJerk = jerk;
        }

        /// <summary>
        /// Delta‐vector of a segment. :contentReference[oaicite:dir]{index=dir}</summary>
        public static void GetSegmentDirection(
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
        public static double CalcChangeInDirectionXYZ(int i)
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

        public static double SegmentXYZLength(SEGMENT p)
        {
            double dx = p.x1 - p.x0;
            double dy = p.y1 - p.y0;
            double dz = p.z1 - p.z0;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
        /// <summary>
        /// Final (“exit”) direction of segment i. :contentReference[oaicite:exit]{index=exit}</summary>
        public static void CalcFinalDirectionOfSegment(
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
        public static void CalcBegDirectionOfSegment(
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


        public static double CubeRoot(double v)
        {
            if (v < 1e-30) return 0.0;
            // exp(log(v)/3)
            return Math.Exp(Math.Log(v) / 3.0);
        }

        /// <summary>
        /// Solve a x^2 + b x + c = 0  r1, r2
        /// </summary>
        public static void Quadradic(double a, double b, double c, out double r1, out double r2)
        {
            double rad = b * b - 4.0 * a * c;
            double sq = Math.Sqrt(rad);
            r1 = (-b + sq) / (2.0 * a);
            r2 = (-b - sq) / (2.0 * a);
        }

        /// <summary>
        /// Number of segments still waiting to be committed.
        /// </summary>
        public static int PendingSegments
        {
            get
            {
                lock (_pending)            // if you’re multi‐threaded
                    return _pending.Count;
            }
        }

        /// <summary>
        /// Enqueue a segment for later dispatch.
        /// Call this wherever you used to write directly into your segment‐buffers.
        /// </summary>
        public static void EnqueueSegment(in SEGMENT seg)
        {
            lock (_pending)
                _pending.Enqueue(seg);
        }

        /// <summary>
        /// Dequeue and dispatch all pending segments in “feed” mode.
        /// </summary>
        public static void DoSegmentCallbacks()
        {
            lock (_pending)
            {
                while (_pending.Count > 0)
                {
                    var seg = _pending.Dequeue();
                    // TODO: replace this with however you actually push a
                    // feed‐rate segment into your interpreter or hardware:
                    GCodeInterpreter.OnFeedSegment(seg);
                }
            }
        }

        /// <summary>
        /// Dequeue and dispatch all pending segments in “rapid” mode.
        /// </summary>
        public static void DoSegmentCallbacksRapid()
        {
            lock (_pending)
            {
                while (_pending.Count > 0)
                {
                    var seg = _pending.Dequeue();
                    // TODO: replace this with your rapid‐traverse handler:
                    GCodeInterpreter.OnRapidSegment(seg);
                }
            }
        }
    }
 public struct SEGMENT
    {
            public double a, b, c, d;
        // G‐code segment header
            public int type;             // SEG_LINEAR, SEG_ARC, etc.
        public int sequence_number;
        public int ID;

        // start and end coordinates
        public double x0, y0, z0, a0, b0, c0, u0, v0;
        public double x1, y1, z1, a1, b1, c1, u1, v1;

        // arc‐specific
        public double xc, yc;
        public bool   DirIsCCW;
        public int    plane;

        // motion profiling
        public double dx;            // “distance” for feed/accel
        public double MaxVel, OrigVel;
        public double MaxAccel, OrigAccel;
        public double MaxDecel;
        public double MaxJerk;

        public double vel;           // the planned end‐velocity
        public double ChangeInDirection;

        // stops & combination flags
        public bool StopRequired;
        public bool StopRequiredNextSeg;
        public bool Done;

        // dwell
        public double dwell_time;

        // special commands
        public int special_cmds_first, special_cmds_last;

        // per‐segment trip table
        public TP_COEFF[] C;
        public int nTrips;
    }


    }



