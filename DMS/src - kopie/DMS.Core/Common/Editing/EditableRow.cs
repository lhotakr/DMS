using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DMS.Core.Common.Editing;

public sealed class EditableRow<T> : INotifyPropertyChanged
{
    private EditableRowState _state;

    public EditableRow(
        T item,
        EditableRowState state = EditableRowState.Unchanged)
    {
        Item = item;
        _state = state;
    }

    public T Item { get; }

    public EditableRowState State
    {
        get => _state;
        set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDeleted));
            OnPropertyChanged(nameof(IsChanged));
        }
    }

    public bool IsDeleted =>
        State == EditableRowState.Deleted;

    public bool IsChanged =>
        State != EditableRowState.Unchanged;

    public void MarkModified()
    {
        if (State == EditableRowState.Unchanged)
        {
            State = EditableRowState.Modified;
        }
    }

    public void MarkDeleted()
    {
        State = State == EditableRowState.Added
            ? EditableRowState.Deleted
            : EditableRowState.Deleted;
    }

    public void AcceptChanges()
    {
        State = EditableRowState.Unchanged;
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