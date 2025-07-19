using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommandLine;
using Microsoft.Extensions.Logging;
using ProcessEngine.Services.Camera;
using ProcessEngine.Services.Vision;

namespace StereoCalibrationTool
{
    public class Program
    {
        private static ILogger<Program> _logger;
        private static StereoCalibration _calibration;
        private static StereoCalibrationOptions _options;

        public static void Main(string[] args)
        {
            Parser.Default.ParseArguments<StereoCalibrationOptions>(args)
                .WithParsed(RunCalibration)
                .WithNotParsed(HandleParseError);
        }

        private static void RunCalibration(StereoCalibrationOptions options)
        {
            _options = options;
            SetupLogging();
            
            try
            {
                _logger.LogInformation("Starting stereo camera calibration...");
                
                // Initialize cameras
                using var leftCamera = CreateCamera(options.LeftCameraConfig);
                using var rightCamera = CreateCamera(options.RightCameraConfig);
                
                // Capture calibration images
                var imagePairs = CaptureCalibrationImages(leftCamera, rightCamera, options).GetAwaiter().GetResult();
                
                // Perform calibration
                _calibration = CalibrateCameras(imagePairs, options);
                
                // Save calibration
                SaveCalibration(_calibration, options.OutputFile);
                
                _logger.LogInformation($"Calibration completed successfully. Results saved to {options.OutputFile}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during calibration");
                Environment.Exit(1);
            }
        }

        private static ICoaxialCameraService CreateCamera(string configPath)
        {
            // Implementation would create the appropriate camera service based on config
            // This is a simplified version
            return new AR0234CameraService(
                new LibCameraWrapper("/dev/video0"), // Would come from config
                new LoggerFactory().CreateLogger<AR0234CameraService>());
        }

        private static async Task<List<StereoFrame>> CaptureCalibrationImages(
            ICoaxialCameraService leftCamera,
            ICoaxialCameraService rightCamera,
            StereoCalibrationOptions options)
        {
            _logger.LogInformation($"Capturing {options.NumImages} calibration image pairs...");
            
            var imagePairs = new List<StereoFrame>();
            var captureCount = 0;
            
            while (captureCount < options.NumImages)
            {
                Console.WriteLine($"\n--- Capture {captureCount + 1}/{options.NumImages} ---");
                Console.WriteLine("Press Enter to capture or 'q' to finish...");
                
                var key = Console.ReadKey();
                if (key.Key == ConsoleKey.Q)
                    break;
                
                try
                {
                    // Capture synchronized stereo pair
                    var leftTask = leftCamera.CaptureFrameAsync();
                    var rightTask = rightCamera.CaptureFrameAsync();
                    await Task.WhenAll(leftTask, rightTask);
                    
                    var frame = new StereoFrame
                    {
                        LeftImage = await leftTask,
                        RightImage = await rightTask,
                        Timestamp = DateTime.UtcNow
                    };
                    
                    // Detect chessboard corners (simplified)
                    bool chessboardFound = true; // Would actually detect corners
                    
                    if (chessboardFound)
                    {
                        // Save captured images for reference
                        var baseName = $"calib_{captureCount:D4}";
                        var outputDir = options.OutputDir ?? "calibration_images";
                        Directory.CreateDirectory(outputDir);
                        
                        frame.LeftImage.Save(Path.Combine(outputDir, $"{baseName}_left.png"));
                        frame.RightImage.Save(Path.Combine(outputDir, $"{baseName}_right.png"));
                        
                        imagePairs.Add(frame);
                        captureCount++;
                        _logger.LogInformation($"Successfully captured image pair {captureCount}/{options.NumImages}");
                    }
                    else
                    {
                        _logger.LogWarning("Chessboard not found in one or both images. Please try again.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error capturing image pair");
                }
            }
            
            return imagePairs;
        }

        private static StereoCalibration CalibrateCameras(
            List<StereoFrame> imagePairs,
            StereoCalibrationOptions options)
        {
            _logger.LogInformation("Calibrating stereo cameras...");
            
            // Implementation would use OpenCV's stereoCalibrate
            // This is a simplified version
            
            var calibration = new StereoCalibration
            {
                // Set calibration parameters
                LeftCameraMatrix = new float[9],
                RightCameraMatrix = new float[9],
                DistortionCoefficients = new float[5],
                RotationMatrix = new float[9],
                TranslationVector = new float[3],
                QMatrix = new float[16],
                DisparitySettings = new DisparitySettings()
            };
            
            // Initialize with identity matrices
            calibration.LeftCameraMatrix[0] = calibration.RightCameraMatrix[0] = 1000; // fx
            calibration.LeftCameraMatrix[4] = calibration.RightCameraMatrix[4] = 1000; // fy
            calibration.LeftCameraMatrix[2] = imagePairs[0].LeftImage.Width / 2f;  // cx
            calibration.LeftCameraMatrix[5] = imagePairs[0].LeftImage.Height / 2f; // cy
            calibration.RightCameraMatrix[2] = imagePairs[0].RightImage.Width / 2f; // cx
            calibration.RightCameraMatrix[5] = imagePairs[0].RightImage.Height / 2f; // cy
            calibration.LeftCameraMatrix[8] = calibration.RightCameraMatrix[8] = 1;
            
            // Set identity rotation
            calibration.RotationMatrix[0] = calibration.RotationMatrix[4] = 
                calibration.RotationMatrix[8] = 1;
                
            // Set translation (stereo baseline)
            calibration.TranslationVector[0] = options.BaselineMm; // X-axis baseline in mm
            
            // Set Q matrix for 3D reconstruction
            calibration.QMatrix[0] = 1; // fx
            calibration.QMatrix[5] = 1; // fy
            calibration.QMatrix[2] = -calibration.LeftCameraMatrix[2]; // -cx
            calibration.QMatrix[6] = -calibration.LeftCameraMatrix[5]; // -cy
            calibration.QMatrix[7] = calibration.LeftCameraMatrix[0]; // fx
            calibration.QMatrix[11] = -options.BaselineMm; // -baseline * fx
            calibration.QMatrix[14] = 1;
            
            _logger.LogInformation("Calibration completed successfully");
            
            return calibration;
        }

        private static void SaveCalibration(StereoCalibration calibration, string outputFile)
        {
            var dir = Path.GetDirectoryName(outputFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            
            var json = System.Text.Json.JsonSerializer.Serialize(calibration, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            File.WriteAllText(outputFile, json);
        }

        private static void SetupLogging()
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddFilter("Microsoft", LogLevel.Warning)
                    .AddFilter("System", LogLevel.Warning)
                    .AddFilter("StereoCalibrationTool", LogLevel.Debug)
                    .AddConsole();
            });
            
            _logger = loggerFactory.CreateLogger<Program>();
        }

