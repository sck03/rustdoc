using System.Text;

namespace ExportDocManager.Services.MasterData;

internal static class HsCodeSearchTextNormalizer
{
    private static readonly (string Source, string Replacement)[] Synonyms =
    [
        ("T-SHIRT", "T恤衫"), ("WOMEN'S", "女式"), ("KNITTED", "针织"),
        ("TSHIRT", "T恤衫"), ("WOMENS", "女式"), ("COTTON", "棉"),
        ("MEN'S", "男式"), ("MENS", "男式"), ("针织物", "针织"),
        ("T恤", "T恤衫"), ("男士", "男式"), ("男款", "男式"),
        ("女士", "女式"), ("女款", "女式"), ("全棉", "100%棉"), ("纯棉", "100%棉")
    ];

    private static readonly (string Source, string Related)[] RelatedTerms =
    [
        ("针织", "钩编"),
        ("钩编", "针织")
    ];

    public static string Normalize(string value)
    {
        string normalized = (value ?? string.Empty).Normalize(NormalizationForm.FormKC).Trim().ToUpperInvariant();
        if (normalized.Length == 0) return string.Empty;

        var output = new StringBuilder(normalized.Length);
        for (int sourceIndex = 0; sourceIndex < normalized.Length;)
        {
            int replacementIndex = FindReplacement(normalized.AsSpan(sourceIndex));
            if (replacementIndex >= 0)
            {
                output.Append(Synonyms[replacementIndex].Replacement);
                sourceIndex += Synonyms[replacementIndex].Source.Length;
                continue;
            }

            char character = normalized[sourceIndex++];
            if (IsSearchCharacter(character)) output.Append(character);
        }
        return output.ToString();
    }

    public static HashSet<string> BuildNgrams(string normalized)
    {
        var grams = new HashSet<string>(StringComparer.Ordinal);
        if (normalized.Length <= 2)
        {
            if (normalized.Length > 0) grams.Add(normalized);
            return grams;
        }
        for (int index = 0; index < normalized.Length - 1; index++) grams.Add(normalized.Substring(index, 2));
        for (int index = 0; index < normalized.Length - 2; index++) grams.Add(normalized.Substring(index, 3));
        return grams;
    }

    public static string FindRelatedToken(string normalizedQuery)
    {
        foreach (var pair in RelatedTerms)
            if (normalizedQuery.Contains(pair.Source, StringComparison.Ordinal)) return pair.Related;
        return string.Empty;
    }

    public static bool HasRelatedMatch(string query, string candidate)
    {
        foreach (var pair in RelatedTerms)
            if (query.Contains(pair.Source, StringComparison.Ordinal) && candidate.Contains(pair.Related, StringComparison.Ordinal)) return true;
        return false;
    }

    private static int FindReplacement(ReadOnlySpan<char> source)
    {
        for (int index = 0; index < Synonyms.Length; index++)
        {
            var replacement = Synonyms[index];
            bool alreadyNormalized = replacement.Replacement.StartsWith(replacement.Source, StringComparison.Ordinal) &&
                source.StartsWith(replacement.Replacement, StringComparison.Ordinal);
            if (!alreadyNormalized && source.StartsWith(replacement.Source, StringComparison.Ordinal)) return index;
        }
        return -1;
    }

    private static bool IsSearchCharacter(char character) =>
        char.IsLetterOrDigit(character) || character is >= '\u4e00' and <= '\u9fff' || character == '%';
}
