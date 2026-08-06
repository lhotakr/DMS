using System.ComponentModel;
using System.Runtime.CompilerServices;

using DMS.Integration.Mes.Models;

namespace DMS.Desktop.Models;

public sealed class MesDeviceEditRow : INotifyPropertyChanged
{
    private string _address = string.Empty;
    private string _category = "TERMINAL";
    private string _name = string.Empty;
    private string _note = string.Empty;
    private int _sourceLineNumber;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Address
    {
        get => _address;
        set => SetField(ref _address, value);
    }

    public string Category
    {
        get => _category;
        set => SetField(ref _category, value);
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string Note
    {
        get => _note;
        set => SetField(ref _note, value);
    }

    public int SourceLineNumber
    {
        get => _sourceLineNumber;
        set => SetField(ref _sourceLineNumber, value);
    }

    public bool IsValid => !string.IsNullOrWhiteSpace(Address);

    public MesDevice ToDevice()
    {
        return new MesDevice
        {
            Address = Address?.Trim() ?? string.Empty,
            Category = string.IsNullOrWhiteSpace(Category) ? "DEVICE" : Category.Trim().ToUpperInvariant(),
            Name = Name?.Trim() ?? string.Empty,
            Note = Note?.Trim() ?? string.Empty,
            SourceLineNumber = SourceLineNumber
        };
    }

    public static MesDeviceEditRow FromDevice(MesDevice device)
    {
        return new MesDeviceEditRow
        {
            Address = device.Address,
            Category = device.Category,
            Name = device.Name,
            Note = device.Note,
            SourceLineNumber = device.SourceLineNumber
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
        OnPropertyChanged(nameof(IsValid));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
