using System.Text.RegularExpressions;

namespace DMS.Core.Sap;

public static class SapPackagingTextParser
{
    private static readonly Regex OldReferenceRegex = new(
        @"^VerBGr\s+(?<oldNumber>\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SapReferenceRegex = new(
        @"^Verpack\.Baugruppe\s+Mat\.\s+(?<sapNumber>\d{10})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static PackagingInfo Parse(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return new PackagingInfo
            {
                PackagingKind = "PackagingComponent"
            };
        }

        var text = description.Trim();

        var sapMatch = SapReferenceRegex.Match(text);

        if (sapMatch.Success)
        {
            return new PackagingInfo
            {
                PackagingKind = "PackagingSetSapReference",
                LinkedArticleSapNumber = sapMatch.Groups["sapNumber"].Value
            };
        }

        var oldMatch = OldReferenceRegex.Match(text);

        if (oldMatch.Success)
        {
            return new PackagingInfo
            {
                PackagingKind = "PackagingSetOldReference",
                LinkedArticleOldNumber = oldMatch.Groups["oldNumber"].Value
            };
        }

        return new PackagingInfo
        {
            PackagingKind = "PackagingComponent"
        };
    }
}