namespace DMS.Desktop.UI;

public sealed class DmsDisplayField
{
    public DmsDisplayField(
        string label,
        string? value,
        int columnSpan = 1)
    {
        Label = label;
        Value = value;
        ColumnSpan = columnSpan < 1
            ? 1
            : columnSpan;
    }

    public string Label { get; }

    public string? Value { get; }

    public int ColumnSpan { get; }
}