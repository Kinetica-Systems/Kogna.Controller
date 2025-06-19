using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using KinematicEngine;
using KognaServer.Server.KognaServer;
using Newtonsoft.Json;
using Semi.Avalonia.Tokens;
using System.Text.RegularExpressions;

namespace KognaServer.Server.KinematicEngine
{
    /// <summary>
    /// Standalone TCP server exposing a JSON IPC interface to your kinematic engine.
    /// </summary>
    public class KinematicEngineServer : IDisposable
    {
        private readonly TcpListener _listener;
        private bool _running;
        private double _lastFeedRate = 0.0;   // <-- new field
        private static readonly CCoordMotion _engine = new CCoordMotion();
        private readonly RS274NGC.GCodeInterpreter _interp = new RS274NGC.GCodeInterpreter(_engine);
        /// <summary>
        /// Initializes a new server listening on the specified IP and port.
        /// </summary>
        public KinematicEngineServer(KognaServerMain serverMain, int port = 5001)
        {
            _listener = new TcpListener(IPAddress.Loopback, port);
            
            
        }

        /// <summary>
        /// Starts accepting client connections asynchronously.
        /// </summary>
        public async Task StartAsync()
        {
            _listener.Start();
            _running = true;

            Console.WriteLine($"KinematicEngineServer listening on {_listener.LocalEndpoint}");

            while (_running)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    _ = Task.Run(() => HandleClientAsync(client));
                }
                catch (ObjectDisposedException)
                {
                    // Listener was stopped
                    break;
                }
            }
        }

        /// <summary>
        /// Stops the server and closes the listener.
        /// </summary>
        public void Stop()
        {
            _running = false;
            _listener.Stop();
        }

        /// <summary>
        /// Handles communication with a connected client: reads commands, processes them, and returns JSON responses.
        /// </summary>
       private async Task HandleClientAsync(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
            {
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    // ← ADD THIS
                    Console.WriteLine($"[SERVER RECEIVED] {line}");

                    IpcResponse response;
                    try
                    {
                        response = await ProcessCommandAsync(line).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // catch any solver/interpreter errors so we still reply
                        Console.WriteLine($"[SERVER ERROR] {ex}");
                        response = new IpcResponse {
                        Status = "Error",
                        Error  = ex.Message,
                        Segments = Array.Empty<Segment>()
                        };
                    }

                    var json = JsonConvert.SerializeObject(response);
                    await writer.WriteLineAsync(json).ConfigureAwait(false);

                    // ← AND THIS
                 //   Console.WriteLine($"[SERVER SENT] {json}");
                }
            }
        }


        public async Task<IpcResponse> ProcessCommandAsync(string commandLine)
        {
            
            var match = Regex.Match(commandLine, @"\bF([\d\.]+)", RegexOptions.IgnoreCase);
            if (match.Success && 
                double.TryParse(match.Groups[1].Value, out double fVal))
            {
                _lastFeedRate = fVal;
            }
            if (string.IsNullOrWhiteSpace(commandLine))
                return new IpcResponse { Status = "Error", Error = "Empty command" };
            var parts = commandLine.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts[0].ToLowerInvariant();
            var payload = parts.Length > 1 ? parts[1] : string.Empty;
            Console.WriteLine($"OK-Payload:{payload}");
            Console.WriteLine($"OK-cmd:{cmd}");
            try
            {
                // 1) definecs
                if (cmd == "definecs")
                {
                    if (!int.TryParse(payload, out var csIndex))
                        return new IpcResponse { Status = "Error", Error = "Invalid CS index" };

                    bool ok = await Task.Run(() =>
                    _engine.SetAxisDefinitions(csIndex, csIndex, csIndex, csIndex, csIndex, csIndex) == 0
                    ).ConfigureAwait(false);

                    return ok
                        ? new IpcResponse { Status = "OK", Result = csIndex.ToString() }
                        : new IpcResponse { Status = "Error", Error = "DefineCS failed" };
                }

                // 2) posN
                if (cmd.StartsWith("pos") && int.TryParse(cmd.Substring(3), out var axisPos))
                {
                    double position = await Task.Run(() =>
                    {
                        _engine.GetPosition(axisPos, out var val);
                        return val;
                    }).ConfigureAwait(false);

                    return new IpcResponse
                    {
                        Status = "OK",
                        Result = position.ToString(CultureInfo.InvariantCulture)
                    };
                }

                // 3) destN
                if (cmd.StartsWith("dest") && int.TryParse(cmd.Substring(4), out var axisDest))
                {
                    double dest = await Task.Run(() =>
                    {
                        _engine.GetDestination(axisDest, out var d);
                        return d;
                    }).ConfigureAwait(false);

                    return new IpcResponse
                    {
                        Status = "OK",
                        Result = dest.ToString(CultureInfo.InvariantCulture)
                    };
                }

                // 4) fallback → G-code interpreter
                Console.WriteLine("[ENGINE] About to _interp.Execute()");
                var status = 0;
                
                Console.WriteLine($"[ENGINE] interpreter returned status={status}");
                var message = RS274NGC.GetLastMessage();
                bool success = status == RS274NGC.RS274NGC_OK
                            || status == RS274NGC.RS274NGC_EXECUTE_FINISH;


                double x, y, z, a, b, c, u, v;

                // axis 1–6 are X,Y,Z,A,B,C
                _engine.GetPosition(1, out x);
                _engine.GetPosition(2, out y);
                _engine.GetPosition(3, out z);
                _engine.GetPosition(4, out a);
                _engine.GetPosition(5, out b);
                _engine.GetPosition(6, out c);
                _engine.GetPosition(7, out u);
                _engine.GetPosition(8, out v);

                Console.WriteLine($"{x} {y} {z} {a} {b} {c} {u} {v}");
                int seqNo = _engine.GetNextSequenceNumber();
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

                        _engine.StraightTraverse(x, y, z, a, b, c, u, v, seqNo, 0, _lastFeedRate);
                        
                    _engine.GetPosition(1, out var newX);
                    _engine.GetPosition(2, out var newY);
                    _engine.GetPosition(3, out var newZ);
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
                    };
                }

                return new IpcResponse
                {
                    Status = success ? "OK" : "Error",
                    Result = success ? message : string.Empty,
                    Error = success ? null : message,
                    Segments = segments
                };
            }
            catch (Exception ex)
            {
              
            // log the full exception to console
            Console.WriteLine($"[ENGINE ERROR] {ex}");

            // return full message+stack and an empty segments array (never null)
            return new IpcResponse {
                Status   = "Error",
                Error    = ex.ToString(),                   // full stack trace
                Segments = Array.Empty<Segment>()           // avoid null
            };
            }
                double ParseAxis(char letter, double current)
                {
                    var m = Regex.Match(payload, $@"\b{letter}([-+]?\d*\.?\d+)", RegexOptions.IgnoreCase);
                    return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
                        ? v
                        : current;
                }
        }
    
        private bool IsMotionCommand(string cmd) => cmd == "G0" || cmd == "G1" || cmd == "G2" || cmd == "G3";


        /// <summary>
        /// Defines the coordinate system mapping. Uses the same index for all axes by default.
        /// </summary>
        public static Task<bool> DefineCSAsync(int csIndex)
            => Task.Run(() =>
            {
                // Use the index for X,Y,Z,A,B,C axes; U and V remain unmapped (-1)
                int result = _engine.SetAxisDefinitions(
                    csIndex, csIndex, csIndex,
                    csIndex, csIndex, csIndex
                );
                return result == 0;
            });

        /// <summary>
        /// Gets the current target (destination) for the specified axis.
        /// </summary>
        public static Task<double> GetDestinationAsync(int axis)
            => Task.Run(() =>
            {
                _engine.GetDestination(axis, out double dest);
                return dest;
            });

        /// <summary>
        /// Stops the server when disposing.
        /// </summary>
        public void Dispose() => Stop();

        /// <summary>
        /// JSON-serializable response object.
        /// </summary>
        public class IpcResponse
        {
            public string Status { get; set; } = string.Empty;
            public string Result { get; set; } = string.Empty;
            public Segment[] Segments { get; set; }     // your transformed joint‐motion data
            public string? Error { get; set; }
        }


        public class Segment
        {
            public double[] JointAngles { get; set; }
            public double DurationMs { get; set; }
            // add any other fields you serialized…
        }



    }
    
}

