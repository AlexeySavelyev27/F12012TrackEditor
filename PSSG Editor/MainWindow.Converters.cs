using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PSSGEditor
{
    public partial class MainWindow
    {
        private string BytesToDisplay(string name, byte[] b)
        {
            if (b == null) return string.Empty;

            // Special handling for raw bytes shown in the Raw Data panel
            if (name == "__data__" && currentNode != null)
            {
                // Raw data for TRANSFORM or BOUNDINGBOX is treated as a float array
                if ((currentNode.Name.Equals("TRANSFORM", StringComparison.OrdinalIgnoreCase) ||
                     currentNode.Name.Equals("BOUNDINGBOX", StringComparison.OrdinalIgnoreCase)) &&
                    b.Length % 4 == 0)
                {
                    int count = b.Length / 4;
                    var sb = new StringBuilder();
                    for (int i = 0; i < count; i++)
                    {
                        float v = ReadFloatFromBytes(b, i * 4);
                        if (i > 0)
                            sb.Append(' ');
                        sb.Append(v.ToString("F6"));
                    }
                    return sb.ToString();
                }

                // All other raw data blocks are displayed as uppercase hex
                return BitConverter.ToString(b).Replace("-", " ").ToUpperInvariant();
            }

            // 1) Числа маленькой длины
            if (b.Length == 1)
                return b[0].ToString();
            if (b.Length == 2)
                return ReadUInt16FromBytes(b, 0).ToString();
            if (b.Length == 4)
            {
                uint intVal = ReadUInt32FromBytes(b, 0);
                float fVal = BitConverter.Int32BitsToSingle((int)intVal);
                uint exp = (intVal >> 23) & 0xFF;
                if (exp != 0 && exp != 0xFF && !float.IsNaN(fVal) && !float.IsInfinity(fVal) &&
                    Math.Abs(fVal) > 1e-6f && Math.Abs(fVal) < 1e6f)
                {
                    return fVal.ToString("F6");
                }
                return intVal.ToString();
            }

            // 2) length-prefixed UTF-8 string
            if (b.Length > 4)
            {
                uint sz = ReadUInt32FromBytes(b, 0);
                if (sz == b.Length - 4)
                {
                    try
                    {
                        return Encoding.UTF8.GetString(b, 4, (int)sz);
                    }
                    catch { }
                }
            }

            // 3) Transform/BoundingBox attribute: массив float
            if ((name.Equals("Transform", StringComparison.OrdinalIgnoreCase) ||
                 name.Equals("BoundingBox", StringComparison.OrdinalIgnoreCase)) &&
                b.Length % 4 == 0)
            {
                int count = b.Length / 4;
                var sb = new StringBuilder();
                for (int i = 0; i < count; i++)
                {
                    float v = ReadFloatFromBytes(b, i * 4);
                    if (i > 0)
                        sb.Append(' ');
                    sb.Append(v.ToString("F6"));
                }
                return sb.ToString();
            }

            // 4) Попытка трактовать как печатаемую UTF-8 строку
            try
            {
                string txt = Encoding.UTF8.GetString(b);
                if (txt.All(c => c >= 32 && c < 127))
                    return txt;
            }
            catch { }

            // 5) fallback – hex-строка
            return Convert.ToHexString(b).ToLowerInvariant();
        }

        private byte[] DisplayToBytes(string name, string s, int originalLength)
        {
            // Число
            if (ulong.TryParse(s, out ulong num))
            {
                try
                {
                    if (originalLength == 1)
                        return new byte[] { (byte)num };
                    if (originalLength == 2)
                        return ToBigEndian((ushort)num);
                    // По умолчанию – 4-byte UInt32
                    return ToBigEndian((uint)num);
                }
                catch { }
            }

            // Hex (например, "0A 0B 0C" или "0x0a0b0c")
            string hex = s.Trim();
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hex = hex.Substring(2);
            hex = hex.Replace(" ", string.Empty)
                     .Replace("\n", string.Empty)
                     .Replace("\r", string.Empty);
            if (hex.Length > 0 && hex.All(c => Uri.IsHexDigit(c)))
            {
                try
                {
                    int byteLen = hex.Length / 2;
                    var result = new byte[byteLen];
                    for (int i = 0; i < byteLen; i++)
                        result[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                    return result;
                }
                catch { }
            }

            // Single float value when original length is 4 bytes
            if (originalLength == 4 && float.TryParse(s, out float singleVal))
            {
                uint bits = BitConverter.SingleToUInt32Bits(singleVal);
                if (BitConverter.IsLittleEndian)
                    bits = BinaryPrimitives.ReverseEndianness(bits);
                var arr = new byte[4];
                BinaryPrimitives.WriteUInt32BigEndian(arr, bits);
                return arr;
            }

            // Transform/BoundingBox: список float’ов
            if (name.Equals("Transform", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("BoundingBox", StringComparison.OrdinalIgnoreCase) ||
                (name == "__data__" && currentNode != null &&
                 (currentNode.Name.Equals("TRANSFORM", StringComparison.OrdinalIgnoreCase) ||
                  currentNode.Name.Equals("BOUNDINGBOX", StringComparison.OrdinalIgnoreCase))))
            {
                var parts = s.Replace(",", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var floats = new List<float>();
                foreach (var p in parts)
                {
                    if (float.TryParse(p, out float vv))
                        floats.Add(vv);
                }
                var result = new byte[floats.Count * 4];
                for (int i = 0; i < floats.Count; i++)
                {
                    uint bits = BitConverter.SingleToUInt32Bits(floats[i]);
                    if (BitConverter.IsLittleEndian)
                        bits = BinaryPrimitives.ReverseEndianness(bits);
                    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(i * 4), bits);
                }
                return result;
            }

            // Иначе UTF-8 строка с 4-byte big-endian префиксом длины
            var strBytes = Encoding.UTF8.GetBytes(s);
            var rented = ArrayPool<byte>.Shared.Rent(strBytes.Length + 4);
            BinaryPrimitives.WriteUInt32BigEndian(rented.AsSpan(), (uint)strBytes.Length);
            strBytes.CopyTo(rented.AsSpan(4));
            var final = rented.AsSpan(0, strBytes.Length + 4).ToArray();
            ArrayPool<byte>.Shared.Return(rented);
            return final;
        }

        private uint ReadUInt32FromBytes(byte[] arr, int offset)
        {
            return BinaryPrimitives.ReadUInt32BigEndian(arr.AsSpan(offset));
        }

        private ushort ReadUInt16FromBytes(byte[] arr, int offset)
        {
            return BinaryPrimitives.ReadUInt16BigEndian(arr.AsSpan(offset));
        }

        private float ReadFloatFromBytes(byte[] arr, int offset)
        {
            uint intVal = BinaryPrimitives.ReadUInt32BigEndian(arr.AsSpan(offset));
            return BitConverter.Int32BitsToSingle((int)intVal);
        }

        private byte[] ToBigEndian(ushort value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return bytes;
        }

        private byte[] ToBigEndian(uint value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return bytes;
        }
    }
}

