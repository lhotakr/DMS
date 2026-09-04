using DMS.Desktop.Logging;
using DMS.Desktop.Views.Dialogs;
using DMS.Desktop.WorkLog;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.ComponentModel;

namespace DMS.Desktop.Views.WorkLog;

public partial class WorkLogWorkView : UserControl
{
    private readonly WorkLogSettingsService _settingsService;
    private readonly DmsLogger? _logger;
    private readonly string _windowsLogin;
    private readonly string _currentUserName;
    private readonly bool _isDmsAdmin;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;

    private WorkLogRepository? _repository;
    private WorkLogAccessPolicy? _access;
    private readonly ObservableCollection<WorkLogProject> _projects = new();
    private readonly ObservableCollection<WorkLogEntryType> _entryTypes = new();
    private ICollectionView? _projectView;
    private WorkLogProject? _selectedProject;
    private WorkLogEntryType? _selectedEntryType;
    private bool _loading;

    public WorkLogWorkView(
        string configurationRootPath,
        string windowsLogin,
        string currentUserName,
        bool isDmsAdmin,
        DmsLogger? logger = null,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null)
    {
        InitializeComponent();

        _settingsService =
            new WorkLogSettingsService(configurationRootPath);
        _windowsLogin = windowsLogin ?? string.Empty;
        _currentUserName =
            string.IsNullOrWhiteSpace(currentUserName)
                ? "UNKNOWN"
                : currentUserName;
        _isDmsAdmin = isDmsAdmin;
        _logger = logger;
        _translate = translate;
        _translateFormat = translateFormat;

        GridProjects.ItemsSource = _projects;
        GridEntryTypes.ItemsSource = _entryTypes;

        ApplyLocalization();
        LoadData();
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("WLWORK.Title");
        TxtSubtitle.Text = T("WLWORK.Subtitle");
        TabProjects.Header = T("WLWORK.Tab.Projects");
        TabEntryTypes.Header = T("WLWORK.Tab.EntryTypes");
        LblProjectFilter.Text = T("WLWORK.Filter");
        ChkShowArchivedProjects.Content = T("WLWORK.ShowArchived");

        TxtProjectEditorTitle.Text = T("WLWORK.ProjectEditor");
        LblProjectTitle.Text = T("WLWORK.ProjectTitle");
        LblProjectDescription.Text = T("WLWORK.ProjectDescription");
        LblProjectType.Text = T("WLWORK.ProjectType");
        LblProjectFulfilled.Text = T("WLWORK.DateFulfilled");
        LblProjectNote.Text = T("WLWORK.Note");

        BtnProjectNew.Content = T("WLWORK.New");
        BtnProjectSave.Content = T("WLWORK.Save");
        BtnProjectArchive.Content = T("WLWORK.Archive");
        BtnProjectRestore.Content = T("WLWORK.Restore");

        ColProjectTitle.Header = T("WLWORK.Col.ProjectTitle");
        ColProjectDescription.Header = T("WLWORK.Col.ProjectDescription");
        ColProjectType.Header = T("WLWORK.Col.ProjectType");
        ColProjectCreator.Header = T("WLWORK.Col.CreatedBy");
        ColProjectArchived.Header = T("WLWORK.Col.Archived");

        TxtTypeEditorTitle.Text = T("WLWORK.TypeEditor");
        LblTypeTitle.Text = T("WLWORK.TypeTitle");
        LblTypeColor.Text = T("WLWORK.Color");
        LblTypeProjectType.Text = T("WLWORK.ProjectType");
        TxtEntryTypeHint.Text = T("WLWORK.EntryTypeHint");
        BtnTypeNew.Content = T("WLWORK.New");
        BtnTypeSave.Content = T("WLWORK.Save");

        ColTypeTitle.Header = T("WLWORK.Col.TypeTitle");
        ColTypeColor.Header = T("WLWORK.Col.Color");
        ColTypeProjectType.Header = T("WLWORK.Col.ProjectType");

        BtnReload.Content = T("WLWORK.Reload");
    }

