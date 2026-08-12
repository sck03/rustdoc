using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

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
        if (context.ParameterDescription is null && schema.Properties is { Count: > 0 })
        {
            schema.Required ??= new HashSet<string>(StringComparer.Ordinal);
            foreach (var propertyInfo in context.JsonTypeInfo.Properties)
            {
                if (propertyInfo.Get is not null &&
                    !propertyInfo.IsGetNullable &&
                    schema.Properties.ContainsKey(propertyInfo.Name))
                {
                    schema.Required.Add(propertyInfo.Name);
                }
            }
        }

        return Task.CompletedTask;
    }
}
