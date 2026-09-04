using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using DMS.Core.Domain.Organization;
using DMS.Core.Domain.People;
using DMS.Core.Domain.Units;
using DMS.Core.Framework.MasterData;
using DMS.Desktop.Services.MasterData;

namespace DMS.Desktop.Views.Framework;

public partial class FrameworkMasterDataView : UserControl
{
    private readonly string _masterDataRoot;
    private readonly string _usersPath;
    private readonly Func<string, string> _translate;
    private readonly Action<string> _executeTransaction;
    private readonly Action<string, string> _log;

    private readonly DmsMasterDataInspector _inspector = new();
    private readonly DmsMasterDataService _service;

    private List<DmsOrganizationUnit> _organizationUnits = new();
    private List<DmsPerson> _people = new();
    private List<UnitDimension> _dimensions = new();
    private List<UnitDefinition> _units = new();
    private List<DmsUserPersonLink> _userLinks = new();
    private IReadOnlyList<DmsMasterDataHealthResult> _health =
        Array.Empty<DmsMasterDataHealthResult>();

    public FrameworkMasterDataView(
        string masterDataRoot,
        string usersPath,
        Func<string, string> translate,
        Action<string> executeTransaction,
        Action<string, string> log)
    {
        InitializeComponent();

        _masterDataRoot = masterDataRoot;
        _usersPath = usersPath;
        _translate = translate;
        _executeTransaction = executeTransaction;
        _log = log;
        _service = new DmsMasterDataService(masterDataRoot);

        ApplyLocalization();

        Loaded += (_, _) => Reload();
    }

    private string T(
        string key,
        string fallback)
    {
        var value = _translate(key);

        return string.IsNullOrWhiteSpace(value) ||
               value.StartsWith(
                   "[[",
                   StringComparison.Ordinal)
            ? fallback
            : value;
    }

    private void ApplyLocalization()
    {
        TitleText.Text =
            T(
                "Framework.FW09.Title",
                "FW09 — Core Master Data");

        SubtitleText.Text =
            T(
                "Framework.FW09.Description",
                "Central registry and integrity view for shared master data used across DMS modules.");

        ReloadButton.Content =
            T(
                "Framework.FW09.Reload",
                "Reload");

        Sys01Button.Content =
            T(
                "Framework.FW09.OpenSys01",
                "Open SYS01");

        UsersButton.Content =
            T(
                "Framework.FW09.OpenUsr01",
                "Open USR01");

        CopyButton.Content =
            T(
                "Framework.FW09.Copy",
                "Copy health report");

        OrganizationsLabel.Text =
            T(
                "Framework.FW09.Organizations",
                "Organization units");

        PeopleLabel.Text =
            T(
                "Framework.FW09.People",
                "People");

        DimensionsLabel.Text =
            T(
                "Framework.FW09.Dimensions",
                "Unit dimensions");

        UnitsLabel.Text =
            T(
                "Framework.FW09.Units",
                "Units");

        HealthLabel.Text =
            T(
                "Framework.FW09.Health",
                "Health");

        EntityColumn.Header =
            T(
                "Framework.FW09.Column.Entity",
                "Entity");

        EntityNameColumn.Header =
            T(
                "Framework.FW09.Column.Name",
                "Name");

        FileColumn.Header =
            T(
                "Framework.FW09.Column.File",
                "File");

        CountColumn.Header =
            T(
                "Framework.FW09.Column.Count",
                "Count");

        ActiveColumn.Header =
            T(
                "Framework.FW09.Column.Active",
                "Active");

        KeyColumn.Header =
            T(
                "Framework.FW09.Column.Key",
                "Key");

        DependenciesColumn.Header =
            T(
                "Framework.FW09.Column.Dependencies",
                "Dependencies");

        PathColumn.Header =
            T(
                "Framework.FW09.Column.Path",
                "Path");

        DescriptionColumn.Header =
            T(
                "Framework.FW09.Column.Description",
                "Purpose");

        SeverityColumn.Header =
            T(
                "Framework.FW09.Column.Severity",
                "Severity");

        AreaColumn.Header =
            T(
                "Framework.FW09.Column.Area",
                "Area");

        CheckColumn.Header =
            T(
                "Framework.FW09.Column.Check",
                "Integrity check");

        DetailsColumn.Header =
            T(
                "Framework.FW09.Column.Details",
                "Details");

        FooterText.Text =
            T(
                "Framework.FW09.Footer",
                "FW09 is read-only. Organization units, people and units remain edited in SYS01; DMS user linkage remains edited in USR01.");
    }

    private void ReloadButton_Click(
        object sender,
        RoutedEventArgs e) =>
        Reload();

    private void Sys01Button_Click(
        object sender,
        RoutedEventArgs e) =>
        _executeTransaction("SYS01");

    private void UsersButton_Click(
        object sender,
        RoutedEventArgs e) =>
        _executeTransaction("USR01");

    private void CopyButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var builder = new StringBuilder();

        builder.AppendLine(TitleText.Text);
        builder.AppendLine(
            $"MasterDataRoot={_masterDataRoot}");
        builder.AppendLine(
            $"UsersPath={_usersPath}");
        builder.AppendLine();

        foreach (var row in _health)
        {
            builder.AppendLine(
                $"{row.Severity}\t{row.Area}\t{row.Check}\t{row.Details}");
        }

        Clipboard.SetText(
            builder.ToString());

