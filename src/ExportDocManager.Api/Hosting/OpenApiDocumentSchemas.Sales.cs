namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateSalesSchemas() =>
            new Dictionary<string, object>
            {
                        ["ApiCrmDashboardDto"] = new
                        {
                            type = "object",
                            required = new[] { "customerCount", "contactCount", "pendingFollowUpCount", "overdueFollowUpCount", "dueNextSevenDaysCount", "upcomingFollowUps", "opportunityStages", "opportunityCurrencies", "upcomingOpportunityClosings" },
                            properties = new Dictionary<string, object>
                            {
                                ["customerCount"] = new { type = "integer", format = "int32" },
                                ["contactCount"] = new { type = "integer", format = "int32" },
                                ["pendingFollowUpCount"] = new { type = "integer", format = "int32" },
                                ["overdueFollowUpCount"] = new { type = "integer", format = "int32" },
                                ["dueNextSevenDaysCount"] = new { type = "integer", format = "int32" },
                                ["upcomingFollowUps"] = new { type = "array", items = RefSchema("ApiCrmFollowUpDto") },
                                ["opportunityStages"] = new { type = "array", items = RefSchema("ApiSalesOpportunityStageSummaryDto") },
                                ["opportunityCurrencies"] = new { type = "array", items = RefSchema("ApiSalesOpportunityCurrencySummaryDto") },
                                ["upcomingOpportunityClosings"] = new { type = "array", items = RefSchema("ApiSalesOpportunityDto") }
                            }
                        },
                        ["ApiSalesOpportunityStageSummaryDto"] = new
                        {
                            type = "object", required = new[] { "stage", "count" },
                            properties = new Dictionary<string, object>
                            {
                                ["stage"] = StringProperty("Opportunity stage."),
                                ["count"] = new { type = "integer", format = "int32" }
                            }
                        },
                        ["ApiSalesOpportunityCurrencySummaryDto"] = new
                        {
                            type = "object", required = new[] { "currency", "count", "estimatedAmount", "weightedAmount" },
                            properties = new Dictionary<string, object>
                            {
                                ["currency"] = StringProperty("ISO currency code."),
                                ["count"] = new { type = "integer", format = "int32" },
                                ["estimatedAmount"] = new { type = "number", format = "decimal" },
                                ["weightedAmount"] = new { type = "number", format = "decimal" }
                            }
                        },
                        ["ApiPagedResponseOfApiSalesOpportunityDto"] = new
                        {
                            type = "object",
                            required = new[] { "items", "totalCount", "pageNumber", "pageSize", "totalPages", "hasPreviousPage", "hasNextPage" },
                            properties = new Dictionary<string, object>
                            {
                                ["items"] = new { type = "array", items = RefSchema("ApiSalesOpportunityDto") },
                                ["totalCount"] = new { type = "integer", format = "int32" }, ["pageNumber"] = new { type = "integer", format = "int32" },
                                ["pageSize"] = new { type = "integer", format = "int32" }, ["totalPages"] = new { type = "integer", format = "int32" },
                                ["hasPreviousPage"] = new { type = "boolean" }, ["hasNextPage"] = new { type = "boolean" }
                            }
                        },
                        ["ApiSalesOpportunityDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "crmCustomerId", "customerName", "title", "stage", "quotationNo", "estimatedAmount", "currency", "probabilityPercent", "nextAction", "notes" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" }, ["crmCustomerId"] = new { type = "integer", format = "int32" },
                                ["customerName"] = StringProperty("CRM customer name."), ["productId"] = new { type = "integer", format = "int32", nullable = true },
                                ["productCode"] = StringProperty("Optional product code."), ["productName"] = StringProperty("Optional product name."),
                                ["title"] = StringProperty("Opportunity title."), ["stage"] = StringProperty("Opportunity stage."),
                                ["quotationNo"] = StringProperty("Latest quotation tracking number."), ["estimatedAmount"] = new { type = "number", format = "decimal" },
                                ["currency"] = StringProperty("ISO currency code."), ["probabilityPercent"] = new { type = "integer", format = "int32" },
                                ["expectedCloseAt"] = new { type = "string", format = "date-time", nullable = true },
                                ["nextAction"] = StringProperty("Next action."), ["notes"] = StringProperty("Notes.")
                            }
                        },
                        ["ApiSalesOpportunitySaveRequest"] = new
                        {
                            type = "object",
                            required = new[] { "id", "crmCustomerId", "title", "stage", "quotationNo", "estimatedAmount", "currency", "probabilityPercent", "nextAction", "notes", "changeNote" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" }, ["crmCustomerId"] = new { type = "integer", format = "int32" },
                                ["productId"] = new { type = "integer", format = "int32", nullable = true }, ["title"] = StringProperty("Opportunity title."),
                                ["stage"] = StringProperty("Opportunity stage."), ["quotationNo"] = StringProperty("Latest quotation tracking number."),
                                ["estimatedAmount"] = new { type = "number", format = "decimal" }, ["currency"] = StringProperty("ISO currency code."),
                                ["probabilityPercent"] = new { type = "integer", format = "int32" },
                                ["expectedCloseAt"] = new { type = "string", format = "date-time", nullable = true },
                                ["nextAction"] = StringProperty("Next action."), ["notes"] = StringProperty("Notes."),
                                ["changeNote"] = StringProperty("Append-only history note for this change.")
                            }
                        },
                        ["ApiSalesOpportunityHistoryDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "salesOpportunityId", "versionNumber", "changeType", "stage", "quotationNo", "estimatedAmount", "currency", "probabilityPercent", "changeNote", "changedBy", "createdAt" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" }, ["salesOpportunityId"] = new { type = "integer", format = "int32" },
                                ["versionNumber"] = new { type = "integer", format = "int32" }, ["changeType"] = StringProperty("Change type."),
                                ["stage"] = StringProperty("Stage snapshot."), ["quotationNo"] = StringProperty("Quotation number snapshot."),
                                ["estimatedAmount"] = new { type = "number", format = "decimal" }, ["currency"] = StringProperty("Currency snapshot."),
                                ["probabilityPercent"] = new { type = "integer", format = "int32" },
                                ["expectedCloseAt"] = new { type = "string", format = "date-time", nullable = true },
                                ["changeNote"] = StringProperty("Change note."), ["changedBy"] = StringProperty("Operator."),
                                ["createdAt"] = new { type = "string", format = "date-time" }
                            }
                        },
                        ["ApiPagedResponseOfApiCrmCustomerDto"] = new
                        {
                            type = "object",
                            required = new[] { "items", "totalCount", "pageNumber", "pageSize", "totalPages", "hasPreviousPage", "hasNextPage" },
                            properties = new Dictionary<string, object>
                            {
                                ["items"] = new { type = "array", items = RefSchema("ApiCrmCustomerDto") },
                                ["totalCount"] = new { type = "integer", format = "int32" },
                                ["pageNumber"] = new { type = "integer", format = "int32" },
                                ["pageSize"] = new { type = "integer", format = "int32" },
                                ["totalPages"] = new { type = "integer", format = "int32" },
                                ["hasPreviousPage"] = new { type = "boolean" },
                                ["hasNextPage"] = new { type = "boolean" }
                            }
                        },
                        ["ApiCrmCustomerDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "name", "status" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["name"] = StringProperty("CRM customer name."),
                                ["countryRegion"] = StringProperty("Country or region."),
                                ["website"] = StringProperty("Customer website."),
                                ["status"] = StringProperty("Customer status."),
                                ["source"] = StringProperty("Customer source."),
                                ["notes"] = StringProperty("Notes."),
                                ["linkedDocumentCustomerId"] = new { type = "integer", format = "int32", nullable = true }
                            }
                        },
                        ["ApiCrmCustomerSaveRequest"] = new
                        {
                            type = "object",
                            required = new[] { "id", "name", "status" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["name"] = StringProperty("CRM customer name."),
                                ["countryRegion"] = StringProperty("Country or region."),
                                ["website"] = StringProperty("Customer website."),
                                ["status"] = StringProperty("Customer status."),
                                ["source"] = StringProperty("Customer source."),
                                ["notes"] = StringProperty("Notes."),
                                ["linkedDocumentCustomerId"] = new { type = "integer", format = "int32", nullable = true }
                            }
                        },
                        ["ApiCrmContactDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "crmCustomerId", "name", "isPrimary" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["crmCustomerId"] = new { type = "integer", format = "int32" },
                                ["name"] = StringProperty("Contact name."),
                                ["title"] = StringProperty("Job title."),
                                ["email"] = StringProperty("Email."),
                                ["phone"] = StringProperty("Phone."),
                                ["instantMessaging"] = StringProperty("Instant messaging account."),
                                ["isPrimary"] = new { type = "boolean" }
                            }
                        },
                        ["ApiCrmContactSaveRequest"] = new
                        {
                            type = "object",
                            required = new[] { "id", "crmCustomerId", "name", "isPrimary" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["crmCustomerId"] = new { type = "integer", format = "int32" },
                                ["name"] = StringProperty("Contact name."),
                                ["title"] = StringProperty("Job title."),
                                ["email"] = StringProperty("Email."),
                                ["phone"] = StringProperty("Phone."),
                                ["instantMessaging"] = StringProperty("Instant messaging account."),
                                ["isPrimary"] = new { type = "boolean" }
                            }
                        },
                        ["ApiCrmFollowUpDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "crmCustomerId", "customerName", "contactName", "type", "summary", "followedUpAt", "isCompleted", "createdAt", "updatedAt" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["crmCustomerId"] = new { type = "integer", format = "int32" },
                                ["customerName"] = StringProperty("CRM customer name."),
                                ["crmContactId"] = new { type = "integer", format = "int32", nullable = true },
                                ["contactName"] = StringProperty("CRM contact name."),
                                ["type"] = StringProperty("Follow-up type."),
                                ["summary"] = StringProperty("Follow-up summary."),
                                ["nextAction"] = StringProperty("Next action."),
                                ["followedUpAt"] = new { type = "string", format = "date-time" },
                                ["nextFollowUpAt"] = new { type = "string", format = "date-time", nullable = true },
                                ["isCompleted"] = new { type = "boolean" },
                                ["createdAt"] = new { type = "string", format = "date-time" },
                                ["updatedAt"] = new { type = "string", format = "date-time" }
                            }
                        },
                        ["ApiCrmFollowUpSaveRequest"] = new
                        {
                            type = "object",
                            required = new[] { "id", "crmCustomerId", "type", "summary", "isCompleted" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["crmCustomerId"] = new { type = "integer", format = "int32" },
                                ["crmContactId"] = new { type = "integer", format = "int32", nullable = true },
                                ["type"] = StringProperty("Follow-up type."),
                                ["summary"] = StringProperty("Follow-up summary."),
                                ["nextAction"] = StringProperty("Next action."),
                                ["followedUpAt"] = new { type = "string", format = "date-time", nullable = true },
                                ["nextFollowUpAt"] = new { type = "string", format = "date-time", nullable = true },
                                ["isCompleted"] = new { type = "boolean" }
                            }
                        },
                        ["ApiCrmCustomerImportRowDto"] = new
                        {
                            type = "object",
                            required = new[] { "rowNumber", "name", "status", "isDuplicate", "error" },
                            properties = new Dictionary<string, object>
                            {
                                ["rowNumber"] = new { type = "integer", format = "int32" },
                                ["name"] = StringProperty("CRM customer name."),
                                ["countryRegion"] = StringProperty("Country or region."),
                                ["website"] = StringProperty("Website."),
                                ["status"] = StringProperty("Status."),
                                ["source"] = StringProperty("Source."),
                                ["notes"] = StringProperty("Notes."),
                                ["contactName"] = StringProperty("Primary contact name."),
                                ["contactTitle"] = StringProperty("Primary contact title."),
                                ["contactEmail"] = StringProperty("Primary contact email."),
                                ["contactPhone"] = StringProperty("Primary contact phone."),
                                ["isDuplicate"] = new { type = "boolean" },
                                ["error"] = StringProperty("Validation error.")
                            }
                        },
                        ["ApiCrmCustomerImportPreviewDto"] = new
                        {
                            type = "object",
                            required = new[] { "totalRows", "validRows", "duplicateRows", "rows" },
                            properties = new Dictionary<string, object>
                            {
                                ["totalRows"] = new { type = "integer", format = "int32" },
                                ["validRows"] = new { type = "integer", format = "int32" },
                                ["duplicateRows"] = new { type = "integer", format = "int32" },
                                ["rows"] = new { type = "array", items = RefSchema("ApiCrmCustomerImportRowDto") }
                            }
                        },
                        ["ApiCrmCustomerImportRequest"] = new
                        {
                            type = "object",
                            required = new[] { "rows" },
                            properties = new Dictionary<string, object>
                            {
                                ["rows"] = new { type = "array", items = RefSchema("ApiCrmCustomerImportRowDto") }
                            }
                        },
                        ["ApiCrmCustomerImportResultDto"] = new
                        {
                            type = "object",
                            required = new[] { "createdCustomers", "createdContacts", "skippedDuplicates" },
                            properties = new Dictionary<string, object>
                            {
                                ["createdCustomers"] = new { type = "integer", format = "int32" },
                                ["createdContacts"] = new { type = "integer", format = "int32" },
                                ["skippedDuplicates"] = new { type = "integer", format = "int32" }
                            }
                        },
                        ["ApiCrmCustomerBatchStatusRequest"] = new
                        {
                            type = "object", required = new[] { "ids", "status" },
                            properties = new Dictionary<string, object>
                            {
                                ["ids"] = new { type = "array", items = new { type = "integer", format = "int32" } },
                                ["status"] = StringProperty("Target CRM customer status.")
                            }
                        },
                        ["ApiCrmCustomerBatchStatusResult"] = new
                        {
                            type = "object", required = new[] { "affectedCount", "status" },
                            properties = new Dictionary<string, object>
                            {
                                ["affectedCount"] = new { type = "integer", format = "int32" },
                                ["status"] = StringProperty("Updated CRM customer status.")
                            }
                        },
                        ["ApiCrmEmailVariableDraftDto"] = new
                        {
                            type = "object", required = new[] { "crmCustomerId", "toAddress", "variables" },
                            properties = new Dictionary<string, object>
                            {
                                ["crmCustomerId"] = new { type = "integer", format = "int32" },
                                ["crmContactId"] = new { type = "integer", format = "int32", nullable = true },
                                ["toAddress"] = StringProperty("Primary contact email address."),
                                ["variables"] = new { type = "object", additionalProperties = new { type = "string" } }
                            }
                        },
                        ["ApiEmailTemplateDto"] = new
                        {
                            type = "object", required = new[] { "id", "name", "category", "subject", "bodyHtml", "isActive", "isShared", "versionNumber", "canEdit" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" }, ["name"] = StringProperty("Template name."),
                                ["category"] = StringProperty("Template category."), ["subject"] = StringProperty("Email subject."),
                                ["bodyHtml"] = StringProperty("Email HTML body."), ["isActive"] = new { type = "boolean" },
                                ["isShared"] = new { type = "boolean" }, ["versionNumber"] = new { type = "integer", format = "int32" },
                                ["canEdit"] = new { type = "boolean" }
                            }
                        },
                        ["ApiEmailTemplateSaveRequest"] = new
                        {
                            type = "object", required = new[] { "id", "name", "category", "subject", "bodyHtml", "isActive", "isShared" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" }, ["name"] = StringProperty("Template name."),
                                ["category"] = StringProperty("Template category."), ["subject"] = StringProperty("Email subject."),
                                ["bodyHtml"] = StringProperty("Email HTML body."), ["isActive"] = new { type = "boolean" },
                                ["isShared"] = new { type = "boolean" }
                            }
                        },
                        ["ApiEmailTemplateVariableDto"] = new
                        {
                            type = "object", required = new[] { "key", "token", "label", "sampleValue" },
                            properties = new Dictionary<string, object>
                            {
                                ["key"] = StringProperty("Variable key."), ["token"] = StringProperty("Template token."),
                                ["label"] = StringProperty("Variable label."), ["sampleValue"] = StringProperty("Sample value.")
                            }
                        },
                        ["ApiEmailTemplatePreviewRequest"] = new
                        {
                            type = "object", required = new[] { "subject", "bodyHtml", "variables" },
                            properties = new Dictionary<string, object>
                            {
                                ["subject"] = StringProperty("Subject template."), ["bodyHtml"] = StringProperty("Body template."),
                                ["variables"] = new { type = "object", additionalProperties = new { type = "string" } }
                            }
                        },
                        ["ApiEmailTemplatePreviewDto"] = new
                        {
                            type = "object", required = new[] { "subject", "bodyHtml", "unresolvedTokens" },
                            properties = new Dictionary<string, object>
                            {
                                ["subject"] = StringProperty("Rendered subject."), ["bodyHtml"] = StringProperty("Rendered HTML body."),
                                ["unresolvedTokens"] = new { type = "array", items = new { type = "string" } }
                            }
                        },
                        ["ApiEmailTemplateVersionDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "emailTemplateId", "versionNumber", "changeType", "name", "category", "subject", "bodyHtml", "isActive", "isShared", "changedBy", "createdAt", "canRestore" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["emailTemplateId"] = new { type = "integer", format = "int32" },
                                ["versionNumber"] = new { type = "integer", format = "int32" },
                                ["changeType"] = StringProperty("Version change type."),
                                ["name"] = StringProperty("Template name."), ["category"] = StringProperty("Template category."),
                                ["subject"] = StringProperty("Email subject."), ["bodyHtml"] = StringProperty("Email HTML body."),
                                ["isActive"] = new { type = "boolean" }, ["isShared"] = new { type = "boolean" },
                                ["changedBy"] = StringProperty("Account that created the version."),
                                ["createdAt"] = new { type = "string", format = "date-time" }, ["canRestore"] = new { type = "boolean" }
                            }
                        },
                        ["ApiSupplierDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "name", "status" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" }, ["name"] = StringProperty("Supplier name."),
                                ["countryRegion"] = StringProperty("Country or region."), ["category"] = StringProperty("Supplier category."),
                                ["website"] = StringProperty("Website."), ["status"] = StringProperty("Supplier status."),
                                ["mainProducts"] = StringProperty("Main products."), ["notes"] = StringProperty("Notes.")
                            }
                        },
                        ["ApiSupplierSaveRequest"] = new
                        {
                            type = "object",
                            required = new[] { "id", "name", "status" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" }, ["name"] = StringProperty("Supplier name."),
                                ["countryRegion"] = StringProperty("Country or region."), ["category"] = StringProperty("Supplier category."),
                                ["website"] = StringProperty("Website."), ["status"] = StringProperty("Supplier status."),
                                ["mainProducts"] = StringProperty("Main products."), ["notes"] = StringProperty("Notes.")
                            }
                        },
                        ["ApiSupplierContactDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "supplierCompanyId", "name", "isPrimary" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" }, ["supplierCompanyId"] = new { type = "integer", format = "int32" },
                                ["name"] = StringProperty("Contact name."), ["title"] = StringProperty("Job title."),
                                ["email"] = StringProperty("Email."), ["phone"] = StringProperty("Phone."),
                                ["instantMessaging"] = StringProperty("Instant messaging."), ["isPrimary"] = new { type = "boolean" }
                            }
                        },
                        ["ApiSupplierContactSaveRequest"] = new
                        {
                            type = "object",
                            required = new[] { "id", "supplierCompanyId", "name", "isPrimary" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" }, ["supplierCompanyId"] = new { type = "integer", format = "int32" },
                                ["name"] = StringProperty("Contact name."), ["title"] = StringProperty("Job title."),
                                ["email"] = StringProperty("Email."), ["phone"] = StringProperty("Phone."),
                                ["instantMessaging"] = StringProperty("Instant messaging."), ["isPrimary"] = new { type = "boolean" }
                            }
                        },
                        ["ApiPagedResponseOfApiSupplierDto"] = new
                        {
                            type = "object",
                            required = new[] { "items", "totalCount", "pageNumber", "pageSize", "totalPages", "hasPreviousPage", "hasNextPage" },
                            properties = new Dictionary<string, object>
                            {
                                ["items"] = new { type = "array", items = RefSchema("ApiSupplierDto") },
                                ["totalCount"] = new { type = "integer", format = "int32" }, ["pageNumber"] = new { type = "integer", format = "int32" },
                                ["pageSize"] = new { type = "integer", format = "int32" }, ["totalPages"] = new { type = "integer", format = "int32" },
                                ["hasPreviousPage"] = new { type = "boolean" }, ["hasNextPage"] = new { type = "boolean" }
                            }
                        },
                        ["ApiSupplierImportRowDto"] = new
                        {
                            type = "object", required = new[] { "rowNumber", "name", "status", "isDuplicate", "error" },
                            properties = new Dictionary<string, object>
                            {
                                ["rowNumber"] = new { type = "integer", format = "int32" }, ["name"] = StringProperty("Supplier name."),
                                ["countryRegion"] = StringProperty("Country or region."), ["category"] = StringProperty("Category."),
                                ["website"] = StringProperty("Website."), ["status"] = StringProperty("Status."),
                                ["mainProducts"] = StringProperty("Main products."), ["notes"] = StringProperty("Notes."),
                                ["contactName"] = StringProperty("Contact name."), ["contactTitle"] = StringProperty("Contact title."),
                                ["contactEmail"] = StringProperty("Contact email."), ["contactPhone"] = StringProperty("Contact phone."),
                                ["isDuplicate"] = new { type = "boolean" }, ["error"] = StringProperty("Validation error.")
                            }
                        },
                        ["ApiSupplierImportPreviewDto"] = new
                        {
                            type = "object", required = new[] { "totalRows", "validRows", "duplicateRows", "rows" },
                            properties = new Dictionary<string, object>
                            {
                                ["totalRows"] = new { type = "integer", format = "int32" }, ["validRows"] = new { type = "integer", format = "int32" },
                                ["duplicateRows"] = new { type = "integer", format = "int32" },
                                ["rows"] = new { type = "array", items = RefSchema("ApiSupplierImportRowDto") }
                            }
                        },
                        ["ApiSupplierImportRequest"] = new
                        {
                            type = "object", required = new[] { "rows" },
                            properties = new Dictionary<string, object> { ["rows"] = new { type = "array", items = RefSchema("ApiSupplierImportRowDto") } }
                        },
                        ["ApiSupplierImportResultDto"] = new
                        {
                            type = "object", required = new[] { "createdSuppliers", "createdContacts", "skippedRows" },
                            properties = new Dictionary<string, object>
                            {
                                ["createdSuppliers"] = new { type = "integer", format = "int32" }, ["createdContacts"] = new { type = "integer", format = "int32" },
                                ["skippedRows"] = new { type = "integer", format = "int32" }
                            }
                        },
                        ["ApiSupplierBatchStatusRequest"] = new
                        {
                            type = "object", required = new[] { "ids", "status" },
                            properties = new Dictionary<string, object>
                            {
                                ["ids"] = new { type = "array", items = new { type = "integer", format = "int32" } }, ["status"] = StringProperty("Target status.")
                            }
                        },
                        ["ApiSupplierBatchStatusResult"] = new
                        {
                            type = "object", required = new[] { "affectedCount", "status" },
                            properties = new Dictionary<string, object> { ["affectedCount"] = new { type = "integer", format = "int32" }, ["status"] = StringProperty("Updated status.") }
                        },
                        ["ApiSupplierProductOptionDto"] = new
                        {
                            type = "object", required = new[] { "id", "productCode", "nameCN", "nameEN" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" }, ["productCode"] = StringProperty("Product code."),
                                ["nameCN"] = StringProperty("Chinese product name."), ["nameEN"] = StringProperty("English product name.")
                            }
                        },
                        ["ApiSupplierProductLinkDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "supplierCompanyId", "productId", "productCode", "productNameCN", "productNameEN", "supplierProductCode", "referencePrice", "currency", "leadTimeDays", "status" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" }, ["supplierCompanyId"] = new { type = "integer", format = "int32" },
                                ["productId"] = new { type = "integer", format = "int32" }, ["productCode"] = StringProperty("Internal product code."),
                                ["productNameCN"] = StringProperty("Chinese product name."), ["productNameEN"] = StringProperty("English product name."),
                                ["supplierProductCode"] = StringProperty("Supplier product code."), ["referencePrice"] = new { type = "number", format = "decimal" },
                                ["currency"] = StringProperty("ISO currency code."), ["leadTimeDays"] = new { type = "integer", format = "int32" },
                                ["status"] = StringProperty("Supply status.")
                            }
                        },
                        ["ApiSupplierProductLinkSaveRequest"] = new
                        {
                            type = "object", required = new[] { "id", "supplierCompanyId", "productId", "supplierProductCode", "referencePrice", "currency", "leadTimeDays", "status" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" }, ["supplierCompanyId"] = new { type = "integer", format = "int32" },
                                ["productId"] = new { type = "integer", format = "int32" }, ["supplierProductCode"] = StringProperty("Supplier product code."),
                                ["referencePrice"] = new { type = "number", format = "decimal" }, ["currency"] = StringProperty("ISO currency code."),
                                ["leadTimeDays"] = new { type = "integer", format = "int32" }, ["status"] = StringProperty("Supply status.")
                            }
                        },
                        ["ApiSupplierAssessmentDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "supplierCompanyId", "assessedAt", "assessmentKind", "qualityScore", "deliveryScore", "serviceScore", "priceScore", "averageScore", "conclusion", "notes", "assessedBy", "createdAt", "updatedAt" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" }, ["supplierCompanyId"] = new { type = "integer", format = "int32" },
                                ["assessedAt"] = new { type = "string", format = "date-time" }, ["assessmentKind"] = StringProperty("Assessment kind."),
                                ["qualityScore"] = new { type = "integer", format = "int32" }, ["deliveryScore"] = new { type = "integer", format = "int32" },
                                ["serviceScore"] = new { type = "integer", format = "int32" }, ["priceScore"] = new { type = "integer", format = "int32" },
                                ["averageScore"] = new { type = "number", format = "decimal" }, ["conclusion"] = StringProperty("Assessment conclusion."),
                                ["notes"] = StringProperty("Assessment notes."), ["assessedBy"] = StringProperty("Account that saved the assessment."),
                                ["createdAt"] = new { type = "string", format = "date-time" }, ["updatedAt"] = new { type = "string", format = "date-time" }
                            }
                        },
                        ["ApiSupplierAssessmentSaveRequest"] = new
                        {
                            type = "object",
                            required = new[] { "id", "supplierCompanyId", "assessedAt", "assessmentKind", "qualityScore", "deliveryScore", "serviceScore", "priceScore", "conclusion", "notes" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" }, ["supplierCompanyId"] = new { type = "integer", format = "int32" },
                                ["assessedAt"] = new { type = "string", format = "date-time" }, ["assessmentKind"] = StringProperty("Assessment kind."),
                                ["qualityScore"] = new { type = "integer", format = "int32" }, ["deliveryScore"] = new { type = "integer", format = "int32" },
                                ["serviceScore"] = new { type = "integer", format = "int32" }, ["priceScore"] = new { type = "integer", format = "int32" },
                                ["conclusion"] = StringProperty("Assessment conclusion."), ["notes"] = StringProperty("Assessment notes.")
                            }
                        },
                        ["ApiSupplierAssessmentOverviewItemDto"] = new
                        {
                            type = "object",
                            required = new[] { "supplierCompanyId", "supplierName", "supplierStatus", "category", "assessmentCount", "latestAssessedAt", "latestAssessmentKind", "qualityScore", "deliveryScore", "serviceScore", "priceScore", "averageScore", "conclusion", "notes" },
                            properties = new Dictionary<string, object>
                            {
                                ["supplierCompanyId"] = new { type = "integer", format = "int32" }, ["supplierName"] = StringProperty("Supplier name."),
                                ["supplierStatus"] = StringProperty("Supplier status."), ["category"] = StringProperty("Supplier category."),
                                ["assessmentCount"] = new { type = "integer", format = "int32" }, ["latestAssessedAt"] = new { type = "string", format = "date-time" },
                                ["latestAssessmentKind"] = StringProperty("Latest assessment kind."), ["qualityScore"] = new { type = "integer", format = "int32" },
                                ["deliveryScore"] = new { type = "integer", format = "int32" }, ["serviceScore"] = new { type = "integer", format = "int32" },
                                ["priceScore"] = new { type = "integer", format = "int32" }, ["averageScore"] = new { type = "number", format = "decimal" },
                                ["conclusion"] = StringProperty("Latest assessment conclusion."), ["notes"] = StringProperty("Latest assessment notes.")
                            }
                        },
                        ["ApiSupplierAssessmentOverviewDto"] = new
                        {
                            type = "object",
                            required = new[] { "totalSuppliers", "assessedSuppliers", "unassessedSuppliers", "preferredCount", "qualifiedCount", "watchCount", "pausedCount", "averageQualityScore", "averageDeliveryScore", "averageServiceScore", "averagePriceScore", "items" },
                            properties = new Dictionary<string, object>
                            {
                                ["totalSuppliers"] = new { type = "integer", format = "int32" }, ["assessedSuppliers"] = new { type = "integer", format = "int32" },
                                ["unassessedSuppliers"] = new { type = "integer", format = "int32" }, ["preferredCount"] = new { type = "integer", format = "int32" },
                                ["qualifiedCount"] = new { type = "integer", format = "int32" }, ["watchCount"] = new { type = "integer", format = "int32" },
                                ["pausedCount"] = new { type = "integer", format = "int32" }, ["averageQualityScore"] = new { type = "number", format = "decimal" },
                                ["averageDeliveryScore"] = new { type = "number", format = "decimal" }, ["averageServiceScore"] = new { type = "number", format = "decimal" },
                                ["averagePriceScore"] = new { type = "number", format = "decimal" },
                                ["items"] = new { type = "array", items = RefSchema("ApiSupplierAssessmentOverviewItemDto") }
                            }
                        },
            };
    }
}