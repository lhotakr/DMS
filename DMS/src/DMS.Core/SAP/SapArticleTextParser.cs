namespace DMS.Core.Sap;

public static class SapArticleTextParser
{
    public static GlassArticleTextInfo? TryParseGlassArticleText(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var parts = description.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 4)
        {
            return null;
        }

        var moldNumber = parts[0];
        var glassTypeNumber = parts[1];
        var volumeText = parts[2];
        var decorationChain = parts[3];

        int? volumeMl = null;

        if (int.TryParse(volumeText, out var parsedVolume))
        {
            volumeMl = parsedVolume;
        }

        var remainingDescription = parts.Length > 4
            ? string.Join(' ', parts.Skip(4))
            : string.Empty;

        return new GlassArticleTextInfo
        {
            MoldNumber = moldNumber,
            GlassTypeNumber = glassTypeNumber,
            VolumeMl = volumeMl,
            DecorationChain = decorationChain,
            DecorationSteps = decorationChain
                .Select(character => character.ToString())
                .ToList(),
            RemainingDescription = remainingDescription
        };
    }
}