namespace ExportDocManager.Services.Infrastructure
{
    public interface ICustomOptionService
    {
        IReadOnlyList<string> GetOptions(string optionType);

        void SaveOption(string optionType, string optionValue);
    }
}
