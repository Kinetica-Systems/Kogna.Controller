using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

using System.Net;
using System.Net.Sockets;
using AppServer;
using TCPServer;
using KinematicEngine.Core;
using KinematicEngine.Kinematics;
using KinematicEngine;
using KinematicEngine.Configuration;
using System.ComponentModel;

namespace KognaComms;

public class KognaControl
{
    public KognaMonitor _monitor { get; set; }
    public KognaMotion _coord { get; set; }
    public RefactoredKinematicEngine _engine { get; set; }
    public IpcServer _ipcServer { get; set; }
    public KServer _tcpServer { get; set; }
    public KognaIO _io { get; set; }
    public KognaControl _control { get; set; }
    private int intPort = 5000;
    private double _lastFeedRate;

    //public event Action<KognaStatus>? OnStatusUpdate;



    public KognaControl(string ipAddress, int port)
    {
        _control = this;
        _io = new KognaIO(ipAddress, port);
        _coord = new KognaMotion(_io);
        _monitor = new KognaMonitor(_io, _coord);
        _tcpServer = new KServer(ipAddress, port, _monitor, _coord, _io);
        _ipcServer = new IpcServer(intPort, this);
        var kinematics = new Fanuc6AxisKinematics();
        _engine = new RefactoredKinematicEngine(_coord, kinematics);
        _lastFeedRate = 100.0; // Initialize with default feed rate
    }
    public async Task<bool> Start()
    {
        _tcpServer.Start(); //TCP server starts the monitor heartbeat
        _ipcServer.Start();
        var config = new EngineConfiguration
        {
            AxisCount = 6,
            MaxVelocities = EngineConstants.DefaultLimits.MAX_VELOCITIES,
            MaxAccelerations = EngineConstants.DefaultLimits.MAX_ACCELERATIONS,
            MaxJerks = EngineConstants.DefaultLimits.MAX_JERKS,
            EnableSoftLimits = true,
            BufferSafetyMargin = 2,  // Trigger shutdown when 2 or fewer segments remain
            // Set soft limits based on workspace and joint limits
            SoftLimitsPositive = new double[]
            {
                2000.0,  // X axis (mm)
                2000.0,  // Y axis (mm)
                3000.0,  // Z axis (mm)
                180.0,   // A axis (degrees)
                90.0,    // B axis (degrees)
                180.0    // C axis (degrees)
            },
            SoftLimitsNegative = new double[]
            {
                -2000.0, // X axis (mm)
                -2000.0, // Y axis (mm)
                0.0,     // Z axis (mm)
                -180.0,  // A axis (degrees)
                -90.0,   // B axis (degrees)
                -180.0   // C axis (degrees)
            }
        };
        
        await _engine.InitializeAsync(config);
        await _engine.StartAsync();
        return true;
    }
    

