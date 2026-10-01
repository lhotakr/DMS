using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DMS.Desktop.Views.Scheduling;

public sealed class Job10SelectionOption : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Code { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
