using System;

namespace DMS.Integration.Mes.Reporting;

public sealed class MesBonusReportRecord
{
    public string OrderCode { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string SapNumber { get; set; } = string.Empty;
    public string OperationCode { get; set; } = string.Empty;
    public string WorkcenterCode { get; set; } = string.Empty;
    public string ShiftCode { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
    public string HumanCode { get; set; } = string.Empty;
    public DateTime? LoginFrom { get; set; }
    public DateTime? LoginTo { get; set; }
    public double NetShiftDurationMinutes { get; set; }
    public double GrossProduction { get; set; }
    public double PrintedNet { get; set; }
}