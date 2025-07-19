using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProcessEngine.Models;
using ProcessEngine.Services.Vision.StereoNet;

namespace ProcessEngine.Services.Vision
{
    /// <summary>
    /// Service for automatically collecting and classifying images during the deposition process.
    /// </summary>
    public class AutoDataCollectionService : IProcessService, IDisposable
    {
        private readonly ILogger<AutoDataCollectionService> _logger;
        private readonly StereoVisionService _stereoVisionService;
        private readonly string _outputDirectory;
        private bool _isCollecting = false;
        private int _samplesCollected = 0;
        private readonly object _lock = new object();

        public bool IsEnabled { get; private set; } = false;
        public string Status => _isCollecting ? "Collecting" : "Idle";
        public int SamplesCollected => _samplesCollected;

        public AutoDataCollectionService(
            ILogger<AutoDataCollectionService> logger,
            StereoVisionService stereoVisionService,
            string outputDirectory = "data/auto_collected")
        {
            _logger = logger;
            _stereoVisionService = stereoVisionService;
            _outputDirectory = outputDirectory;
            
            // Ensure output directory exists
            Directory.CreateDirectory(Path.Combine(_outputDirectory, "train"));
            Directory.CreateDirectory(Path.Combine(_outputDirectory, "val"));
            Directory.CreateDirectory(Path.Combine(_outputDirectory, "train/left"));
            Directory.CreateDirectory(Path.Combine(_outputDirectory, "train/right"));
            Directory.CreateDirectory(Path.Combine(_outputDirectory, "train/labels"));
            Directory.CreateDirectory(Path.Combine(_outputDirectory, "val/left"));
            Directory.CreateDirectory(Path.Combine(_outputDirectory, "val/right"));
            Directory.CreateDirectory(Path.Combine(_outputDirectory, "val/labels"));
        }

        /// <summary>
        /// Enable or disable automatic data collection.
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            lock (_lock)
            {
                if (enabled && !IsEnabled)
                {
                    _logger.LogInformation("Enabling automatic data collection");
                    _stereoVisionService.FrameProcessed += OnFrameProcessed;
                }
                else if (!enabled && IsEnabled)
                {
                    _logger.LogInformation("Disabling automatic data collection");
                    _stereoVisionService.FrameProcessed -= OnFrameProcessed;
                }
                
                IsEnabled = enabled;
            }
        }

        private async void OnFrameProcessed(object sender, FrameProcessedEventArgs e)
        {
            if (!_isCollecting && ShouldCollectSample())
            {
                _isCollecting = true;
                
                try
                {
                    await ProcessAndSaveSampleAsync(e);
                    _samplesCollected++;
                    _logger.LogDebug($"Auto-collected sample {_samplesCollected}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing auto-collected sample");
                }
                finally
                {
                    _isCollecting = false;
                }
            }
        }

        private bool ShouldCollectSample()
        {
            // Implement sampling logic (e.g., random sampling, time-based, or process-based)
            // For now, collect every 60 seconds
            return DateTime.UtcNow.Second % 60 == 0;
        }

        private async Task ProcessAndSaveSampleAsync(FrameProcessedEventArgs e)
        {
            // Determine if this should be a training or validation sample (80/20 split)
            bool isValidation = new Random().NextDouble() < 0.2;
            string split = isValidation ? "val" : "train";
            
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            var baseName = $"auto_{timestamp}";
            
            // Save images
            var leftPath = Path.Combine(_outputDirectory, split, "left", $"{baseName}.png");
            var rightPath = Path.Combine(_outputDirectory, split, "right", $"{baseName}.png");
            
            await Task.WhenAll(
                e.LeftImage.SaveAsync(leftPath),
                e.RightImage.SaveAsync(rightPath)
            );
            
            // Create annotation based on model predictions
            var annotation = new
            {
                width = e.AnalysisResult?.BeadWidthMm ?? 0,
                height = e.AnalysisResult?.BeadHeightMm ?? 0,
                cross_section = e.AnalysisResult?.CrossSectionProfile ?? Array.Empty<float>(),
                defects = e.AnalysisResult?.Defects?.Select(d => new { 
                    type = d.Type.ToString(),
                    confidence = d.Confidence,
                    bounding_box = d.BoundingBox
                }).ToArray() ?? Array.Empty<object>(),
                timestamp = DateTime.UtcNow.ToString("o"),
                process_parameters = e.ProcessState?.ToDictionary() ?? new System.Collections.Generic.Dictionary<string, object>(),
                prediction_confidence = e.AnalysisResult?.Confidence ?? 0
            };
            
            // Save annotation
            var annotationPath = Path.Combine(_outputDirectory, split, "labels", $"{baseName}.json");
            await File.WriteAllTextAsync(annotationPath, 
                System.Text.Json.JsonSerializer.Serialize(annotation, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }

        public void Dispose()
        {
            SetEnabled(false);
        }

        // IProcessService implementation
        public Task InitializeAsync() => Task.CompletedTask;
        public Task StartAsync() => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
    }
}
