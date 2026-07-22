namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateMasterDataSchemas() =>
            new Dictionary<string, object>
            {
                        ["ApiSupportPackageRequest"] = new
                        {
                            type = "object",
                            required = new[] { "includeLatestDatabaseBackup", "includeSampleFiles", "confirmationText" },
                            properties = new Dictionary<string, object>
                            {
                                ["includeLatestDatabaseBackup"] = new { type = "boolean" },
                                ["includeSampleFiles"] = new { type = "boolean" },
                                ["confirmationText"] = StringProperty("Must be INCLUDE OPTIONAL FILES when optional database backup or sample files are included.")
                            }
                        },
                        ["ApiSupportPackageResponse"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "success",
                                "message",
                                "fileName",
                                "fullPath",
                                "sizeBytes",
                                "supportPackageRoot",
                                "storagePolicy"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["message"] = StringProperty("Support package creation result message."),
                                ["fileName"] = StringProperty("Support package file name under the runtime support package root."),
                                ["fullPath"] = StringProperty("Full local support package path for desktop open-path actions."),
                                ["sizeBytes"] = new { type = "integer", format = "int64" },
                                ["supportPackageRoot"] = StringProperty("Runtime data root SupportPackages directory."),
                                ["storagePolicy"] = StringProperty("Path and redaction policy for diagnostic support packages.")
                            }
                        },
                        ["ApiCustomOptionListResponse"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "optionType",
                                "predefinedOptions",
                                "customOptions",
                                "options",
                                "allowCustomValues",
                                "storagePolicy"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["optionType"] = StringProperty("Canonical legacy EditableComboBox option type."),
                                ["predefinedOptions"] = StringArrayProperty("Built-in option values from application constants."),
                                ["customOptions"] = StringArrayProperty("User-saved option values stored in the runtime data root database."),
                                ["options"] = StringArrayProperty("Merged predefined and custom option values."),
                                ["allowCustomValues"] = new { type = "boolean", description = "Whether this option type accepts user-saved custom values." },
                                ["storagePolicy"] = StringProperty("Path and storage policy for audit/review.")
                            }
                        },
                        ["ApiCustomOptionSaveRequest"] = new
                        {
                            type = "object",
                            required = new[] { "value" },
                            properties = new Dictionary<string, object>
                            {
                                ["value"] = StringProperty("Option value to save for the requested option type.")
                            }
                        },
                        ["ApiExcelImportPreviewRequest"] = new
                        {
                            type = "object",
                            required = new[] { "filePath" },
                            properties = new Dictionary<string, object>
                            {
                                ["filePath"] = StringProperty("User-selected Excel source path. The sidecar reads only this explicit path.")
                            }
                        },
                        ["ApiExcelImportPreviewResponse"] = new
                        {
                            type = "object",
                            required = new[] { "sourcePath", "success", "errors", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["sourcePath"] = StringProperty("Normalized source path that was read."),
                                ["success"] = new { type = "boolean" },
                                ["invoice"] = RefSchema("ApiInvoiceDetailDto"),
                                ["customer"] = RefSchema("ApiImportedCustomerDto"),
                                ["exporter"] = RefSchema("ApiImportedExporterDto"),
                                ["analysisReport"] = RefSchema("ApiExcelImportAnalysisReportDto"),
                                ["errors"] = StringArrayProperty("Excel import warnings and parsing errors."),
                                ["storagePolicy"] = StringProperty("Path and storage policy for audit/review.")
                            }
                        },
                        ["ApiExcelImportSheetAnalysisDto"] = new
                        {
                            type = "object",
                            required = new[] { "name", "usedRowCount", "usedColumnCount", "fieldCandidateCount", "hasItemTable", "confidence" },
                            properties = new Dictionary<string, object>
                            {
                                ["name"] = StringProperty("Worksheet name."),
                                ["usedRowCount"] = new { type = "integer", format = "int32" },
                                ["usedColumnCount"] = new { type = "integer", format = "int32" },
                                ["fieldCandidateCount"] = new { type = "integer", format = "int32" },
                                ["hasItemTable"] = new { type = "boolean" },
                                ["confidence"] = new { type = "number", format = "decimal" }
                            }
                        },
                        ["ApiExcelImportFieldAnalysisDto"] = new
                        {
                            type = "object",
                            required = new[] { "fieldKey", "displayName", "value", "worksheetName", "row", "column", "confidence", "source" },
                            properties = new Dictionary<string, object>
                            {
                                ["fieldKey"] = StringProperty("Canonical invoice import field key."),
                                ["displayName"] = StringProperty("Human-readable field label."),
                                ["value"] = StringProperty("Detected field value."),
                                ["worksheetName"] = StringProperty("Worksheet where the field was detected."),
                                ["row"] = new { type = "integer", format = "int32" },
                                ["column"] = new { type = "integer", format = "int32" },
                                ["confidence"] = new { type = "number", format = "decimal" },
                                ["source"] = StringProperty("Detection source or strategy.")
                            }
                        },
                        ["ApiExcelImportItemTableAnalysisDto"] = new
                        {
                            type = "object",
                            required = new[] { "worksheetName", "headerRow", "headerDepth", "dataStartRow", "confidence", "columns" },
                            properties = new Dictionary<string, object>
                            {
                                ["worksheetName"] = StringProperty("Worksheet containing the item table."),
                                ["headerRow"] = new { type = "integer", format = "int32" },
                                ["headerDepth"] = new { type = "integer", format = "int32" },
                                ["dataStartRow"] = new { type = "integer", format = "int32" },
                                ["confidence"] = new { type = "number", format = "decimal" },
                                ["columns"] = RefSchema("ApiExcelImportItemColumnAnalysisDto")
                            }
                        },
                        ["ApiExcelImportItemColumnAnalysisDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "poNumberCol", "styleNoCol", "styleNameCol", "fabricCompositionCol", "styleNameCNCol",
                                "brandCol", "hsCodeCol", "originCol", "quantityCol", "unitENCol", "unitCNCol",
                                "cartonsCol", "ctnUnitENCol", "lengthCol", "widthCol", "heightCol", "dimensionCol",
                                "volumeCol", "gwPerCtnCol", "gwTotalCol", "nwPerCtnCol", "nwTotalCol", "unitPriceCol", "totalPriceCol"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["poNumberCol"] = new { type = "integer", format = "int32" },
                                ["styleNoCol"] = new { type = "integer", format = "int32" },
                                ["styleNameCol"] = new { type = "integer", format = "int32" },
                                ["fabricCompositionCol"] = new { type = "integer", format = "int32" },
                                ["styleNameCNCol"] = new { type = "integer", format = "int32" },
                                ["brandCol"] = new { type = "integer", format = "int32" },
                                ["hsCodeCol"] = new { type = "integer", format = "int32" },
                                ["originCol"] = new { type = "integer", format = "int32" },
                                ["quantityCol"] = new { type = "integer", format = "int32" },
                                ["unitENCol"] = new { type = "integer", format = "int32" },
                                ["unitCNCol"] = new { type = "integer", format = "int32" },
                                ["cartonsCol"] = new { type = "integer", format = "int32" },
                                ["ctnUnitENCol"] = new { type = "integer", format = "int32" },
                                ["lengthCol"] = new { type = "integer", format = "int32" },
                                ["widthCol"] = new { type = "integer", format = "int32" },
                                ["heightCol"] = new { type = "integer", format = "int32" },
                                ["dimensionCol"] = new { type = "integer", format = "int32" },
                                ["volumeCol"] = new { type = "integer", format = "int32" },
                                ["gwPerCtnCol"] = new { type = "integer", format = "int32" },
                                ["gwTotalCol"] = new { type = "integer", format = "int32" },
                                ["nwPerCtnCol"] = new { type = "integer", format = "int32" },
                                ["nwTotalCol"] = new { type = "integer", format = "int32" },
                                ["unitPriceCol"] = new { type = "integer", format = "int32" },
                                ["totalPriceCol"] = new { type = "integer", format = "int32" }
                            }
                        },
                        ["ApiExcelImportAnalysisIssueDto"] = new
                        {
                            type = "object",
                            required = new[] { "severity", "code", "message", "fieldKey" },
                            properties = new Dictionary<string, object>
                            {
                                ["severity"] = StringProperty("Issue severity."),
                                ["code"] = StringProperty("Machine-readable issue code."),
                                ["message"] = StringProperty("Human-readable issue message."),
                                ["fieldKey"] = StringProperty("Related field key, if any.")
                            }
                        },
                        ["ApiImportedCustomerDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "id",
                                "customerNameEN",
                                "displayName",
                                "notifyPartyName",
                                "addressEN",
                                "notifyPartyAddress",
                                "contactPerson",
                                "phone",
                                "email",
                                "taxId",
                                "notes"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["customerNameEN"] = StringProperty("Customer English name."),
                                ["displayName"] = StringProperty("Customer display name."),
                                ["notifyPartyName"] = StringProperty("Notify party name."),
                                ["addressEN"] = StringProperty("Customer English address."),
                                ["notifyPartyAddress"] = StringProperty("Notify party address."),
                                ["contactPerson"] = StringProperty("Contact person."),
                                ["phone"] = StringProperty("Phone."),
                                ["email"] = StringProperty("Email."),
                                ["taxId"] = StringProperty("Tax id."),
                                ["notes"] = StringProperty("Notes.")
                            }
                        },
                        ["ApiImportedExporterDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "id",
                                "exporterNameEN",
                                "exporterNameCN",
                                "addressEN",
                                "addressCN",
                                "contactPerson",
                                "creditCode",
                                "customsCode",
                                "phone",
                                "bankName",
                                "bankAccount",
                                "swiftCode",
                                "notes",
                                "docSealPath",
                                "customsSealPath"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["exporterNameEN"] = StringProperty("Exporter English name."),
                                ["exporterNameCN"] = StringProperty("Exporter Chinese name."),
                                ["addressEN"] = StringProperty("Exporter English address."),
                                ["addressCN"] = StringProperty("Exporter Chinese address."),
                                ["contactPerson"] = StringProperty("Contact person."),
                                ["creditCode"] = StringProperty("Unified social credit code."),
                                ["customsCode"] = StringProperty("Customs code."),
                                ["phone"] = StringProperty("Phone."),
                                ["bankName"] = StringProperty("Bank name."),
                                ["bankAccount"] = StringProperty("Bank account."),
                                ["swiftCode"] = StringProperty("SWIFT code."),
                                ["notes"] = StringProperty("Notes."),
                                ["docSealPath"] = StringProperty("Document seal path."),
                                ["customsSealPath"] = StringProperty("Customs seal path.")
                            }
                        },
                        ["ApiAuditLogPathExportRequest"] = new
                        {
                            type = "object",
                            required = new[] { "destinationPath" },
                            properties = MergeProperties(
                                AuditLogFilterProperties(),
                                new Dictionary<string, object>
                                {
                                    ["destinationPath"] = StringProperty("User-selected .xlsx output path. The sidecar does not choose a default export directory.")
                                })
                        },
                        ["ApiCustomerDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "customerNameEN", "displayName" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["customerNameEN"] = StringProperty("Customer English name."),
                                ["displayName"] = StringProperty("Display name used in selection lists."),
                                ["notifyPartyName"] = StringProperty("Notify party name."),
                                ["addressEN"] = StringProperty("Customer English address."),
                                ["notifyPartyAddress"] = StringProperty("Notify party address."),
                                ["contactPerson"] = StringProperty("Contact person."),
                                ["phone"] = StringProperty("Phone."),
                                ["email"] = StringProperty("Email."),
                                ["taxId"] = StringProperty("Tax id."),
                                ["notes"] = StringProperty("Notes."),
                                ["rowVersion"] = StringProperty("Concurrency row version encoded as base64.")
                            }
                        },
                        ["ApiExporterDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "exporterNameEN", "exporterNameCN" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["exporterNameEN"] = StringProperty("Exporter English name."),
                                ["exporterNameCN"] = StringProperty("Exporter Chinese name."),
                                ["addressEN"] = StringProperty("Exporter English address."),
                                ["addressCN"] = StringProperty("Exporter Chinese address."),
                                ["contactPerson"] = StringProperty("Contact person."),
                                ["creditCode"] = StringProperty("Credit code."),
                                ["customsCode"] = StringProperty("Customs code."),
                                ["phone"] = StringProperty("Phone."),
                                ["bankName"] = StringProperty("Bank name."),
                                ["bankAccount"] = StringProperty("Bank account."),
                                ["swiftCode"] = StringProperty("SWIFT code."),
                                ["notes"] = StringProperty("Notes."),
                                ["docSealPath"] = StringProperty("Document seal path."),
                                ["customsSealPath"] = StringProperty("Customs seal path."),
                                ["rowVersion"] = StringProperty("Concurrency row version encoded as base64.")
                            }
                        },
                        ["ApiProductDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "productCode", "nameEN", "nameCN" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["productCode"] = StringProperty("Product code."),
                                ["nameEN"] = StringProperty("English product name."),
                                ["nameCN"] = StringProperty("Chinese product name."),
                                ["description"] = StringProperty("Description."),
                                ["hsCode"] = StringProperty("HS code."),
                                ["elements"] = StringProperty("Declaration elements."),
                                ["supervisionConditions"] = StringProperty("Supervision conditions."),
                                ["inspectionCategory"] = StringProperty("Inspection category."),
                                ["taxRebateRate"] = DecimalProperty("Tax rebate rate."),
                                ["material"] = StringProperty("Material."),
                                ["brand"] = StringProperty("Brand."),
                                ["origin"] = StringProperty("Origin."),
                                ["unitEN"] = StringProperty("English unit."),
                                ["unitCN"] = StringProperty("Chinese unit."),
                                ["length"] = DecimalProperty("Carton length."),
                                ["width"] = DecimalProperty("Carton width."),
                                ["height"] = DecimalProperty("Carton height."),
                                ["gwPerCtn"] = DecimalProperty("Gross weight per carton."),
                                ["nwPerCtn"] = DecimalProperty("Net weight per carton."),
                                ["pcsPerCtn"] = DecimalProperty("Pieces per carton."),
                                ["packageUnitEN"] = StringProperty("English package unit."),
                                ["packageUnitCN"] = StringProperty("Chinese package unit."),
                                ["defaultPrice"] = DecimalProperty("Default unit price."),
                                ["createdAt"] = new { type = "string", format = "date-time" },
                                ["updatedAt"] = new { type = "string", format = "date-time" },
                                ["rowVersion"] = StringProperty("Concurrency row version encoded as base64.")
                            }
                        },
                        ["ApiPagedResponseOfApiProductDto"] = new
                        {
                            type = "object",
                            required = new[] { "items", "totalCount", "pageNumber", "pageSize", "totalPages", "hasPreviousPage", "hasNextPage" },
                            properties = new Dictionary<string, object>
                            {
                                ["items"] = new { type = "array", items = RefSchema("ApiProductDto") },
                                ["totalCount"] = new { type = "integer", format = "int32" },
                                ["pageNumber"] = new { type = "integer", format = "int32" },
                                ["pageSize"] = new { type = "integer", format = "int32" },
                                ["totalPages"] = new { type = "integer", format = "int32" },
                                ["hasPreviousPage"] = new { type = "boolean" },
                                ["hasNextPage"] = new { type = "boolean" }
                            }
                        },
                        ["ApiPortDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "nameEN", "nameCN", "country", "code" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["nameEN"] = StringProperty("English port name."),
                                ["nameCN"] = StringProperty("Chinese port name."),
                                ["country"] = StringProperty("Country."),
                                ["code"] = StringProperty("Port code."),
                                ["rowVersion"] = StringProperty("Concurrency row version encoded as base64.")
                            }
                        },
                        ["ApiUnitDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "nameEN", "nameCN", "code" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["nameEN"] = StringProperty("English unit name."),
                                ["nameCN"] = StringProperty("Chinese unit name."),
                                ["code"] = StringProperty("Unit code."),
                                ["rowVersion"] = StringProperty("Concurrency row version encoded as base64.")
                            }
                        },
            };
    }
}
