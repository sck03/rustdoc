using ExportDocManager.Models.DTOs.SingleWindow;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static SingleWindowPackageManifest ReadSingleWindowPackageManifest(string packagePath)
        {
            if (!File.Exists(packagePath))
            {
                throw new FileNotFoundException("交接包不存在。", packagePath);
            }

            try
            {
                using var archive = System.IO.Compression.ZipFile.OpenRead(packagePath);
                var entry = archive.GetEntry("manifest.json")
                    ?? throw new InvalidDataException("交接包缺少 manifest.json。");
                using var stream = entry.Open();
                return System.Text.Json.JsonSerializer.Deserialize<SingleWindowPackageManifest>(
                    stream,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidDataException("交接包 manifest.json 无效。");
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("交接包格式无效。", ex);
            }
        }
    }
}
