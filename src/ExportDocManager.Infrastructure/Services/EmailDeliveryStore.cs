using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Infrastructure;

public sealed class EmailDeliveryStore : IEmailDeliveryStore
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly TimeProvider _timeProvider;
    private readonly BusinessDataAccessScope _accessScope;

    public EmailDeliveryStore(
        IDbContextFactory<AppDbContext> contextFactory,
        BusinessDataAccessScope accessScope,
        TimeProvider? timeProvider = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _accessScope = accessScope ?? throw new ArgumentNullException(nameof(accessScope));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<EmailDeliveryBeginResult> BeginAsync(string deliveryId, string requestFingerprint, string jobId, string kind, string recipient, string subject, int attachmentCount, CancellationToken cancellationToken = default)
    {
        string key = Required(deliveryId, nameof(deliveryId), 120);
        string fingerprint = Required(requestFingerprint, nameof(requestFingerprint), 64);
        if (fingerprint.Length != 64 || fingerprint.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("邮件投递请求指纹无效。", nameof(requestFingerprint));
        }
        if (attachmentCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attachmentCount));
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var existing = await context.EmailDeliveryRecords.SingleOrDefaultAsync(item => item.DeliveryId == key, cancellationToken).ConfigureAwait(false);
        if (existing != null)
        {
            return ResolveExisting(existing, fingerprint);
        }

        var currentUser = _accessScope.CurrentUser;
        context.EmailDeliveryRecords.Add(new EmailDeliveryRecord
        {
            DeliveryId = key,
            RequestFingerprint = fingerprint,
            JobId = Truncate(jobId, 120),
            Kind = Truncate(kind, 80),
            OwnerUserId = currentUser?.Id ?? 0,
            RequestedBy = Truncate(currentUser?.Username, 100),
            Recipient = Truncate(recipient, 320),
            Subject = Truncate(subject, 300),
            AttachmentCount = attachmentCount,
            Status = EmailDeliveryStatus.Attempting
        });
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new EmailDeliveryBeginResult(true, false, null);
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();
            var winner = await context.EmailDeliveryRecords.AsNoTracking().SingleOrDefaultAsync(item => item.DeliveryId == key, cancellationToken).ConfigureAwait(false);
            if (winner == null)
            {
                throw;
            }

            return ResolveExisting(winner, fingerprint);
        }
    }

    public Task MarkSentAsync(string deliveryId, CancellationToken cancellationToken = default) => UpdateAsync(deliveryId, item =>
    {
        item.Status = EmailDeliveryStatus.Sent;
        item.SentAt = _timeProvider.GetUtcNow();
        item.ErrorMessage = string.Empty;
    }, cancellationToken);

    public Task MarkUncertainAsync(string deliveryId, string errorMessage, CancellationToken cancellationToken = default) => UpdateAsync(deliveryId, item =>
    {
        item.Status = EmailDeliveryStatus.Uncertain;
        string message = (errorMessage ?? string.Empty).Trim();
        item.ErrorMessage = message[..Math.Min(message.Length, 4000)];
    }, cancellationToken);

    public async Task<IReadOnlyList<EmailDeliverySnapshot>> ListRecentAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        int pageSize = Math.Clamp(limit, 1, 100);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var query = context.EmailDeliveryRecords.AsNoTracking();
        var currentUser = _accessScope.CurrentUser;
        if (_accessScope.ShouldFilterBusinessData(currentUser))
        {
            int userId = currentUser?.Id ?? 0;
            query = query.Where(item => item.OwnerUserId == userId);
        }

        return await query
            .OrderByDescending(item => item.CreatedAt)
            .Take(pageSize)
            .Select(item => new EmailDeliverySnapshot(
                item.DeliveryId,
                item.JobId,
                item.Kind,
                item.Recipient,
                item.Subject,
                item.AttachmentCount,
                item.Status,
                item.ErrorMessage,
                item.CreatedAt,
                item.SentAt,
                item.UpdatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task UpdateAsync(string deliveryId, Action<EmailDeliveryRecord> update, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var record = await context.EmailDeliveryRecords.SingleOrDefaultAsync(item => item.DeliveryId == deliveryId, cancellationToken).ConfigureAwait(false);
        if (record == null) return;
        update(record);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static EmailDeliveryBeginResult ResolveExisting(EmailDeliveryRecord record, string fingerprint)
    {
        if (!string.Equals(record.RequestFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return new EmailDeliveryBeginResult(false, false, "该幂等键已用于另一封邮件，请修改邮件后重新发送。");
        }

        return record.Status == EmailDeliveryStatus.Sent
            ? new EmailDeliveryBeginResult(false, true, null)
            : new EmailDeliveryBeginResult(false, false, "该邮件已经尝试投递，但 SMTP 结果不确定。为避免重复发送，系统不会自动再次投递。");
    }

    private static string Required(string value, string field, int maxLength)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maxLength)
        {
            throw new ArgumentException($"{field}长度无效。", field);
        }

        return normalized;
    }

    private static string Truncate(string? value, int maxLength)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized[..Math.Min(normalized.Length, maxLength)];
    }
}
