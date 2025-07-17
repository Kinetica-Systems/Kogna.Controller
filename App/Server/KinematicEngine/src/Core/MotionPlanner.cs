using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SharedTypes;

namespace KinematicEngine.Core
{
    /// <summary>
    /// Handles motion planning and trajectory generation
    /// </summary>
    public class MotionPlanner : IDisposable
    {
        private readonly object _plannerLock = new object();
        private readonly Queue<MotionSegment> _segmentQueue = new Queue<MotionSegment>();
        private bool _isRunning;
        private bool _disposed;

        /// <summary>
        /// Gets the number of pending motion segments in the queue
        /// </summary>
        public int PendingSegmentCount => _segmentQueue.Count;

        /// <summary>
        /// Starts the motion planner
        /// </summary>
        public async Task StartAsync()
        {
            ThrowIfDisposed();

            lock (_plannerLock)
            {
                if (_isRunning)
                {
                    throw new InvalidOperationException("Motion planner is already running");
                }
                _isRunning = true;
            }

            await Task.CompletedTask; // Placeholder for future async initialization
        }

        /// <summary>
        /// Stops the motion planner and clears the segment queue
        /// </summary>
        public async Task StopAsync()
        {
            ThrowIfDisposed();

            lock (_plannerLock)
            {
                if (!_isRunning)
                {
                    throw new InvalidOperationException("Motion planner is not running");
                }
                _isRunning = false;
                _segmentQueue.Clear();
            }

            await Task.CompletedTask; // Placeholder for future async cleanup
        }

        /// <summary>
        /// Processes a motion command and adds it to the queue
        /// </summary>
        /// <param name="command">The motion command to process</param>
        /// <returns>The result of processing the command</returns>
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
        /// Gets the current buffer status
        /// </summary>
        /// <returns>The current buffer status</returns>
        public BufferStatus GetBufferStatus()
        {
            ThrowIfDisposed();

            lock (_plannerLock)
            {
                return new BufferStatus
                {
                    CommandsInBuffer = _segmentQueue.Count,
                    TotalBufferTime = CalculateTotalBufferTime(),
                    IsBufferHealthy = _segmentQueue.Count > 0
                };
            }
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
            // TODO: Implement proper duration calculation based on velocity and acceleration limits
            return 1.0; // Default 1 second duration
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MotionPlanner));
            }
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
                    // Stop the planner if it's running
                    if (_isRunning)
                    {
                        StopAsync().Wait();
                    }

                    lock (_plannerLock)
                    {
                        _segmentQueue.Clear();
                    }
                }

                _disposed = true;
            }
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
        /// Finalizer that ensures unmanaged resources are cleaned up if the object is not properly disposed.
        /// </summary>
        ~MotionPlanner()
        {
            Dispose(false);
        }
    }
} 