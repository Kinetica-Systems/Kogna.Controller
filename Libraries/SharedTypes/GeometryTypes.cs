using System.Numerics;

namespace SharedTypes;

/// <summary>
/// Main interface for the geometry engine that handles 3D model slicing and toolpath generation
/// </summary>
public interface IGeometryEngine
{
    /// <summary>
    /// Loads a 3D model from an STL file
    /// </summary>
    /// <param name="filePath">Path to the STL file</param>
    /// <returns>True if loading was successful</returns>
    Task<bool> LoadModelAsync(string filePath);

    /// <summary>
    /// Slices the loaded model into layers
    /// </summary>
    /// <param name="layerHeight">Height of each layer in mm</param>
    /// <param name="config">Slicing configuration parameters</param>
    /// <returns>Collection of layers</returns>
    Task<IEnumerable<Layer>> SliceModelAsync(double layerHeight, SlicingConfig config);

    /// <summary>
    /// Generates toolpaths for all layers
    /// </summary>
    /// <param name="layers">Collection of layers to process</param>
    /// <param name="config">Toolpath generation configuration</param>
    /// <returns>Collection of toolpaths</returns>
    Task<IEnumerable<Toolpath>> GenerateToolpathsAsync(IEnumerable<Layer> layers, ToolpathConfig config);

    /// <summary>
    /// Generates support structures for the model
    /// </summary>
    /// <param name="config">Support generation configuration</param>
    /// <returns>Collection of support structures</returns>
    Task<IEnumerable<SupportStructure>> GenerateSupportsAsync(SupportConfig config);

    /// <summary>
    /// Converts toolpaths to G-code commands
    /// </summary>
    /// <param name="toolpaths">Collection of toolpaths to convert</param>
    /// <param name="config">G-code generation configuration</param>
    /// <returns>List of G-code commands</returns>
    Task<IEnumerable<string>> GenerateGCodeAsync(IEnumerable<Toolpath> toolpaths, GCodeConfig config);
}

/// <summary>
/// Represents a line segment in 2D space
/// </summary>
public class LineSegment
{
    /// <summary>
    /// Gets or sets the start point of the line segment
    /// </summary>
    public Vector2 Start { get; set; }

    /// <summary>
    /// Gets or sets the end point of the line segment
    /// </summary>
    public Vector2 End { get; set; }

    /// <summary>
    /// Gets the length of the line segment
    /// </summary>
    public float Length => Vector2.Distance(Start, End);

    /// <summary>
    /// Initializes a new instance of the LineSegment class
    /// </summary>
    /// <param name="start">Start point of the line segment</param>
    /// <param name="end">End point of the line segment</param>
    public LineSegment(Vector2 start, Vector2 end)
    {
        Start = start;
        End = end;
    }

    /// <summary>
    /// Checks if this line segment intersects with another line segment
    /// </summary>
    /// <param name="other">The other line segment to check intersection with</param>
    /// <param name="intersection">The point of intersection if the segments intersect</param>
    /// <returns>True if the segments intersect, false otherwise</returns>
    public bool Intersects(LineSegment other, out Vector2 intersection)
    {
        intersection = Vector2.Zero;

        var p = Start;
        var q = other.Start;
        var r = End - Start;
        var s = other.End - other.Start;

        var rxs = r.X * s.Y - r.Y * s.X;
        var qpxr = (q.X - p.X) * r.Y - (q.Y - p.Y) * r.X;

        // If r × s = 0 and (q - p) × r = 0, then the lines are collinear
        if (Math.Abs(rxs) < float.Epsilon && Math.Abs(qpxr) < float.Epsilon)
            return false;

        // If r × s = 0 and (q - p) × r ≠ 0, then the lines are parallel
        if (Math.Abs(rxs) < float.Epsilon)
            return false;

        var t = ((q.X - p.X) * s.Y - (q.Y - p.Y) * s.X) / rxs;
        var u = qpxr / rxs;

        if (t >= 0 && t <= 1 && u >= 0 && u <= 1)
        {
            intersection = p + t * r;
            return true;
        }

        return false;
    }
}

/// <summary>
/// Represents a 3D mesh with vertices and indices
/// </summary>
public class Mesh
{
    /// <summary>
    /// Gets or sets the list of vertices in the mesh
    /// </summary>
    public List<Vector3> Vertices { get; set; } = new List<Vector3>();

    /// <summary>
    /// Gets or sets the list of indices defining triangles in the mesh
    /// </summary>
    public List<int> Indices { get; set; } = new List<int>();

    /// <summary>
    /// Gets or sets the minimum bounds of the mesh
    /// </summary>
    public Vector3 MinBounds { get; set; }

