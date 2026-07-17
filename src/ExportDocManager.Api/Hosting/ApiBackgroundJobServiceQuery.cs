using ExportDocManager.Models;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Api.Hosting
{
    public sealed partial class ApiBackgroundJobService
    {
        public Task<PagedResult<BackgroundJobSnapshot>> QueryAsync(
            BackgroundJobQuery query,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(query);

            int pageNumber = Math.Max(query.PageNumber, 1);
            int pageSize = Math.Clamp(query.PageSize, 1, 100);
            string status = query.Status?.Trim() ?? string.Empty;
            string keyword = query.Keyword?.Trim() ?? string.Empty;
            string requestedBy = query.RequestedBy?.Trim() ?? string.Empty;

            IEnumerable<BackgroundJobSnapshot> jobs = _jobs.Values;
            if (!string.IsNullOrWhiteSpace(requestedBy))
            {
                jobs = jobs.Where(job => string.Equals(job.RequestedBy, requestedBy, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                jobs = jobs.Where(job => string.Equals(job.Status, status, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                jobs = jobs.Where(job =>
                    Contains(job.JobId, keyword)
                    || Contains(job.Kind, keyword)
                    || Contains(job.Title, keyword)
                    || Contains(job.StatusText, keyword)
                    || Contains(job.DetailText, keyword)
                    || Contains(job.RequestedBy, keyword)
                    || Contains(job.OutputPath, keyword)
                    || Contains(job.ErrorMessage, keyword));
            }

            var ordered = jobs
                .OrderByDescending(job => job.CreatedAt)
                .ThenByDescending(job => job.JobId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var page = ordered
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Task.FromResult(new PagedResult<BackgroundJobSnapshot>(
                page,
                ordered.Count,
                pageNumber,
                pageSize));
        }

        public Task<BackgroundJobSnapshot> GetAsync(
            string jobId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(jobId))
            {
                return Task.FromResult<BackgroundJobSnapshot>(null);
            }

            _jobs.TryGetValue(jobId.Trim(), out var job);
            return Task.FromResult(job);
        }

        private static bool Contains(string value, string keyword)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
