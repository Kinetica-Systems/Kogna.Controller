using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using KinematicEngine;

using System.Text.RegularExpressions;
using System.Dynamic;


namespace KinematicEngine
{
    /// <summary>
    /// Standalone TCP server exposing a JSON IPC interface to your kinematic engine.
    /// </summary>
    public class KEngine
    {

        public KEngine _kinematicEngine = null!;
        private double x, y, z, a, b, c, u, v;
        private double dx, dy, dz, da, db, dc, du, dv;
        private double cx, cy, cz, ca, cb, cc, cu, cv, ci, cj;
        private readonly List<CPT3D> _segStarts = new List<CPT3D>();
        private readonly List<CPT3D> _segEnds = new List<CPT3D>();
        private bool _running;
        public bool _IsUpdated;
        private double _lastFeedRate = 0.0;   // <-- new field
        private double _accel = 100;
        public CCoordMotion _ccmotion;
        public CKinematics _cKinematics;
        public Kinematics6AxisFanuc _kinematics;
        public TrajectoryPlanner _planner;
        public RS274NGC.SetupData _setup;
        public SEGMENT _segment;
        public KEngine _kEngine;
        public MOTION_PARAMS _MOTION_PARAMS;


        //private readonly RS274NGC.GCodeInterpreter _interp = new RS274NGC.GCodeInterpreter(_ccmotion);
        /// <summary>
        /// Initializes a new server listening on the specified IP and port.
        /// </summary>
        public KEngine()
        {
            _kEngine = this;
            _cKinematics = new CKinematics();
            _kinematics = new Kinematics6AxisFanuc();
            _planner = new TrajectoryPlanner(_kEngine, _cKinematics, _kinematics);
            _setup = new RS274NGC.SetupData();
            _ccmotion = new CCoordMotion(_kinematics, _planner, _setup, _kEngine);

        }

        public bool Start()
        {
            loadParameters();
            _running = true;
            _cKinematics.Start();
            _planner.Init();
            Console.WriteLine($"Kinematic Engine Initialised");
            return _running;
        }

