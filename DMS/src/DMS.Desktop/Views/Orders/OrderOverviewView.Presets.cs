using DMS.Desktop.UI;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DMS.Desktop.Views.Orders;

public partial class OrderOverviewView
{
    private sealed class ArchiveOption
    {
        public ArchiveOption(int? value, string label) { Value = value; Label = label; }
        public int? Value { get; }
        public string Label { get; }
        public override string ToString() => Label;
    }

    private sealed class OrdFilterPreset
    {
        public string Name { get; set; } = string.Empty;
        public int? ArchiveStatus { get; set; } = 0;
        public int MaxRows { get; set; } = 500;
        public string Search { get; set; } = string.Empty;
        public List<string> Statuses { get; set; } = new();
        public int? ErrorStatus { get; set; }
        public int? PlanningStatus { get; set; }
        public int? FixationStatus { get; set; }
        public int? ProductionStatus { get; set; }
    }

    private readonly ComboBox _ordArchive = new() { Width = 155, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBox _ordLimit = new() { Width = 70, Height = 30, Text = "500", Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBox _ordPresetName = new() { Width = 145, Height = 30, Margin = new Thickness(0, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ComboBox _ordPresets = new() { Width = 145, Height = 30, Margin = new Thickness(0, 0, 6, 0) };
    private bool _applyingOrdPreset;
    private string OrdPresetPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DMS", "ORD10", "filter-presets.json");

    private void InitializeOrd10Presets()
    {
        if (BtnRefresh.Parent is not WrapPanel panel) throw new InvalidOperationException("ORD10 toolbar was changed.");
        _ordArchive.ItemsSource = new[]
        {
            new ArchiveOption(0, T("ORD10.Archive.Active", "Aktivní")),
            new ArchiveOption(1, T("ORD10.Archive.Archived", "Archivované")),
            new ArchiveOption(null, T("ORD10.Archive.All", "Všechny"))
        };
        _ordArchive.SelectedIndex = 0;
        _ordArchive.SelectionChanged += async (_, _) =>
        {
            if (_loaded && !_applyingOrdPreset) await LoadOrdersAsync(true);
        };
        _ordLimit.ToolTip = T("ORD10.Limit.Help", "Počet zakázek (500–10000); potvrď Enter nebo Obnovit");
        _ordLimit.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; await LoadOrdersAsync(true); }
        };
        var reload = new Button { Content = T("ORD10.Limit.Label", "Limit"), IsEnabled = false, Margin = new Thickness(3, 0, 5, 0) };
        panel.Children.Add(reload);
        panel.Children.Add(_ordLimit);
        panel.Children.Add(_ordArchive);
        _ordPresetName.ToolTip = T("ORD10.Preset.Name", "Název předvolby");
        panel.Children.Add(_ordPresetName);
        var save = new Button { Content = T("ORD10.Preset.Save", "Uložit filtr"), Height = 30, Margin = new Thickness(0, 0, 6, 0) };
        save.Click += (_, _) => SaveOrdPreset();
        panel.Children.Add(save);
        panel.Children.Add(_ordPresets);
        var restore = new Button { Content = T("ORD10.Preset.Load", "Načíst filtr"), Height = 30 };
        restore.Click += async (_, _) => await RestoreOrdPresetAsync();
        panel.Children.Add(restore);
        ReloadOrdPresets();
    }

    private int OrdMaxRows => int.TryParse(_ordLimit.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value)
        ? Math.Clamp(value, 1, 10000) : 500;
    private int? OrdArchiveStatus => (_ordArchive.SelectedItem as ArchiveOption)?.Value;

    private List<OrdFilterPreset> ReadOrdPresets()
    {
        if (!File.Exists(OrdPresetPath)) return new();
        return JsonSerializer.Deserialize<List<OrdFilterPreset>>(File.ReadAllText(OrdPresetPath)) ?? new();
    }
    private void ReloadOrdPresets()
    {
        var selected = _ordPresets.SelectedItem as string;
        var names = ReadOrdPresets().Select(x => x.Name).OrderBy(x => x).ToList();
        _ordPresets.ItemsSource = names;
        _ordPresets.SelectedItem = names.FirstOrDefault(x => x == selected) ?? names.FirstOrDefault();
    }
    private static int? FilterChoice(ComboBox box) => (box.SelectedItem as SimpleFilterOption)?.Value;
    private static void SelectChoice(ComboBox box, int? value)
    {
        var match = box.Items.Cast<object>().OfType<SimpleFilterOption>().FirstOrDefault(x => x.Value == value);
        if (match is not null) box.SelectedItem = match;
    }
    private void SaveOrdPreset()
    {
        try
        {
            var name = _ordPresetName.Text.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 80) throw new InvalidOperationException("Preset name must be 1–80 characters.");
            var all = ReadOrdPresets();
            var p = new OrdFilterPreset
            {
                Name = name, ArchiveStatus = OrdArchiveStatus, MaxRows = OrdMaxRows,
                Search = TxtSearch.Text?.Trim() ?? "",
                Statuses = LstStatuses.SelectedItems.Cast<StatusFilterOption>().Select(x => x.Code).ToList(),
                ErrorStatus = FilterChoice(CmbErrorStatus), PlanningStatus = FilterChoice(CmbPlanningStatus),
                FixationStatus = FilterChoice(CmbFixationStatus), ProductionStatus = FilterChoice(CmbProductionStatus)
            };
            all.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            all.Add(p);
            Directory.CreateDirectory(Path.GetDirectoryName(OrdPresetPath)!);
            var temp = OrdPresetPath + ".tmp." + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temp, OrdPresetPath, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            ReloadOrdPresets();
            _ordPresets.SelectedItem = name;
            _logger.Info($"ORD10 preset saved. User={_user}; Preset={name}; Archive={OrdArchiveStatus}; MaxRows={OrdMaxRows}");
        }
        catch (Exception ex)
        {
            _logger.Error("ORD10 save preset failed.", ex);
            DmsMessage.Error("ORD10", ex.Message, Window.GetWindow(this));
        }
    }

    private async Task RestoreOrdPresetAsync()
    {
        if (_ordPresets.SelectedItem is not string name) return;
        try
        {
            var preset = ReadOrdPresets().FirstOrDefault(x => x.Name == name);
            if (preset is null) return;
            _applyingOrdPreset = true;
            _suppressStatusRefresh = true;
            try
            {
                _ordArchive.SelectedItem = _ordArchive.Items.Cast<ArchiveOption>()
                    .FirstOrDefault(x => x.Value == preset.ArchiveStatus);
                _ordLimit.Text = preset.MaxRows.ToString(CultureInfo.CurrentCulture);
                TxtSearch.Text = preset.Search;
                SelectChoice(CmbErrorStatus, preset.ErrorStatus);
                SelectChoice(CmbPlanningStatus, preset.PlanningStatus);
                SelectChoice(CmbFixationStatus, preset.FixationStatus);
                SelectChoice(CmbProductionStatus, preset.ProductionStatus);
                LstStatuses.UnselectAll();
                foreach (var option in LstStatuses.Items.Cast<StatusFilterOption>())
                    if (preset.Statuses.Contains(option.Code, StringComparer.OrdinalIgnoreCase))
                        LstStatuses.SelectedItems.Add(option);
            }
            finally { _suppressStatusRefresh = false; _applyingOrdPreset = false; }
            await LoadOrdersAsync(true);
            _logger.Info($"ORD10 preset loaded. User={_user}; Preset={name}");
        }
        catch (Exception ex) { _logger.Error("ORD10 load preset failed.", ex); DmsMessage.Error("ORD10", ex.Message, Window.GetWindow(this)); }
    }
}
