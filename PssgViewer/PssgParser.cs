// --------------------------------------------------------------------------------------------------
//  PssgParser.cs  (correct node layout v0.4)
//  * Node header = [nameIdx u32][attrCount u32][childCount u32]
//  * Each attribute = [nameIdx u32][valueIdx u32]
//  * Strings addressed by index into string‑table.
//  Fixes "read beyond end of stream" by removing mistaken variable‑length name bytes.
// --------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace PssgViewer.Core
{
    internal static class PssgParser
    {
        public static XDocument ParseToXDocument(string path)
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);

            // 1) Signature ------------------------------------------------------
            if (!br.ReadBytes(4).SequenceEqual(Encoding.ASCII.GetBytes("PSSG")))
                throw new InvalidDataException("Not a PSSG file");

            // 2) Header (little‑endian)
            uint fileSize = br.ReadUInt32();
            uint strOff = br.ReadUInt32();
            uint rootOff = br.ReadUInt32();
            br.ReadUInt32(); // ver
            br.ReadUInt32(); // flags

            // 3) Strings ---------------------------------------------------------
            fs.Seek(strOff, SeekOrigin.Begin);
            var stringTable = ReadCStringTable(br, (int)(fs.Length - strOff));

            // 4) Node tree -------------------------------------------------------
            fs.Seek(rootOff, SeekOrigin.Begin);
            var root = ReadNode(br, stringTable);
            return new XDocument(root);
        }

        // ----------------------------------------------------------------------
        private static List<string> ReadCStringTable(BinaryReader br, int maxBytes)
        {
            byte[] data = br.ReadBytes(maxBytes);
            var list = new List<string>();
            int start = 0;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] == 0)
                {
                    list.Add(Encoding.ASCII.GetString(data, start, i - start));
                    start = i + 1;
                }
            }
            return list;
        }

        // ----------------------------------------------------------------------
        private static XElement ReadNode(BinaryReader br, IList<string> strings)
        {
            uint nameIdx = br.ReadUInt32();
            uint attrCount = br.ReadUInt32();
            uint childCount = br.ReadUInt32();

            string nodeName = SafeString(strings, nameIdx, $"UNK_{nameIdx}");
            var elem = new XElement(nodeName);

            // Attributes ------------------------------------------------------
            for (uint i = 0; i < attrCount; i++)
            {
                uint attrNameIdx = br.ReadUInt32();
                uint attrValueIdx = br.ReadUInt32();

                string attrName = SafeString(strings, attrNameIdx, $"attr_{attrNameIdx}");
                string attrValue = SafeString(strings, attrValueIdx, $"val_{attrValueIdx}");
                elem.SetAttributeValue(attrName, attrValue);
            }

            // Children --------------------------------------------------------
            for (uint i = 0; i < childCount; i++)
            {
                elem.Add(ReadNode(br, strings));
            }
            return elem;
        }

        private static string SafeString(IList<string> strs, uint idx, string fallback)
            => idx < strs.Count ? strs[(int)idx] : fallback;
    }
}
