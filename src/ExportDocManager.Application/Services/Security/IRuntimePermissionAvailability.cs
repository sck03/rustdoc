using ExportDocManager.Services.Office;

namespace ExportDocManager.Services.Security;

/// <summary>Edition and installed-module policy, supplied by the hosting composition root.</summary>
public interface IRuntimePermissionAvailability
{
    bool IsPermissionAvailable(string resourceKey, string action);
    OfficeOperatingMode OfficeMode { get; }
}
