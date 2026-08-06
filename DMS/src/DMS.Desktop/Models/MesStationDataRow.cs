using DMS.Integration.Mes.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DMS.Desktop.Models;

public sealed class MesStationDataRow : INotifyPropertyChanged
{
    private string _statusIcon = "○";
    private string _statusText = "Unknown";
    private string _value = string.Empty;
    private string _quality = "Unknown";
    private string _errorMessage = string.Empty;
    private string _checkedAtText = string.Empty;
    private string _responseTimeText = string.Empty;
    private string _searchText = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string StationCode { get; set; } = string.Empty;
    public string StationName { get; set; } = string.Empty;
    public string WorkCenter { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string PointName { get; set; } = string.Empty;
    public string PointDisplayName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsOnline { get; private set; }
    public bool IsOk { get; private set; }

    public string StatusIcon
    {
        get => _statusIcon;
        private set => SetField(ref _statusIcon, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string Value
    {
        get => _value;
        private set => SetField(ref _value, value);
    }

    public string Quality
    {
        get => _quality;
        private set => SetField(ref _quality, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    public string CheckedAtText
    {
        get => _checkedAtText;
        private set => SetField(ref _checkedAtText, value);
    }

    public string ResponseTimeText
    {
        get => _responseTimeText;
        private set => SetField(ref _responseTimeText, value);
    }

    public string SearchText
    {
        get => _searchText;
        private set => SetField(ref _searchText, value);
    }

    public void ApplySnapshot(MesStationSnapshot snapshot, MesStationDataPointValue point)
    {
        StationCode = snapshot.StationCode;
        StationName = snapshot.DisplayName;
        WorkCenter = snapshot.WorkCenter;
        Protocol = snapshot.Protocol;
        Host = snapshot.Host;
        Port = snapshot.Port;
        PointName = point.Name;
        PointDisplayName = point.DisplayName;
        Address = point.Address;
        DataType = point.DataType;
        Role = point.Role;
        Value = point.Value;
        Quality = point.Quality;
        ErrorMessage = string.IsNullOrWhiteSpace(point.ErrorMessage) ? snapshot.ErrorMessage : point.ErrorMessage;
        IsOnline = snapshot.IsOnline;
        IsOk = point.IsOk && snapshot.IsOnline;
        StatusText = IsOk ? "OK" : snapshot.State;
        StatusIcon = IsOk ? "●" : "○";
        CheckedAtText = snapshot.CheckedAt.ToString("dd.MM.yyyy HH:mm:ss");
        ResponseTimeText = snapshot.ResponseTimeMs.HasValue ? $"{snapshot.ResponseTimeMs.Value} ms" : string.Empty;
        SearchText = $"{StationCode} {StationName} {WorkCenter} {Protocol} {Host} {PointName} {PointDisplayName} {Address} {Role} {Value} {Quality} {ErrorMessage}".ToLowerInvariant();

        OnPropertyChanged(nameof(StationCode));
        OnPropertyChanged(nameof(StationName));
        OnPropertyChanged(nameof(WorkCenter));
        OnPropertyChanged(nameof(Protocol));
        OnPropertyChanged(nameof(Host));
        OnPropertyChanged(nameof(Port));
        OnPropertyChanged(nameof(PointName));
        OnPropertyChanged(nameof(PointDisplayName));
        OnPropertyChanged(nameof(Address));
        OnPropertyChanged(nameof(DataType));
        OnPropertyChanged(nameof(Role));
        OnPropertyChanged(nameof(IsOnline));
        OnPropertyChanged(nameof(IsOk));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
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
