using System.Buffers.Binary;
using System.IO.Compression;

namespace ExportDocManager.Utils;

internal static partial class RasterImageInspector
{
    private static readonly uint[] CrcTable = CreateCrcTable();

    private static ImageInfo? Png(ReadOnlySpan<byte> bytes, int maxDimension, long maxPixels, CancellationToken token)
    {
        int offset = 8, width = 0, height = 0, bitsPerPixel = 0, interlace = 0;
        bool hasPalette = false, needsPalette = false, dataEnded = false;
        using var compressed = new MemoryStream();
        while (bytes.Length - offset >= 12)
        {
            token.ThrowIfCancellationRequested();
            uint length = BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]);
            if (length > bytes.Length - offset - 12) return null;
            var kind = bytes.Slice(offset + 4, 4);
            var data = bytes.Slice(offset + 8, (int)length);
            uint expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(bytes[(offset + 8 + (int)length)..]);
            if (PngCrc(bytes.Slice(offset + 4, (int)length + 4), token) != expectedCrc) return null;
            if (offset == 8)
            {
                if (!kind.SequenceEqual("IHDR"u8) || length != 13) return null;
                width = BinaryPrimitives.ReadInt32BigEndian(data);
                height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                if (!ValidSize(width, height, maxDimension, maxPixels) || data[10] != 0 || data[11] != 0 || data[12] > 1) return null;
                int depth = data[8], color = data[9];
                bool validDepth = color switch
                {
                    0 => depth is 1 or 2 or 4 or 8 or 16,
                    2 or 4 or 6 => depth is 8 or 16,
                    3 => depth is 1 or 2 or 4 or 8,
                    _ => false
                };
                if (!validDepth) return null;
                bitsPerPixel = depth * (color switch { 2 => 3, 4 => 2, 6 => 4, _ => 1 });
                needsPalette = color == 3;
                interlace = data[12];
            }
            else if (kind.SequenceEqual("IHDR"u8)) return null;
            else if (kind.SequenceEqual("PLTE"u8))
            {
                if (hasPalette || compressed.Length > 0 || length is 0 or > 768 || length % 3 != 0) return null;
                hasPalette = true;
            }
            else if (kind.SequenceEqual("IDAT"u8))
            {
                if (dataEnded || needsPalette && !hasPalette) return null;
                compressed.Write(data);
            }
            else if (kind.SequenceEqual("IEND"u8))
            {
                if (length != 0 || offset + 12 != bytes.Length || compressed.Length == 0) return null;
                return ValidatePngScanlines(compressed, width, height, bitsPerPixel, interlace, token)
                    ? new("image/png", "png", width, height) : null;
            }
            else
            {
                if ((kind[0] & 32) == 0 || kind.SequenceEqual("acTL"u8)) return null;
                dataEnded |= compressed.Length > 0;
            }
            offset += (int)length + 12;
        }
        return null;
    }

    private static bool ValidatePngScanlines(MemoryStream compressed, int width, int height, int bitsPerPixel,
        int interlace, CancellationToken token)
    {
        compressed.Position = 0;
        try
        {
            using var stream = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true);
            byte[] row = new byte[(width * bitsPerPixel + 7) / 8];
            (int X, int Y, int Dx, int Dy)[] passes = interlace == 0
                ? [(0, 0, 1, 1)]
                : [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)];
            foreach (var pass in passes)
            {
                int columns = (width - pass.X + pass.Dx - 1) / pass.Dx;
                if (columns <= 0) continue;
                int rowBytes = (columns * bitsPerPixel + 7) / 8;
                for (int y = pass.Y; y < height; y += pass.Dy)
                {
                    token.ThrowIfCancellationRequested();
                    if (stream.ReadByte() is < 0 or > 4) return false;
                    stream.ReadExactly(row.AsSpan(0, rowBytes));
                }
            }
            return stream.ReadByte() == -1;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException) { return false; }
    }

    private static uint PngCrc(ReadOnlySpan<byte> bytes, CancellationToken token)
    {
        uint crc = uint.MaxValue;
        for (int i = 0; i < bytes.Length; i++)
        {
            if ((i & 65535) == 0) token.ThrowIfCancellationRequested();
            crc = CrcTable[(crc ^ bytes[i]) & 255] ^ (crc >> 8);
        }
        return ~crc;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xedb88320 ^ (value >> 1) : value >> 1;
            table[i] = value;
        }
        return table;
    }
}
