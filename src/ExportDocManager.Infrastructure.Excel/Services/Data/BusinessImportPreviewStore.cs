using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Data;

internal static class BusinessImportPreviewStore
{
    private const int MaximumPayloadBytes = 16 * 1024 * 1024;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static async Task<string> SaveAsync<TRow>(
        AppDbContext context,
        BusinessDataAccessScope accessScope,
        IBusinessClock clock,
        string kind,
        IReadOnlyList<TRow> rows,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(accessScope);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(rows);
        int ownerUserId = accessScope.CurrentUser?.Id ?? 0;
        if (ownerUserId <= 0)
        {
            throw new PermissionDeniedException("当前会话没有可绑定导入预检的用户身份。");
        }

        string normalizedKind = NormalizeKind(kind);
        string payload = JsonSerializer.Serialize(rows, SerializerOptions);
        int payloadBytes = Encoding.UTF8.GetByteCount(payload);
        if (payloadBytes <= 0 || payloadBytes > MaximumPayloadBytes)
        {
            throw new ServiceValidationException(
                $"导入预检内容超过 {MaximumPayloadBytes / 1024 / 1024} MiB 上限，请拆分文件后重试。");
        }

        DateTimeOffset now = clock.UtcNow;
        var expired = await context.BusinessImportPreviews
            .Where(item => item.ExpiresAt <= now || item.ConsumedAt != null && item.ConsumedAt <= now.AddHours(-1))
            .OrderBy(item => item.ExpiresAt)
            .Take(200)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        context.BusinessImportPreviews.RemoveRange(expired);

        string id = Guid.NewGuid().ToString("N");
        context.BusinessImportPreviews.Add(new BusinessImportPreview
        {
            Id = id,
            Kind = normalizedKind,
            OwnerUserId = ownerUserId,
            RowCount = rows.Count,
            PayloadJson = payload,
            PayloadSha256 = ComputeSha256(payload),
            CreatedAt = now,
            ExpiresAt = now.Add(Lifetime),
            VersionNumber = 1
        });
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return id;
    }

    public static async Task<IReadOnlyList<TRow>> LoadForConsumptionAsync<TRow>(
        AppDbContext context,
        BusinessDataAccessScope accessScope,
        IBusinessClock clock,
        string kind,
        string previewId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(accessScope);
        ArgumentNullException.ThrowIfNull(clock);
        if (!Guid.TryParseExact(previewId, "N", out _))
        {
            throw new ServiceValidationException("导入预检编号无效，请重新选择文件。");
        }

        int ownerUserId = accessScope.CurrentUser?.Id ?? 0;
        if (ownerUserId <= 0)
        {
            throw new PermissionDeniedException("当前会话没有可确认导入预检的用户身份。");
        }

        string normalizedKind = NormalizeKind(kind);
        var snapshot = await context.BusinessImportPreviews
            .FirstOrDefaultAsync(item =>
                item.Id == previewId && item.Kind == normalizedKind && item.OwnerUserId == ownerUserId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("导入预检不存在或不属于当前账号，请重新选择文件。");
        if (snapshot.ConsumedAt != null)
        {
            throw new ResourceConflictException("该导入预检已经提交，不能重复导入。");
        }
        if (snapshot.ExpiresAt <= clock.UtcNow)
        {
            throw new ResourceConflictException("导入预检已过期，请重新选择文件。");
        }

        string payload = snapshot.PayloadJson ?? string.Empty;
        int payloadBytes = Encoding.UTF8.GetByteCount(payload);
        if (payloadBytes <= 0 || payloadBytes > MaximumPayloadBytes ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(snapshot.PayloadSha256 ?? string.Empty),
                Encoding.ASCII.GetBytes(ComputeSha256(payload))))
        {
            throw new InfrastructureServiceException("导入预检完整性校验失败，数据库内容可能已损坏。");
        }

        IReadOnlyList<TRow> rows;
        try
        {
            rows = JsonSerializer.Deserialize<TRow[]>(payload, SerializerOptions)
                   ?? throw new JsonException("导入预检没有数据行。");
        }
        catch (JsonException exception)
        {
            throw new InfrastructureServiceException("导入预检内容无法读取，数据库内容可能已损坏。", exception);
        }

        if (rows.Count != snapshot.RowCount)
        {
            throw new InfrastructureServiceException("导入预检行数校验失败，数据库内容可能已损坏。");
        }

        context.Entry(snapshot).Property(item => item.VersionNumber).OriginalValue = snapshot.VersionNumber;
        snapshot.ConsumedAt = clock.UtcNow;
        snapshot.VersionNumber++;
        return rows;
    }

    private static string NormalizeKind(string kind)
    {
        string normalized = (kind ?? string.Empty).Trim();
        if (normalized.Length is 0 or > 30)
        {
            throw new ArgumentException("导入预检类型无效。", nameof(kind));
        }

        return normalized;
    }

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
