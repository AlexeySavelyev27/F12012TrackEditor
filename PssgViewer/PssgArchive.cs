using System;
using System.Collections.Generic;
using System.IO;

namespace PssgViewer
{
    public class PssgNode
    {
        public string Name { get; set; }
        public uint Flags { get; set; }
        public List<PssgNode> Children { get; } = new List<PssgNode>();
    }

    public class PssgArchive
    {
        public uint Size { get; set; }
        public uint StringTableOffset { get; set; }
        public uint RootOffset { get; set; }
        public PssgNode Root { get; set; }
        public List<string> Strings { get; } = new List<string>();

        public static PssgArchive Load(string path)
        {
            using var fs = File.OpenRead(path);
            var br = new BinaryReader(fs);
            // Read signature as raw bytes to avoid decoding issues with ReadChars
            var sig = System.Text.Encoding.ASCII.GetString(br.ReadBytes(4));
            if (sig != "PSSG")
                throw new InvalidDataException("Invalid PSSG signature");
            var archive = new PssgArchive();
            archive.Size = ReadBEUInt32(br);
            archive.StringTableOffset = ReadBEUInt32(br);
            archive.RootOffset = ReadBEUInt32(br);

            fs.Seek(archive.RootOffset, SeekOrigin.Begin);
            archive.Root = ReadNode(br, archive.StringTableOffset);

            fs.Seek(archive.StringTableOffset, SeekOrigin.Begin);
            archive.Strings.AddRange(ReadStringTable(br));
            return archive;
        }

        private static PssgNode ReadNode(BinaryReader br, uint limit)
        {
            long start = br.BaseStream.Position;
            if (start >= limit)
                return null;
            if (br.BaseStream.Position + 4 > br.BaseStream.Length)
                return null;
            uint nameLen = ReadBEUInt32(br);
            if (nameLen == 0 || br.BaseStream.Position + nameLen > br.BaseStream.Length)
                return null;
            string name = System.Text.Encoding.ASCII.GetString(br.ReadBytes((int)nameLen));
            if (br.BaseStream.Position + 8 > br.BaseStream.Length)
                return null;
            uint flags = ReadBEUInt32(br);
            uint childCount = ReadBEUInt32(br);
            var node = new PssgNode { Name = name, Flags = flags };
            for (int i = 0; i < childCount; i++)
            {
                var child = ReadNode(br, limit);
                if (child != null)
                    node.Children.Add(child);
            }
            return node;
        }

        private static IEnumerable<string> ReadStringTable(BinaryReader br)
        {
            var list = new List<string>();
            var buffer = new List<byte>();
            while (br.BaseStream.Position < br.BaseStream.Length)
            {
                int b = br.Read();
                if (b == -1)
                    break;
                if (b == 0)
                {
                    list.Add(System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
                    buffer.Clear();
                }
                else
                {
                    buffer.Add((byte)b);
                }
            }
            if (buffer.Count > 0)
                list.Add(System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
            return list;
        }

        private static uint ReadBEUInt32(BinaryReader br)
        {
            var bytes = br.ReadBytes(4);
            if (bytes.Length < 4)
                throw new EndOfStreamException();
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return BitConverter.ToUInt32(bytes, 0);
        }
    }
}
