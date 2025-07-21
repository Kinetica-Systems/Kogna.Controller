using System;
using System.Threading.Tasks;
using System.Numerics;
using KinematicEngine.Core;
using KinematicEngine.Kinematics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using TCPServer;
using SharedTypes;

namespace KinematicEngine.Core;

/// <summary>
/// Refactored kinematic engine that implements the new architecture for robot motion control.
/// This class provides a high-level interface for motion planning, kinematics calculations,
/// and hardware communication.
/// </summary>
public class RefactoredKinematicEngine : IKinematicEngine, IDisposable
{
    private readonly MotionPlanner _motionPlanner;
    private readonly IKinematics _kinematics;
    private readonly KognaMotion _kognaMotion;
    private readonly IKognaIO _kognaIO;
    private readonly CoordinateSystemManager _coordinateSystemManager;
    private readonly ILogger<RefactoredKinematicEngine> _logger;
    private readonly ILogger<MotionPlanner> _motionPlannerLogger;
    private readonly MotionStatusService _motionStatusService;
    private readonly object _engineLock = new object();
    
    private EngineConfiguration _config = null!;
    private EngineStatus _status = EngineStatus.Uninitialized;
    private double[] _currentPosition = new double[8];
    private double[] _currentVelocity = new double[8];
    private bool _disposed;

    /// <summary>
    /// Gets the current status of the kinematic engine.
    /// </summary>
    public EngineStatus Status => _status;

    /// <summary>
    /// Gets the number of axes configured in the engine.
    /// </summary>
    public int AxisCount => _config?.AxisCount ?? 0;

    /// <summary>
    /// Gets the current position of all axes.
    /// </summary>
    /// <returns>An array containing the current position of each axis in their respective units (mm or degrees).</returns>
    public double[] CurrentPosition => (double[])_currentPosition.Clone();

    /// <summary>
    /// Gets the current velocity of all axes.
    /// </summary>
    /// <returns>An array containing the current velocity of each axis in their respective units (mm/s or degrees/s).</returns>
    public double[] CurrentVelocity => (double[])_currentVelocity.Clone();

    /// <summary>
    /// Gets the coordinate system manager that handles different coordinate frames.
    /// </summary>
    public CoordinateSystemManager CoordinateSystemManager => _coordinateSystemManager;

    /// <summary>
    /// Gets the current configuration of the kinematic engine.
    /// </summary>
    public EngineConfiguration Configuration => _config;


