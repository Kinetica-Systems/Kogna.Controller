using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SharedTypes;
using TCPServer;

namespace KinematicEngine.Core
{
    /// <summary>
    /// Handles motion planning and trajectory generation
    /// </summary>
    public class MotionPlanner : IDisposable
    {
        private const double TARGET_BUFFER_MS = 200.0;  // Target buffer time in milliseconds
        private const int MAX_COMPLETED_SEGMENTS = 100;  // Keep last N completed segments for analysis
        
        private readonly object _plannerLock = new();
        private readonly Queue<MotionSegment> _segmentQueue = new();
        private readonly List<MotionSegment> _completedSegments = new();
        private readonly Stopwatch _segmentTimer = new();
        private readonly Timer _bufferMonitorTimer;
        
        private MotionSegment? _currentSegment;
        private double _totalExecutedTime;
        private bool _isRunning;
        private bool _disposed;
        private double _clockOffset;  // System time - Kogna time
        
        // Kogna buffer management
        private readonly ILogger<MotionPlanner> _logger;
        private readonly IKognaIO _kognaIO;
        private readonly IMotionStatusService _statusService;
        private bool _bufferOpen = false;
        private readonly object _bufferLock = new();
        private readonly CancellationTokenSource _bufferCts = new();
        private Task? _bufferMonitorTask;
        private BufferStatus _bufferStatus = new();

        /// <summary>
        /// Gets the number of pending motion segments in the queue
        /// </summary>
        public int PendingSegmentCount => _segmentQueue.Count;
        
        // Expose internal collections for MotionStatusService
        internal Queue<MotionSegment> SegmentQueue => _segmentQueue;
        internal List<MotionSegment> CompletedSegments => _completedSegments;
        internal MotionSegment? CurrentSegment => _currentSegment;
        
        /// <summary>
        /// Gets the current Kogna time based on the system time and clock offset
        /// </summary>
        internal double GetCurrentKognaTime() => _totalExecutedTime + (_currentSegment != null ? _segmentTimer.Elapsed.TotalSeconds : 0);

        /// <summary>
        /// Monitors the buffer level and ensures it stays within the target range
        /// </summary>
        private async Task MonitorBufferLevelAsync()
        {
            try
            {
                // Skip if we're not running or disposing
                if (!_isRunning || _disposed)
                    return;

                // Get the current buffer status
                var status = GetBufferStatus();
                
                // If buffer is below target, try to add more segments
                if (status.TotalBufferTime < status.TargetBufferTime * 0.8) // 80% of target
                {
                    await ProcessBufferAsync();
                }
                
                // Update the status service with the latest buffer info
                _statusService.UpdateSystemState(
                    _isRunning ? MotionSystemState.Running : MotionSystemState.Idle,
                    $"Buffer: {status.TotalBufferTime:F2}s ({status.CommandsInBuffer} segments)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in MonitorBufferLevelAsync");
            }
        }

        /// <summary>
        /// Starts the motion planner
        /// </summary>
        public MotionPlanner(IKognaIO kognaIO, IMotionStatusService statusService, ILogger<MotionPlanner> logger)
        {
            _kognaIO = kognaIO ?? throw new ArgumentNullException(nameof(kognaIO));
            _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Set up a timer to monitor and maintain the buffer level
            _bufferMonitorTimer = new Timer(
                callback: _ => _ = MonitorBufferLevelAsync(),
                state: null,
                dueTime: TimeSpan.FromMilliseconds(100),  // Start after 100ms
                period: TimeSpan.FromMilliseconds(50));   // Run every 50ms
                
            // Start the buffer monitor task
            _bufferMonitorTask = Task.Run(BufferMonitorLoop, _bufferCts.Token);
            
            // Initialize status service
            _statusService.UpdateSystemState(MotionSystemState.Idle, "Motion planner initialized");
        }

