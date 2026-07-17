using System.Collections.Frozen;
using System.Reflection;

namespace ExportDocManager.Utils
{
    public static class SingleWindowEditorFieldHelper
    {
        public static FrozenDictionary<string, PropertyInfo> BuildPublicStringPropertyMap(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);

            return type
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property =>
                    property.PropertyType == typeof(string) &&
                    property.CanRead &&
                    property.SetMethod?.IsPublic == true)
                .ToFrozenDictionary(property => property.Name, property => property, StringComparer.Ordinal);
        }

        public static FrozenDictionary<string, FieldInfo> BuildPrivateStringBackingFieldMap(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);

            return type
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(field =>
                    field.FieldType == typeof(string) &&
                    field.Name.StartsWith('_') &&
                    field.Name.Length >= 2)
                .ToFrozenDictionary(
                    field => char.ToUpperInvariant(field.Name[1]) + field.Name[2..],
                    field => field,
                    StringComparer.Ordinal);
        }

        public static string NormalizeGroupKey(string groupKey, string fallback)
        {
            return string.IsNullOrWhiteSpace(groupKey) ? fallback : groupKey.Trim();
        }

        public static bool ScopeMatches(string scopeKey, string expectedKey)
        {
            return string.IsNullOrWhiteSpace(scopeKey) ||
                   string.Equals(scopeKey, expectedKey, StringComparison.Ordinal);
        }

        public static string NormalizeStringValue(string value)
        {
            return value?.Trim() ?? string.Empty;
        }

        public static string GetStringPropertyValue(
            object target,
            IReadOnlyDictionary<string, PropertyInfo> propertyMap,
            string propertyName,
            Func<string, string> normalize = null)
        {
            ArgumentNullException.ThrowIfNull(propertyMap);

            if (target == null || !propertyMap.TryGetValue(propertyName, out var property))
            {
                return string.Empty;
            }

            string value = property.GetValue(target) as string;
            return normalize == null ? value ?? string.Empty : normalize(value);
        }

        public static bool SetStringPropertyValue(
            object target,
            IReadOnlyDictionary<string, PropertyInfo> propertyMap,
            string propertyName,
            string value,
            Func<string, string> normalize = null)
        {
            ArgumentNullException.ThrowIfNull(propertyMap);

            if (target == null || !propertyMap.TryGetValue(propertyName, out var property))
            {
                return false;
            }

            property.SetValue(target, normalize == null ? value ?? string.Empty : normalize(value));
            return true;
        }

        public static void AssignStringPropertyValues(
            object target,
            IReadOnlyDictionary<string, PropertyInfo> propertyMap,
            IEnumerable<string> propertyNames,
            Func<string, string> valueFactory,
            Func<string, string, string> normalize = null)
        {
            ArgumentNullException.ThrowIfNull(propertyMap);
            ArgumentNullException.ThrowIfNull(valueFactory);

            foreach (string propertyName in (propertyNames ?? Array.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal))
            {
                if (!propertyMap.TryGetValue(propertyName, out var property))
                {
                    continue;
                }

                string value = valueFactory(propertyName);
                property.SetValue(
                    target,
                    normalize == null
                        ? value ?? string.Empty
                        : normalize(propertyName, value));
            }
        }

        public static void CopyStringPropertyValues(
            object source,
            IReadOnlyDictionary<string, PropertyInfo> sourcePropertyMap,
            object target,
            IReadOnlyDictionary<string, PropertyInfo> targetPropertyMap,
            IEnumerable<string> propertyNames,
            Func<string, string, string> normalize = null)
        {
            ArgumentNullException.ThrowIfNull(sourcePropertyMap);

            AssignStringPropertyValues(
                target,
                targetPropertyMap,
                propertyNames,
                propertyName => GetStringPropertyValue(source, sourcePropertyMap, propertyName),
                normalize);
        }

        public static IReadOnlyDictionary<string, string> SnapshotStringPropertyValues(
            object target,
            IReadOnlyDictionary<string, PropertyInfo> propertyMap,
            IEnumerable<string> propertyNames,
            Func<string, string> normalize = null)
        {
            ArgumentNullException.ThrowIfNull(propertyMap);

            return (propertyNames ?? Array.Empty<string>())
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(
                    propertyName => propertyName,
                    propertyName => GetStringPropertyValue(target, propertyMap, propertyName, normalize),
                    StringComparer.Ordinal);
        }

        public static int ApplyNormalizedStringChange(
            string currentValue,
            string nextValue,
            Action<string> assign,
            Func<string, string> normalize = null)
        {
            ArgumentNullException.ThrowIfNull(assign);

            var normalizer = normalize ?? NormalizeStringValue;
            string normalizedCurrent = normalizer(currentValue);
            string normalizedNext = normalizer(nextValue);
            if (string.Equals(normalizedCurrent, normalizedNext, StringComparison.Ordinal))
            {
                return 0;
            }

            assign(normalizedNext);
            return 1;
        }

        public static int ApplyStringPropertyChange(
            object target,
            IReadOnlyDictionary<string, PropertyInfo> propertyMap,
            string propertyName,
            string nextValue,
            Func<string, string> normalize = null)
        {
            ArgumentNullException.ThrowIfNull(propertyMap);

            if (target == null || !propertyMap.TryGetValue(propertyName, out var property))
            {
                return 0;
            }

            return ApplyNormalizedStringChange(
                property.GetValue(target) as string,
                nextValue,
                value => property.SetValue(target, value),
                normalize);
        }

        public static int ClearStringPropertyValues(
            object target,
            IReadOnlyDictionary<string, PropertyInfo> propertyMap,
            IEnumerable<string> propertyNames,
            Func<string, string> normalize = null)
        {
            ArgumentNullException.ThrowIfNull(propertyMap);

            int changeCount = 0;
            foreach (string propertyName in (propertyNames ?? Array.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal))
            {
                changeCount += ApplyStringPropertyChange(
                    target,
                    propertyMap,
                    propertyName,
                    string.Empty,
                    normalize);
            }

            return changeCount;
        }

        public static bool SetPrivateStringBackingField(
            object target,
            IReadOnlyDictionary<string, FieldInfo> fieldMap,
            string propertyName,
            string value,
            Action<string> raisePropertyChanged = null)
        {
            ArgumentNullException.ThrowIfNull(fieldMap);

            if (target == null || !fieldMap.TryGetValue(propertyName, out var field))
            {
                return false;
            }

            string normalizedValue = NormalizeStringValue(value);
            string currentValue = field.GetValue(target) as string ?? string.Empty;
            if (string.Equals(currentValue, normalizedValue, StringComparison.Ordinal))
            {
                return false;
            }

            field.SetValue(target, normalizedValue);
            raisePropertyChanged?.Invoke(propertyName);
            return true;
        }
    }
}
