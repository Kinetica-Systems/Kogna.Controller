using System.Numerics;
using GeometryEngine.Core.Slicer;
using GeometryEngine.IO;
using SharedTypes;

namespace GeometryEngine.Implementation;

/// <summary>
/// Main implementation of the geometry engine for 3D printing
/// </summary>
public class GeometryEngine : IGeometryEngine
{
    private Mesh? _loadedMesh;
    private List<Layer>? _slicedLayers;
    private readonly object _lock = new object();
    private bool _disposed;

    /// <summary>
    /// Loads a 3D model from an STL file
    /// </summary>
    /// <param name="filePath">Path to the STL file</param>
    /// <returns>True if loading was successful</returns>
    /// <exception cref="ArgumentNullException">Thrown when filePath is null</exception>
    /// <exception cref="FileNotFoundException">Thrown when the STL file does not exist</exception>
    /// <exception cref="InvalidOperationException">Thrown when the engine has been disposed</exception>
    /// <exception cref="GeometryEngineException">Thrown when there is an error loading or processing the STL file</exception>
    public async Task<bool> LoadModelAsync(string filePath)
    {
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentNullException(nameof(filePath), "File path cannot be null or empty");
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("STL file not found", filePath);
        }

        try
        {
            Console.WriteLine($"[GEOMETRY_ENGINE] Loading STL file: {filePath}");
            
            lock (_lock)
            {
                // Clear any existing mesh data
                _loadedMesh = null;
                _slicedLayers = null;
            }

            var mesh = await StlReader.ReadFileAsync(filePath);
            
            if (mesh == null || mesh.Vertices.Count == 0)
            {
                throw new GeometryEngineException("STL file contains no valid mesh data");
            }

            if (mesh.Indices.Count % 3 != 0)
            {
                throw new GeometryEngineException("Invalid mesh: number of indices is not a multiple of 3");
            }

            lock (_lock)
            {
                _loadedMesh = mesh;
            }

            Console.WriteLine($"[GEOMETRY_ENGINE] Successfully loaded STL with {mesh.Vertices.Count} vertices and {mesh.Indices.Count / 3} triangles");
            return true;
        }
        catch (Exception ex) when (ex is not GeometryEngineException 
                                    && ex is not ArgumentNullException 
                                    && ex is not FileNotFoundException)
        {
            var error = $"Error loading STL file: {ex.Message}";
            Console.WriteLine($"[GEOMETRY_ENGINE] {error}");
            Console.WriteLine($"[GEOMETRY_ENGINE] Stack trace: {ex.StackTrace}");
            throw new GeometryEngineException(error, ex);
        }
    }

    /// <summary>
    /// Slices the loaded model into layers
    /// </summary>
    /// <param name="layerHeight">Height of each layer in millimeters</param>
    /// <param name="config">Slicing configuration parameters</param>
    /// <returns>Collection of sliced layers</returns>
    /// <exception cref="InvalidOperationException">Thrown when no model is loaded or the engine has been disposed</exception>
    /// <exception cref="ArgumentException">Thrown when layerHeight is invalid</exception>
    /// <exception cref="GeometryEngineException">Thrown when there is an error during slicing</exception>
    public async Task<IEnumerable<Layer>> SliceModelAsync(double layerHeight, SlicingConfig config)
    {
        ThrowIfDisposed();

        if (_loadedMesh == null)
        {
            throw new InvalidOperationException("No model loaded. Call LoadModel first.");
        }

        if (layerHeight <= 0)
        {
            throw new ArgumentException("Layer height must be greater than 0", nameof(layerHeight));
        }

        try
        {
            Console.WriteLine($"[GEOMETRY_ENGINE] Starting model slicing with layer height: {layerHeight}mm");
            var slicer = new ModelSlicer(_loadedMesh, config);
            
            var layers = await Task.Run(() => slicer.SliceAsync().Result);
            
            lock (_lock)
            {
                _slicedLayers = layers.ToList();
            }

            Console.WriteLine($"[GEOMETRY_ENGINE] Successfully sliced model into {_slicedLayers.Count} layers");
            return _slicedLayers;
        }
        catch (Exception ex)
        {
            var error = $"Error slicing model: {ex.Message}";
            Console.WriteLine($"[GEOMETRY_ENGINE] {error}");
            Console.WriteLine($"[GEOMETRY_ENGINE] Stack trace: {ex.StackTrace}");
            throw new GeometryEngineException(error, ex);
        }
    }

    /// <summary>
    /// Generates toolpaths for all layers
    /// </summary>
    /// <param name="layers">Collection of layers to process</param>
    /// <param name="config">Toolpath generation configuration</param>
    /// <returns>Collection of toolpaths</returns>
    /// <exception cref="ArgumentNullException">Thrown when layers or config is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when the engine has been disposed</exception>
    /// <exception cref="GeometryEngineException">Thrown when there is an error during toolpath generation</exception>
    public async Task<IEnumerable<Toolpath>> GenerateToolpathsAsync(IEnumerable<Layer> layers, ToolpathConfig config)
    {
        ThrowIfDisposed();

        if (layers == null)
        {
            throw new ArgumentNullException(nameof(layers));
        }

        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        try
        {
            return await Task.Run(() =>
            {
                var toolpaths = new List<Toolpath>();

                foreach (var layer in layers)
                {
                    // Process perimeters
                    foreach (var perimeter in layer.Perimeters)
                    {
                        toolpaths.Add(new Toolpath
                        {
                            Points = perimeter.Select(p => new Vector3(p.X, p.Y, (float)layer.Height)).ToList(),
                            FeedRate = config.PrintSpeed,
                            ExtrusionRate = CalculateExtrusionRate(config.ExtrusionWidth, layer.Height, config.PrintSpeed),
                            IsTravel = false,
                            Type = ToolpathType.Extrude
                        });
                    }

                    // Process infill
                    foreach (var infill in layer.Infill)
                    {
                        toolpaths.Add(new Toolpath
                        {
                            Points = infill.Select(p => new Vector3(p.X, p.Y, (float)layer.Height)).ToList(),
                            FeedRate = config.PrintSpeed,
                            ExtrusionRate = CalculateExtrusionRate(config.ExtrusionWidth, layer.Height, config.PrintSpeed),
                            IsTravel = false,
                            Type = ToolpathType.Extrude
                        });
                    }

                    // Add travel moves between paths
                    for (int i = 0; i < toolpaths.Count - 1; i++)
                    {
                        var current = toolpaths[i];
                        var next = toolpaths[i + 1];

                        if (current.Points.Count > 0 && next.Points.Count > 0)
                        {
                            toolpaths.Insert(i + 1, new Toolpath
                            {
                                Points = new List<Vector3> { current.Points.Last(), next.Points.First() },
                                FeedRate = config.TravelSpeed,
                                ExtrusionRate = 0,
                                IsTravel = true,
                                Type = ToolpathType.Travel
                            });
                            i++; // Skip the inserted travel move
                        }
                    }
                }

                return toolpaths;
            });
        }
        catch (Exception ex)
        {
            var error = $"Error generating toolpaths: {ex.Message}";
            Console.WriteLine($"[GEOMETRY_ENGINE] {error}");
            Console.WriteLine($"[GEOMETRY_ENGINE] Stack trace: {ex.StackTrace}");
            throw new GeometryEngineException(error, ex);
        }
    }

    /// <summary>
    /// Generates support structures for the model
    /// </summary>
    /// <param name="config">Support generation configuration</param>
    /// <returns>Collection of support structures</returns>
    /// <exception cref="ArgumentNullException">Thrown when config is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when no model is loaded or the engine has been disposed</exception>
    /// <exception cref="GeometryEngineException">Thrown when there is an error during support generation</exception>
    public async Task<IEnumerable<SupportStructure>> GenerateSupportsAsync(SupportConfig config)
    {
        ThrowIfDisposed();

        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        if (_loadedMesh == null)
        {
            throw new InvalidOperationException("No model loaded. Call LoadModel first.");
        }

        try
        {
            var supports = new List<SupportStructure>();

            // TODO: Implement actual support structure generation
            // For now, return an empty list
            await Task.CompletedTask;

            return supports;
        }
        catch (Exception ex)
        {
            var error = $"Error generating supports: {ex.Message}";
            Console.WriteLine($"[GEOMETRY_ENGINE] {error}");
            Console.WriteLine($"[GEOMETRY_ENGINE] Stack trace: {ex.StackTrace}");
            throw new GeometryEngineException(error, ex);
        }
    }

    /// <summary>
    /// Converts toolpaths to G-code commands
    /// </summary>
    /// <param name="toolpaths">Collection of toolpaths to convert</param>
    /// <param name="config">G-code generation configuration</param>
    /// <returns>List of G-code commands</returns>
    /// <exception cref="ArgumentNullException">Thrown when toolpaths or config is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when the engine has been disposed</exception>
    /// <exception cref="GeometryEngineException">Thrown when there is an error during G-code generation</exception>
    public async Task<IEnumerable<string>> GenerateGCodeAsync(IEnumerable<Toolpath> toolpaths, GCodeConfig config)
    {
        ThrowIfDisposed();

        if (toolpaths == null)
        {
            throw new ArgumentNullException(nameof(toolpaths));
        }

        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        try
        {
            var gcode = new List<string>();

            // Add header
            gcode.Add("; Generated by Kogna Geometry Engine");
            gcode.Add($"M104 S{config.NozzleTemperature} ; Set nozzle temperature");
            gcode.Add($"M140 S{config.BedTemperature} ; Set bed temperature");
            gcode.Add($"M106 S{config.FanSpeed} ; Set fan speed");
            gcode.Add("G21 ; Set units to millimeters");
            gcode.Add("G90 ; Use absolute coordinates");
            gcode.Add("G92 E0 ; Reset extruder");
            gcode.Add("M82 ; Use absolute distances for extrusion");

            // Process toolpaths
            double currentE = 0;
            foreach (var toolpath in toolpaths)
            {
                if (toolpath.Points.Count < 2) continue;

                if (toolpath.IsTravel)
                {
                    // Retraction
                    gcode.Add("G1 E-4 F2700 ; Retract");
                    gcode.Add("G0 Z0.2 ; Lift Z");
                    
                    // Travel move
                    var end = toolpath.Points.Last();
                    gcode.Add($"G0 X{end.X:F3} Y{end.Y:F3} F{toolpath.FeedRate * 60:F0}");
                    
                    // Prime
                    gcode.Add("G1 Z0 ; Lower Z");
                    gcode.Add("G1 E0 F2700 ; Prime");
                }
                else
                {
                    // Extrusion move
                    for (int i = 1; i < toolpath.Points.Count; i++)
                    {
                        var start = toolpath.Points[i - 1];
                        var end = toolpath.Points[i];
                        var length = Vector3.Distance(start, end);
                        currentE += length * toolpath.ExtrusionRate;
                        gcode.Add($"G1 X{end.X:F3} Y{end.Y:F3} E{currentE:F4} F{toolpath.FeedRate * 60:F0}");
                    }
                }
            }

            // Add footer
            gcode.Add("M104 S0 ; Turn off nozzle");
            gcode.Add("M140 S0 ; Turn off bed");
            gcode.Add("M107 ; Turn off fan");
            gcode.Add("G1 X0 Y0 ; Present print");
            gcode.Add("M84 ; Disable motors");

            await Task.CompletedTask; // Placeholder for future async operations

            return gcode;
        }
        catch (Exception ex)
        {
            var error = $"Error generating G-code: {ex.Message}";
            Console.WriteLine($"[GEOMETRY_ENGINE] {error}");
            Console.WriteLine($"[GEOMETRY_ENGINE] Stack trace: {ex.StackTrace}");
            throw new GeometryEngineException(error, ex);
        }
    }

    private double CalculateExtrusionRate(double width, double height, double speed)
    {
        // Calculate extrusion rate based on cross-sectional area and speed
        var area = width * height;
        return area / speed;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(GeometryEngine));
        }
    }

    /// <summary>
    /// Releases the unmanaged resources used by the GeometryEngine and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Clear any loaded data
                lock (_lock)
                {
                    _loadedMesh = null;
                    _slicedLayers = null;
                }
            }

            _disposed = true;
        }
    }

    /// <summary>
    /// Releases all resources used by the GeometryEngine.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Finalizer that ensures unmanaged resources are cleaned up if the object is not properly disposed.
    /// </summary>
    ~GeometryEngine()
    {
        Dispose(false);
    }
}

/// <summary>
/// Custom exception for geometry engine specific errors
/// </summary>
public class GeometryEngineException : Exception
{
    /// <summary>
    /// Initializes a new instance of the GeometryEngineException class with a specified error message
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    public GeometryEngineException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the GeometryEngineException class with a specified error message
    /// and a reference to the inner exception that is the cause of this exception
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="innerException">The exception that is the cause of the current exception</param>
    public GeometryEngineException(string message, Exception innerException) : base(message, innerException) { }
} 