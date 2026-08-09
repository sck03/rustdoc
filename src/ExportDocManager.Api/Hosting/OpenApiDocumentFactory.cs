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
                        required = true,
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

        private static object MasterDataPagedQueryPath(
            string summary,
            string operationId,
            string itemSchemaName)
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
                        QueryParameter("pageNumber", "integer", "int32", "Page number starting from 1.", required: true),
                        QueryParameter("pageSize", "integer", "int32", "Page size capped by the API endpoint.", required: true)
                    },
                    responses = new Dictionary<string, object>
                    {
                        ["200"] = new { description = "Paged master data query results.", content = JsonContent($"ApiPagedResponseOf{itemSchemaName}") },
                        ["401"] = new { description = "Missing or invalid bearer token." }
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
                    requestBody = new
                    {
                        required = true,
                        content = JsonContent("ApiSingleWindowSubmitPackageRequest")
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
                        ["400"] = new { description = "Invalid import package path/type or the deployment is not an independent SQLite station." },
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
                        ["400"] = new { description = "Invalid/empty package upload or the deployment is not an independent SQLite station." },
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

    }
}
