using System.Windows.Input;

namespace DMS.Desktop.UI.FunctionKeys;

public sealed class DmsFunctionKeyAction
{
    public DmsFunctionKeyAction(
        Key key,
        string label,
        Action execute,
        Func<bool>? canExecute = null)
    {
        Key = key;
        Label = label ?? string.Empty;
        Execute = execute ?? throw new ArgumentNullException(nameof(execute));
        CanExecute = canExecute;
    }

    public Key Key { get; }
    public string Label { get; }
    public Action Execute { get; }
    public Func<bool>? CanExecute { get; }

    public bool IsEnabled => CanExecute?.Invoke() ?? true;
}
