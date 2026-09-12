using ExportDocManager.Services.Errors;

namespace ExportDocManager.Api.Hosting;

internal static class ApiRowVersion
{
    public static byte[] ParseRequired(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ServiceValidationException("操作必须提交记录版本号，请刷新后重试。");
        }
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException exception)
        {
            throw new ServiceValidationException("记录版本号必须是有效的 Base64 字符串。", exception);
        }
    }
}
