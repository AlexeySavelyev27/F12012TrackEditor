using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Buffers.Binary;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Controls;

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
    }
}
