namespace ExportDocManager.Services.Security
{
    public interface ILicenseSignatureVerifier
    {
        bool TryValidate(string machineId, string licenseKey, out DateTime expireDate);
    }
}
