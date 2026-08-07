using System.Buffers;

namespace ExportDocManager.Utils
{
    public static class CrossPlatformFileNamePolicy
    {
        private static readonly SearchValues<char> InvalidCharacters =
            SearchValues.Create("<>:\"/\\|?*");

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
            if (!ContainsInvalidCharacters(value))
            {
                return value;
            }

            return string.Create(value.Length, (Value: value, Replacement: replacement), static (output, state) =>
            {
                for (int index = 0; index < state.Value.Length; index++)
                {
                    char character = state.Value[index];
                    output[index] = IsInvalidCharacter(character) ? state.Replacement : character;
                }
            });
        }

        public static bool IsInvalidCharacter(char character) =>
            char.IsControl(character) || InvalidCharacters.Contains(character);
    }
}
