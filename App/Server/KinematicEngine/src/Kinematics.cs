using System;
using System.IO;
using System.Linq;


namespace KinematicEngine
{
    // Simple 2D/3D point structs
    public struct CPT2D { public double x, y; }
    public struct CPT3D { public double x, y, z; }

    // Motion parameters mirror the C++ MOTION_PARAMS struct


    public class CKinematics : IDisposable
    {
        private const int NGCODE_AXES = 8;

        // Public state
        public KEngine.MOTION_PARAMS motionParams;
        public bool GeoTableValid;
        public bool[] LinearTableValid;
        public bool AnyLinearTableValid;
        public CPT3D[] GeoTable;
        public double[][] LinearTables;
        public int[] NLinear;
        public double[] LinearSpacings;
        public double[] LinearOffset;
        public string MainPath = null!;
        public int NRows, NCols;
        public double GeoSpacingX, GeoSpacingY;
        public double GeoOffsetX, GeoOffsetY;
        public static int MaxDecelTime(int axis, double vel, double accel, double jerk) => throw new NotImplementedException();
        public static int NominalFROTime(char axis) => throw new NotImplementedException();


        public CKinematics()
        {
        }


        public int Start()
        {
                     
            

            return 1;
        }


        public void Dispose()
        {
            GeoTable = null!;
            // arrays will be GC-collected
        }

        public int Solve(double[] A, int N)
        {
            int N1 = N + 1;
            int NN = N * N1;
            for (int i = 0; i < N; i++)
            {
                if (Math.Abs(A[i * N1 + i]) < double.Epsilon)
                {
                    int l = i + 1;
                    if (l >= N) return 1;
                    while (l < N && Math.Abs(A[l * N1 + i]) < double.Epsilon) l++;
                    if (l >= N) return 1;
                    for (int m = 0; m <= N; m++)
                    {
                        double tmp = A[i * N1 + m];
                        A[i * N1 + m] = A[l * N1 + m];
                        A[l * N1 + m] = tmp;
                    }
                }
                for (int j = N; j >= i; j--) A[i * N1 + j] /= A[i * N1 + i];
                for (int j = 0; j < N; j++)
                {
                    if (j == i || Math.Abs(A[j * N1 + i]) < double.Epsilon) continue;
                    for (int k = N; k >= i; k--)
                        A[j * N1 + k] -= A[i * N1 + k] * A[j * N1 + i];
                }
            }
            return 0;
        }

        public int RemapForNonStandardAxes(ref double x, ref double y, ref double z, ref double a, ref double b, ref double c) => 0;


        public int ComputeAnglesOption(int isOption)
        {
            // C++ returns 0 unconditionally
            return 0;
        }

        public int MaxRateInDirection(double dx, double dy, double dz, double da, double db, double dc, double du, double dv, out double rate)
        {
            bool pureAngle;
            double d = FeedRateDistance(dx, dy, dz, da, db, dc, du, dv, out pureAngle); // fileciteturn6file1
            double FeedRateToUse = double.MaxValue;
            double fdx = Math.Abs(dx), fdy = Math.Abs(dy), fdz = Math.Abs(dz);
            double fda = Math.Abs(da), fdb = Math.Abs(db), fdc = Math.Abs(dc);
            double fdu = Math.Abs(du), fdv = Math.Abs(dv);
            if (pureAngle)
            {
                if (fda > 0 && motionParams.MaxVelA < FeedRateToUse * fda / d) FeedRateToUse = motionParams.MaxVelA * d / fda;
                if (fdb > 0 && motionParams.MaxVelB < FeedRateToUse * fdb / d) FeedRateToUse = motionParams.MaxVelB * d / fdb;
                if (fdc > 0 && motionParams.MaxVelC < FeedRateToUse * fdc / d) FeedRateToUse = motionParams.MaxVelC * d / fdc;
            }
            else
            {
                if (fdx > 0 && motionParams.MaxVelX < FeedRateToUse * fdx / d) FeedRateToUse = motionParams.MaxVelX * d / fdx;
                if (fdy > 0 && motionParams.MaxVelY < FeedRateToUse * fdy / d) FeedRateToUse = motionParams.MaxVelY * d / fdy;
                if (fdz > 0 && motionParams.MaxVelZ < FeedRateToUse * fdz / d) FeedRateToUse = motionParams.MaxVelZ * d / fdz;
                if (fdu > 0 && motionParams.MaxVelU < FeedRateToUse * fdu / d) FeedRateToUse = motionParams.MaxVelU * d / fdu;
                if (fdv > 0 && motionParams.MaxVelV < FeedRateToUse * fdv / d) FeedRateToUse = motionParams.MaxVelV * d / fdv;
                // fallback angular limits
                if (fda > 0) { double Max = motionParams.MaxVelA; if (Max < FeedRateToUse * fda / d) FeedRateToUse = Max * d / fda; }
                if (fdb > 0) { double Max = motionParams.MaxVelB; if (Max < FeedRateToUse * fdb / d) FeedRateToUse = Max * d / fdb; }
                if (fdc > 0) { double Max = motionParams.MaxVelC; if (Max < FeedRateToUse * fdc / d) FeedRateToUse = Max * d / fdc; }
            }
            rate = FeedRateToUse;
            return 0;
        }

