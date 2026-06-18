namespace DMS.Desktop.UI;

public interface IUnsavedChangesGuard
{
    bool HasUnsavedChanges { get; }

    bool ConfirmNavigationAway();
}