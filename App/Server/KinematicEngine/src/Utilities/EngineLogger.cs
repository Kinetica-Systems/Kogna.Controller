using System;
using System.IO;
using System.Text;
using System.Threading;
using KinematicEngine.Configuration;
using SharedTypes;

namespace KinematicEngine.Utilities
{
    /// <summary>
    /// Centralized logging utility for the kinematic engine
    /// </summary>
    public static class EngineLogger
    {
        private static readonly object _lock = new object();
        private static string _logFilePath = string.Empty;
        private static bool _initialized = false;
        private static int _logLevel = EngineConstants.LogLevels.INFO;
        private static bool _logToConsole = true;
        private static bool _logToFile = true;
        private static int _maxLogFileSize = 10 * 1024 * 1024; // 10 MB
        private static int _maxLogFiles = 5;

        /// <summary>
        /// Initializes the logger with the specified configuration
        /// </summary>
        /// <param name="logFilePath">Path to the log file</param>
        /// <param name="logLevel">Minimum log level to record</param>
        /// <param name="logToConsole">Whether to log to console</param>
        /// <param name="logToFile">Whether to log to file</param>
        public static void Initialize(string logFilePath = "", int logLevel = EngineConstants.LogLevels.INFO, 
                                   bool logToConsole = true, bool logToFile = true)
        {
            lock (_lock)
            {
                _logFilePath = string.IsNullOrEmpty(logFilePath) ? 
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kinematic_engine.log") : logFilePath;
                _logLevel = logLevel;
                _logToConsole = logToConsole;
                _logToFile = logToFile;
                _initialized = true;

                Log(EngineConstants.LogLevels.INFO, "ENGINE", "Logger initialized");
            }
        }

        /// <summary>
        /// Logs a debug message
        /// </summary>
        /// <param name="component">Component name</param>
        /// <param name="message">Message to log</param>
        public static void Debug(string component, string message)
        {
            Log(EngineConstants.LogLevels.DEBUG, component, message);
        }

        /// <summary>
        /// Logs an info message
        /// </summary>
        /// <param name="component">Component name</param>
        /// <param name="message">Message to log</param>
        public static void Info(string component, string message)
        {
            Log(EngineConstants.LogLevels.INFO, component, message);
        }

        /// <summary>
        /// Logs a warning message
        /// </summary>
        /// <param name="component">Component name</param>
        /// <param name="message">Message to log</param>
        public static void Warning(string component, string message)
        {
            Log(EngineConstants.LogLevels.WARNING, component, message);
        }

        /// <summary>
        /// Logs an error message
        /// </summary>
        /// <param name="component">Component name</param>
        /// <param name="message">Message to log</param>
        public static void Error(string component, string message)
        {
            Log(EngineConstants.LogLevels.ERROR, component, message);
        }

        /// <summary>
        /// Logs a critical error message
        /// </summary>
        /// <param name="component">Component name</param>
        /// <param name="message">Message to log</param>
        public static void Critical(string component, string message)
        {
            Log(EngineConstants.LogLevels.CRITICAL, component, message);
        }

        /// <summary>
        /// Logs an exception
        /// </summary>
        /// <param name="component">Component name</param>
        /// <param name="message">Message to log</param>
        /// <param name="exception">Exception to log</param>
        public static void Exception(string component, string message, Exception exception)
        {
            var sb = new StringBuilder();
            sb.AppendLine(message);
            sb.AppendLine($"Exception: {exception.GetType().Name}");
            sb.AppendLine($"Message: {exception.Message}");
            sb.AppendLine($"StackTrace: {exception.StackTrace}");

            if (exception.InnerException != null)
            {
                sb.AppendLine($"Inner Exception: {exception.InnerException.Message}");
            }

            Log(EngineConstants.LogLevels.ERROR, component, sb.ToString());
        }

        /// <summary>
        /// Logs motion data for debugging
        /// </summary>
        /// <param name="component">Component name</param>
        /// <param name="command">Motion command</param>
        public static void LogMotion(string component, MotionCommand command)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Motion Command: {command.Type}");
            sb.AppendLine($"Sequence: {command.SequenceNumber}");
            sb.AppendLine($"Start Position: [{string.Join(", ", command.StartPosition)}]");
            sb.AppendLine($"End Position: [{string.Join(", ", command.EndPosition)}]");
            sb.AppendLine($"Feed Rate: {command.FeedRate}");
            sb.AppendLine($"Acceleration: {command.Acceleration}");
            sb.AppendLine($"Jerk: {command.Jerk}");

            if (command.Type == MotionType.Arc)
            {
                sb.AppendLine($"Arc Center: [{string.Join(", ", command.ArcCenter)}]");
                sb.AppendLine($"Clockwise: {command.IsClockwise}");
            }

            if (command.Type == MotionType.Dwell)
            {
                sb.AppendLine($"Dwell Time: {command.DwellTime}");
            }

            Log(EngineConstants.LogLevels.DEBUG, component, sb.ToString());
        }

        /// <summary>
        /// Logs position data for debugging
        /// </summary>
        /// <param name="component">Component name</param>
        /// <param name="position">Position array</param>
        /// <param name="label">Label for the position</param>
        public static void LogPosition(string component, double[] position, string label = "Position")
        {
            Log(EngineConstants.LogLevels.DEBUG, component, 
                $"{label}: [{string.Join(", ", position)}]");
        }

        /// <summary>
        /// Logs performance metrics
        /// </summary>
        /// <param name="component">Component name</param>
        /// <param name="operation">Operation name</param>
        /// <param name="durationMs">Duration in milliseconds</param>
        public static void LogPerformance(string component, string operation, double durationMs)
        {
            Log(EngineConstants.LogLevels.DEBUG, component, 
                $"Performance: {operation} took {durationMs:F3}ms");
        }

