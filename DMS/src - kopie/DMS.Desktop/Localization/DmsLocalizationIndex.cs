namespace DMS.Desktop.Localization;

public sealed class DmsLocalizationIndex
{
    public string DefaultCulture { get; set; } = "en-US";
    public List<DmsSupportedCulture> SupportedCultures { get; set; } = new();
}