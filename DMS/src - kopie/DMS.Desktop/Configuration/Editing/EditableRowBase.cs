using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace DMS.Desktop.Configuration.Editing;

public abstract class EditableRowBase : INotifyPropertyChanged
{
    private string _state = "Unchanged";

    [JsonIgnore]
    public string State
    {
        get => _state;
        private set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDeleted));
            OnPropertyChanged(nameof(HasChanges));
        }
    }

    [JsonIgnore]
    public bool IsDeleted => State == "Deleted";

    [JsonIgnore]
    public bool HasChanges => State != "Unchanged";

    protected bool SetValue<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;

        if (State == "Unchanged")
        {
            State = "Modified";
        }

        OnPropertyChanged(propertyName);
        return true;
    }

    public void MarkAdded()
    {
        State = "Added";
    }

    public void MarkModified()
    {
        if (State == "Unchanged")
        {
            State = "Modified";
        }
    }

    public void MarkDeleted()
    {
        State = "Deleted";
    }

    public void MarkUnchanged()
    {
        State = "Unchanged";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}