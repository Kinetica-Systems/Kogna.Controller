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
        private bool _running;
        public bool _IsUpdated;
        private double _lastFeedRate = 0.0;   // <-- new field
        private static readonly CCoordMotion? _ccmotion = null!;
        //private readonly RS274NGC.GCodeInterpreter _interp = new RS274NGC.GCodeInterpreter(_ccmotion);
        /// <summary>
        /// Initializes a new server listening on the specified IP and port.
        /// </summary>
        public KEngine()
        {

        }

        public bool Start()
        {
            _running = true;
            Console.WriteLine($"Kinematic Engine Initialised");
            return _running;
        }

        public string ProcessCommand(string commandLine)
        {
            Console.WriteLine("hit entry");

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
            Console.WriteLine($"OK-Payload:{payload}");
            Console.WriteLine($"OK-cmd:{cmd}");
            try
            {
                // 1) definecs
                if (cmd == "setcs")
                {
                    

                }

                // 2) posN
                

                //  {
                //      double position = (_ccmotion.GetPosition(axisPos, out var val));
                //      return response(Status = "OK", Result = position.ToString(CultureInfo.InvariantCulture));
                //  }
             


                // 4) fallback → G-code interpreter

                // axis 1–6 are X,Y,Z,A,B,C
                _ccmotion!.GetPosition(1, out x);
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
                x = ParseAxis('X', x);
                y = ParseAxis('Y', y);
                z = ParseAxis('Z', z);
                a = ParseAxis('A', a);
                b = ParseAxis('B', b);
                c = ParseAxis('C', c);
                u = ParseAxis('U', u);
                v = ParseAxis('V', v);
                Console.WriteLine($"{x} {y} {z} {a} {b} {c} {u} {v}");


                Console.WriteLine($"[ENGINE] dispatching motion '{cmd}' → Straight{(cmd == "G0" ? "Traverse" : "Feed")}");

                _ccmotion.StraightTraverse(x, y, z, a, b, c, u, v, seqNo, 0, _lastFeedRate);

                _ccmotion.GetPosition(1, out var newX);
                _ccmotion.GetPosition(2, out var newY);
                _ccmotion.GetPosition(3, out var newZ);
                Console.WriteLine($"[POST-MOTION] now at X={newX},Y={newY},Z={newZ}");


                // non-motion commands generate no segments


                Console.WriteLine($"[ENGINE] TrajectoryPlanner.SegCount() = {TrajectoryPlanner.SegCount()}");
                // 3) pull the buffered segments out of your TrajectoryPlanner
                int segmentCount = TrajectoryPlanner.SegCount();
                var segments = new Segment[segmentCount];



                for (int i = 0; i < segmentCount; i++)
                {
                    // The planner returns an RS274NGC.SEGMENT struct
                    var raw = TrajectoryPlanner.GetSegment(i);

                    // Map it into your client‐side DTO:
                    segments[i] = new Segment
                    {
                        JointAngles = new[] {
                            raw.angle[0],
                            raw.angle[1],
                            raw.angle[2],
                            raw.angle[3],
                            raw.angle[4],
                            raw.angle[5]
                        },
                        DurationMs = raw.Duration

                        //   main.PushNewSegmentData(segments[i].JointAngles, segments[i].DurationMs);
                    };

                }
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

        public bool UpdateAxisDestinations()
        {

                _ccmotion!.GetDestination(1, out dx);
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
                _ccmotion!.GetPosition(1, out x);
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
        private bool IsMotionCommand(string cmd) => cmd == "G0" || cmd == "G1" || cmd == "G2" || cmd == "G3";



        /// <summary>
        /// Defines the coordinate system mapping. Uses the same index for all axes by default.
        /// </summary>
        public static int SetAxisDefinitions(int csIndexx, int csIndexy, int csIndexz, int csIndexa, int csIndexb, int csIndexc) => _ccmotion!.SetAxisDefinitions(csIndexx, csIndexy, csIndexz, csIndexa, csIndexb, csIndexc);
        public static int GetAxisDefinitions(out int csIndexx, out int csIndexy, out int csIndexz, out int csIndexa, out int csIndexb, out int csIndexc) => _ccmotion!.GetAxisDefinitions(out csIndexx, out csIndexy, out csIndexz, out csIndexa, out csIndexb, out csIndexc);

        /// <summary>
        /// JSON-serializable response object.
        /// </summary>
        public class IpcResponse
        {
            public string Status { get; set; } = string.Empty;
            public string Result { get; set; } = string.Empty;
            public Segment[]? Segments { get; set; }     // your transformed joint‐motion data
            public string? Error { get; set; }
        }

        public class Segment
        {
            public double[]? JointAngles { get; set; }
            public double DurationMs { get; set; }
            // add any other fields you serialized…
        }



    }

    
}