    /// <summary>
    /// Gets or sets the maximum bounds of the mesh
    /// </summary>
    public Vector3 MaxBounds { get; set; }

    /// <summary>
    /// Gets the intersections of the mesh with a horizontal plane at the specified Z height
    /// </summary>
    /// <param name="z">The Z height to intersect with</param>
    /// <returns>A list of line segments representing the intersections</returns>
    public List<LineSegment> GetIntersections(float z)
    {
        var intersections = new List<LineSegment>();

        // Process each triangle
        for (int i = 0; i < Indices.Count; i += 3)
        {
            var v1 = Vertices[Indices[i]];
            var v2 = Vertices[Indices[i + 1]];
            var v3 = Vertices[Indices[i + 2]];

            // Check if the triangle intersects with the Z plane
            if ((v1.Z <= z && v2.Z >= z) || (v1.Z >= z && v2.Z <= z) ||
                (v2.Z <= z && v3.Z >= z) || (v2.Z >= z && v3.Z <= z) ||
                (v3.Z <= z && v1.Z >= z) || (v3.Z >= z && v1.Z <= z))
            {
                // Calculate intersection points
                var points = new List<Vector2>();

                void AddIntersection(Vector3 start, Vector3 end)
                {
                    if ((start.Z <= z && end.Z >= z) || (start.Z >= z && end.Z <= z))
                    {
                        var t = (z - start.Z) / (end.Z - start.Z);
                        var x = start.X + t * (end.X - start.X);
                        var y = start.Y + t * (end.Y - start.Y);
                        points.Add(new Vector2(x, y));
                    }
                }

                AddIntersection(v1, v2);
                AddIntersection(v2, v3);
                AddIntersection(v3, v1);

                // If we found exactly 2 intersection points, create a line segment
                if (points.Count == 2)
                {
                    intersections.Add(new LineSegment(points[0], points[1]));
                }
            }
        }

        return intersections;
    }

    /// <summary>
    /// Creates a deep copy of the mesh
    /// </summary>
    /// <returns>A new Mesh instance with copied data</returns>
    public Mesh Clone()
    {
        return new Mesh
        {
            Vertices = new List<Vector3>(Vertices),
            Indices = new List<int>(Indices),
            MinBounds = MinBounds,
            MaxBounds = MaxBounds
        };
    }
}

/// <summary>
/// Configuration for slicing a 3D model
/// </summary>
public class SlicingConfig
{
    /// <summary>
    /// Gets or sets the layer height in millimeters
    /// </summary>
    public double LayerHeight { get; set; } = 0.2;

    /// <summary>
    /// Gets or sets the number of perimeter shells
    /// </summary>
    public int PerimeterCount { get; set; } = 3;

    /// <summary>
    /// Gets or sets the infill density (0.0 to 1.0)
    /// </summary>
    public double InfillDensity { get; set; } = 0.2;

    /// <summary>
    /// Gets or sets the infill pattern type
    /// </summary>
    public string InfillPattern { get; set; } = "grid";

    /// <summary>
    /// Gets or sets the extrusion width in millimeters
    /// </summary>
    public double ExtrusionWidth { get; set; } = 0.4;
}

/// <summary>
/// Configuration for toolpath generation
/// </summary>
public class ToolpathConfig
{
    /// <summary>
    /// Gets or sets the extrusion width in mm
    /// </summary>
    public double ExtrusionWidth { get; set; } = 0.4;

    /// <summary>
    /// Gets or sets the print speed in mm/s
    /// </summary>
    public double PrintSpeed { get; set; } = 60;

    /// <summary>
    /// Gets or sets the travel speed in mm/s
    /// </summary>
    public double TravelSpeed { get; set; } = 120;

    /// <summary>
    /// Gets or sets the retraction length in mm
    /// </summary>
    public double RetractLength { get; set; } = 4;

    /// <summary>
    /// Gets or sets the retraction speed in mm/s
    /// </summary>
    public double RetractSpeed { get; set; } = 45;
}

/// <summary>
/// Configuration for support structure generation
/// </summary>
public class SupportConfig
{
    /// <summary>
    /// Gets or sets the minimum overhang angle in degrees
    /// </summary>
    public double MinOverhangAngle { get; set; } = 45;

    /// <summary>
    /// Gets or sets the support density (0.0 to 1.0)
    /// </summary>
    public double Density { get; set; } = 0.3;

    /// <summary>
    /// Gets or sets the support pattern type
    /// </summary>
    public string Pattern { get; set; } = "lines";
}

/// <summary>
/// Configuration for G-code generation
/// </summary>
public class GCodeConfig
{
    /// <summary>
    /// Gets or sets the nozzle temperature in Celsius
    /// </summary>
    public double NozzleTemperature { get; set; } = 200;

