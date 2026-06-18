using DMS.Desktop.Configuration.Editing;

namespace DMS.Desktop.Configuration.Roles;

public sealed class DmsRoleDefinition : EditableRowBase
{
    private string _code = string.Empty;
    private string _name = string.Empty;
    private string _description = string.Empty;
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

    public bool IsActive
    {
        get => _isActive;
        set => SetValue(ref _isActive, value);
    }
}