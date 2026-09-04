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

    /// <summary>
    /// Volitelná vazba na centrální registr osob v Core Master Data.
    /// Starší users.json bez této vlastnosti zůstávají kompatibilní.
    /// </summary>
    public Guid? PersonId { get; init; }

    public bool IsActive { get; init; } = true;

    public List<string> Roles { get; init; } = new();
}