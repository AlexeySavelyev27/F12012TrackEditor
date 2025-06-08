using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Buffers.Binary;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Forms;

namespace PSSGEditor
{
    public partial class MainWindow
    {
        private class TextureEntry
        {
            public string Name { get; set; }
            public PSSGNode Node { get; set; }
        }

        private List<TextureEntry> textureEntries = new();

        private void PopulateTextureList()
        {
            textureEntries.Clear();
            TexturesListBox.ItemsSource = null;
            if (rootNode == null) return;

            var stack = new Stack<PSSGNode>();
            stack.Push(rootNode);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (string.Equals(n.Name, "TEXTURE", StringComparison.OrdinalIgnoreCase))
                {
                    if (n.Attributes.TryGetValue("id", out var idBytes))
                    {
                        string name = DecodeString(idBytes);
                        textureEntries.Add(new TextureEntry { Name = name, Node = n });
                    }
                }
                foreach (var c in n.Children)
                    stack.Push(c);
            }

            textureEntries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            TexturesListBox.ItemsSource = textureEntries;
            TexturesListBox.DisplayMemberPath = nameof(TextureEntry.Name);
        }

        private static string DecodeString(byte[] bytes)
        {
            if (bytes.Length >= 4)
            {
                uint len = BinaryPrimitives.ReadUInt32BigEndian(bytes);
                if (len <= bytes.Length - 4)
                    return Encoding.UTF8.GetString(bytes, 4, (int)len);
            }
            return Encoding.UTF8.GetString(bytes);
        }

