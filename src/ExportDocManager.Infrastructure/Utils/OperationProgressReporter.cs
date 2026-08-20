using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Utils
{
    internal static class OperationProgressReporter
    {
        public static int Calculate(int completed, int total, int startPercent, int endPercent)
        {
            int start = Math.Clamp(startPercent, 0, 100);
            int end = Math.Clamp(endPercent, start, 100);
            if (total <= 0)
            {
                return end;
            }

            double ratio = Math.Clamp((double)completed / total, 0d, 1d);
            return start + (int)Math.Round((end - start) * ratio);
        }

        public static void Report(
            IProgress<OperationProgressUpdate>? progress,
            string statusText,
            string detailText,
            int? percent = null) =>
            progress?.Report(new OperationProgressUpdate
            {
                StatusText = statusText ?? string.Empty,
                DetailText = detailText ?? string.Empty,
                ProgressPercent = percent
            });
    }
}
