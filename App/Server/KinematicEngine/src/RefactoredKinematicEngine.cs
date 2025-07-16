using System;
using System.Threading.Tasks;
using KinematicEngine.Core;
using KinematicEngine.Kinematics;
using TCPServer;

namespace KinematicEngine
{
    /// <summary>
    /// Refactored kinematic engine that implements the new architecture
    /// </summary>
    public class RefactoredKinematicEngine : IKinematicEngine
    {
        private readonly MotionPlanner _motionPlanner;
        private readonly IKinematics _kinematics;
        private readonly KognaMotion _kognaMotion;
        private readonly CoordinateSystemManager _coordinateSystemManager;
        private readonly object _engineLock = new object();
        
        private EngineConfiguration _config = null!;
        private EngineStatus _status = EngineStatus.Uninitialized;
        private double[] _currentPosition = new double[8];
        private double[] _currentVelocity = new double[8];
        private bool _disposed = false;

        public EngineStatus Status => _status;
        public int AxisCount => _config?.AxisCount ?? 0;
        public double[] CurrentPosition => (double[])_currentPosition.Clone();
        public double[] CurrentVelocity => (double[])_currentVelocity.Clone();
        public CoordinateSystemManager CoordinateSystemManager => _coordinateSystemManager;

        /// <summary>
        /// Initializes a new instance of the refactored kinematic engine
        /// </summary>
        /// <param name="kognaMotion">Hardware interface for motion control</param>
        /// <param name="kinematics">Kinematic calculations interface</param>
        public RefactoredKinematicEngine(KognaMotion kognaMotion, IKinematics kinematics)
        {
            _kognaMotion = kognaMotion ?? throw new ArgumentNullException(nameof(kognaMotion));
            _kinematics = kinematics ?? throw new ArgumentNullException(nameof(kinematics));
            _motionPlanner = new MotionPlanner();
            _coordinateSystemManager = new CoordinateSystemManager();
        }