    private void LoadData(
        int? projectId = null,
        int? typeId = null)
    {
        _loading = true;

        try
        {
            var settings = _settingsService.Load();
            _repository =
                new WorkLogRepository(settings.DatabasePath);
            _repository.TestConnection();

            var current =
                _repository.FindUserByWindowsUsername(
                    _windowsLogin);
            _access =
                new WorkLogAccessPolicy(
                    current,
                    _isDmsAdmin);

            var admin = _access.IsAdministrator;
            SetControlsEnabled(admin);

            if (!admin)
            {
                _projects.Clear();
                _entryTypes.Clear();
                TxtStatus.Text =
                    T("WLWORK.Status.AccessDenied");
                return;
            }

            var allProjects =
                _repository.GetProjects(
                    includeArchived: true);

            _projects.Clear();
            foreach (var project in allProjects)
            {
                _projects.Add(project);
            }

            _projectView =
                CollectionViewSource
                    .GetDefaultView(_projects);
            _projectView.Filter =
                FilterProject;

            _entryTypes.Clear();
            foreach (var entryType
                     in _repository.GetEntryTypes())
            {
                _entryTypes.Add(entryType);
            }

            GridProjects.SelectedItem =
                projectId.HasValue
                    ? _projects.FirstOrDefault(
                        project =>
                            project.Id ==
                            projectId.Value)
                    : _projects.FirstOrDefault(
                        project =>
                            !project.IsArchived);

            GridEntryTypes.SelectedItem =
                typeId.HasValue
                    ? _entryTypes.FirstOrDefault(
                        type =>
                            type.Id ==
                            typeId.Value)
                    : _entryTypes.FirstOrDefault();

            TxtStatus.Text = TF(
                "WLWORK.Status.Loaded",
                _projects.Count,
                _entryTypes.Count);
        }
        catch (Exception ex)
        {
            SetControlsEnabled(false);
            TxtStatus.Text = TF(
                "WLWORK.Status.LoadFailed",
                ex.Message);

            _logger?.Error(
                "WLWORK: load failed.",
                ex);
        }
        finally
        {
            _loading = false;
        }

        if (_access?.IsAdministrator == true)
        {
            RefreshEditorsFromSelection();
        }
    }

    private void RefreshEditorsFromSelection()
    {
        if (GridProjects.SelectedItem is WorkLogProject project)
        {
            _selectedProject = project;
            TxtProjectTitle.Text = project.ProjectTitle;
            TxtProjectDescription.Text = project.ProjectDescription;
            TxtProjectType.Text = project.ProjectType.ToString();
            TxtProjectFulfilled.Text = project.DateFullFilled;
            TxtProjectNote.Text = project.Note;
            BtnProjectArchive.IsEnabled = !project.IsArchived;
            BtnProjectRestore.IsEnabled = project.IsArchived;
        }
        else
        {
            ClearProjectEditor();
        }

        if (GridEntryTypes.SelectedItem is WorkLogEntryType entryType)
        {
            _selectedEntryType = entryType;
            TxtTypeTitle.Text = entryType.Title;
            TxtTypeColor.Text = entryType.Color;
            TxtTypeProjectType.Text = entryType.ForProjectType?.ToString() ?? string.Empty;
        }
        else
        {
            ClearEntryTypeEditor();
        }
    }

    private void SetControlsEnabled(bool enabled)
    {
        Tabs.IsEnabled = enabled;
        BtnReload.IsEnabled = true;
    }

    private bool FilterProject(object item)
    {
        if (item is not WorkLogProject project)
        {
            return false;
        }

        if (ChkShowArchivedProjects.IsChecked != true &&
            project.IsArchived)
        {
            return false;
        }

        var filter =
            TxtProjectFilter.Text?.Trim();

        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return Contains(
                   project.ProjectTitle,
                   filter) ||
               Contains(
                   project.ProjectDescription,
                   filter) ||
               Contains(
                   project.Note,
                   filter) ||
               project.ProjectType
                   .ToString()
                   .Contains(
                       filter,
                       StringComparison.OrdinalIgnoreCase);
    }

    private static bool Contains(
        string? value,
        string filter) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(
            filter,
            StringComparison.OrdinalIgnoreCase);