        private void TexturesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TexturesListBox.SelectedItem is TextureEntry entry)
            {
                ShowTexture(entry.Node);
            }
        }

        private void ShowTexture(PSSGNode texNode)
        {
            try
            {
                var ddsBytes = BuildDds(texNode);
                if (ddsBytes == null)
                {
                    TexturePreviewImage.Source = null;
                    return;
                }
                using var ms = new MemoryStream(ddsBytes);
                var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                TexturePreviewImage.Source = decoder.Frames[0];
            }
            catch
            {
                TexturePreviewImage.Source = null;
            }
        }

        private byte[]? BuildDds(PSSGNode texNode)
        {
            if (!texNode.Attributes.TryGetValue("width", out var widthBytes) ||
                !texNode.Attributes.TryGetValue("height", out var heightBytes) ||
                !texNode.Attributes.TryGetValue("texelFormat", out var formatBytes))
                return null;

            uint width = BinaryPrimitives.ReadUInt32BigEndian(widthBytes);
            uint height = BinaryPrimitives.ReadUInt32BigEndian(heightBytes);
            string format = DecodeString(formatBytes).ToLowerInvariant();
            uint mipMaps = 1;
            if (texNode.Attributes.TryGetValue("numberMipMapLevels", out var mipBytes))
                mipMaps = BinaryPrimitives.ReadUInt32BigEndian(mipBytes);

            var block = texNode.Children.FirstOrDefault(c => c.Name == "TEXTUREIMAGEBLOCK");
            var dataNode = block?.Children.FirstOrDefault(c => c.Name == "TEXTUREIMAGEBLOCKDATA");
            byte[]? data = dataNode?.Data;
            if (data == null) return null;

            uint fourCC = 0;
            uint pfFlags = 0x4; // DDPF_FOURCC
            uint rgbBitCount = 0;
            uint rMask = 0;
            uint gMask = 0;
            uint bMask = 0;
            uint aMask = 0;
            int blockSize = 16;

            switch (format)
            {
                case "dxt1":
                    fourCC = 0x31545844; // 'DXT1'
                    blockSize = 8;
                    break;
                case "dxt3":
                    fourCC = 0x33545844; // 'DXT3'
                    break;
                case "dxt5":
                    fourCC = 0x35545844; // 'DXT5'
                    break;
                default:
                    pfFlags = 0x41; // DDPF_RGB | ALPHA
                    rgbBitCount = 32;
                    rMask = 0x00FF0000;
                    gMask = 0x0000FF00;
                    bMask = 0x000000FF;
                    aMask = 0xFF000000;
                    blockSize = 4;
                    break;
            }

            uint linearSize = (uint)Math.Max(1, ((width + 3) / 4)) * (uint)Math.Max(1, ((height + 3) / 4)) * (uint)blockSize;
            if (pfFlags != 0x4)
                linearSize = width * height * 4;

            var header = new byte[128];
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0), 0x20534444); // DDS magic
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 124);
            uint flags = 0x1 | 0x2 | 0x4 | 0x1000; // CAPS | HEIGHT | WIDTH | PIXELFORMAT
            if (mipMaps > 1)
                flags |= 0x20000; // MIPMAPCOUNT
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), flags);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), height);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), width);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), linearSize);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(28), mipMaps);
            // reserved[11] already zero
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(76), 32); // pfSize
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(80), pfFlags);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(84), fourCC);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(88), rgbBitCount);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(92), rMask);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(96), gMask);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(100), bMask);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(104), aMask);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(108), 0x1000); // caps
            if (mipMaps > 1)
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(108), 0x401008); // complex | texture | mipmaps
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(112), 0);

            var result = new byte[header.Length + data.Length];
            Buffer.BlockCopy(header, 0, result, 0, header.Length);
            Buffer.BlockCopy(data, 0, result, header.Length, data.Length);
            return result;
        }

        private static byte[] EncodeString(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            var arr = new byte[bytes.Length + 4];
            BinaryPrimitives.WriteUInt32BigEndian(arr.AsSpan(), (uint)bytes.Length);
            Buffer.BlockCopy(bytes, 0, arr, 4, bytes.Length);
            return arr;
        }

        private static (uint width, uint height, uint mipMaps, string format, byte[] data)? ParseDds(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 128 || bytes[0] != 'D' || bytes[1] != 'D' || bytes[2] != 'S' || bytes[3] != ' ')
                return null;
            uint height = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));
            uint width = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16));
            uint mipMaps = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(28));
            if (mipMaps == 0) mipMaps = 1;
            uint pfFlags = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(80));
            uint fourCC = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(84));
            string format = "argb";
            if ((pfFlags & 0x4) != 0)
            {
                if (fourCC == 0x31545844) format = "dxt1";
                else if (fourCC == 0x33545844) format = "dxt3";
                else if (fourCC == 0x35545844) format = "dxt5";
            }
            var data = bytes.AsSpan(128).ToArray();
            return (width, height, mipMaps, format, data);
        }

        private static PSSGNode? FindParent(PSSGNode root, PSSGNode child)
        {
            var stack = new Stack<PSSGNode>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                for (int i = 0; i < n.Children.Count; i++)
                {
                    if (n.Children[i] == child)
                        return n;
                    stack.Push(n.Children[i]);
                }
            }
            return null;
        }

        private PSSGNode? FindTextureLibrary()
        {
            if (rootNode == null) return null;
            var stack = new Stack<PSSGNode>();
            stack.Push(rootNode);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (n.Name == "LIBRARY" && n.Attributes.TryGetValue("type", out var t) && DecodeString(t) == "YYY")
                    return n;
                foreach (var c in n.Children) stack.Push(c);
            }
            return null;
        }

        private void DeleteTextureMenuItem_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (TexturesListBox.SelectedItem is not TextureEntry entry || rootNode == null)
                return;
            var parent = FindParent(rootNode, entry.Node);
            parent?.Children.Remove(entry.Node);
            textureEntries.Remove(entry);
            PopulateTextureList();
            TexturePreviewImage.Source = null;
        }

        private void ImportTextureMenuItem_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (TexturesListBox.SelectedItem is not TextureEntry entry) return;
            var ofd = new Microsoft.Win32.OpenFileDialog { Filter = "DDS files (*.dds)|*.dds" };
            if (ofd.ShowDialog() != true) return;
            ImportIntoNode(entry.Node, ofd.FileName);
            PopulateTextureList();
            ShowTexture(entry.Node);
        }

        private void ExportTextureMenuItem_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (TexturesListBox.SelectedItem is not TextureEntry entry) return;
            var dds = BuildDds(entry.Node);
            if (dds == null) return;
            var sfd = new Microsoft.Win32.SaveFileDialog { Filter = "DDS files (*.dds)|*.dds", FileName = entry.Name + ".dds" };
            if (sfd.ShowDialog() != true) return;
            File.WriteAllBytes(sfd.FileName, dds);
        }

        private void NewTextureMenuItem_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog { Filter = "DDS files (*.dds)|*.dds" };
            if (ofd.ShowDialog() != true) return;
            AddNewTexture(ofd.FileName);
        }

        private void ImportFolderMenuItem_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            foreach (var file in Directory.EnumerateFiles(dlg.SelectedPath, "*.dds"))
            {
                string id = Path.GetFileNameWithoutExtension(file);
                var existing = textureEntries.FirstOrDefault(t => t.Name.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (existing != null) ImportIntoNode(existing.Node, file);
                else AddNewTexture(file);
            }
            PopulateTextureList();
        }

        private void ExportAllTexturesMenuItem_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            foreach (var entry in textureEntries)
            {
                var dds = BuildDds(entry.Node);
                if (dds != null)
                {
                    var path = Path.Combine(dlg.SelectedPath, entry.Name + ".dds");
                    File.WriteAllBytes(path, dds);
                }
            }
        }

        private void ImportIntoNode(PSSGNode texNode, string filePath)
        {
            var info = ParseDds(filePath);
            if (info == null) return;
            texNode.Attributes["width"] = ToBigEndian(info.Value.width);
            texNode.Attributes["height"] = ToBigEndian(info.Value.height);
            texNode.Attributes["texelFormat"] = EncodeString(info.Value.format);
            texNode.Attributes["numberMipMapLevels"] = ToBigEndian(info.Value.mipMaps);

            var block = texNode.Children.FirstOrDefault(c => c.Name == "TEXTUREIMAGEBLOCK");
            if (block == null)
            {
                block = new PSSGNode("TEXTUREIMAGEBLOCK");
                texNode.Children.Add(block);
            }
            block.Attributes["typename"] = EncodeString("Raw");
            block.Attributes["size"] = ToBigEndian((uint)info.Value.data.Length);

            var dataNode = block.Children.FirstOrDefault(c => c.Name == "TEXTUREIMAGEBLOCKDATA");
            if (dataNode == null)
            {
                dataNode = new PSSGNode("TEXTUREIMAGEBLOCKDATA");
                block.Children.Add(dataNode);
            }
            dataNode.Data = info.Value.data;
        }

        private void AddNewTexture(string filePath)
        {
            var info = ParseDds(filePath);
            if (info == null || rootNode == null) return;
            string id = Path.GetFileNameWithoutExtension(filePath);
            var tex = new PSSGNode("TEXTURE");
            tex.Attributes["id"] = EncodeString(id);
            tex.Attributes["width"] = ToBigEndian(info.Value.width);
            tex.Attributes["height"] = ToBigEndian(info.Value.height);
            tex.Attributes["texelFormat"] = EncodeString(info.Value.format);
            tex.Attributes["numberMipMapLevels"] = ToBigEndian(info.Value.mipMaps);

            var block = new PSSGNode("TEXTUREIMAGEBLOCK");
            block.Attributes["typename"] = EncodeString("Raw");
            block.Attributes["size"] = ToBigEndian((uint)info.Value.data.Length);
            var dataNode = new PSSGNode("TEXTUREIMAGEBLOCKDATA");
            dataNode.Data = info.Value.data;
            block.Children.Add(dataNode);
            tex.Children.Add(block);

            var lib = FindTextureLibrary() ?? rootNode;
            lib.Children.Add(tex);

            textureEntries.Add(new TextureEntry { Name = id, Node = tex });
            PopulateTextureList();
        }
    }
}
