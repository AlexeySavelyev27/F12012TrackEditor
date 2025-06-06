﻿// PSSGFormat.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Buffers.Binary;

namespace PSSGEditor
{
    /// <summary>
    /// Represents a single node in the PSSG tree.
    /// </summary>
    public class PSSGNode
    {
        public string Name { get; set; }
        public Dictionary<string, byte[]> Attributes { get; set; } = new();
        public List<PSSGNode> Children { get; set; } = new();

        private byte[] _data;
        internal Func<byte[]>? LazyLoader { get; set; }

        /// <summary>
        /// Raw node payload. For large blobs the bytes are loaded lazily on
        /// first access using <see cref="LazyLoader"/>.
        /// </summary>
        public byte[]? Data
        {
            get
            {
                if (_data == null && LazyLoader != null)
                {
                    _data = LazyLoader();
                    LazyLoader = null;
                }
                return _data;
            }
            set
            {
                _data = value;
                LazyLoader = null;
            }
        }

        // These properties are computed during writing
        public uint AttrBlockSize { get; set; }
        public uint NodeSize { get; set; }

        public PSSGNode(string name)
        {
            Name = name;
        }
    }

    /// <summary>
    /// Schema mapping between node names/attr names and numeric IDs.
    /// </summary>
    public class PSSGSchema
    {
        public Dictionary<uint, string> NodeIdToName { get; } = new();
        public Dictionary<string, uint> NodeNameToId { get; } = new();
        public Dictionary<uint, Dictionary<uint, string>> AttrIdToName { get; } = new();
        public Dictionary<string, Dictionary<string, uint>> AttrNameToId { get; } = new();
        public Dictionary<uint, string> GlobalAttrIdToName { get; } = new();
        public Dictionary<string, uint> GlobalAttrNameToId { get; } = new();

        /// <summary>
        /// Rebuild schema from a tree, assigning sequential IDs.
        /// </summary>
        public void BuildFromTree(PSSGNode root)
        {
            var nodeNames = new List<string>();
            var attrMap = new Dictionary<string, List<string>>();
            var globalAttrs = new HashSet<string>();

            void Collect(PSSGNode node)
            {
                if (!nodeNames.Contains(node.Name))
                    nodeNames.Add(node.Name);

                if (!attrMap.ContainsKey(node.Name))
                    attrMap[node.Name] = new List<string>();

                foreach (var attr in node.Attributes.Keys)
                {
                    if (!globalAttrs.Contains(attr))
                        globalAttrs.Add(attr);
                    if (!attrMap[node.Name].Contains(attr))
                        attrMap[node.Name].Add(attr);
                }

                foreach (var child in node.Children)
                    Collect(child);
            }

            Collect(root);

            uint idCounter = 1;
            foreach (var name in nodeNames)
            {
                NodeIdToName[idCounter] = name;
                NodeNameToId[name] = idCounter;
                idCounter++;
            }

            uint attrCounter = 1;
            foreach (var name in nodeNames)
            {
                var nodeId = NodeNameToId[name];
                AttrIdToName[nodeId] = new Dictionary<uint, string>();
                AttrNameToId[name] = new Dictionary<string, uint>();

                if (!attrMap.TryGetValue(name, out var attrsForNode))
                    attrsForNode = new List<string>();

                foreach (var attr in attrsForNode)
                {
                    AttrIdToName[nodeId][attrCounter] = attr;
                    AttrNameToId[name][attr] = attrCounter;
                    GlobalAttrIdToName[attrCounter] = attr;
                    if (!GlobalAttrNameToId.ContainsKey(attr))
                        GlobalAttrNameToId[attr] = attrCounter;
                    attrCounter++;
                }
            }
        }
    }

    /// <summary>
    /// Parser for reading a PSSG file into a tree of PSSGNode.
    /// </summary>
    public class PSSGParser
    {
        private readonly string path;
        private MemoryStream buffer;
        private BinaryReader reader;
        private PSSGSchema schema;
        private long fileDataLength;
        private byte[] fileData;

        public PSSGParser(string path)
        {
            this.path = path;
        }

        public PSSGNode Parse()
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

            byte[] data;

            // Peek first two bytes to detect gzip
            Span<byte> header = stackalloc byte[2];
            int read = fs.Read(header);
            fs.Position = 0;

            Stream input = fs;
            if (read == 2 && header[0] == 0x1F && header[1] == 0x8B)
            {
                input = new GZipStream(fs, CompressionMode.Decompress);
            }

            using (var ms = new MemoryStream())
            {
                input.CopyTo(ms);
                data = ms.ToArray();
            }

            fileData = data;
            buffer = new MemoryStream(fileData);
            reader = new BinaryReader(buffer, Encoding.UTF8, leaveOpen: true);

            // Read signature "PSSG"
            var sig = reader.ReadBytes(4);
            if (sig.Length < 4 || Encoding.ASCII.GetString(sig) != "PSSG")
                throw new InvalidDataException("Not a PSSG file");

