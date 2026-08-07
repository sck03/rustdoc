using ExportDocManager.Services.Errors;

namespace ExportDocManager.Services.Core
{
    public sealed class InvoiceConflictException : ResourceConflictException
    {
        public InvoiceConflictException(string message)
            : base(message)
        {
        }
    }
}
