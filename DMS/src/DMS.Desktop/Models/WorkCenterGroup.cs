namespace DMS.Desktop.Models;

public sealed class WorkCenterGroup
{
    public string Code { get; set; } = string.Empty;
    // K14-C, K14-D, K15-4, K15-HR, LEP, B-LINE...

    public string Name { get; set; } = string.Empty;

    public string DecorationCode { get; set; } = string.Empty;
    // D, P, B, E, K...

    public int PrintStationCount { get; set; }

    public int HotStampingStationCount { get; set; }

    public bool IsActive { get; set; } = true;

    public string Note { get; set; } = string.Empty;
}