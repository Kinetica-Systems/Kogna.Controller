using System;

namespace ProcessEngine.Core
{
    /// <summary>
    /// Represents the current state of the ProcessEngine
    /// </summary>
    public class ProcessState
    {
        /// <summary>
        /// Gets the timestamp when this state was captured
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Gets the current status of the process engine
        /// </summary>
        public ProcessEngineStatus Status { get; set; }

        /// <summary>
        /// Gets or sets the number of active processing operations
        /// </summary>
        public int ActiveOperations { get; set; }

        /// <summary>
        /// Gets or sets the total number of operations processed
        /// </summary>
        public long TotalOperationsProcessed { get; set; }

        /// <summary>
        /// Gets or sets the current processing rate in operations per second
        /// </summary>
        public double ProcessingRate { get; set; }
    }
}
