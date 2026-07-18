namespace ExportDocManager.Services.Security
{
    public sealed class BusinessConcurrencyException : InvalidOperationException
    {
        public BusinessConcurrencyException(string message)
            : base(message)
        {
        }

        public BusinessConcurrencyException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
