using DMS.Desktop.Configuration.Editing;
using System.Text.Json.Serialization;

namespace DMS.Desktop.Configuration.Transactions;

public sealed class TransactionEditorItem : EditableRowBase
{
    private string _code = string.Empty;
    private string _name = string.Empty;
    private string _module = string.Empty;
    private string _description = string.Empty;
    private string _handlerKey = string.Empty;
    private bool _requiresArticleNumber;
    private bool _isActive = true;
    private List<string> _roles = new();

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

    public string Module
    {
        get => _module;
        set => SetValue(ref _module, value);
    }

    public string Description
    {
        get => _description;
        set => SetValue(ref _description, value);
    }

    public string HandlerKey
    {
        get => _handlerKey;
        set => SetValue(ref _handlerKey, value);
    }

    public bool RequiresArticleNumber
    {
        get => _requiresArticleNumber;
        set => SetValue(ref _requiresArticleNumber, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetValue(ref _isActive, value);
    }

    public List<string> Roles
    {
        get => _roles;
        set
        {
            _roles = value ?? new List<string>();

            MarkModified();

            OnPropertyChanged();
            OnPropertyChanged(nameof(RolesText));
        }
    }

    [JsonIgnore]
    public string RolesText
    {
        get => string.Join(", ", Roles);
        set => Roles = SplitRoles(value);
    }

    private static List<string> SplitRoles(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new List<string>();
        }

        return value
            .Split(new[] { ',', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
    }
}