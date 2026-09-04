namespace DMS.Desktop.WorkLog;

public sealed class WorkLogAccessPolicy
{
    private readonly WorkLogUser? _currentUser;
    private readonly bool _isDmsAdmin;

    public WorkLogAccessPolicy(
        WorkLogUser? currentUser,
        bool isDmsAdmin)
    {
        _currentUser = currentUser;
        _isDmsAdmin = isDmsAdmin;
    }

    public WorkLogUser? CurrentUser => _currentUser;

    public bool IsAdministrator =>
        _isDmsAdmin ||
        (_currentUser is not null &&
         !_currentUser.IsArchived &&
         _currentUser.LevelOfAccess >= 3);

    public bool CanUseDashboard =>
        _currentUser is not null &&
        !_currentUser.IsArchived &&
        (_currentUser.LevelOfAccess > 0 || _isDmsAdmin);

    public bool CanEditUser(WorkLogUser target)
    {
        if (target.IsArchived)
        {
            return false;
        }

        if (IsAdministrator)
        {
            return true;
        }

        if (_currentUser is null)
        {
            return false;
        }

        return target.Id == _currentUser.Id ||
               target.MasterUserId == _currentUser.Id;
    }

    public IReadOnlyList<WorkLogUser> FilterAccessibleUsers(
        IEnumerable<WorkLogUser> allUsers)
    {
        var users = allUsers
            .Where(user => !user.IsArchived)
            .ToList();

        if (IsAdministrator)
        {
            return users
                .OrderBy(user => user.Surname)
                .ThenBy(user => user.FirstName)
                .ToList();
        }

        if (_currentUser is null)
        {
            return Array.Empty<WorkLogUser>();
        }

        return users
            .Where(user =>
                user.Id == _currentUser.Id ||
                user.MasterUserId == _currentUser.Id)
            .OrderBy(user => user.Id == _currentUser.Id ? 0 : 1)
            .ThenBy(user => user.Surname)
            .ThenBy(user => user.FirstName)
            .ToList();
    }
}
