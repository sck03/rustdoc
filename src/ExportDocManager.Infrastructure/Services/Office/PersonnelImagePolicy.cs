using System.Buffers.Binary;
using ExportDocManager.Services.Errors;

namespace ExportDocManager.Services.Office;

/// <summary>Bounded raster headers; no SVG, external references or native image runtime.</summary>
internal static class PersonnelImagePolicy
{
    internal static string Validate(byte[] content, string declaredType)
    {
        if (content.Length is 0 or > PersonnelImageLimits.MaxBytes)
            throw new ServiceValidationException("请选择不超过 5 MB 的 PNG 或 JPEG 图片。");
        ReadOnlySpan<byte> bytes = content;
        (int Width, int Height)? size = null;
        string type = "";
        if (bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            size = PngSize(bytes);
            type = "image/png";
        }
        else if (bytes.StartsWith(new byte[] { 255, 216, 255 }))
        {
            size = JpegSize(bytes);
            type = "image/jpeg";
        }
        if (size is not { Width: > 0, Height: > 0 } dimensions || dimensions.Width > PersonnelImageLimits.MaxDimension ||
            dimensions.Height > PersonnelImageLimits.MaxDimension || (long)dimensions.Width * dimensions.Height > PersonnelImageLimits.MaxPixels)
            throw new ServiceValidationException("图片结构或尺寸无效；仅支持 PNG/JPEG，单边不超过 8192 像素，总像素不超过 3200 万。");
        string declaration = (declaredType ?? "").Trim().ToLowerInvariant();
        if (declaration.Length > 0 && declaration != "application/octet-stream" && declaration != type)
            throw new ServiceValidationException("图片声明类型与文件内容不一致，请重新选择原始图片。");
        return type;
    }

    private static (int, int)? PngSize(ReadOnlySpan<byte> bytes)
    {
        int offset = 8;
        (int, int)? size = null;
        bool hasData = false;
        while (bytes.Length - offset >= 12)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]);
            if (length > bytes.Length - offset - 12) return null;
            var kind = bytes.Slice(offset + 4, 4);
            if (offset == 8)
            {
                if (!kind.SequenceEqual("IHDR"u8) || length != 13) return null;
                int width = BinaryPrimitives.ReadInt32BigEndian(bytes[(offset + 8)..]);
                int height = BinaryPrimitives.ReadInt32BigEndian(bytes[(offset + 12)..]);
                size = (width, height);
            }
            else if (kind.SequenceEqual("IHDR"u8)) return null;
            if (kind.SequenceEqual("IDAT"u8)) hasData |= length > 0;
            if (kind.SequenceEqual("IEND"u8))
                return hasData && length == 0 && offset + 12 == bytes.Length ? size : null;
            offset += (int)length + 12;
        }
        return null;
    }

    private static (int, int)? JpegSize(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || !bytes[^2..].SequenceEqual(new byte[] { 255, 217 })) return null;
        int offset = 2;
        (int, int)? size = null;
        while (offset < bytes.Length - 2)
        {
            if (bytes[offset++] != 255) return null;
            while (offset < bytes.Length && bytes[offset] == 255) offset++;
            if (offset >= bytes.Length) return null;
            int marker = bytes[offset++];
            if (marker is 0 or 216 or 217 || bytes.Length - offset < 2) return null;
            int length = BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);
            if (length < 2 || length > bytes.Length - offset) return null;
            if (marker == 218) return size; // Start of scan, after a validated frame header.
            if (marker is 192 or 193 or 194)
            {
                if (length < 8 || bytes[offset + 2] != 8) return null;
                size = (BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 5)..]),
                    BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 3)..]));
            }
            offset += length;
        }
        return null;
    }
}
