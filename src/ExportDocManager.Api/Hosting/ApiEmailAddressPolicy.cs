using MimeKit;

namespace ExportDocManager.Api.Hosting;

internal static class ApiEmailAddressPolicy
{
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)
            || !MailboxAddress.TryParse(value.Trim(), out var mailbox)
            || mailbox is null
            || string.IsNullOrWhiteSpace(mailbox.Address))
        {
            return false;
        }

        normalized = mailbox.Address.Trim();
        return true;
    }
}
