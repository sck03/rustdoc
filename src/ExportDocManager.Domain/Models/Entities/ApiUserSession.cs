namespace ExportDocManager.Models.Entities
{
    public sealed class ApiUserSession
    {
        public long Id { get; set; }
        public int UserId { get; set; }
        public string TokenHash { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset LastAccessAt { get; set; }
        public DateTimeOffset? RevokedAt { get; set; }
    }
}
