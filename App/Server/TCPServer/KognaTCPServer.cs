using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TCPServer;

public class KServer
{
    private readonly string _ipAddress;
    private readonly int _port;
    private readonly KognaMonitor _monitor;
    private readonly KognaMotion _coord;
    private readonly IKognaIO _io;
    private bool _isRunning;
    private TcpListener? _listener;
    private readonly CancellationTokenSource _cancellationTokenSource;

    public KServer(string ipAddress, int port, KognaMonitor monitor, KognaMotion coord, IKognaIO io)
    {
        _ipAddress = ipAddress;
        _port = port;
        _monitor = monitor;
        _coord = coord;
        _io = io;
        _cancellationTokenSource = new CancellationTokenSource();
    }

    public bool Start()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Parse(_ipAddress), _port);
            _listener.Start();
            _isRunning = true;

            // Start accepting clients in the background
            Task.Run(AcceptClientsAsync, _cancellationTokenSource.Token);

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TCP_SERVER] Failed to start server: {ex.Message}");
            return false;
        }
    }

    public void Stop()
    {
        _isRunning = false;
        _cancellationTokenSource.Cancel();
        _listener?.Stop();
    }

    private async Task AcceptClientsAsync()
    {
        try
        {
            while (_isRunning && !_cancellationTokenSource.Token.IsCancellationRequested)
            {
                if (_listener == null) break;

                var client = await _listener.AcceptTcpClientAsync();
                _ = HandleClientAsync(client);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine($"[TCP_SERVER] Error accepting clients: {ex.Message}");
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            using var stream = client.GetStream();
            var buffer = new byte[1024];

            while (_isRunning && !_cancellationTokenSource.Token.IsCancellationRequested)
            {
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                var message = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                var response = ProcessMessage(message);

                var responseBytes = Encoding.ASCII.GetBytes(response);
                await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TCP_SERVER] Error handling client: {ex.Message}");
        }
        finally
        {
            client.Dispose();
        }
    }

    private string ProcessMessage(string message)
    {
        try
        {
            // Process the message and return appropriate response
            return "OK";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
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