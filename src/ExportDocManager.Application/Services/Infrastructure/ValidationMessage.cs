namespace ExportDocManager.Services.Infrastructure
{
    public enum ValidationLevel
    {
        Info,
        Warning,
        Error
    }

    public class ValidationMessage
    {
        public string PropertyName { get; set; }
        public string Message { get; set; }
        public ValidationLevel Level { get; set; }
    }
}