    public async Task<(string response, string result)> ProcessIpcCommand(string commandLine) //take the string, figure out where its meant to be directed to and send it there.
    {
        Console.WriteLine("hit IPC entry");

        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return ("Error - Empty command", "Error - Empty command");
        }
        var parts = commandLine.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();
        var payload = parts.Length > 1 ? parts[1] : string.Empty;
        Console.WriteLine($"OK-Payload:{payload}");
        Console.WriteLine($"OK-cmd:{cmd}");
        try
        {
            string response = string.Empty;
            // 1) setcs - Set coordinate system
            if (cmd == "setcs")
            {
                Console.WriteLine($"setcs called with payload: {payload}");
                try
                {
                    var setcsParts = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (setcsParts.Length >= 1 && int.TryParse(setcsParts[0], out var systemNumber))
                    {
                        _engine.CoordinateSystemManager.SetActiveSystem(systemNumber);
                        response = $"Coordinate system set to {_engine.CoordinateSystemManager.ActiveSystem.Name}";
                    }
                    else
                    {
                        response = "Error: Invalid coordinate system number";
                    }
                }
                catch (Exception ex)
                {
                    response = $"Error setting coordinate system: {ex.Message}";
                }
                return (response, response);
            }
            
            // 2) getcs - Get current coordinate system
            if (cmd == "getcs")
            {
                Console.WriteLine($"getcs called");
                try
                {
                    var activeSystem = _engine.CoordinateSystemManager.ActiveSystem;
                    response = $"Active: {activeSystem.Name}, System: {activeSystem.SystemNumber}";
                }
                catch (Exception ex)
                {
                    response = $"Error getting coordinate system: {ex.Message}";
                }
                return (response, response);
            }
            
            // 3) zero - Zero the active coordinate system at current position
            if (cmd == "zero")
            {
                Console.WriteLine($"zero called");
                try
                {
                    // Get current machine position
                    var currentPosition = new double[8];
                    for (int i = 0; i < _coord.AxisCount; i++)
                    {
                        currentPosition[i] = _coord.GetPosition(i);
                    }
                    
                    _engine.CoordinateSystemManager.ZeroActiveSystem(currentPosition);
                    response = $"Zeroed {_engine.CoordinateSystemManager.ActiveSystem.Name} at current position";
                }
                catch (Exception ex)
                {
                    response = $"Error zeroing coordinate system: {ex.Message}";
                }
                return (response, response);
            }
            
            // 4) jog - Manual jogging
            if (cmd == "jog")
            {
                Console.WriteLine($"jog called with payload: {payload}");
                try
                {
                    var jogParts = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (jogParts.Length >= 2)
                    {
                        var axis = jogParts[0].ToUpper();
                        if (double.TryParse(jogParts[1], out var distance))
                        {
                            // Create a jog command
                            var jogCommand = new MotionCommand
                            {
                                Type = MotionType.Linear,
                                StartPosition = new double[8],
                                EndPosition = new double[8],
                                FeedRate = 50.0, // Slow jog speed
                                Acceleration = 100.0,
                                Jerk = 1000.0,
                                UseMachineCoordinates = true // Jog in machine coordinates
                            };
                            
                            // Get current position
                            for (int i = 0; i < _coord.AxisCount; i++)
                            {
                                jogCommand.StartPosition[i] = _coord.GetPosition(i);
                                jogCommand.EndPosition[i] = _coord.GetPosition(i);
                            }
                            
                            // Set target position based on axis
                            int axisIndex = axis switch
                            {
                                "X" => 0,
                                "Y" => 1,
                                "Z" => 2,
                                "A" => 3,
                                "B" => 4,
                                "C" => 5,
                                "U" => 6,
                                "V" => 7,
                                _ => -1
                            };
                            
                            if (axisIndex >= 0 && axisIndex < _coord.AxisCount)
                            {
                                jogCommand.EndPosition[axisIndex] += distance;
                                
                                var result = await _engine.ProcessCommandAsync(jogCommand);
                                if (result.Success)
                                {
                                    response = $"Jogged {axis} by {distance}";
                                }
                                else
                                {
                                    response = $"Jog failed: {result.ErrorMessage}";
                                }
                            }
                            else
                            {
                                response = $"Error: Invalid axis '{axis}'";
                            }
                        }
                        else
                        {
                            response = "Error: Invalid distance value";
                        }
                    }
                    else
                    {
                        response = "Error: Usage: jog <axis> <distance>";
                    }
                }
                catch (Exception ex)
                {
                    response = $"Error during jog: {ex.Message}";
                }
                return (response, response);
            }
            if (cmd == "gcode")
            {
                Console.WriteLine($"GCode called");

                // Convert G-code string to motion command
                var command = ParseGCodeToMotionCommand(payload);
                if (command != null)
                {
                    var result = await _engine.ProcessCommandAsync(command);
                    if (!result.Success)
                    {
                        response = $"Error: {result.ErrorMessage}";
                        return (response, response);
                    }
                } 
                return (response, response);

            }
            if (cmd == "version")
            {
                Console.WriteLine($"Version called");
                var ok = _io.WriteLineReadLine(0, $"Version", out response);
                Console.WriteLine($"Version: {response}");
                return (response, response);

            }
            if (cmd == "reset")
            {
                Console.WriteLine($"Manual reset called");
                _engine.ManualReset();
                response = "Manual reset completed";
                return (response, response);

            }

            else
            {
                Console.WriteLine($"other command called");
                var ok = _io.WriteLineReadLine(0, $"{cmd}", out response);
                Console.WriteLine($"resp: {response}");
                return (response, response);
            }
        }
        catch (Exception ex)
        {

            // log the full exception to console
            Console.WriteLine($"[ENGINE ERROR] {ex}");
            string response = "Engine Error {ex}";
            return (response, response);
            // return full message+stack and an empty segments array (never null)


        }
        /*return response;
        /*
                double ParseAxis(char letter, double current)
                {
                    var m = Regex.Match(payload, $@"\b{letter}([-+]?\d*\.?\d+)", RegexOptions.IgnoreCase);
                    return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
                        ? v
                        : current;
                }
        */
    }



    public async Task<string> SendGCodeLine(string cmd, int board)
    {
        var command = ParseGCodeToMotionCommand(cmd);
        if (command != null)
        {
            var result = await _engine.ProcessCommandAsync(command);
            if (!result.Success)
            {
                return $"Error: {result.ErrorMessage}";
            }
        }
        _io.WriteLineReadLine(board, cmd, out var resp);
        return resp;
    }


    public string SendCommand(string cmd, int board)
    {
        _io.WriteLineReadLine(board, cmd, out var resp);
        return resp;

    }
    
        public const int KOGNA_OK = 0;
        public const int KOGNA_TIMEOUT = 1;
        public const int KOGNA_ERROR = 2;
        public const int KOGNA_READY = 3;
        public const int KOGNA_LOCKED = 4;
        public const int KOGNA_IN_USE = 5;
        public const int KOGNA_NOT_CONNECTED = 6;

        /// <summary>
        /// Parses G-code string into a motion command
        /// </summary>
        /// <param name="gcode">G-code string to parse</param>
        /// <returns>Motion command or null if parsing fails</returns>
        private MotionCommand? ParseGCodeToMotionCommand(string gcode)
        {
            try
            {
                var parts = gcode.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return null;

                var command = new MotionCommand
                {
                    SequenceNumber = 0, // Will be assigned by engine
                    StartPosition = new double[8],
                    EndPosition = new double[8],
                    ArcCenter = new double[2],
                    FeedRate = _lastFeedRate > 0 ? _lastFeedRate : 100.0, // Default to 100 if no previous feed rate
                    Acceleration = 100.0,
                    Jerk = 1000.0
                };

                // Track which axes are set
                bool[] axisSet = new bool[8];

                for (int i = 0; i < 8; i++)
                {
                    // Default to NaN so we can detect unset axes
                    command.EndPosition[i] = double.NaN;
                }

                foreach (var part in parts)
                {
                    if (part.Length < 2) continue;

                    var code = part[0].ToString().ToUpper();
                    var value = part.Substring(1);

                    switch (code)
                    {
                        case "G":
                            switch (value)
                            {
                                case "0":
                                    command.Type = MotionType.Rapid;
                                    break;
                                case "1":
                                    command.Type = MotionType.Linear;
                                    break;
                                case "2":
                                    command.Type = MotionType.Arc;
                                    command.IsClockwise = true;
                                    break;
                                case "3":
                                    command.Type = MotionType.Arc;
                                    command.IsClockwise = false;
                                    break;
                                case "4":
                                    command.Type = MotionType.Dwell;
                                    if (double.TryParse(parts.FirstOrDefault(p => p.StartsWith("P")), out var dwellTime))
                                        command.DwellTime = dwellTime;
                                    break;
                                case "53":
                                    command.UseMachineCoordinates = true;
                                    command.CoordinateSystem = 0;
                                    break;
                                case "54":
                                    command.UseMachineCoordinates = false;
                                    command.CoordinateSystem = 1;
                                    break;
                                case "55":
                                    command.UseMachineCoordinates = false;
                                    command.CoordinateSystem = 2;
                                    break;
                                case "56":
                                    command.UseMachineCoordinates = false;
                                    command.CoordinateSystem = 3;
                                    break;
                                case "57":
                                    command.UseMachineCoordinates = false;
                                    command.CoordinateSystem = 4;
                                    break;
                                case "58":
                                    command.UseMachineCoordinates = false;
                                    command.CoordinateSystem = 5;
                                    break;
                                case "59":
                                    command.UseMachineCoordinates = false;
                                    command.CoordinateSystem = 6;
                                    break;
                            }
                            break;
                        case "X":
                            if (double.TryParse(value, out var x)) { command.EndPosition[0] = x; axisSet[0] = true; }
                            break;
                        case "Y":
                            if (double.TryParse(value, out var y)) { command.EndPosition[1] = y; axisSet[1] = true; }
                            break;
                        case "Z":
                            if (double.TryParse(value, out var z)) { command.EndPosition[2] = z; axisSet[2] = true; }
                            break;
                        case "A":
                            if (double.TryParse(value, out var a)) { command.EndPosition[3] = a; axisSet[3] = true; }
                            break;
                        case "B":
                            if (double.TryParse(value, out var b)) { command.EndPosition[4] = b; axisSet[4] = true; }
                            break;
                        case "C":
                            if (double.TryParse(value, out var c)) { command.EndPosition[5] = c; axisSet[5] = true; }
                            break;
                        case "F":
                            if (double.TryParse(value, out var f))
                            {
                                command.FeedRate = f;
                                _lastFeedRate = f;
                            }
                            break;
                        case "I":
                            if (double.TryParse(value, out var i)) command.ArcCenter[0] = i;
                            break;
                        case "J":
                            if (double.TryParse(value, out var j)) command.ArcCenter[1] = j;
                            break;
                    }
                }

                // Fill in missing axes with current position
                int axisCount = _coord.AxisCount;
                for (int i = 0; i < axisCount; i++)
                {
                    if (!axisSet[i] || double.IsNaN(command.EndPosition[i]))
                    {
                        command.EndPosition[i] = _coord.GetPosition(i);
                    }
                }

                return command;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GCODE_PARSER] Error parsing G-code: {ex.Message}");
                return null;
            }
        }
}
   