        public async Task<string> ProcessCommand(string commandLine)
        {
            Console.WriteLine("hit engine entry");

            string response;
            var match = Regex.Match(commandLine, @"\bF([\d\.]+)", RegexOptions.IgnoreCase);
            if (match.Success && double.TryParse(match.Groups[1].Value, out double fVal))
            {
                _lastFeedRate = fVal;
            }
            if (string.IsNullOrWhiteSpace(commandLine))
            {
                response = "Error - Empty command";
                return response;
            }
            var parts = commandLine.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts[0].ToLowerInvariant();
            var payload = parts.Length > 1 ? parts[1] : string.Empty;
            Console.WriteLine($"OK-KE Payload:{payload}");
            Console.WriteLine($"OK-KE cmd:{cmd}");
            try
            {

                // axis 1–6 are X,Y,Z,A,B,C
                _ccmotion.GetPosition(1, out x);
                _ccmotion.GetPosition(2, out y);
                _ccmotion.GetPosition(3, out z);
                _ccmotion.GetPosition(4, out a);
                _ccmotion.GetPosition(5, out b);
                _ccmotion.GetPosition(6, out c);
                _ccmotion.GetPosition(7, out u);
                _ccmotion.GetPosition(8, out v);

                Console.WriteLine($"{x} {y} {z} {a} {b} {c} {u} {v}");
                int seqNo = _ccmotion.GetNextSequenceNumber();
                Console.WriteLine($"{seqNo}");

                cx = ParseAxis('X', cx);
                cy = ParseAxis('Y', cy);
                cz = ParseAxis('Z', cz);
                ca = ParseAxis('A', ca);
                cb = ParseAxis('B', cb);
                cc = ParseAxis('C', cc);
                cu = ParseAxis('U', cu);
                cv = ParseAxis('V', cv);
                ci = ParseAxis('I', ci);
                cj = ParseAxis('J', cj);
                Console.WriteLine($"{cx} {cy} {cz} {ca} {cb} {cc} {cu} {cv} {ci} {cj}");

                _segStarts.Add(new CPT3D { x = x, y = y, z = z });
                _segEnds.Add(new CPT3D { x = cx, y = cy, z = cz });

                response = string.Empty;
                // 1) setcs
                if (cmd == "g0")
                {
                    Console.WriteLine($"[ENGINE] dispatching motion '{cmd}' -> Straight Traverse");
                    _ccmotion.StraightFeedAccel(x, y, z, a, b, c, u, v, cx, cy, cz, ca, cb, cc, cu, cv, _lastFeedRate, _accel, true, seqNo, 0);
                    return response;
                }
                if (cmd == "g1")
                {
                    Console.WriteLine($"[ENGINE] dispatching motion '{cmd}' -> Straight Feed");
                    _ccmotion.StraightFeedAccel(x, y, z, a, b, c, u, v, cx, cy, cz, ca, cb, cc, cu, cv, _lastFeedRate, _accel, false, seqNo, 0);
                    return response;
                }
                if (cmd == "g2")
                {
                    Console.WriteLine($"[ENGINE] dispatching motion '{cmd}' -> CW Arc");
                    
                    _ccmotion.ArcFeedAccel(x, y, z, a, b, c, u, v, cx, cy, cz, ca, cb, cc, cu, cv, ci, cj, false, _lastFeedRate, _accel, seqNo, 0);
                    return response;
                }
                if (cmd == "g3")
                {
                    Console.WriteLine($"[ENGINE] dispatching motion '{cmd}' -> CCW Arc");
                    _ccmotion.ArcFeedAccel(x, y, z, a, b, c, u, v, cx, cy, cz, ca, cb, cc, cu, cv, ci, cj, true, _lastFeedRate, _accel, seqNo, 0);
                    return response;
                }
                if (cmd == "g4")
                {
                    Console.WriteLine($"Dwell - G4 Called");
                    response = "Not implemented yet";
                    return response;
                }
                if (cmd == "g28")
                {
                    Console.WriteLine($"Home Axis - G28 Called");
                    response = "Not implemented yet";
                    return response;
                }

                DumpPlannerSegments();

                Console.WriteLine($"[ENGINE] TrajectoryPlanner.SegCount() = {_planner.SegCount()}");

                // 3) pull the buffered segments out of your TrajectoryPlanner
                int segmentCount = _planner.SegCount();
                var segments = new SEGMENT[segmentCount];



            }

            catch (Exception ex)
            {

                // log the full exception to console
                Console.WriteLine($"[ENGINE ERROR] {ex}");
                return "Engine Error {ex}";
                // return full message+stack and an empty segments array (never null)

            }



            return "pass";

            double ParseAxis(char letter, double current)
            {
                var m = Regex.Match(payload, $@"\b{letter}([-+]?\d*\.?\d+)", RegexOptions.IgnoreCase);
                return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
                    ? v
                    : current;
            }

        }
        public void loadParameters()
        {
            _MOTION_PARAMS = new MOTION_PARAMS
            {
                BreakAngle = 30.0,
                TPLookahead = 3.0,
                MaxAccelV = 1.0,
                MaxAccelU = 1.0,
                MaxAccelC = 1.0,
                MaxAccelB = 1.0,
                MaxAccelA = 1.0,
                MaxAccelX = 1.0,
                MaxAccelY = 1.0,
                MaxAccelZ = 1.0,
                MaxVelV = 1.0,
                MaxVelU = 1.0,
                MaxVelC = 1.0,
                MaxVelB = 1.0,
                MaxVelA = 1.0,
                MaxVelX = 1.0,
                MaxVelY = 1.0,
                MaxVelZ = 1.0,
                MaxRapidJerkV = 10.0,
                MaxRapidJerkU = 10.0,
                MaxRapidJerkC = 10.0,
                MaxRapidJerkB = 10.0,
                MaxRapidJerkA = 10.0,
                MaxRapidJerkX = 10.0,
                MaxRapidJerkY = 10.0,
                MaxRapidJerkZ = 10.0,
                MaxRapidAccelV = 1.0,
                MaxRapidAccelU = 1.0,
                MaxRapidAccelC = 1.0,
                MaxRapidAccelB = 1.0,
                MaxRapidAccelA = 1.0,
                MaxRapidAccelX = 1.0,
                MaxRapidAccelY = 1.0,
                MaxRapidAccelZ = 1.0,
                MaxRapidVelV = 1.0,
                MaxRapidVelU = 1.0,
                MaxRapidVelC = 1.0,
                MaxRapidVelB = 1.0,
                MaxRapidVelA = 1.0,
                MaxRapidVelX = 1.0,
                MaxRapidVelY = 1.0,
                MaxRapidVelZ = 1.0,
                CountsPerInchV = 100.0,
                CountsPerInchU = 100.0,
                CountsPerInchC = 100.0,
                CountsPerInchB = 100.0,
                CountsPerInchA = 100.0,
                CountsPerInchX = 100.0,
                CountsPerInchY = 100.0,
                CountsPerInchZ = 100.0,
                MaxLinearLength = 0.05,
                MaxAngularChange = 0.5,
                MaxRapidFRO = 1.0,
                CollinearTol = 0.0002,
                CornerTol = 0.0002,
                FacetAngle = 0.5,
                PivotToChuckLength = 7.874,
                UseOnlyLinearSegments = true,
                DoRapidsAsFeeds = false,
                DegreesA = false,
                DegreesB = false,
                DegreesC = false,
                SoftLimitNegX = -1e30,
                SoftLimitNegY = -1e30,
                SoftLimitNegZ = -1e30,
                SoftLimitNegA = -1e30,
                SoftLimitNegB = -1e30,
                SoftLimitNegC = -1e30,
                SoftLimitNegU = -1e30,
                SoftLimitNegV = -1e30,
                SoftLimitPosX = 1e30,
                SoftLimitPosY = 1e30,
                SoftLimitPosZ = 1e30,
                SoftLimitPosA = 1e30,
                SoftLimitPosB = 1e30,
                SoftLimitPosC = 1e30,
                SoftLimitPosU = 1e30,
                SoftLimitPosV = 1e30,
                TCP_Active = false,
                TCP_X = 0.0,
                TCP_Y = 0.0,
                TCP_Z = 0.0,
                MaxVel = 500,
                MaxAccel = 1000,
                MaxJerk = 10000,

            };
            
                _planner._motionParams = _MOTION_PARAMS;
                _ccmotion._motionParams = _MOTION_PARAMS;
                _cKinematics.motionParams = _MOTION_PARAMS;
                _kinematics.motionParams = _MOTION_PARAMS;
                _segment._MOTION_PARAMS = _MOTION_PARAMS;
            Console.WriteLine("MotionParameters loaded");

    }
        public bool UpdateAxisDestinations()
        {
            _ccmotion.GetDestination(1, out dx);
            _ccmotion.GetDestination(2, out dy);
            _ccmotion.GetDestination(3, out dz);
            _ccmotion.GetDestination(4, out da);
            _ccmotion.GetDestination(5, out db);
            _ccmotion.GetDestination(6, out dc);
            _ccmotion.GetDestination(7, out du);
            _ccmotion.GetDestination(8, out dv);
            Console.WriteLine($"current pos {dx} {dy} {dz} {da} {db} {dc} {du} {dv}");
            return true;
        }

