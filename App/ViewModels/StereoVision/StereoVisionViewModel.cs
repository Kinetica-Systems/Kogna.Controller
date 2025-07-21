using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using ScottPlot;

namespace KognaServer.ViewModels
{
    public class StereoVisionViewModel : ViewModelBase, IDisposable
    {
        private readonly IDisposable? _metricsSubscription;
        private readonly Subject<SystemMetrics> _metricsSubject = new();
        private readonly List<double> _fpsValues = new();
        private readonly List<double> _latencyValues = new();
        private readonly List<double> _confidenceValues = new();
        private readonly List<DateTime> _timeValues = new();
        private readonly PerformanceCounter _cpuCounter;
        private readonly PerformanceCounter _memoryCounter;
        private readonly PerformanceCounter _diskReadCounter;
        private readonly PerformanceCounter _diskWriteCounter;
        private readonly int _maxDataPoints = 60; // 1 minute of data at 1s intervals
        private DateTime _lastUpdateTime = DateTime.UtcNow;
        private long _lastDiskRead = 0;
        private long _lastDiskWrite = 0;
        
        private string _title = "Stereo Vision";
        private double _cpuUsage;
        private double _gpuUsage;
        private double _memoryUsage;
        private double _diskUsage;
        private string _status = "Disconnected";
        private bool _isConnected;
        private string _currentModel = "No model loaded";
        private int _totalSamples;
        private int _samplesToday;
        private double _sampleRate;
        private readonly Dictionary<string, int> _defectCounts = new();

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public double CpuUsage
        {
            get => _cpuUsage;
            set => SetProperty(ref _cpuUsage, value);
        }

        public double GpuUsage
        {
            get => _gpuUsage;
            set => SetProperty(ref _gpuUsage, value);
        }

        public double MemoryUsage
        {
            get => _memoryUsage;
            set => SetProperty(ref _memoryUsage, value);
        }

        public double DiskUsage
        {
            get => _diskUsage;
            set => SetProperty(ref _diskUsage, value);
        }

        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public bool IsConnected
        {
            get => _isConnected;
            set => SetProperty(ref _isConnected, value);
        }

        public string CurrentModel
        {
            get => _currentModel;
            set => SetProperty(ref _currentModel, value);
        }

        public int TotalSamples
        {
            get => _totalSamples;
            set => SetProperty(ref _totalSamples, value);
        }

        public int SamplesToday
        {
            get => _samplesToday;
            set => SetProperty(ref _samplesToday, value);
        }

        public double SampleRate
        {
            get => _sampleRate;
            set => SetProperty(ref _sampleRate, value);
        }

        public ObservableCollection<DefectViewModel> Defects { get; } = new();

        public Plot FpsPlot { get; private set; }
        public Plot LatencyPlot { get; private set; }
        public Plot ConfidencePlot { get; private set; }

        public StereoVisionViewModel()
        {
            InitializePlots();
            
            try
            {
                // Initialize performance counters
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _memoryCounter = new PerformanceCounter("Memory", "Available MBytes");
                _diskReadCounter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
                _diskWriteCounter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
                
                // Get initial values
                _cpuCounter.NextValue(); // First call always returns 0
                _diskReadCounter.NextValue();
                _diskWriteCounter.NextValue();
                
                Status = "Connected to system metrics";
                IsConnected = true;
            }
            catch (Exception ex)
            {
                Status = $"Limited functionality: {ex.Message}";
                IsConnected = false;
            }
            
            // Start metrics collection
            _metricsSubscription = Observable.Interval(TimeSpan.FromSeconds(1))
                .Subscribe(_ => UpdateMetrics());
        }

        private void InitializePlots()
        {
            FpsPlot = new Plot();
            FpsPlot.Title("FPS");
            FpsPlot.XLabel("Time");
            FpsPlot.YLabel("Frames per Second");
            
            LatencyPlot = new Plot();
            LatencyPlot.Title("Latency (ms)");
            LatencyPlot.XLabel("Time");
            LatencyPlot.YLabel("Milliseconds");
            
            ConfidencePlot = new Plot();
            ConfidencePlot.Title("Confidence");
            ConfidencePlot.XLabel("Time");
            ConfidencePlot.YLabel("Percentage");
        }