    /// Initializes a new instance of the <see cref="RefactoredKinematicEngine"/> class.
    /// </summary>
    /// <param name="kognaMotion">The Kogna motion controller instance.</param>
    /// <param name="kognaIO">The Kogna I/O interface for hardware communication.</param>
    /// <param name="kinematics">The kinematics implementation for the robot.</param>
    /// <param name="logger">The logger for the kinematic engine.</param>
    /// <param name="motionPlannerLogger">The logger for the motion planner (optional).</param>
    /// <param name="statusServiceLogger">The logger for the status service (optional).</param>
    /// <exception cref="ArgumentNullException">Thrown when any required parameter is null.</exception>
    public RefactoredKinematicEngine(KognaMotion kognaMotion, IKognaIO kognaIO, IKinematics kinematics, 
        ILogger<RefactoredKinematicEngine> logger, 
        ILogger<MotionPlanner> motionPlannerLogger = null, 
        ILogger<MotionStatusService> statusServiceLogger = null)
    {
        _kognaMotion = kognaMotion ?? throw new ArgumentNullException(nameof(kognaMotion));
        _kognaIO = kognaIO ?? throw new ArgumentNullException(nameof(kognaIO));
        _kinematics = kinematics ?? throw new ArgumentNullException(nameof(kinematics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Initialize logger for MotionPlanner if not provided
        _motionPlannerLogger = motionPlannerLogger ?? LoggerFactory.Create(builder => 
        {
            builder.AddSimpleConsole(options => 
            {
                options.SingleLine = true;
                options.TimestampFormat = "[HH:mm:ss] ";
            });
        }).CreateLogger<MotionPlanner>();
        
        // Initialize services
        _coordinateSystemManager = new CoordinateSystemManager();
        
        // Create a logger for MotionStatusService if not provided
        var statusLogger = statusServiceLogger ?? LoggerFactory.Create(builder => 
        {
            builder.AddSimpleConsole(options => 
            {
                options.SingleLine = true;
                options.TimestampFormat = "[HH:mm:ss] ";
            });
        }).CreateLogger<MotionStatusService>();
        
        // Initialize motion planner with fallback logger if needed
        _motionPlannerLogger = motionPlannerLogger ?? 
            LoggerFactory.Create(builder => builder.AddSimpleConsole()).CreateLogger<MotionPlanner>();
            
        // Create a temporary status service that won't be used for real operations
        var tempStatusService = new NullMotionStatusService();
        
        try
        {
            // Create the motion planner with the temporary status service
            _motionPlanner = new MotionPlanner(kognaIO, tempStatusService, _motionPlannerLogger);
            
            // Now create the real status service with the motion planner and kognaIO
            _motionStatusService = new MotionStatusService(
                statusLogger ?? LoggerFactory.Create(builder => builder.AddSimpleConsole())
                    .CreateLogger<MotionStatusService>(),
                _motionPlanner,
                kognaIO);
                
            // Update the motion planner with the real status service using reflection
            var motionPlannerType = _motionPlanner.GetType();
            var statusServiceField = motionPlannerType.GetField("_statusService", 
                System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Instance);
                
            if (statusServiceField != null)
            {
                statusServiceField.SetValue(_motionPlanner, _motionStatusService);
            }
            else
            {
                _logger.LogWarning("Could not set status service on motion planner - field not found");
            }
        }
        finally
        {
            // Ensure the temporary status service is disposed
            if (tempStatusService is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    /// <summary>
    /// Initializes the kinematic engine with the specified configuration.
    /// </summary>
    /// <param name="config">Configuration parameters for the engine.</param>
    /// <returns>A task that represents the asynchronous initialization operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when config is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the engine is already initialized or disposed.</exception>
    /// <exception cref="KinematicEngineException">Thrown when initialization fails.</exception>
    public async Task<bool> InitializeAsync(EngineConfiguration config)
    {
        ThrowIfDisposed();

        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        if (_status != EngineStatus.Uninitialized)
        {
            throw new InvalidOperationException("Engine is already initialized");
        }

        try
        {
            lock (_engineLock)
            {
                _config = config;
                _status = EngineStatus.Initialized;
                _currentPosition = new double[config.AxisCount];
                _currentVelocity = new double[config.AxisCount];
            }

            await Task.CompletedTask; // Placeholder for future async initialization
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KINEMATIC_ENGINE] Initialization failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Starts the kinematic engine and begins processing motion commands.
    /// </summary>
    /// <returns>A task that represents the asynchronous start operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the engine is not initialized, already running, or disposed.</exception>
    /// <exception cref="KinematicEngineException">Thrown when startup fails.</exception>
    public async Task<bool> StartAsync()
    {
        ThrowIfDisposed();

        if (_status == EngineStatus.Uninitialized)
        {
            throw new InvalidOperationException("Engine must be initialized before starting");
        }

        if (_status == EngineStatus.Running)
        {
            throw new InvalidOperationException("Engine is already running");
        }

        try
        {
            // Start motion planning and monitoring
            await _motionPlanner.StartAsync();
            _status = EngineStatus.Running;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KINEMATIC_ENGINE] Start failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Stops the kinematic engine and halts all motion.
    /// </summary>
    /// <returns>A task that represents the asynchronous stop operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the engine is not running or is disposed.</exception>
    /// <exception cref="KinematicEngineException">Thrown when shutdown fails.</exception>
    public async Task<bool> StopAsync()
    {
        ThrowIfDisposed();

        if (_status != EngineStatus.Running)
        {
            throw new InvalidOperationException("Engine is not running");
        }

        try
        {
            // Stop motion planning and monitoring
            await _motionPlanner.StopAsync();
            _status = EngineStatus.Stopped;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KINEMATIC_ENGINE] Stop failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Processes a motion command and adds it to the motion queue.
    /// </summary>
    /// <param name="command">The motion command to process.</param>
    /// <returns>A task that represents the asynchronous command processing operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when command is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the engine is not running or is disposed.</exception>
    /// <exception cref="KinematicEngineException">Thrown when command processing fails.</exception>
    public async Task<CommandResult> ProcessCommandAsync(MotionCommand command)
    {
        ThrowIfDisposed();

        if (command == null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        if (_status != EngineStatus.Running)
        {
            throw new InvalidOperationException("Engine must be running to process commands");
        }

        try
        {
            return await _motionPlanner.ProcessCommandAsync(command);
        }
        catch (Exception ex)
        {
            throw new KinematicEngineException("Failed to process motion command", ex);
        }
    }

    /// <summary>
    /// Gets the current buffer status of the motion planner.
    /// </summary>
    /// <returns>The current buffer status containing segment count and available space.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the engine is disposed.</exception>
    public BufferStatus GetBufferStatus()
    {
        ThrowIfDisposed();
        return _motionPlanner.GetBufferStatus();
    }

    /// <summary>
    /// Gets the current motion profile including position, velocity, and acceleration.
    /// </summary>
    /// <returns>The current motion profile.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the engine is disposed.</exception>
    public MotionProfile GetMotionProfile()
    {
        ThrowIfDisposed();
        return new MotionProfile
        {
            CurrentPosition = CurrentPosition,
            CurrentVelocity = CurrentVelocity,
            CurrentAcceleration = new double[AxisCount], // TODO: Implement actual acceleration calculation
            BufferStatus = GetBufferStatus(),
            CurrentTime = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond
        };
    }

    /// <summary>
    /// Checks if the engine is ready to process commands.
    /// </summary>
    /// <returns>True if the engine is initialized and running, false otherwise.</returns>
    public bool IsReady()
    {
        return !_disposed && _status == EngineStatus.Running;
    }

    /// <summary>
    /// Performs a manual reset of the engine (stop planner, clear buffers, reinitialize)
    /// </summary>
    public void ManualReset()
    {
        ThrowIfDisposed();
        lock (_engineLock)
        {
            // Stop planner if running
            _motionPlanner.StopAsync().Wait();
            _motionPlanner.StartAsync().Wait();
            _status = EngineStatus.Initialized;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RefactoredKinematicEngine));
        }
    }

    /// <summary>
    /// Releases the unmanaged resources used by the RefactoredKinematicEngine and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Stop the engine if it's running
                if (_status == EngineStatus.Running)
                {
                    StopAsync().Wait();
                }

                // Dispose managed resources
                (_motionPlanner as IDisposable)?.Dispose();
                (_kinematics as IDisposable)?.Dispose();
            }

            _disposed = true;
        }
    }

    /// <summary>
    /// Releases all resources used by the RefactoredKinematicEngine.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Finalizer that ensures unmanaged resources are cleaned up if the object is not properly disposed.
    /// </summary>
    ~RefactoredKinematicEngine()
    {
        Dispose(false);
    }
}

/// <summary>
/// Custom exception for kinematic engine specific errors.
/// </summary>
public class KinematicEngineException : Exception
{
    /// <summary>
    /// Initializes a new instance of the KinematicEngineException class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public KinematicEngineException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the KinematicEngineException class with a specified error message
    /// and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public KinematicEngineException(string message, Exception innerException) : base(message, innerException) { }
} 