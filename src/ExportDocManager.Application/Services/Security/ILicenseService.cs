namespace ExportDocManager.Services.Security
{
    public interface ILicenseService
    {
        Task<LicenseStatus> GetStatusAsync(CancellationToken cancellationToken = default);

        Task<LicenseRegistrationResult> RegisterAsync(
            string licenseKey,
            CancellationToken cancellationToken = default);
    }

    public sealed class LicenseStatus
    {
        public bool IsRegistered { get; init; }
        public bool IsTrialExpired { get; init; }
        public int TrialDays { get; init; }
        public int DaysRemaining { get; init; }
        public string MachineId { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public DateTime ExpireDate { get; init; }
        public string LicenseStoragePath { get; init; } = string.Empty;
        public string StoragePolicy { get; init; } = string.Empty;
    }

    public sealed class LicenseRegistrationResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public LicenseStatus Status { get; init; }
    }
}
