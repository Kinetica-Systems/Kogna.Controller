using System.Collections.Generic;
using System.Threading.Tasks;
using SharedTypes;

namespace KinematicEngine.Core
{
    /// <summary>
    /// Provides access to motion status information including buffer state, segment tracking, and sensor data
    /// </summary>
    public interface IMotionStatusService
    {
        /// <summary>
        /// Gets the current buffer status
        /// </summary>
        BufferStatus GetBufferStatus();
        
        /// <summary>
        /// Gets detailed information about the current motion profile
        /// </summary>
        MotionProfile GetMotionProfile();
        
        /// <summary>
        /// Gets the current segment being executed
        /// </summary>
        MotionSegment? GetCurrentSegment();
        
        /// <summary>
        /// Gets a list of upcoming segments in the execution queue
        /// </summary>
        /// <param name="count">Maximum number of segments to return</param>
        IReadOnlyList<MotionSegment> GetUpcomingSegments(int count = 5);
        
        /// <summary>
        /// Gets a list of recently completed segments
        /// </summary>
        /// <param name="count">Maximum number of segments to return</param>
        IReadOnlyList<MotionSegment> GetRecentCompletedSegments(int count = 10);
        
        /// <summary>
        /// Gets sensor measurements for a specific segment
        /// </summary>
        /// <param name="segmentId">ID of the segment to get measurements for</param>
        IReadOnlyList<SensorMeasurement> GetSensorMeasurements(string segmentId);
        
        /// <summary>
        /// Gets recent sensor measurements
        /// </summary>
        /// <param name="sensorType">Optional filter by sensor type</param>
        /// <param name="count">Maximum number of measurements to return</param>
        IReadOnlyList<SensorMeasurement> GetRecentSensorMeasurements(string? sensorType = null, int count = 100);
        
        /// <summary>
        /// Gets detailed information about a specific segment
        /// </summary>
        /// <param name="segmentId">ID of the segment to retrieve</param>
        /// <returns>The segment or null if not found</returns>
        MotionSegment? GetSegmentById(string segmentId);
        
        /// <summary>
        /// Gets the current position of all axes
        /// </summary>
        double[] GetCurrentPosition();
        
        /// <summary>
        /// Gets the current velocity of all axes
        /// </summary>
        double[] GetCurrentVelocity();
        
        /// <summary>
        /// Gets the current acceleration of all axes
        /// </summary>
        double[] GetCurrentAcceleration();
        
        /// <summary>
        /// Gets the current state of the motion system
        /// </summary>
        MotionSystemState GetSystemState();
        
        /// <summary>
        /// Updates the system state and notifies subscribers
        /// </summary>
        /// <param name="newState">The new system state</param>
        /// <param name="message">Optional message describing the state change</param>
        void UpdateSystemState(MotionSystemState newState, string? message = null);
        
        /// <summary>
        /// Subscribes to motion status updates
        /// </summary>
        /// <param name="callback">Callback to invoke on status updates</param>
        /// <returns>A subscription token that can be used to unsubscribe</returns>
        System.IDisposable SubscribeToUpdates(System.Action<MotionStatusUpdate> callback);
    }
    
    /// <summary>
    /// Represents the state of the motion system
    /// </summary>
    public enum MotionSystemState
    {
        /// <summary>System is initializing</summary>
        Initializing,
        
        /// <summary>System is idle</summary>
        Idle,
        
        /// <summary>System is homing</summary>
        Homing,
        
        /// <summary>System is running motion commands</summary>
        Running,
        
        /// <summary>System is paused</summary>
        Paused,
        
        /// <summary>System is in error state</summary>
        Error,
        
        /// <summary>System is shutting down</summary>
        ShuttingDown
    }
    
    /// <summary>
    /// Represents an update to the motion status
    /// </summary>
    public class MotionStatusUpdate
    {
        /// <summary>Type of update</summary>
        public MotionUpdateType UpdateType { get; set; }
        
        /// <summary>Timestamp of the update</summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        
        /// <summary>Updated motion profile (if applicable)</summary>
        public MotionProfile? Profile { get; set; }
        
        /// <summary>Updated buffer status (if applicable)</summary>
        public BufferStatus? BufferStatus { get; set; }
        
        /// <summary>Updated segment (if applicable)</summary>
        public MotionSegment? Segment { get; set; }
        
        /// <summary>New sensor measurements (if any)</summary>
        public IReadOnlyList<SensorMeasurement>? SensorData { get; set; }
        
        /// <summary>Current system state</summary>
        public MotionSystemState SystemState { get; set; }
        
        /// <summary>Optional error information</summary>
        public string? Error { get; set; }
    }
    
    /// <summary>
    /// Type of motion status update
    /// </summary>
    public enum MotionUpdateType
    {
        /// <summary>Full status update</summary>
        FullUpdate,
        
        /// <summary>Buffer status changed</summary>
        BufferUpdate,
        
        /// <summary>Current segment changed</summary>
        SegmentUpdate,
        
        /// <summary>New sensor data available</summary>
        SensorUpdate,
        
        /// <summary>System state changed</summary>
        StateChange,
        
        /// <summary>Error occurred</summary>
        Error
    }
}
