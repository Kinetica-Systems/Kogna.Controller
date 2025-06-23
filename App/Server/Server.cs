using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

using System.Net;
using System.Net.Sockets;
using AppServer;
using TCPServer;
using KinematicEngine;
using System.ComponentModel;

namespace KognaComms;

public class KognaControl
{
    public KognaMonitor _monitor { get; set; }
    public KognaMotion _coord { get; set; }
    public KEngine _engine { get; set; }
    public IpcServer _ipcServer { get; set; }
    public KServer _tcpServer { get; set; }
    public KognaIO _io { get; set; }
    public KognaControl _control { get; set; }
    private readonly Dictionary<string, Func<string[], string>> _commands;
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
        _engine = new KEngine();

    }
    public bool Start()
    {
        _tcpServer.Start(); //TCP server starts the monitor heartbeat
        _ipcServer.Start();
        _engine.Start();
        return true;
    }
    

    public string ProcessIpcCommand(string commandLine, out string response) //take the string, figure out where its meant to be directed to and send it there.
    {
        Console.WriteLine("hit entry");

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
            response = string.Empty;
            // 1) setcs
            if (cmd == "setcs")
            {
                Console.WriteLine($"setcs called");
                response = "Not implemented yet";
                return response;
            }
            if (cmd == "getcs")
            {
                Console.WriteLine($"getcs called");
                response = "Not implemented yet";
                return response;
            }
            if (cmd == "gcode")
            {
                Console.WriteLine($"GCode called");
                response = "Not implemented yet";
                return response;

            }
            if (cmd == "version")
            {
                Console.WriteLine($"Version called");
                var ok = _io.WriteLineReadLine(0, $"Version", out response);
                Console.WriteLine($"Version: {response}");
                return response;

            }

            else
            {
                Console.WriteLine($"other command called");
                var ok = _io.WriteLineReadLine(0, $"{cmd}", out response);
                Console.WriteLine($"resp: {response}");
                return response;
            }
        }
        catch (Exception ex)
        {

            // log the full exception to console
            Console.WriteLine($"[ENGINE ERROR] {ex}");
            response = "Engine Error {ex}";
            return response;
            // return full message+stack and an empty segments array (never null)


        }
        return response;
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



    public string SendGCodeLine(string cmd, int board)
    {
        _engine.ProcessCommand(cmd);
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
}
   