        public bool UpdateAxisPos()
        {
            // axis 1–6 are X,Y,Z,A,B,C
            _ccmotion.GetPosition(1, out x);
            _ccmotion.GetPosition(2, out y);
            _ccmotion.GetPosition(3, out z);
            _ccmotion.GetPosition(4, out a);
            _ccmotion.GetPosition(5, out b);
            _ccmotion.GetPosition(6, out c);
            _ccmotion.GetPosition(7, out u);
            _ccmotion.GetPosition(8, out v);
            Console.WriteLine($"current pos {x} {y} {z} {a} {b} {c} {u} {v}");
            return true;
        }

 private void DumpPlannerSegments()
{
    int count = _planner.SegCount();
    Console.WriteLine($"[DEBUG] Planner contains {count} segment(s).");
    for (int i = 0; i < count; i++)
    {
        var s = _planner.GetSegment(i);

        var start = (x: s.x0, y: s.y0, z: s.z0);
        var end   = (x: s.x1, y: s.y1, z: s.z1);

        // Safely grab angles (might still be null pre-patch)
        var angles = s.angle ?? new double[6];

                string coeffInfo = "";
        if (s.C != null && s.C.Length > 0)
        {
            var c0 = s.C[0];
            coeffInfo = $" | C0[a={c0.a:F3}, b={c0.b:F3}, c={c0.c:F3}, d={c0.d:F3}, t={c0.t:F3}]";
        }
        // Dump the basic line, plus optional C[0] coeffs if present
        string line = $"[SEG {i:00}] type={s.type} seq={s.sequence_number} " +
                      $"from=({start.x:F2},{start.y:F2},{start.y:F2}) " +
                      $"to=({end.x:F2},{end.y:F2},{end.z:F2}) " +
                      $"angles=[{string.Join(",", angles.Select(a => a.ToString("F1")))}] " +
                      $"dur={s.Duration}ms" + coeffInfo;


        Console.WriteLine(line);
    }
}
        private string FormatLinearCmd(KEngine.SEGMENT seg)
        {
            // 1) Grab start and end (“0” & “1”) in joint‐space:
            double X0 = seg.x0, Y0 = seg.y0, Z0 = seg.z0;
            double A0 = seg.a0, B0 = seg.b0, C0 = seg.c0;
            double X1 = seg.x1, Y1 = seg.y1, Z1 = seg.z1;
            double A1 = seg.a1, B1 = seg.b1, C1 = seg.c1;

            // 2) Extract the single cubic‐blend for the whole segment:
            //    This only works if you’ve forced the planner to use a single TP_COEFF
            //    per segment (i.e. no multi‐phase jerk profile).
            var p = seg.C[0];
            //double a = p.a, b = p.b, c = p.c, d = p.d;
            //double tF = p.t;  // total segment time in seconds

            // 3) Build the one‐liner:
            // return string.Format($"Linear {0:F4} {1:F4} {2:F4} {3:F4} {4:F4} {5:F4} " + $"{6:F4} {7:F4} {8:F4} {9:F4} {10:F4} {11:F4} " + $"{12:E6} {13:E6} {14:E6} {15:E6} {16:F6}",
            //    X0, Y0, Z0, A0, B0, C0, X1, Y1, Z1, A1, B1, C1, a, b, c, d, tF);
            return "";
        }