        private void UpdateMetrics()
        {
            try
            {
                // Get current timestamp for metrics
                var now = DateTime.UtcNow;
                var timeDiff = (now - _lastUpdateTime).TotalSeconds;
                _lastUpdateTime = now;
                
                // Get CPU usage (percentage)
                CpuUsage = Math.Round(_cpuCounter?.NextValue() ?? 0, 1);
                
                // Get memory usage (percentage)
                var availableMB = _memoryCounter?.NextValue() ?? 0;
                // Using a fixed value for total memory instead of ComputerInfo to avoid dependency issues
                var totalMemoryMB = 16384; // Assuming 16GB of RAM (16 * 1024 MB)
                MemoryUsage = Math.Round(((totalMemoryMB - availableMB) / totalMemoryMB) * 100, 1);
                
                // Get disk usage (MB/s)
                var diskRead = _diskReadCounter?.NextValue() ?? 0;
                var diskWrite = _diskWriteCounter?.NextValue() ?? 0;
                var diskReadMBs = (diskRead - _lastDiskRead) / (1024 * 1024);
                var diskWriteMBs = (diskWrite - _lastDiskWrite) / (1024 * 1024);
                _lastDiskRead = (long)diskRead;
                _lastDiskWrite = (long)diskWrite;
                
                // Calculate disk usage as a percentage of a reasonable maximum (e.g., 1GB/s)
                DiskUsage = Math.Min(100, Math.Round((diskReadMBs + diskWriteMBs) / 10, 1));
                
                // For GPU usage, we'd need a specific GPU monitoring library
                // This is a placeholder that shows CPU usage as a proxy
                GpuUsage = CpuUsage * 0.8; // Simulate GPU being slightly less utilized than CPU
                
                // Get process-specific metrics for the current application
                using var process = Process.GetCurrentProcess();
                var fps = 1.0 / (process.TotalProcessorTime.TotalMilliseconds - process.UserProcessorTime.TotalMilliseconds) * 1000;
                var latency = process.UserProcessorTime.TotalMilliseconds / process.PrivilegedProcessorTime.TotalMilliseconds * 100;
                
                // Update plots with new data
                UpdatePlot(FpsPlot, _fpsValues, _timeValues, Math.Round(fps, 1));
                UpdatePlot(LatencyPlot, _latencyValues, _timeValues, Math.Round(latency, 1));
                
                // Confidence metric could be based on system responsiveness
                var confidence = 100 - Math.Min(100, Math.Max(0, CpuUsage + MemoryUsage / 2)) / 2;
                UpdatePlot(ConfidencePlot, _confidenceValues, _timeValues, Math.Round(confidence, 1));
                
                // Update status
                Status = $"Running - {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
                IsConnected = false;
            }
            
            // Update model info
            CurrentModel = "StereoNet v1.0";
            TotalSamples = _fpsValues.Count;
            SamplesToday = (int)(_fpsValues.Count * 0.8);
            SampleRate = _fpsValues.Count > 1 ? 
                (_fpsValues.Last() - _fpsValues.First()) / _fpsValues.Count : 0; // 1-5 samples/second
            
            // Simulate defect detection (in a real app, this would come from the stereo vision system)
            if (DateTime.Now.Second % 30 == 0) // Every 30 seconds
            {
                var defectTypes = new[] { "Blur", "Occlusion", "Reflection", "Low Contrast" };
                var defectType = defectTypes[new Random().Next(defectTypes.Length)];
                
                if (!_defectCounts.ContainsKey(defectType))
                {
                    _defectCounts[defectType] = 0;
                    Defects.Add(new DefectViewModel { Name = defectType, Count = 0 });
                }
                
                _defectCounts[defectType]++;
                var defect = Defects.First(d => d.Name == defectType);
                defect.Count = _defectCounts[defectType];
                
                // Notify UI of the update
                OnPropertyChanged(nameof(Defects));
            }
        }
        
        private void UpdatePlot(Plot plot, List<double> values, List<DateTime> timeValues, double value)
        {
            // Add new data point
            values.Add(value);
            timeValues.Add(DateTime.Now);
            
            // Remove old points
            while (values.Count > _maxDataPoints)
            {
                values.RemoveAt(0);
                timeValues.RemoveAt(0);
            }
            
            // Update plot
            plot.Clear();
            
            // Convert DateTime to double for plotting
            double[] times = timeValues.Select(t => t.ToOADate()).ToArray();
            double[] dataValues = values.ToArray();
            
            // Add the line series
            if (times.Length > 0 && dataValues.Length > 0)
            {
                var scatter = plot.Add.Scatter(times, dataValues);
                scatter.LineWidth = 2;
            }
            
            // Format x-axis for time display
            plot.Axes.DateTimeTicksBottom();
            
            // Notify UI of the update
            OnPropertyChanged(plot == FpsPlot ? nameof(FpsPlot) : 
                              plot == LatencyPlot ? nameof(LatencyPlot) : 
                              nameof(ConfidencePlot));
        }

        public void Dispose()
        {
            _metricsSubscription?.Dispose();
            _metricsSubject.Dispose();
            _cpuCounter?.Dispose();
            _memoryCounter?.Dispose();
            _diskReadCounter?.Dispose();
            _diskWriteCounter?.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    public class SystemMetrics
    {
        public double CpuUsage { get; set; }
        public double GpuUsage { get; set; }
        public double MemoryUsage { get; set; }
        public double DiskUsage { get; set; }
        public double Fps { get; set; }
        public double LatencyMs { get; set; }
        public double Confidence { get; set; }
    }

    public class DefectViewModel : ObservableObject
    {
        private string _name = string.Empty;
        private int _count;
        private double _percentage;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public int Count
        {
            get => _count;
            set
            {
                if (SetProperty(ref _count, value))
                {
                    OnPropertyChanged(nameof(DisplayText));
                }
            }
        }

        public double Percentage
        {
            get => _percentage;
            set => SetProperty(ref _percentage, value);
        }

        public string DisplayText => $"{Name}: {Count}";
    }
}
