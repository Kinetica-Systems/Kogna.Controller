using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SharedTypes;

namespace KinematicEngine.Core
{
    /// <summary>
    /// A no-op implementation of IMotionStatusService that can be used during initialization
    /// to break circular dependencies.
    /// </summary>
    internal class NullMotionStatusService : IMotionStatusService, IDisposable
    {
        private readonly IObservable<MotionStatusUpdate> _statusUpdates = 
            new System.Reactive.Subjects.BehaviorSubject<MotionStatusUpdate>(
                new MotionStatusUpdate { 
                    SystemState = MotionSystemState.Initializing,
                    UpdateType = MotionUpdateType.FullUpdate,
                    Timestamp = DateTime.UtcNow
                });

        public MotionSystemState SystemState => MotionSystemState.Initializing;
        public string StatusMessage => "Initializing...";
        public IObservable<MotionStatusUpdate> StatusUpdates => _statusUpdates;

        public void AddSensorMeasurement(SensorMeasurement measurement)
        {
            // No-op
        }

        public void Dispose()
        {
            // No-op
        }

        public BufferStatus GetBufferStatus()
        {
            return new BufferStatus
            {
                TotalBufferTime = 0,
                CommandsInBuffer = 0,
                CommandsCompleted = 0,
                AverageCommandDuration = 0,
                EstimatedTimeToEmpty = 0,
                BufferUtilization = 0,
                IsBufferHealthy = false,
                TargetBufferTime = 1.0, // 1 second target
                RecentCompleted = new List<MotionSegment>(),
                KognaExecTime = 0,
                ClockOffset = 0
            };
        }

        public MotionSegment? GetSegment(string segmentId)
        {
            return null;
        }

        public IReadOnlyList<MotionSegment> GetSegments()
        {
            return Array.Empty<MotionSegment>();
        }

        public IReadOnlyList<SensorMeasurement> GetSensorMeasurements(string segmentId)
        {
            return Array.Empty<SensorMeasurement>();
        }

        public void RecordCommandCompletion(CommandResult result)
        {
            // No-op
        }

        public void RecordSegmentCompletion(MotionSegment segment)
        {
            // No-op
        }

        public void RecordSegmentStart(MotionSegment segment)
        {
            // No-op
        }

        public void UpdateSystemState(MotionSystemState newState, string? message = null)
        {
            // No-op
        }

        public MotionProfile GetMotionProfile()
        {
            return new MotionProfile
            {
                CurrentTime = 0,
                CurrentPosition = new double[8],
                CurrentVelocity = new double[8],
                CurrentAcceleration = new double[8],
                RecentCommands = Array.Empty<MotionCommand>()
            };
        }

        public MotionSegment? GetCurrentSegment()
        {
            return null;
        }

        public IReadOnlyList<MotionSegment> GetUpcomingSegments(int count)
        {
            return Array.Empty<MotionSegment>();
        }

        public IReadOnlyList<MotionSegment> GetRecentCompletedSegments(int count)
        {
            return Array.Empty<MotionSegment>();
        }

        public IReadOnlyList<SensorMeasurement> GetRecentSensorMeasurements(string? sensorId, int count)
        {
            return Array.Empty<SensorMeasurement>();
        }

        public MotionSegment? GetSegmentById(string segmentId)
        {
            return null;
        }

        public double[] GetCurrentPosition()
        {
            return new double[8];
        }

        public double[] GetCurrentVelocity()
        {
            return new double[8];
        }

        public double[] GetCurrentAcceleration()
        {
            return new double[8];
        }

        public MotionSystemState GetSystemState()
        {
            return MotionSystemState.Initializing;
        }

        public IDisposable SubscribeToUpdates(Action<MotionStatusUpdate> onStatusUpdate)
        {
            return _statusUpdates.Subscribe(onStatusUpdate);
        }
    }
}
