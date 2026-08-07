using System.Diagnostics;
using System.Text;

namespace ExportDocManager.Utils;

internal static class BoundedProcessOutput
{
    public const int DefaultMaximumCharacters = 1 * 1024 * 1024;

    public static async Task<string> ReadAsync(
        StreamReader reader,
        int maximumCharacters = DefaultMaximumCharacters,
        string truncationMessage = "[外部工具输出过长，已截断]")
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (maximumCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(truncationMessage);

        var output = new StringBuilder(Math.Min(maximumCharacters, 8192));
        char[] buffer = new char[8192];
        int remaining = maximumCharacters;
        bool truncated = false;

        while (true)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (remaining <= 0)
            {
                truncated = true;
                continue;
            }

            int copyLength = Math.Min(remaining, read);
            output.Append(buffer, 0, copyLength);
            remaining -= copyLength;
            truncated |= copyLength < read;
        }

        if (truncated)
        {
            output.AppendLine();
            output.Append(truncationMessage);
        }

        return output.ToString();
    }

    public static async Task ObserveAsync(
        TimeSpan timeout,
        params Task<string>[] outputTasks)
    {
        ArgumentNullException.ThrowIfNull(outputTasks);
        try
        {
            await Task.WhenAll(outputTasks)
                .WaitAsync(timeout)
                .ConfigureAwait(false);
        }
        catch
        {
            // A timeout/cancellation from the process is more useful than a stream-drain error.
        }
    }

    public static async Task DrainProcessAsync(Process process, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(process);
        try
        {
            await process.WaitForExitAsync(CancellationToken.None)
                .WaitAsync(timeout)
                .ConfigureAwait(false);
        }
        catch
        {
            // The caller already has the primary process error.
        }
    }
}
