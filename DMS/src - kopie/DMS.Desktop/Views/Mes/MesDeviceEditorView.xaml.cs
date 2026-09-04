using DMS.Desktop.Logging;
using DMS.Desktop.Models;
using DMS.Integration.Mes.Models;
using DMS.Integration.Mes.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Mes;

public partial class MesDeviceEditorView : UserControl
{
    private readonly string _devicesFilePath;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserDisplayName;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;
    private readonly MesDeviceFileService _deviceFileService = new();
    private readonly ObservableCollection<MesDeviceEditRow> _rows = new();

    public MesDeviceEditorView()
        : this(Path.Combine(AppContext.BaseDirectory, "Config", "devices.txt"), null, Environment.UserName)
    {
    }

    public MesDeviceEditorView(
        string devicesFilePath,
        DmsLogger? logger,
        string currentUserDisplayName,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null)
    {
        InitializeComponent();
        _devicesFilePath = devicesFilePath;
        _logger = logger;
        _currentUserDisplayName = currentUserDisplayName;
        _translate = translate;
        _translateFormat = translateFormat;

        ApplyLocalization();
        GridDevices.ItemsSource = _rows;
        LoadRows();
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("MES02.Title", "MES02 - Editace zařízení");
        TxtSubtitle.Text = T("MES02.Subtitle", "Editace seznamu MES zařízení. Zápis se ukládá zpět do prostého textového souboru devices.txt.");
        BtnReload.Content = T("MES02.Button.Reload", "Načíst");
        BtnAdd.Content = T("MES02.Button.Add", "Přidat");
        BtnDelete.Content = T("MES02.Button.Delete", "Smazat vybrané");
        BtnSave.Content = T("MES02.Button.Save", "Uložit TXT");
        TxtHint.Text = T("MES02.Hint", "Formát: adresa-nebo-hostname;typ;název;poznámka. Typy doporučeně: SERVER, MONITOR, STROJ, TERMINAL.");
        ColAddress.Header = T("MES02.Column.Address", "Adresa / hostname");
        ColCategory.Header = T("MES02.Column.Category", "Typ");
        ColName.Header = T("MES02.Column.Name", "Název");
        ColNote.Header = T("MES02.Column.Note", "Poznámka");
        ColSourceLine.Header = T("MES02.Column.SourceLine", "Řádek");
    }

    private void LoadRows()
    {
        try
        {
            _deviceFileService.EnsureTemplateFile(_devicesFilePath);
            var devices = _deviceFileService.Load(_devicesFilePath);
            _rows.Clear();
            foreach (var device in devices)
            {
                _rows.Add(MesDeviceEditRow.FromDevice(device));
            }

            RefreshSummary();
            TxtStatusLine.Text = TF("MES02.Status.Loaded", "Načteno {0} zařízení.", _rows.Count);
            _logger?.AdminAction("MES02", "LoadMesDeviceEditor", _currentUserDisplayName, $"File={_devicesFilePath}; Devices={_rows.Count}");
        }
        catch (Exception ex)
        {
            TxtStatusLine.Text = TF("MES02.Status.LoadFailed", "Načtení selhalo: {0}", ex.Message);
            _logger?.AdminAction("MES02", "LoadMesDeviceEditorFailed", _currentUserDisplayName, $"File={_devicesFilePath}; Error={ex.Message}");
        }
    }

    private void SaveRows()
    {
        try
        {
            GridDevices.CommitEdit(DataGridEditingUnit.Cell, true);
            GridDevices.CommitEdit(DataGridEditingUnit.Row, true);

            var invalid = _rows.Where(row => !row.IsValid).ToList();
            if (invalid.Count > 0)
            {
                TxtStatusLine.Text = TF("MES02.Status.Invalid", "U {0} řádků chybí adresa / hostname. Uložení nebylo provedeno.", invalid.Count);
                return;
            }

            var devices = _rows.Select(row => row.ToDevice()).ToList();
            _deviceFileService.Save(_devicesFilePath, devices);

            TxtStatusLine.Text = TF("MES02.Status.Saved", "Uloženo {0} zařízení do TXT.", devices.Count);
            _logger?.AdminAction("MES02", "SaveMesDevices", _currentUserDisplayName, $"File={_devicesFilePath}; Devices={devices.Count}");
            LoadRows();
        }
        catch (Exception ex)
        {
            TxtStatusLine.Text = TF("MES02.Status.SaveFailed", "Uložení selhalo: {0}", ex.Message);
            _logger?.AdminAction("MES02", "SaveMesDevicesFailed", _currentUserDisplayName, $"File={_devicesFilePath}; Error={ex.Message}");
        }
    }

    private void RefreshSummary()
    {
        TxtSummary.Text = TF("MES02.Summary", "Zařízení v seznamu: {0}", _rows.Count);
        TxtFilePath.Text = TF("MES02.FilePath", "Soubor: {0}", _devicesFilePath);
    }

    private void BtnReload_Click(object sender, RoutedEventArgs e) => LoadRows();

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        var row = new MesDeviceEditRow
        {
            Category = "TERMINAL",
            Name = T("MES02.NewDeviceName", "Nové zařízení")
        };
        _rows.Add(row);
        GridDevices.SelectedItem = row;
        GridDevices.ScrollIntoView(row);
        RefreshSummary();
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        var selected = GridDevices.SelectedItems.Cast<MesDeviceEditRow>().ToList();
        if (selected.Count == 0)
        {
            TxtStatusLine.Text = T("MES02.Status.NoSelection", "Není vybrán žádný řádek.");
            return;
        }

        foreach (var row in selected)
        {
            _rows.Remove(row);
        }

        RefreshSummary();
        TxtStatusLine.Text = TF("MES02.Status.Deleted", "Odebráno {0} řádků. Změny se zapíšou až tlačítkem Uložit TXT.", selected.Count);
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e) => SaveRows();

    private string T(string key, string fallback)
    {
        var value = _translate?.Invoke(key);
        return IsMissing(value, key) ? fallback : value!;
    }

    private string TF(string key, string fallback, params object[] args)
    {
        var value = _translateFormat?.Invoke(key, args);
        if (!string.IsNullOrWhiteSpace(value) && !IsMissing(value, key))
        {
            return value;
        }

        try
        {
            return string.Format(fallback, args);
        }
        catch
        {
            return fallback;
        }
    }

    private static bool IsMissing(string? value, string key)
    {
        return string.IsNullOrWhiteSpace(value)
               || string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);
    }
}
