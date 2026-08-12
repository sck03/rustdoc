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
        public string PropertyName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public ValidationLevel Level { get; set; }
    }
}
