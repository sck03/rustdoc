using ExportDocManager.Services.Errors;

namespace ExportDocManager.Services.Security
{
    public sealed class BusinessConcurrencyException : ServiceConcurrencyException
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
