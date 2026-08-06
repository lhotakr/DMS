using DMS.Core.Mes;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DMS.Desktop.Views.Mes;

public sealed class MesDataPointDisplayRow : INotifyPropertyChanged
{
    private string _logicalSignal = string.Empty;
    private string _displayName = string.Empty;
    private string _moduleType = string.Empty;
    private int _slot;
    private int _channel;
    private string _sourceText = string.Empty;
    private string _valueText = string.Empty;
    private string _qualityText = string.Empty;
    private string _readAtText = string.Empty;
    private string _changedAtText = string.Empty;
    private string _error = string.Empty;
    private object? _rawValue;
    private DateTimeOffset? _changedAt;
    private bool _wasChangedInLastRead;

    public string Code { get; init; } = string.Empty;

    public string LogicalSignal
    {
        get => _logicalSignal;
        private set => SetField(ref _logicalSignal, value);
    }

    public string DisplayName
    {
        get => _displayName;
        private set => SetField(ref _displayName, value);
    }

    public string ModuleType
    {
        get => _moduleType;
        private set => SetField(ref _moduleType, value);
    }

    public int Slot
    {
        get => _slot;
        private set => SetField(ref _slot, value);
    }

    public int Channel
    {
        get => _channel;
        private set => SetField(ref _channel, value);
    }

    public string PhysicalPointText => $"S{Slot}/CH{Channel}";

    public string SourceText
    {
        get => _sourceText;
        private set => SetField(ref _sourceText, value);
    }

    public string ValueText
    {
        get => _valueText;
        private set => SetField(ref _valueText, value);
    }

    public string QualityText
    {
        get => _qualityText;
        private set => SetField(ref _qualityText, value);
    }

    public string ReadAtText
    {
        get => _readAtText;
        private set => SetField(ref _readAtText, value);
    }

    public string ChangedAtText
    {
        get => _changedAtText;
        private set => SetField(ref _changedAtText, value);
    }

    public string Error
    {
        get => _error;
        private set => SetField(ref _error, value);
    }

    public object? RawValue
    {
        get => _rawValue;
        private set => SetField(ref _rawValue, value);
    }

    public bool WasChangedInLastRead
    {
        get => _wasChangedInLastRead;
        private set => SetField(ref _wasChangedInLastRead, value);
    }

    public bool IsActive => RawValue switch
    {
        bool booleanValue => booleanValue,
        byte byteValue => byteValue != 0,
        short shortValue => shortValue != 0,
        ushort ushortValue => ushortValue != 0,
        int intValue => intValue != 0,
        uint uintValue => uintValue != 0,
        long longValue => longValue != 0,
        ulong ulongValue => ulongValue != 0,
        float floatValue => Math.Abs(floatValue) > float.Epsilon,
        double doubleValue => Math.Abs(doubleValue) > double.Epsilon,
        _ => false
    };

    public string DetailText =>
        $"Code: {Code}\n" +
        $"Logical signal: {LogicalSignal}\n" +
        $"Meaning: {DisplayName}\n" +
        $"Module: {ModuleType}\n" +
        $"Physical point: {PhysicalPointText}\n" +
        $"Modbus source: {SourceText}\n" +
        $"Value: {ValueText}\n" +
        $"Quality: {QualityText}\n" +
        $"Last read: {ReadAtText}\n" +
        $"Last change: {ChangedAtText}\n" +
        $"Error: {Error}";

    public void UpdateFrom(
        MesDataPointValue value,
        string? displayNameOverride = null,
        string? qualityTextOverride = null,
        string? sourceTextOverride = null,
        string? valueTextOverride = null)
    {
        var previousComparable = NormalizeComparable(RawValue);
        var nextComparable = NormalizeComparable(value.RawValue);
        var hadPreviousValue = RawValue is not null;

        LogicalSignal = value.LogicalSignal;
        DisplayName = string.IsNullOrWhiteSpace(displayNameOverride)
            ? value.DisplayName
            : displayNameOverride;
        ModuleType = value.ModuleType;
        Slot = value.Slot;
        Channel = value.Channel;
        SourceText = string.IsNullOrWhiteSpace(sourceTextOverride)
            ? value.SourceText
            : sourceTextOverride;
        QualityText = string.IsNullOrWhiteSpace(qualityTextOverride)
            ? value.Quality.ToString()
            : qualityTextOverride;
        ReadAtText = value.ReadAt == default
            ? string.Empty
            : value.ReadAt.ToString("HH:mm:ss.fff");
        Error = value.Error;

        WasChangedInLastRead = hadPreviousValue &&
                               !string.Equals(
                                   previousComparable,
                                   nextComparable,
                                   StringComparison.Ordinal);

        RawValue = value.RawValue;
        ValueText = string.IsNullOrWhiteSpace(valueTextOverride)
            ? value.DisplayValue
            : valueTextOverride;

        if (!hadPreviousValue || WasChangedInLastRead)
        {
            _changedAt = value.ReadAt;
        }

        ChangedAtText = _changedAt.HasValue
            ? _changedAt.Value.ToString("HH:mm:ss.fff")
            : string.Empty;

        OnPropertyChanged(nameof(PhysicalPointText));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(DetailText));
    }

    private static string NormalizeComparable(object? value)
    {
        return value switch
        {
            null => "<null>",
            float floatValue => floatValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            double doubleValue => doubleValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(
                null,
                System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
