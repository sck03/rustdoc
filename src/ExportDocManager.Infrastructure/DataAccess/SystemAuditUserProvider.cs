namespace ExportDocManager.DataAccess
{
    public sealed class SystemAuditUserProvider : IAuditUserProvider
    {
        public string GetCurrentUserName()
        {
            return "System";
        }
    }
}
