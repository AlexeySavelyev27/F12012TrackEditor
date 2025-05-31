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
                // Attempt binary parsing first
                XmlDocument? parsed = BinaryPssgParser.Parse(filePath);
                if (parsed == null)
                {
                    parsed = new XmlDocument();
                    parsed.Load(filePath);
                }

                return parsed;
            });
        }
    }
}
