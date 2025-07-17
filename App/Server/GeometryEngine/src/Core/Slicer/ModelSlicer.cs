using System.Numerics;
using SharedTypes;

namespace GeometryEngine.Core.Slicer;

/// <summary>
/// Handles slicing of 3D models into layers for 3D printing
/// </summary>
public class ModelSlicer
{
    private readonly Mesh _mesh;
    private readonly SlicingConfig _config;

    public ModelSlicer(Mesh mesh, SlicingConfig config)
    {
        _mesh = mesh;
        _config = config;
    }

    /// <summary>
    /// Slices the model into layers
    /// </summary>
    public async Task<List<Layer>> SliceAsync()
    {
        var layers = new List<Layer>();
        var layerCount = (int)((_mesh.MaxBounds.Z - _mesh.MinBounds.Z) / _config.LayerHeight);

        // Process each layer in parallel for better performance
        var layerTasks = new Task<Layer>[layerCount];
        for (int i = 0; i < layerCount; i++)
        {
            var z = _mesh.MinBounds.Z + (i * _config.LayerHeight);
            layerTasks[i] = Task.Run(() => ProcessLayer(z));
        }

        // Wait for all layers to be processed
        var processedLayers = await Task.WhenAll(layerTasks);
        layers.AddRange(processedLayers);

        // Sort layers by height
        layers.Sort((a, b) => a.Height.CompareTo(b.Height));

        return layers;
    }

    private Layer ProcessLayer(double z)
    {
        var layer = new Layer { Height = z };
        var intersections = _mesh.GetIntersections((float)z);

        // Group line segments into contours
        var contours = BuildContours(intersections);

        // Identify outer perimeters and holes
        foreach (var contour in contours)
        {
            // Calculate area to determine if it's a hole
            var area = CalculateContourArea(contour);
            contour.Type = area > 0 ? ContourType.Perimeter : ContourType.Hole;
            layer.Contours.Add(contour);
        }

        // Generate infill if needed
        if (_config.InfillDensity > 0)
        {
            layer.InfillPaths.AddRange(GenerateInfill(layer.Contours.ToList(), z));
        }

        return layer;
    }

    private List<Contour> BuildContours(List<LineSegment> segments)
    {
        var contours = new List<Contour>();
        var remainingSegments = new List<LineSegment>(segments);

        while (remainingSegments.Count > 0)
        {
            var contour = new Contour();
            var currentSegment = remainingSegments[0];
            remainingSegments.RemoveAt(0);

            contour.Points.Add(currentSegment.Start);
            contour.Points.Add(currentSegment.End);

            var connected = true;
            while (connected && remainingSegments.Count > 0)
            {
                connected = false;
                for (int i = remainingSegments.Count - 1; i >= 0; i--)
                {
                    var segment = remainingSegments[i];
                    var lastPoint = contour.Points[^1];

                    if (Vector2.Distance(segment.Start, lastPoint) < 0.001f)
                    {
                        contour.Points.Add(segment.End);
                        remainingSegments.RemoveAt(i);
                        connected = true;
                    }
                    else if (Vector2.Distance(segment.End, lastPoint) < 0.001f)
                    {
                        contour.Points.Add(segment.Start);
                        remainingSegments.RemoveAt(i);
                        connected = true;
                    }
                }
            }

            // Check if contour is closed
            if (Vector2.Distance(contour.Points[0], contour.Points[^1]) < 0.001f)
            {
                contour.IsClosed = true;
                contour.Points.RemoveAt(contour.Points.Count - 1); // Remove duplicate point
            }

            contours.Add(contour);
        }

        return contours;
    }

    private double CalculateContourArea(Contour contour)
    {
        double area = 0;
        for (int i = 0; i < contour.Points.Count; i++)
        {
            var j = (i + 1) % contour.Points.Count;
            area += contour.Points[i].X * contour.Points[j].Y;
            area -= contour.Points[j].X * contour.Points[i].Y;
        }
        return area / 2;
    }

    private List<Contour> GenerateInfill(List<Contour> contours, double z)
    {
        var infill = new List<Contour>();
        var bounds = GetContoursBounds(contours);
        var spacing = _config.ExtrusionWidth / _config.InfillDensity;

        switch (_config.InfillPattern.ToLower())
        {
            case "grid":
                infill.AddRange(GenerateGridInfill(contours, bounds, spacing));
                break;
            case "lines":
                infill.AddRange(GenerateLineInfill(contours, bounds, spacing, z));
                break;
            case "triangles":
                infill.AddRange(GenerateTriangleInfill(contours, bounds, spacing));
                break;
            // Add more infill patterns as needed
        }

        return infill;
    }