    /// <summary>
    /// Gets or sets the bed temperature in Celsius
    /// </summary>
    public double BedTemperature { get; set; } = 60;

    /// <summary>
    /// Gets or sets the fan speed (0 to 255)
    /// </summary>
    public int FanSpeed { get; set; } = 255;

    // Added properties for preview configuration
    public double StartX { get; set; } = 0;
    public double StartY { get; set; } = 0;
    public double StartZ { get; set; } = 0;
    public bool RelativeExtrusion { get; set; } = false;
    public string StartGCode { get; set; } = string.Empty;
    public string EndGCode { get; set; } = string.Empty;
}

/// <summary>
/// Represents a layer in the sliced model
/// </summary>
public class Layer
{
    /// <summary>
    /// Gets or sets the Z height of the layer
    /// </summary>
    public double Height { get; set; }

    /// <summary>
    /// Gets or sets the contours in the layer
    /// </summary>
    public List<Contour> Contours { get; set; } = new List<Contour>();

    /// <summary>
    /// Gets or sets the perimeter paths
    /// </summary>
    public List<List<Vector2>> Perimeters { get; set; } = new List<List<Vector2>>();

    /// <summary>
    /// Gets or sets the infill paths
    /// </summary>
    public List<List<Vector2>> Infill { get; set; } = new List<List<Vector2>>();

    /// <summary>
    /// Gets or sets the infill paths (legacy)
    /// </summary>
    public List<Contour> InfillPaths { get; set; } = new List<Contour>();

    /// <summary>
    /// Gets or sets the support paths
    /// </summary>
    public List<List<Vector2>> Support { get; set; } = new List<List<Vector2>>();
}

/// <summary>
/// Represents a toolpath segment
/// </summary>
public class Toolpath
{
    /// <summary>
    /// Gets or sets the points in the toolpath
    /// </summary>
    public List<Vector3> Points { get; set; } = new List<Vector3>();

    /// <summary>
    /// Gets or sets whether this is a travel move
    /// </summary>
    public bool IsTravel { get; set; }

    /// <summary>
    /// Gets or sets whether this is a retraction move
    /// </summary>
    public bool IsRetraction { get; set; }

    /// <summary>
    /// Gets or sets the type of toolpath
    /// </summary>
    public ToolpathType Type { get; set; }

    /// <summary>
    /// Gets or sets the extrusion rate in mm³/s
    /// </summary>
    public double ExtrusionRate { get; set; }

    /// <summary>
    /// Gets or sets the feed rate in mm/s
    /// </summary>
    public double FeedRate { get; set; }
}

/// <summary>
/// Types of toolpath segments
/// </summary>
public enum ToolpathType
{
    /// <summary>
    /// Travel move without extrusion
    /// </summary>
    Travel,

    /// <summary>
    /// Extrusion move
    /// </summary>
    Extrude,

    /// <summary>
    /// Retraction move
    /// </summary>
    Retract,

    /// <summary>
    /// Prime move after retraction
    /// </summary>
    Prime
}

/// <summary>
/// Represents a support structure
/// </summary>
public class SupportStructure
{
    /// <summary>
    /// Gets or sets the base points of the support
    /// </summary>
    public List<Vector2> BasePoints { get; set; } = new List<Vector2>();

    /// <summary>
    /// Gets or sets the height of the support
    /// </summary>
    public double Height { get; set; }

    /// <summary>
    /// Gets or sets the density of the support
    /// </summary>
    public double Density { get; set; }
}

/// <summary>
/// Represents a contour in a sliced layer
/// </summary>
public class Contour
{
    /// <summary>
    /// Gets or sets the points that make up the contour
    /// </summary>
    public List<Vector2> Points { get; set; } = new List<Vector2>();

    /// <summary>
    /// Gets or sets whether the contour is closed (first and last points are connected)
    /// </summary>
    public bool IsClosed { get; set; }

    /// <summary>
    /// Gets or sets the type of the contour
    /// </summary>
    public ContourType Type { get; set; }
}

/// <summary>
/// Types of contours in a sliced layer
/// </summary>
public enum ContourType
{
    /// <summary>
    /// Outer perimeter of the model
    /// </summary>
    Perimeter,

    /// <summary>
    /// Inner hole in the model
    /// </summary>
    Hole,

    /// <summary>
    /// Infill pattern
    /// </summary>
    Infill,

    /// <summary>
    /// Support structure
    /// </summary>
    Support,

    /// <summary>
    /// Skirt around the model
    /// </summary>
    Skirt,

    /// <summary>
    /// Brim for better bed adhesion
    /// </summary>
    Brim
} 