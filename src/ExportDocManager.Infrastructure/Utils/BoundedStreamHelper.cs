using System.Buffers;
using System.Text;

namespace ExportDocManager.Utils
{
    public sealed class PayloadLimitExceededException : Exception
    {
        public PayloadLimitExceededException(long maximumBytes)
            : base($"数据超过允许的最大大小 {FormatBytes(maximumBytes)}。")
        {
            MaximumBytes = maximumBytes;
        }

        public long MaximumBytes { get; }

        private static string FormatBytes(long value) =>
            value >= 1024L * 1024L
                ? $"{value / (1024d * 1024d):0.##} MB"
                : $"{value / 1024d:0.##} KB";
    }

    public static class BoundedStreamHelper
    {
        private const int BufferSize = 81920;

        public static async Task<long> CopyToAsync(
            Stream source,
            Stream destination,
            long maximumBytes,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(destination);
            if (maximumBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            }

            byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            long total = 0;
            try
            {
                while (true)
                {
                    int read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        return total;
                    }

                    if (total > maximumBytes - read)
                    {
                        throw new PayloadLimitExceededException(maximumBytes);
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);
                    total += read;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }

        public static async Task<string> ReadUtf8TextAsync(
            Stream source,
            long maximumBytes,
            CancellationToken cancellationToken = default)
        {
            await using var buffer = new MemoryStream(
                capacity: (int)Math.Min(maximumBytes, 1024L * 1024L));
            await CopyToAsync(source, buffer, maximumBytes, cancellationToken).ConfigureAwait(false);
            return Encoding.UTF8
                .GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length))
                .TrimStart('\uFEFF');
        }
    }
}
