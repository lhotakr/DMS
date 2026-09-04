namespace DMS.Core.Sap;

public sealed class SapWorkCenter
{
    public string ObjectId { get; set; } = string.Empty;     // CRHD-OBJID
    public string WorkCenter { get; set; } = string.Empty;  // CRHD-ARBPL
    public string Plant { get; set; } = string.Empty;       // CRHD-WERKS

    public List<SapWorkCenterText> Texts { get; set; } = new();

    public string DisplayText
    {
        get
        {
            var preferredText =
                Texts.FirstOrDefault(item => item.Language.Equals("CS", StringComparison.OrdinalIgnoreCase))
                ?? Texts.FirstOrDefault(item => item.Language.Equals("EN", StringComparison.OrdinalIgnoreCase))
                ?? Texts.FirstOrDefault(item => item.Language.Equals("DE", StringComparison.OrdinalIgnoreCase))
                ?? Texts.FirstOrDefault();

            return preferredText?.Text ?? string.Empty;
        }
    }

    public DateTime ImportedAt { get; set; } = DateTime.Now;
}

public sealed class SapWorkCenterText
{
    public string Language { get; set; } = string.Empty; // CRTX-SPRAS
    public string Text { get; set; } = string.Empty;     // CRTX-KTEXT
}