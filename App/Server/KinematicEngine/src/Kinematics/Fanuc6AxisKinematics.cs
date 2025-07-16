using System;
using System.Numerics;

namespace KinematicEngine.Kinematics
{
    /// <summary>
    /// Fanuc-style 6-axis robot kinematics implementation
    /// </summary>
    public class Fanuc6AxisKinematics : KinematicsBase
    {
        // Link lengths (in mm)
        private const double L1_X = 180.0;     // Base offset in X
        private const double L1_Z = 1000.0;    // Base height
        private const double L2 = 950.0;       // Upper arm length
        private static readonly double L3 = Math.Sqrt(1150 * 1150 + 240 * 240); // Forearm length
        private const double L6 = 200.0;       // Tool length

        // TCP (Tool Center Point) configuration
        public Vector3 TcpTranslation { get; set; } = Vector3.Zero;
        public Vector3 TcpRotationEuler { get; set; } = Vector3.Zero;

        public Fanuc6AxisKinematics() : base(6, 6)
        {
            // Set joint limits (in degrees)
            _jointLimits = new double[]
            {
                -180.0, 180.0,  // Joint 1 (base rotation)
                -90.0,  90.0,   // Joint 2 (shoulder)
                -180.0, 180.0,  // Joint 3 (elbow)
                -180.0, 180.0,  // Joint 4 (wrist roll)
                -90.0,  90.0,   // Joint 5 (wrist pitch)
                -180.0, 180.0   // Joint 6 (wrist yaw)
            };

            // Set workspace limits (in mm)
            _workspaceLimits = new double[]
            {
                -2000.0, 2000.0,  // X limits
                -2000.0, 2000.0,  // Y limits
                0.0, 3000.0       // Z limits
            };
        }

        /// <summary>
        /// Forward kinematics: joint angles -> cartesian position
        /// </summary>
        /// <param name="jointAngles">Joint angles in radians</param>
        /// <returns>Cartesian position [x, y, z, a, b, c] in mm and degrees</returns>
        public override double[] ForwardKinematics(double[] jointAngles)
        {
            ValidateInput(jointAngles, nameof(jointAngles));

            // Convert to degrees for the existing implementation
            var jointAnglesDegrees = RadiansToDegrees(jointAngles);

            // Calculate flange position from first 3 joints
            var flangePosition = ForwardKinematicsFanuc(
                jointAnglesDegrees[0],  // θ1 (deg)
                jointAnglesDegrees[1],  // θ2 (deg)
                jointAnglesDegrees[2]   // θ3 (deg)
            );

            // Build flange pose [Xf, Yf, Zf, Af, Bf, Cf]
            var flangePose = new double[6]
            {
                flangePosition[0], flangePosition[1], flangePosition[2],
                jointAnglesDegrees[3], jointAnglesDegrees[4], jointAnglesDegrees[5]
            };

            // Build transformation matrix from flange pose
            var T0_6 = TransformFromPose(flangePose);

            // Apply TCP offset: T0_tcp = T0_6 · ToolOffset
            var T0_tcp = T0_6 * ComputeToolOffset();

            // Extract final TCP pose [X, Y, Z, A, B, C]
            return PoseFromTransform(T0_tcp);
        }

        /// <summary>
        /// Inverse kinematics: cartesian position -> joint angles
        /// </summary>
        /// <param name="cartesianPosition">Cartesian position [x, y, z, a, b, c] in mm and degrees</param>
        /// <returns>Joint angles in radians, or null if no solution exists</returns>
        public override double[]? InverseKinematics(double[] cartesianPosition)
        {
            ValidateCartesianInput(cartesianPosition, nameof(cartesianPosition));

            // Peel off TCP: desired flange pose T0_6 = T0_tcp · ToolOffset⁻¹
            var T0_tcp = TransformFromPose(cartesianPosition);
            var T0_6 = T0_tcp * ComputeToolOffsetInverse();

            // Turn flange pose back into XYZABC for IK routine
            var flangePose = PoseFromTransform(T0_6);

            // Solve inverse kinematics
            var jointAnglesDegrees = new double[6];
            if (!SolveInverseKinematicsFanuc(flangePose, jointAnglesDegrees))
            {
                return null; // No solution found
            }

            // Convert to radians
            return DegreesToRadians(jointAnglesDegrees);
        }

        /// <summary>
        /// Calculates the Jacobian matrix for the given joint configuration
        /// </summary>
        /// <param name="jointAngles">Joint angles in radians</param>
        /// <returns>6x6 Jacobian matrix</returns>
        public override double[,] CalculateJacobian(double[] jointAngles)
        {
            ValidateInput(jointAngles, nameof(jointAngles));

            var jacobian = new double[6, 6];
            const double delta = 0.001; // Small perturbation for numerical differentiation

            // Get current position
            var currentPosition = ForwardKinematics(jointAngles);

            // Calculate Jacobian by finite differences
            for (int i = 0; i < 6; i++)
            {
                var perturbedAngles = (double[])jointAngles.Clone();
                perturbedAngles[i] += delta;

                var perturbedPosition = ForwardKinematics(perturbedAngles);

                for (int j = 0; j < 6; j++)
                {
                    jacobian[j, i] = (perturbedPosition[j] - currentPosition[j]) / delta;
                }
            }

            return jacobian;
        }