        /// <summary>
        /// Defines the coordinate system mapping. Uses the same index for all axes by default.
        /// </summary>
        public int SetAxisDefinitions(int csIndexx, int csIndexy, int csIndexz, int csIndexa, int csIndexb, int csIndexc) => _ccmotion.SetAxisDefinitions(csIndexx, csIndexy, csIndexz, csIndexa, csIndexb, csIndexc, 0, 0);
        public int GetAxisDefinitions(out int csIndexx, out int csIndexy, out int csIndexz, out int csIndexa, out int csIndexb, out int csIndexc) => _ccmotion.GetAxisDefinitions(out csIndexx, out csIndexy, out csIndexz, out csIndexa, out csIndexb, out csIndexc);

        /// <summary>
        /// JSON-serializable response object.
        /// </summary>
        public class IpcResponse
        {
            public string Status { get; set; } = string.Empty;
            public string Result { get; set; } = string.Empty;
            public SEGMENT[]? Segments { get; set; }     // your transformed joint‐motion data
            public string? Error { get; set; }
        }

        public struct SEGMENT
        {
            public double[]? JointAngles { get; set; }
            public double[] delta { get; set; }
            public double DurationMs { get; set; }
            // G‐code segment header
            public int type;             // SEG_LINEAR, SEG_ARC, etc.
            public int nTrips;
            public int sequence_number;
            public double Duration, t;
            public double[] angle;
            public Array[] arr;
            public double[] startActs;
            public double[] endActs;
            public int ID;
            //realtime positions and cartesian targets
            public double x, y, z, a, b, c, d, u, v;
            public double cx, cy, cz, ca, cb, cc, cd, cu, cv;
            // start and end coordinates
            public double x0, y0, z0, a0, b0, c0, u0, v0;
            public double x1, y1, z1, a1, b1, c1, u1, v1;
            public int SpecialCmdsFirst { get; set; }
            public int SpecialCmdsLast { get; set; }
            public bool StopRequiredNextSeg { get; set; }
            public double qa, qb, qc, qd;
            public double qx0, qy0, qz0, qa0, qb0, qc0, qx1, qy1, qz1, qa1, qb1, qc1, qt;
            public double[] entry, exit;
            // arc‐specific
            public double xc, yc, i1, j1, theta0, dtheta, radius ;
            public bool DirIsCCW;
            public int plane;

