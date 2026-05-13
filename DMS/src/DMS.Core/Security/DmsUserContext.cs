namespace DMS.Core.Security;

/// <summary>
/// Aktuální bezpečnostní kontext aplikace.
/// Obsahuje přihlášeného uživatele a jeho role.
/// </summary>
public sealed class DmsUserContext
{
    public string WindowsLogin { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();

    public bool HasRole(string role)
    {
        return Roles.Any(item =>
            string.Equals(item, role, StringComparison.OrdinalIgnoreCase));
    }

    public bool HasAnyRole(IEnumerable<string> roles)
    {
        return roles.Any(HasRole);
    }
}