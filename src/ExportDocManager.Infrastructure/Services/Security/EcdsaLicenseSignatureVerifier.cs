using ExportDocManager.Shared.Security;

namespace ExportDocManager.Services.Security
{
    public sealed class EcdsaLicenseSignatureVerifier : ILicenseSignatureVerifier
    {
        public bool TryValidate(string machineId, string licenseKey, out DateTime expireDate)
        {
            return LicenseKeyCodec.TryValidateSigned(
                machineId,
                licenseKey,
                LicenseDefaults.SignaturePublicKey,
                out expireDate);
        }
    }
}
