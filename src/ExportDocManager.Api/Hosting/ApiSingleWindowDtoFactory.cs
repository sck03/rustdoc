using System.Reflection;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiSingleWindowDtoFactory
    {
        private static readonly Type[] ScalarTypes =
        [
            typeof(string),
            typeof(int),
            typeof(bool),
            typeof(DateTime)
        ];

        public static IReadOnlyList<string> NormalizeGroupKeys(IEnumerable<string> groupKeys)
        {
            return (groupKeys ?? Enumerable.Empty<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static void CopyScalarProperties<TSource, TTarget>(TSource source, TTarget target)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(target);

            var targetProperties = typeof(TTarget)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.CanWrite && IsScalarType(property.PropertyType))
                .ToDictionary(property => property.Name, StringComparer.Ordinal);

            foreach (var sourceProperty in typeof(TSource)
                         .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                         .Where(property => property.CanRead && IsScalarType(property.PropertyType)))
            {
                if (!targetProperties.TryGetValue(sourceProperty.Name, out var targetProperty) ||
                    !targetProperty.PropertyType.IsAssignableFrom(sourceProperty.PropertyType))
                {
                    continue;
                }

                object value = sourceProperty.GetValue(source);
                if (targetProperty.PropertyType == typeof(string) && value == null)
                {
                    value = string.Empty;
                }

                targetProperty.SetValue(target, value);
            }
        }

        private static bool IsScalarType(Type type)
        {
            return ScalarTypes.Contains(Nullable.GetUnderlyingType(type) ?? type);
        }
    }
}
