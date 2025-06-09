



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
            _coord.GetAxisDefinitions();

            while (!ct.IsCancellationRequested)
            {
                _io.ServiceConsole();    // pick up any console‐print lines
                Console.WriteLine("[DRO] Loop tick");


                var status = new KognaStatus();
                            // poll each logical axis (0=X,1=Y,…5=C)
                            for (int i = 0; i < 6; i++)
                            {
                                try
                                {
                                
                               
                        // raw counts
                                    var rawAct = _coord.GetPosition(i);
                                    var rawTgt = _coord.GetDestination(i);
                                    Console.WriteLine($"[DRO] Axis {i}: rawAct={rawAct}, rawTgt={rawTgt}");
                                    // convert to degrees
                                    status.JointsActual[i] = rawAct * degPerCount;
                                    status.JointsTarget[i] = rawTgt * degPerCount;
                                    status.JointsEnabled[i] = true;
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[DRO] Axis {i} poll failed: {ex.Message}");
                                }
                            }
                            
                            // fire update
                            OnStatusUpdate?.Invoke(status);


                await Task.Delay(50, ct);
            }
        }



    }
}
