using System.Security.Cryptography;
using System.Text;

namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiDesktopAccessOptions
    {
        public const string HeaderName = "X-ExportDocManager-Desktop-Token";

        public string Token { get; init; } = string.Empty;

        public bool IsEnabled => !string.IsNullOrWhiteSpace(Token);

        public static ApiDesktopAccessOptions FromRuntimeOptions(ApiRuntimeOptions runtimeOptions)
        {
            ArgumentNullException.ThrowIfNull(runtimeOptions);

            return new ApiDesktopAccessOptions
            {
                Token = runtimeOptions.DesktopAccessToken?.Trim() ?? string.Empty
            };
        }

        public bool IsValid(string submittedToken)
        {
            if (!IsEnabled || string.IsNullOrWhiteSpace(submittedToken))
            {
                return false;
            }

            byte[] expected = Encoding.UTF8.GetBytes(Token);
            byte[] actual = Encoding.UTF8.GetBytes(submittedToken.Trim());
            return expected.Length == actual.Length &&
                CryptographicOperations.FixedTimeEquals(expected, actual);
        }
    }
}
