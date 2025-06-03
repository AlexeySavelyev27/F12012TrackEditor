// PSSGFormat.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

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
        public byte[] Data { get; set; }  // null if node has children

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

        /// <summary>
        /// Rebuild schema from a tree, assigning sequential IDs.
        /// </summary>
        public void BuildFromTree(PSSGNode root)
        {
            var nodeNames = new List<string>();
            var attrMap = new Dictionary<string, HashSet<string>>();

            void Collect(PSSGNode node)
            {
                if (!nodeNames.Contains(node.Name))
                    nodeNames.Add(node.Name);

                if (!attrMap.ContainsKey(node.Name))
                    attrMap[node.Name] = new HashSet<string>();

                foreach (var attr in node.Attributes.Keys)
                    attrMap[node.Name].Add(attr);

                foreach (var child in node.Children)
                    Collect(child);
            }

            Collect(root);

            // Assign NodeID (start from 1)
            uint idCounter = 1;
            foreach (var name in nodeNames)
            {
                NodeIdToName[idCounter] = name;
                NodeNameToId[name] = idCounter;
                idCounter++;
            }

            // Assign AttrID per node
            foreach (var kvp in attrMap)
            {
                var nodeName = kvp.Key;
                var attrs = kvp.Value;
                var nodeId = NodeNameToId[nodeName];
                AttrIdToName[nodeId] = new Dictionary<uint, string>();
                AttrNameToId[nodeName] = new Dictionary<string, uint>();

                uint aCounter = 1;
                foreach (var attrName in attrs)
                {
                    AttrIdToName[nodeId][aCounter] = attrName;
                    AttrNameToId[nodeName][attrName] = aCounter;
                    aCounter++;
                }
            }
        }
    }

    /// <summary>
    /// Parser for reading a PSSG file into a tree of PSSGNode.
    /// </summary>
    public class PSSGParser
    {
        private readonly byte[] fileBytes;
        private MemoryStream buffer;
        private BinaryReader reader;
        private PSSGSchema schema;
        private long fileDataLength;

        public PSSGParser(string path)
        {
            fileBytes = File.ReadAllBytes(path);
        }

        public PSSGNode Parse()
        {
            byte[] data = fileBytes;
            // Check for GZip signature
            if (data.Length >= 2 && data[0] == 0x1F && data[1] == 0x8B)
            {
                using var gz = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
                using var ms = new MemoryStream();
                gz.CopyTo(ms);
                data = ms.ToArray();
            }

            buffer = new MemoryStream(data);
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
            if (schema.AttrIdToName.ContainsKey(nodeId))
            {
                var attrMap = schema.AttrIdToName[nodeId];
                while (buffer.Position < attrEnd)
                {
                    uint attrId = ReadUInt32BE();
                    uint valSize = ReadUInt32BE();
                    byte[] val = reader.ReadBytes((int)valSize);

                    string attrName;
                    if (attrId == 63) // custom mapping
                        attrName = "id";
                    else if (attrMap.ContainsKey(attrId))
                        attrName = attrMap[attrId];
                    else
                        attrName = $"attr_{attrId}";

                    attrs[attrName] = val;
                }
            }
            else
            {
                // Если нет сопоставления, просто пропустить
                while (buffer.Position < attrEnd)
                {
                    ReadUInt32BE();
                    uint valSize = ReadUInt32BE();
                    reader.ReadBytes((int)valSize);
                }
            }

            var children = new List<PSSGNode>();
            byte[] data = null;

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
                data = reader.ReadBytes((int)(nodeEnd - buffer.Position));
                break;
            }

            buffer.Position = nodeEnd;
            var node = new PSSGNode(nodeName)
            {
                Attributes = attrs,
                Children = children,
                Data = children.Count == 0 ? data : null
            };
            return node;
        }

        /// <summary>
        /// Reads a big-endian UInt32 from the stream.
        /// </summary>
        private uint ReadUInt32BE()
        {
            var bytes = reader.ReadBytes(4);
            if (bytes.Length < 4) throw new EndOfStreamException();
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return BitConverter.ToUInt32(bytes, 0);
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
                if (attrName == "id")
                {
                    attrId = 63; // custom assumption
                }
                else
                {
                    attrId = schema.AttrNameToId[node.Name][attrName];
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
