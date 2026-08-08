using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateInvoicePaymentDocumentSchemas() =>
            new Dictionary<string, object>
            {
                        ["ApiInvoiceListItemDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "id",
                                "invoiceNo",
                                "contractNo",
                                "invoiceDate",
                                "customerName",
                                "exporterName",
                                "destinationCountry",
                                "portOfLoading",
                                "portOfDestination",
                                "currency",
                                "totalAmount",
                                "type",
                                "status"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["invoiceNo"] = StringProperty("Invoice number."),
                                ["contractNo"] = StringProperty("Contract number."),
                                ["invoiceDate"] = new { type = "string", format = "date-time" },
                                ["customerName"] = StringProperty("Customer English name snapshot."),
                                ["exporterName"] = StringProperty("Exporter name snapshot."),
                                ["destinationCountry"] = StringProperty("Destination country."),
                                ["portOfLoading"] = StringProperty("Port of loading."),
                                ["portOfDestination"] = StringProperty("Port of destination."),
                                ["currency"] = StringProperty("Invoice currency."),
                                ["totalAmount"] = new { type = "number", format = "decimal" },
                                ["type"] = StringProperty("Invoice type."),
                                ["status"] = StringProperty("Invoice status.")
                            }
                        },
                        ["ApiPagedResponseOfApiInvoiceListItemDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "items",
                                "totalCount",
                                "pageNumber",
                                "pageSize",
                                "totalPages",
                                "hasPreviousPage",
                                "hasNextPage"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["items"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiInvoiceListItemDto")
                                },
                                ["totalCount"] = new { type = "integer", format = "int32" },
                                ["pageNumber"] = new { type = "integer", format = "int32" },
                                ["pageSize"] = new { type = "integer", format = "int32" },
                                ["totalPages"] = new { type = "integer", format = "int32" },
                                ["hasPreviousPage"] = new { type = "boolean" },
                                ["hasNextPage"] = new { type = "boolean" }
                            }
                        },
                        ["ApiQueryInvoiceRowDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "id",
                                "invoiceNo",
                                "invoiceDate",
                                "contractNo",
                                "customerName",
                                "exporterName",
                                "destinationCountry",
                                "tradeTerms",
                                "shipmentDate",
                                "transportMode",
                                "totalCartons",
                                "totalQuantity",
                                "totalAmount",
                                "currency",
                                "type"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["invoiceNo"] = StringProperty("Invoice number."),
                                ["invoiceDate"] = StringProperty("Invoice date formatted as yyyy-MM-dd for the legacy query grid."),
                                ["contractNo"] = StringProperty("Contract number."),
                                ["customerName"] = StringProperty("Customer English name snapshot."),
                                ["exporterName"] = StringProperty("Exporter name snapshot."),
                                ["destinationCountry"] = StringProperty("Destination country."),
                                ["tradeTerms"] = StringProperty("Trade terms."),
                                ["shipmentDate"] = StringProperty("Shipment date formatted as yyyy-MM-dd for the legacy query grid."),
                                ["transportMode"] = StringProperty("Transport mode."),
                                ["totalCartons"] = DecimalProperty("Total cartons."),
                                ["totalQuantity"] = DecimalProperty("Total quantity."),
                                ["totalAmount"] = DecimalProperty("Total amount."),
                                ["currency"] = StringProperty("Invoice currency."),
                                ["type"] = StringProperty("Invoice type.")
                            }
                        },
                        ["ApiPagedResponseOfApiQueryInvoiceRowDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "items",
                                "totalCount",
                                "pageNumber",
                                "pageSize",
                                "totalPages",
                                "hasPreviousPage",
                                "hasNextPage"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["items"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiQueryInvoiceRowDto")
                                },
                                ["totalCount"] = new { type = "integer", format = "int32" },
                                ["pageNumber"] = new { type = "integer", format = "int32" },
                                ["pageSize"] = new { type = "integer", format = "int32" },
                                ["totalPages"] = new { type = "integer", format = "int32" },
                                ["hasPreviousPage"] = new { type = "boolean" },
                                ["hasNextPage"] = new { type = "boolean" }
                            }
                        },
                        ["ApiQueryInvoiceFilterRequest"] = new
                        {
                            type = "object",
                            properties = QueryInvoiceFilterProperties()
                        },
                        ["ApiQueryInvoiceExportRequest"] = new
                        {
                            type = "object",
                            required = new[] { "destinationPath" },
                            properties = MergeProperties(
                                QueryInvoiceFilterProperties(),
                                new Dictionary<string, object>
                                {
                                    ["destinationPath"] = StringProperty("User-selected .xlsx output path. The sidecar does not choose a default export directory.")
                                })
                        },
                        ["ApiInvoiceDetailDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "id",
                                "invoiceNo",
                                "contractNo",
                                "invoiceDate",
                                "shipmentDate",
                                "customerNameEN",
                                "exporterNameEN",
                                "currency",
                                "totalAmount",
                                "status",
                                "items"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["ownerUserId"] = new { type = "integer", format = "int32", nullable = true },
                                ["departmentId"] = StringProperty("Department scope."),
                                ["companyScope"] = StringProperty("Company scope."),
                                ["invoiceNo"] = StringProperty("Invoice number."),
                                ["contractNo"] = StringProperty("Contract number."),
                                ["invoiceDate"] = new { type = "string", format = "date-time" },
                                ["letterOfCreditNo"] = StringProperty("Letter of credit number."),
                                ["letterOfCreditSourcePath"] = StringProperty("Letter of credit source path."),
                                ["letterOfCreditContent"] = StringProperty("Letter of credit content."),
                                ["issuingBank"] = StringProperty("Issuing bank."),
                                ["customsBrokerName"] = StringProperty("Customs broker name."),
                                ["customsBrokerCode"] = StringProperty("Customs broker code."),
                                ["paymentTerms"] = StringProperty("Payment terms."),
                                ["portOfLoading"] = StringProperty("Port of loading."),
                                ["portOfDestination"] = StringProperty("Port of destination."),
                                ["destinationCountry"] = StringProperty("Destination country."),
                                ["shippingMarks"] = StringProperty("Shipping marks."),
                                ["shippingMarksType"] = StringProperty("Shipping marks type."),
                                ["shippingMarksImage"] = StringProperty("DataRoot-relative managed shipping marks image path under Marks/."),
                                ["tradeTerms"] = StringProperty("Trade terms."),
                                ["transportMode"] = StringProperty("Transport mode."),
                                ["shipmentDate"] = new { type = "string", format = "date-time", nullable = true },
                                ["exporterId"] = new { type = "integer", format = "int32" },
                                ["customerId"] = new { type = "integer", format = "int32" },
                                ["totalCartons"] = DecimalProperty("Total cartons."),
                                ["totalQuantity"] = DecimalProperty("Total quantity."),
                                ["totalGrossWeight"] = DecimalProperty("Total gross weight."),
                                ["totalNetWeight"] = DecimalProperty("Total net weight."),
                                ["totalVolume"] = DecimalProperty("Total volume."),
                                ["totalAmount"] = DecimalProperty("Total amount calculated from invoice line amounts."),
                                ["totalPurchaseAmount"] = DecimalProperty("Total purchase amount."),
                                ["totalTaxRefundAmount"] = DecimalProperty("Total tax refund amount."),
                                ["totalProfit"] = DecimalProperty("Total profit."),
                                ["currency"] = StringProperty("Invoice currency."),
                                ["specialTerms"] = StringProperty("Special terms."),
                                ["type"] = StringProperty("Invoice type."),
                                ["supervisionMode"] = StringProperty("Supervision mode."),
                                ["customerNameEN"] = StringProperty("Customer English name snapshot."),
                                ["customerAddressEN"] = StringProperty("Customer English address snapshot."),
                                ["notifyPartyName"] = StringProperty("Notify party name."),
                                ["notifyPartyAddress"] = StringProperty("Notify party address."),
                                ["exporterNameEN"] = StringProperty("Exporter English name snapshot."),
                                ["exporterNameCN"] = StringProperty("Exporter Chinese name snapshot."),
                                ["exporterAddressEN"] = StringProperty("Exporter English address snapshot."),
                                ["exporterAddressCN"] = StringProperty("Exporter Chinese address snapshot."),
                                ["exporterCreditCode"] = StringProperty("Exporter credit code."),
                                ["exporterCustomsCode"] = StringProperty("Exporter customs code."),
                                ["bankName"] = StringProperty("Bank name."),
                                ["bankAccount"] = StringProperty("Bank account."),
                                ["swiftCode"] = StringProperty("SWIFT code."),
                                ["exchangeRate"] = new { type = "number", format = "decimal", nullable = true },
                                ["status"] = StringProperty("Invoice status."),
                                ["rowVersion"] = StringProperty("Concurrency row version encoded as base64."),
                                ["spare1"] = StringProperty("Spare field 1."),
                                ["spare2"] = StringProperty("Spare field 2."),
                                ["spare3"] = StringProperty("Spare field 3."),
                                ["customFieldsJson"] = StringProperty("Custom fields JSON."),
                                 ["items"] = new
                                 {
                                     type = "array",
                                     items = RefSchema("ApiInvoiceItemDto")
                                 },
                                 ["pendingHsFeedback"] = new
                                 {
                                     type = "array",
                                     description = "HS suggestions selected in the editor; persisted together with the invoice transaction.",
                                     items = RefSchema("HsCodeKnowledgeFeedbackInput")
                                 }
                            }
                        },
                        ["ApiInvoiceItemDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "id",
                                "invoiceId",
                                "styleNo",
                                "styleName",
                                "quantity",
                                "priceCalculationMode",
                                "unitPrice",
                                "totalPrice"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["invoiceId"] = new { type = "integer", format = "int32" },
                                ["poNumber"] = StringProperty("PO number."),
                                ["styleNo"] = StringProperty("Style number."),
                                ["styleName"] = StringProperty("Style name."),
                                ["fabricComposition"] = StringProperty("Fabric composition."),
                                ["styleNameCN"] = StringProperty("Chinese style name."),
                                ["brand"] = StringProperty("Brand."),
                                ["hsCode"] = StringProperty("HS code."),
                                ["origin"] = StringProperty("Origin."),
                                ["quantity"] = DecimalProperty("Quantity."),
                                ["unitEN"] = StringProperty("English unit."),
                                ["unitCN"] = StringProperty("Chinese unit."),
                                ["pcsPerCtn"] = DecimalProperty("Pieces per carton."),
                                ["cartons"] = DecimalProperty("Cartons."),
                                ["ctnUnitEN"] = StringProperty("English carton unit."),
                                ["ctnUnitCN"] = StringProperty("Chinese carton unit."),
                                ["length"] = DecimalProperty("Carton length."),
                                ["width"] = DecimalProperty("Carton width."),
                                ["height"] = DecimalProperty("Carton height."),
                                ["volume"] = DecimalProperty("Volume."),
                                ["gwPerCtn"] = DecimalProperty("Gross weight per carton."),
                                ["nwPerCtn"] = DecimalProperty("Net weight per carton."),
                                ["gwTotal"] = DecimalProperty("Total gross weight."),
                                ["nwTotal"] = DecimalProperty("Total net weight."),
                                ["priceCalculationMode"] = new
                                {
                                    type = "string",
                                    description = "Authoritative price input for the row: UnitPriceDriven or LineAmountDriven.",
                                    @enum = new[] { ItemPriceCalculationModeCatalog.UnitPriceDriven, ItemPriceCalculationModeCatalog.LineAmountDriven }
                                },
                                ["unitPrice"] = DecimalProperty("Unit price, stored to at most five decimal places; ordinary values display with two decimals."),
                                ["totalPrice"] = DecimalProperty("Authoritative line amount rounded to two decimal places."),
                                ["purchasePrice"] = DecimalProperty("Purchase price."),
                                ["purchaseTotal"] = DecimalProperty("Purchase total."),
                                ["taxRebateRate"] = DecimalProperty("Tax rebate rate."),
                                ["taxRefundAmount"] = DecimalProperty("Calculated tax refund amount."),
                                ["spare1"] = StringProperty("Spare field 1."),
                                ["spare2"] = StringProperty("Spare field 2."),
                                ["spare3"] = StringProperty("Spare field 3."),
                                ["customFieldsJson"] = StringProperty("Custom fields JSON.")
                            }
                        },
                        ["ApiInvoiceSaveResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "id", "isUpdate", "invoice" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["id"] = new { type = "integer", format = "int32" },
                                ["isUpdate"] = new { type = "boolean" },
                                ["invoice"] = RefSchema("ApiInvoiceDetailDto")
                             }
                         },
                        ["ApiInvoicePurgeRequest"] = new
                        {
                            type = "object",
                            required = new[] { "invoiceNoConfirmation", "reason" },
                            properties = new Dictionary<string, object>
                            {
                                ["invoiceNoConfirmation"] = StringProperty("Exact invoice number entered again by the administrator."),
                                ["reason"] = StringProperty("Audited maintenance reason, up to 500 characters.")
                            }
                        },
                        ["ApiInvoiceDataMaintenancePreviewResponse"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "id", "invoiceNo", "type", "status", "statusDisplayName", "invoiceDate",
                                "customerName", "canPurge", "guidance", "storagePolicy"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["invoiceNo"] = StringProperty("Invoice number."),
                                ["type"] = StringProperty("Actual/customs invoice data type."),
                                ["status"] = StringProperty("Normalized invoice status."),
                                ["statusDisplayName"] = StringProperty("Localized invoice status label."),
                                ["invoiceDate"] = new { type = "string", format = "date-time" },
                                ["customerName"] = StringProperty("Customer snapshot name used to identify the record."),
                                ["canPurge"] = new { type = "boolean" },
                                ["guidance"] = StringProperty("Commercial retention and cleanup guidance for the current status."),
                                ["storagePolicy"] = StringProperty("Data and audit boundary for the maintenance operation.")
                            }
                        },
                        ["ApiInvoicePurgeResponse"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "success", "invoiceId", "invoiceNo", "previousStatus", "message", "storagePolicy"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["invoiceId"] = new { type = "integer", format = "int32" },
                                ["invoiceNo"] = StringProperty("Purged invoice number."),
                                ["previousStatus"] = StringProperty("Status immediately before purge; always Cancelled on success."),
                                ["message"] = StringProperty("Maintenance result message."),
                                ["storagePolicy"] = StringProperty("Data and audit boundary for the maintenance operation.")
                            }
                        },
                        ["ApiInvoiceStatusTransitionRequest"] = new
                        {
                            type = "object",
                            required = new[] { "targetStatus", "rowVersion" },
                            properties = new Dictionary<string, object>
                            {
                                ["targetStatus"] = StringProperty("Target status: Verified, Shipped, Completed, or Cancelled."),
                                ["rowVersion"] = StringProperty("Expected concurrency row version encoded as base64."),
                                ["note"] = StringProperty("Optional status change note; required when cancelling.")
                            }
                        },
                        ["ApiInvoiceUnverifyRequest"] = new
                        {
                            type = "object",
                            required = new[] { "rowVersion", "note" },
                            properties = new Dictionary<string, object>
                            {
                                ["rowVersion"] = StringProperty("Expected concurrency row version encoded as base64."),
                                ["note"] = StringProperty("Reason for returning the invoice to Draft.")
                            }
                        },
                        ["ApiInvoiceStatusHistoryDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "invoiceId", "fromStatus", "toStatus", "note", "changedAt" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["invoiceId"] = new { type = "integer", format = "int32" },
                                ["fromStatus"] = StringProperty("Display name of the previous status."),
                                ["toStatus"] = StringProperty("Display name of the new status."),
                                ["note"] = StringProperty("Status change note."),
                                ["changedByUserId"] = new { type = "integer", format = "int32", nullable = true },
                                ["changedByUsername"] = StringProperty("Operator username."),
                                ["changedAt"] = new { type = "string", format = "date-time" }
                            }
                        },
                        ["ApiInvoiceStatusHistoryDtoArray"] = new
                        {
                            type = "array",
                            items = RefSchema("ApiInvoiceStatusHistoryDto")
                        },
                        ["ApiInvoiceCloneRequest"] = new
                        {
                            type = "object",
                            required = new[] { "newInvoiceNo" },
                            properties = new Dictionary<string, object>
                            {
                                ["newInvoiceNo"] = StringProperty("New invoice number for the clone."),
                                ["options"] = new
                                {
                                    type = "object",
                                    nullable = true,
                                    properties = new Dictionary<string, object>
                                    {
                                        ["copyHeader"] = new { type = "boolean" },
                                        ["copyItems"] = new { type = "boolean" },
                                        ["resetDates"] = new { type = "boolean" },
                                        ["clearAmounts"] = new { type = "boolean" }
                                    }
                                }
                            }
                        },
                        ["ApiInvoiceCloneResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "id", "invoice", "message" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["id"] = new { type = "integer", format = "int32" },
                                ["invoice"] = RefSchema("ApiInvoiceDetailDto"),
                                ["message"] = StringProperty("Clone result message.")
                            }
                        },
                        ["ApiInvoiceCloneTypeRequest"] = new
                        {
                            type = "object",
                            required = new[] { "targetType" },
                            properties = new Dictionary<string, object>
                            {
                                ["targetType"] = StringProperty("Target trade data type for the clone. Supported values are 实际数据 and 报关数据."),
                                ["options"] = new
                                {
                                    type = "object",
                                    nullable = true,
                                    properties = new Dictionary<string, object>
                                    {
                                        ["copyHeader"] = new { type = "boolean" },
                                        ["copyItems"] = new { type = "boolean" },
                                        ["resetDates"] = new { type = "boolean" },
                                        ["clearAmounts"] = new { type = "boolean" }
                                    }
                                }
                            }
                        },
                        ["ApiInvoiceCloneTypeResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "id", "invoice", "message" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["id"] = new { type = "integer", format = "int32" },
                                ["invoice"] = RefSchema("ApiInvoiceDetailDto"),
                                ["message"] = StringProperty("Clone result message.")
                            }
                        },
                        ["ApiInvoiceTransferPathRequest"] = new
                        {
                            type = "object",
                            required = new[] { "packagePath" },
                            properties = new Dictionary<string, object>
                            {
                                ["packagePath"] = StringProperty("User-selected .edpkg package path.")
                            }
                        },
                        ["ApiInvoiceTransferImportRequest"] = new
                        {
                            type = "object",
                            required = new[] { "packagePath", "conflictAction" },
                            properties = new Dictionary<string, object>
                            {
                                ["packagePath"] = StringProperty("User-selected .edpkg package path."),
                                ["conflictAction"] = StringProperty("Conflict action: Skip, Overwrite, NewInvoiceNo, or AppendItems."),
                                ["newInvoiceNo"] = StringProperty("Optional invoice number used when conflictAction is NewInvoiceNo.")
                            }
                        },
                        ["ApiInvoiceTransferPreviewDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "invoiceNo",
                                "type",
                                "itemCount",
                                "customerExists",
                                "exporterExists",
                                "invoiceExists",
                                "invoiceMatches",
                                "existingInvoiceId"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["invoiceNo"] = StringProperty("Package invoice number."),
                                ["type"] = StringProperty("Package invoice type, such as 实际数据 or 报关数据."),
                                ["itemCount"] = new { type = "integer", format = "int32" },
                                ["customerExists"] = new { type = "boolean" },
                                ["exporterExists"] = new { type = "boolean" },
                                ["invoiceExists"] = new { type = "boolean" },
                                ["invoiceMatches"] = new { type = "boolean" },
                                ["existingInvoiceId"] = new { type = "integer", format = "int32" }
                            }
                        },
                        ["ApiInvoiceTransferPreviewResponse"] = new
                        {
                            type = "object",
                            required = new[] { "checksumValid", "checksumMessage", "preview", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["checksumValid"] = new { type = "boolean" },
                                ["checksumMessage"] = StringProperty("Checksum validation result."),
                                ["preview"] = RefSchema("ApiInvoiceTransferPreviewDto"),
                                ["storagePolicy"] = StringProperty("Runtime path and data-domain policy for invoice transfer packages.")
                            }
                        },
                        ["ApiInvoiceTransferExportResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "invoiceId", "packagePath", "storagePolicy", "message" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["invoiceId"] = new { type = "integer", format = "int32" },
                                ["packagePath"] = StringProperty("Normalized user-selected .edpkg output path."),
                                ["storagePolicy"] = StringProperty("Runtime path and data-domain policy for invoice transfer package export."),
                                ["message"] = StringProperty("Export result message.")
                            }
                        },
                        ["ApiInvoiceTransferImportResultDto"] = new
                        {
                            type = "object",
                            required = new[] { "success", "message", "finalInvoiceNo", "actionTaken" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["message"] = StringProperty("Import result message."),
                                ["invoiceId"] = new { type = "integer", format = "int32", nullable = true },
                                ["finalInvoiceNo"] = StringProperty("Final invoice number after import."),
                                ["actionTaken"] = StringProperty("Conflict action applied by the import.")
                            }
                        },
                        ["ApiInvoiceTransferImportResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "result", "preview", "storagePolicy", "message" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["result"] = RefSchema("ApiInvoiceTransferImportResultDto"),
                                ["preview"] = RefSchema("ApiInvoiceTransferPreviewDto"),
                                ["storagePolicy"] = StringProperty("Runtime path and data-domain policy for invoice transfer package import."),
                                ["message"] = StringProperty("Import response message.")
                            }
                        },
                        ["ApiInvoiceProfitAnalysisRequest"] = new
                        {
                            type = "object",
                            required = new[] { "invoice" },
                            properties = new Dictionary<string, object>
                            {
                                ["invoice"] = RefSchema("ApiInvoiceDetailDto")
                            }
                        },
                        ["ApiInvoiceProfitAnalysisResponse"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "currency",
                                "salesTotal",
                                "exchangeRate",
                                "salesRmb",
                                "purchaseCost",
                                "taxRefund",
                                "grossProfit",
                                "margin",
                                "salesTotalText",
                                "exchangeRateText",
                                "salesRmbText",
                                "purchaseCostText",
                                "taxRefundText",
                                "grossProfitText",
                                "marginText",
                                "storagePolicy"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["currency"] = StringProperty("Invoice currency from the request draft."),
                                ["salesTotal"] = DecimalProperty("Sales total in the invoice currency."),
                                ["exchangeRate"] = NullableDecimalProperty("Exchange rate used to convert sales to RMB. Null when unset and the invoice currency is not RMB/CNY."),
                                ["salesRmb"] = DecimalProperty("Sales total converted to RMB."),
                                ["purchaseCost"] = DecimalProperty("Purchase cost in RMB."),
                                ["taxRefund"] = DecimalProperty("Tax refund in RMB."),
                                ["grossProfit"] = DecimalProperty("Estimated gross profit in RMB."),
                                ["margin"] = DecimalProperty("Gross margin as a decimal ratio."),
                                ["salesTotalText"] = StringProperty("Legacy WinForms-compatible sales total text."),
                                ["exchangeRateText"] = StringProperty("Legacy WinForms-compatible exchange rate text."),
                                ["salesRmbText"] = StringProperty("Legacy WinForms-compatible RMB sales text."),
                                ["purchaseCostText"] = StringProperty("Legacy WinForms-compatible purchase cost text."),
                                ["taxRefundText"] = StringProperty("Legacy WinForms-compatible tax refund text."),
                                ["grossProfitText"] = StringProperty("Legacy WinForms-compatible gross profit text."),
                                ["marginText"] = StringProperty("Legacy WinForms-compatible margin text."),
                                ["storagePolicy"] = StringProperty("Path, storage, and data-domain policy for review.")
                            }
                        },
                        ["ApiPaymentDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "rowVersion" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["ownerUserId"] = new { type = "integer", format = "int32", nullable = true },
                                ["departmentId"] = StringProperty("Department scope."),
                                ["companyScope"] = StringProperty("Company scope."),
                                ["invoiceNo"] = StringProperty("Invoice number."),
                                ["shipmentDate"] = new { type = "string", format = "date-time" },
                                ["payeeId"] = new { type = "integer", format = "int32" },
                                ["department"] = StringProperty("Department."),
                                ["project"] = StringProperty("Project."),
                                ["usdAmount"] = DecimalProperty("USD amount."),
                                ["cnyAmount"] = DecimalProperty("CNY amount."),
                                ["paymentMethod"] = StringProperty("Payment method."),
                                ["payeeName"] = StringProperty("Payee name."),
                                ["payerName"] = StringProperty("Payer name."),
                                ["bankName"] = StringProperty("Bank name."),
                                ["accountNo"] = StringProperty("Account number."),
                                ["notes"] = StringProperty("Notes."),
                                ["paymentDate"] = new { type = "string", format = "date-time", nullable = true },
                                ["goodsName"] = StringProperty("Goods name."),
                                ["quantity"] = StringProperty("Quantity text."),
                                ["shipmentCountry"] = StringProperty("Shipment country."),
                                ["receiptDate"] = new { type = "string", format = "date-time", nullable = true },
                                ["travelExpense"] = DecimalProperty("Travel expense."),
                                ["businessEntertainmentExpense"] = DecimalProperty("Business entertainment expense."),
                                ["telephoneExpense"] = DecimalProperty("Telephone expense."),
                                ["officeExpense"] = DecimalProperty("Office expense."),
                                ["repairExpense"] = DecimalProperty("Repair expense."),
                                ["freightMiscExpense"] = DecimalProperty("Freight miscellaneous expense."),
                                ["inspectionExpense"] = DecimalProperty("Inspection expense."),
                                ["otherExpense"] = DecimalProperty("Other expense."),
                                ["rowVersion"] = StringProperty("Concurrency row version encoded as base64.")
                            }
                        },
                        ["ApiPaymentSaveResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "id", "payment" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["id"] = new { type = "integer", format = "int32" },
                                ["payment"] = RefSchema("ApiPaymentDto")
                            }
                        },
                        ["ApiPagedResponseOfApiPaymentDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "items",
                                "totalCount",
                                "pageNumber",
                                "pageSize",
                                "totalPages",
                                "hasPreviousPage",
                                "hasNextPage"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["items"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiPaymentDto")
                                },
                                ["totalCount"] = new { type = "integer", format = "int32" },
                                ["pageNumber"] = new { type = "integer", format = "int32" },
                                ["pageSize"] = new { type = "integer", format = "int32" },
                                ["totalPages"] = new { type = "integer", format = "int32" },
                                ["hasPreviousPage"] = new { type = "boolean" },
                                ["hasNextPage"] = new { type = "boolean" }
                            }
                        },
            };
    }
}
