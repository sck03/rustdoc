namespace ExportDocManager.Services.Security
{
    public static class UserRoleCatalog
    {
        public const string Admin = "Admin";
        public const string User = "User";
        public const string Finance = "Finance";
        public const string Sales = "Sales";

        public static readonly IReadOnlyList<string> Roles =
        [
            Admin,
            User,
            Finance,
            Sales
        ];

        public static string Normalize(string role)
        {
            var normalized = (role ?? string.Empty).Trim();
            return Roles.FirstOrDefault(item =>
                       string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase))
                   ?? User;
        }
    }
}
