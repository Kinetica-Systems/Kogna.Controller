// File: CSharpCoordMotion.cs
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KognaServer.Server.KognaServer
{
    /// <summary>
    /// Managed version of CCoordMotion: 
    /// sends DefineCS/PosN/DestN over KognaIO and parses responses.
    /// </summary>
    public class KognaMotion 
    {
        private readonly KognaIO _io;
        private readonly int[]   _axes = new int[6];       // channel indices for X,Y,Z,A,B,C
        private bool             _axesDefined;

        public KognaMotion(KognaIO io)
        {
            _io = io;
        }

        /// <summary>
        /// Send "DefineCS" once to populate _axes[0..5] = {x_axis,y_axis,...,c_axis}.
        /// </summary>
        public void GetAxisDefinitions()
        {
            if (_axesDefined) return;

            // blocking call: send "DefineCS" and read back six ints
            if (_io.WriteLineReadLine(1, "DefineCS", out var resp) != KognaIO.KOGNA_OK)
                throw new InvalidOperationException("DefineCS failed");

            // e.g. resp == "0 1 2 3 4 5"
            var parts = resp
                .Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.Parse(s, CultureInfo.InvariantCulture))
                .ToArray();

            if (parts.Length < 6)
                throw new InvalidOperationException($"DefineCS returned {parts.Length} values");

            for (int i = 0; i < 6; i++)
                _axes[i] = parts[i];

            _axesDefined = true;
        }

        /// <summary>
        /// Query the current actual position of the given logical axis (0..5 → X..C).
        /// </summary>
        public double GetPosition(int logicalAxis)
        {
            if (!_axesDefined) GetAxisDefinitions();
            int channel = _axes[logicalAxis];
            if (_io.WriteLineReadLine(1, $"Pos{channel}", out var resp) != KognaIO.KOGNA_OK)
                throw new InvalidOperationException($"Pos{channel} failed");
            // resp might be "123.456" or "123.456 XYZ" – parse first number
            var s = resp.Trim().Split(' ')[0];
            return double.Parse(s, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Query the current target (destination) position of the given logical axis.
        /// </summary>
        public double GetDestination(int logicalAxis)
        {
            if (!_axesDefined) GetAxisDefinitions();
            int channel = _axes[logicalAxis];
            if (_io.WriteLineReadLine(1, $"Dest{channel}", out var resp) != KognaIO.KOGNA_OK)
                throw new InvalidOperationException($"Dest{channel} failed");
            var s = resp.Trim().Split(' ')[0];
            return double.Parse(s, CultureInfo.InvariantCulture);
        }


    }
}
