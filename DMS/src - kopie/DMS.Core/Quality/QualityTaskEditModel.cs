using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DMS.Core.Quality;

public sealed class QualityTaskEditModel : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private DateTime? _dueDate;
    private DateTime? _completedAt;
    private DateTime? _createdAt;
    private string _createdBy = string.Empty;
    private string _completedBy = string.Empty;

    public DateTime? CreatedAt
    {
        get => _createdAt;
        set
        {
            if (_createdAt == value) return;
            _createdAt = value;
            OnPropertyChanged();
        }
    }

    public string CreatedBy
    {
        get => _createdBy;
        set
        {
            if (string.Equals(_createdBy, value, StringComparison.Ordinal)) return;
            _createdBy = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public string CompletedBy
    {
        get => _completedBy;
        set
        {
            if (string.Equals(_completedBy, value, StringComparison.Ordinal)) return;
            _completedBy = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public int Number { get; set; }

    public string Text
    {
        get => _text;
        set
        {
            var newValue = value ?? string.Empty;

            if (string.Equals(_text, newValue, StringComparison.Ordinal))
            {
                return;
            }

            _text = newValue;

            OnPropertyChanged();
        }
    }

    public DateTime? DueDate
    {
        get => _dueDate;
        set
        {
            if (_dueDate == value)
            {
                return;
            }

            _dueDate = value;
            OnPropertyChanged();
        }
    }

    public DateTime? CompletedAt
    {
        get => _completedAt;
        set
        {
            if (_completedAt == value)
            {
                return;
            }

            _completedAt = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCompleted));
        }
    }

    public bool IsCompleted
    {
        get => CompletedAt.HasValue;
        set
        {
            if (value == IsCompleted) return;

            if (value)
            {
                CompletedAt = DateTime.Today;

                if (string.IsNullOrWhiteSpace(CompletedBy))
                {
                    CompletedBy = Environment.UserName;
                }
            }
            else
            {
                CompletedAt = null;
                CompletedBy = string.Empty;
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }

}