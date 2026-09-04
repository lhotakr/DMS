using Microsoft.Data.Sqlite;
using System.Globalization;
using System.IO;

namespace DMS.Desktop.WorkLog;

public sealed class WorkLogRepository
{
    private static readonly string[] RequiredTables =
    {
        "Users",
        "UserGroups",
        "Projects",
        "TimeEntries",
        "TimeEntryTypes",
        "SpecialDays",
        "ArrivalsDepartures"
    };

    public WorkLogRepository(string databasePath)
    {
        DatabasePath = NormalizePath(databasePath);
    }

    public string DatabasePath { get; }

    public void TestConnection()
    {
        using var connection = OpenConnection();
        ValidateSchema(connection);
    }

    public WorkLogDatabaseInfo GetDatabaseInfo()
    {
        using var connection = OpenConnection();
        ValidateSchema(connection);

        return new WorkLogDatabaseInfo
        {
            DatabasePath = DatabasePath,
            SqliteVersion = ExecuteScalarString(
                connection,
                "SELECT sqlite_version();"),
            FileSizeBytes = new FileInfo(DatabasePath).Length,
            Users = ExecuteScalarInt(
                connection,
                "SELECT COUNT(*) FROM Users;"),
            ActiveUsers = ExecuteScalarInt(
                connection,
                "SELECT COUNT(*) FROM Users WHERE COALESCE(IsArchived, 0) = 0;"),
            Projects = ExecuteScalarInt(
                connection,
                "SELECT COUNT(*) FROM Projects;"),
            TimeEntries = ExecuteScalarInt(
                connection,
                "SELECT COUNT(*) FROM TimeEntries;"),
            ArrivalsDepartures = ExecuteScalarInt(
                connection,
                "SELECT COUNT(*) FROM ArrivalsDepartures;"),
            LockedSpecialDays = ExecuteScalarInt(
                connection,
                "SELECT COUNT(*) FROM SpecialDays WHERE COALESCE(Locked, 0) = 1;")
        };
    }

    public IReadOnlyList<WorkLogUser> GetUsers(
        bool includeArchived = false)
    {
        using var connection = OpenConnection();

        var sql = """
            SELECT
                u.ID,
                u.FirstName,
                u.Surname,
                u.PersonalNumber,
                u.WindowsUsername,
                u.LevelOfAccess,
                u.UserGroupId,
                COALESCE(g.Title, '') AS UserGroupTitle,
                COALESCE(u.Email, '') AS Email,
                u.MasterUserID,
                COALESCE(u.IsArchived, 0) AS IsArchived
            FROM Users u
            LEFT JOIN UserGroups g ON g.Id = u.UserGroupId
            WHERE $includeArchived = 1
               OR COALESCE(u.IsArchived, 0) = 0
            ORDER BY u.Surname COLLATE NOCASE, u.FirstName COLLATE NOCASE;
            """;

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue(
            "$includeArchived",
            includeArchived ? 1 : 0);

        using var reader = command.ExecuteReader();
        var result = new List<WorkLogUser>();

        while (reader.Read())
        {
            result.Add(ReadUser(reader));
        }

        return result;
    }

