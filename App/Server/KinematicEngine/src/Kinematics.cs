using System;
using System.IO;
using System.Linq;


namespace KinematicEngine
{
    // Simple 2D/3D point structs
    public struct CPT2D { public double x, y; }
    public struct CPT3D { public double x, y, z; }

    // Motion parameters mirror the C++ MOTION_PARAMS struct
    public class MotionParams
    {
        public double BreakAngle;
        public double TPLookahead;
        public double MaxAccelV, MaxAccelU, MaxAccelC, MaxAccelB, MaxAccelA, MaxAccelX, MaxAccelY, MaxAccelZ;
        public double MaxVelV,  MaxVelU,  MaxVelC,  MaxVelB,  MaxVelA,  MaxVelX,  MaxVelY,  MaxVelZ;
        public double MaxRapidJerkV, MaxRapidJerkU, MaxRapidJerkC, MaxRapidJerkB, MaxRapidJerkA, MaxRapidJerkX, MaxRapidJerkY, MaxRapidJerkZ;
        public double MaxRapidAccelV, MaxRapidAccelU, MaxRapidAccelC, MaxRapidAccelB, MaxRapidAccelA, MaxRapidAccelX, MaxRapidAccelY, MaxRapidAccelZ;
        public double MaxRapidVelV,   MaxRapidVelU,   MaxRapidVelC,   MaxRapidVelB,   MaxRapidVelA,   MaxRapidVelX,   MaxRapidVelY,   MaxRapidVelZ;
        public double CountsPerInchV, CountsPerInchU, CountsPerInchC, CountsPerInchB, CountsPerInchA, CountsPerInchX, CountsPerInchY, CountsPerInchZ;
        public double MaxLinearLength;
        public double MaxAngularChange;
        public double MaxRapidFRO;
        public double CollinearTol;
        public double CornerTol;
        public double FacetAngle;
        public bool UseOnlyLinearSegments;
        public bool DoRapidsAsFeeds;
        public bool DegreesA, DegreesB, DegreesC;
        public double SoftLimitNegX, SoftLimitNegY, SoftLimitNegZ, SoftLimitNegA, SoftLimitNegB, SoftLimitNegC, SoftLimitNegU, SoftLimitNegV;
        public double SoftLimitPosX, SoftLimitPosY, SoftLimitPosZ, SoftLimitPosA, SoftLimitPosB, SoftLimitPosC, SoftLimitPosU, SoftLimitPosV;
        public bool   TCP_Active;
        public double TCP_X, TCP_Y, TCP_Z;
    }

    public class CKinematics : IDisposable
    {
        private const int NGCODE_AXES = 8;

        // Public state
        public MotionParams m_MotionParams;
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
            m_MotionParams = new MotionParams
            {
                BreakAngle = 30.0,
                TPLookahead = 3.0,
                MaxAccelV = 1.0,
                MaxAccelU = 1.0,
                MaxAccelC = 1.0,
                MaxAccelB = 1.0,
                MaxAccelA = 1.0,
                MaxAccelX = 1.0,
                MaxAccelY = 1.0,
                MaxAccelZ = 1.0,
                MaxVelV = 1.0,
                MaxVelU = 1.0,
                MaxVelC = 1.0,
                MaxVelB = 1.0,
                MaxVelA = 1.0,
                MaxVelX = 1.0,
                MaxVelY = 1.0,
                MaxVelZ = 1.0,
                MaxRapidJerkV = 10.0,
                MaxRapidJerkU = 10.0,
                MaxRapidJerkC = 10.0,
                MaxRapidJerkB = 10.0,
                MaxRapidJerkA = 10.0,
                MaxRapidJerkX = 10.0,
                MaxRapidJerkY = 10.0,
                MaxRapidJerkZ = 10.0,
                MaxRapidAccelV = 1.0,
                MaxRapidAccelU = 1.0,
                MaxRapidAccelC = 1.0,
                MaxRapidAccelB = 1.0,
                MaxRapidAccelA = 1.0,
                MaxRapidAccelX = 1.0,
                MaxRapidAccelY = 1.0,
                MaxRapidAccelZ = 1.0,
                MaxRapidVelV = 1.0,
                MaxRapidVelU = 1.0,
                MaxRapidVelC = 1.0,
                MaxRapidVelB = 1.0,
                MaxRapidVelA = 1.0,
                MaxRapidVelX = 1.0,
                MaxRapidVelY = 1.0,
                MaxRapidVelZ = 1.0,
                CountsPerInchV = 100.0,
                CountsPerInchU = 100.0,
                CountsPerInchC = 100.0,
                CountsPerInchB = 100.0,
                CountsPerInchA = 100.0,
                CountsPerInchX = 100.0,
                CountsPerInchY = 100.0,
                CountsPerInchZ = 100.0,
                MaxLinearLength = 1e30,
                MaxAngularChange = 1e30,
                MaxRapidFRO = 1.0,
                CollinearTol = 0.0002,
                CornerTol = 0.0002,
                FacetAngle = 0.5,
                UseOnlyLinearSegments = false,
                DoRapidsAsFeeds = false,
                DegreesA = false,
                DegreesB = false,
                DegreesC = false,
                SoftLimitNegX = -1e30,
                SoftLimitNegY = -1e30,
                SoftLimitNegZ = -1e30,
                SoftLimitNegA = -1e30,
                SoftLimitNegB = -1e30,
                SoftLimitNegC = -1e30,
                SoftLimitNegU = -1e30,
                SoftLimitNegV = -1e30,
                SoftLimitPosX = 1e30,
                SoftLimitPosY = 1e30,
                SoftLimitPosZ = 1e30,
                SoftLimitPosA = 1e30,
                SoftLimitPosB = 1e30,
                SoftLimitPosC = 1e30,
                SoftLimitPosU = 1e30,
                SoftLimitPosV = 1e30,
                TCP_Active = false,
                TCP_X = 0.0,
                TCP_Y = 0.0,
                TCP_Z = 0.0,
            };

