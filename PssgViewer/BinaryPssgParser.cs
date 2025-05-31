using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace PssgViewer
{
    /// <summary>
    /// Parser that converts binary PSSG archives into an XmlDocument.
    /// </summary>
    public static class BinaryPssgParser
    {
        /// <summary>
        /// Parse a PSSG file. Returns null if the file does not start with the
        /// "PSSG" signature.
        /// </summary>
        public static XmlDocument? Parse(string filePath)
        {
            using FileStream fs = File.OpenRead(filePath);
            using BinaryReader br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: false);

            // Check for the signature
            byte[] sig = br.ReadBytes(4);
            if (sig.Length != 4 || Encoding.ASCII.GetString(sig) != "PSSG")
                return null;

            // Header fields
            uint fileSize = ReadUInt32BE(br);
            uint stringTableOffset = ReadUInt32BE(br);
            uint rootOffset = ReadUInt32BE(br);
            _ = ReadUInt32BE(br); // expected constant 1
            _ = ReadUInt32BE(br); // expected constant 7

            // Build element and attribute tables from the string table
            Dictionary<uint, string> elementNames = new();
            Dictionary<uint, string> attributeNames = new();

            br.BaseStream.Seek(stringTableOffset, SeekOrigin.Begin);

            List<string> strings = new();
            while (br.BaseStream.Position < fileSize + 8) // fileSize excludes signature & size field
            {
                List<byte> b = new();
                byte val;
                while (br.BaseStream.Position < fileSize + 8 && (val = br.ReadByte()) != 0)
                {
                    b.Add(val);
                }
                if (b.Count == 0)
                    break;
                strings.Add(Encoding.ASCII.GetString(b.ToArray()));
            }

            for (uint i = 0; i < strings.Count; i++)
            {
                elementNames[i] = strings[(int)i];
                attributeNames[i] = strings[(int)i];
            }

            br.BaseStream.Seek(rootOffset, SeekOrigin.Begin);

            // Root node
            XmlDocument doc = new XmlDocument();
            XmlElement root = ReadNode(br, doc, elementNames, attributeNames);
            doc.AppendChild(root);
            return doc;
        }

        private static XmlElement ReadNode(BinaryReader br, XmlDocument doc,
                                           Dictionary<uint, string> elementNames,
                                           Dictionary<uint, string> attributeNames)
        {
            uint elementIndex = ReadUInt32BE(br);
            uint totalSize = ReadUInt32BE(br);
            long blockEnd = br.BaseStream.Position + totalSize;

            uint attrDataSize = ReadUInt32BE(br);
            long attrEnd = br.BaseStream.Position + attrDataSize;

            // Create XML element
            string nodeName = elementNames.TryGetValue(elementIndex, out string? en) ? en : $"NODE_{elementIndex}";
            XmlElement elem = doc.CreateElement(nodeName);

            // Attributes
            while (br.BaseStream.Position < attrEnd)
            {
                uint attrId = ReadUInt32BE(br);
                uint valSize = ReadUInt32BE(br);

                string attrName = attributeNames.TryGetValue(attrId, out string? an) ? an : $"ATTR_{attrId}";
                string valueStr = ReadAttributeValue(br, valSize);
                elem.SetAttribute(attrName, valueStr);
            }

            long remaining = blockEnd - br.BaseStream.Position;
            if (remaining > 0)
            {
                bool hasChildren = attrDataSize > 0;
                if (!hasChildren && remaining >= 12)
                {
                    long pos = br.BaseStream.Position;
                    uint nextIdx = ReadUInt32BE(br);
                    br.BaseStream.Position = pos;
                    if (elementNames.ContainsKey(nextIdx))
                        hasChildren = true;
                }

                if (hasChildren)
                {
                    while (br.BaseStream.Position < blockEnd)
                    {
                        XmlElement child = ReadNode(br, doc, elementNames, attributeNames);
                        elem.AppendChild(child);
                    }
                }
                else
                {
                    byte[] data = br.ReadBytes((int)remaining);
                    elem.InnerText = Convert.ToBase64String(data);
                }
            }

            // Ensure we end exactly at blockEnd
            if (br.BaseStream.Position != blockEnd)
                br.BaseStream.Position = blockEnd;
            return elem;
        }

        private static string ReadAttributeValue(BinaryReader br, uint size)
        {
            if (size == 4)
            {
                int raw = ReadInt32BE(br);
                object val = (raw < -100000 || raw > 100000)
                    ? BitConverter.Int32BitsToSingle(raw)
                    : raw;
                if (val is float f)
                    return f.ToString(CultureInfo.InvariantCulture);
                return ((int)val).ToString(CultureInfo.InvariantCulture);
            }
            else if (size > 4)
            {
                byte[] bytes = br.ReadBytes((int)size);
                if (size >= 4 && BinaryPrimitives.ReadUInt32BigEndian(bytes) == size - 4)
                    return Encoding.UTF8.GetString(bytes, 4, (int)size - 4);
                return Convert.ToBase64String(bytes);
            }
            else
            {
                byte[] bytes = br.ReadBytes((int)size);
                return Convert.ToBase64String(bytes);
            }
        }

        private static uint ReadUInt32BE(BinaryReader br)
        {
            Span<byte> buf = stackalloc byte[4];
            br.Read(buf);
            return BinaryPrimitives.ReadUInt32BigEndian(buf);
        }

        private static int ReadInt32BE(BinaryReader br)
        {
            Span<byte> buf = stackalloc byte[4];
            br.Read(buf);
            return BinaryPrimitives.ReadInt32BigEndian(buf);
        }
    }
}