        public int MaxRateInDirection(double dx, double dy, double dz, double da, double db, double dc, out double rate)
        {
            return MaxRateInDirection(dx, dy, dz, da, db, dc, 0, 0, out rate);
        }

        public int MaxAccelInDirection(double dx, double dy, double dz, double da, double db, double dc, double du, double dv, out double accel)
        {
            bool pureAngle;
            double d = FeedRateDistance(dx, dy, dz, da, db, dc, du, dv, out pureAngle); // fileciteturn6file9
            double AccelToUse = double.MaxValue;
            double fdx = Math.Abs(dx), fdy = Math.Abs(dy), fdz = Math.Abs(dz);
            double fda = Math.Abs(da), fdb = Math.Abs(db), fdc = Math.Abs(dc);
            double fdu = Math.Abs(du), fdv = Math.Abs(dv);
            if (pureAngle)
            {
                if (fda > 0 && motionParams.MaxAccelA < AccelToUse * fda / d) AccelToUse = motionParams.MaxAccelA * d / fda;
                if (fdb > 0 && motionParams.MaxAccelB < AccelToUse * fdb / d) AccelToUse = motionParams.MaxAccelB * d / fdb;
                if (fdc > 0 && motionParams.MaxAccelC < AccelToUse * fdc / d) AccelToUse = motionParams.MaxAccelC * d / fdc;
            }
            else
            {
                if (fdx > 0 && motionParams.MaxAccelX < AccelToUse * fdx / d) AccelToUse = motionParams.MaxAccelX * d / fdx;
                if (fdy > 0 && motionParams.MaxAccelY < AccelToUse * fdy / d) AccelToUse = motionParams.MaxAccelY * d / fdy;
                if (fdz > 0 && motionParams.MaxAccelZ < AccelToUse * fdz / d) AccelToUse = motionParams.MaxAccelZ * d / fdz;
                if (fdu > 0 && motionParams.MaxAccelU < AccelToUse * fdu / d) AccelToUse = motionParams.MaxAccelU * d / fdu;
                if (fdv > 0 && motionParams.MaxAccelV < AccelToUse * fdv / d) AccelToUse = motionParams.MaxAccelV * d / fdv;
                if (fda > 0) { double Max = motionParams.MaxAccelA; if (Max < AccelToUse * fda / d) AccelToUse = Max * d / fda; }
                if (fdb > 0) { double Max = motionParams.MaxAccelB; if (Max < AccelToUse * fdb / d) AccelToUse = Max * d / fdb; }
                if (fdc > 0) { double Max = motionParams.MaxAccelC; if (Max < AccelToUse * fdc / d) AccelToUse = Max * d / fdc; }
            }
            accel = AccelToUse;
            return 0;
        }

