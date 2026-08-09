using System.Text.Json;

namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
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
                  <p>Process liveness is available at <a href="/livez"><code>/livez</code></a>.</p>
                  <p>Dependency-aware readiness is available at <a href="/readyz"><code>/readyz</code></a>.</p>
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

        private static object PagedResponseSchema(string itemSchemaName)
        {
            return new
            {
                type = "object",
                required = new[] { "items", "totalCount", "pageNumber", "pageSize", "totalPages", "hasPreviousPage", "hasNextPage" },
                properties = new Dictionary<string, object>
                {
                    ["items"] = RefArraySchema(itemSchemaName),
                    ["totalCount"] = new { type = "integer", format = "int32" },
                    ["pageNumber"] = new { type = "integer", format = "int32" },
                    ["pageSize"] = new { type = "integer", format = "int32" },
                    ["totalPages"] = new { type = "integer", format = "int32" },
                    ["hasPreviousPage"] = new { type = "boolean" },
                    ["hasNextPage"] = new { type = "boolean" }
                }
            };
        }
    }
}
