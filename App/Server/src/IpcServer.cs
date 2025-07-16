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



namespace AppServer;

public class IpcServer
{
    public readonly TcpListener _listener;
    public readonly TCPServer.KognaIO? _io;
    public KognaControl _control;
    public bool _isConnected { get; set; }
    public string? result;

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

    private async Task AcceptLoopAsync()
    {
        while (true)
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
                var raw = await reader.ReadLineAsync();
                if (raw is null)
                    break;
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                var req = JsonConvert.DeserializeObject<TCPServer.IpcRequest>(raw);
                if (req == null || req?.Command == null)
                {
                    Console.WriteLine("bad json");
                    // either JSON bad or missing command field
                    break;
                }
                if (req.Command.Equals("isconnected", StringComparison.OrdinalIgnoreCase))
                    {
                        // optionally still ACK at writer
                        await writer.WriteLineAsync("{\"Status\":\"OK\"}");
                        continue;
                    }

                var rawArgs = req.Args ?? Array.Empty<string>();
                var intArgs = rawArgs .Select(s => int.TryParse(s, out var i) ? i : 0) .ToArray();
                var singleArg = intArgs.Length > 0 ? intArgs[0] : 0;

                
                    var cmdLine = new[]{ req.Command }
                   .Concat(req.Args ?? Enumerable.Empty<string>());
                    var cmd = string.Join(" ", cmdLine);
                    var (response, result) = await _control.ProcessIpcCommand(cmd);
   
                var resp = new IpcResponse
                {
                    Status = "OK",
                    Result = result
                };
                await writer.WriteLineAsync(JsonConvert.SerializeObject(resp));



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