namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateCommonSchemas() =>
            new Dictionary<string, object>
            {
                        ["ApiLogoutResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" }
                            }
                        },
                        ["ApiErrorResponse"] = new
                        {
                            type = "object",
                            required = new[] { "message" },
                            properties = new Dictionary<string, object>
                            {
                                ["message"] = StringProperty("Error message.")
                            }
                        },
                        ["ApiCommandResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "message" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["message"] = StringProperty("Command result message.")
                            }
                        },
                        ["ApiDashboardResponse"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "monthlyExportAmount",
                                "monthlyProfit",
                                "monthlyTaxRefund",
                                "pendingCount",
                                "shippedCount",
                                "totalActiveCount",
                                "singleWindowStatusSummary",
                                "recentInvoices",
                                "todoItems",
                                "storagePolicy"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["monthlyExportAmount"] = DecimalProperty("Current month export amount from visible non-cancelled invoices."),
                                ["monthlyProfit"] = DecimalProperty("Current month estimated profit from visible non-cancelled invoices."),
                                ["monthlyTaxRefund"] = DecimalProperty("Current month tax refund from visible non-cancelled invoices."),
                                ["pendingCount"] = new { type = "integer", format = "int32" },
                                ["shippedCount"] = new { type = "integer", format = "int32" },
                                ["totalActiveCount"] = new { type = "integer", format = "int32" },
                                ["singleWindowStatusSummary"] = StringProperty("Single Window batch status summary."),
                                ["recentInvoices"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiDashboardRecentInvoiceDto")
                                },
                                ["todoItems"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiDashboardTodoItemDto")
                                },
                                ["storagePolicy"] = StringProperty("Path, storage, and data-domain policy for review. Dashboard does not read payment/reimbursement data.")
                            }
                        },
                        ["ApiDashboardTodoItemDto"] = new
                        {
                            type = "object",
                            required = new[] { "title", "description", "actionType", "referenceId" },
                            properties = new Dictionary<string, object>
                            {
                                ["title"] = StringProperty("Todo title."),
                                ["description"] = StringProperty("Todo description."),
                                ["actionType"] = StringProperty("Todo action type."),
                                ["referenceId"] = StringProperty("Todo reference id.")
                            }
                        },
                        ["ApiPackedCargoItemDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "x",
                                "y",
                                "width",
                                "height",
                                "baseHeight",
                                "occupiedHeight",
                                "topHeight",
                                "colorArgb",
                                "unitsRepresented",
                                "stackCount",
                                "loadCount",
                                "displayText",
                                "detailText",
                                "isRotated",
                                "isPalletized",
                                "name",
                                "totalWeight",
                                "priorityGroup",
                                "preferredZone"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["x"] = new { type = "number", format = "float" },
                                ["y"] = new { type = "number", format = "float" },
                                ["width"] = new { type = "number", format = "float" },
                                ["height"] = new { type = "number", format = "float" },
                                ["baseHeight"] = new { type = "number", format = "float" },
                                ["occupiedHeight"] = new { type = "number", format = "float" },
                                ["topHeight"] = new { type = "number", format = "float" },
                                ["colorArgb"] = new { type = "integer", format = "int32" },
                                ["unitsRepresented"] = new { type = "integer", format = "int32" },
                                ["stackCount"] = new { type = "integer", format = "int32" },
                                ["loadCount"] = new { type = "integer", format = "int32" },
                                ["displayText"] = StringProperty("Compact load label."),
                                ["detailText"] = StringProperty("Additional load detail."),
                                ["isRotated"] = new { type = "boolean" },
                                ["isPalletized"] = new { type = "boolean" },
                                ["name"] = StringProperty("Cargo display name."),
                                ["totalWeight"] = DecimalProperty("Packed item total weight in kilograms."),
                                ["priorityGroup"] = StringProperty("Priority group."),
                                ["preferredZone"] = StringProperty("Container zone where the item was placed.")
                            }
                        },
                        ["ApiExcelOutputRequest"] = new
                        {
                            type = "object",
                            required = new[] { "destinationPath" },
                            properties = new Dictionary<string, object>
                            {
                                ["destinationPath"] = StringProperty("User-selected .xlsx output path. The sidecar does not choose a default system-drive path.")
                            }
                        },
                        ["ApiExcelConvertBookingSheetRequest"] = new
                        {
                            type = "object",
                            required = new[] { "sourcePath", "destinationPath" },
                            properties = new Dictionary<string, object>
                            {
                                ["sourcePath"] = StringProperty("User-selected filled Excel import template path."),
                                ["destinationPath"] = StringProperty("User-selected .xlsx booking sheet output path.")
                            }
                        },
                        ["ApiShippingMarkImageSaveRequest"] = new
                        {
                            type = "object",
                            required = new[] { "imageDataUrl" },
                            properties = new Dictionary<string, object>
                            {
                                ["imageDataUrl"] = StringProperty("PNG data URL generated by the visual shipping mark editor.")
                            }
                        },
                        ["ApiShippingMarkImagePreviewRequest"] = new
                        {
                            type = "object",
                            required = new[] { "imagePath" },
                            properties = new Dictionary<string, object>
                            {
                                ["imagePath"] = StringProperty("Previously saved shipping mark image path under the runtime data root Marks directory.")
                            }
                        },
                        ["ApiShippingMarkImageSaveResponse"] = new
                        {
                            type = "object",
                            required = new[] { "imagePath", "fileName", "contentType", "sizeBytes", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["imagePath"] = StringProperty("Saved image path under runtime data root Marks."),
                                ["fileName"] = StringProperty("Saved image file name."),
                                ["contentType"] = StringProperty("Image MIME type."),
                                ["sizeBytes"] = new { type = "integer", format = "int64" },
                                ["storagePolicy"] = StringProperty("Runtime data root storage and invoice/payment data-domain policy.")
                            }
                        },
                        ["ApiShippingMarkImagePreviewResponse"] = new
                        {
                            type = "object",
                            required = new[] { "imagePath", "fileName", "contentType", "sizeBytes", "dataUrl", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["imagePath"] = StringProperty("Normalized image path under runtime data root Marks."),
                                ["fileName"] = StringProperty("Image file name."),
                                ["contentType"] = StringProperty("Image MIME type."),
                                ["sizeBytes"] = new { type = "integer", format = "int64" },
                                ["dataUrl"] = StringProperty("Image data URL for browser preview."),
                                ["storagePolicy"] = StringProperty("Runtime data root storage and invoice/payment data-domain policy.")
                            }
                        },
                        ["ApiPayeeDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "category", "name" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["category"] = StringProperty("Payee category."),
                                ["name"] = StringProperty("Payee name."),
                                ["bankName"] = StringProperty("Bank name."),
                                ["rmbAccount"] = StringProperty("RMB account."),
                                ["usdAccount"] = StringProperty("USD account."),
                                ["contactPerson"] = StringProperty("Contact person."),
                                ["phone"] = StringProperty("Phone."),
                                ["notes"] = StringProperty("Notes."),
                                ["rowVersion"] = StringProperty("Concurrency row version encoded as base64.")
                            }
                        },
            };
    }
}
