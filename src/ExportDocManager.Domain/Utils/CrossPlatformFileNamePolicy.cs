using System.Buffers;
using System.Text;

namespace ExportDocManager.Utils
{
    public static class CrossPlatformFileNamePolicy
    {
        private static readonly SearchValues<char> InvalidCharacters =
            SearchValues.Create("<>:\"/\\|?*");
        private static readonly string[] ReservedDeviceNames =
        [
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        ];

        public static bool ContainsInvalidCharacters(ReadOnlySpan<char> value)
        {
            foreach (char character in value)
            {
                if (IsInvalidCharacter(character))
                {
                    return true;
                }
            }
            return false;
        }

        public static string ReplaceInvalidCharacters(string value, char replacement)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value ?? string.Empty;
            }
            string normalized = value.Normalize(NormalizationForm.FormC);
            if (!ContainsInvalidCharacters(normalized) && string.Equals(normalized, value, StringComparison.Ordinal))
            {
                return value;
            }

            return string.Create(normalized.Length, (Value: normalized, Replacement: replacement), static (output, state) =>
            {
                for (int index = 0; index < state.Value.Length; index++)
                {
                    char character = state.Value[index];
                    output[index] = IsInvalidCharacter(character) ? state.Replacement : character;
                }
            });
        }

        /// <summary>
        /// Normalizes one portable file-name component independently of the host OS.
        /// A name accepted on Linux must remain safe when the same data is opened on Windows.
        /// </summary>
        public static string SanitizeFileNamePart(
            string? value,
            char replacement = '_',
            string fallback = "file")
        {
            string normalized = ReplaceInvalidCharacters(value ?? string.Empty, replacement)
                .Trim(' ', '.');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return fallback;
            }

            return IsReservedDeviceName(normalized)
                ? replacement + normalized
                : normalized;
        }

        public static bool IsSafeFileName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.Normalize(NormalizationForm.FormC);
            return !ContainsInvalidCharacters(normalized)
                && string.Equals(normalized, normalized.Trim(' ', '.'), StringComparison.Ordinal)
                && !IsReservedDeviceName(normalized);
        }

        public static bool IsReservedDeviceName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmed = value.Trim(' ', '.');
            int extensionSeparator = trimmed.IndexOf('.', StringComparison.Ordinal);
            string baseName = extensionSeparator >= 0
                ? trimmed[..extensionSeparator]
                : trimmed;
            baseName = baseName.Trim(' ', '.');
            return ReservedDeviceNames.Any(name =>
                string.Equals(name, baseName, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsInvalidCharacter(char character) =>
            char.IsControl(character) || InvalidCharacters.Contains(character);
    }
}
