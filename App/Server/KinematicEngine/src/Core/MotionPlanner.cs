using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace KinematicEngine.Core
{
    /// <summary>
    /// Handles trajectory planning and optimization for multi-axis motion
    /// </summary>
    public class MotionPlanner : IDisposable
    {
        private readonly List<MotionSegment> _segments = new List<MotionSegment>();
        private readonly Queue<MotionSegment> _pendingSegments = new Queue<MotionSegment>();
        private readonly object _segmentLock = new object();
        
        private EngineConfiguration _config = null!;
        private bool _disposed = false;

        /// <summary>
        /// Gets the number of pending segments
        /// </summary>
        public int PendingSegmentCount => _pendingSegments.Count;

        /// <summary>
        /// Gets the total planned time
        /// </summary>
        public double TotalPlannedTime { get; private set; }

        /// <summary>
        /// Initializes the motion planner with the given configuration
        /// </summary>
        /// <param name="config">Engine configuration</param>
        public void Initialize(EngineConfiguration config)
        {
            _config = config;
            lock (_segmentLock)
            {
                _segments.Clear();
                _pendingSegments.Clear();
                TotalPlannedTime = 0.0;
            }
        }

        /// <summary>
        /// Plans a motion command and adds it to the trajectory
        /// </summary>
        /// <param name="command">Motion command to plan</param>
        /// <returns>Planning result</returns>
        public PlanningResult PlanMotion(MotionCommand command)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MotionPlanner));

            try
            {
                var segment = CreateMotionSegment(command);
                
                lock (_segmentLock)
                {
                    _segments.Add(segment);
                    _pendingSegments.Enqueue(segment);
                    TotalPlannedTime += segment.Duration;
                }

                return new PlanningResult
                {
                    Success = true,
                    SegmentCount = _segments.Count,
                    EstimatedDuration = segment.Duration
                };
            }
            catch (Exception ex)
            {
                return new PlanningResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Gets the next segment to execute
        /// </summary>
        /// <returns>Next motion segment or null if none available</returns>
        public MotionSegment? GetNextSegment()
        {
            lock (_segmentLock)
            {
                return _pendingSegments.Count > 0 ? _pendingSegments.Dequeue() : null;
            }
        }

        /// <summary>
        /// Optimizes the trajectory for better performance
        /// </summary>
        public void OptimizeTrajectory()
        {
            lock (_segmentLock)
            {
                // Implement trajectory optimization algorithms here
                // This could include:
                // - Velocity profile optimization
                // - Corner smoothing
                // - Look-ahead optimization
                // - Collision avoidance
            }
        }

        /// <summary>
        /// Clears all planned segments
        /// </summary>
        public void Clear()
        {
            lock (_segmentLock)
            {
                _segments.Clear();
                _pendingSegments.Clear();
                TotalPlannedTime = 0.0;
            }
        }

        /// <summary>
        /// Gets the current trajectory status
        /// </summary>
        /// <returns>Trajectory status information</returns>
        public TrajectoryStatus GetStatus()
        {
            lock (_segmentLock)
            {
                return new TrajectoryStatus
                {
                    TotalSegments = _segments.Count,
                    PendingSegments = _pendingSegments.Count,
                    TotalPlannedTime = TotalPlannedTime,
                    IsOptimized = false // TODO: Implement optimization tracking
                };
            }
        }

        private MotionSegment CreateMotionSegment(MotionCommand command)
        {
            var segment = new MotionSegment
            {
                SequenceNumber = command.SequenceNumber,
                Type = command.Type,
                StartPosition = (double[])command.StartPosition.Clone(),
                EndPosition = (double[])command.EndPosition.Clone(),
                FeedRate = command.FeedRate,
                Acceleration = command.Acceleration,
                Jerk = command.Jerk,
                ArcCenter = command.Type == MotionType.Arc ? (double[])command.ArcCenter.Clone() : new double[2],
                IsClockwise = command.IsClockwise,
                DwellTime = command.DwellTime
            };

            // Calculate segment duration based on motion type
            segment.Duration = CalculateSegmentDuration(segment);
            
            // Calculate velocity profile
            segment.VelocityProfile = CalculateVelocityProfile(segment);
            
            return segment;
        }

        private double CalculateSegmentDuration(MotionSegment segment)
        {
            switch (segment.Type)
            {
                case MotionType.Linear:
                    return CalculateLinearDuration(segment);
                case MotionType.Arc:
                    return CalculateArcDuration(segment);
                case MotionType.Rapid:
                    return CalculateRapidDuration(segment);
                case MotionType.Dwell:
                    return segment.DwellTime;
                default:
                    return 0.0;
            }
        }

        private double CalculateLinearDuration(MotionSegment segment)
        {
            // Calculate distance
            double distance = 0.0;
            for (int i = 0; i < segment.StartPosition.Length; i++)
            {
                double delta = segment.EndPosition[i] - segment.StartPosition[i];
                distance += delta * delta;
            }
            distance = Math.Sqrt(distance);

            // Use feed rate to calculate time
            return distance / segment.FeedRate;
        }

        private double CalculateArcDuration(MotionSegment segment)
        {
            // Calculate arc length
            double arcLength = CalculateArcLength(segment);
            return arcLength / segment.FeedRate;
        }

        private double CalculateArcLength(MotionSegment segment)
        {
            // Calculate radius and angle for arc
            double dx = segment.EndPosition[0] - segment.StartPosition[0];
            double dy = segment.EndPosition[1] - segment.StartPosition[1];
            
            double centerX = segment.ArcCenter[0];
            double centerY = segment.ArcCenter[1];
            
            double startAngle = Math.Atan2(segment.StartPosition[1] - centerY, segment.StartPosition[0] - centerX);
            double endAngle = Math.Atan2(segment.EndPosition[1] - centerY, segment.EndPosition[0] - centerX);
            
            double radius = Math.Sqrt((segment.StartPosition[0] - centerX) * (segment.StartPosition[0] - centerX) +
                                    (segment.StartPosition[1] - centerY) * (segment.StartPosition[1] - centerY));
            
            double angleDiff = Math.Abs(endAngle - startAngle);
            if (!segment.IsClockwise && angleDiff < Math.PI)
                angleDiff = 2 * Math.PI - angleDiff;
            else if (segment.IsClockwise && angleDiff > Math.PI)
                angleDiff = 2 * Math.PI - angleDiff;
            
            return radius * angleDiff;
        }

        private double CalculateRapidDuration(MotionSegment segment)
        {
            // Rapid motions use maximum velocity
            double maxVelocity = _config.MaxVelocities[0]; // Use first axis as reference
            return CalculateLinearDuration(segment) * (segment.FeedRate / maxVelocity);
        }

        private double[] CalculateVelocityProfile(MotionSegment segment)
        {
            // Simple trapezoidal velocity profile
            // In a real implementation, this would be more sophisticated
            int steps = 100;
            var profile = new double[steps];
            
            double duration = segment.Duration;
            double maxVelocity = segment.FeedRate;
            
            for (int i = 0; i < steps; i++)
            {
                double t = (double)i / (steps - 1) * duration;
                
                if (t < duration / 3)
                {
                    // Acceleration phase
                    profile[i] = maxVelocity * (3 * t / duration);
                }
                else if (t > 2 * duration / 3)
                {
                    // Deceleration phase
                    profile[i] = maxVelocity * (3 * (duration - t) / duration);
                }
                else
                {
                    // Constant velocity phase
                    profile[i] = maxVelocity;
                }
            }
            
            return profile;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                lock (_segmentLock)
                {
                    _segments.Clear();
                    _pendingSegments.Clear();
                }
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Represents a single motion segment in the trajectory
    /// </summary>
    public class MotionSegment
    {
        public int SequenceNumber { get; set; }
        public MotionType Type { get; set; }
        public double[] StartPosition { get; set; } = new double[8];
        public double[] EndPosition { get; set; } = new double[8];
        public double FeedRate { get; set; }
        public double Acceleration { get; set; }
        public double Jerk { get; set; }
        public double[] ArcCenter { get; set; } = new double[2];
        public bool IsClockwise { get; set; }
        public double DwellTime { get; set; }
        public double Duration { get; set; }
        public double[] VelocityProfile { get; set; } = new double[0];
        public bool IsCompleted { get; set; }
        public double CompletionTime { get; set; }
    }

    /// <summary>
    /// Result of trajectory planning
    /// </summary>
    public class PlanningResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int SegmentCount { get; set; }
        public double EstimatedDuration { get; set; }
    }

    /// <summary>
    /// Status information about the trajectory
    /// </summary>
    public class TrajectoryStatus
    {
        public int TotalSegments { get; set; }
        public int PendingSegments { get; set; }
        public double TotalPlannedTime { get; set; }
        public bool IsOptimized { get; set; }
    }
} 