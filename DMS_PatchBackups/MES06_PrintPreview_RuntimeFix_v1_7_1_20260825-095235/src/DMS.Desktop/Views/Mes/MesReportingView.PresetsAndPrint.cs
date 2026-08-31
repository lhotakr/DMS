using System.Collections;
using System.Globalization;
using System.Printing;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;

namespace DMS.Desktop.Views.Mes;

public partial class MesReportingView
{
    private sealed class Mes06PresetDocument
    {
        public int Version { get; set; } = 1;
        public string DefaultPresetName { get; set; } = string.Empty;
        public List<Mes06ReportPreset> Presets { get; set; } = new();
    }

    private sealed class Mes06ReportPreset
    {
        public string Name { get; set; } = string.Empty;
        public string ReportCode { get; set; } = string.Empty;
        public string QuickPeriodCode { get; set; } = "YESTERDAY";
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public bool SelectAllWorkcenters { get; set; } = true;
        public List<string> WorkcenterCodes { get; set; } = new();
        public string ShiftCode { get; set; } = string.Empty;
        public string Article { get; set; } = string.Empty;
        public string Order { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;
    }

    private readonly JsonSerializerOptions _mes06PresetJsonOptions =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    private Mes06PresetDocument _mes06PresetDocument = new();
    private string _mes06PresetPath = string.Empty;
    private string _activePresetName = string.Empty;
    private string _pendingPresetShiftCode = string.Empty;
    private bool _hasPendingPresetShift;
    private bool _presetUiUpdating;

    private void InitializeReportToolbar()
    {
        TxtPresetMenuTitle.Text =
            T(
                "MES06.Toolbar.Presets",
                "Presets");

        BtnSavePreset.Content =
            T(
                "MES06.Preset.Save",
                "Save preset...");

        BtnDeletePreset.Content =
            T(
                "MES06.Preset.Delete",
                "Delete current preset");

        BtnToolbarPrint.Content =
            T(
                "MES06.Toolbar.Print",
                "Print");

        BtnToolbarPreview.Content =
            T(
                "MES06.Toolbar.Preview",
                "Preview");

        BtnToolbarExcel.Content =
            T(
                "MES06.Toolbar.Excel",
                "Excel");

        _mes06PresetPath =
            BuildPresetFilePath();

        var fileExisted =
            File.Exists(
                _mes06PresetPath);

        _mes06PresetDocument =
            LoadPresetDocument();

        if (!fileExisted)
        {
            CreateStarterPreset();
            SavePresetDocument();
        }

        RefreshPresetMenu();
    }

    private string BuildPresetFilePath()
    {
        var root =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "DMS",
                "MES06");

        Directory.CreateDirectory(
            root);

        var rawUser =
            string.IsNullOrWhiteSpace(
                _user)
                ? "default"
                : _user.Trim();

        var invalid =
            Path.GetInvalidFileNameChars();

        var safeUser =
            new string(
                rawUser
                    .Select(ch =>
                        invalid.Contains(ch)
                            ? '_'
                            : ch)
                    .ToArray());

        return Path.Combine(
            root,
            $"presets-{safeUser}.json");
    }

    private Mes06PresetDocument LoadPresetDocument()
    {
        try
        {
            if (!File.Exists(
                    _mes06PresetPath))
            {
                return new Mes06PresetDocument();
            }

            var json =
                File.ReadAllText(
                    _mes06PresetPath,
                    Encoding.UTF8);

            return JsonSerializer.Deserialize<Mes06PresetDocument>(
                       json,
                       _mes06PresetJsonOptions)
                   ?? new Mes06PresetDocument();
        }
        catch (Exception ex)
        {
            _logger.Error(
                "MES06 preset file could not be loaded.",
                ex);

            return new Mes06PresetDocument();
        }
    }

