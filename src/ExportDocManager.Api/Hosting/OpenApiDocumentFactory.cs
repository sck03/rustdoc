using System.Text.Json;

namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        public static object Create(ApiRuntimeOptions runtimeOptions)
        {
            ArgumentNullException.ThrowIfNull(runtimeOptions);

            return new
            {
                openapi = "3.0.1",
                info = new
                {
                    title = "ExportDocManager API",
                    version = ProductVersionProvider.ProductVersion,
                    description = "Local sidecar API for the multi-platform ExportDocManager refactor."
                },
                servers = runtimeOptions.ListenUrls
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(url => new { url })
                    .ToArray(),
                paths = CreatePaths(),
                components = new
                {
                    securitySchemes = new Dictionary<string, object>
                    {
                        ["BearerAuth"] = new
                        {
                            type = "http",
                            scheme = "bearer",
                            bearerFormat = "opaque",
                            description = "Use the accessToken returned by /api/auth/login as a Bearer token for protected /api endpoints."
                        },
                        ["DesktopAccess"] = new
                        {
                            type = "apiKey",
                            @in = "header",
                            name = ApiDesktopAccessOptions.HeaderName,
                            description = "Internal desktop sidecar token passed by the Tauri shell for lifecycle-only endpoints."
                        }
                    },
                    schemas = CreateSchemas()
                }
            };
        }

        private static void AddOpenApiEntries(Dictionary<string, object> target, Dictionary<string, object> source)
        {
            foreach (var entry in source)
            {
                target.Add(entry.Key, entry.Value);
            }
        }

        private static object MasterDataListPath(
            string summary,
            string operationId,
            string schemaName,
            string createSummary,
            string createOperationId)
        {
            return new
            {
                get = new
                {
                    summary,
                    operationId,
                    parameters = new object[]
                    {
                        QueryParameter("keyword", "string", null, "Optional keyword filter.")
                    },
                    responses = new Dictionary<string, object>
                    {
                        ["200"] = new
                        {
                            description = "Master data query results.",
                            content = JsonArrayContent(schemaName)
                        },
                        ["401"] = new { description = "Missing or invalid bearer token." }
                    }
                },
                post = new
                {
                    summary = createSummary,
                    operationId = createOperationId,
                    requestBody = new
                    {
                        required = true,
                        content = JsonContent(schemaName)
                    },
                    responses = new Dictionary<string, object>
                    {
                        ["201"] = new
                        {
                            description = "Created master data row.",
                            content = JsonContent(schemaName)
                        },
                        ["400"] = new { description = "Invalid master data payload." },
                        ["401"] = new { description = "Missing or invalid bearer token." },
                        ["409"] = new { description = "Master data row could not be saved." }
                    }
                }
            };
        }

        private static object MasterDataDetailPath(
            string getSummary,
            string getOperationId,
            string updateSummary,
            string updateOperationId,
            string deleteSummary,
            string deleteOperationId,
            string schemaName,
            string idDescription)
        {
            return new
            {
                get = new
                {
                    summary = getSummary,
                    operationId = getOperationId,
                    parameters = new object[]
                    {
                        PathParameter("id", "integer", "int32", idDescription)
                    },
                    responses = new Dictionary<string, object>
                    {
                        ["200"] = new
                        {
                            description = "Master data row detail.",
                            content = JsonContent(schemaName)
                        },
                        ["400"] = new { description = "Invalid master data id." },
                        ["401"] = new { description = "Missing or invalid bearer token." },
                        ["404"] = new { description = "Master data row not found." }
                    }
                },
                put = new
                {
                    summary = updateSummary,
                    operationId = updateOperationId,
                    parameters = new object[]
                    {
                        PathParameter("id", "integer", "int32", idDescription)
                    },
                    requestBody = new
                    {
                        required = true,
                        content = JsonContent(schemaName)
                    },
                    responses = new Dictionary<string, object>
                    {
                        ["200"] = new
                        {
                            description = "Updated master data row.",
                            content = JsonContent(schemaName)
                        },
                        ["400"] = new { description = "Invalid master data payload." },
                        ["401"] = new { description = "Missing or invalid bearer token." },
                        ["404"] = new { description = "Master data row not found." },
                        ["409"] = new { description = "Master data row could not be saved." }
                    }
                },
                delete = new
                {
                    summary = deleteSummary,
                    operationId = deleteOperationId,
                    parameters = new object[]
                    {
                        PathParameter("id", "integer", "int32", idDescription)
                    },
                    responses = new Dictionary<string, object>
                    {
                        ["200"] = new
                        {
                            description = "Deleted master data row.",
                            content = JsonContent("ApiCommandResponse")
                        },
                        ["400"] = new { description = "Invalid master data id." },
                        ["401"] = new { description = "Missing or invalid bearer token." },
                        ["404"] = new { description = "Master data row not found." },
                        ["409"] = new { description = "Master data row could not be deleted." }
                    }
                }
            };
        }

        private static object SingleWindowDocumentPath(
            string getSummary,
            string getOperationId,
            string saveSummary,
            string saveOperationId,
            string documentSchemaName,
            string saveResponseSchemaName)
        {
            return new
            {
                get = new
                {
                    summary = getSummary,
                    operationId = getOperationId,
                    parameters = new object[]
                    {
                        PathParameter("invoiceId", "integer", "int32", "Source invoice id.")
                    },
                    responses = SingleWindowDocumentReadResponses(documentSchemaName)
                },
                put = new
                {
                    summary = saveSummary,
                    operationId = saveOperationId,
                    parameters = new object[]
                    {
                        PathParameter("invoiceId", "integer", "int32", "Source invoice id.")
                    },
                    requestBody = new
                    {
                        required = true,
                        content = JsonContent(documentSchemaName)
                    },
                    responses = new Dictionary<string, object>
                    {
                        ["200"] = new
                        {
                            description = "Saved Single Window draft document.",
                            content = JsonContent(saveResponseSchemaName)
                        },
                        ["400"] = new { description = "Invalid invoice id or draft payload." },
                        ["401"] = new { description = "Missing or invalid bearer token." },
                        ["403"] = new { description = "The current user cannot access the invoice." },
                        ["404"] = new { description = "Source invoice not found." },
                        ["409"] = new { description = "Draft document could not be saved." }
                    }
                }
            };
        }

        private static object SingleWindowBuildDefaultsPath(
            string summary,
            string operationId,
            string documentSchemaName)
        {
            return new
            {
                post = new
                {
                    summary,
                    operationId,
                    parameters = new object[]
                    {
                        PathParameter("invoiceId", "integer", "int32", "Source invoice id.")
                    },
                    responses = SingleWindowDocumentReadResponses(documentSchemaName)
                }
            };
        }

        private static object SingleWindowLockedFieldsPath(
            string summary,
            string operationId)
        {
            return new
            {
                get = new
                {
                    summary,
                    operationId,
                    parameters = new object[]
                    {
                        PathParameter("invoiceId", "integer", "int32", "Source invoice id.")
                    },
                    responses = new Dictionary<string, object>
                    {
                        ["200"] = new
                        {
                            description = "Manually locked Single Window draft fields.",
                            content = JsonContent("ApiSingleWindowLockedFieldsResponse")
                        },
                        ["400"] = new { description = "Invalid invoice id." },
                        ["401"] = new { description = "Missing or invalid bearer token." },
                        ["403"] = new { description = "The current user cannot access the invoice." },
                        ["404"] = new { description = "Source invoice not found." },
                        ["409"] = new { description = "Locked fields could not be read." }
                    }
                }
            };
        }

        private static object SingleWindowUnlockFieldsPath(
            string summary,
            string operationId,
            string responseSchemaName)
        {
            return new
            {
                post = new
                {
                    summary,
                    operationId,
                    parameters = new object[]
                    {
                        PathParameter("invoiceId", "integer", "int32", "Source invoice id.")
                    },
                    requestBody = new
                    {
                        required = true,
                        content = JsonContent("ApiSingleWindowUnlockFieldsRequest")
                    },
                    responses = new Dictionary<string, object>
                    {
                        ["200"] = new
                        {
                            description = "Selected locked fields restored to current suggested values.",
                            content = JsonContent(responseSchemaName)
                        },
                        ["400"] = new { description = "Invalid invoice id or unlock payload." },
                        ["401"] = new { description = "Missing or invalid bearer token." },
                        ["403"] = new { description = "The current user cannot access the invoice." },
                        ["404"] = new { description = "Source invoice not found." },
                        ["409"] = new { description = "Locked fields could not be restored." }
                    }
                }
            };
        }

        private static object SingleWindowSubmitPackagePath(
            string summary,
            string operationId)
        {
            return new
            {
                post = new
                {
                    summary,
                    operationId,
                    parameters = new object[]
                    {
                        PathParameter("invoiceId", "integer", "int32", "Source invoice id.")
                    },
                    requestBody = new
                    {
                        required = false,
                        content = JsonContent("ApiSingleWindowSubmitPackageRequest")
                    },
                    responses = new Dictionary<string, object>
                    {
                        ["200"] = new
                        {
                            description = "Single Window submit package exported.",
                            content = JsonContent("ApiSingleWindowHandoffPackageResponse")
                        },
                        ["400"] = new { description = "Invalid invoice id or package path." },
                        ["401"] = new { description = "Missing or invalid bearer token." },
                        ["403"] = new { description = "The current user cannot access the invoice." },
                        ["404"] = new { description = "Source invoice not found." },
                        ["409"] = new { description = "Submit package could not be exported." }
                    }
                }
            };
        }

        private static object MasterDataPagedListPath(
            string summary,
            string operationId,
            string itemSchemaName,
            string createSummary,
            string createOperationId)
        {
            return new
            {
                get = new
                {
                    summary,
                    operationId,
                    parameters = new object[]
                    {
                        QueryParameter("keyword", "string", null, "Optional keyword filter."),
                        QueryParameter("pageNumber", "integer", "int32", "Page number starting from 1."),
                        QueryParameter("pageSize", "integer", "int32", "Page size capped by the API endpoint.")
                    },
                    responses = new Dictionary<string, object>
                    {
                        ["200"] = new { description = "Paged master data query results.", content = JsonContent($"ApiPagedResponseOf{itemSchemaName}") },
                        ["401"] = new { description = "Missing or invalid bearer token." }
                    }
                },
                post = new
                {
                    summary = createSummary,
                    operationId = createOperationId,
                    requestBody = new { required = true, content = JsonContent(itemSchemaName) },
                    responses = new Dictionary<string, object>
                    {
                        ["201"] = new { description = "Created master data row.", content = JsonContent(itemSchemaName) },
                        ["400"] = new { description = "Invalid master data payload." },
                        ["401"] = new { description = "Missing or invalid bearer token." },
                        ["409"] = new { description = "Master data row could not be saved." }
                    }
                }
            };
        }

        private static object SingleWindowSubmitPackageDownloadPath(
            string summary,
            string operationId)
        {
            return new
            {
                post = new
                {
                    summary,
                    operationId,
                    parameters = new object[]
                    {
                        PathParameter("invoiceId", "integer", "int32", "Source invoice id.")
                    },
                    responses = new Dictionary<string, object>
                    {
                        ["200"] = new
                        {
                            description = "Single Window submit package attachment.",
                            content = BinaryContent()
                        },
                        ["400"] = new { description = "Invalid invoice id." },
                        ["401"] = new { description = "Missing or invalid bearer token." },
                        ["403"] = new { description = "The current user cannot access the invoice." },
                        ["404"] = new { description = "Source invoice not found." },
                        ["409"] = new { description = "Submit package could not be generated." }
                    }
                }
            };
        }

        private static object SingleWindowImportPackagePath(
            string summary,
            string operationId,
            string successDescription)
        {
            return new
            {
                post = new
                {
                    summary,
                    operationId,
                    requestBody = new
                    {
                        required = true,
                        content = JsonContent("ApiSingleWindowImportPackageRequest")
                    },
                    responses = new Dictionary<string, object>
                    {
                        ["200"] = new
                        {
                            description = successDescription,
                            content = JsonContent("ApiSingleWindowImportedPackageResponse")
                        },
                        ["400"] = new { description = "Invalid import package path or package type." },
                        ["401"] = new { description = "Missing or invalid bearer token." },
                        ["403"] = new { description = "The current user cannot import the package for its source invoice." },
                        ["404"] = new { description = "Package file not found." },
                        ["409"] = new { description = "Package could not be imported." }
                    }
                }
            };
        }

        private static object SingleWindowUploadPackagePath(
            string summary,
            string operationId)
        {
            return new
            {
                post = new
                {
                    summary,
                    operationId,
                    parameters = new object[]
                    {
                        QueryParameter("fileName", "string", null, "Uploaded .swpkg file name."),
                        QueryParameter("workingDirectory", "string", null, "Optional controlled working directory."),
                        QueryParameter("keepWorkingDirectory", "boolean", null, "Whether to keep the extracted working directory.")
                    },
                    requestBody = new { required = true, content = BinaryContent() },
                    responses = new Dictionary<string, object>
                    {
                        ["200"] = new { description = "Uploaded package imported.", content = JsonContent("ApiSingleWindowImportedPackageResponse") },
                        ["400"] = new { description = "Invalid or empty package upload." },
                        ["401"] = new { description = "Missing or invalid bearer token." },
                        ["404"] = new { description = "Package source data was not found." },
                        ["409"] = new { description = "Package could not be imported." }
                    }
                }
            };
        }

        private static Dictionary<string, object> SingleWindowDocumentReadResponses(string documentSchemaName)
        {
            return new Dictionary<string, object>
            {
                ["200"] = new
                {
                    description = "Single Window draft document.",
                    content = JsonContent(documentSchemaName)
                },
                ["400"] = new { description = "Invalid invoice id." },
                ["401"] = new { description = "Missing or invalid bearer token." },
                ["403"] = new { description = "The current user cannot access the invoice." },
                ["404"] = new { description = "Source invoice not found." },
                ["409"] = new { description = "Draft document could not be built." }
            };
        }

        private static object SingleWindowCustomsCooDocumentSchema()
        {
            var properties = SchemaProperties(
                stringProperties:
                [
                    "InvoiceNo",
                    "ContractNo",
                    "Status",
                    "CertNo",
                    "ApplyType",
                    "CertStatus",
                    "CertType",
                    "EntMgrNo",
                    "CiqRegNo",
                    "AplRegNo",
                    "EtpsName",
                    "ApplName",
                    "Applicant",
                    "ApplTel",
                    "OrgCode",
                    "FetchPlace",
                    "AplAdd",
                    "InvDate",
                    "InvNo",
                    "AplDate",
                    "DestCountry",
                    "DestCountryCode",
                    "DestCountryName",
                    "Exporter",
                    "Consignee",
                    "GoodsSpecClause",
                    "Mark",
                    "LoadPort",
                    "UnloadPort",
                    "TransMeans",
                    "TransName",
                    "TransCountryCode",
                    "TransCountryName",
                    "TransPort",
                    "DestPort",
                    "TransDetails",
                    "IntendExpDate",
                    "TradeModeCode",
                    "FobValue",
                    "TotalAmt",
                    "Note",
                    "LcNo",
                    "SpecInvTerms",
                    "PriceTerms",
                    "Curr",
                    "Remark",
                    "Producer",
                    "ProducerSertFlag",
                    "ExhibitFlag",
                    "ThirdPartyInvFlag",
                    "ExporterTel",
                    "ExporterFax",
                    "ExporterEmail",
                    "ConsigneeTel",
                    "ConsigneeFax",
                    "ConsigneeEmail",
                    "PredictFlag",
                    "ExpDeclDate",
                    "OriCountryCode",
                    "OriCountry",
                    "ChkValidDate",
                    "EtpsConcEr",
                    "EtpsTel",
                    "EntryId",
                    "PrcsAssembly",
                    "OldCertNo",
                    "ModReason",
                    "ModColm",
                    "OldSituDesc",
                    "ModSituDesc",
                    "OldDeclDate",
                    "OldIssueDate",
                    "AplPromiseCode",
                    "WarningSummary",
                    "SourceDiffSummary"
                ],
                integerProperties:
                [
                    "Id",
                    "SourceInvoiceId",
                    "WarningCount",
                    "DraftRevision",
                    "SourceDiffCount",
                    "ManualLockedFieldCount"
                ],
                dateTimeProperties: ["LastGeneratedAt"]);

            properties["items"] = RefArraySchema("ApiCustomsCooItemDto");
            properties["nonpartyCorps"] = RefArraySchema("ApiCustomsCooNonpartyCorpDto");
            properties["attachments"] = RefArraySchema("ApiCustomsCooAttachmentDto");
            return ObjectSchema(properties);
        }

        private static object SingleWindowCustomsCooItemSchema()
        {
            var properties = SchemaProperties(
                stringProperties:
                [
                    "SourceStyleNo",
                    "GoodsItemFlag",
                    "HSCode",
                    "GoodsName",
                    "GoodsNameE",
                    "PackQty",
                    "PackUnit",
                    "GoodsQty",
                    "GoodsQtyRef",
                    "GoodsUnitE",
                    "GoodsUnit",
                    "GoodsUnitRef",
                    "SecdGoodsQtyRef",
                    "SecdGoodsUnitRef",
                    "GrossWt",
                    "NetWt",
                    "WtUnit",
                    "InvPrice",
                    "InvValue",
                    "FobValue",
                    "ICompPrpr",
                    "GoodsDesc",
                    "OriCriteria",
                    "OriCriteriaRef",
                    "GoodsOriginCountry",
                    "GoodsOriginCountryEn",
                    "Producer",
                    "ProducerTel",
                    "ProducerFax",
                    "ProducerEmail",
                    "CiqRegNo",
                    "PrdcEtpsName",
                    "PrdcEtpsConcEr",
                    "PrdcEtpsTel",
                    "ProducerSertFlag",
                    "OriCriteriaSub",
                    "InvNo",
                    "PackType",
                    "GoodsTaxRate"
                ],
                integerProperties:
                [
                    "Id",
                    "DocumentId",
                    "SourceItemId",
                    "GNo"
                ]);

            return ObjectSchema(properties);
        }

        private static object SingleWindowCustomsCooNonpartyCorpSchema()
        {
            return ObjectSchema(SchemaProperties(
                stringProperties:
                [
                    "EntName",
                    "EntAddr",
                    "EntCountryCode",
                    "EntCountryName"
                ],
                integerProperties:
                [
                    "Id",
                    "DocumentId",
                    "SortNo"
                ]));
        }

        private static object SingleWindowCustomsCooAttachmentSchema()
        {
            return ObjectSchema(SchemaProperties(
                stringProperties:
                [
                    "CertNo",
                    "CertType",
                    "AplRegNo",
                    "CiqRegNo",
                    "FileType",
                    "FileName",
                    "FilePath",
                    "MediaType",
                    "Description",
                    "DocType"
                ],
                integerProperties:
                [
                    "Id",
                    "DocumentId",
                    "SortOrder"
                ],
                booleanProperties:
                [
                    "IsDelay",
                    "FileExistsAtBuild"
                ]));
        }

        private static object SingleWindowAgentConsignmentDocumentSchema()
        {
            return ObjectSchema(SchemaProperties(
                stringProperties:
                [
                    "InvoiceNo",
                    "ContractNo",
                    "Status",
                    "CounterpartyStatus",
                    "CopCusCode",
                    "Sign",
                    "OperType",
                    "GName",
                    "CodeTS",
                    "DeclTotal",
                    "IEDate",
                    "ListNo",
                    "TradeMode",
                    "OriCountry",
                    "TradeCode",
                    "AgentCode",
                    "Curr",
                    "QtyOrWeight",
                    "PackingCondition",
                    "OtherNote",
                    "ConsignTele",
                    "EntryId",
                    "ReceiveDate",
                    "PaperInfo",
                    "OtherRecInfo",
                    "DeclarePrice",
                    "PromiseNote",
                    "DeclTele",
                    "ConsignNo",
                    "WarningSummary",
                    "SourceDiffSummary"
                ],
                integerProperties:
                [
                    "Id",
                    "SourceInvoiceId",
                    "WarningCount",
                    "DraftRevision",
                    "SourceDiffCount",
                    "ManualLockedFieldCount"
                ],
                dateTimeProperties: ["LastGeneratedAt"]));
        }

        private static Dictionary<string, object> SchemaProperties(
            IReadOnlyList<string> stringProperties = null,
            IReadOnlyList<string> integerProperties = null,
            IReadOnlyList<string> booleanProperties = null,
            IReadOnlyList<string> dateTimeProperties = null)
        {
            var properties = new Dictionary<string, object>(StringComparer.Ordinal);

            foreach (string name in stringProperties ?? [])
            {
                properties[JsonPropertyName(name)] = StringProperty($"{name}.");
            }

            foreach (string name in integerProperties ?? [])
            {
                properties[JsonPropertyName(name)] = new { type = "integer", format = "int32" };
            }

            foreach (string name in booleanProperties ?? [])
            {
                properties[JsonPropertyName(name)] = new { type = "boolean" };
            }

            foreach (string name in dateTimeProperties ?? [])
            {
                properties[JsonPropertyName(name)] = new { type = "string", format = "date-time" };
            }

            return properties;
        }

        private static object ObjectSchema(Dictionary<string, object> properties)
        {
            return new
            {
                type = "object",
                required = properties.Keys.ToArray(),
                properties
            };
        }

        private static string JsonPropertyName(string name)
        {
            return JsonNamingPolicy.CamelCase.ConvertName(name);
        }

        private static object QueryParameter(
            string name,
            string type,
            string format,
            string description,
            bool required = false)
        {
            var schema = new Dictionary<string, object>
            {
                ["type"] = type
            };

            if (!string.IsNullOrWhiteSpace(format))
            {
                schema["format"] = format;
            }

            return new
            {
                name,
                @in = "query",
                required,
                description,
                schema
            };
        }

        private static Dictionary<string, string[]>[] BearerSecurity() =>
        [
            new Dictionary<string, string[]>
            {
                ["Bearer"] = Array.Empty<string>()
            }
        ];

        private static object PathParameter(
            string name,
            string type,
            string format,
            string description)
        {
            var schema = new Dictionary<string, object>
            {
                ["type"] = type
            };

            if (!string.IsNullOrWhiteSpace(format))
            {
                schema["format"] = format;
            }

            return new
            {
                name,
                @in = "path",
                required = true,
                description,
                schema
            };
        }

        private static Dictionary<string, object> AuditLogFilterProperties()
        {
            return new Dictionary<string, object>
            {
                ["invoiceKeyword"] = StringProperty("Invoice-related keyword."),
                ["entityName"] = StringProperty("Entity name filter."),
                ["action"] = StringProperty("Audit action filter."),
                ["userId"] = StringProperty("Operator keyword."),
                ["startTime"] = new { type = "string", format = "date-time", nullable = true },
                ["endTime"] = new { type = "string", format = "date-time", nullable = true },
                ["keyword"] = StringProperty("Keyword for entity, entity id, user, old values, or new values."),
                ["maxCount"] = new { type = "integer", format = "int32" }
            };
        }

        private static Dictionary<string, object> QueryInvoiceFilterProperties()
        {
            return new Dictionary<string, object>
            {
                ["startDate"] = new { type = "string", format = "date-time", nullable = true },
                ["endDate"] = new { type = "string", format = "date-time", nullable = true },
                ["customerId"] = new { type = "integer", format = "int32", nullable = true },
                ["exporterId"] = new { type = "integer", format = "int32", nullable = true },
                ["keyword"] = StringProperty("Keyword for invoice number, contract number, customer, or exporter."),
                ["contractNo"] = StringProperty("Contract number keyword."),
                ["invoiceType"] = StringProperty("Invoice type filter."),
                ["transportMode"] = StringProperty("Transport mode filter."),
                ["styleName"] = StringProperty("Line-item style name keyword."),
                ["styleNo"] = StringProperty("Line-item style number keyword.")
            };
        }

        private static Dictionary<string, object> MergeProperties(
            Dictionary<string, object> first,
            Dictionary<string, object> second)
        {
            var merged = new Dictionary<string, object>(first ?? new Dictionary<string, object>());
            foreach (var item in second ?? new Dictionary<string, object>())
            {
                merged[item.Key] = item.Value;
            }

            return merged;
        }

        public static string CreateSwaggerLandingPage()
        {
            return """
                <!doctype html>
                <html lang="zh-CN">
                <head>
                  <meta charset="utf-8">
                  <title>ExportDocManager API</title>
                  <style>
                    body { font-family: system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; margin: 32px; line-height: 1.55; }
                    code { background: #f2f4f7; padding: 2px 6px; border-radius: 4px; }
                    a { color: #075985; }
                  </style>
                </head>
                <body>
                  <h1>ExportDocManager API</h1>
                  <p>Sidecar is running. OpenAPI JSON is available at <a href="/openapi/v1.json"><code>/openapi/v1.json</code></a>.</p>
                  <p>Lightweight readiness is available at <a href="/readyz"><code>/readyz</code></a>.</p>
                  <p>Health check is available at <a href="/healthz"><code>/healthz</code></a>.</p>
                </body>
                </html>
                """;
        }

        private static object JsonContent(string schemaName)
        {
            return new Dictionary<string, object>
            {
                ["application/json"] = new
                {
                    schema = new Dictionary<string, object>
                    {
                        ["$ref"] = $"#/components/schemas/{schemaName}"
                    }
                }
            };
        }

        private static object JsonArrayContent(string schemaName)
        {
            return new Dictionary<string, object>
            {
                ["application/json"] = new
                {
                    schema = new
                    {
                        type = "array",
                        items = RefSchema(schemaName)
                    }
                }
            };
        }

        private static object BinaryContent()
        {
            return new Dictionary<string, object>
            {
                ["application/octet-stream"] = new
                {
                    schema = new
                    {
                        type = "string",
                        format = "binary"
                    }
                }
            };
        }

        private static object StringProperty(string description)
        {
            return new
            {
                type = "string",
                description
            };
        }

        private static object StringArrayProperty(string description)
        {
            return new
            {
                type = "array",
                description,
                items = new { type = "string" }
            };
        }

        private static object DecimalProperty(string description)
        {
            return new
            {
                type = "number",
                format = "decimal",
                description
            };
        }

        private static object NullableDecimalProperty(string description)
        {
            return new
            {
                type = "number",
                format = "decimal",
                nullable = true,
                description
            };
        }

        private static object RefSchema(string schemaName)
        {
            return new Dictionary<string, object>
            {
                ["$ref"] = $"#/components/schemas/{schemaName}"
            };
        }

        private static object RefArraySchema(string schemaName)
        {
            return new
            {
                type = "array",
                items = RefSchema(schemaName)
            };
        }
    }
}
