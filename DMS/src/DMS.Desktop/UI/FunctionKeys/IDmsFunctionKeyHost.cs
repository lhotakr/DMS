namespace DMS.Desktop.UI.FunctionKeys;

public interface IDmsFunctionKeyHost
{
    IReadOnlyList<DmsFunctionKeyAction> GetFunctionKeyActions();
}
