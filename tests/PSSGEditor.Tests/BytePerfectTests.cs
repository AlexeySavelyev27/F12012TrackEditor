using System.IO;
using Xunit;
using PSSGEditor;

namespace PSSGEditor.Tests;

public class BytePerfectTests
{
    [Fact]
    public void Roundtrip_Produces_Identical_File()
    {
        var baseDir = AppContext.BaseDirectory;
        var samplePath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "catalunya", "land.pssg"));
        var originalBytes = File.ReadAllBytes(samplePath);

        var parser = new PSSGParser(samplePath);
        var root = parser.Parse();
        var schema = parser.Schema;

        var tempFile = Path.GetTempFileName();
        try
        {
            var writer = new PSSGWriter(root, schema);
            writer.Save(tempFile);

            var newBytes = File.ReadAllBytes(tempFile);
            Assert.Equal(originalBytes, newBytes);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
