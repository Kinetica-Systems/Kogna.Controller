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
        private readonly TcpClient _ipcClient;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly StreamReader _ipcReader;
        private readonly StreamWriter   _ipcWriter;
        public KinematicEngineClient(string host, int port)
        
        {
            try
            {
                _ipcClient = new TcpClient("localhost", 5000);
                var stream = _ipcClient.GetStream();
                _ipcReader = new StreamReader(stream, Encoding.UTF8);
                _ipcWriter = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                Console.WriteLine("🔌 IPC socket connected to 127.0.0.1:5000\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($" IPC connection failed: {ex.Message}\n");

                // fall back to harmless, never-null stubs
                _ipcClient = new TcpClient();                  
                _ipcReader = new StreamReader(Stream.Null);        
                _ipcWriter = new StreamWriter(Stream.Null){AutoFlush=true};
        
            }



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
                await _ipcWriter.WriteLineAsync(commandLine);

                // Read one response line (up to the newline)
                var json = await _ipcReader.ReadLineAsync();
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
