using System;
using System.Buffers.Binary;
using System.Linq;
using System.Numerics;
using System.Xml;
using System.Windows.Media.Media3D;
using System.Windows.Media;
using System.Windows;
using System.Diagnostics;

namespace PssgViewer
{
    /// <summary>
    /// Handles binary reading and parsing of PSSG data
    /// </summary>
    public class PssgBinaryReader
    {
        /// <summary>
        /// Parse a DATABLOCKDATA node to extract vertices, normals, and texture coordinates
        /// </summary>
        public static void ParseGeometryData(XmlNode dataBlockNode, GeometryBlock block,
                                       out Point3DCollection positions,
                                       out Vector3DCollection normals,
                                       out PointCollection texCoords)
        {
            positions = new Point3DCollection();
            normals = new Vector3DCollection();
            texCoords = new PointCollection();

            // Find data streams
            DataStream posStream = block.Streams.FirstOrDefault(s => s.RenderType == "Vertex");
            DataStream normStream = block.Streams.FirstOrDefault(s => s.RenderType == "Normal");
            DataStream texStream = block.Streams.FirstOrDefault(s => s.RenderType == "ST");

            if (posStream == null)
            {
                Debug.WriteLine("No position stream found");
                GenerateTestVertices(positions, normals, texCoords);
                return;
            }

            // Find DATABLOCKDATA node
            XmlNode dataNode = dataBlockNode.SelectSingleNode("./DATABLOCKDATA");
            if (dataNode == null || string.IsNullOrWhiteSpace(dataNode.InnerText))
            {
                Debug.WriteLine("No DATABLOCKDATA found or it's empty");
                GenerateTestVertices(positions, normals, texCoords);
                return;
            }

            // Get raw data
            string rawData = dataNode.InnerText;

            // Try parsing as floats first
            if (TryParseFloatData(rawData, block, posStream, normStream, texStream,
                               positions, normals, texCoords))
            {
                return;
            }

            // Try binary parsing
            byte[] binaryData = null;

            // Try different conversion methods
            if ((binaryData = ConvertHexToBinary(rawData, block.ElementCount, posStream.Stride)) == null &&
                (binaryData = ConvertFloatTextToBinary(rawData, block.ElementCount, posStream.Stride)) == null &&
                (binaryData = ConvertDirectBinary(rawData, block.ElementCount, posStream.Stride)) == null)
            {
                Debug.WriteLine("All binary conversion methods failed");
                GenerateTestVertices(positions, normals, texCoords);
                return;
            }

            // Process each vertex
            int stride = posStream.Stride;
            for (int i = 0; i < block.ElementCount; i++)
            {
                int baseOffset = i * stride;
                if (baseOffset + stride > binaryData.Length) break;

                try
                {
                    // Extract position
                    if (posStream != null && baseOffset + posStream.Offset + 12 <= binaryData.Length)
                    {
                        Vector3 pos = ReadVector3BigEndian(binaryData, baseOffset + posStream.Offset);
                        positions.Add(new Point3D(pos.X, pos.Y, pos.Z));
                    }

                    // Extract normal
                    if (normStream != null && baseOffset + normStream.Offset + 12 <= binaryData.Length)
                    {
                        Vector3 norm = ReadVector3BigEndian(binaryData, baseOffset + normStream.Offset);
                        normals.Add(new Vector3D(norm.X, norm.Y, norm.Z));
                    }

                    // Extract texture coords
                    if (texStream != null && baseOffset + texStream.Offset + 4 <= binaryData.Length)
                    {
                        Vector2 tex;
                        if (texStream.DataType == "half2")
                            tex = ReadVector2HalfBigEndian(binaryData, baseOffset + texStream.Offset);
                        else
                            tex = ReadVector2BigEndian(binaryData, baseOffset + texStream.Offset);

                        texCoords.Add(new Point(tex.X, tex.Y));
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error processing vertex {i}: {ex.Message}");
                }
            }

            if (positions.Count == 0)
                GenerateTestVertices(positions, normals, texCoords);
        }

        /// <summary>
        /// Try to parse the data as space-separated float values
        /// </summary>
        private static bool TryParseFloatData(string data, GeometryBlock block,
                                      DataStream posStream, DataStream normStream, DataStream texStream,
                                      Point3DCollection positions, Vector3DCollection normals,
                                      PointCollection texCoords)
        {
            try
            {
                // Split by whitespace
                string[] tokens = data.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length < 3) return false;

                int floatsPerVertex = posStream.Stride / 4; // Assuming 4 bytes per float

                for (int i = 0; i < block.ElementCount && (i + 1) * floatsPerVertex <= tokens.Length; i++)
                {
                    int startIndex = i * floatsPerVertex;

                    // Extract position (always first 3 floats)
                    if (float.TryParse(tokens[startIndex], out float x) &&
                        float.TryParse(tokens[startIndex + 1], out float y) &&
                        float.TryParse(tokens[startIndex + 2], out float z))
                    {
                        positions.Add(new Point3D(x, y, z));

                        // Extract normal (next 3 floats if available)
                        if (normStream != null && startIndex + 5 < tokens.Length)
                        {
                            if (float.TryParse(tokens[startIndex + 3], out float nx) &&
                                float.TryParse(tokens[startIndex + 4], out float ny) &&
                                float.TryParse(tokens[startIndex + 5], out float nz))
                            {
                                normals.Add(new Vector3D(nx, ny, nz));
                            }
                        }

                        // Extract texture coords (next 2 floats if available)
                        if (texStream != null && startIndex + 7 < tokens.Length)
                        {
                            if (float.TryParse(tokens[startIndex + 6], out float u) &&
                                float.TryParse(tokens[startIndex + 7], out float v))
                            {
                                texCoords.Add(new Point(u, v));
                            }
                        }
                    }
                    else
                    {
                        // Not valid float data
                        return false;
                    }
                }

                return positions.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Generate some simple test vertices for visualization
        /// </summary>
        private static void GenerateTestVertices(Point3DCollection positions,
                                            Vector3DCollection normals,
                                            PointCollection texCoords)
        {
            // Create cube vertices
            positions.Add(new Point3D(-1, -1, 1));
            positions.Add(new Point3D(1, -1, 1));
            positions.Add(new Point3D(1, 1, 1));
            positions.Add(new Point3D(-1, 1, 1));
            positions.Add(new Point3D(-1, -1, -1));
            positions.Add(new Point3D(1, -1, -1));
            positions.Add(new Point3D(1, 1, -1));
            positions.Add(new Point3D(-1, 1, -1));

            // Add normals and UV coordinates
            for (int i = 0; i < 8; i++)
            {
                normals.Add(new Vector3D(0, 0, 1));
                texCoords.Add(new Point(0, 0));
            }
        }

        /// <summary>
        /// Parse index data from INDEXSOURCEDATA
        /// </summary>
        public static Int32Collection ParseIndices(XmlNode indexDataNode, XmlNode indexNode)
        {
            Int32Collection indices = new Int32Collection();

            if (indexDataNode == null || string.IsNullOrWhiteSpace(indexDataNode.InnerText))
                return indices;

            try
            {
                string format = indexNode.Attributes?["format"]?.Value ?? "ushort";
                int count = int.Parse(indexNode.Attributes?["count"]?.Value ?? "0");

                // Try direct parsing first
                string[] tokens = indexDataNode.InnerText.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                if (tokens.Length > 0)
                {
                    // Try to parse as direct integer values
                    bool allParsed = true;
                    foreach (string token in tokens)
                    {
                        if (int.TryParse(token, out int index))
                            indices.Add(index);
                        else
                        {
                            allParsed = false;
                            break;
                        }
                    }

                    if (allParsed && indices.Count > 0)
                        return indices;
                }

                // Fall back to binary parsing
                indices.Clear();

                // Try binary conversion
                byte[] binaryData = ConvertHexToBinary(indexDataNode.InnerText, count, format == "ushort" ? 2 : 4);
                if (binaryData == null)
                    binaryData = ConvertDirectBinary(indexDataNode.InnerText, count, format == "ushort" ? 2 : 4);

                if (binaryData == null || binaryData.Length == 0)
                {
                    // Generate test indices for cube if all conversion methods failed
                    GenerateTestIndices(indices);
                    return indices;
                }

                int stride = format == "ushort" ? 2 : 4;

                // Parse indices based on format
                for (int i = 0; i < binaryData.Length - stride + 1; i += stride)
                {
                    int index;
                    if (format == "ushort")
                        index = BinaryPrimitives.ReadUInt16BigEndian(binaryData.AsSpan(i));
                    else
                        index = (int)BinaryPrimitives.ReadUInt32BigEndian(binaryData.AsSpan(i));

                    indices.Add(index);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing indices: {ex.Message}");
                GenerateTestIndices(indices);
            }

            return indices;
        }

        private static void GenerateTestIndices(Int32Collection indices)
        {
            // Cube indices
            indices.Add(0); indices.Add(1); indices.Add(2);
            indices.Add(0); indices.Add(2); indices.Add(3);
            indices.Add(4); indices.Add(7); indices.Add(6);
            indices.Add(4); indices.Add(6); indices.Add(5);
            indices.Add(3); indices.Add(2); indices.Add(6);
            indices.Add(3); indices.Add(6); indices.Add(7);
            indices.Add(0); indices.Add(4); indices.Add(5);
            indices.Add(0); indices.Add(5); indices.Add(1);
            indices.Add(0); indices.Add(3); indices.Add(7);
            indices.Add(0); indices.Add(7); indices.Add(4);
            indices.Add(1); indices.Add(5); indices.Add(6);
            indices.Add(1); indices.Add(6); indices.Add(2);
        }

        /// <summary>
        /// Parse a transformation matrix from TRANSFORM node
        /// </summary>
        public static Transform3D ParseTransform(XmlNode transformNode)
        {
            if (transformNode == null || string.IsNullOrWhiteSpace(transformNode.InnerText))
                return null;

            try
            {
                // Try direct float parsing first
                string[] tokens = transformNode.InnerText.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                if (tokens.Length >= 16)
                {
                    float[] values = new float[16];
                    bool allParsed = true;

                    for (int i = 0; i < 16; i++)
                    {
                        if (!float.TryParse(tokens[i], out values[i]) ||
                            float.IsNaN(values[i]) || float.IsInfinity(values[i]) ||
                            Math.Abs(values[i]) > 10000)
                        {
                            allParsed = false;
                            break;
                        }
                    }

                    if (allParsed)
                    {
                        Matrix3D matrix = new Matrix3D(
                            values[0], values[1], values[2], values[3],
                            values[4], values[5], values[6], values[7],
                            values[8], values[9], values[10], values[11],
                            values[12], values[13], values[14], values[15]
                        );

                        if (matrix.HasInverse)
                            return new MatrixTransform3D(matrix);
                    }
                }

                // Fall back to binary parsing
                byte[] binaryData = ConvertHexToBinary(transformNode.InnerText, 16, 4);
                if (binaryData == null || binaryData.Length < 64)
                    binaryData = ConvertDirectBinary(transformNode.InnerText, 16, 4);

                if (binaryData == null || binaryData.Length < 64)
                    return new MatrixTransform3D(Matrix3D.Identity);

                // Parse matrix from binary
                float[] matrixValues = new float[16];
                bool validMatrix = true;

                for (int i = 0; i < 16; i++)
                {
                    float value = BinaryPrimitives.ReadSingleBigEndian(binaryData.AsSpan(i * 4));
                    if (float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) > 10000)
                    {
                        validMatrix = false;
                        break;
                    }
                    matrixValues[i] = value;
                }

                if (validMatrix)
                {
                    Matrix3D matrix = new Matrix3D(
                        matrixValues[0], matrixValues[1], matrixValues[2], matrixValues[3],
                        matrixValues[4], matrixValues[5], matrixValues[6], matrixValues[7],
                        matrixValues[8], matrixValues[9], matrixValues[10], matrixValues[11],
                        matrixValues[12], matrixValues[13], matrixValues[14], matrixValues[15]
                    );

                    if (matrix.HasInverse)
                        return new MatrixTransform3D(matrix);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing transform: {ex.Message}");
            }

            return new MatrixTransform3D(Matrix3D.Identity);
        }

        #region Binary Conversion Methods

        /// <summary>
        /// Convert hex string to binary data
        /// </summary>
        private static byte[] ConvertHexToBinary(string text, int elementCount, int stride)
        {
            try
            {
                // Clean up text and check if valid hex
                string cleanText = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
                if (!cleanText.All(c => IsHexChar(c)))
                    return null;

                // Ensure even number of characters
                if (cleanText.Length % 2 != 0)
                    cleanText += "0";

                // Parse hex
                int bytesToRead = Math.Min(cleanText.Length / 2, elementCount * stride);
                byte[] bytes = new byte[bytesToRead];

                for (int i = 0; i < bytesToRead; i++)
                {
                    int pos = i * 2;
                    if (pos + 1 < cleanText.Length)
                    {
                        string byteStr = cleanText.Substring(pos, 2);
                        bytes[i] = byte.Parse(byteStr, System.Globalization.NumberStyles.HexNumber);
                    }
                }

                return bytes;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Convert space-separated float text to binary
        /// </summary>
        private static byte[] ConvertFloatTextToBinary(string text, int elementCount, int stride)
        {
            try
            {
                // Split into tokens
                string[] tokens = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length < 3)
                    return null;

                // Estimate floats per vertex
                int floatsPerVertex = stride / 4;
                int floatCount = Math.Min(tokens.Length, elementCount * floatsPerVertex);
                byte[] result = new byte[floatCount * 4];

                for (int i = 0; i < floatCount; i++)
                {
                    if (float.TryParse(tokens[i], out float value))
                    {
                        byte[] floatBytes = BitConverter.GetBytes(value);
                        if (BitConverter.IsLittleEndian)
                            Array.Reverse(floatBytes);

                        Buffer.BlockCopy(floatBytes, 0, result, i * 4, 4);
                    }
                }

                return result;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Try to interpret the data as direct binary
        /// </summary>
        private static byte[] ConvertDirectBinary(string text, int elementCount, int stride)
        {
            try
            {
                // Try base64 first
                try
                {
                    return Convert.FromBase64String(text);
                }
                catch { }

                // Direct byte conversion
                byte[] bytes = new byte[Math.Min(text.Length, elementCount * stride)];
                for (int i = 0; i < bytes.Length; i++)
                {
                    if (i < text.Length)
                        bytes[i] = (byte)text[i];
                }

                return bytes;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsHexChar(char c) =>
            (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');

        #endregion

        #region Binary Reading Methods

        private static Vector3 ReadVector3BigEndian(byte[] data, int offset)
        {
            if (offset + 12 > data.Length)
                return new Vector3();

            return new Vector3(
                BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(offset)),
                BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(offset + 4)),
                BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(offset + 8))
            );
        }

        private static Vector2 ReadVector2BigEndian(byte[] data, int offset)
        {
            if (offset + 8 > data.Length)
                return new Vector2();

            return new Vector2(
                BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(offset)),
                BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(offset + 4))
            );
        }

        private static Vector2 ReadVector2HalfBigEndian(byte[] data, int offset)
        {
            if (offset + 4 > data.Length)
                return new Vector2();

            return new Vector2(
                HalfToFloat(BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset))),
                HalfToFloat(BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 2)))
            );
        }

        private static float HalfToFloat(ushort half)
        {
            // Extract components
            int sign = (half >> 15) & 0x1;
            int exp = (half >> 10) & 0x1F;
            int mantissa = half & 0x3FF;

            // Handle special cases
            if (exp == 0)
            {
                if (mantissa == 0)
                    return sign == 1 ? -0.0f : 0.0f;

                // Denormalized number
                float num = (float)mantissa / 1024.0f * (float)Math.Pow(2, -14);
                return sign == 1 ? -num : num;
            }
            else if (exp == 31)
            {
                if (mantissa == 0)
                    return sign == 1 ? float.NegativeInfinity : float.PositiveInfinity;
                else
                    return float.NaN;
            }

            // Normalized number
            float value = (1.0f + (float)mantissa / 1024.0f) * (float)Math.Pow(2, exp - 15);
            return sign == 1 ? -value : value;
        }

        #endregion
    }
}