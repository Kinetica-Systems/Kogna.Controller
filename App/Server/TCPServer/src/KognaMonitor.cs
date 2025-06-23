



using System;
using System.Collections.Concurrent;

using System.Text;
using System.Threading;
using System.Threading.Tasks;

using System.Globalization;  // at top of file
using System.Linq;

using System.Security.Cryptography.X509Certificates;

namespace TCPServer
{
    /// <summary>
    /// the background process that grabs all the information inbetween commands
    /// </summary>


    public class KognaMonitor
    {
        private KognaIO _io;
        private readonly KognaMotion _coord;
        public event Action<KognaStatus>? OnStatusUpdate;
        const double degPerCount = 360.0 / 2000.0;  // 0.18° per pulse


        public KognaMonitor(KognaIO io, KognaMotion coord)
        {
            _io = io;
            _coord = coord;
        }


        public async Task StartAsyncMonitor(CancellationToken ct)
        {
            const int axisCount = 6;
            _coord.GetAxisDefinitions();
            Console.WriteLine("KognaMonitor Started");

            while (!ct.IsCancellationRequested)
            {
                _io.ServiceConsole();    // pick up any console‐print lines

                // 1) Batch‐read raw counts into arrays
                var rawActCounts = new double[axisCount];
                var rawTgtCounts = new double[axisCount];
                for (int i = 0; i < axisCount; i++)
                {
                    try
                    {
                        rawActCounts[i] = _coord.GetPosition(i);
                        rawTgtCounts[i] = _coord.GetDestination(i);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[DRO] Axis {i} poll failed: {ex.Message}");
                        rawActCounts[i] = 0;
                        rawTgtCounts[i] = 0;
                    }
                }

                // 2) Convert to degrees and populate status
                var status = new KognaStatus();
                for (int i = 0; i < axisCount; i++)
                {
                    status.JointsActual[i] = rawActCounts[i] * degPerCount;
                    status.JointsTarget[i] = rawTgtCounts[i] * degPerCount;
                    status.JointsEnabled[i] = true;
                }

                // 3) Fire the update event
                OnStatusUpdate?.Invoke(status);

                await Task.Delay(500, ct);
            }
        }
    }
        public class KognaStatus
        {
            public double[] JointsActual { get; set; } = new double[6];
            public double[] JointsTarget { get; set; } = new double[6];
            public bool[] JointsEnabled { get; set; } = new bool[6];
        }
}