        _log(
            "MASTER_DATA_HEALTH_COPY",
            $"Checks={_health.Count}");
    }

    private void Reload()
    {
        _organizationUnits =
            _service.LoadOrganizationUnits();

        _people =
            _service.LoadPeople();

        _dimensions =
            _service.LoadUnitDimensions();

        _units =
            _service.LoadUnits();

        _userLinks =
            LoadUserLinks();

        _health =
            _inspector.Inspect(
                _organizationUnits,
                _people,
                _dimensions,
                _units,
                _userLinks);

        OrganizationsValue.Text =
            $"{_organizationUnits.Count} / {_organizationUnits.Count(x => x.IsActive)}";

        PeopleValue.Text =
            $"{_people.Count} / {_people.Count(x => x.IsActive)}";

        DimensionsValue.Text =
            $"{_dimensions.Count} / {_dimensions.Count(x => x.IsActive)}";

        UnitsValue.Text =
            $"{_units.Count} / {_units.Count(x => x.IsActive)}";

        var errors =
            _health.Count(x =>
                x.Severity == "ERROR");

        var warnings =
            _health.Count(x =>
                x.Severity == "WARNING");

        HealthValue.Text =
            errors == 0 && warnings == 0
                ? "OK"
                : $"{errors}E / {warnings}W";

        RegistryGrid.ItemsSource =
            BuildRegistryRows();

        HealthGrid.ItemsSource =
            _health;

        _log(
            "MASTER_DATA_OVERVIEW",
            $"OrganizationUnits={_organizationUnits.Count}; People={_people.Count}; Dimensions={_dimensions.Count}; Units={_units.Count}; HealthErrors={errors}; HealthWarnings={warnings}");
    }

    private IReadOnlyList<RegistryRow> BuildRegistryRows()
    {
        return DmsMasterDataRegistry.All
            .Select(descriptor =>
            {
                var (count, active, path) =
                    descriptor.Code switch
                    {
                        "ORGANIZATION_UNITS" =>
                            (
                                _organizationUnits.Count,
                                _organizationUnits.Count(x => x.IsActive),
                                _service.OrganizationUnitsPath),

                        "PEOPLE" =>
                            (
                                _people.Count,
                                _people.Count(x => x.IsActive),
                                _service.PeoplePath),

                        "UNIT_DIMENSIONS" =>
                            (
                                _dimensions.Count,
                                _dimensions.Count(x => x.IsActive),
                                _service.UnitDimensionsPath),

                        "UNITS" =>
                            (
                                _units.Count,
                                _units.Count(x => x.IsActive),
                                _service.UnitsPath),

                        "USERS" =>
                            (
                                _userLinks.Count,
                                _userLinks.Count(x => x.IsActive),
                                _usersPath),

                        _ =>
                            (
                                0,
                                0,
                                Path.Combine(
                                    _masterDataRoot,
                                    descriptor.FileName))
                    };

                return new RegistryRow(
                    descriptor.Code,
                    descriptor.Name,
                    descriptor.FileName,
                    count,
                    active,
                    descriptor.KeyField,
                    descriptor.Dependencies.Count == 0
                        ? "—"
                        : string.Join(
                            ", ",
                            descriptor.Dependencies),
                    path,
                    descriptor.Description);
            })
            .ToList();
    }

    private List<DmsUserPersonLink> LoadUserLinks()
    {
        if (!File.Exists(_usersPath))
        {
            return new List<DmsUserPersonLink>();
        }

        try
        {
            using var reader =
                new StreamReader(
                    _usersPath,
                    detectEncodingFromByteOrderMarks: true);

            var text =
                reader.ReadToEnd();

            using var document =
                JsonDocument.Parse(text);

            if (document.RootElement.ValueKind !=
                JsonValueKind.Array)
            {
                return new List<DmsUserPersonLink>();
            }

            var rows =
                new List<DmsUserPersonLink>();

            foreach (var item in
                     document.RootElement.EnumerateArray())
            {
                var login =
                    GetString(
                        item,
                        "WindowsLogin",
                        "windowsLogin",
                        "Login",
                        "login");

                Guid? personId = null;

                var rawPersonId =
                    GetString(
                        item,
                        "PersonId",
                        "personId");

                if (Guid.TryParse(
                        rawPersonId,
                        out var parsedPersonId))
                {
                    personId =
                        parsedPersonId;
                }

                rows.Add(
                    new DmsUserPersonLink(
                        login,
                        personId,
                        GetBoolean(
                            item,
                            defaultValue: true,
                            "IsActive",
                            "isActive")));
            }

            return rows;
        }
        catch
        {
            return new List<DmsUserPersonLink>();
        }
    }

    private static string GetString(
        JsonElement element,
        params string[] names)
    {
        if (element.ValueKind !=
            JsonValueKind.Object)
        {
            return string.Empty;
        }

        foreach (var property in
                 element.EnumerateObject())
        {
            if (names.Any(name =>
                    string.Equals(
                        name,
                        property.Name,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return property.Value.ValueKind ==
                       JsonValueKind.String
                    ? property.Value.GetString() ??
                      string.Empty
                    : property.Value.ToString();
            }
        }

        return string.Empty;
    }

    private static bool GetBoolean(
        JsonElement element,
        bool defaultValue,
        params string[] names)
    {
        if (element.ValueKind !=
            JsonValueKind.Object)
        {
            return defaultValue;
        }

        foreach (var property in
                 element.EnumerateObject())
        {
            if (!names.Any(name =>
                    string.Equals(
                        name,
                        property.Name,
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (property.Value.ValueKind ==
                JsonValueKind.True)
            {
                return true;
            }

            if (property.Value.ValueKind ==
                JsonValueKind.False)
            {
                return false;
            }
        }

        return defaultValue;
    }

    private sealed record RegistryRow(
        string Code,
        string Name,
        string FileName,
        int Count,
        int Active,
        string KeyField,
        string Dependencies,
        string Path,
        string Description);
}
