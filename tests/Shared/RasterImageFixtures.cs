using System.Text.Json;

namespace ExportDocManager.Testing;

internal static class RasterImageFixtures
{
    internal static byte[] Read(string format)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "raster-images.json")));
        return Convert.FromBase64String(document.RootElement.GetProperty(format).GetString()!);
    }
}
