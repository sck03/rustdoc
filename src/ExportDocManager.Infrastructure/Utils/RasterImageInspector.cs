using System.Buffers.Binary;

namespace ExportDocManager.Utils;

/// <summary>Validates bounded raster containers without a platform image runtime.</summary>
internal static partial class RasterImageInspector
{
    internal sealed record ImageInfo(string MediaType, string Extension, int Width, int Height);

    public static ImageInfo? Inspect(ReadOnlySpan<byte> bytes, int maxDimension, long maxPixels,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return Png(bytes, maxDimension, maxPixels, cancellationToken);
        if (bytes.StartsWith(new byte[] { 255, 216, 255 }))
            return Jpeg(bytes, maxDimension, maxPixels, cancellationToken);
        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8))
            return Gif(bytes, maxDimension, maxPixels, cancellationToken);
        if (bytes.Length >= 20 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
            return Webp(bytes, maxDimension, maxPixels, cancellationToken);
        return null;
    }

    private static bool ValidSize(int width, int height, int maxDimension, long maxPixels) =>
        width > 0 && height > 0 && width <= maxDimension && height <= maxDimension && (long)width * height <= maxPixels;

    private static ImageInfo? Jpeg(ReadOnlySpan<byte> bytes, int maxDimension, long maxPixels, CancellationToken token)
    {
        int offset = 2;
        ImageInfo? info = null;
        bool hasScan = false;
        while (offset < bytes.Length)
        {
            token.ThrowIfCancellationRequested();
            if (bytes[offset++] != 255) return null;
            while (offset < bytes.Length && bytes[offset] == 255) offset++;
            if (offset >= bytes.Length) return null;
            int marker = bytes[offset++];
            if (marker == 217) return hasScan && offset == bytes.Length ? info : null;
            if (marker is 0 or 216 or >= 208 and <= 215 || bytes.Length - offset < 2) return null;
            int length = BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);
            if (length < 2 || length > bytes.Length - offset) return null;
            if (marker is 192 or 193 or 194)
            {
                if (info != null || length < 11 || bytes[offset + 2] != 8) return null;
                int height = BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 3)..]);
                int width = BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 5)..]);
                int components = bytes[offset + 7];
                if (components is < 1 or > 4 || length != 8 + 3 * components || !ValidSize(width, height, maxDimension, maxPixels)) return null;
                info = new("image/jpeg", "jpg", width, height);
            }
            if (marker == 218)
            {
                if (info == null || length < 8 || length != 6 + 2 * bytes[offset + 2]) return null;
                offset += length;
                int scanStart = offset;
                while (offset < bytes.Length)
                {
                    if ((offset & 65535) == 0) token.ThrowIfCancellationRequested();
                    if (bytes[offset] != 255) { offset++; continue; }
                    int markerStart = offset++;
                    while (offset < bytes.Length && bytes[offset] == 255) offset++;
                    if (offset >= bytes.Length) return null;
                    int next = bytes[offset];
                    if (next is 0 or >= 208 and <= 215) { offset++; continue; }
                    offset = markerStart;
                    break;
                }
                if (offset <= scanStart) return null;
                hasScan = true;
            }
            else offset += length;
        }
        return null;
    }

    private static ImageInfo? Gif(ReadOnlySpan<byte> bytes, int maxDimension, long maxPixels, CancellationToken token)
    {
        if (bytes.Length < 14) return null;
        int width = BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]);
        if (!ValidSize(width, height, maxDimension, maxPixels)) return null;
        int offset = 13 + ((bytes[10] & 128) != 0 ? 3 << ((bytes[10] & 7) + 1) : 0);
        long pixels = 0;
        while (offset < bytes.Length)
        {
            token.ThrowIfCancellationRequested();
            int marker = bytes[offset++];
            if (marker == 59) return pixels > 0 && offset == bytes.Length ? new("image/gif", "gif", width, height) : null;
            if (marker == 33)
            {
                if (offset >= bytes.Length) return null;
                offset++; // Extension label, followed by size-delimited sub-blocks.
                if (!SkipGifBlocks(bytes, ref offset, out _)) return null;
                continue;
            }
            if (marker != 44 || bytes.Length - offset < 10) return null;
            int left = BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
            int top = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(offset + 2)..]);
            int frameWidth = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(offset + 4)..]);
            int frameHeight = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(offset + 6)..]);
            if (frameWidth == 0 || frameHeight == 0 || left + frameWidth > width || top + frameHeight > height) return null;
            pixels += (long)frameWidth * frameHeight;
            if (pixels > maxPixels) return null;
            int flags = bytes[offset + 8];
            offset += 9 + ((flags & 128) != 0 ? 3 << ((flags & 7) + 1) : 0);
            if (offset >= bytes.Length || bytes[offset++] is < 2 or > 8) return null;
            if (!SkipGifBlocks(bytes, ref offset, out int dataLength) || dataLength < 2) return null;
        }
        return null;
    }

    private static bool SkipGifBlocks(ReadOnlySpan<byte> bytes, ref int offset, out int dataLength)
    {
        dataLength = 0;
        while (offset < bytes.Length)
        {
            int size = bytes[offset++];
            if (size == 0) return true;
            if (size > bytes.Length - offset) return false;
            offset += size;
            dataLength += size;
        }
        return false;
    }

    private static ImageInfo? Webp(ReadOnlySpan<byte> bytes, int maxDimension, long maxPixels, CancellationToken token)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) != bytes.Length - 8) return null;
        int offset = 12;
        ImageInfo? info = null;
        int canvasWidth = 0, canvasHeight = 0;
        while (bytes.Length - offset >= 8)
        {
            token.ThrowIfCancellationRequested();
            var kind = bytes.Slice(offset, 4);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset + 4)..]);
            if (length > bytes.Length - offset - 8) return null;
            var chunk = bytes.Slice(offset + 8, (int)length);
            if (kind.SequenceEqual("VP8X"u8))
            {
                if (offset != 12 || length != 10 || (chunk[0] & 2) != 0) return null; // Animated WebP has a different frame budget.
                canvasWidth = ReadUInt24(chunk[4..]) + 1;
                canvasHeight = ReadUInt24(chunk[7..]) + 1;
                if (!ValidSize(canvasWidth, canvasHeight, maxDimension, maxPixels)) return null;
            }
            else if (kind.SequenceEqual("VP8 "u8) || kind.SequenceEqual("VP8L"u8))
            {
                if (info != null) return null;
                int width, height;
                if (kind.SequenceEqual("VP8 "u8))
                {
                    if (length < 11 || (chunk[0] & 1) != 0 || !chunk.Slice(3, 3).SequenceEqual(new byte[] { 157, 1, 42 })) return null;
                    if ((ReadUInt24(chunk) >> 5) > length - 10) return null;
                    width = BinaryPrimitives.ReadUInt16LittleEndian(chunk[6..]) & 16383;
                    height = BinaryPrimitives.ReadUInt16LittleEndian(chunk[8..]) & 16383;
                }
                else
                {
                    if (length < 6 || chunk[0] != 47 || (chunk[4] & 224) != 0) return null;
                    uint bits = BinaryPrimitives.ReadUInt32LittleEndian(chunk[1..]);
                    width = (int)(bits & 16383) + 1;
                    height = (int)((bits >> 14) & 16383) + 1;
                }
                if (!ValidSize(width, height, maxDimension, maxPixels) ||
                    canvasWidth != 0 && (canvasWidth != width || canvasHeight != height)) return null;
                info = new("image/webp", "webp", width, height);
            }
            offset += 8 + (int)length + (int)(length & 1);
        }
        return offset == bytes.Length ? info : null;
    }

    private static int ReadUInt24(ReadOnlySpan<byte> bytes) => bytes[0] | bytes[1] << 8 | bytes[2] << 16;
}
