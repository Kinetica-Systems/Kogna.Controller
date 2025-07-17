using System;
using System.Collections.Generic;
using System.Numerics;

namespace SharedTypes;

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
    public double ExtrusionRate { get; set; } // For 3D printing: mm³/s of filament to extrude
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
    public List<MotionSegment> Segments { get; set; } = new List<MotionSegment>();
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

/// <summary>
/// Result of command validation
/// </summary>
public class CommandValidationResult
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Configuration for the kinematic engine
/// </summary>
public class EngineConfiguration
{
    public int AxisCount { get; set; }
    public double[] MaxVelocities { get; set; } = new double[8];
    public double[] MaxAccelerations { get; set; } = new double[8];
    public double[] MaxJerks { get; set; } = new double[8];
    public bool EnableSoftLimits { get; set; }
    public int BufferSafetyMargin { get; set; }
    public double[] SoftLimitsPositive { get; set; } = new double[8];
    public double[] SoftLimitsNegative { get; set; } = new double[8];
}

/// <summary>
/// Status of the kinematic engine
/// </summary>
public enum EngineStatus
{
    Uninitialized,
    Initialized,
    Running,
    Stopped,
    Error
} 