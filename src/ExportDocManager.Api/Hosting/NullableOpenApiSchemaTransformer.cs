using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Text.Json.Serialization.Metadata;

namespace ExportDocManager.Api.Hosting;

/// <summary>
/// Makes the official OpenAPI schema reflect the enabled C# nullable contract.
/// System.Text.Json emits nullability but does not mark ordinary DTO properties
/// as JSON-Schema required unless they use the C# required modifier.
/// </summary>
internal sealed class NullableOpenApiSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Request schemas already express their write contract through constructor
        // parameters, required members and nullable annotations.  A response DTO,
        // however, serializes every readable non-nullable property, so expose that
        // stronger contract only when the schema is not being built for a bound
        // endpoint parameter.
        if (schema.Properties is not { Count: > 0 }) return Task.CompletedTask;

        bool responseSchema = context.ParameterDescription is null;
        if (responseSchema) schema.Required ??= new HashSet<string>(StringComparer.Ordinal);

        foreach (JsonPropertyInfo propertyInfo in context.JsonTypeInfo.Properties)
        {
            if (propertyInfo.Get is null || !schema.Properties.ContainsKey(propertyInfo.Name))
            {
                continue;
            }

            if (propertyInfo.IsGetNullable)
            {
                schema.Properties[propertyInfo.Name] = AllowNull(schema.Properties[propertyInfo.Name]);
                if (responseSchema)
                {
                    schema.Required!.Remove(propertyInfo.Name);
                }
            }
            else if (responseSchema)
            {
                schema.Required!.Add(propertyInfo.Name);
            }
        }

        return Task.CompletedTask;
    }

    private static IOpenApiSchema AllowNull(IOpenApiSchema propertySchema)
    {
        if (IncludesNull(propertySchema))
        {
            return propertySchema;
        }

        if (propertySchema is OpenApiSchemaReference)
        {
            return new OpenApiSchema
            {
                OneOf =
                [
                    new OpenApiSchema { Type = JsonSchemaType.Null },
                    propertySchema
                ]
            };
        }

        if (propertySchema is OpenApiSchema schema)
        {
            if (schema.OneOf is { Count: > 0 })
            {
                schema.OneOf.Add(new OpenApiSchema { Type = JsonSchemaType.Null });
            }
            else if (schema.AnyOf is { Count: > 0 })
            {
                schema.AnyOf.Add(new OpenApiSchema { Type = JsonSchemaType.Null });
            }
            else
            {
                schema.Type = schema.Type.GetValueOrDefault() | JsonSchemaType.Null;
            }
        }

        return propertySchema;
    }

    private static bool IncludesNull(IOpenApiSchema schema) =>
        (schema.Type.GetValueOrDefault() & JsonSchemaType.Null) != 0 ||
        schema.OneOf?.Any(IncludesNull) == true ||
        schema.AnyOf?.Any(IncludesNull) == true;

}
