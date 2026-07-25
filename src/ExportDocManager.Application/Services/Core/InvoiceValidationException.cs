namespace ExportDocManager.Services.Core
{
    public sealed class InvoiceValidationException : Exception
    {
        public InvoiceValidationException(string message)
            : base(message)
        {
        }
    }
}