        /// <summary>
        /// Core logging method
        /// </summary>
        /// <param name="level">Log level</param>
        /// <param name="component">Component name</param>
        /// <param name="message">Message to log</param>
        private static void Log(int level, string component, string message)
        {
            if (!_initialized)
            {
                Initialize();
            }

            if (level < _logLevel)
                return;

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var levelName = GetLevelName(level);
            var logEntry = $"[{timestamp}] [{levelName}] [{component}] {message}";

            lock (_lock)
            {
                if (_logToConsole)
                {
                    WriteToConsole(level, logEntry);
                }

                if (_logToFile)
                {
                    WriteToFile(logEntry);
                }
            }
        }

        /// <summary>
        /// Writes log entry to console with appropriate color
        /// </summary>
        /// <param name="level">Log level</param>
        /// <param name="logEntry">Log entry to write</param>
        private static void WriteToConsole(int level, string logEntry)
        {
            var originalColor = Console.ForegroundColor;
            
            try
            {
                Console.ForegroundColor = GetConsoleColor(level);
                Console.WriteLine(logEntry);
            }
            finally
            {
                Console.ForegroundColor = originalColor;
            }
        }

        /// <summary>
        /// Writes log entry to file
        /// </summary>
        /// <param name="logEntry">Log entry to write</param>
        private static void WriteToFile(string logEntry)
        {
            try
            {
                // Check if log file exists and is too large
                if (File.Exists(_logFilePath))
                {
                    var fileInfo = new FileInfo(_logFilePath);
                    if (fileInfo.Length > _maxLogFileSize)
                    {
                        RotateLogFiles();
                    }
                }

                // Ensure directory exists
                var directory = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Write log entry
                File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
            }
            catch (Exception ex)
            {
                // If we can't write to the log file, at least write to console
                Console.WriteLine($"[ERROR] Failed to write to log file: {ex.Message}");
            }
        }

        /// <summary>
        /// Rotates log files when the current one gets too large
        /// </summary>
        private static void RotateLogFiles()
        {
            try
            {
                // Delete oldest log file if we have too many
                var oldestLogFile = Path.Combine(
                    Path.GetDirectoryName(_logFilePath)!,
                    $"kinematic_engine_{_maxLogFiles}.log");

                if (File.Exists(oldestLogFile))
                {
                    File.Delete(oldestLogFile);
                }

                // Shift existing log files
                for (int i = _maxLogFiles - 1; i >= 1; i--)
                {
                    var sourceFile = Path.Combine(
                        Path.GetDirectoryName(_logFilePath)!,
                        $"kinematic_engine_{i}.log");

                    var destFile = Path.Combine(
                        Path.GetDirectoryName(_logFilePath)!,
                        $"kinematic_engine_{i + 1}.log");

                    if (File.Exists(sourceFile))
                    {
                        File.Move(sourceFile, destFile);
                    }
                }

                // Move current log file to .1
                var backupFile = Path.Combine(
                    Path.GetDirectoryName(_logFilePath)!,
                    "kinematic_engine_1.log");

                File.Move(_logFilePath, backupFile);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to rotate log files: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the string name for a log level
        /// </summary>
        /// <param name="level">Log level</param>
        /// <returns>Level name</returns>
        private static string GetLevelName(int level)
        {
            return level switch
            {
                EngineConstants.LogLevels.DEBUG => "DEBUG",
                EngineConstants.LogLevels.INFO => "INFO",
                EngineConstants.LogLevels.WARNING => "WARN",
                EngineConstants.LogLevels.ERROR => "ERROR",
                EngineConstants.LogLevels.CRITICAL => "CRIT",
                _ => "UNKNOWN"
            };
        }

        /// <summary>
        /// Gets the console color for a log level
        /// </summary>
        /// <param name="level">Log level</param>
        /// <returns>Console color</returns>
        private static ConsoleColor GetConsoleColor(int level)
        {
            return level switch
            {
                EngineConstants.LogLevels.DEBUG => ConsoleColor.Gray,
                EngineConstants.LogLevels.INFO => ConsoleColor.White,
                EngineConstants.LogLevels.WARNING => ConsoleColor.Yellow,
                EngineConstants.LogLevels.ERROR => ConsoleColor.Red,
                EngineConstants.LogLevels.CRITICAL => ConsoleColor.Magenta,
                _ => ConsoleColor.White
            };
        }

        /// <summary>
        /// Sets the log level
        /// </summary>
        /// <param name="level">New log level</param>
        public static void SetLogLevel(int level)
        {
            lock (_lock)
            {
                _logLevel = level;
            }
        }

        /// <summary>
        /// Enables or disables console logging
        /// </summary>
        /// <param name="enabled">Whether to enable console logging</param>
        public static void SetConsoleLogging(bool enabled)
        {
            lock (_lock)
            {
                _logToConsole = enabled;
            }
        }

        /// <summary>
        /// Enables or disables file logging
        /// </summary>
        /// <param name="enabled">Whether to enable file logging</param>
        public static void SetFileLogging(bool enabled)
        {
            lock (_lock)
            {
                _logToFile = enabled;
            }
        }

        /// <summary>
        /// Sets the maximum log file size
        /// </summary>
        /// <param name="maxSizeBytes">Maximum size in bytes</param>
        public static void SetMaxLogFileSize(int maxSizeBytes)
        {
            lock (_lock)
            {
                _maxLogFileSize = maxSizeBytes;
            }
        }

        /// <summary>
        /// Sets the maximum number of log files to keep
        /// </summary>
        /// <param name="maxFiles">Maximum number of files</param>
        public static void SetMaxLogFiles(int maxFiles)
        {
            lock (_lock)
            {
                _maxLogFiles = maxFiles;
            }
        }
    }
} 