        /// <summary>
        /// Computes the tool offset transformation matrix
        /// </summary>
        private Matrix4x4 ComputeToolOffset()
        {
            // Build R = Rₓ(α)·Rᵧ(β)·R𝓏(γ)
            var Rx = Matrix4x4.CreateRotationX(TcpRotationEuler.X);
            var Ry = Matrix4x4.CreateRotationY(TcpRotationEuler.Y);
            var Rz = Matrix4x4.CreateRotationZ(TcpRotationEuler.Z);
            var R = Rx * Ry * Rz;

            // Then translate by TcpTranslation
            var T = Matrix4x4.CreateTranslation(TcpTranslation);
            return T * R;
        }

        /// <summary>
        /// Computes the inverse of the tool offset transformation matrix
        /// </summary>
        private Matrix4x4 ComputeToolOffsetInverse()
        {
            Matrix4x4.Invert(ComputeToolOffset(), out var inv);
            return inv;
        }

        /// <summary>
        /// Fanuc-style analytic IK for the first 3 joints
        /// </summary>
        private static bool SolveInverseKinematicsFanuc(double[] cart, double[] acts)
        {
            // cart = [x,y,z,a,b,c], angles in degrees
            double x = cart[0], y = cart[1], z = cart[2];
            double ar = DegToRad(cart[3]), br = DegToRad(cart[4]), cr = DegToRad(cart[5]);

            // Compute wrist center
            double ca = Math.Cos(ar), cb = Math.Cos(br), cc = Math.Cos(cr);
            double sa = Math.Sin(ar), sb = Math.Sin(br), sc = Math.Sin(cr);

            double nx = cb * cc;
            double ny = cb * sc;
            double nz = -sb;

            double wx = x - L6 * nx - L1_X;
            double wy = y - L6 * ny;
            double wz = z - L6 * nz - L1_Z;

            // Joint 1
            double theta1 = Math.Atan2(wy, wx);
            double r = Math.Sqrt(wx * wx + wy * wy), s = wz;

            // Joints 2 & 3 via law-of-cosines
            double D = (r * r + s * s - L2 * L2 - L3 * L3) / (2 * L2 * L3);
            if (D < -1 || D > 1) return false;
            
            double theta3 = Math.Atan2(Math.Sqrt(1 - D * D), D);
            double theta2 = Math.Atan2(s, r) - Math.Atan2(L3 * Math.Sin(theta3), L2 + L3 * Math.Cos(theta3));

            // Write out in degrees
            acts[0] = RadToDeg(theta1);
            acts[1] = RadToDeg(theta2);
            acts[2] = RadToDeg(theta3);

            // Wrist orientation will be assigned by caller
            acts[3] = cart[3];
            acts[4] = cart[4];
            acts[5] = cart[5];

            return true;
        }

        /// <summary>
        /// Forward kinematics for the first 3 joints (Fanuc style)
        /// </summary>
        private static double[] ForwardKinematicsFanuc(double th1_deg, double th2_deg, double th3_deg)
        {
            double th1 = DegToRad(th1_deg);
            double th2 = DegToRad(th2_deg);
            double th3 = DegToRad(th3_deg);

            double c1 = Math.Cos(th1), s1 = Math.Sin(th1);
            double c2 = Math.Cos(th2), s2 = Math.Sin(th2);
            double c3 = Math.Cos(th3), s3 = Math.Sin(th3);

            double x = L1_X + L2 * c2 * c1 + L3 * (c2 * c3 - s2 * s3) * c1;
            double y = L2 * c2 * s1 + L3 * (c2 * c3 - s2 * s3) * s1;
            double z = L1_Z + L2 * s2 + L3 * (c2 * s3 + s2 * c3);

            return new double[] { x, y, z };
        }

        /// <summary>
        /// Builds 4x4 transformation matrix from pose [X,Y,Z,A,B,C]
        /// </summary>
        private Matrix4x4 TransformFromPose(double[] pose)
        {
            float x = (float)pose[0], y = (float)pose[1], z = (float)pose[2];
            float a = (float)(pose[3] * Deg2Rad), b = (float)(pose[4] * Deg2Rad), c = (float)(pose[5] * Deg2Rad);

            var Rx = Matrix4x4.CreateRotationX(a);
            var Ry = Matrix4x4.CreateRotationY(b);
            var Rz = Matrix4x4.CreateRotationZ(c);
            var R = Rx * Ry * Rz;

            var T = Matrix4x4.CreateTranslation(x, y, z);
            return T * R;
        }

        /// <summary>
        /// Extracts [X,Y,Z,A,B,C] from 4x4 transformation matrix
        /// </summary>
        private double[] PoseFromTransform(Matrix4x4 M)
        {
            double x = M.M41, y = M.M42, z = M.M43;
            double r11 = M.M11, r12 = M.M12, r13 = M.M13;
            double r23 = M.M23, r33 = M.M33;

            double B = Math.Asin(Math.Clamp(r13, -1.0, 1.0));
            double A = Math.Atan2(-r23, r33);
            double C = Math.Atan2(-r12, r11);

            return new double[] { x, y, z, A * Rad2Deg, B * Rad2Deg, C * Rad2Deg };
        }

        private static double DegToRad(double d) => d * Math.PI / 180.0;
        private static double RadToDeg(double r) => r * 180.0 / Math.PI;
        private const double Deg2Rad = Math.PI / 180.0;
        private const double Rad2Deg = 180.0 / Math.PI;
    }
} 