    private void SavePresetDocument()
    {
        try
        {
            var directory =
                Path.GetDirectoryName(
                    _mes06PresetPath);

            if (!string.IsNullOrWhiteSpace(
                    directory))
            {
                Directory.CreateDirectory(
                    directory);
            }

            var json =
                JsonSerializer.Serialize(
                    _mes06PresetDocument,
                    _mes06PresetJsonOptions);

            var temp =
                _mes06PresetPath + ".tmp";

            File.WriteAllText(
                temp,
                json,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false));

            File.Move(
                temp,
                _mes06PresetPath,
                overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.Error(
                "MES06 preset file could not be saved.",
                ex);

            MessageBox.Show(
                T(
                    "MES06.Preset.SaveFailed",
                    "The preset could not be saved."),
                "MES06",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CreateStarterPreset()
    {
        var reportCode =
            (CmbReport.SelectedItem
                as DMS.Integration.Mes.Reporting.Definitions.MesReportDefinition)
            ?.Code
            ?? string.Empty;

        var starter =
            new Mes06ReportPreset
            {
                Name =
                    T(
                        "MES06.Preset.Starter.DailyScrap",
                        "Daily scrap"),
                ReportCode =
                    reportCode,
                QuickPeriodCode =
                    "YESTERDAY",
                SelectAllWorkcenters =
                    true
            };

        _mes06PresetDocument.Presets.Add(
            starter);

        _mes06PresetDocument.DefaultPresetName =
            starter.Name;

        _activePresetName =
            starter.Name;
    }

    private void RefreshPresetMenu()
    {
        _presetUiUpdating = true;

        try
        {
            var items =
                _mes06PresetDocument.Presets
                    .OrderBy(preset =>
                        preset.Name,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

            LstReportPresets.ItemsSource =
                items;

            var selectedName =
                !string.IsNullOrWhiteSpace(
                    _activePresetName)
                    ? _activePresetName
                    : _mes06PresetDocument.DefaultPresetName;

            var selected =
                items.FirstOrDefault(preset =>
                    string.Equals(
                        preset.Name,
                        selectedName,
                        StringComparison.CurrentCultureIgnoreCase));

            LstReportPresets.SelectedItem =
                selected;

            TxtActivePreset.Text =
                selected?.Name
                ?? T(
                    "MES06.Toolbar.Presets",
                    "Presets");

            BtnDeletePreset.IsEnabled =
                selected is not null;
        }
        finally
        {
            _presetUiUpdating = false;
        }
    }

    private void ApplyStartupPresetAfterWorkcenters()
    {
        var preset =
            FindPreset(
                _mes06PresetDocument.DefaultPresetName)
            ?? _mes06PresetDocument.Presets.FirstOrDefault();

        if (preset is null)
        {
            RefreshPresetMenu();
            return;
        }

        _activePresetName =
            preset.Name;

        ApplyPresetFilters(
            preset);

        RefreshPresetMenu();
    }

    private Mes06ReportPreset? FindPreset(
        string name)
    {
        if (string.IsNullOrWhiteSpace(
                name))
        {
            return null;
        }

        return _mes06PresetDocument.Presets
            .FirstOrDefault(preset =>
                string.Equals(
                    preset.Name,
                    name,
                    StringComparison.CurrentCultureIgnoreCase));
    }

    private void ApplyPresetFilters(
        Mes06ReportPreset preset)
    {
        _mes06InitializingFilters = true;

        try
        {
            if (!string.IsNullOrWhiteSpace(
                    preset.ReportCode))
            {
                var definition =
                    _definitions.FirstOrDefault(item =>
                        string.Equals(
                            item.Code,
                            preset.ReportCode,
                            StringComparison.OrdinalIgnoreCase));

                if (definition is not null)
                {
                    CmbReport.SelectedItem =
                        definition;
                }
            }

            var quick =
                CmbQuickPeriod.Items
                    .OfType<Mes06FilterChoice>()
                    .FirstOrDefault(item =>
                        string.Equals(
                            item.Code,
                            preset.QuickPeriodCode,
                            StringComparison.OrdinalIgnoreCase))
                ?? CmbQuickPeriod.Items
                    .OfType<Mes06FilterChoice>()
                    .FirstOrDefault(item =>
                        string.Equals(
                            item.Code,
                            "CUSTOM",
                            StringComparison.OrdinalIgnoreCase));

            CmbQuickPeriod.SelectedItem =
                quick;

            ApplyPresetPeriod(
                preset);

            ApplyPresetWorkcenters(
                preset);

            TxtProduct.Text =
                preset.Article
                ?? string.Empty;

            TxtOrder.Text =
                preset.Order
                ?? string.Empty;

            TxtOperation.Text =
                preset.Operation
                ?? string.Empty;

            _pendingPresetShiftCode =
                preset.ShiftCode
                ?? string.Empty;

            // Empty ShiftCode is meaningful: it means "All shifts".
            // Keep a separate flag so an ordinary report reload does not
            // accidentally interpret "nothing pending" as "All shifts".
            _hasPendingPresetShift =
                true;
        }
        finally
        {
            _mes06InitializingFilters = false;
        }

        UpdateWorkcenterSelectionSummary();
        ApplySelectedDefinition();
    }

    private void ApplyPresetPeriod(
        Mes06ReportPreset preset)
    {
        var today =
            DateTime.Today;

        switch (
            preset.QuickPeriodCode?.Trim().ToUpperInvariant())
        {
            case "TODAY":
                DateFrom.SelectedDate =
                    today;

                DateTo.SelectedDate =
                    today.AddDays(1);
                break;

            case "YESTERDAY":
                DateFrom.SelectedDate =
                    today.AddDays(-1);

                DateTo.SelectedDate =
                    today;
                break;

            case "LAST7":
                DateFrom.SelectedDate =
                    today.AddDays(-6);

                DateTo.SelectedDate =
                    today.AddDays(1);
                break;

            case "THISWEEK":
                var offset =
                    ((int)today.DayOfWeek + 6) % 7;

                var monday =
                    today.AddDays(-offset);

                DateFrom.SelectedDate =
                    monday;

                DateTo.SelectedDate =
                    monday.AddDays(7);
                break;

            default:
                DateFrom.SelectedDate =
                    preset.From
                    ?? today;

                DateTo.SelectedDate =
                    preset.To
                    ?? (
                        preset.From
                        ?? today
                    ).AddDays(1);
                break;
        }
    }

    private void ApplyPresetWorkcenters(
        Mes06ReportPreset preset)
    {
        if (_mes06Workcenters.Count == 0)
        {
            return;
        }

        if (preset.SelectAllWorkcenters)
        {
            foreach (var item
                     in _mes06Workcenters)
            {
                item.IsSelected = true;
            }

            return;
        }

        var selectedCodes =
            new HashSet<string>(
                preset.WorkcenterCodes
                ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var item
                 in _mes06Workcenters)
        {
            item.IsSelected =
                selectedCodes.Contains(
                    item.Code);
        }
    }

    private void ApplyPendingPresetShift()
    {
        if (!_hasPendingPresetShift)
        {
            return;
        }

        var choices =
            CmbShift.Items
                .OfType<Mes06FilterChoice>()
                .ToList();

        if (choices.Count == 0)
        {
            // Keep the pending flag. The database-backed shift choices
            // may become available on the next report load.
            return;
        }

        var selected =
            choices.FirstOrDefault(choice =>
                string.Equals(
                    choice.Code,
                    _pendingPresetShiftCode,
                    StringComparison.CurrentCultureIgnoreCase))
            ?? choices[0];

        CmbShift.SelectedItem =
            selected;

        _pendingPresetShiftCode =
            string.Empty;

        _hasPendingPresetShift =
            false;
    }

    private Mes06ReportPreset CaptureCurrentPreset(
        string name)
    {
        var selectedWorkcenters =
            GetSelectedWorkcenterCodes()
                .ToList();

        var allSelected =
            _mes06Workcenters.Count > 0
            && selectedWorkcenters.Count == _mes06Workcenters.Count;

        return new Mes06ReportPreset
        {
            Name =
                name.Trim(),

            ReportCode =
                (CmbReport.SelectedItem
                    as DMS.Integration.Mes.Reporting.Definitions.MesReportDefinition)
                ?.Code
                ?? string.Empty,

            QuickPeriodCode =
                (CmbQuickPeriod.SelectedItem
                    as Mes06FilterChoice)
                ?.Code
                ?? "CUSTOM",

            From =
                DateFrom.SelectedDate,

            To =
                DateTo.SelectedDate,

            SelectAllWorkcenters =
                allSelected,

            WorkcenterCodes =
                allSelected
                    ? new List<string>()
                    : selectedWorkcenters,

            ShiftCode =
                (CmbShift.SelectedItem
                    as Mes06FilterChoice)
                ?.Code
                ?? string.Empty,

            Article =
                TxtProduct.Text?.Trim()
                ?? string.Empty,

            Order =
                TxtOrder.Text?.Trim()
                ?? string.Empty,

            Operation =
                TxtOperation.Text?.Trim()
                ?? string.Empty
        };
    }

    private async void LstReportPresets_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_presetUiUpdating
            || LstReportPresets.SelectedItem
                is not Mes06ReportPreset preset)
        {
            return;
        }

        _activePresetName =
            preset.Name;

        _mes06PresetDocument.DefaultPresetName =
            preset.Name;

        SavePresetDocument();

        ApplyPresetFilters(
            preset);

        RefreshPresetMenu();

        PopupPresets.IsOpen =
            false;

        BtnPresetMenu.IsChecked =
            false;

        await LoadCurrentReportAsync();
    }

    private void BtnSavePreset_Click(
        object sender,
        RoutedEventArgs e)
    {
        var suggested =
            !string.IsNullOrWhiteSpace(
                _activePresetName)
                ? _activePresetName
                : T(
                    "MES06.Preset.NewName",
                    "New preset");

        var name =
            ShowPresetNameDialog(
                suggested);

        if (string.IsNullOrWhiteSpace(
                name))
        {
            return;
        }

        var existing =
            FindPreset(
                name);

        if (existing is not null)
        {
            var overwrite =
                MessageBox.Show(
                    string.Format(
                        T(
                            "MES06.Preset.OverwriteConfirm",
                            "Preset '{0}' already exists. Overwrite it?"),
                        name),
                    "MES06",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

            if (overwrite !=
                MessageBoxResult.Yes)
            {
                return;
            }

            _mes06PresetDocument.Presets.Remove(
                existing);
        }

        var preset =
            CaptureCurrentPreset(
                name);

        _mes06PresetDocument.Presets.Add(
            preset);

        _mes06PresetDocument.DefaultPresetName =
            preset.Name;

        _activePresetName =
            preset.Name;

        SavePresetDocument();
        RefreshPresetMenu();

        PopupPresets.IsOpen =
            false;

        BtnPresetMenu.IsChecked =
            false;

        _logger.AdminAction(
            "MES06",
            "SaveReportPreset",
            _user,
            $"Preset={preset.Name}; Report={preset.ReportCode}; Workcenters={(preset.SelectAllWorkcenters ? "ALL" : string.Join(",", preset.WorkcenterCodes))}; Shift={preset.ShiftCode}; Quick={preset.QuickPeriodCode}; From={preset.From:O}; To={preset.To:O}; Article={preset.Article}; Order={preset.Order}; Operation={preset.Operation}");
    }

    private void BtnDeletePreset_Click(
        object sender,
        RoutedEventArgs e)
    {
        var preset =
            FindPreset(
                _activePresetName);

        if (preset is null)
        {
            return;
        }

        var answer =
            MessageBox.Show(
                string.Format(
                    T(
                        "MES06.Preset.DeleteConfirm",
                        "Delete preset '{0}'?"),
                    preset.Name),
                "MES06",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

        if (answer !=
            MessageBoxResult.Yes)
        {
            return;
        }

        _mes06PresetDocument.Presets.Remove(
            preset);

        var next =
            _mes06PresetDocument.Presets
                .OrderBy(item =>
                    item.Name,
                    StringComparer.CurrentCultureIgnoreCase)
                .FirstOrDefault();

        _activePresetName =
            next?.Name
            ?? string.Empty;

        _mes06PresetDocument.DefaultPresetName =
            _activePresetName;

        SavePresetDocument();
        RefreshPresetMenu();

        PopupPresets.IsOpen =
            false;

        BtnPresetMenu.IsChecked =
            false;

        _logger.AdminAction(
            "MES06",
            "DeleteReportPreset",
            _user,
            $"Preset={preset.Name}");
    }

    private string? ShowPresetNameDialog(
        string initialValue)
    {
        var owner =
            Window.GetWindow(
                this);

        var dialog =
            new Window
            {
                Title =
                    T(
                        "MES06.Preset.DialogTitle",
                        "Save report preset"),
                Width = 430,
                Height = 180,
                ResizeMode =
                    ResizeMode.NoResize,
                WindowStartupLocation =
                    WindowStartupLocation.CenterOwner,
                Owner =
                    owner,
                ShowInTaskbar =
                    false
            };

        dialog.SetResourceReference(
            Window.BackgroundProperty,
            "DmsPanelBrush");

        var grid =
            new Grid
            {
                Margin =
                    new Thickness(
                        16)
            };

        grid.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });

        grid.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });

        grid.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });

        var label =
            new TextBlock
            {
                Text =
                    T(
                        "MES06.Preset.NameLabel",
                        "Preset name"),
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        6)
            };

        label.SetResourceReference(
            TextBlock.ForegroundProperty,
            "DmsForegroundBrush");

        Grid.SetRow(
            label,
            0);

        grid.Children.Add(
            label);

        var textBox =
            new TextBox
            {
                Text =
                    initialValue,
                Height =
                    30,
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        14)
            };

        Grid.SetRow(
            textBox,
            1);

        grid.Children.Add(
            textBox);

        var buttons =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,
                HorizontalAlignment =
                    HorizontalAlignment.Right
            };

        var save =
            new Button
            {
                Content =
                    T(
                        "MES06.Preset.SaveButton",
                        "Save"),
                Width =
                    90,
                Height =
                    30,
                Margin =
                    new Thickness(
                        0,
                        0,
                        8,
                        0),
                IsDefault =
                    true
            };

        var cancel =
            new Button
            {
                Content =
                    T(
                        "MES06.Preset.CancelButton",
                        "Cancel"),
                Width =
                    90,
                Height =
                    30,
                IsCancel =
                    true
            };

        string? result =
            null;

        save.Click +=
            (_, _) =>
            {
                var value =
                    textBox.Text?.Trim();

                if (string.IsNullOrWhiteSpace(
                        value))
                {
                    return;
                }

                result =
                    value;

                dialog.DialogResult =
                    true;
            };

        buttons.Children.Add(
            save);

        buttons.Children.Add(
            cancel);

        Grid.SetRow(
            buttons,
            2);

        grid.Children.Add(
            buttons);

        dialog.Content =
            grid;

        dialog.Loaded +=
            (_, _) =>
            {
                textBox.Focus();
                textBox.SelectAll();
            };

        return dialog.ShowDialog() == true
            ? result
            : null;
    }

    private void BtnToolbarExcel_Click(
        object sender,
        RoutedEventArgs e)
    {
        BtnExportExcel_Click(
            sender,
            e);
    }

    private void BtnToolbarPreview_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_currentRows.Count == 0)
        {
            ShowNoPrintData();
            return;
        }

        var document =
            BuildPrintableDocument();

        var viewer =
            new DocumentViewer
            {
                Document =
                    document
            };

        var window =
            new Window
            {
                Title =
                    T(
                        "MES06.Print.PreviewTitle",
                        "MES06 print preview"),
                Width =
                    1280,
                Height =
                    820,
                WindowStartupLocation =
                    WindowStartupLocation.CenterOwner,
                Owner =
                    Window.GetWindow(
                        this),
                Content =
                    viewer
            };

        window.ShowDialog();
    }

    private void BtnToolbarPrint_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_currentRows.Count == 0)
        {
            ShowNoPrintData();
            return;
        }

        var printDialog =
            new PrintDialog();

        if (printDialog.ShowDialog() !=
            true)
        {
            return;
        }

        if (printDialog.PrintTicket is not null)
        {
            printDialog.PrintTicket.PageOrientation =
                PageOrientation.Landscape;
        }

        var document =
            BuildPrintableDocument();

        if (printDialog.PrintableAreaWidth > 0)
        {
            document.PageWidth =
                printDialog.PrintableAreaWidth;
        }

        if (printDialog.PrintableAreaHeight > 0)
        {
            document.PageHeight =
                printDialog.PrintableAreaHeight;
        }

        var printReportName =
            (CmbReport.SelectedItem
                as DMS.Integration.Mes.Reporting.Definitions.MesReportDefinition)
            ?.Name
            ?? T(
                "MES06.Title",
                "MES06");

        printDialog.PrintDocument(
            ((IDocumentPaginatorSource)document)
                .DocumentPaginator,
            $"MES06 - {printReportName}");

        _logger.AdminAction(
            "MES06",
            "PrintReport",
            _user,
            $"Rows={_currentRows.Count}; Preset={_activePresetName}");
    }

    private void ShowNoPrintData()
    {
        MessageBox.Show(
            T(
                "MES06.Print.NoData",
                "There is no report data to print."),
            "MES06",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private FlowDocument BuildPrintableDocument()
    {
        var document =
            new FlowDocument
            {
                PageWidth =
                    1122,
                PageHeight =
                    793,
                PagePadding =
                    new Thickness(
                        30),
                ColumnWidth =
                    double.PositiveInfinity,
                FontFamily =
                    new FontFamily(
                        "Segoe UI"),
                FontSize =
                    8,
                Foreground =
                    Brushes.Black,
                Background =
                    Brushes.White
            };

        var definitionName =
            (CmbReport.SelectedItem
                as DMS.Integration.Mes.Reporting.Definitions.MesReportDefinition)
            ?.Name
            ?? T(
                "MES06.Report.Counter.Name",
                "Counter report");

        var title =
            new Paragraph
            {
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        4),
                FontSize =
                    17,
                FontWeight =
                    FontWeights.Bold
            };

        title.Inlines.Add(
            $"{TxtTitle.Text} – {definitionName}");

        document.Blocks.Add(
            title);

        var filters =
            new Paragraph
            {
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        10),
                FontSize =
                    8.5
            };

        filters.Inlines.Add(
            BuildPrintFilterSummary());

        document.Blocks.Add(
            filters);

        AppendVisibleChartToDocument(
            document);

        AppendGridToDocument(
            document,
            GridReport,
            _currentRows);

        if (CounterSummaryBorder.Visibility ==
                Visibility.Visible
            && GridCounterSummary.ItemsSource
                is IEnumerable summaryRows)
        {
            var summaryTitle =
                new Paragraph
                {
                    Margin =
                        new Thickness(
                            0,
                            14,
                            0,
                            5),
                    FontSize =
                        12,
                    FontWeight =
                        FontWeights.Bold
                };

            summaryTitle.Inlines.Add(
                TxtCounterSummaryTitle.Text);

            document.Blocks.Add(
                summaryTitle);

            AppendGridToDocument(
                document,
                GridCounterSummary,
                summaryRows.Cast<object>().ToList());
        }

        var footer =
            new Paragraph
            {
                Margin =
                    new Thickness(
                        0,
                        10,
                        0,
                        0),
                FontSize =
                    7,
                Foreground =
                    Brushes.DimGray
            };

        footer.Inlines.Add(
            string.Format(
                T(
                    "MES06.Print.Generated",
                    "Generated {0} by {1}"),
                DateTime.Now.ToString(
                    "dd.MM.yyyy HH:mm:ss"),
                _user));

        document.Blocks.Add(
            footer);

        return document;
    }

    private string BuildPrintFilterSummary()
    {
        var parts =
            new List<string>();

        parts.Add(
            $"{LblFrom.Text}: {DateFrom.SelectedDate:dd.MM.yyyy}");

        parts.Add(
            $"{LblTo.Text}: {DateTo.SelectedDate:dd.MM.yyyy}");

        parts.Add(
            $"{LblWorkcenter.Text}: {TxtWorkcenterSelectionSummary.Text}");

        var shift =
            (CmbShift.SelectedItem
                as Mes06FilterChoice)
            ?.Text;

        if (!string.IsNullOrWhiteSpace(
                shift))
        {
            parts.Add(
                $"{LblShift.Text}: {shift}");
        }

        if (!string.IsNullOrWhiteSpace(
                TxtProduct.Text))
        {
            parts.Add(
                $"{LblProduct.Text}: {TxtProduct.Text.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(
                TxtOrder.Text))
        {
            parts.Add(
                $"{LblOrder.Text}: {TxtOrder.Text.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(
                TxtOperation.Text))
        {
            parts.Add(
                $"{LblOperation.Text}: {TxtOperation.Text.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(
                _activePresetName))
        {
            parts.Add(
                $"{T("MES06.Toolbar.Presets", "Preset")}: {_activePresetName}");
        }

        return string.Join(
            "   |   ",
            parts);
    }

    private void AppendVisibleChartToDocument(
        FlowDocument document)
    {
        if (ChartBorder.Visibility !=
                Visibility.Visible
            || ChartHost.Content is null)
        {
            return;
        }

        try
        {
            ChartBorder.UpdateLayout();

            var width =
                (int)Math.Ceiling(
                    ChartBorder.ActualWidth);

            var height =
                (int)Math.Ceiling(
                    ChartBorder.ActualHeight);

            if (width < 10
                || height < 10)
            {
                return;
            }

            var bitmap =
                new RenderTargetBitmap(
                    width,
                    height,
                    96d,
                    96d,
                    PixelFormats.Pbgra32);

            bitmap.Render(
                ChartBorder);

            if (bitmap.CanFreeze)
            {
                bitmap.Freeze();
            }

            var availableWidth =
                Math.Max(
                    600d,
                    document.PageWidth
                    - document.PagePadding.Left
                    - document.PagePadding.Right);

            var image =
                new Image
                {
                    Source =
                        bitmap,
                    Stretch =
                        Stretch.Uniform,
                    Width =
                        Math.Min(
                            availableWidth,
                            width),
                    MaxHeight =
                        235d,
                    HorizontalAlignment =
                        HorizontalAlignment.Left
                };

            var block =
                new BlockUIContainer(
                    image)
                {
                    Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            10)
                };

            document.Blocks.Add(
                block);
        }
        catch (Exception ex)
        {
            // A printer/preview must still work even when a GPU-backed
            // chart cannot be rasterized on a specific workstation.
            _logger.Error(
                "MES06 print chart rendering failed.",
                ex);
        }
    }

    private void AppendGridToDocument(
        FlowDocument document,
        DataGrid grid,
        IReadOnlyList<object> rows)
    {
        var columns =
            grid.Columns
                .Where(column =>
                    column.Visibility ==
                    Visibility.Visible)
                .OrderBy(column =>
                    column.DisplayIndex)
                .ToList();

        if (columns.Count == 0)
        {
            return;
        }

        var table =
            new Table
            {
                CellSpacing =
                    0
            };

        var totalWidth =
            columns.Sum(column =>
                Math.Max(
                    40,
                    column.ActualWidth));

        var printableWidth =
            Math.Max(
                600,
                document.PageWidth
                - document.PagePadding.Left
                - document.PagePadding.Right
                - 10);

        foreach (var column
                 in columns)
        {
            var weight =
                Math.Max(
                    40,
                    column.ActualWidth)
                / totalWidth;

            table.Columns.Add(
                new TableColumn
                {
                    Width =
                        new GridLength(
                            printableWidth * weight)
                });
        }

        var rowGroup =
            new TableRowGroup();

        table.RowGroups.Add(
            rowGroup);

        var header =
            new TableRow
            {
                Background =
                    Brushes.Gainsboro
            };

        foreach (var column
                 in columns)
        {
            header.Cells.Add(
                CreatePrintCell(
                    Convert.ToString(
                        column.Header)
                    ?? string.Empty,
                    bold: true));
        }

        rowGroup.Rows.Add(
            header);

        foreach (var row
                 in rows)
        {
            var tableRow =
                new TableRow();

            foreach (var column
                     in columns)
            {
                tableRow.Cells.Add(
                    CreatePrintCell(
                        GetPrintableCellText(
                            column,
                            row),
                        bold: false));
            }

            rowGroup.Rows.Add(
                tableRow);
        }

        document.Blocks.Add(
            table);
    }

    private TableCell CreatePrintCell(
        string text,
        bool bold)
    {
        var paragraph =
            new Paragraph
            {
                Margin =
                    new Thickness(
                        2,
                        1,
                        2,
                        1),
                FontSize =
                    7.3,
                FontWeight =
                    bold
                        ? FontWeights.Bold
                        : FontWeights.Normal
            };

        paragraph.Inlines.Add(
            text);

        return new TableCell(
            paragraph)
        {
            BorderBrush =
                Brushes.LightGray,
            BorderThickness =
                new Thickness(
                    0.4),
            Padding =
                new Thickness(
                    1)
        };
    }

    private string GetPrintableCellText(
        DataGridColumn column,
        object row)
    {
        if (column
                is DataGridTextColumn textColumn
            && textColumn.Binding
                is Binding binding)
        {
            var property =
                binding.Path?.Path
                ?? string.Empty;

            property =
                property
                    .Trim()
                    .TrimStart('[')
                    .TrimEnd(']');

            var value =
                ReadProperty(
                    row,
                    property);

            return FormatPrintValue(
                value,
                binding.StringFormat);
        }

        var workers =
            ReadProperty(
                row,
                "WorkersDisplay");

        return Convert.ToString(
                   workers,
                   CultureInfo.CurrentCulture)
               ?? string.Empty;
    }

    private static string FormatPrintValue(
        object? value,
        string? format)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is DateTime dateTime)
        {
            return string.IsNullOrWhiteSpace(
                       format)
                ? dateTime.ToString(
                    "dd.MM.yyyy HH:mm:ss")
                : dateTime.ToString(
                    format,
                    CultureInfo.CurrentCulture);
        }

        if (!string.IsNullOrWhiteSpace(
                format)
            && value
                is IFormattable formattable)
        {
            return formattable.ToString(
                       format,
                       CultureInfo.CurrentCulture)
                   ?? string.Empty;
        }

        return Convert.ToString(
                   value,
                   CultureInfo.CurrentCulture)
               ?? string.Empty;
    }
}
