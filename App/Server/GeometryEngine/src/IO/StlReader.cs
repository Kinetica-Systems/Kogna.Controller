using System.Numerics;
using System.Text;
using SharedTypes;

namespace GeometryEngine.IO;

public static class StlReader
{
    private const int HEADER_SIZE = 80;
    private const int SIZE_OF_FACET = 50;
    private const float SCALE = 1.0f;

    /// <summary>
    /// Reads an STL file asynchronously and returns a Mesh object.
    /// </summary>
    /// <param name="filePath">Path to the STL file</param>
    /// <returns>A Task that represents the asynchronous operation, containing the loaded Mesh</returns>
    public static async Task<Mesh> ReadFileAsync(string filePath)
    {
        var mesh = new Mesh();
        
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(fileStream, Encoding.ASCII, true);

        // Read and validate header
        var header = await ReadBytesAsync(fileStream, HEADER_SIZE);
        var headerStr = Encoding.ASCII.GetString(header).Trim('\0');
        
        // Read number of triangles
        var triangleCountBytes = await ReadBytesAsync(fileStream, 4);
        var triangleCount = BitConverter.ToInt32(triangleCountBytes, 0);

        // Pre-allocate collections
        mesh.Vertices = new List<Vector3>(triangleCount * 3);
        mesh.Indices = new List<int>(triangleCount * 3);

        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var minZ = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;
        var maxZ = float.MinValue;

        // Read all triangles
        for (int i = 0; i < triangleCount; i++)
        {
            var facetData = await ReadBytesAsync(fileStream, SIZE_OF_FACET);
            
            // Skip normal vector (12 bytes)
            int offset = 12;

            // Read vertices
            for (int j = 0; j < 3; j++)
            {
                var x = BitConverter.ToSingle(facetData, offset) * SCALE;
                var y = BitConverter.ToSingle(facetData, offset + 4) * SCALE;
                var z = BitConverter.ToSingle(facetData, offset + 8) * SCALE;
                offset += 12;

                var vertex = new Vector3(x, y, z);
                mesh.Vertices.Add(vertex);
                mesh.Indices.Add(mesh.Vertices.Count - 1);

                // Update bounds
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                minZ = Math.Min(minZ, z);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                maxZ = Math.Max(maxZ, z);
            }

            // Skip attribute byte count (2 bytes)
            // offset += 2; // Not needed as we're done with the facet
        }

        mesh.MinBounds = new Vector3(minX, minY, minZ);
        mesh.MaxBounds = new Vector3(maxX, maxY, maxZ);

        return mesh;
    }

    private static async Task<byte[]> ReadBytesAsync(Stream stream, int count)
    {
        var buffer = new byte[count];
        var totalBytesRead = 0;

        while (totalBytesRead < count)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(totalBytesRead, count - totalBytesRead));
            if (bytesRead == 0)
            {
                throw new EndOfStreamException($"End of stream reached before reading {count} bytes");
            }
            totalBytesRead += bytesRead;
        }

        return buffer;
    }
} 