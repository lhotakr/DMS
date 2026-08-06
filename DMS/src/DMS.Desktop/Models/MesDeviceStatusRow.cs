using System.ComponentModel;
using System.Runtime.CompilerServices;

using DMS.Integration.Mes.Models;

namespace DMS.Desktop.Models;

public sealed class MesDeviceStatusRow : INotifyPropertyChanged
{
    private string _statusIcon = "○";
    private string _statusText = "Unknown";
    private string _failureReason = string.Empty;
    private string _responseTimeText = string.Empty;
    private string _checkedAtText = string.Empty;
    private string _searchText = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Address { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public int SourceLineNumber { get; set; }
    public bool IsOnline { get; private set; }
    public string State { get; private set; } = "Unknown";
    public long? ResponseTimeMs { get; private set; }
    public DateTime? CheckedAt { get; private set; }

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

    public string FailureReason
    {
        get => _failureReason;
        private set => SetField(ref _failureReason, value);
    }

    public string ResponseTimeText
    {
        get => _responseTimeText;
        private set => SetField(ref _responseTimeText, value);
    }

    public string CheckedAtText
    {
        get => _checkedAtText;
        private set => SetField(ref _checkedAtText, value);
    }

    public string SearchText
    {
        get => _searchText;
        private set => SetField(ref _searchText, value);
    }

    public void ApplyDevice(MesDevice device)
    {
        Address = device.Address;
        Category = device.Category;
        Name = device.Name;
        Note = device.Note;
        SourceLineNumber = device.SourceLineNumber;
        SearchText = $"{Address} {Category} {Name} {Note}".ToLowerInvariant();

        OnPropertyChanged(nameof(Address));
        OnPropertyChanged(nameof(Category));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Note));
        OnPropertyChanged(nameof(SourceLineNumber));
        OnPropertyChanged(nameof(SearchText));
    }

    public void ApplyResult(MesProbeResult result)
    {
        State = result.State;
        IsOnline = result.IsOnline;
        ResponseTimeMs = result.ResponseTimeMs;
        CheckedAt = result.CheckedAt;
        FailureReason = result.FailureReason;

        if (result.IsOnline)
        {
            StatusIcon = "●";
            StatusText = "Online";
        }
        else if (string.Equals(result.State, "Offline", StringComparison.OrdinalIgnoreCase))
        {
            StatusIcon = "●";
            StatusText = "Offline";
        }
        else
        {
            StatusIcon = "○";
            StatusText = result.State;
        }

        ResponseTimeText = result.ResponseTimeMs.HasValue
            ? $"{result.ResponseTimeMs.Value} ms"
            : string.Empty;

        CheckedAtText = result.CheckedAt.ToString("dd.MM.yyyy HH:mm:ss");

        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsOnline));
        OnPropertyChanged(nameof(ResponseTimeMs));
        OnPropertyChanged(nameof(CheckedAt));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(FailureReason));
    }

    public MesMonitorSnapshotRow ToSnapshotRow()
    {
        return new MesMonitorSnapshotRow
        {
            Address = Address,
            Category = Category,
            Name = Name,
            Note = Note,
            State = State,
            IsOnline = IsOnline,
            ResponseTimeMs = ResponseTimeMs,
            FailureReason = FailureReason,
            CheckedAt = CheckedAt ?? DateTime.MinValue
        };
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
