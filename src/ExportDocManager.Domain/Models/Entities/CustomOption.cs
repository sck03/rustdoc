namespace ExportDocManager.Models
{
    public class CustomOption
    {
        public int Id { get; set; }
        public string OptionType { get; set; } = string.Empty;
        public string OptionValue { get; set; } = string.Empty;
        public System.DateTimeOffset CreatedAt { get; set; }
    }
}