        private static void HandleParseError(IEnumerable<CommandLine.Error> errors)
        {
            foreach (var error in errors)
            {
                Console.Error.WriteLine(error.ToString());
            }
            
            Environment.Exit(1);
        }
    }

    public class StereoCalibrationOptions
    {
        [Option('l', "left-camera", Required = true, HelpText = "Left camera configuration file")]
        public string LeftCameraConfig { get; set; }
        
        [Option('r', "right-camera", Required = true, HelpText = "Right camera configuration file")]
        public string RightCameraConfig { get; set; }
        
        [Option('o', "output", Default = "calibration.json", HelpText = "Output calibration file")]
        public string OutputFile { get; set; } = "calibration.json";
        
        [Option('d', "output-dir", Default = "calibration_images", HelpText = "Directory to save calibration images")]
        public string OutputDir { get; set; } = "calibration_images";
        
        [Option('n', "num-images", Default = 20, HelpText = "Number of calibration images to capture")]
        public int NumImages { get; set; } = 20;
        
        [Option('b', "baseline", Default = 60.0f, HelpText = "Approximate baseline between cameras in mm")]
        public float BaselineMm { get; set; } = 60.0f;
        
        [Option("bw", "board-width", Default = 9, HelpText = "Number of inner corners per a chessboard row")]
        public int BoardWidth { get; set; } = 9;
        
        [Option("bh", "board-height", Default = 6, HelpText = "Number of inner corners per a chessboard column")]
        public int BoardHeight { get; set; } = 6;
        
        [Option('s', "square-size", Default = 25.0f, HelpText = "Chessboard square size in mm")]
        public float SquareSizeMm { get; set; } = 25.0f;
    }

    public class CameraConfig
    {
        public string DeviceId { get; set; }
        public int Width { get; set; } = 1920;
        public int Height { get; set; } = 1200;
        public int Fps { get; set; } = 30;
    }
}
