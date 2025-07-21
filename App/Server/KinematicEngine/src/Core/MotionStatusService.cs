using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SharedTypes;
using TCPServer;

namespace KinematicEngine.Core
{
    /// <summary>
    /// Implementation of IMotionStatusService that provides real-time status of the motion system
    /// </summary>
    public class MotionStatusService : IMotionStatusService, IDisposable
    {
        private readonly ILogger<MotionStatusService> _logger;
        private readonly MotionPlanner _motionPlanner;
        private readonly IKognaIO _kognaIO;
        private readonly ConcurrentDictionary<string, MotionSegment> _segmentCache = new();
        private readonly ConcurrentDictionary<string, List<SensorMeasurement>> _sensorMeasurements = new();
        private readonly Subject<MotionStatusUpdate> _statusUpdates = new();
        private readonly object _updateLock = new();
        private MotionSystemState _systemState = MotionSystemState.Initializing;
        private readonly Timer _statusUpdateTimer;
        private bool _disposed;
        private const int StatusUpdateIntervalMs = 50; // 20Hz update rate

        public MotionStatusService(ILogger<MotionStatusService> logger, MotionPlanner motionPlanner, IKognaIO kognaIO)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _motionPlanner = motionPlanner ?? throw new ArgumentNullException(nameof(motionPlanner));
            _kognaIO = kognaIO ?? throw new ArgumentNullException(nameof(kognaIO));
            
            // Set up periodic status updates
            _statusUpdateTimer = new Timer(OnStatusUpdateTimer, null, 
                TimeSpan.FromMilliseconds(StatusUpdateIntervalMs), 
                TimeSpan.FromMilliseconds(StatusUpdateIntervalMs));
        }

        public BufferStatus GetBufferStatus()
        {
            // Get a snapshot of the current buffer status
            var status = _motionPlanner.GetBufferStatus();
            
            // Add additional calculated fields
            status.BufferUtilization = Math.Min(1.0, status.TotalBufferTime / status.TargetBufferTime);
            status.IsBufferHealthy = status.TotalBufferTime > (status.TargetBufferTime * 0.5); // Healthy if > 50% of target
            
            // Calculate estimated time to empty based on average command duration
            if (status.AverageCommandDuration > 0)
            {
                status.EstimatedTimeToEmpty = status.TotalBufferTime;
            }
            
            return status;
        }

        public MotionProfile GetMotionProfile()
        {
            var status = GetBufferStatus();
            var currentSegment = GetCurrentSegment();
            
            // Get current position and velocity from Kogna (simplified)
            double[] currentPosition = GetCurrentPosition();
            double[] currentVelocity = GetCurrentVelocity();
            double[] currentAcceleration = GetCurrentAcceleration();
            
            return new MotionProfile
            {
                CurrentTime = _motionPlanner.GetCurrentKognaTime(),
                CurrentPosition = currentPosition,
                CurrentVelocity = currentVelocity,
                CurrentAcceleration = currentAcceleration,
                BufferStatus = status,
                Timestamp = DateTime.UtcNow,
                CorrelationId = currentSegment?.CorrelationId ?? Guid.NewGuid().ToString()
            };
        }

        public MotionSegment? GetCurrentSegment()
        {
            // Get the current segment from the motion planner
            return _motionPlanner.CurrentSegment;
        }

        public IReadOnlyList<MotionSegment> GetUpcomingSegments(int count = 5)
        {
            // Get upcoming segments from the motion planner
            return _motionPlanner.SegmentQueue.Take(count).ToList().AsReadOnly();
        }

        public IReadOnlyList<MotionSegment> GetRecentCompletedSegments(int count = 10)
        {
            // Get recently completed segments from the motion planner
            return _motionPlanner.CompletedSegments
                .OrderByDescending(s => s.CompletionTime)
                .Take(count)
                .ToList()
                .AsReadOnly();
        }

        public IReadOnlyList<SensorMeasurement> GetSensorMeasurements(string segmentId)
        {
            if (_sensorMeasurements.TryGetValue(segmentId, out var measurements))
            {
                return measurements.AsReadOnly();
            }
            return Array.Empty<SensorMeasurement>();
        }

        public IReadOnlyList<SensorMeasurement> GetRecentSensorMeasurements(string? sensorType = null, int count = 100)
        {
            IEnumerable<SensorMeasurement> query = _sensorMeasurements.Values
                .SelectMany(x => x);
                
            if (!string.IsNullOrEmpty(sensorType))
            {
                query = query.Where(m => string.Equals(m.SensorType, sensorType, StringComparison.OrdinalIgnoreCase));
            }
            
            // Convert to list first to avoid multiple enumeration
            var result = query.ToList();
            
            // Order by timestamp and take the requested count
            return result
                .OrderByDescending(m => m.Timestamp)
                .Take(count)
                .ToList()
                .AsReadOnly();
        }

