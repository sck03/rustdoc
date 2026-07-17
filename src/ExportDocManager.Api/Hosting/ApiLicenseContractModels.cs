using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public sealed record ApiLicenseStatusResponse(
        bool IsRegistered,
        bool IsTrialExpired,
        int TrialDays,
        int DaysRemaining,
        string MachineId,
        string Message,
        DateTime ExpireDate,
        string LicenseStoragePath,
        string StoragePolicy);

    public sealed record ApiLicenseRegisterRequest(
        string LicenseKey);

    public sealed record ApiLicenseRegisterResponse(
        bool Success,
        string Message,
        ApiLicenseStatusResponse Status);

    public static class ApiLicenseDtoFactory
    {
        public static ApiLicenseStatusResponse FromStatus(LicenseStatus status)
        {
            ArgumentNullException.ThrowIfNull(status);

            return new ApiLicenseStatusResponse(
                status.IsRegistered,
                status.IsTrialExpired,
                status.TrialDays,
                status.DaysRemaining,
                status.MachineId ?? string.Empty,
                status.Message ?? string.Empty,
                status.ExpireDate,
                status.LicenseStoragePath ?? string.Empty,
                status.StoragePolicy ?? string.Empty);
        }

        public static ApiLicenseRegisterResponse FromResult(LicenseRegistrationResult result)
        {
            ArgumentNullException.ThrowIfNull(result);

            return new ApiLicenseRegisterResponse(
                result.Success,
                result.Message ?? string.Empty,
                FromStatus(result.Status));
        }
    }
}