            // Read FileDataLength (we advance pointer; we won't really use it)
            fileDataLength = ReadUInt32BE();

            // Read schema
            schema = ReadSchema();

            // Read root node
            return ReadNode();
        }

        private PSSGSchema ReadSchema()
        {
            var sch = new PSSGSchema();
            uint attrInfoCount = ReadUInt32BE();
            uint nodeInfoCount = ReadUInt32BE();

            for (int i = 0; i < nodeInfoCount; i++)
            {
                uint nodeId = ReadUInt32BE();
                uint nameLen = ReadUInt32BE();
                string nodeName = Encoding.UTF8.GetString(reader.ReadBytes((int)nameLen));
                uint attrCount = ReadUInt32BE();

                sch.NodeIdToName[nodeId] = nodeName;
                sch.NodeNameToId[nodeName] = nodeId;
                sch.AttrIdToName[nodeId] = new Dictionary<uint, string>();
                sch.AttrNameToId[nodeName] = new Dictionary<string, uint>();

                for (int j = 0; j < attrCount; j++)
                {
                    uint attrId = ReadUInt32BE();
                    uint attrNameLen = ReadUInt32BE();
                    string attrName = Encoding.UTF8.GetString(reader.ReadBytes((int)attrNameLen));
                    sch.AttrIdToName[nodeId][attrId] = attrName;
                    sch.AttrNameToId[nodeName][attrName] = attrId;
                    if (!sch.GlobalAttrIdToName.ContainsKey(attrId))
                        sch.GlobalAttrIdToName[attrId] = attrName;
                    if (!sch.GlobalAttrNameToId.ContainsKey(attrName))
                        sch.GlobalAttrNameToId[attrName] = attrId;
                }
            }

            return sch;
        }

        private PSSGNode ReadNode()
        {
            long startPos = buffer.Position;

            uint nodeId = ReadUInt32BE();
            uint nodeSize = ReadUInt32BE();
            long nodeEnd = buffer.Position + nodeSize;

            uint attrBlockSize = ReadUInt32BE();
            long attrEnd = buffer.Position + attrBlockSize;

            string nodeName = schema.NodeIdToName.ContainsKey(nodeId)
                ? schema.NodeIdToName[nodeId]
                : $"unknown_{nodeId}";

            var attrs = new Dictionary<string, byte[]>();
            Dictionary<uint, string> attrMap = schema.AttrIdToName.ContainsKey(nodeId)
                ? schema.AttrIdToName[nodeId]
                : null;
            while (buffer.Position < attrEnd)
            {
                uint attrId = ReadUInt32BE();
                uint valSize = ReadUInt32BE();
                byte[] val = reader.ReadBytes((int)valSize);

                string attrName;
                if (attrMap != null && attrMap.ContainsKey(attrId))
                    attrName = attrMap[attrId];
                else if (schema.GlobalAttrIdToName.ContainsKey(attrId))
                    attrName = schema.GlobalAttrIdToName[attrId];
                else
                    attrName = $"attr_{attrId}";

                attrs[attrName] = val;
            }

            var children = new List<PSSGNode>();
            Func<byte[]>? loader = null;
            long dataStart = 0;
            int dataLen = 0;

            while (buffer.Position < nodeEnd)
            {
                long pos = buffer.Position;
                long remaining = nodeEnd - pos;
                if (remaining >= 8)
                {
                    uint peekId = ReadUInt32BE();
                    uint peekSize = ReadUInt32BE();
                    if (schema.NodeIdToName.ContainsKey(peekId) && peekSize <= (uint)(nodeEnd - (pos + 8)))
                    {
                        buffer.Position = pos;
                        var child = ReadNode();
                        children.Add(child);
                        continue;
                    }
                    else
                    {
                        buffer.Position = pos;
                    }
                }

                // Считаем оставшиеся байты как raw-data
                dataStart = buffer.Position;
                dataLen = (int)(nodeEnd - buffer.Position);
                buffer.Position = nodeEnd;
                var bytesRef = fileData;
                loader = () =>
                {
                    var arr = new byte[dataLen];
                    Buffer.BlockCopy(bytesRef, (int)dataStart, arr, 0, dataLen);
                    return arr;
                };
                break;
            }

            buffer.Position = nodeEnd;
            var node = new PSSGNode(nodeName)
            {
                Attributes = attrs,
                Children = children
            };
            if (children.Count == 0 && loader != null)
                node.LazyLoader = loader;
            else if (children.Count == 0)
                node.Data = Array.Empty<byte>();
            return node;
        }

