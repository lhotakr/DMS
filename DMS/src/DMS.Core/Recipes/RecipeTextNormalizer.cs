using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DMS.Core.Recipes;

public static partial class RecipeTextNormalizer
{
    [GeneratedRegex(@"^(?<family>[A-Za-z]+)(?<key>[0-9].*)$", RegexOptions.Compiled)]
    private static partial Regex ScreenPrintCodeRegex();

    public static (string Family, string Key)? TrySplitScreenPrintCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var compact = Regex.Replace(value.Trim(), @"\s+", string.Empty);
        var match = ScreenPrintCodeRegex().Match(compact);

        if (!match.Success)
        {
            return null;
        }

        return (
            match.Groups["family"].Value.ToUpperInvariant(),
            NormalizeCompact(match.Groups["key"].Value));
    }

    public static string NormalizeCompact(string? value)
    {
        return string.Concat(
            NormalizeText(value)
                .Where(char.IsLetterOrDigit));
    }

    public static string NormalizeTokenSignature(string? value)
    {
        var tokens = Tokenize(value)
            .OrderBy(token => token, StringComparer.Ordinal)
            .ToArray();

        return string.Join("|", tokens);
    }

    public static IReadOnlyList<string> Tokenize(string? value)
    {
        var normalized = NormalizeText(value);

        return Regex
            .Split(normalized, @"[^A-Z0-9]+")
            .Where(token => token.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value
            .Trim()
            .ToUpperInvariant()
            .Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder
            .ToString()
            .Normalize(NormalizationForm.FormC);
    }
}
