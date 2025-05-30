using System.Threading.Tasks;
using System.Xml;

namespace PssgViewer
{
    /// <summary>
    /// Simple view model used to load files asynchronously.
    /// </summary>
    public class MainViewModel
    {
        public async Task<XmlDocument> LoadFileAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                // Try to parse the file directly if it's a binary PSSG
                XmlDocument? doc = BinaryPssgParser.Parse(filePath);
                if (doc != null)
                    return doc;

                // Fall back to regular XML loading
                XmlDocument xml = new XmlDocument();
                xml.Load(filePath);
                return xml;
            });
        }
    }
}
