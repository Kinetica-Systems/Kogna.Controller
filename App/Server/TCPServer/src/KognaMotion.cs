// File: CSharpCoordMotion.cs
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TCPServer
{
    /// <summary>
    /// Managed version of CCoordMotion: 
    /// sends DefineCS/PosN/DestN over KognaIO and parses responses.
    /// </summary>
    public class KognaMotion 
    {
        private readonly KognaIO _io;
        private int[]   _axes = new int[8];       // channel indices for X,Y,Z,A,B,C,U,V
        private bool             _axesDefined;
        private int _axisCount = 6; // Default to 6 for backward compatibility
        public int AxisCount => _axisCount;

        public KognaMotion(KognaIO io)
        {
            _io = io;
        }

        /// <summary>
        /// Send "DefineCS" once to populate _axes[0..N-1] = {x_axis,y_axis,...} and set axis count.
        /// </summary>
        public void GetAxisDefinitions()
        {
            if (_axesDefined) return;

            try
            {
                Console.WriteLine("[KOGNA_MOTION] Sending DefineCS command...");
                
                // blocking call: send "DefineCS" and read back ints
                if (_io.WriteLineReadLine(1, "DefineCS", out var resp) != KognaIO.KOGNA_OK)
                {
                    throw new InvalidOperationException("DefineCS failed - no response from device");
                }

                Console.WriteLine($"[KOGNA_MOTION] DefineCS response: '{resp}'");

                // e.g. resp == "0 1 2 3 4 5" or "0 1 2 3 4 5 6 7"
                var parts = resp
                    .Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.Parse(s, CultureInfo.InvariantCulture))
                    .ToArray();

                _axisCount = parts.Length;
                if (_axisCount < 1 || _axisCount > 8)
                {
                    throw new InvalidOperationException($"DefineCS returned {_axisCount} values, expected 1-8. Response: '{resp}'");
                }

                // Resize _axes if needed
                if (_axes.Length != _axisCount)
                {
                    Array.Resize(ref _axes, _axisCount);
                }

                for (int i = 0; i < _axisCount; i++)
                    _axes[i] = parts[i];

                _axesDefined = true;
                Console.WriteLine($"[KOGNA_MOTION] Axis definitions set: [{string.Join(", ", _axes)}], AxisCount={_axisCount}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[KOGNA_MOTION] ERROR in GetAxisDefinitions: {ex.Message}");
                throw; // Re-throw to let caller handle
            }
        }

        /// <summary>
        /// Query the current actual position of the given logical axis (0..N-1).
        /// </summary>
        public double GetPosition(int logicalAxis)
        {
            if (!_axesDefined) GetAxisDefinitions();
            
            if (logicalAxis < 0 || logicalAxis >= _axisCount)
                throw new ArgumentOutOfRangeException(nameof(logicalAxis), $"Axis must be between 0 and {_axisCount-1}");
                
            int channel = _axes[logicalAxis];
            Console.WriteLine($"[KOGNA_MOTION] Getting position for axis {logicalAxis} (channel {channel})");
            if (_io.WriteLineReadLine(1, $"Pos{channel}", out var resp) != KognaIO.KOGNA_OK)
                throw new InvalidOperationException($"Pos{channel} failed");
            // resp might be "123.456" or "123.456 XYZ" – parse first number
            var s = resp.Trim().Split(' ')[0];
            double position = double.Parse(s, CultureInfo.InvariantCulture);
            Console.WriteLine($"[KOGNA_MOTION] Position for axis {logicalAxis}: {position}");
            return position;
        }

        /// <summary>
        /// Query the current target (destination) position of the given logical axis.
        /// </summary>
        public double GetDestination(int logicalAxis)
        {
            if (!_axesDefined) GetAxisDefinitions();
            
            if (logicalAxis < 0 || logicalAxis >= _axisCount)
                throw new ArgumentOutOfRangeException(nameof(logicalAxis), $"Axis must be between 0 and {_axisCount-1}");
                
            int channel = _axes[logicalAxis];
            if (_io.WriteLineReadLine(1, $"Dest{channel}", out var resp) != KognaIO.KOGNA_OK)
                throw new InvalidOperationException($"Dest{channel} failed");
            var s = resp.Trim().Split(' ')[0];
            return double.Parse(s, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Send a linear motion command to the Kogna controller.
        /// </summary>
        public int SendLinear(double x0, double y0, double z0, double a0, double b0, double c0,
                             double x1, double y1, double z1, double a1, double b1, double c1,
                             double feed, double accel, double jerk, double t)
        {
            // Format: Linear X0 Y0 Z0 A0 B0 C0 X1 Y1 Z1 A1 B1 C1 F A J T
            string cmd = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Linear {0:F4} {1:F4} {2:F4} {3:F4} {4:F4} {5:F4} " +
                "{6:F4} {7:F4} {8:F4} {9:F4} {10:F4} {11:F4} " +
                "{12:F4} {13:F4} {14:F4} {15:F4}",
                x0, y0, z0, a0, b0, c0, x1, y1, z1, a1, b1, c1, feed, accel, jerk, t);
            
            Console.WriteLine($"[KOGNA_MOTION] Sending Linear command: {cmd}");
            
            // Open buffer for streaming
            Console.WriteLine("[KOGNA_MOTION] Opening buffer...");
            int openResult = OpenBuffer();
            Console.WriteLine($"[KOGNA_MOTION] OpenBuf result: {openResult}");
            
            // Send the command to buffer
            int result = _io.WriteLine(1, cmd);
            Console.WriteLine($"[KOGNA_MOTION] Linear command result: {result}");
            
            if (result == KognaIO.KOGNA_OK)
            {
                // Flush buffer to ensure all commands are sent
                Console.WriteLine("[KOGNA_MOTION] Flushing buffer...");
                int flushResult = FlushBuffer();
                Console.WriteLine($"[KOGNA_MOTION] FlushBuf result: {flushResult}");
                
                // Execute the buffer to start motion
                Console.WriteLine("[KOGNA_MOTION] Executing buffer...");
                int execResult = ExecBuffer();
                Console.WriteLine($"[KOGNA_MOTION] ExecBuf result: {execResult}");
            }
            
            return result;
        }

        /// <summary>
        /// Send an arc motion command to the Kogna controller.
        /// </summary>
        public int SendArc(double x0, double y0, double z0, double a0, double b0, double c0,
                          double x1, double y1, double z1, double a1, double b1, double c1,
                          double xc, double yc, bool dirIsCCW, double feed, double accel, double jerk, double t)
        {
            // Format: Arc X0 Y0 Z0 A0 B0 C0 X1 Y1 Z1 A1 B1 C1 XC YC DIR F A J T
            string cmd = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Arc {0:F4} {1:F4} {2:F4} {3:F4} {4:F4} {5:F4} " +
                "{6:F4} {7:F4} {8:F4} {9:F4} {10:F4} {11:F4} " +
                "{12:F4} {13:F4} {14} {15:F4} {16:F4} {17:F4} {18:F4}",
                x0, y0, z0, a0, b0, c0, x1, y1, z1, a1, b1, c1, xc, yc, dirIsCCW ? 1 : 0, feed, accel, jerk, t);
            return _io.WriteLine(1, cmd);
        }

        /// <summary>
        /// Open the controller buffer for streaming commands.
        /// </summary>
        public int OpenBuffer()
        {
            Console.WriteLine("[KOGNA_MOTION] Sending OpenBuf command");
            int result = _io.WriteLine(1, "OpenBuf");
            Console.WriteLine($"[KOGNA_MOTION] OpenBuf result: {result}");
            return result;
        }

        /// <summary>
        /// Flush the controller buffer (send all buffered commands).
        /// </summary>
        public int FlushBuffer()
        {
            Console.WriteLine("[KOGNA_MOTION] Sending FlushBuf command");
            int result = _io.WriteLine(1, "FlushBuf");
            Console.WriteLine($"[KOGNA_MOTION] FlushBuf result: {result}");
            return result;
        }

        /// <summary>
        /// Execute the controller buffer (start motion).
        /// </summary>
        public int ExecBuffer()
        {
            Console.WriteLine("[KOGNA_MOTION] Sending ExecBuf command");
            int result = _io.WriteLine(1, "ExecBuf");
            Console.WriteLine($"[KOGNA_MOTION] ExecBuf result: {result}");
            return result;
        }

        /// <summary>
        /// Check if the buffer is done executing.
        /// </summary>
        public int CheckDoneBuffer(out bool isDone)
        {
            int result = _io.WriteLineReadLine(1, "CheckDoneBuf", out var resp);
            isDone = resp.Trim() == "1";
            return result;
        }


    }
}
