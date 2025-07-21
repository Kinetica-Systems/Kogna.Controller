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
    // Laser-wire additive parameters
    public double LaserPower { get; set; }  // Watts
    public double WireFeedRate { get; set; } // mm/s
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
    /// <summary>Unique identifier for this segment</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    
    /// <summary>Sequence number in the motion plan</summary>
    public int SequenceNumber { get; set; }
    
    /// <summary>Type of motion segment</summary>
    public MotionType Type { get; set; }
    
    /// <summary>Start position of all axes (mm or deg)</summary>
    public double[] StartPosition { get; set; } = new double[8];
    
    /// <summary>Target end position of all axes (mm or deg)</summary>
    public double[] EndPosition { get; set; } = new double[8];
    
    /// <summary>Feed rate in mm/s or deg/s</summary>
    public double FeedRate { get; set; }
    
    /// <summary>Acceleration in mm/s² or deg/s²</summary>
    public double Acceleration { get; set; }
    
    /// <summary>Jerk in mm/s³ or deg/s³</summary>
    public double Jerk { get; set; }
    
    /// <summary>Center point for arc motions (X,Y)</summary>
    public double[] ArcCenter { get; set; } = new double[2];
    
    /// <summary>True for clockwise arc, false for counter-clockwise</summary>
    public bool IsClockwise { get; set; }
    
    /// <summary>Dwell time in seconds (for Dwell segments)</summary>
    public double DwellTime { get; set; }
    
    /// <summary>Planned duration of the segment in seconds</summary>
    public double Duration { get; set; }
    
    /// <summary>Velocity profile for the segment (optional)</summary>
    public double[] VelocityProfile { get; set; } = Array.Empty<double>();
    
    /// <summary>True if segment has completed execution</summary>
    public bool IsCompleted { get; set; }
    
    /// <summary>System time when segment execution completed (UTC)</summary>
    public DateTime? CompletionTime { get; set; }
    
    /// <summary>System time when segment execution started (UTC)</summary>
    public DateTime? StartTime { get; set; }
    
    /// <summary>Kogna's execution time when segment started (seconds since power-on)</summary>
    public double? KognaStartTime { get; set; }
    
    /// <summary>Correlation ID for matching with sensor data</summary>
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");
    
    /// <summary>Sensor measurements associated with this segment</summary>
    public List<SensorMeasurement> SensorMeasurements { get; set; } = new();
    
    /// <summary>Estimated time when this segment will start executing (UTC)</summary>
    public DateTime? EstimatedStartTime { get; set; }
    
    /// <summary>Estimated time when this segment will complete (UTC)</summary>
    public DateTime? EstimatedEndTime { get; set; }
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
    /// <summary>Total time of buffered motion segments in seconds</summary>
    public double TotalBufferTime { get; set; }
    
    /// <summary>Number of segments currently in the queue</summary>
    public int CommandsInBuffer { get; set; }
    
    /// <summary>Number of segments that have been completed</summary>
    public int CommandsCompleted { get; set; }
    
    /// <summary>Average duration of motion segments in seconds</summary>
    public double AverageCommandDuration { get; set; }
    
    /// <summary>Indicates if buffer has sufficient data (above minimum threshold)</summary>
    public bool IsBufferHealthy { get; set; }
    
    /// <summary>Buffer fill percentage (0-1)</summary>
    public double BufferUtilization { get; set; }
    
    /// <summary>Estimated time until buffer is empty at current rate</summary>
    public double EstimatedTimeToEmpty { get; set; }
    
    /// <summary>Target buffer time in seconds (typically 0.2s for 200ms)</summary>
    public double TargetBufferTime { get; set; } = 0.2;
    
    /// <summary>Current segment being executed</summary>
    public MotionSegment? CurrentSegment { get; set; }
    
    /// <summary>Last N completed segments for analysis</summary>
    public List<MotionSegment> RecentCompleted { get; set; } = new();
    
    /// <summary>When the current segment started executing</summary>
    public DateTime? CurrentSegmentStartTime { get; set; }
    
    /// <summary>Kogna's reported execution time in seconds with high precision</summary>
    public double KognaExecTime { get; set; }
    
    /// <summary>Time difference between system and Kogna clock (system - Kogna)</summary>
    public double ClockOffset { get; set; }
}

/// <summary>
/// Current motion profile information with timing and synchronization data
/// </summary>
public class MotionProfile
{
    /// <summary>Current time in the motion profile (seconds)</summary>
    public double CurrentTime { get; set; }
    
    /// <summary>Current position of all axes (mm or deg)</summary>
    public double[] CurrentPosition { get; set; } = new double[8];
    
    /// <summary>Current velocity of all axes (mm/s or deg/s)</summary>
    public double[] CurrentVelocity { get; set; } = new double[8];
    
