



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
using CommunityToolkit.Mvvm.DependencyInjection;

namespace KognaServer.Server.KognaServer
{
    /// <summary>
    /// the background process that grabs all the information inbetween commands
    /// </summary>


    public class KognaMonitor
    {
        private KognaIO _io;
        public event Action<KognaStatus>? OnStatusUpdate;
        private readonly KognaMotion _coord;

        public KognaMonitor(KognaIO io)
        {
            _io = io;
            _coord = new KognaMotion(io);

        }

        const double degPerCount = 360.0 / 2000.0;  // 0.18° per pulse

    public async Task StartAsync(CancellationToken ct)
    {
        const int axisCount = 6;
        _coord.GetAxisDefinitions();

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
                status.JointsActual[i]  = rawActCounts[i] * degPerCount;
                status.JointsTarget[i]  = rawTgtCounts[i] * degPerCount;
                status.JointsEnabled[i] = true;
            }

            // 3) Fire the update event
            OnStatusUpdate?.Invoke(status);

            await Task.Delay(500, ct);
        }
    }




    }
}
