using System.Text;
using System.Text.Json.Nodes;

internal static partial class TypeScriptClientGenerator
{
    private static void EmitSchemaDefaults(StringBuilder builder, JsonObject? schemas)
    {
        if (schemas == null) return;
        foreach (var schema in schemas.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (schema.Value is not JsonObject definition || !TryGetProperties(definition, out var properties)) continue;
            var defaults = properties.Where(property => property.Value is JsonObject value && value.ContainsKey("default"))
                .OrderBy(property => property.Key, StringComparer.Ordinal).ToArray();
            if (defaults.Length == 0) continue;
            string typeName = ToTypeName(schema.Key);
            builder.AppendLine($"export const {typeName}Defaults = {{");
            foreach (var property in defaults)
                builder.AppendLine($"  {FormatPropertyName(property.Key)}: {property.Value!["default"]?.ToJsonString() ?? "null"},");
            builder.AppendLine($"}} as const satisfies Partial<{typeName}>;");
            builder.AppendLine();
        }
    }
}
