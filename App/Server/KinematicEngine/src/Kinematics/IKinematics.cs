using System;

namespace KinematicEngine.Kinematics
{
    /// <summary>
    /// Interface for kinematic calculations (forward and inverse kinematics)
    /// </summary>
    public interface IKinematics : IDisposable
    {
        /// <summary>
        /// Gets the number of joints/axes supported by this kinematics
        /// </summary>
        int JointCount { get; }

        /// <summary>
        /// Gets the number of degrees of freedom
        /// </summary>
        int DegreesOfFreedom { get; }

        /// <summary>
        /// Performs forward kinematics: joint angles -> cartesian position
        /// </summary>
        /// <param name="jointAngles">Joint angles in radians</param>
        /// <returns>Cartesian position [x, y, z, a, b, c]</returns>
        double[] ForwardKinematics(double[] jointAngles);

        /// <summary>
        /// Performs inverse kinematics: cartesian position -> joint angles
        /// </summary>
        /// <param name="cartesianPosition">Cartesian position [x, y, z, a, b, c]</returns>
        /// <returns>Joint angles in radians, or null if no solution exists</returns>
        double[]? InverseKinematics(double[] cartesianPosition);

        /// <summary>
        /// Calculates the Jacobian matrix for the given joint configuration
        /// </summary>
        /// <param name="jointAngles">Joint angles in radians</param>
        /// <returns>Jacobian matrix</returns>
        double[,] CalculateJacobian(double[] jointAngles);

        /// <summary>
        /// Checks if a given cartesian position is reachable
        /// </summary>
        /// <param name="cartesianPosition">Cartesian position to check</param>
        /// <returns>True if reachable</returns>
        bool IsReachable(double[] cartesianPosition);

        /// <summary>
        /// Gets the workspace limits
        /// </summary>
        /// <returns>Workspace limits [x_min, x_max, y_min, y_max, z_min, z_max]</returns>
        double[] GetWorkspaceLimits();

        /// <summary>
        /// Gets the joint limits
        /// </summary>
        /// <returns>Joint limits [joint1_min, joint1_max, joint2_min, joint2_max, ...]</returns>
        double[] GetJointLimits();

        /// <summary>
        /// Validates joint angles against limits
        /// </summary>
        /// <param name="jointAngles">Joint angles to validate</param>
        /// <returns>True if within limits</returns>
        bool ValidateJointLimits(double[] jointAngles);

        /// <summary>
        /// Converts joint angles from degrees to radians
        /// </summary>
        /// <param name="jointAnglesDegrees">Joint angles in degrees</param>
        /// <returns>Joint angles in radians</returns>
        double[] DegreesToRadians(double[] jointAnglesDegrees);

        /// <summary>
        /// Converts joint angles from radians to degrees
        /// </summary>
        /// <param name="jointAnglesRadians">Joint angles in radians</param>
        /// <returns>Joint angles in degrees</returns>
        double[] RadiansToDegrees(double[] jointAnglesRadians);
    }

    /// <summary>
    /// Base class for kinematic implementations
    /// </summary>
    public abstract class KinematicsBase : IKinematics
    {
        protected readonly int _jointCount;
        protected readonly int _degreesOfFreedom;
        protected double[] _jointLimits;
        protected double[] _workspaceLimits;
        protected bool _disposed = false;

        protected KinematicsBase(int jointCount, int degreesOfFreedom)
        {
            _jointCount = jointCount;
            _degreesOfFreedom = degreesOfFreedom;
            _jointLimits = new double[jointCount * 2]; // min, max for each joint
            _workspaceLimits = new double[6]; // x_min, x_max, y_min, y_max, z_min, z_max
        }

        public int JointCount => _jointCount;
        public int DegreesOfFreedom => _degreesOfFreedom;

        public abstract double[] ForwardKinematics(double[] jointAngles);
        public abstract double[]? InverseKinematics(double[] cartesianPosition);
        public abstract double[,] CalculateJacobian(double[] jointAngles);

        public virtual bool IsReachable(double[] cartesianPosition)
        {
            if (cartesianPosition == null || cartesianPosition.Length != 6)
                return false;

            var limits = GetWorkspaceLimits();
            
            return cartesianPosition[0] >= limits[0] && cartesianPosition[0] <= limits[1] &&
                   cartesianPosition[1] >= limits[2] && cartesianPosition[1] <= limits[3] &&
                   cartesianPosition[2] >= limits[4] && cartesianPosition[2] <= limits[5];
        }

        public virtual double[] GetWorkspaceLimits() => (double[])_workspaceLimits.Clone();
        public virtual double[] GetJointLimits() => (double[])_jointLimits.Clone();

        public virtual bool ValidateJointLimits(double[] jointAngles)
        {
            if (jointAngles == null || jointAngles.Length != _jointCount)
                return false;

            for (int i = 0; i < _jointCount; i++)
            {
                double min = _jointLimits[i * 2];
                double max = _jointLimits[i * 2 + 1];
                if (jointAngles[i] < min || jointAngles[i] > max)
                    return false;
            }
            return true;
        }

        public virtual double[] DegreesToRadians(double[] jointAnglesDegrees)
        {
            if (jointAnglesDegrees == null)
                return new double[0];

            var result = new double[jointAnglesDegrees.Length];
            for (int i = 0; i < jointAnglesDegrees.Length; i++)
            {
                result[i] = jointAnglesDegrees[i] * Math.PI / 180.0;
            }
            return result;
        }

        public virtual double[] RadiansToDegrees(double[] jointAnglesRadians)
        {
            if (jointAnglesRadians == null)
                return new double[0];

            var result = new double[jointAnglesRadians.Length];
            for (int i = 0; i < jointAnglesRadians.Length; i++)
            {
                result[i] = jointAnglesRadians[i] * 180.0 / Math.PI;
            }
            return result;
        }

        public virtual void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }

        protected void ValidateInput(double[] jointAngles, string paramName)
        {
            if (jointAngles == null)
                throw new ArgumentNullException(paramName);
            if (jointAngles.Length != _jointCount)
                throw new ArgumentException($"Expected {_jointCount} joint angles, got {jointAngles.Length}", paramName);
        }

        protected void ValidateCartesianInput(double[] cartesianPosition, string paramName)
        {
            if (cartesianPosition == null)
                throw new ArgumentNullException(paramName);
            if (cartesianPosition.Length != 6)
                throw new ArgumentException("Expected 6 cartesian coordinates [x, y, z, a, b, c]", paramName);
        }
    }
} 