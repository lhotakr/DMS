using DMS.Desktop.Settings;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DMS.Desktop.Views.Settings;

public partial class ClientSettingsView : UserControl
{
    private readonly DmsUserSettings _settings;
    private readonly Action _applyTheme;
    private readonly Action _saveSettings;

    public ClientSettingsView(
        DmsUserSettings settings,
        Action applyTheme,
        Action saveSettings)
    {
        InitializeComponent();

        _settings = settings;
        _applyTheme = applyTheme;
        _saveSettings = saveSettings;

        LoadValues();
        UpdateCustomColorsEnabled();
    }

    private void LoadValues()
    {
        TxtThemeMode.Text = string.IsNullOrWhiteSpace(_settings.ThemeMode) ? "Light" : _settings.ThemeMode;

        TxtBackgroundColor.Text = _settings.BackgroundColor;
        TxtPanelColor.Text = _settings.PanelColor;
        TxtForegroundColor.Text = _settings.ForegroundColor;
        TxtMutedForegroundColor.Text = _settings.MutedForegroundColor;
        TxtBorderColor.Text = _settings.BorderColor;
        TxtAccentColor.Text = _settings.AccentColor;
        TxtOnAccentColor.Text = _settings.OnAccentColor;

        TxtMaxHistory.Text = _settings.MaxTransactionHistoryItems.ToString();
    }

    private void CmbThemeMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateCustomColorsEnabled();
    }

    private void UpdateCustomColorsEnabled()
    {
        var themeMode = GetSelectedThemeMode();

        var isCustom = string.Equals(
            themeMode,
            "Custom",
            StringComparison.OrdinalIgnoreCase);

        CustomColorsPanel.IsEnabled = isCustom;
        CustomColorsPanel.Opacity = isCustom ? 1.0 : 0.45;
    }

    private bool SaveValuesToSettings()
    {
        var themeMode = GetSelectedThemeMode();

        if (!string.Equals(themeMode, "Light", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(themeMode, "Dark", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(themeMode, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "Režim vzhledu musí být Light, Dark nebo Custom.",
                "DMS - nastavení klienta",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;
        }

        if (!int.TryParse(TxtMaxHistory.Text.Trim(), out var maxHistory) ||
            maxHistory <= 0 ||
            maxHistory > 100)
        {
            MessageBox.Show(
                "Počet položek historie musí být číslo 1 až 100.",
                "DMS - nastavení klienta",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;
        }

        if (themeMode == "Custom")
        {
            var colors = new[]
            {
                TxtBackgroundColor.Text,
                TxtPanelColor.Text,
                TxtForegroundColor.Text,
                TxtMutedForegroundColor.Text,
                TxtBorderColor.Text,
                TxtAccentColor.Text,
                TxtOnAccentColor.Text
            };

            if (colors.Any(color => !IsValidHexColor(color)))
            {
                MessageBox.Show(
                    "Všechny vlastní barvy musí být ve formátu #RRGGBB.",
                    "DMS - nastavení klienta",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return false;
            }

            _settings.BackgroundColor = TxtBackgroundColor.Text.Trim();
            _settings.PanelColor = TxtPanelColor.Text.Trim();
            _settings.ForegroundColor = TxtForegroundColor.Text.Trim();
            _settings.MutedForegroundColor = TxtMutedForegroundColor.Text.Trim();
            _settings.BorderColor = TxtBorderColor.Text.Trim();
            _settings.AccentColor = TxtAccentColor.Text.Trim();
            _settings.OnAccentColor = TxtOnAccentColor.Text.Trim();
        }
        else
        {
            // I v Light/Dark dovolíme nastavovat akcentní barvu,
            // pokud je pole vyplněné a platné.
            if (IsValidHexColor(TxtAccentColor.Text))
            {
                _settings.AccentColor = TxtAccentColor.Text.Trim();
            }
        }

        _settings.ThemeMode = themeMode;
        _settings.MaxTransactionHistoryItems = maxHistory;

        return true;
    }

    private void BtnThemeModeDropDown_Click(object sender, RoutedEventArgs e)
    {
        PopupThemeMode.IsOpen = !PopupThemeMode.IsOpen;
    }

    private void LstThemeModes_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (LstThemeModes.SelectedItem is not ListBoxItem item)
        {
            return;
        }

        var value = item.Content?.ToString();

        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        TxtThemeMode.Text = value;
        PopupThemeMode.IsOpen = false;

        UpdateCustomColorsEnabled();
    }

    private string GetSelectedThemeMode()
    {
        var value = TxtThemeMode.Text?.Trim();

        return string.IsNullOrWhiteSpace(value)
            ? "Light"
            : value;
    }

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        if (!SaveValuesToSettings())
        {
            return;
        }

        _applyTheme();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (!SaveValuesToSettings())
        {
            return;
        }

        _applyTheme();
        _saveSettings();

        MessageBox.Show(
            "Nastavení klienta bylo uloženo.",
            "DMS - nastavení klienta",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static bool IsValidHexColor(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        value = value.Trim();

        if (!value.StartsWith("#") || value.Length != 7)
        {
            return false;
        }

        return value
            .Skip(1)
            .All(Uri.IsHexDigit);
    }
}