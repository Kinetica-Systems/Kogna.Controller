namespace KinematicEngine.Configuration
{
    /// <summary>
    /// Centralized constants and configuration values for the kinematic engine
    /// </summary>
    public static class EngineConstants
    {
        // Axis and joint constants
        public const int MAX_AXES = 8;
        public const int DEFAULT_AXIS_COUNT = 6;
        public const int MAX_JOINTS = 8;

        // Motion parameters
        public const double DEFAULT_FEED_RATE = 100.0;        // mm/s
        public const double DEFAULT_ACCELERATION = 100.0;     // mm/s²
        public const double DEFAULT_JERK = 1000.0;           // mm/s³
        public const double DEFAULT_RAPID_VELOCITY = 500.0;   // mm/s

        // Buffer management
        public const double TARGET_BUFFER_TIME = 0.2;         // seconds
        public const double MIN_BUFFER_TIME = 0.05;           // seconds
        public const double MAX_BUFFER_TIME = 0.5;            // seconds
        public const double BUFFER_UPDATE_INTERVAL = 0.01;    // seconds

        // Timing constants
        public const double MIN_SEGMENT_TIME = 0.001;         // seconds
        public const double MAX_SEGMENT_TIME = 10.0;          // seconds
        public const double DEFAULT_DWELL_TIME = 1.0;         // seconds

        // Numerical tolerances
        public const double POSITION_TOLERANCE = 1e-6;        // mm
        public const double ANGLE_TOLERANCE = 1e-6;           // radians
        public const double VELOCITY_TOLERANCE = 1e-6;        // mm/s
        public const double TIME_TOLERANCE = 1e-9;            // seconds

        // Kinematic constants
        public const double PI = 3.141592653589793;
        public const double TWO_PI = 2.0 * PI;
        public const double DEG_TO_RAD = PI / 180.0;
        public const double RAD_TO_DEG = 180.0 / PI;

        // Hardware communication
        public const int KOGNA_OK = 0;
        public const int KOGNA_ERROR = -1;
        public const int MAX_COMMAND_LENGTH = 1024;
        public const int MAX_RESPONSE_LENGTH = 1024;

        // Trajectory planning
        public const int MAX_SEGMENTS = 1000;
        public const int MAX_PENDING_SEGMENTS = 100;
        public const double CORNER_TOLERANCE = 0.001;         // radians
        public const double COLLINEAR_TOLERANCE = 0.001;      // radians

        // Velocity profile
        public const int VELOCITY_PROFILE_STEPS = 100;
        public const double ACCELERATION_PHASE_RATIO = 0.33;  // 1/3 of total time
        public const double DECELERATION_PHASE_RATIO = 0.33;  // 1/3 of total time

        // Error codes
        public static class ErrorCodes
        {
            public const int SUCCESS = 0;
            public const int INVALID_PARAMETER = -1;
            public const int HARDWARE_ERROR = -2;
            public const int KINEMATIC_ERROR = -3;
            public const int PLANNING_ERROR = -4;
            public const int TIMEOUT_ERROR = -5;
            public const int LIMIT_ERROR = -6;
            public const int NOT_INITIALIZED = -7;
            public const int ALREADY_RUNNING = -8;
            public const int NOT_RUNNING = -9;
        }

        // Status codes
        public static class StatusCodes
        {
            public const int IDLE = 0;
            public const int RUNNING = 1;
            public const int PAUSED = 2;
            public const int ERROR = 3;
            public const int EMERGENCY_STOP = 4;
            public const int INITIALIZING = 5;
            public const int STOPPING = 6;
        }

        // Motion types
        public static class MotionTypes
        {
            public const int UNDEFINED = 0;
            public const int LINEAR = 1;
            public const int ARC = 2;
            public const int RAPID = 3;
            public const int DWELL = 4;
            public const int HOME = 5;
            public const int REFERENCE = 6;
        }

        // Plane definitions
        public static class Planes
        {
            public const int XY = 1;
            public const int YZ = 2;
            public const int XZ = 3;
        }

        // Units
        public static class Units
        {
            public const int UNDEFINED = -1;
            public const int INCHES = 1;
            public const int MM = 2;
            public const int CM = 3;
        }

        // Default limits for 6-axis robot
        public static class DefaultLimits
        {
            // Joint limits (degrees)
            public static readonly double[] JOINT_LIMITS = new double[]
            {
                -180.0, 180.0,  // Joint 1 (base rotation)
                -90.0,  90.0,   // Joint 2 (shoulder)
                -180.0, 180.0,  // Joint 3 (elbow)
                -180.0, 180.0,  // Joint 4 (wrist roll)
                -90.0,  90.0,   // Joint 5 (wrist pitch)
                -180.0, 180.0   // Joint 6 (wrist yaw)
            };

            // Workspace limits (mm)
            public static readonly double[] WORKSPACE_LIMITS = new double[]
            {
                -2000.0, 2000.0,  // X limits
                -2000.0, 2000.0,  // Y limits
                0.0, 3000.0       // Z limits
            };

            // Maximum velocities (mm/s or deg/s)
            public static readonly double[] MAX_VELOCITIES = new double[]
            {
                500.0,  // X axis
                500.0,  // Y axis
                500.0,  // Z axis
                180.0,  // A axis (deg/s)
                180.0,  // B axis (deg/s)
                180.0   // C axis (deg/s)
            };

            // Maximum accelerations (mm/s² or deg/s²)
            public static readonly double[] MAX_ACCELERATIONS = new double[]
            {
                1000.0, // X axis
                1000.0, // Y axis
                1000.0, // Z axis
                360.0,  // A axis (deg/s²)
                360.0,  // B axis (deg/s²)
                360.0   // C axis (deg/s²)
            };

            // Maximum jerks (mm/s³ or deg/s³)
            public static readonly double[] MAX_JERKS = new double[]
            {
                10000.0, // X axis
                10000.0, // Y axis
                10000.0, // Z axis
                3600.0,  // A axis (deg/s³)
                3600.0,  // B axis (deg/s³)
                3600.0   // C axis (deg/s³)
            };
        }

        // Link lengths for Fanuc-style robot (mm)
        public static class LinkLengths
        {
            public const double L1_X = 180.0;     // Base offset in X
            public const double L1_Z = 1000.0;    // Base height
            public const double L2 = 950.0;       // Upper arm length
            public static readonly double L3 = System.Math.Sqrt(1150 * 1150 + 240 * 240); // Forearm length
            public const double L6 = 200.0;       // Tool length
        }

        // File paths and extensions
        public static class FileExtensions
        {
            public const string CONFIG_FILE = ".config";
            public const string LOG_FILE = ".log";
            public const string BACKUP_FILE = ".bak";
            public const string TEMP_FILE = ".tmp";
        }

        // Logging levels
        public static class LogLevels
        {
            public const int DEBUG = 0;
            public const int INFO = 1;
            public const int WARNING = 2;
            public const int ERROR = 3;
            public const int CRITICAL = 4;
        }

        // Threading
        public static class Threading
        {
            public const int DEFAULT_THREAD_PRIORITY = 2; // Normal
            public const int HIGH_THREAD_PRIORITY = 3;    // AboveNormal
            public const int LOW_THREAD_PRIORITY = 1;     // BelowNormal
            public const int BACKGROUND_THREAD_PRIORITY = 0; // Lowest
        }

        // Network and communication
        public static class Communication
        {
            public const int DEFAULT_TCP_PORT = 8080;
            public const int DEFAULT_UDP_PORT = 8081;
            public const int MAX_CONNECTIONS = 10;
            public const int CONNECTION_TIMEOUT = 5000; // milliseconds
            public const int READ_TIMEOUT = 1000;       // milliseconds
            public const int WRITE_TIMEOUT = 1000;      // milliseconds
        }
    }
} 