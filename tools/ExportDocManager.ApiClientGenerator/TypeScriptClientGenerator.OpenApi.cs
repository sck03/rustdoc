using System.Text.Json;
using System.Text.Json.Nodes;

internal static partial class TypeScriptClientGenerator
{
    private static IReadOnlyList<ApiOperation> DiscoverOperations(JsonObject document)
    {
        var operations = new List<ApiOperation>();
        var paths = GetObject(document, "paths") ?? throw new InvalidOperationException("OpenAPI document does not contain paths.");

        foreach (var path in paths.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (path.Value is not JsonObject pathObject)
            {
                continue;
            }

            foreach (string method in HttpMethods)
            {
                if (pathObject[method] is not JsonObject operationObject)
                {
                    continue;
                }

                string operationId = GetString(operationObject, "operationId") ??
                    CreateOperationId(method, path.Key);
                var parameters = ReadParameters(operationObject);
                var requestBody = operationObject["requestBody"] as JsonObject;
                string? bodyType = ContentToType(requestBody?["content"]);
                bool bodyRequired = requestBody?["required"]?.GetValue<bool>() ?? false;
                string responseType = ReadResponseType(operationObject);

                operations.Add(new ApiOperation(
                    method,
                    path.Key,
                    operationId,
                    parameters.Where(item => item.Location == "path").ToArray(),
                    parameters.Where(item => item.Location == "query").ToArray(),
                    parameters.Where(item => item.Location == "header").ToArray(),
                    bodyType,
                    bodyRequired,
                    responseType));
            }
        }

        return operations
            .OrderBy(item => item.OperationId, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<ApiParameter> ReadParameters(JsonObject operation)
    {
        var parameters = new List<ApiParameter>();
        if (operation["parameters"] is not JsonArray array)
        {
            return parameters;
        }

        foreach (var item in array)
        {
            if (item is not JsonObject parameter)
            {
                continue;
            }

            string? parameterName = GetString(parameter, "name");
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                continue;
            }

            string location = GetString(parameter, "in") ?? string.Empty;
            bool required = string.Equals(location, "path", StringComparison.OrdinalIgnoreCase) ||
                (parameter["required"]?.GetValue<bool>() ?? false);
            string typeName = SchemaToType(parameter["schema"] as JsonObject);
            parameters.Add(new ApiParameter(parameterName, location, typeName, required));
        }

        return parameters;
    }

    private static string ReadResponseType(JsonObject operation)
    {
        if (operation["responses"] is not JsonObject responses)
        {
            return "void";
        }

        foreach (string preferred in new[] { "200", "201", "202", "203", "206", "204" })
        {
            if (responses[preferred] is JsonObject response)
            {
                string? responseType = ContentToType(response["content"]);
                if (!string.IsNullOrWhiteSpace(responseType))
                {
                    return responseType;
                }
            }
        }

        foreach (var response in responses)
        {
            if (response.Key.Length == 3 &&
                response.Key[0] == '2' &&
                response.Value is JsonObject responseObject)
            {
                string? responseType = ContentToType(responseObject["content"]);
                if (!string.IsNullOrWhiteSpace(responseType))
                {
                    return responseType;
                }
            }
        }

        return "void";
    }

    private static string? ContentToType(JsonNode? content)
    {
        if (content is not JsonObject contentObject)
        {
            return null;
        }

        if (contentObject["application/octet-stream"] is JsonObject)
        {
            return "Blob";
        }

        foreach (var entry in contentObject)
        {
            if (entry.Value is JsonObject mediaType &&
                mediaType["schema"] is JsonObject binarySchema &&
                GetString(binarySchema, "format") is { } format &&
                (string.Equals(format, "binary", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(format, "byte", StringComparison.OrdinalIgnoreCase)))
            {
                return "Blob";
            }
        }

        if (contentObject["multipart/form-data"] is JsonObject)
        {
            return "FormData";
        }

        JsonObject? jsonContent = contentObject["application/json"] as JsonObject
            ?? contentObject.FirstOrDefault(entry =>
                    entry.Key.EndsWith("+json", StringComparison.OrdinalIgnoreCase))
                .Value as JsonObject;
        if (jsonContent?["schema"] is not JsonObject schema)
        {
            return contentObject.Any(entry =>
                entry.Key.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
                ? "string"
                : null;
        }

        return SchemaToType(schema);
    }

    private static string SchemaToType(JsonObject? schema)
    {
        if (schema == null)
        {
            return "unknown";
        }

        if (TryGetEnumType(schema, out string enumType))
        {
            return enumType;
        }

        if (GetString(schema, "$ref") is { Length: > 0 } reference)
        {
            return ToTypeName(reference[(reference.LastIndexOf('/') + 1)..]);
        }

        if (schema["oneOf"] is JsonArray oneOf)
        {
            return UnionSchemaToType(oneOf);
        }

        if (schema["anyOf"] is JsonArray anyOf)
        {
            return UnionSchemaToType(anyOf);
        }

        if (schema["allOf"] is JsonArray allOf)
        {
            return string.Join(" & ", allOf
                .OfType<JsonObject>()
                .Select(SchemaToType)
                .Distinct(StringComparer.Ordinal));
        }

        string[] types = GetSchemaTypes(schema);
        if (types.Length == 1 && types[0] == "null")
        {
            return "null";
        }

        string result = types.FirstOrDefault(type => type != "null") switch
        {
            "array" => $"{SchemaToType(schema["items"] as JsonObject)}[]",
            "boolean" => "boolean",
            "integer" => "number",
            "number" => "number",
            "string" => "string",
            "object" when TryGetProperties(schema, out var properties) => InlineObjectType(properties, GetRequiredProperties(schema)),
            "object" => "Record<string, unknown>",
            _ when TryGetProperties(schema, out var properties) => InlineObjectType(properties, GetRequiredProperties(schema)),
            _ => "unknown"
        };

        return types.Contains("null", StringComparer.Ordinal) && result != "unknown"
            ? $"{result} | null"
            : result;
    }

    private static bool TryGetEnumType(JsonObject schema, out string type)
    {
        type = string.Empty;
        if (schema["enum"] is not JsonArray values || values.Count == 0)
        {
            return false;
        }

        var literals = values
            .Select(value => value switch
            {
                JsonValue jsonValue when jsonValue.TryGetValue<string>(out string? text) =>
                    JsonSerializer.Serialize(text),
                JsonValue jsonValue when jsonValue.TryGetValue<bool>(out bool flag) =>
                    flag ? "true" : "false",
                JsonValue jsonValue when jsonValue.TryGetValue<decimal>(out decimal number) =>
                    number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => null
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (literals.Length == 0)
        {
            return false;
        }

        type = string.Join(" | ", literals);
        return true;
    }

    private static string UnionSchemaToType(JsonArray schemas)
    {
        string[] types = schemas
            .OfType<JsonObject>()
            .Select(SchemaToType)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return types.Length == 0 ? "unknown" : string.Join(" | ", types);
    }

    private static string[] GetSchemaTypes(JsonObject schema)
    {
        return schema["type"] switch
        {
            JsonValue value when value.TryGetValue<string>(out string? type) && !string.IsNullOrWhiteSpace(type) =>
                [type],
            JsonArray array => array
                .OfType<JsonValue>()
                .Select(value => value.TryGetValue<string>(out string? type) ? type : null)
                .Where(type => !string.IsNullOrWhiteSpace(type))
                .Select(type => type!)
                .ToArray(),
            _ => []
        };
    }

    private static string InlineObjectType(JsonObject properties, HashSet<string> required)
    {
        var parts = properties
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item =>
            {
                string optional = required.Contains(item.Key) ? string.Empty : "?";
                return $"{FormatPropertyName(item.Key)}{optional}: {SchemaToType(item.Value as JsonObject)}";
            });

        return $"{{ {string.Join("; ", parts)} }}";
    }
}
