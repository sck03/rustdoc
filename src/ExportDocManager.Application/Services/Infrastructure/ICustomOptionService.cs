namespace ExportDocManager.Services.Infrastructure
{
    public interface ICustomOptionService
    {
        Task<IReadOnlyList<string>> GetOptionsAsync(
            string optionType,
            CancellationToken cancellationToken = default);

        Task SaveOptionAsync(
            string optionType,
            string optionValue,
            CancellationToken cancellationToken = default);
    }
}