    public WorkLogUser? FindUserByWindowsUsername(
        string windowsLogin)
    {
        var normalized = NormalizeWindowsLogin(windowsLogin);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return GetUsers(includeArchived: true)
            .FirstOrDefault(user =>
                string.Equals(
                    NormalizeWindowsLogin(user.WindowsUsername),
                    normalized,
                    StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<WorkLogUserGroup> GetUserGroups()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT Id, COALESCE(Title, '') AS Title
            FROM UserGroups
            ORDER BY Title COLLATE NOCASE;
            """;

        using var reader = command.ExecuteReader();
        var result = new List<WorkLogUserGroup>();

        while (reader.Read())
        {
            result.Add(new WorkLogUserGroup
            {
                Id = reader.GetInt32(0),
                Title = reader.GetString(1)
            });
        }

        return result;
    }

    public int SaveUser(WorkLogUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        if (user.Id <= 0)
        {
            command.CommandText = """
                INSERT INTO Users
                (
                    FirstName,
                    Surname,
                    PersonalNumber,
                    WindowsUsername,
                    LevelOfAccess,
                    UserGroupId,
                    Email,
                    MasterUserID,
                    IsArchived
                )
                VALUES
                (
                    $firstName,
                    $surname,
                    $personalNumber,
                    $windowsUsername,
                    $levelOfAccess,
                    $userGroupId,
                    $email,
                    $masterUserId,
                    0
                );
                SELECT last_insert_rowid();
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE Users
                SET
                    FirstName = $firstName,
                    Surname = $surname,
                    PersonalNumber = $personalNumber,
                    WindowsUsername = $windowsUsername,
                    LevelOfAccess = $levelOfAccess,
                    UserGroupId = $userGroupId,
                    Email = $email,
                    MasterUserID = $masterUserId
                WHERE ID = $id;
                SELECT $id;
                """;
            command.Parameters.AddWithValue("$id", user.Id);
        }

        command.Parameters.AddWithValue(
            "$firstName",
            user.FirstName.Trim());
        command.Parameters.AddWithValue(
            "$surname",
            user.Surname.Trim());
        command.Parameters.AddWithValue(
            "$personalNumber",
            user.PersonalNumber);
        command.Parameters.AddWithValue(
            "$windowsUsername",
            user.WindowsUsername.Trim());
        command.Parameters.AddWithValue(
            "$levelOfAccess",
            user.LevelOfAccess);
        command.Parameters.AddWithValue(
            "$userGroupId",
            DbValue(user.UserGroupId));
        command.Parameters.AddWithValue(
            "$email",
            NullIfWhiteSpace(user.Email));
        command.Parameters.AddWithValue(
            "$masterUserId",
            DbValue(user.MasterUserId));

        var id = Convert.ToInt32(
            command.ExecuteScalar(),
            CultureInfo.InvariantCulture);

        transaction.Commit();
        return id;
    }

    public void SetUserArchived(
        int userId,
        bool archived)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = """
            UPDATE Users
            SET IsArchived = $archived
            WHERE ID = $id;
            """;

        command.Parameters.AddWithValue("$id", userId);
        command.Parameters.AddWithValue(
            "$archived",
            archived ? 1 : 0);

        command.ExecuteNonQuery();
    }

    public IReadOnlyList<WorkLogProject> GetProjects(
        bool includeArchived = false)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                p.Id,
                p.ProjectType,
                p.ProjectTitle,
                p.ProjectDescription,
                p.CreatedBy,
                TRIM(COALESCE(u.Surname, '') || ' ' || COALESCE(u.FirstName, '')) AS CreatedByName,
                COALESCE(p.Note, '') AS Note,
                COALESCE(p.IsArchived, 0) AS IsArchived,
                COALESCE(p.DateFullFilled, '') AS DateFullFilled
            FROM Projects p
            LEFT JOIN Users u ON u.ID = p.CreatedBy
            WHERE $includeArchived = 1
               OR COALESCE(p.IsArchived, 0) = 0
            ORDER BY p.ProjectTitle COLLATE NOCASE, p.Id;
            """;

        command.Parameters.AddWithValue(
            "$includeArchived",
            includeArchived ? 1 : 0);

        using var reader = command.ExecuteReader();
        var result = new List<WorkLogProject>();

        while (reader.Read())
        {
            result.Add(new WorkLogProject
            {
                Id = reader.GetInt32(0),
                ProjectType = reader.GetInt32(1),
                ProjectTitle = reader.GetString(2),
                ProjectDescription = reader.GetString(3),
                CreatedBy = reader.GetInt32(4),
                CreatedByName = reader.GetString(5),
                Note = reader.GetString(6),
                IsArchived = reader.GetInt32(7) != 0,
                DateFullFilled = reader.GetString(8)
            });
        }

        return result;
    }

    public int SaveProject(WorkLogProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        if (project.Id <= 0)
        {
            command.CommandText = """
                INSERT INTO Projects
                (
                    ProjectType,
                    ProjectTitle,
                    ProjectDescription,
                    CreatedBy,
                    Note,
                    IsArchived,
                    DateFullFilled
                )
                VALUES
                (
                    $projectType,
                    $title,
                    $description,
                    $createdBy,
                    $note,
                    0,
                    $fulfilled
                );
                SELECT last_insert_rowid();
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE Projects
                SET
                    ProjectType = $projectType,
                    ProjectTitle = $title,
                    ProjectDescription = $description,
                    Note = $note,
                    DateFullFilled = $fulfilled
                WHERE Id = $id;
                SELECT $id;
                """;
            command.Parameters.AddWithValue("$id", project.Id);
        }

        command.Parameters.AddWithValue(
            "$projectType",
            project.ProjectType);
        command.Parameters.AddWithValue(
            "$title",
            project.ProjectTitle.Trim());
        command.Parameters.AddWithValue(
            "$description",
            project.ProjectDescription.Trim());
        command.Parameters.AddWithValue(
            "$createdBy",
            project.CreatedBy);
        command.Parameters.AddWithValue(
            "$note",
            NullIfWhiteSpace(project.Note));
        command.Parameters.AddWithValue(
            "$fulfilled",
            NullIfWhiteSpace(project.DateFullFilled));

        var id = Convert.ToInt32(
            command.ExecuteScalar(),
            CultureInfo.InvariantCulture);

        transaction.Commit();
        return id;
    }

    public void SetProjectArchived(
        int projectId,
        bool archived)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = """
            UPDATE Projects
            SET IsArchived = $archived
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue("$id", projectId);
        command.Parameters.AddWithValue(
            "$archived",
            archived ? 1 : 0);

        command.ExecuteNonQuery();
    }

    public IReadOnlyList<WorkLogEntryType> GetEntryTypes()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                ID,
                COALESCE(Title, '') AS Title,
                COALESCE(Color, '#ADD8E6') AS Color,
                ForProjectType
            FROM TimeEntryTypes
            ORDER BY COALESCE(ForProjectType, 0), Title COLLATE NOCASE, ID;
            """;

        using var reader = command.ExecuteReader();
        var result = new List<WorkLogEntryType>();

        while (reader.Read())
        {
            result.Add(new WorkLogEntryType
            {
                Id = reader.GetInt32(0),
                Title = reader.GetString(1),
                Color = reader.GetString(2),
                ForProjectType = reader.IsDBNull(3)
                    ? null
                    : reader.GetInt32(3)
            });
        }

        return result;
    }

    public int SaveEntryType(WorkLogEntryType entryType)
    {
        ArgumentNullException.ThrowIfNull(entryType);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        if (entryType.Id <= 0)
        {
            command.CommandText = """
                INSERT INTO TimeEntryTypes
                (
                    Title,
                    Color,
                    ForProjectType
                )
                VALUES
                (
                    $title,
                    $color,
                    $projectType
                );
                SELECT last_insert_rowid();
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE TimeEntryTypes
                SET
                    Title = $title,
                    Color = $color,
                    ForProjectType = $projectType
                WHERE ID = $id;
                SELECT $id;
                """;
            command.Parameters.AddWithValue("$id", entryType.Id);
        }

        command.Parameters.AddWithValue(
            "$title",
            entryType.Title.Trim());
        command.Parameters.AddWithValue(
            "$color",
            string.IsNullOrWhiteSpace(entryType.Color)
                ? "#ADD8E6"
                : entryType.Color.Trim());
        command.Parameters.AddWithValue(
            "$projectType",
            DbValue(entryType.ForProjectType));

        var id = Convert.ToInt32(
            command.ExecuteScalar(),
            CultureInfo.InvariantCulture);

        transaction.Commit();
        return id;
    }


    public IReadOnlyList<WorkLogDayOverviewRow> GetMonthOverview(
        int userId,
        DateTime month)
    {
        var firstDay = new DateTime(month.Year, month.Month, 1);
        var lastDay = firstDay.AddMonths(1).AddDays(-1);

        var rows = Enumerable
            .Range(0, lastDay.Day)
            .Select(offset => new WorkLogDayOverviewRow
            {
                Date = firstDay.AddDays(offset)
            })
            .ToDictionary(
                row => row.Date.Date,
                row => row);

        using var connection = OpenConnection();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT
                    a.WorkDate,
                    a.ArrivalTimestamp,
                    a.DepartureTimestamp,
                    COALESCE(a.HoursWorked, 0) AS HoursWorked,
                    COALESCE(a.HoursOvertime, 0) AS HoursOvertime
                FROM ArrivalsDepartures a
                INNER JOIN
                (
                    SELECT
                        date(WorkDate) AS WorkDay,
                        MAX(Id) AS MaxId
                    FROM ArrivalsDepartures
                    WHERE UserId = $userId
                      AND date(WorkDate) >= date($from)
                      AND date(WorkDate) <= date($to)
                    GROUP BY date(WorkDate)
                ) latest
                    ON latest.MaxId = a.Id
                ORDER BY date(a.WorkDate);
                """;

            command.Parameters.AddWithValue("$userId", userId);
            command.Parameters.AddWithValue("$from", ToSqlDate(firstDay));
            command.Parameters.AddWithValue("$to", ToSqlDate(lastDay));

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var date = ParseSqlDateTime(
                        reader.GetString(0))
                    .Date;

                if (!rows.TryGetValue(date, out var row))
                {
                    continue;
                }

                row.ArrivalTimestamp =
                    reader.IsDBNull(1)
                        ? null
                        : ParseNullableSqlDateTime(
                            reader.GetString(1));

                row.DepartureTimestamp =
                    reader.IsDBNull(2)
                        ? null
                        : ParseNullableSqlDateTime(
                            reader.GetString(2));

                row.HoursWorked =
                    reader.IsDBNull(3)
                        ? 0
                        : reader.GetDouble(3);

                row.HoursOvertime =
                    reader.IsDBNull(4)
                        ? 0
                        : reader.GetDouble(4);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT
                    date(Timestamp) AS WorkDay,
                    COUNT(*) AS EntryCount,
                    COALESCE(SUM(EntryMinutes), 0) AS EntryMinutes
                FROM TimeEntries
                WHERE UserId = $userId
                  AND date(Timestamp) >= date($from)
                  AND date(Timestamp) <= date($to)
                GROUP BY date(Timestamp);
                """;

            command.Parameters.AddWithValue("$userId", userId);
            command.Parameters.AddWithValue("$from", ToSqlDate(firstDay));
            command.Parameters.AddWithValue("$to", ToSqlDate(lastDay));

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var date = ParseSqlDateTime(
                        reader.GetString(0))
                    .Date;

                if (!rows.TryGetValue(date, out var row))
                {
                    continue;
                }

                row.EntryCount = reader.GetInt32(1);
                row.EntryMinutes = reader.GetInt32(2);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT
                    date(Date) AS WorkDay,
                    MAX(COALESCE(Locked, 0)) AS Locked,
                    GROUP_CONCAT(
                        CASE
                            WHEN TRIM(COALESCE(Title, '')) = '' THEN NULL
                            ELSE TRIM(Title)
                        END,
                        ', ') AS Titles
                FROM SpecialDays
                WHERE date(Date) >= date($from)
                  AND date(Date) <= date($to)
                GROUP BY date(Date);
                """;

            command.Parameters.AddWithValue("$from", ToSqlDate(firstDay));
            command.Parameters.AddWithValue("$to", ToSqlDate(lastDay));

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var date = ParseSqlDateTime(
                        reader.GetString(0))
                    .Date;

                if (!rows.TryGetValue(date, out var row))
                {
                    continue;
                }

                row.IsLocked =
                    !reader.IsDBNull(1) &&
                    reader.GetInt32(1) != 0;

                row.SpecialTitle =
                    reader.IsDBNull(2)
                        ? string.Empty
                        : reader.GetString(2);
            }
        }

        return rows.Values
            .OrderBy(row => row.Date)
            .ToList();
    }

    public IReadOnlyList<WorkLogTimeEntry> GetTimeEntries(
        int userId,
        DateTime date)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                e.Id,
                e.UserId,
                e.ProjectId,
                COALESCE(p.ProjectTitle, '') AS ProjectTitle,
                e.EntryTypeId,
                COALESCE(t.Title, '') AS EntryTypeTitle,
                COALESCE(t.Color, '#315A7D') AS EntryTypeColor,
                e.Timestamp,
                COALESCE(e.Description, '') AS Description,
                e.EntryMinutes,
                COALESCE(e.AfterCare, 0) AS AfterCare,
                COALESCE(e.Note, '') AS Note,
                COALESCE(e.IsLocked, 0) AS IsLocked,
                COALESCE(e.IsValid, 0) AS IsValid
            FROM TimeEntries e
            LEFT JOIN Projects p ON p.Id = e.ProjectId
            LEFT JOIN TimeEntryTypes t ON t.ID = e.EntryTypeId
            WHERE e.UserId = $userId
              AND date(e.Timestamp) = date($date)
            ORDER BY e.Timestamp, e.Id;
            """;

        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue(
            "$date",
            ToSqlDate(date));

        using var reader = command.ExecuteReader();
        var result = new List<WorkLogTimeEntry>();

        while (reader.Read())
        {
            result.Add(ReadTimeEntry(reader));
        }

        return result;
    }

    public WorkLogTimeEntry? GetTimeEntry(int id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                e.Id,
                e.UserId,
                e.ProjectId,
                COALESCE(p.ProjectTitle, '') AS ProjectTitle,
                e.EntryTypeId,
                COALESCE(t.Title, '') AS EntryTypeTitle,
                COALESCE(t.Color, '#315A7D') AS EntryTypeColor,
                e.Timestamp,
                COALESCE(e.Description, '') AS Description,
                e.EntryMinutes,
                COALESCE(e.AfterCare, 0) AS AfterCare,
                COALESCE(e.Note, '') AS Note,
                COALESCE(e.IsLocked, 0) AS IsLocked,
                COALESCE(e.IsValid, 0) AS IsValid
            FROM TimeEntries e
            LEFT JOIN Projects p ON p.Id = e.ProjectId
            LEFT JOIN TimeEntryTypes t ON t.ID = e.EntryTypeId
            WHERE e.Id = $id;
            """;

        command.Parameters.AddWithValue("$id", id);

        using var reader = command.ExecuteReader();

        return reader.Read()
            ? ReadTimeEntry(reader)
            : null;
    }

    public int SaveTimeEntry(WorkLogTimeEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        EnsureDayIsEditable(
            connection,
            transaction,
            entry.Timestamp.Date,
            entry.Id);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        if (entry.Id <= 0)
        {
            command.CommandText = """
                INSERT INTO TimeEntries
                (
                    UserId,
                    ProjectId,
                    EntryTypeId,
                    Timestamp,
                    Description,
                    EntryMinutes,
                    AfterCare,
                    Note,
                    IsLocked,
                    IsValid
                )
                VALUES
                (
                    $userId,
                    $projectId,
                    $entryTypeId,
                    $timestamp,
                    $description,
                    $minutes,
                    $afterCare,
                    $note,
                    0,
                    1
                );
                SELECT last_insert_rowid();
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE TimeEntries
                SET
                    UserId = $userId,
                    ProjectId = $projectId,
                    EntryTypeId = $entryTypeId,
                    Timestamp = $timestamp,
                    Description = $description,
                    EntryMinutes = $minutes,
                    AfterCare = $afterCare,
                    Note = $note,
                    IsValid = 1
                WHERE Id = $id;
                SELECT $id;
                """;
            command.Parameters.AddWithValue("$id", entry.Id);
        }

        command.Parameters.AddWithValue(
            "$userId",
            entry.UserId);
        command.Parameters.AddWithValue(
            "$projectId",
            DbValue(entry.ProjectId));
        command.Parameters.AddWithValue(
            "$entryTypeId",
            DbValue(entry.EntryTypeId));
        command.Parameters.AddWithValue(
            "$timestamp",
            entry.Timestamp.ToString(
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$description",
            NullIfWhiteSpace(entry.Description));
        command.Parameters.AddWithValue(
            "$minutes",
            Math.Max(1, entry.EntryMinutes));
        command.Parameters.AddWithValue(
            "$afterCare",
            entry.AfterCare ? 1 : 0);
        command.Parameters.AddWithValue(
            "$note",
            NullIfWhiteSpace(entry.Note));

        var id = Convert.ToInt32(
            command.ExecuteScalar(),
            CultureInfo.InvariantCulture);

        transaction.Commit();
        return id;
    }

    public void MoveTimeEntries(
        IReadOnlyCollection<int> entryIds,
        int expectedUserId,
        TimeSpan delta)
    {
        ArgumentNullException.ThrowIfNull(entryIds);

        var ids = entryIds
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        if (ids.Length == 0 || delta == TimeSpan.Zero)
        {
            return;
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var moves = new List<(int Id, DateTime OldTimestamp, DateTime NewTimestamp)>();

        foreach (var id in ids)
        {
            int userId;
            DateTime oldTimestamp;
            bool isLocked;

            using (var lookup = connection.CreateCommand())
            {
                lookup.Transaction = transaction;
                lookup.CommandText = """
                    SELECT
                        UserId,
                        Timestamp,
                        COALESCE(IsLocked, 0)
                    FROM TimeEntries
                    WHERE Id = $id;
                    """;
                lookup.Parameters.AddWithValue("$id", id);

                using var reader = lookup.ExecuteReader();

                if (!reader.Read())
                {
                    throw new InvalidOperationException(
                        $"WorkLog entry {id} was not found.");
                }

                userId = reader.GetInt32(0);
                oldTimestamp = ParseSqlDateTime(reader.GetString(1));
                isLocked = reader.GetInt32(2) != 0;
            }

            if (userId != expectedUserId)
            {
                throw new InvalidOperationException(
                    "The selected WorkLog entries do not belong to the expected user.");
            }

            if (isLocked || IsDayLocked(connection, transaction, oldTimestamp.Date))
            {
                throw new InvalidOperationException(
                    "One of the selected WorkLog entries is locked.");
            }

            var newTimestamp = oldTimestamp.Add(delta);

            if (newTimestamp.Date != oldTimestamp.Date)
            {
                throw new InvalidOperationException(
                    "Moving a WorkLog block to another day is not allowed in the day agenda.");
            }

            if (IsDayLocked(connection, transaction, newTimestamp.Date))
            {
                throw new InvalidOperationException(
                    "The target WorkLog day is locked.");
            }

            moves.Add((id, oldTimestamp, newTimestamp));
        }

        foreach (var move in moves)
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE TimeEntries
                SET Timestamp = $timestamp
                WHERE Id = $id;
                """;
            update.Parameters.AddWithValue("$id", move.Id);
            update.Parameters.AddWithValue(
                "$timestamp",
                move.NewTimestamp.ToString(
                    "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture));
            update.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void DeleteTimeEntry(int id)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var lookup = connection.CreateCommand())
        {
            lookup.Transaction = transaction;
            lookup.CommandText = """
                SELECT Timestamp, COALESCE(IsLocked, 0)
                FROM TimeEntries
                WHERE Id = $id;
                """;
            lookup.Parameters.AddWithValue("$id", id);

            using var reader = lookup.ExecuteReader();

            if (!reader.Read())
            {
                transaction.Rollback();
                return;
            }

            var timestamp = ParseSqlDateTime(reader.GetString(0));
            var locked = reader.GetInt32(1) != 0;

            if (locked || IsDayLocked(connection, transaction, timestamp.Date))
            {
                throw new InvalidOperationException(
                    "The selected WorkLog entry is locked.");
            }
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM TimeEntries
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();

        transaction.Commit();
    }

    public WorkLogArrivalDeparture? GetArrivalDeparture(
        int userId,
        DateTime date)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                Id,
                UserId,
                WorkDate,
                ArrivalTimestamp,
                DepartureTimestamp,
                COALESCE(DepartureReason, '') AS DepartureReason,
                COALESCE(HoursWorked, 0) AS HoursWorked,
                COALESCE(HoursOvertime, 0) AS HoursOvertime
            FROM ArrivalsDepartures
            WHERE UserId = $userId
              AND date(WorkDate) = date($date)
            ORDER BY Id DESC
            LIMIT 1;
            """;

        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue(
            "$date",
            ToSqlDate(date));

        using var reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        return new WorkLogArrivalDeparture
        {
            Id = reader.GetInt32(0),
            UserId = reader.GetInt32(1),
            WorkDate = ParseSqlDateTime(reader.GetString(2)),
            ArrivalTimestamp = reader.IsDBNull(3)
                ? null
                : ParseNullableSqlDateTime(reader.GetString(3)),
            DepartureTimestamp = reader.IsDBNull(4)
                ? null
                : ParseNullableSqlDateTime(reader.GetString(4)),
            DepartureReason = reader.GetString(5),
            HoursWorked = reader.GetDouble(6),
            HoursOvertime = reader.GetDouble(7)
        };
    }

    public bool IsDayLocked(DateTime date)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var locked = IsDayLocked(connection, transaction, date);
        transaction.Rollback();
        return locked;
    }

    public IReadOnlyList<WorkLogSpecialDay> GetSpecialDays(
        DateTime from,
        DateTime to)
    {
        if (to.Date < from.Date)
        {
            (from, to) = (to, from);
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                Id,
                Date,
                Title,
                COALESCE(Locked, 0) AS Locked,
                COALESCE(Color, '') AS Color
            FROM SpecialDays
            WHERE date(Date) >= date($from)
              AND date(Date) <= date($to)
            ORDER BY date(Date), Id;
            """;

        command.Parameters.AddWithValue("$from", ToSqlDate(from));
        command.Parameters.AddWithValue("$to", ToSqlDate(to));

        using var reader = command.ExecuteReader();
        var result = new List<WorkLogSpecialDay>();

        while (reader.Read())
        {
            result.Add(new WorkLogSpecialDay
            {
                Id = reader.GetInt32(0),
                Date = ParseSqlDateTime(reader.GetString(1)),
                Title = reader.GetString(2),
                Locked = reader.GetInt32(3) != 0,
                Color = reader.GetString(4)
            });
        }

        return result;
    }

    public void SetLockedRange(
        DateTime from,
        DateTime to,
        bool locked)
    {
        if (to.Date < from.Date)
        {
            (from, to) = (to, from);
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        if (locked)
        {
            for (var date = from.Date;
                 date <= to.Date;
                 date = date.AddDays(1))
            {
                using var specialDay = connection.CreateCommand();
                specialDay.Transaction = transaction;
                specialDay.CommandText = """
                    INSERT INTO SpecialDays
                    (
                        Date,
                        Title,
                        Locked,
                        Color
                    )
                    SELECT
                        $date,
                        'Uzamčeno',
                        1,
                        '#DCDCDC'
                    WHERE NOT EXISTS
                    (
                        SELECT 1
                        FROM SpecialDays
                        WHERE date(Date) = date($date)
                          AND Title = 'Uzamčeno'
                    );
                    """;
                specialDay.Parameters.AddWithValue(
                    "$date",
                    ToSqlDate(date));
                specialDay.ExecuteNonQuery();
            }

            using var entries = connection.CreateCommand();
            entries.Transaction = transaction;
            entries.CommandText = """
                UPDATE TimeEntries
                SET IsLocked = 1
                WHERE date(Timestamp) >= date($from)
                  AND date(Timestamp) <= date($to);
                """;
            entries.Parameters.AddWithValue("$from", ToSqlDate(from));
            entries.Parameters.AddWithValue("$to", ToSqlDate(to));
            entries.ExecuteNonQuery();
        }
        else
        {
            using (var removeManualLocks = connection.CreateCommand())
            {
                removeManualLocks.Transaction = transaction;
                removeManualLocks.CommandText = """
                    DELETE FROM SpecialDays
                    WHERE Title = 'Uzamčeno'
                      AND date(Date) >= date($from)
                      AND date(Date) <= date($to);
                    """;
                removeManualLocks.Parameters.AddWithValue(
                    "$from",
                    ToSqlDate(from));
                removeManualLocks.Parameters.AddWithValue(
                    "$to",
                    ToSqlDate(to));
                removeManualLocks.ExecuteNonQuery();
            }

            using (var entries = connection.CreateCommand())
            {
                entries.Transaction = transaction;
                entries.CommandText = """
                    UPDATE TimeEntries
                    SET IsLocked = 0
                    WHERE date(Timestamp) >= date($from)
                      AND date(Timestamp) <= date($to);
                    """;
                entries.Parameters.AddWithValue(
                    "$from",
                    ToSqlDate(from));
                entries.Parameters.AddWithValue(
                    "$to",
                    ToSqlDate(to));
                entries.ExecuteNonQuery();
            }

            // Keep entries locked on holidays or other special days which are
            // independently marked Locked=1. Only manual "Uzamčeno" rows are
            // removed by the unlock operation above.
            using var preserveOtherLocks = connection.CreateCommand();
            preserveOtherLocks.Transaction = transaction;
            preserveOtherLocks.CommandText = """
                UPDATE TimeEntries
                SET IsLocked = 1
                WHERE date(Timestamp) IN
                (
                    SELECT date(Date)
                    FROM SpecialDays
                    WHERE COALESCE(Locked, 0) = 1
                      AND date(Date) >= date($from)
                      AND date(Date) <= date($to)
                );
                """;
            preserveOtherLocks.Parameters.AddWithValue(
                "$from",
                ToSqlDate(from));
            preserveOtherLocks.Parameters.AddWithValue(
                "$to",
                ToSqlDate(to));
            preserveOtherLocks.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private SqliteConnection OpenConnection()
    {
        if (!File.Exists(DatabasePath))
        {
            throw new FileNotFoundException(
                "WorkLog database file was not found.",
                DatabasePath);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true
        };

        var connection = new SqliteConnection(
            builder.ToString());

        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout = 5000;";
        command.ExecuteNonQuery();

        return connection;
    }

    private static void ValidateSchema(
        SqliteConnection connection)
    {
        var existing = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table';
            """;

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            existing.Add(reader.GetString(0));
        }

        var missing = RequiredTables
            .Where(table => !existing.Contains(table))
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "WorkLog database schema is incomplete. Missing tables: " +
                string.Join(", ", missing));
        }
    }

    private static WorkLogUser ReadUser(
        SqliteDataReader reader)
    {
        return new WorkLogUser
        {
            Id = reader.GetInt32(0),
            FirstName = reader.GetString(1),
            Surname = reader.GetString(2),
            PersonalNumber = reader.GetInt32(3),
            WindowsUsername = reader.GetString(4),
            LevelOfAccess = reader.GetInt32(5),
            UserGroupId = reader.IsDBNull(6)
                ? null
                : reader.GetInt32(6),
            UserGroupTitle = reader.GetString(7),
            Email = reader.GetString(8),
            MasterUserId = reader.IsDBNull(9)
                ? null
                : reader.GetInt32(9),
            IsArchived = reader.GetInt32(10) != 0
        };
    }

    private static WorkLogTimeEntry ReadTimeEntry(
        SqliteDataReader reader)
    {
        return new WorkLogTimeEntry
        {
            Id = reader.GetInt32(0),
            UserId = reader.GetInt32(1),
            ProjectId = reader.IsDBNull(2)
                ? null
                : reader.GetInt32(2),
            ProjectTitle = reader.GetString(3),
            EntryTypeId = reader.IsDBNull(4)
                ? null
                : reader.GetInt32(4),
            EntryTypeTitle = reader.GetString(5),
            EntryTypeColor = reader.GetString(6),
            Timestamp = ParseSqlDateTime(reader.GetString(7)),
            Description = reader.GetString(8),
            EntryMinutes = reader.GetInt32(9),
            AfterCare = reader.GetInt32(10) != 0,
            Note = reader.GetString(11),
            IsLocked = reader.GetInt32(12) != 0,
            IsValid = reader.GetInt32(13) != 0
        };
    }

    private static void EnsureDayIsEditable(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTime date,
        int entryId)
    {
        if (IsDayLocked(connection, transaction, date))
        {
            throw new InvalidOperationException(
                "The selected WorkLog day is locked.");
        }

        if (entryId <= 0)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COALESCE(IsLocked, 0)
            FROM TimeEntries
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", entryId);

        var value = command.ExecuteScalar();

        if (value is not null &&
            value is not DBNull &&
            Convert.ToInt32(
                value,
                CultureInfo.InvariantCulture) != 0)
        {
            throw new InvalidOperationException(
                "The selected WorkLog entry is locked.");
        }
    }

    private static bool IsDayLocked(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTime date)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS
            (
                SELECT 1
                FROM SpecialDays
                WHERE date(Date) = date($date)
                  AND COALESCE(Locked, 0) = 1
            );
            """;
        command.Parameters.AddWithValue(
            "$date",
            ToSqlDate(date));

        return Convert.ToInt32(
            command.ExecuteScalar(),
            CultureInfo.InvariantCulture) != 0;
    }

    private static int ExecuteScalarInt(
        SqliteConnection connection,
        string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt32(
            command.ExecuteScalar(),
            CultureInfo.InvariantCulture);
    }

    private static string ExecuteScalarString(
        SqliteConnection connection,
        string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToString(
                   command.ExecuteScalar(),
                   CultureInfo.InvariantCulture)
               ?? string.Empty;
    }

    private static object DbValue(int? value) =>
        value.HasValue
            ? value.Value
            : DBNull.Value;

    private static object NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? DBNull.Value
            : value.Trim();

    private static string NormalizePath(string value)
    {
        var path = string.IsNullOrWhiteSpace(value)
            ? WorkLogSettings.DefaultDatabasePath
            : value.Trim();

        return Path.GetFullPath(
            Environment.ExpandEnvironmentVariables(path));
    }

    public static string NormalizeWindowsLogin(
        string? windowsLogin)
    {
        if (string.IsNullOrWhiteSpace(windowsLogin))
        {
            return string.Empty;
        }

        var value = windowsLogin.Trim();

        var slashIndex = value.LastIndexOf('\\');

        if (slashIndex >= 0 &&
            slashIndex + 1 < value.Length)
        {
            value = value[(slashIndex + 1)..];
        }

        var atIndex = value.IndexOf('@');

        if (atIndex > 0)
        {
            value = value[..atIndex];
        }

        return value.Trim();
    }

    private static string ToSqlDate(DateTime date) =>
        date.ToString(
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture);

    private static DateTime ParseSqlDateTime(string value)
    {
        if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var result))
        {
            return result;
        }

        return DateTime.Parse(value);
    }

    private static DateTime? ParseNullableSqlDateTime(
        string value)
    {
        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var result)
            ? result
            : null;
    }
}
