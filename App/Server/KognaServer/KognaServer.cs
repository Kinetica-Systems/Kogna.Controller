using System;
using System.Collections.Concurrent;

using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;  // at top of file
using System.Linq;
using KognaServer.Server;
using KognaServer.ViewModels;
using System.Security.Cryptography.X509Certificates;
using Avalonia.Controls.Documents;

namespace KognaServer.Server.KognaServer
{
    /// <summary>
    /// Named‐pipe server that dispatches client requests into the in‐process KognaIO.
    /// </summary>


    public class KognaStatus
        {
            public double[] JointsActual  { get; set; } = new double[6];
            public double[] JointsTarget  { get; set; } = new double[6];
            public bool[]   JointsEnabled { get; set; } = new bool[6];
        }


    public class KognaServerMain : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        public readonly KognaIO _io = null!;
        public KognaMonitor monitor;

        public event Action<KognaStatus>? OnStatusUpdate;
        //public event Action<string>? ConsoleOutput;
       // private bool _isConnected;
        public bool IsConnected => _io != null && _io.Connected;   // or however you check “up”  
        
        public (double X, double Y, double Z, double A, double B, double C)ComputeTcp(double[] jointsActual)
        {
            return (0, 0, 0, 0, 0, 0);
        }







        public KognaServerMain(string ipAddress, int port)
        {

            _io = new KognaIO(ipAddress, port);
            monitor = new KognaMonitor(_io);

            

        }

        /// <summary>Start listening for pipe clients.</summary>
        public void Start()
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] KognaServerHost.Start() called");
            // ** establish TCP link to the Kogna device **
            int connResult = _io.Connect();
            if (connResult != KognaIO.KOGNA_OK)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ERROR: Could not connect to Kogna at {_io.USBLocation()}. " +
                                    $"Code={connResult}, ErrMsg={_io.ErrMsg}");
                // optionally: throw new InvalidOperationException or retry logic here
            }
            else
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Connected to Kogna at {_io.USBLocation()}");
                monitor.OnStatusUpdate += s => OnStatusUpdate?.Invoke(s);
                _ = monitor.StartAsync(_cts.Token);
            }

           
        }

        
        public string SendCommand(string cmd, int board)
        {
            // you may need to tweak the board-ID; 1 is common on KMotion setups
            var ok = _io.WriteLineReadLine(board, cmd, out var resp);
            return resp;
        }


       
       



        public void Dispose()
        {
            _cts.Cancel();
            _io.Dispose();

        }


    }
}
