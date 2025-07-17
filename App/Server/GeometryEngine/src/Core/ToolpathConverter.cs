using System.Numerics;
using SharedTypes;

namespace GeometryEngine.Core;

/// <summary>
/// Converts toolpaths to motion commands for the kinematic engine
/// </summary>
public class ToolpathConverter
{
    private readonly ToolpathConfig _config;
    private Vector3 _currentPosition;
    private bool _isRetracted;

    public ToolpathConverter(ToolpathConfig config)
    {
        _config = config;
        _currentPosition = new Vector3(0, 0, 10); // Start at safe Z height
        _isRetracted = true;
    }

    /// <summary>
    /// Converts a toolpath to a sequence of motion commands
    /// </summary>
    public IEnumerable<MotionCommand> ConvertToolpath(Toolpath toolpath)
    {
        var commands = new List<MotionCommand>();

        // Handle retraction if needed
        if (toolpath.IsRetraction)
        {
            if (!_isRetracted)
            {
                commands.Add(CreateRetractCommand());
                _isRetracted = true;
            }
            return commands;
        }

        // For travel moves, retract if not already retracted
        if (toolpath.Type == ToolpathType.Travel && !_isRetracted)
        {
            commands.Add(CreateRetractCommand());
            _isRetracted = true;
        }

        // Process each point in the toolpath
        for (int i = 0; i < toolpath.Points.Count; i++)
        {
            var point = toolpath.Points[i];

            // For first extrusion point, prime if retracted
            if (i == 0 && toolpath.Type == ToolpathType.Extrude && _isRetracted)
            {
                commands.Add(CreatePrimeCommand());
                _isRetracted = false;
            }

            // Create motion command
            var command = new MotionCommand
            {
                Type = MotionType.Linear,
                StartPosition = new double[] { _currentPosition.X, _currentPosition.Y, _currentPosition.Z, 0, 0, 0, 0, 0 },
                EndPosition = new double[] { point.X, point.Y, point.Z, 0, 0, 0, 0, 0 },
                FeedRate = toolpath.Type == ToolpathType.Travel ? _config.TravelSpeed : toolpath.FeedRate,
                ExtrusionRate = toolpath.Type == ToolpathType.Extrude ? toolpath.ExtrusionRate : 0
            };

            commands.Add(command);
            _currentPosition = point;
        }

        return commands;
    }

    private MotionCommand CreateRetractCommand()
    {
        return new MotionCommand
        {
            Type = MotionType.Linear,
            StartPosition = new double[] { _currentPosition.X, _currentPosition.Y, _currentPosition.Z, 0, 0, 0, 0, 0 },
            EndPosition = new double[] { _currentPosition.X, _currentPosition.Y, _currentPosition.Z + _config.RetractLength, 0, 0, 0, 0, 0 },
            FeedRate = _config.RetractSpeed,
            ExtrusionRate = 0
        };
    }

    private MotionCommand CreatePrimeCommand()
    {
        return new MotionCommand
        {
            Type = MotionType.Linear,
            StartPosition = new double[] { _currentPosition.X, _currentPosition.Y, _currentPosition.Z, 0, 0, 0, 0, 0 },
            EndPosition = new double[] { _currentPosition.X, _currentPosition.Y, _currentPosition.Z - _config.RetractLength, 0, 0, 0, 0, 0 },
            FeedRate = _config.RetractSpeed,
            ExtrusionRate = 0
        };
    }
} 