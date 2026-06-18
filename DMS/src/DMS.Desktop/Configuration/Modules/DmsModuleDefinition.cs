using DMS.Desktop.Configuration.Editing;

namespace DMS.Desktop.Configuration.Modules;

public sealed class DmsModuleDefinition : EditableRowBase
{
    private string _code = string.Empty;
    private string _name = string.Empty;
    private string _description = string.Empty;
    private int _sortOrder = 100;
    private bool _isActive = true;

    public string Code
    {
        get => _code;
        set => SetValue(ref _code, value);
    }

    public string Name
    {
        get => _name;
        set => SetValue(ref _name, value);
    }

    public string Description
    {
        get => _description;
        set => SetValue(ref _description, value);
    }

    public int SortOrder
    {
        get => _sortOrder;
        set => SetValue(ref _sortOrder, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetValue(ref _isActive, value);
    }
}