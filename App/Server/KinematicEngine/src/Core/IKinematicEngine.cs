using System;
using System.Threading.Tasks;

namespace KinematicEngine.Core
{
    /// <summary>
    /// Core interface for the kinematic engine, defining the main operations
    /// that can be performed on a multi-axis motion system.
    /// </summary>
    public interface IKinematicEngine : IDisposable
    {
        /// <summary>
        /// Gets the current status of the kinematic engine
        /// </summary>
        EngineStatus Status { get; }

        /// <summary>
        /// Gets the number of axes supported by this engine
        /// </summary>
        int AxisCount { get; }

        /// <summary>
        /// Gets the current position of all axes
        /// </summary>
        double[] CurrentPosition { get; }

        /// <summary>
        /// Gets the current velocity of all axes
        /// </summary>
        double[] CurrentVelocity { get; }

        /// <summary>
        /// Initializes the kinematic engine with the specified configuration
        /// </summary>
        /// <param name="config">Engine configuration parameters</param>
        /// <returns>True if initialization was successful</returns>
        Task<bool> InitializeAsync(EngineConfiguration config);

        /// <summary>
        /// Starts the kinematic engine
        /// </summary>
        /// <returns>True if startup was successful</returns>
        Task<bool> StartAsync();

        /// <summary>
        /// Stops the kinematic engine
        /// </summary>
        /// <returns>True if shutdown was successful</returns>
        Task<bool> StopAsync();

        /// <summary>
        /// Processes a motion command
        /// </summary>
        /// <param name="command">The motion command to process</param>
        /// <returns>Command execution result</returns>
        Task<CommandResult> ProcessCommandAsync(MotionCommand command);

        /// <summary>
        /// Gets the current buffer status
        /// </summary>
        /// <returns>Buffer status information</returns>
        BufferStatus GetBufferStatus();

        /// <summary>
        /// Gets the current motion profile
        /// </summary>
        /// <returns>Motion profile information</returns>
        MotionProfile GetMotionProfile();

        /// <summary>
        /// Checks if the engine is ready to accept new commands
        /// </summary>
        /// <returns>True if ready</returns>
        bool IsReady();

        /// <summary>
        /// Emergency stop - immediately halts all motion
        /// </summary>
        void EmergencyStop();

        /// <summary>
        /// Resets the engine to a safe state
        /// </summary>
        void Reset();

        /// <summary>
        /// Manually resets the engine after buffer closure
        /// </summary>
        void ManualReset();
    }

    /// <summary>
    /// Represents the current status of the kinematic engine
    /// </summary>
    public enum EngineStatus
    {
        Uninitialized,
        Initializing,
        Ready,
        Running,
        Paused,
        Error,
        Stopping,
        Stopped,
        BufferClosed  // New state for controlled buffer shutdown
    }

    /// <summary>
    /// Configuration parameters for the kinematic engine
    /// </summary>
    public class EngineConfiguration
    {
        public int AxisCount { get; set; } = 6;
        public double[] MaxVelocities { get; set; } = new double[8];
        public double[] MaxAccelerations { get; set; } = new double[8];
        public double[] MaxJerks { get; set; } = new double[8];
        public double[] SoftLimitsPositive { get; set; } = new double[8];
        public double[] SoftLimitsNegative { get; set; } = new double[8];
        public double[] CountsPerUnit { get; set; } = new double[8];
        public bool EnableSoftLimits { get; set; } = true;
        public bool EnableHardwareLimits { get; set; } = true;
        public double DefaultFeedRate { get; set; } = 100.0;
        public double DefaultAcceleration { get; set; } = 100.0;
        public double DefaultJerk { get; set; } = 1000.0;
        public double BufferTargetTime { get; set; } = 0.2;
        public double BufferMinTime { get; set; } = 0.05;
        public double BufferMaxTime { get; set; } = 0.5;
        public int BufferSafetyMargin { get; set; } = 2;  // Minimum segments before starvation
    }

    /// <summary>
    /// Represents a motion command to be executed by the kinematic engine
    /// </summary>
    public class MotionCommand
    {
        public int SequenceNumber { get; set; }
        public MotionType Type { get; set; }
        public double[] StartPosition { get; set; } = new double[8];
        public double[] EndPosition { get; set; } = new double[8];
        public double FeedRate { get; set; }
        public double Acceleration { get; set; }
        public double Jerk { get; set; }
        public double[] ArcCenter { get; set; } = new double[2]; // For arc motions
        public bool IsClockwise { get; set; } // For arc motions
        public double DwellTime { get; set; } // For dwell commands
        public string? Comment { get; set; }
        public int CoordinateSystem { get; set; } = 1; // 0=Machine (G53), 1=G54, 2=G55, etc.
        public bool UseMachineCoordinates { get; set; } = false; // True for G53, false for work coordinates
    }

    /// <summary>
    /// Types of motion commands supported by the kinematic engine
    /// </summary>
    public enum MotionType
    {
        Linear,
        Arc,
        Rapid,
        Dwell,
        Home,
        Reference
    }

    /// <summary>
    /// Result of a command execution
    /// </summary>
    public class CommandResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int CommandsInBuffer { get; set; }
        public double EstimatedDuration { get; set; }
        public double[] FinalPosition { get; set; } = new double[8];
    }

    /// <summary>
    /// Status information about the command buffer
    /// </summary>
    public class BufferStatus
    {
        public double TotalBufferTime { get; set; }
        public int CommandsInBuffer { get; set; }
        public int CommandsCompleted { get; set; }
        public double AverageCommandDuration { get; set; }
        public bool IsBufferHealthy { get; set; }
        public double BufferUtilization { get; set; }
        public double EstimatedTimeToEmpty { get; set; }
    }

    /// <summary>
    /// Current motion profile information
    /// </summary>
    public class MotionProfile
    {
        public double CurrentTime { get; set; }
        public double[] CurrentPosition { get; set; } = new double[8];
        public double[] CurrentVelocity { get; set; } = new double[8];
        public double[] CurrentAcceleration { get; set; } = new double[8];
        public BufferStatus BufferStatus { get; set; } = new BufferStatus();
        public MotionCommand[] RecentCommands { get; set; } = new MotionCommand[0];
    }
} 