    private List<Contour> GenerateGridInfill(List<Contour> contours, (Vector2 min, Vector2 max) bounds, double spacing)
    {
        var infill = new List<Contour>();
        
        // Generate horizontal lines
        for (double y = bounds.min.Y; y <= bounds.max.Y; y += spacing)
        {
            var line = new LineSegment(
                new Vector2((float)bounds.min.X, (float)y),
                new Vector2((float)bounds.max.X, (float)y)
            );
            AddInfillLine(infill, line, contours);
        }

        // Generate vertical lines
        for (double x = bounds.min.X; x <= bounds.max.X; x += spacing)
        {
            var line = new LineSegment(
                new Vector2((float)x, (float)bounds.min.Y),
                new Vector2((float)x, (float)bounds.max.Y)
            );
            AddInfillLine(infill, line, contours);
        }

        return infill;
    }

    private List<Contour> GenerateLineInfill(List<Contour> contours, (Vector2 min, Vector2 max) bounds, double spacing, double z)
    {
        var infill = new List<Contour>();
        var angle = ((int)(z / _config.LayerHeight) % 2) * 90; // Alternate between 0 and 90 degrees
        var radians = angle * Math.PI / 180;

        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        // Calculate rotated bounds
        var width = bounds.max.X - bounds.min.X;
        var height = bounds.max.Y - bounds.min.Y;
        var rotatedWidth = Math.Abs(width * cos) + Math.Abs(height * sin);
        var rotatedHeight = Math.Abs(width * sin) + Math.Abs(height * cos);

        // Generate lines
        for (double d = -rotatedWidth; d <= rotatedWidth; d += spacing)
        {
            var line = new LineSegment(
                new Vector2(
                    (float)(bounds.min.X + d * cos - rotatedHeight * sin),
                    (float)(bounds.min.Y + d * sin + rotatedHeight * cos)
                ),
                new Vector2(
                    (float)(bounds.min.X + d * cos + rotatedHeight * sin),
                    (float)(bounds.min.Y + d * sin - rotatedHeight * cos)
                )
            );
            AddInfillLine(infill, line, contours);
        }

        return infill;
    }

    private List<Contour> GenerateTriangleInfill(List<Contour> contours, (Vector2 min, Vector2 max) bounds, double spacing)
    {
        var infill = new List<Contour>();
        var height = spacing * Math.Sqrt(3) / 2;

        // Generate horizontal lines
        for (double y = bounds.min.Y; y <= bounds.max.Y; y += height)
        {
            var offset = ((int)((y - bounds.min.Y) / height) % 2) * spacing / 2;
            var line = new LineSegment(
                new Vector2((float)(bounds.min.X + offset), (float)y),
                new Vector2((float)(bounds.max.X + offset), (float)y)
            );
            AddInfillLine(infill, line, contours);
        }

        // Generate diagonal lines
        for (double x = bounds.min.X - bounds.max.Y; x <= bounds.max.X + bounds.max.Y; x += spacing)
        {
            var line1 = new LineSegment(
                new Vector2((float)x, (float)bounds.min.Y),
                new Vector2((float)(x + bounds.max.Y - bounds.min.Y), (float)bounds.max.Y)
            );
            var line2 = new LineSegment(
                new Vector2((float)x, (float)bounds.min.Y),
                new Vector2((float)(x - bounds.max.Y + bounds.min.Y), (float)bounds.max.Y)
            );
            AddInfillLine(infill, line1, contours);
            AddInfillLine(infill, line2, contours);
        }

        return infill;
    }

    private void AddInfillLine(List<Contour> infill, LineSegment line, List<Contour> contours)
    {
        var intersections = new List<Vector2>();

        // Find all intersections with contours
        foreach (var contour in contours)
        {
            for (int i = 0; i < contour.Points.Count; i++)
            {
                var j = (i + 1) % contour.Points.Count;
                var segment = new LineSegment(contour.Points[i], contour.Points[j]);

                if (line.Intersects(segment, out var intersection))
                {
                    intersections.Add(intersection);
                }
            }
        }

        // Sort intersections by distance from line start
        intersections.Sort((a, b) =>
            Vector2.Distance(line.Start, a).CompareTo(Vector2.Distance(line.Start, b)));

        // Create infill segments between pairs of intersections
        for (int i = 0; i < intersections.Count - 1; i += 2)
        {
            var infillContour = new Contour
            {
                Points = new List<Vector2> { intersections[i], intersections[i + 1] },
                Type = ContourType.Infill
            };
            infill.Add(infillContour);
        }
    }

    private (Vector2 min, Vector2 max) GetContoursBounds(List<Contour> contours)
    {
        if (contours.Count == 0 || contours[0].Points.Count == 0)
            return (Vector2.Zero, Vector2.Zero);

        var min = contours[0].Points[0];
        var max = min;

        foreach (var contour in contours)
        {
            foreach (var point in contour.Points)
            {
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
        }

        return (min, max);
    }
} 