        /// <summary>
        /// Reads a big-endian UInt32 from the stream.
        /// Previous implementation allocated a new 4-byte array per call,
        /// which caused a lot of temporary allocations when parsing large files.
        /// Using ReadUInt32 with explicit endianness reversal avoids that.
        /// </summary>
        private uint ReadUInt32BE()
        {
            uint val = reader.ReadUInt32();
            return BitConverter.IsLittleEndian
                ? BinaryPrimitives.ReverseEndianness(val)
                : val;
        }
    }

    /// <summary>
    /// Writer for serializing a PSSGNode tree back into a .pssg file.
    /// </summary>
    public class PSSGWriter
    {
        private readonly PSSGNode root;
        private readonly PSSGSchema schema;

        public PSSGWriter(PSSGNode rootNode)
        {
            root = rootNode;
            schema = new PSSGSchema();
            schema.BuildFromTree(root);
        }

        public void Save(string path)
        {
            // Compute sizes bottom-up
            ComputeSizes(root);

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true);

            // Write header
            writer.Write(Encoding.ASCII.GetBytes("PSSG"));
            writer.Write(0u); // placeholder for FileDataLength

            // Count total attr entries
            uint totalAttrEntries = 0;
            foreach (var kv in schema.AttrNameToId)
                totalAttrEntries += (uint)kv.Value.Count;
            uint nodeEntryCount = (uint)schema.NodeNameToId.Count;

            writer.Write(ToBigEndian(totalAttrEntries));
            writer.Write(ToBigEndian(nodeEntryCount));

            // Write schema entries
            foreach (var kv in schema.NodeNameToId)
            {
                string nodeName = kv.Key;
                uint nodeId = kv.Value;
                byte[] nameBytes = Encoding.UTF8.GetBytes(nodeName);

                writer.Write(ToBigEndian(nodeId));
                writer.Write(ToBigEndian((uint)nameBytes.Length));
                writer.Write(nameBytes);

                var attrMap = schema.AttrNameToId[nodeName];
                writer.Write(ToBigEndian((uint)attrMap.Count));
                foreach (var a in attrMap)
                {
                    string attrName = a.Key;
                    uint attrId = a.Value;
                    byte[] attrBytes = Encoding.UTF8.GetBytes(attrName);
                    writer.Write(ToBigEndian(attrId));
                    writer.Write(ToBigEndian((uint)attrBytes.Length));
                    writer.Write(attrBytes);
                }
            }

            // Write nodes recursively
            WriteNode(writer, root);

            // Go back and fill FileDataLength = fileLength - 8
            long endPos = fs.Position;
            fs.Position = 4;
            writer.Write(ToBigEndian((uint)(endPos - 8)));
        }

        private void ComputeSizes(PSSGNode node)
        {
            // Attribute block size = sum of (4 bytes attrId + 4 bytes valSize + actual bytes)
            uint attrSize = 0;
            foreach (var kv in node.Attributes)
            {
                attrSize += 8u + (uint)kv.Value.Length;
            }

            uint childrenPayload = 0;
            if (node.Children.Count > 0)
            {
                foreach (var c in node.Children)
                {
                    ComputeSizes(c);
                    // For each child: 4 bytes nodeId + 4 bytes nodeSize + actual child.nodeSize
                    childrenPayload += 8u + c.NodeSize;
                }
            }
            else
            {
                childrenPayload = (uint)(node.Data?.Length ?? 0);
            }

            node.AttrBlockSize = attrSize;
            // NodeSize = 4 bytes (AttrBlockSize) + attrSize + payload
            node.NodeSize = 4u + attrSize + childrenPayload;
        }

        private void WriteNode(BinaryWriter writer, PSSGNode node)
        {
            uint nodeId = schema.NodeNameToId[node.Name];
            writer.Write(ToBigEndian(nodeId));
            writer.Write(ToBigEndian(node.NodeSize));
            writer.Write(ToBigEndian(node.AttrBlockSize));

            // Write attributes
            foreach (var kv in node.Attributes)
            {
                string attrName = kv.Key;
                byte[] value = kv.Value;
                uint attrId;

                if (schema.AttrNameToId.ContainsKey(node.Name) && schema.AttrNameToId[node.Name].ContainsKey(attrName))
                {
                    attrId = schema.AttrNameToId[node.Name][attrName];
                }
                else if (attrName.StartsWith("attr_") && uint.TryParse(attrName.Substring(5), out var parsed))
                {
                    attrId = parsed;
                }
                else
                {
                    throw new InvalidDataException($"Unknown attribute name: {attrName}");
                }

                writer.Write(ToBigEndian(attrId));
                writer.Write(ToBigEndian((uint)value.Length));
                writer.Write(value);
            }

            // Write payload (children or raw data)
            if (node.Children.Count > 0)
            {
                foreach (var c in node.Children)
                    WriteNode(writer, c);
            }
            else if (node.Data != null)
            {
                writer.Write(node.Data);
            }
        }

        /// <summary>
        /// Converts a little-endian uint to big-endian byte order.
        /// </summary>
        private static uint ToBigEndian(uint value)
        {
            if (BitConverter.IsLittleEndian)
                return ((value & 0x000000FF) << 24) |
                       ((value & 0x0000FF00) << 8) |
                       ((value & 0x00FF0000) >> 8) |
                       ((value & 0xFF000000) >> 24);
            else
                return value;
        }
    }
}
