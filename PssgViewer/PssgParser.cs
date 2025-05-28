using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace PssgViewer.Core
{
    internal static class PssgParser
    {
        // ------------------------------------------------------------
        //  Public entry point
        // ------------------------------------------------------------
        public static XDocument ParseToXDocument(string path)
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);

            // 1. Header ------------------------------------------------
            var sig = br.ReadBytes(4);
            if (!sig.SequenceEqual(Encoding.ASCII.GetBytes("PSSG")))
                throw new InvalidDataException("Not a PSSG file");

            uint fileSize = ReadBEUInt32(br);
            uint strTabOffset = ReadBEUInt32(br);
            uint rootOffset = ReadBEUInt32(br);

            br.ReadBytes(8); // two constants – usually 1 and 7, ignore for now

            // 2. String table -----------------------------------------
            fs.Seek(strTabOffset, SeekOrigin.Begin);
            var stringTableData = br.ReadBytes((int)(fileSize - strTabOffset));
            var strings = GetNullTerminatedStrings(stringTableData);

            // 3. Node tree -------------------------------------------
            fs.Seek(rootOffset, SeekOrigin.Begin);
            var rootNode = ParseNode(br, strings);

            // 4. Convert to XElement
            var xRoot = NodeToXElement(rootNode);
            return new XDocument(xRoot);
        }

        // ------------------------------------------------------------
        //  Helpers
        // ------------------------------------------------------------

        private static uint ReadBEUInt32(BinaryReader br)
        {
            var tmp = br.ReadUInt32();
            return BinaryPrimitives.ReverseEndianness(tmp);
        }

        private static List<string> GetNullTerminatedStrings(byte[] data)
        {
            var list = new List<string>();
            int start = 0;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] == 0)
                {
                    if (i > start)
                        list.Add(Encoding.ASCII.GetString(data, start, i - start));
                    start = i + 1;
                }
            }
            return list;
        }

        private class Node
        {
            public string Name { get; }
            public uint Flags { get; }
            public string? Value { get; set; }
            public List<Node> Children { get; } = new();

            public Node(string name, uint flags) { Name = name; Flags = flags; }
        }

        private static Node ParseNode(BinaryReader br, IList<string> strings)
        {
            uint nameLen = ReadBEUInt32(br);
            var nameBytes = br.ReadBytes((int)nameLen);
            string name = Encoding.ASCII.GetString(nameBytes);

            uint flags = ReadBEUInt32(br);
            uint childCount = ReadBEUInt32(br);

            var node = new Node(name, flags);

            if (childCount == 0)
            {
                long pos = br.BaseStream.Position;
                uint possibleIndex = ReadBEUInt32(br);
                if (possibleIndex < strings.Count)
                    node.Value = strings[(int)possibleIndex];
                else
                    br.BaseStream.Position = pos; // rewind, not a value index
            }
            else
            {
                for (int i = 0; i < childCount; i++)
                    node.Children.Add(ParseNode(br, strings));
            }

            return node;
        }

        private static XElement NodeToXElement(Node n)
        {
            var el = new XElement(n.Name);
            if (n.Flags != 0)
                el.SetAttributeValue("flags", $"0x{n.Flags:X}");

            if (n.Value != null)
                el.Value = n.Value;

            foreach (var child in n.Children)
                el.Add(NodeToXElement(child));
            return el;
        }
    }
}
