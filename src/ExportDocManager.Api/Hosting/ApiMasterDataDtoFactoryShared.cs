namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiMasterDataDtoFactory
    {
        private static string RowVersionToString(byte[] rowVersion)
        {
            return rowVersion == null || rowVersion.Length == 0
                ? string.Empty
                : Convert.ToBase64String(rowVersion);
        }

        private static byte[] RowVersionFromString(string rowVersion)
        {
            return string.IsNullOrWhiteSpace(rowVersion)
                ? null
                : Convert.FromBase64String(rowVersion);
        }
    }
}