    private void GridProjects_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (GridProjects.SelectedItem
            is WorkLogProject project)
        {
            _selectedProject = project;
            TxtProjectTitle.Text =
                project.ProjectTitle;
            TxtProjectDescription.Text =
                project.ProjectDescription;
            TxtProjectType.Text =
                project.ProjectType.ToString();
            TxtProjectFulfilled.Text =
                project.DateFullFilled;
            TxtProjectNote.Text =
                project.Note;

            BtnProjectArchive.IsEnabled =
                !project.IsArchived;
            BtnProjectRestore.IsEnabled =
                project.IsArchived;
        }
        else
        {
            ClearProjectEditor();
        }
    }

    private void ClearProjectEditor()
    {
        _selectedProject = null;
        TxtProjectTitle.Text =
            string.Empty;
        TxtProjectDescription.Text =
            string.Empty;
        TxtProjectType.Text = "0";
        TxtProjectFulfilled.Text =
            string.Empty;
        TxtProjectNote.Text =
            string.Empty;

        BtnProjectArchive.IsEnabled = false;
        BtnProjectRestore.IsEnabled = false;
    }

    private void GridEntryTypes_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (GridEntryTypes.SelectedItem
            is WorkLogEntryType entryType)
        {
            _selectedEntryType = entryType;
            TxtTypeTitle.Text =
                entryType.Title;
            TxtTypeColor.Text =
                entryType.Color;
            TxtTypeProjectType.Text =
                entryType.ForProjectType?.ToString()
                ?? string.Empty;
        }
        else
        {
            ClearEntryTypeEditor();
        }
    }

    private void ClearEntryTypeEditor()
    {
        _selectedEntryType = null;
        TxtTypeTitle.Text =
            string.Empty;
        TxtTypeColor.Text =
            "#ADD8E6";
        TxtTypeProjectType.Text =
            string.Empty;
    }

    private void TxtProjectFilter_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        _projectView?.Refresh();
    }

    private void ProjectFilter_Changed(
        object sender,
        RoutedEventArgs e)
    {
        _projectView?.Refresh();
    }

    private void BtnProjectNew_Click(
        object sender,
        RoutedEventArgs e)
    {
        GridProjects.SelectedItem = null;
        ClearProjectEditor();
        TxtProjectTitle.Focus();
    }

    private void BtnProjectSave_Click(
        object sender,
        RoutedEventArgs e)
    {
        var repository = _repository;
        var current = _access?.CurrentUser;

        if (repository is null ||
            _access?.IsAdministrator != true ||
            current is null)
        {
            return;
        }

        var title =
            TxtProjectTitle.Text.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            ShowValidation(
                T("WLWORK.Validation.ProjectTitle"));
            return;
        }

        if (!int.TryParse(
                TxtProjectType.Text.Trim(),
                out var projectType) ||
            projectType < 0)
        {
            ShowValidation(
                T("WLWORK.Validation.ProjectType"));
            return;
        }

        var old =
            _selectedProject is null
                ? null
                : Clone(_selectedProject);

        var project = new WorkLogProject
        {
            Id = _selectedProject?.Id ?? 0,
            ProjectType = projectType,
            ProjectTitle = title,
            ProjectDescription =
                TxtProjectDescription.Text.Trim(),
            CreatedBy =
                _selectedProject?.CreatedBy
                ?? current.Id,
            Note = TxtProjectNote.Text.Trim(),
            DateFullFilled =
                TxtProjectFulfilled.Text.Trim(),
            IsArchived =
                _selectedProject?.IsArchived
                ?? false
        };

        try
        {
            var id =
                repository.SaveProject(project);

            if (old is null)
            {
                _logger?.AuditCreated(
                    "WORKLOG",
                    "Project",
                    id.ToString(),
                    _currentUserName,
                    $"Title={project.ProjectTitle}; ProjectType={project.ProjectType}; CreatedBy={project.CreatedBy}");
            }
            else
            {
                LogProjectChanges(
                    old,
                    project,
                    id);
            }

            LoadData(
                projectId: id,
                typeId:
                    _selectedEntryType?.Id);

            TxtStatus.Text =
                T("WLWORK.Status.ProjectSaved");
        }
        catch (Exception ex)
        {
            _logger?.Error(
                "WLWORK: project save failed.",
                ex);

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("WLWORK.Dialog.ErrorTitle"),
                TF(
                    "WLWORK.Dialog.SaveFailed",
                    ex.Message));
        }
    }

    private void BtnProjectArchive_Click(
        object sender,
        RoutedEventArgs e)
    {
        SetProjectArchived(true);
    }

    private void BtnProjectRestore_Click(
        object sender,
        RoutedEventArgs e)
    {
        SetProjectArchived(false);
    }

    private void SetProjectArchived(
        bool archived)
    {
        var repository = _repository;
        var project = _selectedProject;

        if (repository is null ||
            project is null ||
            _access?.IsAdministrator != true)
        {
            return;
        }

        var questionKey =
            archived
                ? "WLWORK.Dialog.ArchiveProject"
                : "WLWORK.Dialog.RestoreProject";

        if (!DmsConfirmDialog.ShowQuestion(
                Window.GetWindow(this),
                T("WLWORK.Dialog.ArchiveTitle"),
                TF(
                    questionKey,
                    project.ProjectTitle)))
        {
            return;
        }

        try
        {
            repository.SetProjectArchived(
                project.Id,
                archived);

            _logger?.AuditChange(
                "WORKLOG",
                "Project",
                project.Id.ToString(),
                "IsArchived",
                project.IsArchived
                    ? "true"
                    : "false",
                archived
                    ? "true"
                    : "false",
                _currentUserName);

            LoadData(
                projectId:
                    archived
                        ? null
                        : project.Id,
                typeId:
                    _selectedEntryType?.Id);

            TxtStatus.Text =
                archived
                    ? T("WLWORK.Status.ProjectArchived")
                    : T("WLWORK.Status.ProjectRestored");
        }
        catch (Exception ex)
        {
            _logger?.Error(
                "WLWORK: project archive state change failed.",
                ex);

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("WLWORK.Dialog.ErrorTitle"),
                ex.Message);
        }
    }

    private void BtnTypeNew_Click(
        object sender,
        RoutedEventArgs e)
    {
        GridEntryTypes.SelectedItem = null;
        ClearEntryTypeEditor();
        TxtTypeTitle.Focus();
    }

    private void BtnTypeSave_Click(
        object sender,
        RoutedEventArgs e)
    {
        var repository = _repository;

        if (repository is null ||
            _access?.IsAdministrator != true)
        {
            return;
        }

        var title =
            TxtTypeTitle.Text.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            ShowValidation(
                T("WLWORK.Validation.TypeTitle"));
            return;
        }

        int? projectType = null;
        var rawProjectType =
            TxtTypeProjectType.Text.Trim();

        if (!string.IsNullOrWhiteSpace(
                rawProjectType))
        {
            if (!int.TryParse(
                    rawProjectType,
                    out var parsed) ||
                parsed < 0)
            {
                ShowValidation(
                    T("WLWORK.Validation.ProjectType"));
                return;
            }

            projectType = parsed;
        }

        var old =
            _selectedEntryType is null
                ? null
                : new WorkLogEntryType
                {
                    Id = _selectedEntryType.Id,
                    Title = _selectedEntryType.Title,
                    Color = _selectedEntryType.Color,
                    ForProjectType =
                        _selectedEntryType
                            .ForProjectType
                };

        var entryType =
            new WorkLogEntryType
            {
                Id =
                    _selectedEntryType?.Id
                    ?? 0,
                Title = title,
                Color =
                    string.IsNullOrWhiteSpace(
                        TxtTypeColor.Text)
                        ? "#ADD8E6"
                        : TxtTypeColor.Text.Trim(),
                ForProjectType =
                    projectType
            };

        try
        {
            var id =
                repository.SaveEntryType(
                    entryType);

            if (old is null)
            {
                _logger?.AuditCreated(
                    "WORKLOG",
                    "TimeEntryType",
                    id.ToString(),
                    _currentUserName,
                    $"Title={entryType.Title}; ForProjectType={entryType.ForProjectType}");
            }
            else
            {
                LogTypeChange(
                    id,
                    "Title",
                    old.Title,
                    entryType.Title);
                LogTypeChange(
                    id,
                    "Color",
                    old.Color,
                    entryType.Color);
                LogTypeChange(
                    id,
                    "ForProjectType",
                    old.ForProjectType?.ToString(),
                    entryType.ForProjectType?.ToString());
            }

            LoadData(
                projectId:
                    _selectedProject?.Id,
                typeId: id);

            TxtStatus.Text =
                T("WLWORK.Status.TypeSaved");
        }
        catch (Exception ex)
        {
            _logger?.Error(
                "WLWORK: entry type save failed.",
                ex);

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("WLWORK.Dialog.ErrorTitle"),
                TF(
                    "WLWORK.Dialog.SaveFailed",
                    ex.Message));
        }
    }

    private void BtnReload_Click(
        object sender,
        RoutedEventArgs e)
    {
        LoadData(
            _selectedProject?.Id,
            _selectedEntryType?.Id);
    }

    private void ShowValidation(string message)
    {
        DmsConfirmDialog.ShowInfo(
            Window.GetWindow(this),
            T("WLWORK.Dialog.ValidationTitle"),
            message);
    }

    private void LogProjectChanges(
        WorkLogProject oldProject,
        WorkLogProject newProject,
        int id)
    {
        LogProjectChange(
            id,
            "ProjectTitle",
            oldProject.ProjectTitle,
            newProject.ProjectTitle);
        LogProjectChange(
            id,
            "ProjectDescription",
            oldProject.ProjectDescription,
            newProject.ProjectDescription);
        LogProjectChange(
            id,
            "ProjectType",
            oldProject.ProjectType.ToString(),
            newProject.ProjectType.ToString());
        LogProjectChange(
            id,
            "Note",
            oldProject.Note,
            newProject.Note);
        LogProjectChange(
            id,
            "DateFullFilled",
            oldProject.DateFullFilled,
            newProject.DateFullFilled);
    }

    private void LogProjectChange(
        int id,
        string field,
        string? oldValue,
        string? newValue)
    {
        if (string.Equals(
                oldValue,
                newValue,
                StringComparison.Ordinal))
        {
            return;
        }

        _logger?.AuditChange(
            "WORKLOG",
            "Project",
            id.ToString(),
            field,
            oldValue,
            newValue,
            _currentUserName);
    }

    private void LogTypeChange(
        int id,
        string field,
        string? oldValue,
        string? newValue)
    {
        if (string.Equals(
                oldValue,
                newValue,
                StringComparison.Ordinal))
        {
            return;
        }

        _logger?.AuditChange(
            "WORKLOG",
            "TimeEntryType",
            id.ToString(),
            field,
            oldValue,
            newValue,
            _currentUserName);
    }

    private static WorkLogProject Clone(
        WorkLogProject project)
    {
        return new WorkLogProject
        {
            Id = project.Id,
            ProjectType =
                project.ProjectType,
            ProjectTitle =
                project.ProjectTitle,
            ProjectDescription =
                project.ProjectDescription,
            CreatedBy =
                project.CreatedBy,
            CreatedByName =
                project.CreatedByName,
            Note = project.Note,
            IsArchived =
                project.IsArchived,
            DateFullFilled =
                project.DateFullFilled
        };
    }

    private string T(string key)
    {
        if (_translate is null)
        {
            return key;
        }

        var value = _translate(key);

        return string.IsNullOrWhiteSpace(value) ||
               string.Equals(
                   value,
                   $"[[{key}]]",
                   StringComparison.OrdinalIgnoreCase)
            ? key
            : value;
    }

    private string TF(
        string key,
        params object[] args)
    {
        if (_translateFormat is not null)
        {
            return _translateFormat(key, args);
        }

        try
        {
            return string.Format(
                T(key),
                args);
        }
        catch
        {
            return T(key);
        }
    }
}
