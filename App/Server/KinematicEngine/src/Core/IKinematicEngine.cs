using System;
using System.Threading.Tasks;
using SharedTypes;

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
        /// Checks if the engine is ready to accept commands
        /// </summary>
        /// <returns>True if ready</returns>
        bool IsReady();
    }
} 