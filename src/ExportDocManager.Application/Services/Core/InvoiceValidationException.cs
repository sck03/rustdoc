using ExportDocManager.Services.Errors;

namespace ExportDocManager.Services.Core
{
    public sealed class InvoiceValidationException : ServiceValidationException
    {
        public InvoiceValidationException(string message)
            : base(message)
        {
        }
    }
}
