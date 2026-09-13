using ExportDocManager.Services.Errors;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Office;

internal static class PersonnelImagePolicy
{
    internal static string Validate(byte[] content, string declaredType)
    {
        if (content.Length is 0 or > PersonnelImageLimits.MaxBytes)
            throw new ServiceValidationException("请选择不超过 5 MB 的 PNG 或 JPEG 图片。");
        var image = RasterImageInspector.Inspect(content, PersonnelImageLimits.MaxDimension, PersonnelImageLimits.MaxPixels);
        if (image == null || image.MediaType is not ("image/png" or "image/jpeg"))
            throw new ServiceValidationException("图片结构或尺寸无效；仅支持 PNG/JPEG，单边不超过 8192 像素，总像素不超过 3200 万。");
        string declaration = (declaredType ?? "").Trim().ToLowerInvariant();
        if (declaration.Length > 0 && declaration != "application/octet-stream" && declaration != image.MediaType)
            throw new ServiceValidationException("图片声明类型与文件内容不一致，请重新选择原始图片。");
        return image.MediaType;
    }
}