        public async Task StartAsync()
        {
            ThrowIfDisposed();

            // First, check if already running outside the lock for fast path
            if (_isRunning)
            {
                throw new InvalidOperationException("Motion planner is already running");
            }

            // Initialize Kogna buffer before taking the lock
            bool bufferInitialized = false;
            try
            {
                int result = await Task.Run(() => _kognaIO.WriteLine(0, "OPENBUF"));
                if (result == 0)
                {
                    bufferInitialized = true;
                }
                else
                {
                    string error = $"Failed to open Kogna buffer: {result}";
                    _logger.LogError($"Failed to open Kogna buffer: {result}");
                    throw new InvalidOperationException($"Failed to open Kogna buffer: {result}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing Kogna buffer");
                throw new InvalidOperationException("Failed to initialize Kogna buffer", ex);
            }

            // Now take the lock for state updates
            lock (_plannerLock)
            {
                if (_isRunning)
                {
                    // Another thread might have started while we were initializing
                    throw new InvalidOperationException("Motion planner is already running");
                }
                
                _isRunning = true;
                _bufferOpen = bufferInitialized;
                _segmentTimer.Start();
                _statusService.UpdateSystemState(
                    MotionSystemState.Running, 
                    bufferInitialized 
                        ? "Motion planner started with Kogna buffer" 
                        : "Motion planner started but Kogna buffer initialization failed");
                
                UpdateBufferStatus();
            }

            // Initial time synchronization
            await SyncWithKognaTimeAsync();
        }
        
        /// <summary>
        /// Synchronizes the local clock with the Kogna controller's clock
        /// </summary>
        private async Task SyncWithKognaTimeAsync()
        {
            try
            {
                // Get current Kogna time using the EXECTIME command
                string response;
                int result = _kognaIO.WriteLineReadLine(0, "EXECTIME", out response);
                
                if (result == 0 && double.TryParse(response, out double kognaTime))
                {
                    // Calculate offset: system time - Kogna time
                    _clockOffset = (DateTime.UtcNow - new DateTime(2020, 1, 1)).TotalSeconds - kognaTime;
                    _logger.LogInformation($"Synchronized with Kogna time. Offset: {_clockOffset:F6}s");
                }
                else
                {
                    _logger.LogWarning($"Failed to sync with Kogna time. Result: {result}, Response: {response}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error synchronizing with Kogna time");
                // Continue with default offset (0) if synchronization fails
                _clockOffset = 0;
            }
        }

        /// <summary>
        /// Stops the motion planner and clears the segment queue
        /// </summary>
        public async Task StopAsync()
        {
            ThrowIfDisposed();

            _isRunning = false;
            _segmentTimer.Stop();
            _currentSegment = null;
            
            try
            {
                // Flush any remaining commands
                if (_bufferOpen)
                {
                    int flushResult = await Task.Run(() => _kognaIO.WriteLine(0, "FLUSHBUF"));
                    if (flushResult != 0)
                    {
                        _logger.LogError($"FlushBuffer failed during stop: {flushResult}");
                    }
                    
                    // Start execution of any buffered commands
                    int execResult = await Task.Run(() => _kognaIO.WriteLine(0, "EXECBUF"));
                    if (execResult != 0)
                    {
                        _logger.LogError($"ExecBuffer failed during stop: {execResult}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during stop");
                throw;
            }
            finally
            {
                _isRunning = false;
                _segmentQueue.Clear();
                _completedSegments.Clear();
                _totalExecutedTime = 0;
                _statusService.UpdateSystemState(MotionSystemState.Idle, "Motion planner stopped");
            }
            
            // No need to return anything - this is a Task-returning method
            return;
        }

        /// <summary>
        /// Processes a motion command by generating a segment and adding it to the queue
        /// </summary>
        public async Task<CommandResult> ProcessCommandAsync(MotionCommand command)
        {
            ThrowIfDisposed();

            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            if (!_isRunning)
            {
                return new CommandResult
                {
                    Success = false,
                    ErrorMessage = "Motion planner is not running"
                };
            }

            try
            {
                var segment = await Task.Run(() => GenerateSegment(command));
                
                lock (_plannerLock)
                {
                    _segmentQueue.Enqueue(segment);
                    UpdateBufferStatus();
                }

                return new CommandResult
                {
                    Success = true,
                    CommandsInBuffer = _segmentQueue.Count,
                    EstimatedDuration = segment.Duration
                };
            }
            catch (Exception ex)
            {
                return new CommandResult
                {
                    Success = false,
                    ErrorMessage = $"Failed to process command: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Updates the current buffer status
        /// </summary>
        private void UpdateBufferStatus()
        {
            lock (_plannerLock)
            {
                _bufferStatus = new BufferStatus
                {
                    CommandsInBuffer = _segmentQueue.Count,
                    CommandsCompleted = _completedSegments.Count,
                    TotalBufferTime = CalculateTotalBufferTime(),
                    CurrentSegment = _currentSegment,
                    CurrentSegmentStartTime = _currentSegment?.SequenceNumber > 0 ? 
                        DateTime.UtcNow - _segmentTimer.Elapsed : null,
                    KognaExecTime = _totalExecutedTime + (_currentSegment != null ? 
                        _segmentTimer.Elapsed.TotalSeconds : 0),
                    ClockOffset = _clockOffset,
                    TargetBufferTime = TARGET_BUFFER_MS / 1000.0,
                    RecentCompleted = _completedSegments.TakeLast(10).ToList(),
                    IsBufferHealthy = true, // Will be updated below
                    BufferUtilization = 0, // Will be updated below
                    EstimatedTimeToEmpty = 0, // Will be updated below
                    AverageCommandDuration = 0 // Will be updated below
                };

                // Calculate buffer health based on target buffer time
                _bufferStatus.IsBufferHealthy = _bufferStatus.TotalBufferTime >= (TARGET_BUFFER_MS / 1000.0 * 0.8);
                _bufferStatus.BufferUtilization = Math.Min(1.0, 
                    _bufferStatus.TotalBufferTime / (TARGET_BUFFER_MS / 1000.0));
                
                if (_bufferStatus.CommandsInBuffer > 0 && _currentSegment != null)
                {
                    _bufferStatus.EstimatedTimeToEmpty = _bufferStatus.TotalBufferTime;
                    _bufferStatus.AverageCommandDuration = _bufferStatus.TotalBufferTime / 
                        (_bufferStatus.CommandsInBuffer + 1);
                }
            }
        }

        /// <summary>
        /// Gets the current buffer status
        /// </summary>
        /// <returns>The current buffer status</returns>
        public BufferStatus GetBufferStatus()
        {
            ThrowIfDisposed();
            UpdateBufferStatus();
            return _bufferStatus;
        }

        private double CalculateTotalBufferTime()
        {
            double totalTime = 0;
            foreach (var segment in _segmentQueue)
            {
                totalTime += segment.Duration;
            }
            return totalTime;
        }

        private MotionSegment GenerateSegment(MotionCommand command)
        {
            // TODO: Implement actual motion planning logic
            return new MotionSegment
            {
                StartPosition = (double[])command.StartPosition.Clone(),
                EndPosition = (double[])command.EndPosition.Clone(),
                Type = command.Type,
                FeedRate = command.FeedRate,
                Acceleration = command.Acceleration,
                Jerk = command.Jerk,
                Duration = EstimateSegmentDuration(command)
            };
        }

        private double EstimateSegmentDuration(MotionCommand command)
        {
            // Calculate distance for each axis
            double maxDistance = 0;
            for (int i = 0; i < 8; i++)
            {
                double dist = Math.Abs(command.EndPosition[i] - command.StartPosition[i]);
                maxDistance = Math.Max(maxDistance, dist);
            }

            if (maxDistance <= 0)
                return 0;  // No movement

            // Simple model: time = distance / speed + acceleration time
            double accelerationTime = command.FeedRate / command.Acceleration;
            double accelerationDistance = 0.5 * command.Acceleration * accelerationTime * accelerationTime;
            
            if (2 * accelerationDistance >= maxDistance)
            {
                // Triangle profile - never reaches full speed
                return 2 * Math.Sqrt(maxDistance / command.Acceleration);
            }
            else
            {
                // Trapezoid profile - reaches full speed
                double constantDistance = maxDistance - 2 * accelerationDistance;
                return 2 * accelerationTime + (constantDistance / command.FeedRate);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MotionPlanner));
            }
        }

        private async Task BufferMonitorLoop()
        {
            while (!_bufferCts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(10, _bufferCts.Token); // Check every 10ms
                    await ProcessBufferAsync();
                }
                catch (OperationCanceledException)
                {
                    // Shutdown requested
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MOTION_PLANNER] Buffer monitor error: {ex.Message}");
                    await Task.Delay(1000, _bufferCts.Token); // Wait before retrying
                }
            }
        }
        
        private async Task ProcessBufferAsync()
        {
            if (!_isRunning) return;
            
            // Check if we need to send more segments to the Kogna buffer
            if (GetBufferStatus().TotalBufferTime < TARGET_BUFFER_MS / 1000.0 * 0.5) // If buffer is less than 50% full
            {
                await SendNextSegmentToKognaAsync();
            }
        }
        
        private async Task SendNextSegmentToKognaAsync()
        {
            MotionSegment? nextSegment = null;
            
            lock (_plannerLock)
            {
                if (_segmentQueue.Count == 0) return;
                nextSegment = _segmentQueue.Peek();
                
                // Set the estimated start time based on the current Kogna execution time
                double currentKognaTime = GetCurrentKognaTime();
                nextSegment.KognaStartTime = currentKognaTime;
                nextSegment.StartTime = DateTime.UtcNow;
                nextSegment.EstimatedEndTime = nextSegment.StartTime?.AddSeconds(nextSegment.Duration);
                
                // Send the segment to Kogna
                try
                {
                    // Format the motion command for Kogna
                    string command = FormatMotionCommand(nextSegment);
                    int result = _kognaIO.WriteLine(0, command);
                    
                    if (result != 0)
                    {
                        _logger.LogError($"Failed to send motion command to Kogna: {result}");
                        return;
                    }
                    
                    // Move the segment from queue to current
                    _currentSegment = _segmentQueue.Dequeue();
                    _segmentTimer.Restart();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending segment to Kogna");
                }
            }
            
            // Update the current segment tracking
            UpdateBufferStatus();
        }
        
        private string FormatMotionCommand(MotionSegment segment)
        {
            // Format the motion command with precise timing information
            // Command format: Linear X Y Z A B C X1 Y1 Z1 A1 B1 C1 F A J T [CorrelationId]
            return $"Linear " +
                   $"{segment.StartPosition[0]:F4} {segment.StartPosition[1]:F4} {segment.StartPosition[2]:F4} " +
                   $"{segment.StartPosition[3]:F4} {segment.StartPosition[4]:F4} {segment.StartPosition[5]:F4} " +
                   $"{segment.EndPosition[0]:F4} {segment.EndPosition[1]:F4} {segment.EndPosition[2]:F4} " +
                   $"{segment.EndPosition[3]:F4} {segment.EndPosition[4]:F4} {segment.EndPosition[5]:F4} " +
                   $"{segment.FeedRate:F4} {segment.Acceleration:F4} {segment.Jerk:F4} {segment.Duration:F6} " +
                   $"#{segment.CorrelationId}";
        }

        private Task<int> SendSegmentToKognaAsync(MotionSegment segment)
        {
            try
            {
                string cmd = FormatMotionCommand(segment);
                _logger.LogInformation($"[MOTION_PLANNER] Sending segment {segment.SequenceNumber}: {cmd}");
                
                // Send the command to Kogna
                return Task.Run(() => _kognaIO.WriteLine(1, cmd));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SendSegmentToKognaAsync");
                return Task.FromResult(-1); // Return error code
            }
        }
        

        
        /// <summary>
        /// Converts a Kogna time to a system DateTime
        /// </summary>
        private DateTime KognaTimeToDateTime(double kognaTime)
        {
            // SystemTime = KognaTime + ClockOffset
            return DateTime.UnixEpoch.AddSeconds(kognaTime + _clockOffset);
        }
        
        /// <summary>
        /// Releases all resources used by the MotionPlanner.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the MotionPlanner and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Cancel and clean up the buffer monitor
                    _bufferCts.Cancel();
                    _bufferMonitorTask?.GetAwaiter().GetResult(); // Wait for buffer monitor to complete
                    _bufferCts.Dispose();
                    
                    // Close the Kogna buffer if open
                    if (_bufferOpen)
                    {
                        try
                        {
                            _kognaIO.WriteLine(0, "FLUSHBUF");
                            // Note: No explicit close buffer command - ExecBuf starts execution
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error during dispose");
                        }
                    }
                    
                    _bufferMonitorTimer?.Dispose();
                    _segmentTimer.Stop();
                    
                    if (_isRunning)
                    {
                        StopAsync().Wait();
                    }
                }
                _disposed = true;
            }
        }

        /// <summary>
        /// Finalizer that ensures unmanaged resources are cleaned up if the object is not properly disposed.
        /// </summary>
        ~MotionPlanner()
        {
            Dispose(false);
        }
    }
} 