        public int MaxAccelInDirection(double dx, double dy, double dz, double da, double db, double dc, out double accel)
        {
            return MaxAccelInDirection(dx, dy, dz, da, db, dc, 0, 0, out accel);
        }
        public static double FeedRateDistance(double dx, double dy, double dz, double da, double db, double dc, double du, double dv, out bool pureAngle)
        {
            double fdx = Math.Abs(dx), fdy = Math.Abs(dy), fdz = Math.Abs(dz);
            double fda = Math.Abs(da), fdb = Math.Abs(db), fdc = Math.Abs(dc);
            double fdu = Math.Abs(du), fdv = Math.Abs(dv);
            bool anyLinear = fdx > 0 || fdy > 0 || fdz > 0 || fdu > 0 || fdv > 0;
            bool anyAngular = fda > 0 || fdb > 0 || fdc > 0;
            pureAngle = !anyLinear && anyAngular;
            double d;
            if (pureAngle)
                d = Math.Sqrt(fda * fda + fdb * fdb + fdc * fdc);
            else
                d = Math.Sqrt(fdx * fdx + fdy * fdy + fdz * fdz + fda * fda + fdb * fdb + fdc * fdc + fdu * fdu + fdv * fdv);
            return d;
        }

        public int MaxRapidJerkInDirection(double dx, double dy, double dz, double da, double db, double dc, double du, double dv, out double jerk)
        {
            bool pureAngle;
            double d = FeedRateDistance(dx, dy, dz, da, db, dc, du, dv, out pureAngle); // fileciteturn6file11
            double JerkToUse = double.MaxValue;
            double fdx = Math.Abs(dx), fdy = Math.Abs(dy), fdz = Math.Abs(dz);
            double fda = Math.Abs(da), fdb = Math.Abs(db), fdc = Math.Abs(dc);
            double fdu = Math.Abs(du), fdv = Math.Abs(dv);
            if (pureAngle)
            {
                if (fda > 0 && motionParams.MaxRapidJerkA < JerkToUse * fda / d) JerkToUse = motionParams.MaxRapidJerkA * d / fda;
                if (fdb > 0 && motionParams.MaxRapidJerkB < JerkToUse * fdb / d) JerkToUse = motionParams.MaxRapidJerkB * d / fdb;
                if (fdc > 0 && motionParams.MaxRapidJerkC < JerkToUse * fdc / d) JerkToUse = motionParams.MaxRapidJerkC * d / fdc;
            }
            else
            {
                if (fdx > 0 && motionParams.MaxRapidJerkX < JerkToUse * fdx / d) JerkToUse = motionParams.MaxRapidJerkX * d / fdx;
                if (fdy > 0 && motionParams.MaxRapidJerkY < JerkToUse * fdy / d) JerkToUse = motionParams.MaxRapidJerkY * d / fdy;
                if (fdz > 0 && motionParams.MaxRapidJerkZ < JerkToUse * fdz / d) JerkToUse = motionParams.MaxRapidJerkZ * d / fdz;
                if (fdu > 0 && motionParams.MaxRapidJerkU < JerkToUse * fdu / d) JerkToUse = motionParams.MaxRapidJerkU * d / fdu;
                if (fdv > 0 && motionParams.MaxRapidJerkV < JerkToUse * fdv / d) JerkToUse = motionParams.MaxRapidJerkV * d / fdv;
                if (fda > 0) { double Max = motionParams.MaxRapidJerkA; if (Max < JerkToUse * fda / d) JerkToUse = Max * d / fda; }
                if (fdb > 0) { double Max = motionParams.MaxRapidJerkB; if (Max < JerkToUse * fdb / d) JerkToUse = Max * d / fdb; }
                if (fdc > 0) { double Max = motionParams.MaxRapidJerkC; if (Max < JerkToUse * fdc / d) JerkToUse = Max * d / fdc; }
            }
            jerk = JerkToUse;
            return 0;
        }


