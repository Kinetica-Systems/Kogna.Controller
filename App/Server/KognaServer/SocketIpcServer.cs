using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace KognaServer.Server.KognaServer;

public class SocketIpcServer
{
    private readonly KognaServerMain _innerServer;
    private readonly TcpListener _listener;

    public SocketIpcServer(KognaServerMain innerServer, int port = 5000)
    {
        _innerServer = innerServer;
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

                var req = JsonConvert.DeserializeObject<IpcRequest>(raw);
                if (req == null || req.Command == null)
                {
                    // either JSON bad or missing command field
                    continue;
                }

                var cmdText = req.Command.Trim().ToLowerInvariant();
                string result;

                switch (cmdText)
                {
                    case "isconnected":
                        result = _innerServer.IsConnected.ToString();
                        break;

                    default:    
                    var rawArgs = req.Args ?? Array.Empty<string>();
                    var intArgs = rawArgs .Select(s => int.TryParse(s, out var i) ? i : 0) .ToArray();

                    // pick the first (or default to 0)
                    var singleArg = intArgs.Length > 0 ? intArgs[0] : 0;

                    result = _innerServer.SendCommand(req.Command, singleArg);


                    break;
                }


                var resp = new IpcResponse
                {
                    Status = "OK",
                    Result = result
                };
                await writer.WriteLineAsync(JsonConvert.SerializeObject(resp));


            }
        }
    }
    

    // Simple DTOs:
    private class IpcRequest
    {
        public string Command { get; set; } = null!;
        public string[] Args { get; set; } = null!;
    }

    private class IpcResponse
    {
        public string Status { get; set; } = null!;
        public string Result { get; set; } = null!;
        public string Error { get; set; } = null!;
    }
}
