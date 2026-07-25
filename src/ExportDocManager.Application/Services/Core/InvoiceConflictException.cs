namespace ExportDocManager.Services.Core
{
    public sealed class InvoiceConflictException : Exception
    {
        public InvoiceConflictException(string message)
            : base(message)
        {
        }
    }
}
