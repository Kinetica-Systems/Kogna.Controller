using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProcessEngine.Configuration;

namespace ProcessEngine.Services.Vision
{
    /// <summary>
    /// Service that periodically retrains the stereo vision model with newly collected data.
    /// </summary>
    public class ModelRetrainingService : BackgroundService
    {
        private readonly ILogger<ModelRetrainingService> _logger;
        private readonly ProcessEngineConfig _config;
        private readonly string _pythonPath;
        private readonly string _trainingScriptPath;
        private readonly string _dataDir;
        private readonly string _outputModelPath;
        private Timer _retrainTimer;
        private bool _isTraining = false;
        private DateTime _lastRetrainTime = DateTime.MinValue;

        public ModelRetrainingService(
            ILogger<ModelRetrainingService> logger,
            ProcessEngineConfig config)
        {
            _logger = logger;
            _config = config;
            
            // Set up paths
            _pythonPath = _config.GetValue("Python:Path", "python");
            _trainingScriptPath = Path.Combine(
                AppContext.BaseDirectory,
                "tools",
                "DataCollection",
                "train_stereo_net.py");
            _dataDir = Path.Combine(AppContext.BaseDirectory, "data", "processed");
            _outputModelPath = Path.Combine(
                AppContext.BaseDirectory,
                "models",
                "stereo_net_retrained.onnx");
            
            // Ensure directories exist
            Directory.CreateDirectory(Path.GetDirectoryName(_outputModelPath));
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting model retraining service");
            
            // Check for new data and retrain every hour
            _retrainTimer = new Timer(
                CheckAndRetrain,
                null,
                TimeSpan.Zero,
                TimeSpan.FromHours(1));
            
            return Task.CompletedTask;
        }

        private async void CheckAndRetrain(object state)
        {
            if (_isTraining)
            {
                _logger.LogInformation("Training already in progress, skipping this cycle");
                return;
            }

            try
            {
                _isTraining = true;
                
                // Check if we have enough new data
                if (!HasEnoughNewData())
                {
                    _logger.LogDebug("Not enough new data for retraining");
                    return;
                }
                
                _logger.LogInformation("Starting model retraining with new data...");
                
                // Run the training script
                var arguments = new System.Text.StringBuilder();
                arguments.Append('"').Append(_trainingScriptPath).Append('"');
                arguments.Append(" --data-dir ").Append('"').Append(_dataDir).Append('"');
                arguments.Append(" --output ").Append('"').Append(_outputModelPath).Append('"');
                arguments.Append(" --epochs 20");
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = _pythonPath,
                    Arguments = arguments.ToString(),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                using var process = new Process { StartInfo = startInfo };
                
                // Capture output
                var output = new System.Text.StringBuilder();
                process.OutputDataReceived += (sender, args) => 
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        _logger.LogInformation($"[Train] {args.Data}");
                        output.AppendLine(args.Data);
                    }
                };
                
                process.ErrorDataReceived += (sender, args) => 
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        _logger.LogError($"[Train Error] {args.Data}");
                    }
                };
                
                // Start the process
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                
                // Wait for completion with timeout (4 hours)
                if (await Task.Run(() => process.WaitForExit(4 * 60 * 60 * 1000)))
                {
                    if (process.ExitCode == 0)
                    {
                        _logger.LogInformation("Model retraining completed successfully");
                        _lastRetrainTime = DateTime.UtcNow;
                        
                        // TODO: Notify other services to reload the model
                        // _stereoVisionService?.UpdateModel(_outputModelPath);
                    }
                    else
                    {
                        _logger.LogError($"Model training failed with exit code {process.ExitCode}");
                    }
                }
                else
                {
                    _logger.LogError("Model training timed out");
                    process.Kill();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during model retraining");
            }
            finally
            {
                _isTraining = false;
            }
        }
        
        private bool HasEnoughNewData()
        {
            try
            {
                // Check if we have at least 100 new samples since last retrain
                var trainDir = Path.Combine(_dataDir, "train");
                if (!Directory.Exists(trainDir))
                    return false;
                
                var trainFiles = Directory.GetFiles(Path.Combine(trainDir, "left"), "*.png");
                var newSamples = 0;
                
                foreach (var file in trainFiles)
                {
                    var lastWrite = File.GetLastWriteTimeUtc(file);
                    if (lastWrite > _lastRetrainTime)
                    {
                        newSamples++;
                        if (newSamples >= 100)  // Minimum samples for retraining
                            return true;
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for new data");
                return false;
            }
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Stopping model retraining service");
            _retrainTimer?.Change(Timeout.Infinite, 0);
            await base.StopAsync(stoppingToken);
        }

        public override void Dispose()
        {
            _retrainTimer?.Dispose();
            base.Dispose();
        }
    }
}