    /// <summary>Current acceleration of all axes (mm/s² or deg/s²)</summary>
    public double[] CurrentAcceleration { get; set; } = new double[8];
    
    /// <summary>Detailed buffer and timing status</summary>
    public BufferStatus BufferStatus { get; set; } = new BufferStatus();
    
    /// <summary>Recent commands for debugging and visualization</summary>
    public MotionCommand[] RecentCommands { get; set; } = Array.Empty<MotionCommand>();
    
    /// <summary>Timestamp when this profile was generated</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    /// <summary>Correlation ID for matching with sensor data</summary>
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
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

/// <summary>
/// Target bead profile for laser wire additive manufacturing
/// </summary>
public class BeadProfile
{
    public double TargetWidth { get; set; } = 2.0; // mm
    public double TargetHeight { get; set; } = 1.5; // mm
    public double TargetEnergyPerUnitLength { get; set; } = 100.0; // J/mm
    public double WireDiameter { get; set; } = 1.2; // mm
    public double WireFeedRate { get; set; } = 50.0; // mm/s
    public double LaserPower { get; set; } = 500.0; // W
    public double LaserSpeed { get; set; } = 10.0; // mm/s
    public string Name { get; set; } = "Default";
    public string Description { get; set; } = "";
}

/// <summary>
/// Represents a sensor measurement with timestamp and correlation data
/// </summary>
public class SensorMeasurement
{
    /// <summary>Unique identifier for this measurement</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    
    /// <summary>Type of sensor (e.g., "temperature", "force", "vision")</summary>
    public string SensorType { get; set; } = string.Empty;
    
    /// <summary>Timestamp when measurement was taken (UTC)</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    /// <summary>Kogna's execution time when measurement was taken (seconds since power-on)</summary>
    public double KognaTime { get; set; }
    
    /// <summary>Measurement values (sensor-specific)</summary>
    public double[] Values { get; set; } = Array.Empty<double>();
    
    /// <summary>Optional metadata or tags</summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
    
    /// <summary>Correlation ID to match with motion segments</summary>
    public string CorrelationId { get; set; } = string.Empty;
    
    /// <summary>Motion segment ID this measurement is associated with</summary>
    public string? SegmentId { get; set; }
}

/// <summary>
/// Real-time bead measurement from vision system
/// </summary>
public class BeadMeasurement
{
    public double MeasuredWidth { get; set; }
    public double MeasuredHeight { get; set; }
    public double MeasuredEnergy { get; set; }
    public double PoolTemperature { get; set; } // proxy from camera
    public double PoolSize { get; set; } // mm²
    public DateTime Timestamp { get; set; }
    public int LayerIndex { get; set; }
    public double ZPosition { get; set; }
    public double XPosition { get; set; }
    public double YPosition { get; set; }
}

/// <summary>
/// Deviation analysis between target and measured bead
/// </summary>
public class BeadDeviation
{
    public double WidthError { get; set; }
    public double HeightError { get; set; }
    public double EnergyError { get; set; }
    public double TotalError { get; set; }
    public bool RequiresCorrection { get; set; }
    public string CorrectionType { get; set; } = ""; // "thicken", "thin", "reslice"
    public BeadMeasurement Measurement { get; set; } = new();
    public BeadProfile TargetProfile { get; set; } = new();
}

/// <summary>
/// Deposition state for closed-loop control
/// </summary>
public class DepositionState
{
    public BeadProfile CurrentProfile { get; set; } = new();
    public List<BeadMeasurement> RecentMeasurements { get; set; } = new();
    public List<BeadDeviation> Deviations { get; set; } = new();
    public double AverageWidthError { get; set; }
    public double AverageHeightError { get; set; }
    public bool IsStable { get; set; }
    public DateTime LastUpdate { get; set; }
    public int CurrentLayer { get; set; }
    public double CurrentZ { get; set; }
}

/// <summary>
/// Batch of layers for adaptive slicing
/// </summary>
public class LayerBatch
{
    public int BatchIndex { get; set; }
    public List<Layer> Layers { get; set; } = new();
    public double StartZ { get; set; }
    public double EndZ { get; set; }
    public int LayerCount { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// Slice manager configuration
/// </summary>
public class SliceConfig
{
    public double LayerHeight { get; set; } = 0.2;
    public int BatchSize { get; set; } = 5; // layers per batch
    public double MaxBatchHeight { get; set; } = 2.0; // mm
    public bool EnableAdaptiveSlicing { get; set; } = true;
    public double DeviationThreshold { get; set; } = 0.5; // mm
    public int MaxCorrectionLayers { get; set; } = 3;
}

/// <summary>
/// Result of batch slicing operation
/// </summary>
public class SliceResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public LayerBatch? Batch { get; set; }
    public bool HasMoreLayers { get; set; }
    public double RemainingHeight { get; set; }
    public int TotalLayersSliced { get; set; }
} 