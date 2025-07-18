



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
        private readonly IKognaIO _io;
        private readonly KognaMotion _coord;
        public event Action<KognaStatus>? OnStatusUpdate;
        const double degPerCount = 360.0 / 2000.0;  // 0.18° per pulse


        public KognaMonitor(IKognaIO io, KognaMotion coord)
        {
            _io = io;
            _coord = coord;
        }


        public async Task StartAsyncMonitor(CancellationToken ct)
        {
            int axisCount = 6; // Default, will be updated after GetAxisDefinitions
            
            try
            {
                Console.WriteLine("[KOGNA_MONITOR] Getting axis definitions...");
                _coord.GetAxisDefinitions();
                axisCount = _coord.AxisCount;
                Console.WriteLine($"[KOGNA_MONITOR] Axis definitions retrieved successfully, axisCount={axisCount}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[KOGNA_MONITOR] ERROR: Failed to get axis definitions: {ex.Message}");
                Console.WriteLine($"[KOGNA_MONITOR] Will continue with default axis mapping");
            }
            
            Console.WriteLine("KognaMonitor Started");

            while (!ct.IsCancellationRequested)
            {
                try
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
                            
                            // Debug output for position values
                            if (i == 0) // Only log first axis to avoid spam
                            {
                                Console.WriteLine($"[KOGNA_MONITOR] Raw positions - Actual[{i}]: {rawActCounts[i]}, Target[{i}]: {rawTgtCounts[i]}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[DRO] Axis {i} poll failed: {ex.Message}");
                            rawActCounts[i] = 0;
                            rawTgtCounts[i] = 0;
                        }
                    }

                    // 2) Convert to degrees and populate status
                    var status = new KognaStatus(axisCount);
                    for (int i = 0; i < axisCount; i++)
                    {
                        status.JointsActual[i] = rawActCounts[i] * degPerCount;
                        status.JointsTarget[i] = rawTgtCounts[i] * degPerCount;
                        status.JointsEnabled[i] = true;
                    }

                    // 3) Calculate cartesian positions (if kinematics available)
                    // For now, we'll use the joint positions as cartesian positions
                    // In a real implementation, you'd use forward kinematics
                    if (axisCount > 0) status.CurrentX = status.JointsActual[0];
                    if (axisCount > 1) status.CurrentY = status.JointsActual[1];
                    if (axisCount > 2) status.CurrentZ = status.JointsActual[2];
                    if (axisCount > 3) status.CurrentA = status.JointsActual[3];
                    if (axisCount > 4) status.CurrentB = status.JointsActual[4];
                    if (axisCount > 5) status.CurrentC = status.JointsActual[5];
                    if (axisCount > 6) status.CurrentU = status.JointsActual[6];
                    if (axisCount > 7) status.CurrentV = status.JointsActual[7];

                    if (axisCount > 0) status.TargetX = status.JointsTarget[0];
                    if (axisCount > 1) status.TargetY = status.JointsTarget[1];
                    if (axisCount > 2) status.TargetZ = status.JointsTarget[2];
                    if (axisCount > 3) status.TargetA = status.JointsTarget[3];
                    if (axisCount > 4) status.TargetB = status.JointsTarget[4];
                    if (axisCount > 5) status.TargetC = status.JointsTarget[5];
                    if (axisCount > 6) status.TargetU = status.JointsTarget[6];
                    if (axisCount > 7) status.TargetV = status.JointsTarget[7];

                    // 4) Set joint angles (same as actual positions for now)
                    if (axisCount > 0) status.JointAngle1 = status.JointsActual[0];
                    if (axisCount > 1) status.JointAngle2 = status.JointsActual[1];
                    if (axisCount > 2) status.JointAngle3 = status.JointsActual[2];
                    if (axisCount > 3) status.JointAngle4 = status.JointsActual[3];
                    if (axisCount > 4) status.JointAngle5 = status.JointsActual[4];
                    if (axisCount > 5) status.JointAngle6 = status.JointsActual[5];
                    if (axisCount > 6) status.JointAngle7 = status.JointsActual[6];
                    if (axisCount > 7) status.JointAngle8 = status.JointsActual[7];

                    // 5) Fire the update event
                    OnStatusUpdate?.Invoke(status);

                    Console.WriteLine($"[DRO] Updated positions - Current: ({status.CurrentX:F3}, {status.CurrentY:F3}, {status.CurrentZ:F3}) Target: ({status.TargetX:F3}, {status.TargetY:F3}, {status.TargetZ:F3})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[KOGNA_MONITOR] ERROR in monitor loop: {ex.Message}");
                    // Don't break the loop, just continue
                }

                await Task.Delay(500, ct);
            }
        }

        public class KognaStatus
        {
            public double[] JointsActual { get; set; }
            public double[] JointsTarget { get; set; }
            public bool[] JointsEnabled { get; set; }

            // Current Cartesian Position
            public double CurrentX { get; set; }
            public double CurrentY { get; set; }
            public double CurrentZ { get; set; }
            public double CurrentA { get; set; }
            public double CurrentB { get; set; }
            public double CurrentC { get; set; }
            public double CurrentU { get; set; }
            public double CurrentV { get; set; }

            // Target Cartesian Position
            public double TargetX { get; set; }
            public double TargetY { get; set; }
            public double TargetZ { get; set; }
            public double TargetA { get; set; }
            public double TargetB { get; set; }
            public double TargetC { get; set; }
            public double TargetU { get; set; }
            public double TargetV { get; set; }

            // Joint Angles
            public double JointAngle1 { get; set; }
            public double JointAngle2 { get; set; }
            public double JointAngle3 { get; set; }
            public double JointAngle4 { get; set; }
            public double JointAngle5 { get; set; }
            public double JointAngle6 { get; set; }
            public double JointAngle7 { get; set; }
            public double JointAngle8 { get; set; }

            public KognaStatus(int axisCount)
            {
                JointsActual = new double[axisCount];
                JointsTarget = new double[axisCount];
                JointsEnabled = new bool[axisCount];
            }
        }
    }
}
