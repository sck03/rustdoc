namespace ExportDocManager.Models.Entities
{
    public enum NotifyPartyMode
    {
        None = 0,
        SameAsConsignee = 1,
        Separate = 2
    }

    public static class NotifyPartyModePolicy
    {
        public static void Normalize(Customer customer)
        {
            ArgumentNullException.ThrowIfNull(customer);
            if (customer.NotifyPartyMode != NotifyPartyMode.Separate)
            {
                customer.NotifyPartyName = string.Empty;
                customer.NotifyPartyAddress = string.Empty;
            }
        }

        public static void Normalize(Invoice invoice)
        {
            ArgumentNullException.ThrowIfNull(invoice);
            if (invoice.NotifyPartyMode != NotifyPartyMode.Separate)
            {
                invoice.NotifyPartyName = string.Empty;
                invoice.NotifyPartyAddress = string.Empty;
            }
        }

        public static (string Name, string Address) ResolveForDocument(
            NotifyPartyMode mode,
            string? consigneeName,
            string? consigneeAddress,
            string? separateName,
            string? separateAddress)
        {
            return mode switch
            {
                // SameAsConsignee is a projection rule, never a persisted
                // notification-party copy. Exporters receive the consignee
                // values directly at the document boundary.
                NotifyPartyMode.SameAsConsignee => (consigneeName ?? string.Empty, consigneeAddress ?? string.Empty),
                NotifyPartyMode.Separate => (separateName ?? string.Empty, separateAddress ?? string.Empty),
                _ => (string.Empty, string.Empty)
            };
        }
    }
}
