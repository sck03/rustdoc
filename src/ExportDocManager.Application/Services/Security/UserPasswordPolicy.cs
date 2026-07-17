namespace ExportDocManager.Services.Security
{
    public static class UserPasswordPolicy
    {
        public const int MinimumLength = 8;
        public const int MaximumLength = 128;

        public static void EnsureValid(string password, string purpose = "密码")
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException($"{purpose}不能为空。");
            }

            if (password.Length < MinimumLength)
            {
                throw new InvalidOperationException($"{purpose}至少需要 {MinimumLength} 个字符。");
            }

            if (password.Length > MaximumLength)
            {
                throw new InvalidOperationException($"{purpose}不能超过 {MaximumLength} 个字符。");
            }
        }
    }
}
