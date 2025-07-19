using System;
using System.Threading.Tasks;
using SharedTypes;

namespace ProcessEngine.Core
{
    /// <summary>
    /// Core interface for the ProcessEngine, responsible for data capture and processing
    /// for the Geometry engine to make in-process changes.
    /// </summary>
    public interface IProcessEngine : IDisposable
    {
        /// <summary>
        /// Gets the current status of the process engine
        /// </summary>
        ProcessEngineStatus Status { get; }

        /// <summary>
        /// Initializes the process engine with the specified configuration
        /// </summary>
        /// <param name="config">Process engine configuration parameters</param>
        /// <returns>True if initialization was successful</returns>
        Task<bool> InitializeAsync(ProcessEngineConfiguration config);

        /// <summary>
        /// Starts the process engine and begins data capture
        /// </summary>
        /// <returns>True if startup was successful</returns>
        Task<bool> StartAsync();

        /// <summary>
        /// Stops the process engine and data capture
        /// </summary>
        /// <returns>True if shutdown was successful</returns>
        Task<bool> StopAsync();

        /// <summary>
        /// Processes the current geometry data and applies any necessary in-process changes
        /// </summary>
        /// <param name="geometryData">The geometry data to process</param>
        /// <returns>Processed geometry data with any modifications</returns>
        Task<GeometryData> ProcessGeometryAsync(GeometryData geometryData);

        /// <summary>
        /// Captures the current state of the geometry processing
        /// </summary>
        /// <returns>A snapshot of the current processing state</returns>
        Task<ProcessState> CaptureProcessStateAsync();

        /// <summary>
        /// Applies a set of processing parameters to the engine
        /// </summary>
        /// <param name="parameters">Parameters to apply</param>
        /// <returns>True if parameters were applied successfully</returns>
        Task<bool> ApplyProcessingParametersAsync(ProcessingParameters parameters);
    }

    /// <summary>
    /// Represents the status of the process engine
    /// </summary>
    public enum ProcessEngineStatus
    {
        /// <summary>Engine is not initialized</summary>
        Uninitialized,
        
        /// <summary>Engine is initialized but not started</summary>
        Initialized,
        
        /// <summary>Engine is running and processing data</summary>
        Running,
        
        /// <summary>Engine is paused</summary>
        Paused,
        
        /// <summary>Engine encountered an error</summary>
        Error,
        
        /// <summary>Engine is being disposed</summary>
        Disposed
    }
}
