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
using KinematicEngine.Configuration;
using System.ComponentModel;
using System.Linq;
using System.Diagnostics;
using System.IO;
using GeometryEngine.Core;
using GeometryEngine.Implementation;
using SharedTypes;
using System.Threading; // add for CancellationTokenSource

namespace KognaComms;

public class KognaControl : IDisposable
{
    private readonly object _startLock = new object();
    private bool _isStarted;
    private bool _disposed;

    public TCPServer.KognaMonitor _monitor { get; private set; }
    public KognaMotion _coord { get; private set; }
    public RefactoredKinematicEngine _engine { get; private set; }
    public IpcServer _ipcServer { get; private set; }
    public KServer _tcpServer { get; private set; }
    public KognaIO _io { get; private set; }
    private readonly int _intPort = 5000;
    private double _lastFeedRate;

    // Add new fields for geometry engine
    private readonly GeometryEngine.Implementation.GeometryEngine _geometryEngine;
    private readonly ToolpathConverter _toolpathConverter;
    private readonly ToolpathConfig _toolpathConfig;
    private List<Layer>? _slicedLayers;
    private readonly CancellationTokenSource _monitorCts = new();

    public KognaControl(string ipAddress, int port)
    {
        if (string.IsNullOrEmpty(ipAddress))
        {
            throw new ArgumentNullException(nameof(ipAddress));
        }

        _io = new KognaIO(ipAddress, port);
        _coord = new KognaMotion(_io);
        _monitor = new KognaMonitor(_io, _coord);
        _tcpServer = new KServer(ipAddress, port, _monitor, _coord, _io);
        _ipcServer = new IpcServer(_intPort, this);
        var kinematics = new Fanuc6AxisKinematics();
        _engine = new RefactoredKinematicEngine(_coord, kinematics);
        _lastFeedRate = 100.0; // Initialize with default feed rate
        
        // Initialize geometry engine components
        _geometryEngine = new GeometryEngine.Implementation.GeometryEngine();
        _toolpathConfig = new ToolpathConfig
        {
            ExtrusionWidth = 0.4,
            PrintSpeed = 60,
            TravelSpeed = 120,
            RetractLength = 4,
            RetractSpeed = 45
        };
        _toolpathConverter = new ToolpathConverter(_toolpathConfig);
        
        // Set up console handler to capture C program output
        _io.SetConsoleCallback((board, message) =>
        {
            Console.WriteLine($"[KOGNA_CONSOLE] {message}");
            return 0;
        });
    }

