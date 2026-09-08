using System.IO.Compression;
using System.Text;
using ExportDocManager.Services.Errors;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Attachments;

internal static class BusinessAttachmentFilePolicy
{
    public static string FileName(string value)
    {
        string name = (value ?? string.Empty).Normalize(NormalizationForm.FormC);
        if (!CrossPlatformFileNamePolicy.IsSafeFileName(name))
            throw new ServiceValidationException("文件名含非法字符、路径或保留名称，或超过 240 字节。请重命名后上传。");
        return name;
    }

    public static string Validate(string name, byte[] bytes)
    {
        if (bytes.Length == 0) throw new ServiceValidationException("不能归档空文件。");
        ReadOnlySpan<byte> data = bytes;
        string extension = Path.GetExtension(name).ToLowerInvariant();
        string? media = extension switch
        {
            ".pdf" when data.StartsWith("%PDF-"u8) => "application/pdf",
            ".png" when data.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) => "image/png",
            ".jpg" or ".jpeg" when data.StartsWith(new byte[] { 255, 216, 255 }) => "image/jpeg",
            ".gif" when data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8) => "image/gif",
            ".webp" when data.Length >= 12 && data.StartsWith("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8) => "image/webp",
            ".xls" or ".doc" when data.StartsWith(new byte[] { 208, 207, 17, 224, 161, 177, 26, 225 }) => "application/octet-stream",
            ".xlsx" or ".docx" or ".pptx" => ValidateOffice(extension, bytes),
            ".txt" or ".csv" => ValidateText(bytes),
            _ => null
        };
        return media ?? throw new ServiceValidationException("文件类型或文件头不匹配。支持 PDF、PNG/JPEG/GIF/WebP、Word、Excel、PowerPoint、UTF-8 TXT/CSV。");
    }

    private static string ValidateText(byte[] bytes)
    {
        try
        {
            _ = new UTF8Encoding(false, true).GetCharCount(bytes);
            if (bytes.AsSpan().Contains((byte)0))
                throw new ServiceValidationException("文本附件不能包含二进制内容。");
            return "text/plain";
        }
        catch (DecoderFallbackException)
        {
            throw new ServiceValidationException("文本附件须使用 UTF-8 编码。");
        }
    }

    private static string ValidateOffice(string extension, byte[] bytes)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(bytes, false), ZipArchiveMode.Read);
            string document = extension switch { ".xlsx" => "xl/workbook.xml", ".docx" => "word/document.xml", _ => "ppt/presentation.xml" };
            if (archive.Entries.Count > 2048 || archive.GetEntry("[Content_Types].xml") == null || archive.GetEntry(document) == null ||
                archive.Entries.Sum(entry => entry.Length) > 256L * 1024 * 1024)
                throw new ServiceValidationException("Office 附件结构无效或展开容量超过限制。");
            return "application/octet-stream";
        }
        catch (InvalidDataException)
        {
            throw new ServiceValidationException("Office 附件不是有效的文档包。");
        }
    }

    public static string Text(string? value, string label, int maximum, bool required = false)
    {
        string text = (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormC);
        if (text.Length > maximum || required && text.Length == 0 || text.Any(char.IsControl))
            throw new ServiceValidationException($"{label}须为{(required ? 1 : 0)}—{maximum}个字符，不能含控制字符。");
        return text;
    }
}
