using System;

namespace ProcessEngine.Core
{
    /// <summary>
    /// Configuration settings for the ProcessEngine
    /// </summary>
    public class ProcessEngineConfiguration
    {
        /// <summary>
        /// Gets or sets the maximum number of concurrent processing operations
        /// </summary>
        public int MaxConcurrentOperations { get; set; } = 4;

        /// <summary>
        /// Gets or sets the buffer size for processing data
        /// </summary>
        public int ProcessingBufferSize { get; set; } = 8192;

        /// <summary>
        /// Gets or sets a value indicating whether to enable detailed logging
        /// </summary>
        public bool EnableDetailedLogging { get; set; } = false;

        /// <summary>
        /// Gets or sets the processing mode
        /// </summary>
        public ProcessingMode ProcessingMode { get; set; } = ProcessingMode.Standard;
    }

    /// <summary>
    /// Defines the processing modes for the ProcessEngine
    /// </summary>
    public enum ProcessingMode
    {
        /// <summary>Standard processing mode with balanced performance and quality</summary>
        Standard,
        
        /// <summary>High performance mode with optimized processing speed</summary>
        Performance,
        
        /// <summary>High quality mode with more detailed processing</summary>
        Quality
    }
}