            // motion profiling
            public double dx;            // “distance” for feed/accel
            public double MaxVel;
            public double OrigVel;
            public double MaxAccel;
            public double OrigAccel;
            public double MaxDecel;
            public double MaxJerk;
            public double MaxCombineLength;
            public int NumLinearNotDrawn;

            public double vel;           // the planned end‐velocity
            public double ChangeInDirection;

            // stops & combination flags
            public bool StopRequired;
            public bool Done;

            // dwell
            public double dwell_time;

            // special commands
            public int special_cmds_first, special_cmds_last;

            // per‐segment trip table
            public TP_COEFF[] C { get; set; }
            public MOTION_PARAMS _MOTION_PARAMS { get; set; }
            // add any other fields you serialized…
        }

        public struct MOTION_PARAMS
        {
            public double BreakAngle;
            public double TPLookahead;
            public double MaxAccelV, MaxAccelU, MaxAccelC, MaxAccelB, MaxAccelA, MaxAccelX, MaxAccelY, MaxAccelZ;
            public double MaxVelV, MaxVelU, MaxVelC, MaxVelB, MaxVelA, MaxVelX, MaxVelY, MaxVelZ;
            public double MaxRapidJerkV, MaxRapidJerkU, MaxRapidJerkC, MaxRapidJerkB, MaxRapidJerkA, MaxRapidJerkX, MaxRapidJerkY, MaxRapidJerkZ;
            public double MaxRapidAccelV, MaxRapidAccelU, MaxRapidAccelC, MaxRapidAccelB, MaxRapidAccelA, MaxRapidAccelX, MaxRapidAccelY, MaxRapidAccelZ;
            public double MaxRapidVelV, MaxRapidVelU, MaxRapidVelC, MaxRapidVelB, MaxRapidVelA, MaxRapidVelX, MaxRapidVelY, MaxRapidVelZ;
            public double CountsPerInchV, CountsPerInchU, CountsPerInchC, CountsPerInchB, CountsPerInchA, CountsPerInchX, CountsPerInchY, CountsPerInchZ;
            public double RadiusA, RadiusB, RadiusC;// RadiusX, RadiusY, RadiusZ;
            public double MaxVel;
            public double OrigVel;
            public double MaxAccel;
            public double OrigAccel;
            public double MaxDecel;
            public double MaxJerk;
            public double MaxLinearLength, MaxCombineLength;
            public double MaxAngularChange;
            public double MaxRapidFRO;
            public double CollinearTol;
            public double CornerTol;
            public double FacetAngle, PivotToChuckLength;
            public bool UseOnlyLinearSegments;

            public bool DoRapidsAsFeeds;
            public bool DegreesA, DegreesB, DegreesC;
            public double SoftLimitNegX, SoftLimitNegY, SoftLimitNegZ, SoftLimitNegA, SoftLimitNegB, SoftLimitNegC, SoftLimitNegU, SoftLimitNegV;
            public double SoftLimitPosX, SoftLimitPosY, SoftLimitPosZ, SoftLimitPosA, SoftLimitPosB, SoftLimitPosC, SoftLimitPosU, SoftLimitPosV;
            public bool TCP_Active;
            public double TCP_X, TCP_Y, TCP_Z;
        }

        

    }

    
}