    public async Task<bool> Start()
    {
        ThrowIfDisposed();

        lock (_startLock)
        {
            if (_isStarted)
            {
                throw new InvalidOperationException("KognaControl is already started");
            }
        }

        try
        {
            Console.WriteLine("[KOGNA_CONTROL] Starting services...");

            // Always start IPC listener first so UI can connect even if hardware is offline
            _ipcServer.Start();
            Console.WriteLine("[KOGNA_CONTROL] IPC server started successfully");

            // Try to start the hardware‐facing TCP server, but don’t abort entire startup on failure
            if (!_tcpServer.Start())
            {
                Console.WriteLine("[KOGNA_CONTROL] WARNING: Hardware TCP server failed to start (running in offline mode)");
            }
            else
            {
                Console.WriteLine("[KOGNA_CONTROL] TCP server started successfully");
            }

            // Configure and start the kinematic engine
            var config = new EngineConfiguration
            {
                AxisCount = 6,
                MaxVelocities = EngineConstants.DefaultLimits.MAX_VELOCITIES,
                MaxAccelerations = EngineConstants.DefaultLimits.MAX_ACCELERATIONS,
                MaxJerks = EngineConstants.DefaultLimits.MAX_JERKS,
                EnableSoftLimits = true,
                BufferSafetyMargin = 2,  // Trigger shutdown when 2 or fewer segments remain
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

            // Initialize and start the engine
            await _engine.InitializeAsync(config);
            await _engine.StartAsync();

            Console.WriteLine("[KOGNA_CONTROL] Kinematic engine started successfully");

            // Attempt to connect to the robot controller and start the status monitor
            if (!_io.Connected)
            {
                Console.WriteLine("[KOGNA_CONTROL] Attempting robot connection …");
                if (_io.Connect())
                {
                    Console.WriteLine("[KOGNA_CONTROL] Robot connection established");
                    _ = _monitor.StartAsyncMonitor(_monitorCts.Token); // fire-and-forget
                }
                else
                {
                    Console.WriteLine($"[KOGNA_CONTROL] WARNING: Robot connection failed – {_io.ErrMsg}");
                }
            }

            lock (_startLock)
            {
                _isStarted = true;
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KOGNA_CONTROL] Error during startup: {ex.Message}");
            Console.WriteLine($"[KOGNA_CONTROL] Stack trace: {ex.StackTrace}");

            // Attempt to clean up on failure
            try
            {
                await Stop();
            }
            catch (Exception stopEx)
            {
                Console.WriteLine($"[KOGNA_CONTROL] Error during cleanup: {stopEx.Message}");
            }

            return false;
        }
    }

    public async Task Stop()
    {
        ThrowIfDisposed();

        lock (_startLock)
        {
            if (!_isStarted)
            {
                return;
            }
        }

        try
        {
            Console.WriteLine("[KOGNA_CONTROL] Stopping services...");

            // Stop the engine first
            try
            {
                await _engine.StopAsync();
                Console.WriteLine("[KOGNA_CONTROL] Kinematic engine stopped successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[KOGNA_CONTROL] Error stopping kinematic engine: {ex.Message}");
            }

            // Stop the TCP server
            try
            {
                _tcpServer.Stop();
                Console.WriteLine("[KOGNA_CONTROL] TCP server stopped successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[KOGNA_CONTROL] Error stopping TCP server: {ex.Message}");
            }

            // Stop the IPC server
            try
            {
                _ipcServer.Stop();
                Console.WriteLine("[KOGNA_CONTROL] IPC server stopped successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[KOGNA_CONTROL] Error stopping IPC server: {ex.Message}");
            }

            // Cancel monitor loop
            try { _monitorCts.Cancel(); } catch {}

            lock (_startLock)
            {
                _isStarted = false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KOGNA_CONTROL] Error during shutdown: {ex.Message}");
            throw;
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Stop all services
                Stop().Wait();

                // Dispose managed resources
                _io?.Dispose();
                _geometryEngine?.Dispose();
                (_engine as IDisposable)?.Dispose();
            }

            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(KognaControl));
        }
    }

    ~KognaControl()
    {
        Dispose(false);
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
            // --- connection status -------------------------------------------------
            if (cmd == "isconnected")
            {
                var isConnected = _io.Connected;
                response = isConnected.ToString().ToLowerInvariant();
                return (response, response);
            }
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
                    response = "GCode command executed successfully";
                } 
                else
                {
                    response = "Error: Failed to parse GCode command";
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

            // 5) laser - Laser control commands
            if (cmd == "laser")
            {
                Console.WriteLine($"Laser control called with payload: {payload}");
                try
                {
                    var laserParts = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (laserParts.Length >= 2)
                    {
                        var laser = laserParts[0]; // "1" or "2"
                        var action = laserParts[1]; // "on", "off", or power value
                        
                        string command;
                        int channel = laser switch
                        {
                            "1" => 8,  // Laser 1 on channel 8
                            "2" => 9,  // Laser 2 on channel 9
                            _ => 8     // Default to laser 1
                        };
                        
                        switch (action.ToLower())
                        {
                            case "on":
                                command = $"M42 P{channel} S255"; // Full power
                                break;
                            case "off":
                                command = $"M42 P{channel} S0"; // No power
                                break;
                            default:
                                // Assume it's a power value (0-255)
                                if (int.TryParse(action, out var power) && power >= 0 && power <= 255)
                                {
                                    command = $"M42 P{channel} S{power}";
                                }
                                else
                                {
                                    response = "Error: Power value must be 0-255";
                                    return (response, response);
                                }
                                break;
                        }
                        
                        var ok = _io.WriteLineReadLine(1, command, out response);
                        response = ok == KOGNA_OK ? $"Laser {laser} {action}" : "Laser command failed";
                    }
                    else
                    {
                        response = "Error: Laser command requires laser number and action (usage: laser <1|2> <on|off|0-255>)";
                    }
                }
                catch (Exception ex)
                {
                    response = $"Error controlling laser: {ex.Message}";
                }
                return (response, response);
            }

            // 6) wirefeeder - Wire feeder control commands
            if (cmd == "wirefeeder")
            {
                Console.WriteLine($"Wire feeder control called with payload: {payload}");
                try
                {
                    var feederParts = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (feederParts.Length >= 2)
                    {
                        var action = feederParts[0]; // "step", "dir", "enable", "disable"
                        var value = feederParts[1];   // "high", "low", "1", "0"
                        
                        int channel = action switch
                        {
                            "step" => 10,     // Step signal on channel 10
                            "dir" => 11,      // Direction signal on channel 11
                            _ => 10           // Default to step
                        };
                        
                        int state = value switch
                        {
                            "high" or "1" => 1,
                            "low" or "0" => 0,
                            _ => 0
                        };
                        
                        var command = $"M42 P{channel} S{state}";
                        var ok = _io.WriteLineReadLine(1, command, out response);
                        response = ok == KOGNA_OK ? $"Wire feeder {action} {value}" : "Wire feeder command failed";
                    }
                    else
                    {
                        response = "Error: Wire feeder command requires action and value (usage: wirefeeder <step|dir> <high|low|1|0>)";
                    }
                }
                catch (Exception ex)
                {
                    response = $"Error controlling wire feeder: {ex.Message}";
                }
                return (response, response);
            }

            // 7) pwm - Direct PWM control
            if (cmd == "pwm")
            {
                Console.WriteLine($"PWM control called with payload: {payload}");
                try
                {
                    var pwmParts = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (pwmParts.Length >= 2)
                    {
                        var pin = pwmParts[0];
                        var value = pwmParts[1];
                        
                        if (int.TryParse(pin, out var pinNum) && int.TryParse(value, out var pwmValue))
                        {
                            if (pwmValue >= 0 && pwmValue <= 255)
                            {
                                var command = $"M42 P{pinNum} S{pwmValue}";
                                var ok = _io.WriteLineReadLine(1, command, out response);
                                response = ok == KOGNA_OK ? $"PWM pin {pinNum} set to {pwmValue}" : "PWM command failed";
                            }
                            else
                            {
                                response = "Error: PWM value must be 0-255";
                            }
                        }
                        else
                        {
                            response = "Error: Invalid pin number or PWM value";
                        }
                    }
                    else
                    {
                        response = "Error: PWM command requires pin and value (usage: pwm <pin> <0-255>)";
                    }
                }
                catch (Exception ex)
                {
                    response = $"Error setting PWM: {ex.Message}";
                }
                return (response, response);
            }

            // Add 3D printing commands
            if (cmd == "loadstl")
            {
                Console.WriteLine($"Loading STL file: {payload}");
                try
                {
                    var success = await _geometryEngine.LoadModelAsync(payload);
                    response = success ? "STL file loaded successfully" : "Failed to load STL file";
                }
                catch (Exception ex)
                {
                    response = $"Error loading STL file: {ex.Message}";
                }
                return (response, response);
            }

            if (cmd == "slice")
            {
                Console.WriteLine($"Slicing model with parameters: {payload}");
                try
                {
                    var sliceParams = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (sliceParams.Length >= 1 && double.TryParse(sliceParams[0], out var layerHeight))
                    {
                        var config = new SlicingConfig
                        {
                            LayerHeight = layerHeight,
                            PerimeterCount = sliceParams.Length > 1 && int.TryParse(sliceParams[1], out var p) ? p : 3,
                            InfillDensity = sliceParams.Length > 2 && double.TryParse(sliceParams[2], out var d) ? d : 0.2,
                            InfillPattern = sliceParams.Length > 3 ? sliceParams[3] : "grid"
                        };

                        var layers = await _geometryEngine.SliceModelAsync(layerHeight, config);
                        _slicedLayers = layers.ToList(); // Store layers for preview
                        var toolpaths = await _geometryEngine.GenerateToolpathsAsync(_slicedLayers, _toolpathConfig);

                        // Convert toolpaths to motion commands
                        var commands = new List<MotionCommand>();
                        foreach (var toolpath in toolpaths)
                        {
                            commands.AddRange(_toolpathConverter.ConvertToolpath(toolpath));
                        }

                        // Execute motion commands
                        foreach (var command in commands)
                        {
                            var result = await _engine.ProcessCommandAsync(command);
                            if (!result.Success)
                            {
                                response = $"Error executing motion: {result.ErrorMessage}";
                                return (response, response);
                            }
                        }

                        response = "Model sliced and printed successfully";
                    }
                    else
                    {
                        response = "Error: Invalid layer height";
                    }
                }
                catch (Exception ex)
                {
                    response = $"Error slicing model: {ex.Message}";
                }
                return (response, response);
            }

            if (cmd == "preview")
            {
                Console.WriteLine($"Generating preview G-code");
                try
                {
                    var config = new GCodeConfig
                    {
                        StartX = 0,
                        StartY = 0,
                        StartZ = 10,
                        RelativeExtrusion = true,
                        StartGCode = "G28 ; Home all axes\nG1 Z10 F1000 ; Raise Z\nM109 S200 ; Wait for hotend temp\nM190 S60 ; Wait for bed temp\nG92 E0 ; Reset extruder",
                        EndGCode = "M104 S0 ; Turn off hotend\nM140 S0 ; Turn off bed\nG91 ; Relative positioning\nG1 E-3 F1800 ; Retract\nG1 Z10 F1000 ; Raise Z\nG90 ; Absolute positioning\nG1 X0 Y0 ; Present print\nM84 ; Disable motors"
                    };

                    if (_slicedLayers is null || _slicedLayers.Count == 0)
                    {
                        response = "Error: Slice the model first (no layers present)";
                        return (response, response);
                    }
                    var toolpaths = await _geometryEngine.GenerateToolpathsAsync(_slicedLayers, _toolpathConfig);
                    var gcode = await _geometryEngine.GenerateGCodeAsync(toolpaths, config);

                    response = string.Join("\n", gcode);
                }
                catch (Exception ex)
                {
                    response = $"Error generating preview: {ex.Message}";
                }
                return (response, response);
            }

            if (cmd == "loadgcode")
            {
                Console.WriteLine($"Loading G-code file: {payload}");
                try
                {
                    var content = await File.ReadAllTextAsync(payload);
                    response = content;
                }
                catch (Exception ex)
                {
                    response = $"Error loading G-code file: {ex.Message}";
                }
                return (response, response);
            }

            if (cmd == "setgcode")
            {
                Console.WriteLine($"Setting G-code content");
                response = payload;
                return (response, response);
            }

            // RS485 Passthrough for FS50L Servo Drives
            if (cmd == "rs485")
            {
                Console.WriteLine($"RS485 passthrough command called with payload: {payload}");
                try
                {
                    var rs485Parts = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (rs485Parts.Length >= 2)
                    {
                        var slaveAddress = rs485Parts[0]; // Slave address (1-247)
                        var register = rs485Parts[1];     // Register address (hex or decimal)
                        var value = rs485Parts.Length > 2 ? rs485Parts[2] : "0"; // Value (0 for read)

                        // Validate slave address
                        if (!int.TryParse(slaveAddress, out var addr) || addr < 1 || addr > 247)
                        {
                            response = "Error: Slave address must be 1-247";
                            return (response, response);
                        }

                        // Parse register - support both hex (0x3002) and decimal formats
                        int regAddr;
                        if (register.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        {
                            // Parse hex value
                            if (!int.TryParse(register.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out regAddr))
                            {
                                response = "Error: Invalid hex register address";
                                return (response, response);
                            }
                        }
                        else
                        {
                            // Parse decimal value
                            if (!int.TryParse(register, out regAddr))
                            {
                                response = "Error: Register must be a number or hex value (0x...)";
                                return (response, response);
                            }
                        }

                        if (!int.TryParse(value, out var regValue))
                        {
                            response = "Error: Value must be an integer (or omitted for read)";
                            return (response, response);
                        }

                        Console.WriteLine($"RS485 parameters: slave={addr}, register=0x{regAddr:X4} ({regAddr}), value={regValue}");
                        
                        // Set persist data using SetPersistDec
                        _io.WriteLine(0, $"SetPersistDec 0 {addr}");      // Slave ID
                        _io.WriteLine(0, $"SetPersistDec 1 {regAddr}");   // Register address
                        _io.WriteLine(0, $"SetPersistDec 2 {regValue}");  // Value to write

                        // Execute Thread 3 (program should be flashed to Thread 3 on Kogna)
                        Console.WriteLine("Executing Thread 3...");
                        
                        // Send Execute command and wait for response with longer timeout
                        _io.WriteLine(0, "Execute 3");
                        var ok = _io.ReadLineTimeOut(0, out var execResponse, 15000); // 15 second timeout
                        Console.WriteLine($"Execute result: {ok}, response: '{execResponse}'");
                        
                        if (ok == KOGNA_OK)
                        {
                            // Wait a moment for the C program to execute
                            await Task.Delay(200);  // Reduced from 500ms to 200ms
                            
                            Console.WriteLine("Reading result from persist data...");
                            // Use GetPersistDec to get the decimal integer value
                            var persistOk = _io.WriteLineReadLine(0, "GetPersistDec 10", out var persistResponse);
                            Console.WriteLine($"GetPersistDec returned: {persistOk}, response:'{persistResponse}'");
                            
                            int result = 0;
                            var hasValidResult = persistOk == KOGNA_OK && int.TryParse(persistResponse, out result);
                            
                            if (hasValidResult)
                            {
                                response = $"RS485 {slaveAddress} {register}: Result={result}";
                            }
                            else
                            {
                                response = $"RS485 {slaveAddress} {register}: Failed to read result from persist data";
                            }
                        }
                        else
                        {
                            response = "RS485 command failed: ExecThread 3 failed";
                        }
                    }
                    else
                    {
                        response = "Error: RS485 command requires slave address and register (usage: rs485 <slave> <register|0xHEX> [value])";
                        return (response, response);
                    }
                }
                catch (Exception ex)
                {
                    response = $"Error sending RS485 command: {ex.Message}";
                }
                return (response, response);
            }

            // RS232 Passthrough for LED/Laser Drivers
            if (cmd == "rs232")
            {
                Console.WriteLine($"RS232 passthrough command called with payload: {payload}");
                try
                {
                    var rs232Parts = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (rs232Parts.Length >= 3)
                    {
                        var slaveAddress = rs232Parts[0]; // Slave address
                        var register = rs232Parts[1];     // Register address
                        var value = rs232Parts.Length > 2 ? rs232Parts[2] : ""; // Value (empty for read)
                        
                        // Send M101 command to Kogna for RS232 communication
                        string mCommand = value.Length > 0 
                            ? $"M101 {slaveAddress} {register} {value}"  // Write command
                            : $"M101 {slaveAddress} {register}";         // Read command
                            
                        var ok = _io.WriteLineReadLine(1, mCommand, out response);
                        response = ok == KOGNA_OK ? $"RS232 {slaveAddress} {register}: {response}" : "RS232 command failed";
                    }
                    else
                    {
                        response = "Error: RS232 command requires slave address and register (usage: rs232 <slave> <register> [value])";
                    }
                }
                catch (Exception ex)
                {
                    response = $"Error sending RS232 command: {ex.Message}";
                }
                return (response, response);
            }

            // Convenience commands for common FS50L operations
            if (cmd == "servostatus")
            {
                Console.WriteLine($"Servo status command called with payload: {payload}");
                try
                {
                    var statusParts = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (statusParts.Length >= 2)
                    {
                        var slaveAddress = statusParts[0]; // Servo drive address (1-247)
                        var statusType = statusParts[1];   // Status type
                        
                        // Map status types to FS50L register addresses
                        string register = statusType.ToLower() switch
                        {
                            "running" => "3000",    // Communication set value
                            "frequency" => "3001",   // Running frequency
                            "voltage" => "3002",     // Bus voltage
                            "current" => "3004",     // Output current
                            "power" => "3005",       // Output power
                            "torque" => "3006",      // Output torque
                            "speed" => "3007",       // Running speed
                            "fault" => "8000",       // Drive fault information
                            "comfault" => "8001",    // Communication fault
                            _ => "3000"              // Default to running status
                        };
                        
                        // Send M100 read command
                        string mCommand = $"M100 {slaveAddress} {register}";
                        var ok = _io.WriteLineReadLine(1, mCommand, out response);
                        response = ok == KOGNA_OK ? $"Servo {slaveAddress} {statusType}: {response}" : "Servo status command failed";
                    }
                    else
                    {
                        response = "Error: Servo status command requires address and status type (usage: servostatus <address> <status_type>)";
                    }
                }
                catch (Exception ex)
                {
                    response = $"Error getting servo status: {ex.Message}";
                }
                return (response, response);
            }

            if (cmd == "servocontrol")
            {
                Console.WriteLine($"Servo control command called with payload: {payload}");
                try
                {
                    var controlParts = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (controlParts.Length >= 3)
                    {
                        var slaveAddress = controlParts[0]; // Servo drive address (1-247)
                        var action = controlParts[1];       // Control action
                        var value = controlParts[2];        // Value
                        
                        // Map control actions to FS50L register addresses and values
                        (string register, string controlValue) = action.ToLower() switch
                        {
                            "forward" => ("1000", "0001"),     // Forward run
                            "reverse" => ("1000", "0002"),     // Reverse run
                            "jog_forward" => ("1000", "0003"), // Forward jog
                            "jog_reverse" => ("1000", "0004"), // Reverse jog
                            "free_stop" => ("1000", "0005"),   // Free stop
                            "decel_stop" => ("1000", "0006"),  // Deceleration stop
                            "reset" => ("1000", "0007"),       // Fault reset
                            "frequency" => ("3000", value),     // Set frequency (0-10000)
                            _ => ("1000", "0005")              // Default to free stop
                        };
                        
                        // Send M100 write command
                        string mCommand = $"M100 {slaveAddress} {register} {controlValue}";
                        var ok = _io.WriteLineReadLine(1, mCommand, out response);
                        response = ok == KOGNA_OK ? $"Servo {slaveAddress} {action}: {response}" : "Servo control command failed";
                    }
                    else
                    {
                        response = "Error: Servo control command requires address, action, and value (usage: servocontrol <address> <action> <value>)";
                    }
                }
                catch (Exception ex)
                {
                    response = $"Error controlling servo: {ex.Message}";
                }
                return (response, response);
            }

            // UART Configuration
            if (cmd == "uartconfig")
            {
                Console.WriteLine($"UART config command called with payload: {payload}");
                try
                {
                    var configParts = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (configParts.Length >= 3)
                    {
                        var uartType = configParts[0]; // "rs485" or "rs232"
                        var baudRate = configParts[1]; // Baud rate
                        var port = configParts[2];     // Port number
                        
                        // Send M102 command to configure UART
                        string mCommand = $"M102 {uartType} {baudRate} {port}";
                        var ok = _io.WriteLineReadLine(1, mCommand, out response);
                        response = ok == KOGNA_OK ? $"UART {uartType} configured: {response}" : "UART config failed";
                    }
                    else
                    {
                        response = "Error: UART config requires type, baud rate, and port (usage: uartconfig <rs485|rs232> <baudrate> <port>)";
                    }
                }
                catch (Exception ex)
                {
                    response = $"Error configuring UART: {ex.Message}";
                }
                return (response, response);
            }

            // Handle Kogna-specific commands
            else if (cmd == "setpersist")
            {
                Console.WriteLine($"SetPersist command called with payload: {payload}");
                try
                {
                    var persistParts = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (persistParts.Length >= 2)
                    {
                        var variable = persistParts[0]; // e.g., "UserData[0]"
                        var value = persistParts[1];    // value to set
                        
                        var command = $"SetPersist {variable} {value}";
                        var ok = _io.WriteLineReadLine(1, command, out response);
                        response = ok == KOGNA_OK ? "SETPERSIST" : "SetPersist failed";
                    }
                    else
                    {
                        response = "Error: SetPersist requires variable and value";
                    }
                }
                catch (Exception ex)
                {
                    response = $"Error in SetPersist: {ex.Message}";
                }
                return (response, response);
            }
            else if (cmd == "setpersistdec")
            {
                Console.WriteLine($"SetPersistDec command called with payload: {payload}");
                try
                {
                    var persistParts = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (persistParts.Length >= 2)
                    {
                        var index = persistParts[0]; // e.g., "10"
                        var value = persistParts[1];    // decimal value to set
                        
                        var command = $"SetPersistDec {index} {value}";
                        var ok = _io.WriteLineReadLine(1, command, out response);
                        response = ok == KOGNA_OK ? "SETPERSISTDEC" : "SetPersistDec failed";
                    }
                    else
                    {
                        response = "Error: SetPersistDec requires index and decimal value";
                    }
                }
                catch (Exception ex)
                {
                    response = $"Error in SetPersistDec: {ex.Message}";
                }
                return (response, response);
            }
            else if (cmd == "getpersist")
            {
                Console.WriteLine($"GetPersist command called with payload: {payload}");
                try
                {
                    var persistParts = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (persistParts.Length >= 1)
                    {
                        var variable = persistParts[0]; // e.g., "UserData[10]"
                        
                        var command = $"GetPersist {variable}";
                        var ok = _io.WriteLineReadLine(1, command, out response);
                        if (ok == KOGNA_OK && !string.IsNullOrEmpty(response))
                        {
                            // Try to parse the response as a number
                            if (int.TryParse(response, out var result))
                            {
                                response = result.ToString();
                            }
                            else
                            {
                                response = "0"; // Default to 0 if not a number
                            }
                        }
                        else
                        {
                            response = "0"; // Default to 0 on error
                        }
                    }
                    else
                    {
                        response = "Error: GetPersist requires variable name";
                    }
                }
                catch (Exception ex)
                {
                    response = $"Error in GetPersist: {ex.Message}";
                }
                return (response, response);
            }
            else if (cmd == "getpersistdec")
            {
                Console.WriteLine($"GetPersistDec command called with payload: {payload}");
                try
                {
                    var persistParts = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (persistParts.Length >= 1)
                    {
                        var index = persistParts[0]; // e.g., "10"
                        
                        var command = $"GetPersistDec {index}";
                        var ok = _io.WriteLineReadLine(1, command, out response);
                        if (ok == KOGNA_OK && !string.IsNullOrEmpty(response))
                        {
                            // Return the decimal value directly
                            response = response.Trim();
                        }
                        else
                        {
                            response = "0"; // Default to 0 on error
                        }
                    }
                    else
                    {
                        response = "Error: GetPersistDec requires index";
                    }
                }
                catch (Exception ex)
                {
                    response = $"Error in GetPersistDec: {ex.Message}";
                }
                return (response, response);
            }
            else if (cmd == "getpersisthex")
            {
                Console.WriteLine($"GetPersistHex command called with payload: {payload}");
                try
                {
                    var persistParts = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (persistParts.Length >= 1)
                    {
                        var variable = persistParts[0]; // e.g., "UserData[10]"
                        
                        var command = $"GetPersistHex {variable}";
                        var ok = _io.WriteLineReadLine(1, command, out response);
                        if (ok == KOGNA_OK && !string.IsNullOrEmpty(response))
                        {
                            // Return the hex value directly
                            response = response.Trim();
                        }
                        else
                        {
                            response = "0"; // Default to 0 on error
                        }
                    }
                    else
                    {
                        response = "Error: GetPersistHex requires variable name";
                    }
                }
                catch (Exception ex)
                {
                    response = $"Error in GetPersistHex: {ex.Message}";
                }
                return (response, response);
            }
            else if (cmd == "execthread")
            {
                Console.WriteLine($"ExecThread command called with payload: {payload}");
                try
                {
                    var threadParts = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (threadParts.Length >= 1 && int.TryParse(threadParts[0], out var threadNum))
                    {
                        var command = $"ExecThread {threadNum}";
                        var ok = _io.WriteLineReadLine(1, command, out response);
                        response = ok == KOGNA_OK ? "EXECTHREAD" : "ExecThread failed";
                    }
                    else
                    {
                        response = "Error: ExecThread requires thread number";
                    }
                }
                catch (Exception ex)
                {
                    response = $"Error in ExecThread: {ex.Message}";
                }
                return (response, response);
            }
            else if (cmd == "serviceconsole")
            {
                Console.WriteLine($"ServiceConsole command called");
                try
                {
                    var ok = _io.ServiceConsole();
                    response = ok == KOGNA_OK ? "Console serviced - check terminal for output" : "ServiceConsole failed";
                }
                catch (Exception ex)
                {
                    response = $"Error in ServiceConsole: {ex.Message}";
                }
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
            string response = $"Engine Error: {ex.Message}";
            return (response, response);
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
                bool hasMotion = false;

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
                                    hasMotion = true;
                                    break;
                                case "1":
                                    command.Type = MotionType.Linear;
                                    hasMotion = true;
                                    break;
                                case "2":
                                    command.Type = MotionType.Arc;
                                    command.IsClockwise = true;
                                    hasMotion = true;
                                    break;
                                case "3":
                                    command.Type = MotionType.Arc;
                                    command.IsClockwise = false;
                                    hasMotion = true;
                                    break;
                                case "4":
                                    command.Type = MotionType.Dwell;
                                    if (double.TryParse(parts.FirstOrDefault(p => p.StartsWith("P"))?.Substring(1), out var dwellTime))
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
                        case "Y":
                        case "Z":
                        case "A":
                        case "B":
                        case "C":
                            var axisIndex = "XYZABC".IndexOf(code[0]);
                            if (axisIndex >= 0 && axisIndex < _coord.AxisCount)
                            {
                                if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var axisValue))
                                {
                                    // Validate against soft limits if enabled
                                    if (_engine.Configuration.EnableSoftLimits)
                                    {
                                        var limit = axisValue >= 0 ? 
                                            _engine.Configuration.SoftLimitsPositive[axisIndex] : 
                                            _engine.Configuration.SoftLimitsNegative[axisIndex];
                                            
                                        if (Math.Abs(axisValue) > Math.Abs(limit))
                                        {
                                            throw new ArgumentException($"Axis {code} value {axisValue} exceeds soft limit of {limit}");
                                        }
                                    }
                                    command.EndPosition[axisIndex] = axisValue;
                                    axisSet[axisIndex] = true;
                                    hasMotion = true;
                                }
                                else
                                {
                                    throw new ArgumentException($"Invalid value for axis {code}: {value}");
                                }
                            }
                            break;
                        case "F":
                            if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var f))
                            {
                                if (f <= 0)
                                {
                                    throw new ArgumentException($"Feed rate must be positive: {f}");
                                }
                                command.FeedRate = f;
                                _lastFeedRate = f;
                            }
                            else
                            {
                                throw new ArgumentException($"Invalid feed rate value: {value}");
                            }
                            break;
                        case "I":
                        case "J":
                            var arcIndex = "IJ".IndexOf(code[0]);
                            if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var arcValue))
                            {
                                command.ArcCenter[arcIndex] = arcValue;
                            }
                            else
                            {
                                throw new ArgumentException($"Invalid arc center value for {code}: {value}");
                            }
                            break;
                        case "M":
                            // Handle M-codes for hardware control
                            switch (value)
                            {
                                case "42":
                                    // M42 - Set Pin State (PWM control)
                                    var pin = parts.FirstOrDefault(p => p.StartsWith("P"))?.Substring(1);
                                    var state = parts.FirstOrDefault(p => p.StartsWith("S"))?.Substring(1);
                                    var mode = parts.FirstOrDefault(p => p.StartsWith("T"))?.Substring(1);
                                    
                                    if (pin != null && state != null)
                                    {
                                        // Send M42 command directly to hardware
                                        var m42Command = $"M42 P{pin} S{state}";
                                        if (mode != null) m42Command += $" T{mode}";
                                        
                                        _io.WriteLineReadLine(1, m42Command, out var response);
                                        Console.WriteLine($"[GCODE_PARSER] M42 command sent: {m42Command}, response: {response}");
                                        return null; // M42 doesn't create motion commands
                                    }
                                    break;
                                case "3":
                                    // M3 - Spindle CW / Laser On
                                    _io.WriteLineReadLine(1, "M3", out var m3Response);
                                    Console.WriteLine($"[GCODE_PARSER] M3 command sent, response: {m3Response}");
                                    return null;
                                case "4":
                                    // M4 - Spindle CCW / Laser On  
                                    _io.WriteLineReadLine(1, "M4", out var m4Response);
                                    Console.WriteLine($"[GCODE_PARSER] M4 command sent, response: {m4Response}");
                                    return null;
                                case "5":
                                    // M5 - Spindle / Laser Off
                                    _io.WriteLineReadLine(1, "M5", out var m5Response);
                                    Console.WriteLine($"[GCODE_PARSER] M5 command sent, response: {m5Response}");
                                    return null;
                            }
                            break;
                    }
                }

                // Validate motion commands
                if (hasMotion)
                {
                    // Fill in missing axes with current position
                    int axisCount = _coord.AxisCount;
                    for (int i = 0; i < axisCount; i++)
                    {
                        if (!axisSet[i] || double.IsNaN(command.EndPosition[i]))
                        {
                            command.EndPosition[i] = _coord.GetPosition(i);
                        }
                    }

                    // For arc moves, validate that I,J values are provided
                    if (command.Type == MotionType.Arc)
                    {
                        if (double.IsNaN(command.ArcCenter[0]) || double.IsNaN(command.ArcCenter[1]))
                        {
                            throw new ArgumentException("Arc center (I,J) values must be provided for arc moves");
                        }
                    }

                    return command;
                }

                // No motion in this command
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GCODE_PARSER] Error parsing G-code: {ex.Message}");
                throw; // Rethrow to be handled by caller
            }
        }



}
   