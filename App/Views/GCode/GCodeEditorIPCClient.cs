using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;


namespace KognaServer.Views
{
    /// <summary>
    /// A simple thread-safe TCP client for your KinematicEngineServer.
    /// </summary>
    public class KinematicEngineClient : IDisposable
    {
        private TcpClient? _ipcClient;
        private StreamReader? _ipcReader;
        private StreamWriter? _ipcWriter;
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        private readonly string _host;
        private readonly int _port;

        public KinematicEngineClient(string host, int port)
        {
            _host = host;
            _port = port;
        }

        private async Task EnsureConnectedAsync()
        {
            if (_ipcClient?.Connected == true) return;

            const int maxAttempts = 5;
            var delay = TimeSpan.FromMilliseconds(500);

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    _ipcClient?.Dispose();
                    _ipcClient = new TcpClient();
                    await _ipcClient.ConnectAsync(_host, _port);

                    var stream = _ipcClient.GetStream();
                    _ipcReader = new StreamReader(stream, Encoding.UTF8);
                    _ipcWriter = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                    Console.WriteLine($"🔌 IPC connected on attempt {attempt}");
                    return;
                }
                catch (SocketException) when (attempt < maxAttempts)
                {
                    await Task.Delay(delay);
                    delay += delay; // simple back-off
                }
            }

            throw new InvalidOperationException($"Unable to connect to IPC server at {_host}:{_port}");
        }

        /// <summary>
        /// Sends one G-code line (with trailing newline) and reads one response line back.
        /// </summary>
        public async Task<IpcResponse?> SendCommandAsync(string commandLine)
        {
            
            await EnsureConnectedAsync();
            await _sendLock.WaitAsync();
            try
            {
                await _ipcWriter!.WriteLineAsync(commandLine);
                var json = await _ipcReader!.ReadLineAsync();
                //Console.WriteLine($"[RAW JSON]  {json}");

                // if the connection closed or no data, return null
                if (string.IsNullOrEmpty(json))
                return null;

        // otherwise parse
                return JsonConvert.DeserializeObject<IpcResponse>(json);
            }
            finally
            {
                _sendLock.Release();

            }
        }

        public void Dispose()
        {
            _ipcReader?.Dispose();
            _ipcWriter?.Dispose();
            _ipcClient?.Close();
            _sendLock?.Dispose();
        }
    }
     public class IpcResponse
    {
        public string Status  { get; set; } = "";
        public string Result  { get; set; } = "";
        public Segment[] Segments { get; set; } = Array.Empty<Segment>();
        public string? Error  { get; set; }
    }

    public class Segment
    {
        public double[] JointAngles { get; set; } = Array.Empty<double>();
        public double DurationMs { get; set; }
        public double Seq       { get; set; }
        public double Id { get; set; }        
        public double[] Actuators { get; set; } = Array.Empty<double>();
        public bool IsRapid  {get; set;}
        public double FeedRate  { get; set; }

    }
}
