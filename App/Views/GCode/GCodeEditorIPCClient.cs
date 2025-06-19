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
        private readonly TcpClient _tcp;
        //private readonly NetworkStream _stream;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly StreamReader _reader;
        private readonly StreamWriter   _writer;

        public KinematicEngineClient(string host, int port)
        {
            _tcp = new TcpClient("localhost", 5001);
          //  _tcp.Connect(host, port);
           var _stream = _tcp.GetStream();
            _reader = new StreamReader(_stream, Encoding.ASCII, leaveOpen: true);
            _writer = new StreamWriter(_stream, Encoding.ASCII, leaveOpen: true)
            {
                AutoFlush = true
            };
        }

        /// <summary>
        /// Sends one G-code line (with trailing newline) and reads one response line back.
        /// </summary>
        public async Task<IpcResponse?> SendCommandAsync(string commandLine)
        {
            
            await _sendLock.WaitAsync();
            try
            {
                // Write the command plus newline
                await _writer.WriteLineAsync(commandLine);

                // Read one response line (up to the newline)
                var json = await _reader.ReadLineAsync();
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
            _reader?.Dispose();
            _writer?.Dispose();
            _tcp?.Close();
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