        public int IntersectionTwoCircles(CPT2D c0, double r0, CPT2D c1, double r1, out CPT2D[] intersections)
        {
            double dx = c1.x - c0.x;
            double dy = c1.y - c0.y;
            double d = Math.Sqrt(dx * dx + dy * dy);

            if (d > r0 + r1 || d < Math.Abs(r0 - r1))
            {
                intersections = Array.Empty<CPT2D>();
                return 1; // no intersection
            }

            double a = (r0 * r0 - r1 * r1 + d * d) / (2 * d);
            double h = Math.Sqrt(Math.Max(r0 * r0 - a * a, 0));
            double xm = c0.x + a * dx / d;
            double ym = c0.y + a * dy / d;
            double rx = -dy * (h / d);
            double ry = dx * (h / d);

            intersections = new CPT2D[2];
            intersections[0] = new CPT2D { x = xm + rx, y = ym + ry };
            intersections[1] = new CPT2D { x = xm - rx, y = ym - ry };
            return 0;
        }

        public int ReadGeoTable(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                GeoTableValid = false;
                return 0;
            }
            if (!File.Exists(filePath))
            {
                GeoTableValid = false;
                return 1;
            }
            var lines = File.ReadAllLines(filePath);
            if (lines.Length < 3) return 1;

            var header = lines[0].Split(',');
            if (header.Length < 2 || !int.TryParse(header[0], out int nRows) || !int.TryParse(header[1], out int nCols))
                return 1;

            var spacing = lines[1].Split(',');
            if (spacing.Length < 2 || !double.TryParse(spacing[0], out GeoSpacingX) || !double.TryParse(spacing[1], out GeoSpacingY))
                return 1;

            var offset = lines[2].Split(',');
            if (offset.Length < 2 || !double.TryParse(offset[0], out GeoOffsetX) || !double.TryParse(offset[1], out GeoOffsetY))
                return 1;

            GeoTable = new CPT3D[NRows * NCols];
            for (int i = 0; i < NRows * NCols; i++)
            {
                var parts = lines[3 + i].Split(',');
                if (parts.Length < 5) return 1;
                if (!int.TryParse(parts[0], out int row) || !int.TryParse(parts[1], out int col)
                    || !double.TryParse(parts[2], out double X) || !double.TryParse(parts[3], out double Y)
                    || !double.TryParse(parts[4], out double Z)) return 1;
                if (row < 0 || row >= NRows || col < 0 || col >= NCols) return 1;
                GeoTable[row * NCols + col] = new CPT3D { x = X, y = Y, z = Z };
            }
            GeoTableValid = true;
            return 0;
        }

        public int ReadLinearTable(int axisIndex, string filePath, out bool valid)
        {
            valid = false;
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return 1;

            var lines = File.ReadAllLines(filePath);
            if (lines.Length < 3)
                return 1;

            // First line: number of entries
            if (!int.TryParse(lines[0], out int count) || count <= 0)
                return 1;
            // Second line: spacing
            if (!double.TryParse(lines[1], out double spacing))
                return 1;
            // Third line: offset
            if (!double.TryParse(lines[2], out double offset))
                return 1;

            NLinear[axisIndex] = count;
            LinearSpacings[axisIndex] = spacing;
            LinearOffset[axisIndex] = offset;
            LinearTables[axisIndex] = new double[count];

            // Subsequent lines: index,value
            for (int i = 0; i < count; i++)
            {
                var parts = lines[3 + i].Split(',');
                if (parts.Length < 2)
                    return 1;
                if (!int.TryParse(parts[0], out int idx) || !double.TryParse(parts[1], out double val))
                    return 1;
                if (idx < 0 || idx >= count)
                    return 1;
                LinearTables[axisIndex][idx] = val;
            }

            LinearTableValid[axisIndex] = true;
            AnyLinearTableValid |= true;
            valid = true;
            return 0;
        }


        public int GetParameter(string key, out double value)
        {
            // simplistic reflection-based retrieval
            var field = typeof(KEngine.MOTION_PARAMS).GetField(key);
            if (field != null && field.GetValue(motionParams) is double d)
            {
                value = d;
                return 0;
            }
            value = 0;
            return 1;
        }

        private string RemoveChar(string s, char c)
        {
            return new string(s.Where(ch => ch != c).ToArray());
        }

    
    }
}
