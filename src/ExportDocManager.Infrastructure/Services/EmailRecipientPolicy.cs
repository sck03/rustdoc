using System.Globalization;
using ExportDocManager.Models;
using ExportDocManager.Services.Errors;
using MimeKit;

namespace ExportDocManager.Services.Infrastructure;

/// <summary>
/// Enforces one recipient and domain policy for every SMTP delivery path.
/// Rules are either a complete mailbox or a DNS domain; a domain includes
/// its subdomains.  The block list always wins and an empty allow list means
/// that otherwise-valid recipients are allowed.
/// </summary>
public static class EmailRecipientPolicy
{
    private const int MaximumRuleCount = 500;

    public static string ValidateAndNormalize(string? recipient, EmailConfig? config)
    {
        if (string.IsNullOrWhiteSpace(recipient) ||
            !MailboxAddress.TryParse(recipient.Trim(), out var mailbox) ||
            mailbox is null ||
            string.IsNullOrWhiteSpace(mailbox.Address))
        {
            throw new ServiceValidationException("收件人地址无效。");
        }

        var address = ParseAddress(mailbox.Address, "收件人地址");
        var blocked = ParseRules(config?.RecipientBlockList, "收件人黑名单");
        if (blocked.Any(rule => rule.Matches(address)))
        {
            throw new PermissionDeniedException("收件人被邮件外发策略禁止。");
        }

        var allowed = ParseRules(config?.RecipientAllowList, "收件人白名单");
        if (allowed.Count > 0 && !allowed.Any(rule => rule.Matches(address)))
        {
            throw new PermissionDeniedException("收件人不在邮件外发白名单内。");
        }

        return address.Address;
    }

    public static string NormalizeRules(string? rules, string fieldName)
    {
        return string.Join(
            Environment.NewLine,
            ParseRules(rules, fieldName)
                .Select(rule => rule.CanonicalValue)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<RecipientRule> ParseRules(string? rules, string fieldName)
    {
        string[] values = (rules ?? string.Empty)
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length > MaximumRuleCount)
        {
            throw new ServiceValidationException($"{fieldName}不能超过 {MaximumRuleCount} 条规则。");
        }

        var parsed = new List<RecipientRule>(values.Length);
        foreach (string value in values)
        {
            if (value.Contains('@') && !value.StartsWith('@'))
            {
                var address = ParseAddress(value, fieldName);
                parsed.Add(new RecipientRule(address.Address, address.Domain, IsAddress: true));
                continue;
            }

            string domain = value.Trim();
            if (domain.StartsWith("*@", StringComparison.Ordinal))
            {
                domain = domain[2..];
            }
            else if (domain.StartsWith("*.", StringComparison.Ordinal))
            {
                domain = domain[2..];
            }
            else if (domain.StartsWith('@'))
            {
                domain = domain[1..];
            }

            parsed.Add(new RecipientRule(string.Empty, NormalizeDomain(domain, fieldName), IsAddress: false));
        }

        return parsed;
    }

    private static ParsedAddress ParseAddress(string value, string fieldName)
    {
        if (!MailboxAddress.TryParse(value.Trim(), out var mailbox) || mailbox is null)
        {
            throw new ServiceValidationException($"{fieldName}包含无效邮箱地址：{value}");
        }

        string address = mailbox.Address.Trim();
        int separator = address.LastIndexOf('@');
        if (separator <= 0 || separator == address.Length - 1)
        {
            throw new ServiceValidationException($"{fieldName}包含无效邮箱地址：{value}");
        }

        string domain = NormalizeDomain(address[(separator + 1)..], fieldName);
        return new ParsedAddress($"{address[..separator]}@{domain}", domain);
    }

    private static string NormalizeDomain(string value, string fieldName)
    {
        string domain = value.Trim().TrimEnd('.');
        if (domain.Length == 0 || domain.Length > 253 || domain.Contains('/') || domain.Contains('\\'))
        {
            throw new ServiceValidationException($"{fieldName}包含无效域名：{value}");
        }

        try
        {
            domain = new IdnMapping().GetAscii(domain).ToLowerInvariant();
        }
        catch (ArgumentException exception)
        {
            throw new ServiceValidationException($"{fieldName}包含无效域名：{value}", exception);
        }

        string[] labels = domain.Split('.');
        if (labels.Any(label => label.Length is 0 or > 63 ||
                                label.StartsWith('-') ||
                                label.EndsWith('-') ||
                                label.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-')))
        {
            throw new ServiceValidationException($"{fieldName}包含无效域名：{value}");
        }

        return domain;
    }

    private sealed record ParsedAddress(string Address, string Domain);

    private sealed record RecipientRule(string Address, string Domain, bool IsAddress)
    {
        public string CanonicalValue => IsAddress ? Address : $"@{Domain}";

        public bool Matches(ParsedAddress candidate) => IsAddress
            ? string.Equals(Address, candidate.Address, StringComparison.OrdinalIgnoreCase)
            : string.Equals(Domain, candidate.Domain, StringComparison.OrdinalIgnoreCase) ||
              candidate.Domain.EndsWith($".{Domain}", StringComparison.OrdinalIgnoreCase);
    }
}
