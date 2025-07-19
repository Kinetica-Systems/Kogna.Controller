using System;
using System.Collections.Generic;

namespace ProcessEngine.Core
{
    /// <summary>
    /// Represents a set of parameters that can be applied to the ProcessEngine
    /// to modify its behavior during geometry processing.
    /// </summary>
    public class ProcessingParameters
    {
        /// <summary>
        /// Gets or sets the processing quality level (0.0 to 1.0)
        /// </summary>
        public double QualityLevel { get; set; } = 0.8;

        /// <summary>
        /// Gets or sets a value indicating whether to enable adaptive processing
        /// </summary>
        public bool EnableAdaptiveProcessing { get; set; } = true;

        /// <summary>
        /// Gets or sets the maximum processing time in milliseconds per operation
        /// </summary>
        public int MaxProcessingTimeMs { get; set; } = 1000;

        /// <summary>
        /// Gets or sets the specific processing features to enable
        /// </summary>
        public ProcessingFeatures EnabledFeatures { get; set; } = ProcessingFeatures.All;

        /// <summary>
        /// Gets or sets custom parameters as key-value pairs
        /// </summary>
        public Dictionary<string, object> CustomParameters { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Creates a deep copy of the current ProcessingParameters instance
        /// </summary>
        public ProcessingParameters Clone()
        {
            return new ProcessingParameters
            {
                QualityLevel = this.QualityLevel,
                EnableAdaptiveProcessing = this.EnableAdaptiveProcessing,
                MaxProcessingTimeMs = this.MaxProcessingTimeMs,
                EnabledFeatures = this.EnabledFeatures,
                CustomParameters = new Dictionary<string, object>(this.CustomParameters)
            };
        }
    }

    /// <summary>
    /// Flags enum representing different processing features that can be enabled/disabled
    /// </summary>
    [Flags]
    public enum ProcessingFeatures
    {
        /// <summary>No features enabled</summary>
        None = 0,
        
        /// <summary>Enable mesh optimization</summary>
        MeshOptimization = 1 << 0,
        
        /// <summary>Enable normal recalculation</summary>
        NormalRecalculation = 1 << 1,
        
        /// <summary>Enable hole filling</summary>
        HoleFilling = 1 << 2,
        
        /// <summary>Enable smoothing</summary>
        Smoothing = 1 << 3,
        
        /// <summary>Enable decimation</summary>
        Decimation = 1 << 4,
        
        /// <summary>Enable all features</summary>
        All = ~0
    }
}