        public MotionSegment? GetSegmentById(string segmentId)
        {
            // First check the current segment
            var current = GetCurrentSegment();
            if (current?.Id == segmentId)
                return current;
                
            // Check upcoming segments
            var upcoming = GetUpcomingSegments(int.MaxValue).FirstOrDefault(s => s.Id == segmentId);
            if (upcoming != null)
                return upcoming;
                
            // Check completed segments
            return GetRecentCompletedSegments(int.MaxValue).FirstOrDefault(s => s.Id == segmentId);
        }

        public double[] GetCurrentPosition()
        {
            // Get current position from Kogna (simplified - would use actual Kogna IO)
            try
            {
                // TODO: Replace with actual Kogna position query
                return new double[8]; // Return zeros for now
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current position from Kogna");
                return new double[8];
            }
            
            return new double[6]; // Default to zeros if reading fails
        }

        public double[] GetCurrentVelocity()
        {
            try
            {
                string response;
                int result = _kognaIO.WriteLineReadLine(0, "GETVEL", out response);
                
                if (result == 0 && !string.IsNullOrEmpty(response))
                {
                    var values = response.Split(',')
                        .Select(s => double.TryParse(s, out var value) ? value : 0.0)
                        .ToArray();
                    
                    return values.Length >= 6 ? values.Take(6).ToArray() : new double[6];
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading current velocity from Kogna");
            }
            
            return new double[6]; // Default to zeros if reading fails
        }

        public double[] GetCurrentAcceleration()
        {
            try
            {
                string response;
                int result = _kognaIO.WriteLineReadLine(0, "GETACCEL", out response);
                
                if (result == 0 && !string.IsNullOrEmpty(response))
                {
                    var values = response.Split(',')
                        .Select(s => double.TryParse(s, out var value) ? value : 0.0)
                        .ToArray();
                    
                    return values.Length >= 6 ? values.Take(6).ToArray() : new double[6];
                }
                
                // Return zero acceleration if we couldn't read from Kogna
                return new double[6];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading current acceleration from Kogna");
                return new double[6];
            }
        }

        public MotionSystemState GetSystemState()
        {
            return _systemState;
        }

        public IDisposable SubscribeToUpdates(Action<MotionStatusUpdate> callback)
        {
            return _statusUpdates.Subscribe(callback);
        }

        /// <summary>
        /// Updates the system state and notifies subscribers
        /// </summary>
        public void UpdateSystemState(MotionSystemState newState, string? message = null)
        {
            if (_systemState != newState)
            {
                _logger.LogInformation("System state changing from {OldState} to {NewState}: {Message}", 
                    _systemState, newState, message ?? "No additional information");
                    
                _systemState = newState;
                
                // Notify subscribers of state change
                _statusUpdates.OnNext(new MotionStatusUpdate
                {
                    UpdateType = MotionUpdateType.StateChange,
                    SystemState = newState,
                    Timestamp = DateTime.UtcNow,
                    Error = message
                });
            }
        }

        /// <summary>
        /// Records a sensor measurement and associates it with a motion segment
        /// </summary>
        public void RecordSensorMeasurement(SensorMeasurement measurement)
        {
            if (string.IsNullOrEmpty(measurement.SegmentId))
            {
                // Try to associate with current segment if no segment ID is provided
                var currentSegment = GetCurrentSegment();
                if (currentSegment != null)
                {
                    measurement.SegmentId = currentSegment.Id;
                    measurement.CorrelationId = currentSegment.CorrelationId;
                }
            }
            
            // Store the measurement
            var measurements = _sensorMeasurements.GetOrAdd(measurement.SegmentId ?? "unknown", _ => new List<SensorMeasurement>());
            lock (measurements)
            {
                measurements.Add(measurement);
            }
            
            // Notify subscribers of new sensor data
            _statusUpdates.OnNext(new MotionStatusUpdate
            {
                UpdateType = MotionUpdateType.SensorUpdate,
                SensorData = new[] { measurement },
                SystemState = _systemState,
                Timestamp = DateTime.UtcNow
            });
        }

        private void OnStatusUpdateTimer(object? state)
        {
            try
            {
                // Skip if disposed
                if (_disposed)
                    return;
                    
                // Get the current status
                var status = GetBufferStatus();
                var profile = GetMotionProfile();
                var currentSegment = GetCurrentSegment();
                
                // Send a status update
                _statusUpdates.OnNext(new MotionStatusUpdate
                {
                    UpdateType = MotionUpdateType.FullUpdate,
                    Profile = profile,
                    BufferStatus = status,
                    Segment = currentSegment,
                    SystemState = _systemState,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in status update timer");
            }
        }

        /// <summary>
        /// Updates the system state and notifies subscribers
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _statusUpdateTimer?.Dispose();
                _statusUpdates.OnCompleted();
                _statusUpdates.Dispose();
                
                GC.SuppressFinalize(this);
            }
        }
    }
}
