using System;
using System.IO;
using System.Net;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Security.Cryptography.X509Certificates;
using KognaComms;
using System.Threading;


namespace AppServer;

public class IpcServer
{
    public readonly TcpListener _listener;
    public readonly TCPServer.KognaIO? _io;
    public KognaControl _control;
    public bool _isConnected { get; set; }
    public string? result;
    private CancellationTokenSource _cts = new CancellationTokenSource();

    public IpcServer(int port, KognaControl control)
    {
        _control = control;
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    public void Start()
    {
        _listener.Start();
        Console.WriteLine($"IPC Server listening on port {_listener.LocalEndpoint}");
        _ = AcceptLoopAsync();
    }

    public void Stop()
    {
        try
        {
            _cts.Cancel();
            _listener.Stop();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[IPC_SERVER] Error during stop: {ex.Message}");
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync();
            _ = HandleClientAsync(client);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
        {
            while (true)
            {
                try
                {
                    var raw = await reader.ReadLineAsync();
                    if (raw is null)
                        break;
                    if (string.IsNullOrWhiteSpace(raw))
                        continue;

                    Console.WriteLine($"[IPC_SERVER] Received request: {raw}");
                    
                    TCPServer.IpcRequest? req;
                    try
                    {
                        req = JsonConvert.DeserializeObject<TCPServer.IpcRequest>(raw);
                    }
                    catch (JsonException ex)
                    {
                        Console.WriteLine($"[IPC_SERVER] JSON parsing error: {ex.Message}");
                        await writer.WriteLineAsync(JsonConvert.SerializeObject(new IpcResponse
                        {
                            Status = "ERROR",
                            Error = "Invalid JSON format",
                            Result = string.Empty
                        }));
                        continue;
                    }

                    if (req == null || req?.Command == null)
                    {
                        Console.WriteLine("[IPC_SERVER] Invalid request format");
                        await writer.WriteLineAsync(JsonConvert.SerializeObject(new IpcResponse
                        {
                            Status = "ERROR",
                            Error = "Invalid request format",
                            Result = string.Empty
                        }));
                        continue;
                    }

                    if (req.Command.Equals("isconnected", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync(JsonConvert.SerializeObject(new IpcResponse
                        {
                            Status = "OK",
                            Result = "true",
                            Error = string.Empty
                        }));
                        continue;
                    }

                    var rawArgs = req.Args ?? Array.Empty<string>();
                    var intArgs = rawArgs.Select(s => int.TryParse(s, out var i) ? i : 0).ToArray();
                    var singleArg = intArgs.Length > 0 ? intArgs[0] : 0;

                    var cmdLine = new[] { req.Command }
                        .Concat(req.Args ?? Enumerable.Empty<string>());
                    var cmd = string.Join(" ", cmdLine);
                    
                    Console.WriteLine($"[IPC_SERVER] Processing command: {cmd}");
                    var (response, result) = await _control.ProcessIpcCommand(cmd);

                    var resp = new IpcResponse
                    {
                        Status = string.IsNullOrEmpty(response) ? "ERROR" : "OK",
                        Result = result ?? string.Empty,
                        Error = string.IsNullOrEmpty(response) ? "Command failed" : string.Empty
                    };
                    
                    await writer.WriteLineAsync(JsonConvert.SerializeObject(resp));
                    Console.WriteLine($"[IPC_SERVER] Sent response: {JsonConvert.SerializeObject(resp)}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[IPC_SERVER] Error handling client: {ex.Message}");
                    try
                    {
                        await writer.WriteLineAsync(JsonConvert.SerializeObject(new IpcResponse
                        {
                            Status = "ERROR",
                            Error = $"Internal server error: {ex.Message}",
                            Result = string.Empty
                        }));
                    }
                    catch
                    {
                        // If we can't write the error response, just break the connection
                        break;
                    }
                }
            }
        }
    }
}
    public class IpcRequest
    {
        public string Command { get; set; } = null!;
        public string[] Args { get; set; } = null!;
    }

    public class IpcResponse
    {
        public string Status { get; set; } = null!;
        public string Result { get; set; } = null!;
        public string Error { get; set; } = null!;
    }