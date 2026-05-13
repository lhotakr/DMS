namespace DMS.Core.Security;

/// <summary>
/// Uživatel DMS navázaný na Windows login.
/// DMS neukládá hesla, pouze profil a role.
/// </summary>
public sealed class DmsUser
{
    public string WindowsLogin { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public bool IsActive { get; init; } = true;

    public List<string> Roles { get; init; } = new();
}