        public async Task<bool> InitializeAsync(EngineConfiguration config)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RefactoredKinematicEngine));

            try
            {
                _status = EngineStatus.Initializing;
                _config = config ?? throw new ArgumentNullException(nameof(config));
                
                // Initialize motion planner
                _motionPlanner.Initialize(config);
                
                // Initialize hardware interface
                await InitializeHardwareAsync();
                
                // Update current position from hardware
                await UpdateCurrentPositionAsync();
                
                _status = EngineStatus.Ready;
                Console.WriteLine("[REFACTORED_ENGINE] Initialized successfully");
                return true;
            }
            catch (Exception ex)
            {
                _status = EngineStatus.Error;
                Console.WriteLine($"[REFACTORED_ENGINE] Initialization failed: {ex.Message}");
                return false;
            }
        }

        public Task<bool> StartAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RefactoredKinematicEngine));

            if (_status != EngineStatus.Ready)
            {
                Console.WriteLine($"[REFACTORED_ENGINE] Cannot start engine in status: {_status}");
                return Task.FromResult(false);
            }

            try
            {
                _status = EngineStatus.Running;
                Console.WriteLine("[REFACTORED_ENGINE] Started successfully");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _status = EngineStatus.Error;
                Console.WriteLine($"[REFACTORED_ENGINE] Failed to start engine: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public Task<bool> StopAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RefactoredKinematicEngine));

            try
            {
                _status = EngineStatus.Stopping;
                
                // Stop all motion
                StopAllMotionAsync().Wait();
                
                _status = EngineStatus.Stopped;
                Console.WriteLine("[REFACTORED_ENGINE] Stopped successfully");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _status = EngineStatus.Error;
                Console.WriteLine($"[REFACTORED_ENGINE] Failed to stop engine: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public async Task<CommandResult> ProcessCommandAsync(MotionCommand command)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RefactoredKinematicEngine));

            if (_status != EngineStatus.Running)
            {
                return new CommandResult
                {
                    Success = false,
                    ErrorMessage = $"Engine not running. Current status: {_status}"
                };
            }

            try
            {
                // Check for buffer starvation before processing new command
                CheckBufferStarvation();

                // Validate command
                var validationResult = ValidateCommand(command);
                if (!validationResult.IsValid)
                {
                    return new CommandResult
                    {
                        Success = false,
                        ErrorMessage = validationResult.ErrorMessage
                    };
                }

                // Convert coordinates if needed
                var convertedCommand = ConvertCoordinates(command);

                // Plan the motion
                var planningResult = _motionPlanner.PlanMotion(convertedCommand);
                if (!planningResult.Success)
                {
                    return new CommandResult
                    {
                        Success = false,
                        ErrorMessage = planningResult.ErrorMessage
                    };
                }

                // Execute the motion
                var executionResult = await ExecuteMotionAsync(convertedCommand);
                if (!executionResult.Success)
                {
                    return executionResult;
                }

                return new CommandResult
                {
                    Success = true,
                    CommandsInBuffer = _motionPlanner.PendingSegmentCount,
                    EstimatedDuration = planningResult.EstimatedDuration,
                    FinalPosition = (double[])convertedCommand.EndPosition.Clone()
                };
            }
            catch (Exception ex)
            {
                return new CommandResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public BufferStatus GetBufferStatus()
        {
            var trajectoryStatus = _motionPlanner.GetStatus();
            
            return new BufferStatus
            {
                TotalBufferTime = trajectoryStatus.TotalPlannedTime,
                CommandsInBuffer = trajectoryStatus.PendingSegments,
                CommandsCompleted = trajectoryStatus.TotalSegments - trajectoryStatus.PendingSegments,
                AverageCommandDuration = trajectoryStatus.TotalSegments > 0 ? 
                    trajectoryStatus.TotalPlannedTime / trajectoryStatus.TotalSegments : 0.0,
                IsBufferHealthy = trajectoryStatus.PendingSegments > 0 && 
                    trajectoryStatus.TotalPlannedTime <= _config.BufferMaxTime,
                BufferUtilization = Math.Min(trajectoryStatus.TotalPlannedTime / _config.BufferTargetTime, 1.0),
                EstimatedTimeToEmpty = trajectoryStatus.TotalPlannedTime
            };
        }

        public MotionProfile GetMotionProfile()
        {
            return new MotionProfile
            {
                CurrentTime = GetCurrentTime(),
                CurrentPosition = CurrentPosition,
                CurrentVelocity = CurrentVelocity,
                CurrentAcceleration = new double[8], // TODO: Calculate actual acceleration
                BufferStatus = GetBufferStatus(),
                RecentCommands = new MotionCommand[0] // TODO: Track recent commands
            };
        }

        public bool IsReady()
        {
            return _status == EngineStatus.Running && !_disposed;
        }

        public void EmergencyStop()
        {
            lock (_engineLock)
            {
                if (_status == EngineStatus.Running)
                {
                    _status = EngineStatus.Error;
                    
                    // Stop hardware motion
                    try
                    {
                        _kognaMotion.SendLinear(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[REFACTORED_ENGINE] Emergency stop hardware command failed: {ex.Message}");
                    }
                    
                    Console.WriteLine("[REFACTORED_ENGINE] Emergency stop executed");
                }
            }
        }

        public void Reset()
        {
            lock (_engineLock)
            {
                _motionPlanner.Clear();
                _status = EngineStatus.Ready;
                Console.WriteLine("[REFACTORED_ENGINE] Engine reset to ready state");
            }
        }

        /// <summary>
        /// Manually resets the engine after buffer closure
        /// </summary>
        public void ManualReset()
        {
            lock (_engineLock)
            {
                if (_status == EngineStatus.BufferClosed)
                {
                    _motionPlanner.Clear();
                    _status = EngineStatus.Ready;
                    Console.WriteLine("[REFACTORED_ENGINE] Manual reset completed. Engine ready for new program.");
                }
                else
                {
                    Console.WriteLine($"[REFACTORED_ENGINE] Manual reset ignored. Current status: {_status}");
                }
            }
        }

        /// <summary>
        /// Checks for buffer starvation and initiates controlled shutdown if needed
        /// </summary>
        private void CheckBufferStarvation()
        {
            lock (_engineLock)
            {
                if (_status != EngineStatus.Running)
                    return;

                int pendingSegments = _motionPlanner.PendingSegmentCount;
                
                // Check if we're approaching the safety margin
                if (pendingSegments <= _config.BufferSafetyMargin)
                {
                    Console.WriteLine($"[REFACTORED_ENGINE] Buffer starvation detected! Pending segments: {pendingSegments}, Safety margin: {_config.BufferSafetyMargin}");
                    
                    // Initiate controlled buffer shutdown
                    InitiateControlledBufferShutdown();
                }
            }
        }

        /// <summary>
        /// Initiates a controlled buffer shutdown by flushing the buffer and setting state
        /// </summary>
        private void InitiateControlledBufferShutdown()
        {
            try
            {
                Console.WriteLine("[REFACTORED_ENGINE] Initiating controlled buffer shutdown...");
                
                // Send FlushBuf command to tell controller this is all the commands
                int flushResult = _kognaMotion.FlushBuffer();
                Console.WriteLine($"[REFACTORED_ENGINE] FlushBuf result: {flushResult}");
                
                // Set status to BufferClosed - requires manual reset
                _status = EngineStatus.BufferClosed;
                
                Console.WriteLine("[REFACTORED_ENGINE] Buffer closed. Manual reset required for next program.");
                Console.WriteLine("[REFACTORED_ENGINE] Controller will complete current motion buffer safely.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[REFACTORED_ENGINE] ERROR during controlled buffer shutdown: {ex.Message}");
                _status = EngineStatus.Error;
            }
        }

        /// <summary>
        /// Converts work coordinates to machine coordinates if needed
        /// </summary>
        /// <param name="command">Original motion command</param>
        /// <returns>Command with machine coordinates</returns>
        private MotionCommand ConvertCoordinates(MotionCommand command)
        {
            var convertedCommand = new MotionCommand
            {
                SequenceNumber = command.SequenceNumber,
                Type = command.Type,
                StartPosition = (double[])command.StartPosition.Clone(),
                EndPosition = (double[])command.EndPosition.Clone(),
                FeedRate = command.FeedRate,
                Acceleration = command.Acceleration,
                Jerk = command.Jerk,
                ArcCenter = (double[])command.ArcCenter.Clone(),
                IsClockwise = command.IsClockwise,
                DwellTime = command.DwellTime,
                Comment = command.Comment,
                CoordinateSystem = command.CoordinateSystem,
                UseMachineCoordinates = command.UseMachineCoordinates
            };

            // If using machine coordinates (G53), no conversion needed
            if (command.UseMachineCoordinates)
            {
                return convertedCommand;
            }

            // Convert work coordinates to machine coordinates
            if (!command.UseMachineCoordinates)
            {
                // Convert start position
                convertedCommand.StartPosition = _coordinateSystemManager.ToMachineCoordinates(command.StartPosition);
                
                // Convert end position
                convertedCommand.EndPosition = _coordinateSystemManager.ToMachineCoordinates(command.EndPosition);
                
                // Convert arc center if this is an arc motion
                if (command.Type == MotionType.Arc)
                {
                    var arcCenterWork = new double[8];
                    arcCenterWork[0] = command.ArcCenter[0];
                    arcCenterWork[1] = command.ArcCenter[1];
                    var arcCenterMachine = _coordinateSystemManager.ToMachineCoordinates(arcCenterWork);
                    convertedCommand.ArcCenter[0] = arcCenterMachine[0];
                    convertedCommand.ArcCenter[1] = arcCenterMachine[1];
                }
            }

            return convertedCommand;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _motionPlanner.Dispose();
                _kinematics.Dispose();
                _disposed = true;
            }
        }

        private Task InitializeHardwareAsync()
        {
            try
            {
                // Get axis definitions from hardware
                _kognaMotion.GetAxisDefinitions();
                
                Console.WriteLine($"[REFACTORED_ENGINE] Hardware initialized with {_kognaMotion.AxisCount} axes");
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to initialize hardware: {ex.Message}", ex);
            }
        }

        private Task UpdateCurrentPositionAsync()
        {
            try
            {
                for (int i = 0; i < _config.AxisCount; i++)
                {
                    _currentPosition[i] = _kognaMotion.GetPosition(i);
                }
                
                Console.WriteLine($"[REFACTORED_ENGINE] Current position updated: [{string.Join(", ", _currentPosition)}]");
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[REFACTORED_ENGINE] Failed to update current position: {ex.Message}");
                return Task.CompletedTask;
            }
        }

        private Task StopAllMotionAsync()
        {
            try
            {
                // Send stop command to hardware
                _kognaMotion.SendLinear(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                Console.WriteLine("[REFACTORED_ENGINE] All motion stopped");
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[REFACTORED_ENGINE] Failed to stop motion: {ex.Message}");
                return Task.CompletedTask;
            }
        }

        private async Task<CommandResult> ExecuteMotionAsync(MotionCommand command)
        {
            try
            {
                Console.WriteLine($"[REFACTORED_ENGINE] Executing command {command.SequenceNumber}: {command.Type}");
                
                switch (command.Type)
                {
                    case MotionType.Linear:
                        return await ExecuteLinearMotionAsync(command);
                    case MotionType.Arc:
                        return await ExecuteArcMotionAsync(command);
                    case MotionType.Rapid:
                        return await ExecuteRapidMotionAsync(command);
                    case MotionType.Dwell:
                        return await ExecuteDwellAsync(command);
                    default:
                        return new CommandResult
                        {
                            Success = false,
                            ErrorMessage = $"Unknown motion type: {command.Type}"
                        };
                }
            }
            catch (Exception ex)
            {
                return new CommandResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private Task<CommandResult> ExecuteLinearMotionAsync(MotionCommand command)
        {
            try
            {
                var result = _kognaMotion.SendLinear(
                    command.StartPosition[0], command.StartPosition[1], command.StartPosition[2],
                    command.StartPosition[3], command.StartPosition[4], command.StartPosition[5],
                    command.EndPosition[0], command.EndPosition[1], command.EndPosition[2],
                    command.EndPosition[3], command.EndPosition[4], command.EndPosition[5],
                    command.FeedRate, command.Acceleration, command.Jerk, 0.0);
                    
                if (result != 0)
                {
                    return Task.FromResult(new CommandResult
                    {
                        Success = false,
                        ErrorMessage = $"Hardware command failed with code: {result}"
                    });
                }

                // Update current position
                _currentPosition = (double[])command.EndPosition.Clone();
                
                return Task.FromResult(new CommandResult { Success = true });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new CommandResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }
        }

        private Task<CommandResult> ExecuteArcMotionAsync(MotionCommand command)
        {
            try
            {
                var result = _kognaMotion.SendArc(
                    command.StartPosition[0], command.StartPosition[1], command.StartPosition[2],
                    command.StartPosition[3], command.StartPosition[4], command.StartPosition[5],
                    command.EndPosition[0], command.EndPosition[1], command.EndPosition[2],
                    command.EndPosition[3], command.EndPosition[4], command.EndPosition[5],
                    command.ArcCenter[0], command.ArcCenter[1], command.IsClockwise,
                    command.FeedRate, command.Acceleration, command.Jerk, 0.0);
                
                if (result != 0)
                {
                    return Task.FromResult(new CommandResult
                    {
                        Success = false,
                        ErrorMessage = $"Hardware arc command failed with code: {result}"
                    });
                }

                // Update current position
                _currentPosition = (double[])command.EndPosition.Clone();
                
                return Task.FromResult(new CommandResult { Success = true });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new CommandResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }
        }

        private async Task<CommandResult> ExecuteRapidMotionAsync(MotionCommand command)
        {
            // Rapid motion is similar to linear but with higher velocity
            return await ExecuteLinearMotionAsync(command);
        }

        private async Task<CommandResult> ExecuteDwellAsync(MotionCommand command)
        {
            try
            {
                await Task.Delay((int)(command.DwellTime * 1000));
                return new CommandResult { Success = true };
            }
            catch (Exception ex)
            {
                return new CommandResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private CommandValidationResult ValidateCommand(MotionCommand command)
        {
            if (command == null)
            {
                return new CommandValidationResult { IsValid = false, ErrorMessage = "Command is null" };
            }

            if (command.StartPosition == null || command.EndPosition == null)
            {
                return new CommandValidationResult { IsValid = false, ErrorMessage = "Position arrays are null" };
            }

            if (command.StartPosition.Length < _config.AxisCount || command.EndPosition.Length < _config.AxisCount)
            {
                return new CommandValidationResult { IsValid = false, ErrorMessage = "Position array length mismatch" };
            }

            if (command.FeedRate <= 0)
            {
                return new CommandValidationResult { IsValid = false, ErrorMessage = "Feed rate must be positive" };
            }

            if (command.Acceleration <= 0)
            {
                return new CommandValidationResult { IsValid = false, ErrorMessage = "Acceleration must be positive" };
            }

            // Check soft limits
            if (_config.EnableSoftLimits)
            {
                for (int i = 0; i < _config.AxisCount; i++)
                {
                    if (command.EndPosition[i] > _config.SoftLimitsPositive[i] || 
                        command.EndPosition[i] < _config.SoftLimitsNegative[i])
                    {
                        return new CommandValidationResult 
                        { 
                            IsValid = false, 
                            ErrorMessage = $"Position {i} ({command.EndPosition[i]}) exceeds soft limits" 
                        };
                    }
                }
            }

            return new CommandValidationResult { IsValid = true };
        }

        private double GetCurrentTime()
        {
            return Environment.TickCount / 1000.0;
        }
    }

    /// <summary>
    /// Result of command validation
    /// </summary>
    public class CommandValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
    }
} 