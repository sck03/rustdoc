using System;
using System.Collections.Generic;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Models.DTOs
{
    public class InvoiceTransferPackage
    {
        public string SchemaVersion { get; set; }
        public string AppVersion { get; set; }
        public DateTime CreatedAt { get; set; }
        public Invoice Invoice { get; set; }
        public List<Item> Items { get; set; }
        public Customer Customer { get; set; }
        public Exporter Exporter { get; set; }
    }

    public class InvoiceTransferReadResult
    {
        public InvoiceTransferPackage Package { get; set; }
        public bool ChecksumValid { get; set; }
        public string ChecksumMessage { get; set; }
    }
}
