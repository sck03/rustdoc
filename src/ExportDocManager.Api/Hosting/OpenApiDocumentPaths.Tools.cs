namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateToolsPaths() =>
            new Dictionary<string, object>
            {
                    ["/api/tools/pdf/merge/save-to-path"] = new
                    {
                        post = new
                        {
                            summary = "Start PDF merge job",
                            operationId = "startPdfMergeSaveToPathJob",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiPdfMergeRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["202"] = new
                                {
                                    description = "PDF merge background job was accepted.",
                                    content = JsonContent("BackgroundJobSnapshot")
                                },
                                ["400"] = new { description = "Invalid PDF merge request." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Trusted desktop token required." }
                            }
                        }
                    },
                    ["/api/tools/letter-of-credit/import"] = new
                    {
                        post = new
                        {
                            summary = "Import letter of credit document",
                            operationId = "importLetterOfCreditDocument",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiLetterOfCreditImportRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Extracted letter of credit text. The sidecar only reads the explicit source file path and does not create a default system-drive output.",
                                    content = JsonContent("ApiLetterOfCreditImportResponse")
                                },
                                ["400"] = new { description = "Invalid path or unsupported document type." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Source file was not found." },
                                ["409"] = new { description = "Text extraction failed or OCR runtime is not enabled." }
                            }
                        }
                    },
                    ["/api/tools/letter-of-credit/review"] = new
                    {
                        post = new
                        {
                            summary = "Review letter of credit compliance from an in-memory invoice draft",
                            operationId = "reviewLetterOfCreditCompliance",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiLetterOfCreditReviewRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "AI compliance review report. The sidecar uses only the current invoice draft and returns the report in memory without writing business storage.",
                                    content = JsonContent("ApiLetterOfCreditReviewResponse")
                                },
                                ["400"] = new { description = "Missing invoice draft or letter-of-credit review context." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["409"] = new { description = "AI service is not configured or the AI request failed." }
                            }
                        }
                    },
                    ["/api/tools/ocr/recognize-image"] = new
                    {
                        post = new
                        {
                            summary = "Recognize text from an image",
                            operationId = "recognizeOcrImage",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiOcrRecognizeImageRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "OCR result. The sidecar only reads the explicit source image path and returns text in memory.",
                                    content = JsonContent("ApiOcrRecognizeImageResponse")
                                },
                                ["400"] = new { description = "Invalid path or unsupported image type." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Source image was not found." },
                                ["409"] = new { description = "OCR runtime is not enabled or recognition failed." }
                            }
                        }
                    },
                    ["/api/tools/ocr/recognize-image-content"] = new
                    {
                        post = new
                        {
                            summary = "Recognize text from in-memory image content",
                            operationId = "recognizeOcrImageContent",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiOcrRecognizeImageContentRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "OCR result. Clipboard or other pathless image content is passed in memory and is not written to a temporary file.",
                                    content = JsonContent("ApiOcrRecognizeImageResponse")
                                },
                                ["400"] = new { description = "Invalid Base64 image content or unsupported MIME type." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["409"] = new { description = "OCR runtime is not enabled or recognition failed." }
                            }
                        }
                    },
                    ["/api/tools/exchange-rates"] = new
                    {
                        get = new
                        {
                            summary = "List current exchange rates",
                            operationId = "listExchangeRates",
                            parameters = new[]
                            {
                                QueryParameter("forceRefresh", "boolean", null, "When true, clears the in-memory exchange-rate cache before fetching.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Current exchange rates from the appsettings.json configured remote source. Results are returned in memory and are not written to business storage.",
                                    content = JsonContent("ApiExchangeRateListResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["409"] = new { description = "Exchange-rate source failed." }
                            }
                        }
                    },
                    ["/api/tools/exchange-rates/available-currencies"] = new
                    {
                        get = new
                        {
                            summary = "List currencies available from the exchange-rate source",
                            operationId = "listAvailableExchangeRateCurrencies",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Currencies reported by the appsettings.json configured exchange-rate source. The sidecar does not write these values automatically.",
                                    content = JsonContent("ApiExchangeRateAvailableCurrenciesResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["409"] = new { description = "Exchange-rate source failed." }
                            }
                        }
                    },
                    ["/api/tools/email/status"] = new
                    {
                        get = new
                        {
                            summary = "Get email tool SMTP status",
                            operationId = "getEmailToolStatus",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "SMTP status from appsettings.json. Sensitive values are not returned.",
                                    content = JsonContent("ApiEmailStatusResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/tools/email/server-suggestion"] = new
                    {
                        post = new
                        {
                            summary = "Suggest SMTP settings from an email address",
                            operationId = "suggestEmailServerConfig",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiEmailServerSuggestionRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "SMTP suggestion derived from an email address. The sidecar does not save settings or write files.",
                                    content = JsonContent("ApiEmailServerSuggestionResponse")
                                },
                                ["400"] = new { description = "Invalid email address." },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/tools/email/send"] = new
                    {
                        post = new
                        {
                            summary = "Send an email with explicit attachments",
                            operationId = "sendEmail",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiEmailSendRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Email was sent through the configured SMTP server.",
                                    content = JsonContent("ApiEmailSendResponse")
                                },
                                ["400"] = new { description = "Invalid recipient address or attachment path." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "An explicit attachment file path was not found." },
                                ["409"] = new { description = "SMTP is not configured or sending failed." }
                            }
                        }
                    },
                    ["/api/tools/email/test-connection"] = new
                    {
                        post = new
                        {
                            summary = "Test the saved SMTP configuration",
                            operationId = "testEmailConnection",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "A test email was sent to the configured sender address using the saved appsettings.json SMTP configuration.",
                                    content = JsonContent("ApiEmailTestResponse")
                                },
                                ["400"] = new { description = "Configured sender address is invalid." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["409"] = new { description = "SMTP is not configured or the test send failed." }
                            }
                        }
                    },
                    ["/api/tools/container-packing/analyze"] = new
                    {
                        post = new
                        {
                            summary = "Analyze container packing",
                            operationId = "analyzeContainerPacking",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiContainerPackingAnalyzeRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Container packing analysis. The sidecar processes only in-memory request data and does not write a default system-drive path.",
                                    content = JsonContent("ApiContainerPackingAnalyzeResponse")
                                },
                                ["400"] = new { description = "Invalid container or cargo input." },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/tools/container-packing/projects"] = new
                    {
                        get = new
                        {
                            summary = "List saved container packing projects",
                            operationId = "listContainerPackingProjects",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Saved container packing project summaries from the runtime database. The sidecar does not read invoice, customs, payment, or reimbursement data.",
                                    content = JsonContent("ApiContainerPackingProjectListResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        },
                        post = new
                        {
                            summary = "Save container packing project",
                            operationId = "saveContainerPackingProject",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiContainerPackingProjectSaveRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Container packing project was saved to the runtime database under the configured data root.",
                                    content = JsonContent("ApiContainerPackingProjectSaveResponse")
                                },
                                ["400"] = new { description = "Invalid project, container, or cargo input." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["409"] = new { description = "Project could not be saved." }
                            }
                        }
                    },
                    ["/api/tools/container-packing/projects/{id}"] = new
                    {
                        get = new
                        {
                            summary = "Get saved container packing project",
                            operationId = "getContainerPackingProject",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "Container packing project id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Saved container packing project detail with cargo rows.",
                                    content = JsonContent("ApiContainerPackingProjectResponse")
                                },
                                ["400"] = new { description = "Invalid project id." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Project was not found." }
                            }
                        },
                        delete = new
                        {
                            summary = "Delete saved container packing project",
                            operationId = "deleteContainerPackingProject",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "Container packing project id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Container packing project was deleted from the runtime database.",
                                    content = JsonContent("ApiCommandResponse")
                                },
                                ["400"] = new { description = "Invalid project id." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Project was not found." }
                            }
                        }
                    },
                    ["/api/tools/container-packing/container-types"] = new
                    {
                        get = new
                        {
                            summary = "List container packing container types",
                            operationId = "listContainerPackingContainerTypes",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Container type definitions from the runtime database.",
                                    content = JsonContent("ApiContainerTypeListResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        },
                        post = new
                        {
                            summary = "Save container packing container type",
                            operationId = "saveContainerPackingContainerType",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiContainerTypeSaveRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Container type was saved to the runtime database.",
                                    content = JsonContent("ApiContainerTypeSaveResponse")
                                },
                                ["400"] = new { description = "Invalid container type payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["409"] = new { description = "Container type could not be saved." }
                            }
                        }
                    },
                    ["/api/tools/container-packing/container-types/{id}"] = new
                    {
                        delete = new
                        {
                            summary = "Delete container packing container type",
                            operationId = "deleteContainerPackingContainerType",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "Container type id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Custom container type was deleted from the runtime database.",
                                    content = JsonContent("ApiCommandResponse")
                                },
                                ["400"] = new { description = "Invalid container type id." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Container type was not found." },
                                ["409"] = new { description = "System default container types cannot be deleted." }
                            }
                        }
                    },
                    ["/api/tools/excel/import-preview"] = new
                    {
                        post = new
                        {
                            summary = "Preview Excel invoice import",
                            operationId = "previewExcelImport",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiExcelImportPreviewRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Parsed Excel invoice draft. The sidecar only reads the explicit source path and does not persist the result.",
                                    content = JsonContent("ApiExcelImportPreviewResponse")
                                },
                                ["400"] = new { description = "Invalid Excel source path." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Source file was not found." },
                                ["409"] = new { description = "Excel parsing failed." }
                            }
                        }
                    },
                    ["/api/tools/excel/template/save-to-path"] = new
                    {
                        post = new
                        {
                            summary = "Start Excel import template export job",
                            operationId = "startExcelTemplateSaveToPathJob",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiExcelOutputRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["202"] = new
                                {
                                    description = "Excel template export job was accepted. The built-in template is read from Resources/ExcelTemplates under the program root and written only to the explicit destination path.",
                                    content = JsonContent("BackgroundJobSnapshot")
                                },
                                ["400"] = new { description = "Invalid Excel output path." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Trusted desktop token required." }
                            }
                        }
                    },
                    ["/api/tools/pdf/merge/upload"] = new
                    {
                        post = new
                        {
                            summary = "Upload PDF files and start browser download merge job",
                            operationId = "uploadAndStartPdfMergeDownloadJob",
                            requestBody = new
                            {
                                required = true,
                                content = new Dictionary<string, object>
                                {
                                    ["multipart/form-data"] = new
                                    {
                                        schema = new
                                        {
                                            type = "object",
                                            properties = new Dictionary<string, object>
                                            {
                                                ["files"] = new
                                                {
                                                    type = "array",
                                                    items = new { type = "string", format = "binary" }
                                                }
                                            }
                                        }
                                    }
                                }
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["202"] = new { description = "Controlled PDF merge download job accepted.", content = JsonContent("BackgroundJobSnapshot") },
                                ["400"] = new { description = "Invalid PDF upload." },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/tools/excel/template/download"] = new
                    {
                        post = new
                        {
                            summary = "Start Excel import template browser download job",
                            operationId = "startExcelTemplateDownloadJob",
                            responses = new Dictionary<string, object>
                            {
                                ["202"] = new { description = "Controlled Excel download job accepted.", content = JsonContent("BackgroundJobSnapshot") },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/tools/excel/booking-sheet/blank/save-to-path"] = new
                    {
                        post = new
                        {
                            summary = "Start blank booking sheet export job",
                            operationId = "startBlankBookingSheetSaveToPathJob",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiExcelOutputRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["202"] = new
                                {
                                    description = "Blank booking sheet export job was accepted. The built-in template is read from Resources/ExcelTemplates under the program root and written only to the explicit destination path.",
                                    content = JsonContent("BackgroundJobSnapshot")
                                },
                                ["400"] = new { description = "Invalid Excel output path." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Trusted desktop token required." }
                            }
                        }
                    },
                    ["/api/tools/excel/booking-sheet/blank/download"] = new
                    {
                        post = new
                        {
                            summary = "Start blank booking sheet browser download job",
                            operationId = "startBlankBookingSheetDownloadJob",
                            responses = new Dictionary<string, object>
                            {
                                ["202"] = new { description = "Controlled Excel download job accepted.", content = JsonContent("BackgroundJobSnapshot") },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/tools/excel/booking-sheet/convert/save-to-path"] = new
                    {
                        post = new
                        {
                            summary = "Start booking sheet conversion job",
                            operationId = "startBookingSheetConvertSaveToPathJob",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiExcelConvertBookingSheetRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["202"] = new
                                {
                                    description = "Booking sheet conversion job was accepted. The source is an explicit user-selected Excel path and the result is written only to the explicit destination path.",
                                    content = JsonContent("BackgroundJobSnapshot")
                                },
                                ["400"] = new { description = "Invalid Excel source or output path." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Trusted desktop token required." }
                            }
                        }
                    },
                    ["/api/tools/excel/booking-sheet/convert/upload"] = new
                    {
                        post = new
                        {
                            summary = "Upload Excel and start booking sheet browser download job",
                            operationId = "uploadAndStartBookingSheetConvertDownloadJob",
                            parameters = new object[]
                            {
                                QueryParameter("fileName", "string", null, "Original Excel file name.")
                            },
                            requestBody = new { required = true, content = BinaryContent() },
                            responses = new Dictionary<string, object>
                            {
                                ["202"] = new { description = "Controlled Excel conversion job accepted.", content = JsonContent("BackgroundJobSnapshot") },
                                ["400"] = new { description = "Invalid or empty Excel upload." },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/tools/excel/booking-sheet/from-invoice/save-to-path"] = new
                    {
                        post = new
                        {
                            summary = "Start invoice booking sheet export job",
                            operationId = "startInvoiceBookingSheetSaveToPathJob",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiInvoiceBookingSheetRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["202"] = new
                                {
                                    description = "Invoice booking sheet export job was accepted. The built-in template is read from Resources/ExcelTemplates under the program root and written only to the explicit destination path.",
                                    content = JsonContent("BackgroundJobSnapshot")
                                },
                                ["400"] = new { description = "Invalid invoice id or Excel output path." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Trusted desktop token required." },
                                ["404"] = new { description = "Invoice was not found." }
                            }
                        }
                    },
                    ["/api/tools/excel/booking-sheet/from-invoice/{invoiceId}/download"] = new
                    {
                        post = new
                        {
                            summary = "Start invoice booking sheet browser download job",
                            operationId = "startInvoiceBookingSheetDownloadJob",
                            parameters = new object[]
                            {
                                PathParameter("invoiceId", "integer", "int32", "Invoice id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["202"] = new { description = "Controlled invoice booking sheet download job accepted.", content = JsonContent("BackgroundJobSnapshot") },
                                ["400"] = new { description = "Invalid invoice id." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Invoice was not found." }
                            }
                        }
                    },
            };
    }
}
