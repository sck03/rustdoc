namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateSingleWindowOperationCenterSchemas() =>
            new Dictionary<string, object>
            {
                         ["SingleWindowOperationCenterRow"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "batchId",
                                "batchReference",
                                "submissionVersion",
                                "draftRevision",
                                "businessType",
                                "invoiceNo",
                                 "contractNo",
                                 "companyScope",
                                "status",
                                "createdAt",
                                "updatedAt",
                                "receiptCount"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["batchId"] = new { type = "integer", format = "int32" },
                                ["batchReference"] = StringProperty("Single Window submission batch reference."),
                                ["submissionVersion"] = new { type = "integer", format = "int32" },
                                ["draftRevision"] = new { type = "integer", format = "int32" },
                                ["businessType"] = StringProperty("Single Window business type."),
                                ["invoiceNo"] = StringProperty("Source invoice number."),
                                 ["contractNo"] = StringProperty("Source contract number."),
                                 ["companyScope"] = StringProperty("Company identity bound to the batch."),
                                ["status"] = StringProperty("Submission batch status."),
                                ["referenceNo"] = StringProperty("External Single Window reference number."),
                                ["lastReceiptCode"] = StringProperty("Last receipt code."),
                                ["lastReceiptMessage"] = StringProperty("Last receipt message."),
                                 ["clientProfileName"] = StringProperty("Single Window client profile name."),
                                 ["assignedCardIdentifier"] = StringProperty("Assigned non-secret operation-card identifier."),
                                ["createdAt"] = new { type = "string", format = "date-time" },
                                ["updatedAt"] = new { type = "string", format = "date-time" },
                                ["receiptCount"] = new { type = "integer", format = "int32" }
                            }
                        },
                        ["SingleWindowOperationCenterPageResult"] = new
                        {
                            type = "object",
                            required = new[] { "rows", "totalCount", "pageNumber", "pageSize", "totalPages" },
                            properties = new Dictionary<string, object>
                            {
                                ["rows"] = new
                                {
                                    type = "array",
                                    items = RefSchema("SingleWindowOperationCenterRow")
                                },
                                ["totalCount"] = new { type = "integer", format = "int32" },
                                ["pageNumber"] = new { type = "integer", format = "int32" },
                                ["pageSize"] = new { type = "integer", format = "int32" },
                                ["totalPages"] = new { type = "integer", format = "int32" }
                            }
                        },
                        ["SingleWindowOperationCenterPackageRecord"] = new
                        {
                            type = "object",
                             required = new[] { "packageType", "direction", "createdAt" },
                            properties = new Dictionary<string, object>
                            {
                                ["packageType"] = StringProperty("Package type."),
                                ["direction"] = StringProperty("Package direction."),
                                ["payloadFileCount"] = new { type = "integer", format = "int32" },
                                ["attachmentFileCount"] = new { type = "integer", format = "int32" },
                                ["warningCount"] = new { type = "integer", format = "int32" },
                                ["createdAt"] = new { type = "string", format = "date-time" }
                            }
                        },
                        ["SingleWindowOperationCenterReceiptRecord"] = new
                        {
                            type = "object",
                            required = new[] { "receiptKind", "businessStatus", "sourceFileName", "importedAt" },
                            properties = new Dictionary<string, object>
                            {
                                ["receiptKind"] = StringProperty("Receipt kind."),
                                ["referenceNo"] = StringProperty("External Single Window reference number."),
                                ["receiptCode"] = StringProperty("Receipt code."),
                                ["receiptMessage"] = StringProperty("Receipt message."),
                                ["businessStatus"] = StringProperty("Receipt business status."),
                                ["sourceFileName"] = StringProperty("Source receipt file name."),
                                ["importedAt"] = new { type = "string", format = "date-time" },
                                ["occurredAt"] = new { type = "string", format = "date-time", nullable = true }
                            }
                        },
                        ["SingleWindowOperationCenterDetail"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "batchId",
                                "batchReference",
                                "submissionVersion",
                                "draftRevision",
                                "businessType",
                                "invoiceNo",
                                 "contractNo",
                                 "companyScope",
                                "status",
                                "createdAt",
                                "updatedAt",
                                "packageRecords",
                                "receiptRecords"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["batchId"] = new { type = "integer", format = "int32" },
                                ["batchReference"] = StringProperty("Single Window submission batch reference."),
                                ["submissionVersion"] = new { type = "integer", format = "int32" },
                                ["draftRevision"] = new { type = "integer", format = "int32" },
                                ["businessType"] = StringProperty("Single Window business type."),
                                ["invoiceNo"] = StringProperty("Source invoice number."),
                                 ["contractNo"] = StringProperty("Source contract number."),
                                 ["companyScope"] = StringProperty("Company identity bound to the batch."),
                                ["status"] = StringProperty("Submission batch status."),
                                ["referenceNo"] = StringProperty("External Single Window reference number."),
                                 ["clientProfileName"] = StringProperty("Single Window client profile name."),
                                 ["assignedCardIdentifier"] = StringProperty("Assigned non-secret operation-card identifier."),
                                ["payloadFileCount"] = new { type = "integer", format = "int32" },
                                ["attachmentFileCount"] = new { type = "integer", format = "int32" },
                                ["warningCount"] = new { type = "integer", format = "int32" },
                                ["createdAt"] = new { type = "string", format = "date-time" },
                                ["updatedAt"] = new { type = "string", format = "date-time" },
                                ["lastReceiptAt"] = new { type = "string", format = "date-time", nullable = true },
                                ["lastClientDispatchAt"] = new { type = "string", format = "date-time", nullable = true },
                                ["packageRecords"] = new
                                {
                                    type = "array",
                                    items = RefSchema("SingleWindowOperationCenterPackageRecord")
                                },
                                ["receiptRecords"] = new
                                {
                                    type = "array",
                                    items = RefSchema("SingleWindowOperationCenterReceiptRecord")
                                }
                            }
                        },
            };
    }
}