            GeoTableValid = AnyLinearTableValid = false;
            GeoTable = null!;
            LinearTables = new double[NGCODE_AXES][];
            LinearTableValid = new bool[NGCODE_AXES];
            NLinear = new int[NGCODE_AXES];
            LinearSpacings = new double[NGCODE_AXES];
            LinearOffset = new double[NGCODE_AXES];
            for (int i = 0; i < NGCODE_AXES; i++)
            {
                LinearTables[i] = null!;
                LinearTableValid[i] = false;
            }
        }


        public int Initialize(string geoFile, string[] linearFiles)
        {
            // Load geometric table
            int geoStatus = ReadGeoTable(geoFile);
            GeoTableValid = (geoStatus == 0);
            // Load linear tables
            for (int i = 0; i < NGCODE_AXES; i++)
            {
                string path = (linearFiles != null && i < linearFiles.Length) ? linearFiles[i] : null!;
                ReadLinearTable(i, path, out bool valid);
            }
            AnyLinearTableValid = LinearTableValid.Any(v => v);
            return geoStatus;
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
                if (fda > 0 && m_MotionParams.MaxVelA < FeedRateToUse * fda / d) FeedRateToUse = m_MotionParams.MaxVelA * d / fda;
                if (fdb > 0 && m_MotionParams.MaxVelB < FeedRateToUse * fdb / d) FeedRateToUse = m_MotionParams.MaxVelB * d / fdb;
                if (fdc > 0 && m_MotionParams.MaxVelC < FeedRateToUse * fdc / d) FeedRateToUse = m_MotionParams.MaxVelC * d / fdc;
            }
            else
            {
                if (fdx > 0 && m_MotionParams.MaxVelX < FeedRateToUse * fdx / d) FeedRateToUse = m_MotionParams.MaxVelX * d / fdx;
                if (fdy > 0 && m_MotionParams.MaxVelY < FeedRateToUse * fdy / d) FeedRateToUse = m_MotionParams.MaxVelY * d / fdy;
                if (fdz > 0 && m_MotionParams.MaxVelZ < FeedRateToUse * fdz / d) FeedRateToUse = m_MotionParams.MaxVelZ * d / fdz;
                if (fdu > 0 && m_MotionParams.MaxVelU < FeedRateToUse * fdu / d) FeedRateToUse = m_MotionParams.MaxVelU * d / fdu;
                if (fdv > 0 && m_MotionParams.MaxVelV < FeedRateToUse * fdv / d) FeedRateToUse = m_MotionParams.MaxVelV * d / fdv;
                // fallback angular limits
                if (fda > 0) { double Max = m_MotionParams.MaxVelA; if (Max < FeedRateToUse * fda / d) FeedRateToUse = Max * d / fda; }
                if (fdb > 0) { double Max = m_MotionParams.MaxVelB; if (Max < FeedRateToUse * fdb / d) FeedRateToUse = Max * d / fdb; }
                if (fdc > 0) { double Max = m_MotionParams.MaxVelC; if (Max < FeedRateToUse * fdc / d) FeedRateToUse = Max * d / fdc; }
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
                if (fda > 0 && m_MotionParams.MaxAccelA < AccelToUse * fda / d) AccelToUse = m_MotionParams.MaxAccelA * d / fda;
                if (fdb > 0 && m_MotionParams.MaxAccelB < AccelToUse * fdb / d) AccelToUse = m_MotionParams.MaxAccelB * d / fdb;
                if (fdc > 0 && m_MotionParams.MaxAccelC < AccelToUse * fdc / d) AccelToUse = m_MotionParams.MaxAccelC * d / fdc;
            }
            else
            {
                if (fdx > 0 && m_MotionParams.MaxAccelX < AccelToUse * fdx / d) AccelToUse = m_MotionParams.MaxAccelX * d / fdx;
                if (fdy > 0 && m_MotionParams.MaxAccelY < AccelToUse * fdy / d) AccelToUse = m_MotionParams.MaxAccelY * d / fdy;
                if (fdz > 0 && m_MotionParams.MaxAccelZ < AccelToUse * fdz / d) AccelToUse = m_MotionParams.MaxAccelZ * d / fdz;
                if (fdu > 0 && m_MotionParams.MaxAccelU < AccelToUse * fdu / d) AccelToUse = m_MotionParams.MaxAccelU * d / fdu;
                if (fdv > 0 && m_MotionParams.MaxAccelV < AccelToUse * fdv / d) AccelToUse = m_MotionParams.MaxAccelV * d / fdv;
                if (fda > 0) { double Max = m_MotionParams.MaxAccelA; if (Max < AccelToUse * fda / d) AccelToUse = Max * d / fda; }
                if (fdb > 0) { double Max = m_MotionParams.MaxAccelB; if (Max < AccelToUse * fdb / d) AccelToUse = Max * d / fdb; }
                if (fdc > 0) { double Max = m_MotionParams.MaxAccelC; if (Max < AccelToUse * fdc / d) AccelToUse = Max * d / fdc; }
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
                if (fda > 0 && m_MotionParams.MaxRapidJerkA < JerkToUse * fda / d) JerkToUse = m_MotionParams.MaxRapidJerkA * d / fda;
                if (fdb > 0 && m_MotionParams.MaxRapidJerkB < JerkToUse * fdb / d) JerkToUse = m_MotionParams.MaxRapidJerkB * d / fdb;
                if (fdc > 0 && m_MotionParams.MaxRapidJerkC < JerkToUse * fdc / d) JerkToUse = m_MotionParams.MaxRapidJerkC * d / fdc;
            }
            else
            {
                if (fdx > 0 && m_MotionParams.MaxRapidJerkX < JerkToUse * fdx / d) JerkToUse = m_MotionParams.MaxRapidJerkX * d / fdx;
                if (fdy > 0 && m_MotionParams.MaxRapidJerkY < JerkToUse * fdy / d) JerkToUse = m_MotionParams.MaxRapidJerkY * d / fdy;
                if (fdz > 0 && m_MotionParams.MaxRapidJerkZ < JerkToUse * fdz / d) JerkToUse = m_MotionParams.MaxRapidJerkZ * d / fdz;
                if (fdu > 0 && m_MotionParams.MaxRapidJerkU < JerkToUse * fdu / d) JerkToUse = m_MotionParams.MaxRapidJerkU * d / fdu;
                if (fdv > 0 && m_MotionParams.MaxRapidJerkV < JerkToUse * fdv / d) JerkToUse = m_MotionParams.MaxRapidJerkV * d / fdv;
                if (fda > 0) { double Max = m_MotionParams.MaxRapidJerkA; if (Max < JerkToUse * fda / d) JerkToUse = Max * d / fda; }
                if (fdb > 0) { double Max = m_MotionParams.MaxRapidJerkB; if (Max < JerkToUse * fdb / d) JerkToUse = Max * d / fdb; }
                if (fdc > 0) { double Max = m_MotionParams.MaxRapidJerkC; if (Max < JerkToUse * fdc / d) JerkToUse = Max * d / fdc; }
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
            var field = typeof(MotionParams).GetField(key);
            if (field != null && field.GetValue(m_MotionParams) is double d)
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
        /// <summary>
        /// Factory: load the text file at 'filePath', which should list
        /// the geometric table on line 1 and one linear‐table per axis thereafter.
        /// </summary>
        public static CKinematics LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Kinematics definition not found", filePath);

            // Read every non-blank line as a relative path
            var lines = File.ReadAllLines(filePath)
                            .Select(l => l.Trim())
                            .Where(l => l.Length > 0)
                            .ToArray();

            if (lines.Length == 0)
                throw new InvalidDataException("Kinematics file is empty: " + filePath);

            // First line is the geo‐table
            string geoRel = lines[0];
            // Remaining lines are linear tables (one per axis)
            var linearRels = lines.Skip(1).ToArray();

            // Base directory for all relative names
            string dir = Path.GetDirectoryName(filePath)!;

            // Build full paths
            string geoPath    = Path.Combine(dir, geoRel);
            string[] linearPaths = linearRels
                .Select(rel => Path.Combine(dir, rel))
                .ToArray();

            // Create, initialize, and return
            var kin = new CKinematics();
            int status = kin.Initialize(geoPath, linearPaths);
            if (status != 0)
                throw new InvalidOperationException(
                    $"Failed to initialize kinematics (Init returned {status})");

            return kin;
        }
    
    }
}
