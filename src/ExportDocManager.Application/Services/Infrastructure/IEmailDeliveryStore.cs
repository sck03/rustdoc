using System.Security.Cryptography;
using System.Text;
using ExportDocManager.Models;

namespace ExportDocManager.Services.Infrastructure;

public sealed record EmailDeliveryBeginResult(bool ShouldSend, bool AlreadySent, string? ErrorMessage);
public sealed record EmailDeliverySnapshot(
    string DeliveryId,
    string JobId,
    string Kind,
    string Recipient,
    string Subject,
    int AttachmentCount,
    string Status,
    string ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset UpdatedAt);

public interface IEmailDeliveryStore
{
    Task<EmailDeliveryBeginResult> BeginAsync(string deliveryId, string requestFingerprint, string jobId, string kind, string recipient, string subject, int attachmentCount, CancellationToken cancellationToken = default);
    Task MarkSentAsync(string deliveryId, CancellationToken cancellationToken = default);
    Task MarkUncertainAsync(string deliveryId, string errorMessage, CancellationToken cancellationToken = default);
    Task<PagedResult<EmailDeliverySnapshot>> QueryAsync(string? keyword = null, string? status = null, int pageNumber = 1, int pageSize = 20, CancellationToken cancellationToken = default);
}

public static class EmailDeliveryFingerprint
{
    public static string Create(IEnumerable<string?> parts)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(int)];
        foreach (string? part in parts)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(part ?? string.Empty);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
