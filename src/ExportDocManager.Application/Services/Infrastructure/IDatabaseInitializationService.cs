using System.Threading.Tasks;

namespace ExportDocManager.Services.Infrastructure
{
    public interface IDatabaseInitializationService
    {
        Task<DatabaseInitializationResult> InitializeAsync(
            string username,
            string password,
            string bootstrapToken = null);
    }

    public sealed class DatabaseInitializationResult
    {
        public static DatabaseInitializationResult Success() => new() { IsSuccess = true };

        public static DatabaseInitializationResult Fail(
            string errorMessage,
            bool shouldResetPassword = true,
            bool isAuthenticationFailure = false) =>
            new()
            {
                IsSuccess = false,
                ErrorMessage = errorMessage ?? string.Empty,
                ShouldResetPassword = shouldResetPassword,
                IsAuthenticationFailure = isAuthenticationFailure
            };

        public bool IsSuccess { get; init; }

        public string ErrorMessage { get; init; } = string.Empty;

        public bool ShouldResetPassword { get; init; }

        public bool IsAuthenticationFailure { get; init; }
    }
}
