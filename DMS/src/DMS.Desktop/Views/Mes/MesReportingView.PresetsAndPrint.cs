using Microsoft.Win32;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Printing;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
        public string TimeFrom { get; set; } = string.Empty;
        public string TimeTo { get; set; } = string.Empty;
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

        BtnToolbarPdf.Content =
            T(
                "MES06.Toolbar.Pdf",
                "PDF");

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

        TimeFrom.Text =
            preset.TimeFrom
            ?? string.Empty;

        TimeTo.Text =
            preset.TimeTo
            ?? string.Empty;
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

            TimeFrom =
                TimeFrom.Text?.Trim()
                ?? string.Empty,

            TimeTo =
                TimeTo.Text?.Trim()
                ?? string.Empty,

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

    private void BtnToolbarPdf_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_currentRows.Count == 0)
        {
            ShowNoPrintData();
            return;
        }

        try
        {
            var reportName =
                (CmbReport.SelectedItem
                    as DMS.Integration.Mes.Reporting.Definitions.MesReportDefinition)
                ?.Name
                ?? T(
                    "MES06.Report.Counter.Name",
                    "MES06 report");

            var dialog =
                new SaveFileDialog
                {
                    Title =
                        T(
                            "MES06.Pdf.DialogTitle",
                            "Export report to PDF"),
                    Filter =
                        T(
                            "MES06.Pdf.FileFilter",
                            "PDF document (*.pdf)|*.pdf"),
                    DefaultExt =
                        ".pdf",
                    AddExtension =
                        true,
                    FileName =
                        BuildPdfFileName(
                            reportName)
                };

            if (dialog.ShowDialog() !=
                true)
            {
                return;
            }

            var document =
                BuildPrintableDocument();

            ExportFlowDocumentToPdf(
                document,
                dialog.FileName);

            _logger.AdminAction(
                "MES06",
                "ExportPdf",
                _user,
                $"Rows={_currentRows.Count}; Preset={_activePresetName}; File={dialog.FileName}");

            TxtStatus.Text =
                string.Format(
                    T(
                        "MES06.Pdf.Exported",
                        "PDF export created: {0}"),
                    dialog.FileName);


            OfferOpenExportedFile(
                dialog.FileName);
        }
        catch (Exception ex)
        {
            _logger.Error(
                "MES06 PDF export failed.",
                ex);

            MessageBox.Show(
                string.Format(
                    T(
                        "MES06.Pdf.ExportFailed",
                        "PDF export could not be created.{0}{1}"),
                    Environment.NewLine,
                    ex.Message),
                "MES06",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OfferOpenExportedFile(
        string filePath)
    {
        if (string.IsNullOrWhiteSpace(
                filePath)
            || !File.Exists(
                filePath))
        {
            return;
        }

        var answer =
            MessageBox.Show(
                string.Format(
                    T(
                        "MES06.Export.OpenPrompt",
                        "The file was created successfully:{0}{1}{0}{0}Open it now?"),
                    Environment.NewLine,
                    filePath),
                T(
                    "MES06.Export.OpenTitle",
                    "Open exported file"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

        if (answer !=
            MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName =
                        filePath,
                    UseShellExecute =
                        true
                });
        }
        catch (Exception ex)
        {
            _logger.Error(
                "MES06 exported file could not be opened.",
                ex);

            DmsMessage.Show(
                string.Format(
                    T(
                        "MES06.Export.OpenFailed",
                        "The exported file could not be opened.{0}{1}"),
                    Environment.NewLine,
                    ex.Message),
                "MES06",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
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

        try
        {
            var document =
                BuildPrintableDocument();

            var viewer =
                new FlowDocumentPageViewer
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
                    MinWidth =
                        900,
                    MinHeight =
                        600,
                    WindowStartupLocation =
                        WindowStartupLocation.CenterOwner,
                    Owner =
                        Window.GetWindow(
                            this),
                    Content =
                        viewer
                };

            _logger.AdminAction(
                "MES06",
                "OpenPrintPreview",
                _user,
                $"Rows={_currentRows.Count}; Preset={_activePresetName}");

            window.ShowDialog();
        }
        catch (Exception ex)
        {
            _logger.Error(
                "MES06 print preview failed.",
                ex);

            MessageBox.Show(
                string.Format(
                    T(
                        "MES06.Print.PreviewFailed",
                        "Print preview could not be created.{0}{1}"),
                    Environment.NewLine,
                    ex.Message),
                "MES06",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
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

        try
        {
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
        catch (Exception ex)
        {
            _logger.Error(
                "MES06 report printing failed.",
                ex);

            MessageBox.Show(
                string.Format(
                    T(
                        "MES06.Print.PrintFailed",
                        "The report could not be printed.{0}{1}"),
                    Environment.NewLine,
                    ex.Message),
                "MES06",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
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
                // WPF validates FlowDocument.ColumnWidth at runtime.
                // Use a large finite width to keep the landscape document
                // in one column without triggering dependency-property validation.
                ColumnWidth =
                    10000d,
                FontFamily =
                    new FontFamily(
                        "Segoe UI"),
                FontSize =
                    8,
                Foreground =
                    Brushes.Black,
                Background =
                    Brushes.White,
                IsColumnWidthFlexible =
                    false
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
            GetPrintableGridRows(
                GridReport));

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

    private sealed class Mes06PdfPageImage
    {
        public byte[] JpegBytes { get; init; } =
            Array.Empty<byte>();

        public int PixelWidth { get; init; }

        public int PixelHeight { get; init; }

        public double WidthPoints { get; init; }

        public double HeightPoints { get; init; }
    }

    private string BuildPdfFileName(
        string reportName)
    {
        var invalid =
            Path.GetInvalidFileNameChars();

        var safeReport =
            new string(
                reportName
                    .Select(ch =>
                        invalid.Contains(ch)
                            ? '_'
                            : ch)
                    .ToArray())
                .Trim();

        if (string.IsNullOrWhiteSpace(
                safeReport))
        {
            safeReport =
                "MES06";
        }

        return $"MES06_{safeReport}_{DateTime.Now:yyyyMMdd-HHmm}.pdf";
    }

    private void ExportFlowDocumentToPdf(
        FlowDocument document,
        string filePath)
    {
        var paginator =
            ((IDocumentPaginatorSource)document)
                .DocumentPaginator;

        paginator.PageSize =
            new Size(
                document.PageWidth,
                document.PageHeight);

        if (!paginator.IsPageCountValid)
        {
            paginator.ComputePageCount();
        }

        var pages =
            new List<Mes06PdfPageImage>();

        for (var pageIndex = 0;
             pageIndex < paginator.PageCount;
             pageIndex++)
        {
            var page =
                paginator.GetPage(
                    pageIndex);

            if (page == DocumentPage.Missing)
            {
                continue;
            }

            pages.Add(
                RenderDocumentPageForPdf(
                    page));
        }

        if (pages.Count == 0)
        {
            throw new InvalidOperationException(
                T(
                    "MES06.Pdf.NoPages",
                    "The report did not generate any PDF pages."));
        }

        WriteImagePdf(
            filePath,
            pages);
    }

    private static Mes06PdfPageImage RenderDocumentPageForPdf(
        DocumentPage page)
    {
        const double sourceDpi =
            96d;

        const double pdfRenderDpi =
            180d;

        var pageSize =
            page.Size;

        var pixelWidth =
            Math.Max(
                1,
                (int)Math.Ceiling(
                    pageSize.Width
                    * pdfRenderDpi
                    / sourceDpi));

        var pixelHeight =
            Math.Max(
                1,
                (int)Math.Ceiling(
                    pageSize.Height
                    * pdfRenderDpi
                    / sourceDpi));

        var drawing =
            new DrawingVisual();

        using (var context =
               drawing.RenderOpen())
        {
            context.DrawRectangle(
                Brushes.White,
                null,
                new Rect(
                    0,
                    0,
                    pageSize.Width,
                    pageSize.Height));

            var pageBrush =
                new VisualBrush(
                    page.Visual)
                {
                    Stretch =
                        Stretch.Fill,
                    AlignmentX =
                        AlignmentX.Left,
                    AlignmentY =
                        AlignmentY.Top
                };

            context.DrawRectangle(
                pageBrush,
                null,
                new Rect(
                    0,
                    0,
                    pageSize.Width,
                    pageSize.Height));
        }

        var bitmap =
            new RenderTargetBitmap(
                pixelWidth,
                pixelHeight,
                pdfRenderDpi,
                pdfRenderDpi,
                PixelFormats.Pbgra32);

        bitmap.Render(
            drawing);

        var encoder =
            new JpegBitmapEncoder
            {
                QualityLevel =
                    94
            };

        encoder.Frames.Add(
            BitmapFrame.Create(
                bitmap));

        using var jpegStream =
            new MemoryStream();

        encoder.Save(
            jpegStream);

        return new Mes06PdfPageImage
        {
            JpegBytes =
                jpegStream.ToArray(),
            PixelWidth =
                pixelWidth,
            PixelHeight =
                pixelHeight,
            WidthPoints =
                pageSize.Width
                * 72d
                / sourceDpi,
            HeightPoints =
                pageSize.Height
                * 72d
                / sourceDpi
        };
    }

    private static void WriteImagePdf(
        string filePath,
        IReadOnlyList<Mes06PdfPageImage> pages)
    {
        // Minimal PDF 1.4 writer.
        // Each FlowDocument page is embedded as one high-resolution JPEG.
        // This intentionally favors visual parity with Print Preview and
        // avoids introducing a second document-layout engine or NuGet package.
        var objectCount =
            2
            + pages.Count * 3;

        var offsets =
            new long[
                objectCount
                + 1];

        using var stream =
            new FileStream(
                filePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);

        WritePdfAscii(
            stream,
            "%PDF-1.4\n");

        stream.Write(
            new byte[]
            {
                0x25,
                0xE2,
                0xE3,
                0xCF,
                0xD3,
                0x0A
            });

        // 1: Catalog
        offsets[1] =
            stream.Position;

        WritePdfObject(
            stream,
            1,
            "<< /Type /Catalog /Pages 2 0 R >>");

        // 2: Pages
        offsets[2] =
            stream.Position;

        var kids =
            string.Join(
                " ",
                Enumerable.Range(
                        0,
                        pages.Count)
                    .Select(index =>
                        $"{3 + index * 3} 0 R"));

        WritePdfObject(
            stream,
            2,
            $"<< /Type /Pages /Count {pages.Count} /Kids [{kids}] >>");

        for (var index = 0;
             index < pages.Count;
             index++)
        {
            var page =
                pages[index];

            var pageObject =
                3
                + index * 3;

            var contentObject =
                pageObject
                + 1;

            var imageObject =
                pageObject
                + 2;

            var widthPoints =
                page.WidthPoints.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture);

            var heightPoints =
                page.HeightPoints.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture);

            // Page object.
            offsets[pageObject] =
                stream.Position;

            WritePdfObject(
                stream,
                pageObject,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {widthPoints} {heightPoints}] /Resources << /XObject << /Im0 {imageObject} 0 R >> >> /Contents {contentObject} 0 R >>");

            // Content stream draws the JPEG to full page.
            var content =
                $"q\n{widthPoints} 0 0 {heightPoints} 0 0 cm\n/Im0 Do\nQ\n";

            var contentBytes =
                Encoding.ASCII.GetBytes(
                    content);

            offsets[contentObject] =
                stream.Position;

            WritePdfAscii(
                stream,
                $"{contentObject} 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");

            stream.Write(
                contentBytes,
                0,
                contentBytes.Length);

            WritePdfAscii(
                stream,
                "endstream\nendobj\n");

            // JPEG XObject.
            offsets[imageObject] =
                stream.Position;

            WritePdfAscii(
                stream,
                $"{imageObject} 0 obj\n<< /Type /XObject /Subtype /Image /Width {page.PixelWidth} /Height {page.PixelHeight} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {page.JpegBytes.Length} >>\nstream\n");

            stream.Write(
                page.JpegBytes,
                0,
                page.JpegBytes.Length);

            WritePdfAscii(
                stream,
                "\nendstream\nendobj\n");
        }

        var xrefOffset =
            stream.Position;

        WritePdfAscii(
            stream,
            $"xref\n0 {objectCount + 1}\n");

        WritePdfAscii(
            stream,
            "0000000000 65535 f \n");

        for (var objectNumber = 1;
             objectNumber <= objectCount;
             objectNumber++)
        {
            WritePdfAscii(
                stream,
                offsets[objectNumber]
                    .ToString(
                        "D10",
                        CultureInfo.InvariantCulture)
                + " 00000 n \n");
        }

        WritePdfAscii(
            stream,
            $"trailer\n<< /Size {objectCount + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
    }

    private static void WritePdfObject(
        Stream stream,
        int objectNumber,
        string body)
    {
        WritePdfAscii(
            stream,
            $"{objectNumber} 0 obj\n{body}\nendobj\n");
    }

    private static void WritePdfAscii(
        Stream stream,
        string text)
    {
        var bytes =
            Encoding.ASCII.GetBytes(
                text);

        stream.Write(
            bytes,
            0,
            bytes.Length);
    }

    private string BuildPrintFilterSummary()
    {
        var parts =
            new List<string>();

        parts.Add(
            $"{LblFrom.Text}: {DateFrom.SelectedDate:dd.MM.yyyy}");

        parts.Add(
            $"{LblTo.Text}: {DateTo.SelectedDate:dd.MM.yyyy}");

        var selectedWorkcenters =
            GetSelectedWorkcenterCodes()
                .Where(code =>
                    !string.IsNullOrWhiteSpace(
                        code))
                .Select(code =>
                    code.Trim())
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    code =>
                        code,
                    Comparer<string>.Create(
                        CompareNaturalWorkcenterCodes))
                .ToList();

        var workcenterText =
            selectedWorkcenters.Count > 0
                ? $"[{string.Join("; ", selectedWorkcenters)}]"
                : "[]";

        parts.Add(
            $"{LblWorkcenter.Text}: {workcenterText}");

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
                $"{LblProduct.Text}: [{TxtProduct.Text.Trim()}]");
        }

        if (!string.IsNullOrWhiteSpace(
                TxtOrder.Text))
        {
            parts.Add(
                $"{LblOrder.Text}: [{TxtOrder.Text.Trim()}]");
        }

        if (!string.IsNullOrWhiteSpace(
                TxtOperation.Text))
        {
            parts.Add(
                $"{LblOperation.Text}: [{TxtOperation.Text.Trim()}]");
        }

        var definition =
            CmbReport.SelectedItem
                as DMS.Integration.Mes.Reporting.Definitions.MesReportDefinition;

        if (definition is not null
            && IsProcessValuesReport(
                definition))
        {
            var states =
                GetSelectedProcessValueStates();

            if (states.Count > 0)
            {
                parts.Add(
                    $"{T("MES06.ProcessValues.Filter.States", "States")}: [{string.Join("; ", states)}]");
            }
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
        var definition =
            CmbReport.SelectedItem
                as DMS.Integration.Mes.Reporting.Definitions.MesReportDefinition;

        if (definition is not null
            && IsOeeReport(
                definition))
        {
            AppendOeeChartToDocument(
                document);

            return;
        }

        if (definition is not null
            && IsProcessValuesReport(
                definition))
        {
            AppendProcessValuesTimelineToDocument(
                document);

            return;
        }

        if (definition is not null
            && IsProductionGraphReport(
                definition))
        {
            AppendProductionGraphToDocument(
                document);

            return;
        }

        if (definition is not null
            && IsMachineTimelineReport(
                definition))
        {
            AppendMachineTimelineToDocument(
                document);

            return;
        }

        if (definition?.Chart is null
            || _currentRows.Count == 0)
        {
            return;
        }

        try
        {
            var chartDefinition =
                definition.Chart;

            var groups =
                AggregateChart(
                        _currentRows,
                        chartDefinition)
                    .Take(
                        chartDefinition.Top)
                    .ToList();

            if (groups.Count == 0)
            {
                return;
            }

            var availableWidth =
                Math.Max(
                    650d,
                    document.PageWidth
                    - document.PagePadding.Left
                    - document.PagePadding.Right);

            // Intentionally compact. The previous screenshot-based chart
            // consumed too much vertical space in printed reports.
            const double logicalHeight =
                245d;

            const double renderDpi =
                144d;

            var bitmap =
                RenderCompactPrintableChart(
                    groups,
                    chartDefinition,
                    availableWidth,
                    logicalHeight,
                    renderDpi);

            var image =
                new Image
                {
                    Source =
                        bitmap,
                    Stretch =
                        Stretch.Uniform,
                    Width =
                        availableWidth,
                    Height =
                        logicalHeight,
                    HorizontalAlignment =
                        HorizontalAlignment.Left
                };

            document.Blocks.Add(
                new BlockUIContainer(
                    image)
                {
                    Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            10)
                });
        }
        catch (Exception ex)
        {
            _logger.Error(
                "MES06 print chart rendering failed.",
                ex);
        }
    }

    private BitmapSource RenderCompactPrintableChart(
        IReadOnlyList<(string Label, double Value)> groups,
        DMS.Integration.Mes.Reporting.Definitions.MesChartDefinition chartDefinition,
        double logicalWidth,
        double logicalHeight,
        double renderDpi)
    {
        var scale =
            renderDpi
            / 96d;

        var pixelWidth =
            Math.Max(
                1,
                (int)Math.Ceiling(
                    logicalWidth
                    * scale));

        var pixelHeight =
            Math.Max(
                1,
                (int)Math.Ceiling(
                    logicalHeight
                    * scale));

        var visual =
            new DrawingVisual();

        var pixelsPerDip =
            VisualTreeHelper.GetDpi(
                    this)
                .PixelsPerDip;

        using (var dc =
               visual.RenderOpen())
        {
            dc.DrawRectangle(
                Brushes.White,
                null,
                new Rect(
                    0,
                    0,
                    logicalWidth,
                    logicalHeight));

            var title =
                new FormattedText(
                    chartDefinition.Title
                    ?? string.Empty,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(
                        "Segoe UI"),
                    11d,
                    Brushes.Black,
                    pixelsPerDip)
                {
                    MaxTextWidth =
                        logicalWidth - 20d,
                    Trimming =
                        TextTrimming.CharacterEllipsis
                };

            dc.DrawText(
                title,
                new Point(
                    8d,
                    5d));

            const double leftMargin =
                52d;

            const double rightMargin =
                16d;

            const double topMargin =
                34d;

            const double bottomMargin =
                58d;

            var plotWidth =
                Math.Max(
                    100d,
                    logicalWidth
                    - leftMargin
                    - rightMargin);

            var plotHeight =
                Math.Max(
                    70d,
                    logicalHeight
                    - topMargin
                    - bottomMargin);

            var maximum =
                groups.Max(item =>
                    Math.Max(
                        0d,
                        item.Value));

            var axisMaximum =
                CalculatePrintableChartAxisMaximum(
                    maximum);

            const int gridSteps =
                4;

            var gridPen =
                new Pen(
                    new SolidColorBrush(
                        Color.FromRgb(
                            222,
                            226,
                            230)),
                    0.7d);

            for (var step = 0;
                 step <= gridSteps;
                 step++)
            {
                var ratio =
                    step
                    / (double)gridSteps;

                var y =
                    topMargin
                    + plotHeight
                    - ratio
                    * plotHeight;

                dc.DrawLine(
                    gridPen,
                    new Point(
                        leftMargin,
                        y),
                    new Point(
                        leftMargin
                        + plotWidth,
                        y));

                var axisValue =
                    axisMaximum
                    * ratio;

                var axisText =
                    new FormattedText(
                        FormatCompactChartNumber(
                            axisValue),
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface(
                            "Segoe UI"),
                        8d,
                        Brushes.DimGray,
                        pixelsPerDip);

                dc.DrawText(
                    axisText,
                    new Point(
                        Math.Max(
                            2d,
                            leftMargin
                            - axisText.Width
                            - 7d),
                        y
                        - axisText.Height
                        / 2d));
            }

            var slotWidth =
                plotWidth
                / groups.Count;

            var barWidth =
                Math.Min(
                    58d,
                    Math.Max(
                        18d,
                        slotWidth
                        * 0.46d));

            var barBrush =
                new SolidColorBrush(
                    Color.FromRgb(
                        33,
                        150,
                        243));

            for (var index = 0;
                 index < groups.Count;
                 index++)
            {
                var item =
                    groups[index];

                var centerX =
                    leftMargin
                    + slotWidth
                    * index
                    + slotWidth
                    / 2d;

                var value =
                    Math.Max(
                        0d,
                        item.Value);

                var barHeight =
                    axisMaximum <= 0d
                        ? 0d
                        : value
                          / axisMaximum
                          * plotHeight;

                // Keep tiny positive values visible.
                if (value > 0d)
                {
                    barHeight =
                        Math.Max(
                            1.5d,
                            barHeight);
                }

                var barTop =
                    topMargin
                    + plotHeight
                    - barHeight;

                dc.DrawRoundedRectangle(
                    barBrush,
                    null,
                    new Rect(
                        centerX
                        - barWidth / 2d,
                        barTop,
                        barWidth,
                        barHeight),
                    2d,
                    2d);

                var valueText =
                    new FormattedText(
                        FormatPrintableChartValue(
                            value,
                            chartDefinition),
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface(
                            new FontFamily(
                                "Segoe UI"),
                            FontStyles.Normal,
                            FontWeights.SemiBold,
                            FontStretches.Normal),
                        8.5d,
                        Brushes.Black,
                        pixelsPerDip);

                dc.DrawText(
                    valueText,
                    new Point(
                        centerX
                        - valueText.Width
                        / 2d,
                        Math.Max(
                            topMargin - 1d,
                            barTop
                            - valueText.Height
                            - 3d)));

                var label =
                    new FormattedText(
                        item.Label
                        ?? string.Empty,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface(
                            "Segoe UI"),
                        8.2d,
                        Brushes.DimGray,
                        pixelsPerDip)
                    {
                        TextAlignment =
                            TextAlignment.Center,
                        MaxTextWidth =
                            Math.Max(
                                48d,
                                slotWidth - 6d),
                        MaxTextHeight =
                            bottomMargin - 8d,
                        Trimming =
                            TextTrimming.CharacterEllipsis
                    };

                dc.DrawText(
                    label,
                    new Point(
                        centerX
                        - label.MaxTextWidth
                        / 2d,
                        topMargin
                        + plotHeight
                        + 7d));
            }
        }

        var bitmap =
            new RenderTargetBitmap(
                pixelWidth,
                pixelHeight,
                renderDpi,
                renderDpi,
                PixelFormats.Pbgra32);

        bitmap.Render(
            visual);

        if (bitmap.CanFreeze)
        {
            bitmap.Freeze();
        }

        return bitmap;
    }

    private static double CalculatePrintableChartAxisMaximum(
        double maximum)
    {
        if (maximum <= 0d)
        {
            return 1d;
        }

        var rawStep =
            maximum
            / 4d;

        var magnitude =
            Math.Pow(
                10d,
                Math.Floor(
                    Math.Log10(
                        rawStep)));

        var normalized =
            rawStep
            / magnitude;

        var niceNormalized =
            normalized <= 1d
                ? 1d
                : normalized <= 2d
                    ? 2d
                    : normalized <= 5d
                        ? 5d
                        : 10d;

        var step =
            niceNormalized
            * magnitude;

        return Math.Ceiling(
                   maximum
                   / step)
               * step;
    }

    private static string FormatCompactChartNumber(
        double value)
    {
        if (Math.Abs(
                value
                - Math.Round(
                    value))
            < 0.001d)
        {
            return Math.Round(
                    value)
                .ToString(
                    "N0",
                    CultureInfo.CurrentCulture);
        }

        return value.ToString(
            "N1",
            CultureInfo.CurrentCulture);
    }

    private static string FormatPrintableChartValue(
        double value,
        DMS.Integration.Mes.Reporting.Definitions.MesChartDefinition chartDefinition)
    {
        var number =
            FormatCompactChartNumber(
                value);

        var title =
            chartDefinition.Title
            ?? string.Empty;

        if (title.Contains(
                "[min]",
                StringComparison.OrdinalIgnoreCase)
            || title.Contains(
                "(min)",
                StringComparison.OrdinalIgnoreCase))
        {
            return $"{number} min";
        }

        return number;
    }

    private static IReadOnlyList<object> GetPrintableGridRows(
        DataGrid grid)
    {
        return grid.Items
            .Cast<object>()
            .Where(item =>
                item != CollectionView.NewItemPlaceholder)
            .ToList();
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

            ApplyPrintableStateRowColor(
                grid,
                row,
                tableRow);

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

    private void ApplyPrintableStateRowColor(
        DataGrid grid,
        object row,
        TableRow tableRow)
    {
        var definition =
            CmbReport.SelectedItem
                as DMS.Integration.Mes.Reporting.Definitions.MesReportDefinition;

        if (definition is null
            || !IsStatesReport(
                definition))
        {
            return;
        }

        // Both the detail grid and the lower state summary can be colored.
        // ResolveStateColorDefinition understands Mes06GridRow as well as
        // the summary-row CLR properties.
        var colorDefinition =
            ResolveStateColorDefinition(
                row);

        var color =
            ParseFastecColor(
                colorDefinition?.StateColor)
            ?? ParseFastecColor(
                colorDefinition?.CategoryColor);

        if (!color.HasValue)
        {
            return;
        }

        var background =
            new SolidColorBrush(
                color.Value);

        if (background.CanFreeze)
        {
            background.Freeze();
        }

        tableRow.Background =
            background;

        // Keep print, preview and PDF aligned with the requested
        // on-screen presentation.
        tableRow.Foreground =
            Brushes.Black;
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
                ReadPrintableValue(
                    row,
                    property);

            return FormatPrintValue(
                value,
                binding.StringFormat);
        }

        var workers =
            ReadPrintableValue(
                row,
                "WorkersDisplay");

        return Convert.ToString(
                   workers,
                   CultureInfo.CurrentCulture)
               ?? string.Empty;
    }

    private static object? ReadPrintableValue(
        object row,
        string propertyName)
    {
        if (row is Mes06GridRow gridRow)
        {
            return gridRow[
                propertyName];
        }

        if (row is IDictionary dictionary
            && dictionary.Contains(
                propertyName))
        {
            return dictionary[
                propertyName];
        }

        var stringIndexer =
            row.GetType()
                .GetProperty(
                    "Item",
                    new[]
                    {
                        typeof(string)
                    });

        if (stringIndexer is not null)
        {
            try
            {
                return stringIndexer.GetValue(
                    row,
                    new object[]
                    {
                        propertyName
                    });
            }
            catch
            {
                // Fall back to a normal CLR property below.
            }
        }

        return ReadProperty(
            row,
            propertyName);
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
