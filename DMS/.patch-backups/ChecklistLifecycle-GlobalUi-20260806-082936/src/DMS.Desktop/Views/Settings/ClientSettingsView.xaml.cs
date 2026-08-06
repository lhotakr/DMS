using DMS.Desktop.Settings;
using DMS.Desktop.UI;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DMS.Desktop.Views.Settings;

public partial class ClientSettingsView : UserControl
{
    private readonly DmsUserSettings _settings;
    private readonly Action _applyTheme;
    private readonly Action _applyLocalization;
    private readonly Action _saveSettings;
    private readonly Func<string, string> _translate;
    private readonly Func<string, object[], string> _translateFormat;
    private readonly Action<string, string> _logClientSettingsAction;

    public ClientSettingsView(
    DmsUserSettings settings,
    Action applyTheme,
    Action applyLocalization,
    Action saveSettings,
    Func<string, string> translate,
    Func<string, object[], string> translateFormat,
    Action<string, string> logClientSettingsAction)
    {
        InitializeComponent();

        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _applyTheme = applyTheme ?? throw new ArgumentNullException(nameof(applyTheme));
        _applyLocalization = applyLocalization ?? throw new ArgumentNullException(nameof(applyLocalization));
        _saveSettings = saveSettings ?? throw new ArgumentNullException(nameof(saveSettings));
        _translate = translate ?? throw new ArgumentNullException(nameof(translate));
        _translateFormat = translateFormat ?? throw new ArgumentNullException(nameof(translateFormat));
        _logClientSettingsAction = logClientSettingsAction ?? throw new ArgumentNullException(nameof(logClientSettingsAction));

        LoadValues();
        ApplyLocalization();
        UpdateCustomColorsEnabled();
    }

    private void LogClientSettingsAction(string action)
    {
        var details =
            $"ThemeMode={_settings.ThemeMode}; " +
            $"LanguageMode={_settings.LanguageMode}; " +
            $"CultureName={_settings.CultureName}; " +
            $"MaxTransactionHistoryItems={_settings.MaxTransactionHistoryItems}; " +
            $"BackgroundColor={_settings.BackgroundColor}; " +
            $"PanelColor={_settings.PanelColor}; " +
            $"ForegroundColor={_settings.ForegroundColor}; " +
            $"MutedForegroundColor={_settings.MutedForegroundColor}; " +
            $"BorderColor={_settings.BorderColor}; " +
            $"AccentColor={_settings.AccentColor}; " +
            $"OnAccentColor={_settings.OnAccentColor}";

        _logClientSettingsAction(action, details);
    }

    private string T(string key)
    {
        return _translate(key);
    }

    private string T(string key, params object[] args)
    {
        return _translateFormat(key, args);
    }

    private void LoadValues()
    {
        LoadThemeValue();

        TxtBackgroundColor.Text = _settings.BackgroundColor;
        TxtPanelColor.Text = _settings.PanelColor;
        TxtForegroundColor.Text = _settings.ForegroundColor;
        TxtMutedForegroundColor.Text = _settings.MutedForegroundColor;
        TxtBorderColor.Text = _settings.BorderColor;
        TxtAccentColor.Text = _settings.AccentColor;
        TxtOnAccentColor.Text = _settings.OnAccentColor;

        TxtMaxHistory.Text = _settings.MaxTransactionHistoryItems.ToString();

        LoadLanguageValues();
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("CLSET.Title");
        TxtAppearanceTitle.Text = T("CLSET.Appearance");
        TxtThemeTitle.Text = T("CLSET.Theme");

        TxtPreviewTitleLabel.Text = T("CLSET.Preview");
        PreviewTitle.Text = T("CLSET.PreviewCardTitle");
        PreviewText.Text = T("CLSET.PreviewCardText");
        PreviewMutedText.Text = T("CLSET.PreviewMutedText");
        PreviewButton.Content = T("CLSET.PreviewButton");

        TxtLanguageTitle.Text = T("CLSET.Language");
        TxtLanguageHelp.Text = T("CLSET.LanguageHelp");

        TxtCustomColorsTitle.Text = T("CLSET.CustomColors");
        TxtBackgroundColorLabel.Text = T("CLSET.Color.Background");
        TxtPanelColorLabel.Text = T("CLSET.Color.Panel");
        TxtForegroundColorLabel.Text = T("CLSET.Color.Foreground");
        TxtMutedForegroundColorLabel.Text = T("CLSET.Color.MutedForeground");
        TxtBorderColorLabel.Text = T("CLSET.Color.Border");
        TxtAccentColorLabel.Text = T("CLSET.Color.Accent");
        TxtOnAccentColorLabel.Text = T("CLSET.Color.OnAccent");

        TxtHistoryTitle.Text = T("CLSET.TransactionHistory");

        BtnApply.Content = T("Common.Apply");
        BtnSave.Content = T("Common.Save");

        ApplyThemeDisplayTexts();
        ApplyLanguageDisplayTexts();
    }

    private void LoadThemeValue()
    {
        var themeMode = string.IsNullOrWhiteSpace(_settings.ThemeMode)
            ? "Light"
            : _settings.ThemeMode;

        SetSelectedThemeMode(themeMode);
    }

    private void SetSelectedThemeMode(string themeMode)
    {
        BtnThemeSelector.Tag = themeMode;
        TxtThemeModeValue.Text = GetThemeDisplayText(themeMode);
    }

    private string GetSelectedThemeMode()
    {
        return BtnThemeSelector.Tag?.ToString() ?? "Light";
    }

    private string GetThemeDisplayText(string themeMode)
    {
        return themeMode switch
        {
            "Light" => T("CLSET.Theme.Light"),
            "Dark" => T("CLSET.Theme.Dark"),
            "HG" => T("CLSET.Theme.HG"),
            "Custom" => T("CLSET.Theme.Custom"),
            _ => themeMode
        };
    }

    private void ApplyThemeDisplayTexts()
    {
        foreach (var item in LstThemeModes.Items.OfType<ListBoxItem>())
        {
            var tag = item.Tag?.ToString();

            item.Content = tag switch
            {
                "Light" => T("CLSET.Theme.Light"),
                "Dark" => T("CLSET.Theme.Dark"),
                "HG" => T("CLSET.Theme.HG"),
                "Custom" => T("CLSET.Theme.Custom"),
                _ => item.Content
            };
        }

        SetSelectedThemeMode(GetSelectedThemeMode());
    }

    private void BtnThemeSelector_Click(object sender, RoutedEventArgs e)
    {
        PopupThemeMode.IsOpen = !PopupThemeMode.IsOpen;
    }

    private void LstThemeModes_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (LstThemeModes.SelectedItem is not ListBoxItem item)
        {
            return;
        }

        var code = item.Tag?.ToString();

        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        SetSelectedThemeMode(code);
        PopupThemeMode.IsOpen = false;
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
            !string.Equals(themeMode, "HG", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(themeMode, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            ShowWarning("CLSET.ValidationTitle", "CLSET.InvalidThemeMode");
            return false;
        }

        if (!int.TryParse(TxtMaxHistory.Text.Trim(), out var maxHistory) ||
            maxHistory <= 0 ||
            maxHistory > 100)
        {
            ShowWarning("CLSET.ValidationTitle", "CLSET.InvalidHistoryCount");
            return false;
        }

        if (string.Equals(themeMode, "Custom", StringComparison.OrdinalIgnoreCase))
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
                ShowWarning("CLSET.ValidationTitle", "CLSET.InvalidCustomColors");
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
        else if (string.Equals(themeMode, "HG", StringComparison.OrdinalIgnoreCase))
        {
            _settings.AccentColor = "#FFE500";
            _settings.OnAccentColor = "#111111";
        }

        _settings.ThemeMode = themeMode;
        _settings.MaxTransactionHistoryItems = maxHistory;

        SaveLanguageValuesToSettings();

        return true;
    }

    private void LoadLanguageValues()
    {
        var languageMode = string.IsNullOrWhiteSpace(_settings.LanguageMode)
            ? "Auto"
            : _settings.LanguageMode;

        var cultureName = _settings.CultureName ?? string.Empty;

        var selectedCode = string.Equals(languageMode, "Manual", StringComparison.OrdinalIgnoreCase)
            ? cultureName
            : "Auto";

        SetSelectedLanguageCode(selectedCode);
    }

    private void SetSelectedLanguageCode(string cultureCode)
    {
        BtnLanguageSelector.Tag = cultureCode;
        TxtLanguageModeValue.Text = GetLanguageDisplayText(cultureCode);
    }

    private string GetSelectedLanguageCode()
    {
        return BtnLanguageSelector.Tag?.ToString() ?? "Auto";
    }

    private string GetLanguageDisplayText(string cultureCode)
    {
        return cultureCode switch
        {
            "Auto" => T("CLSET.LanguageAuto"),
            "cs-CZ" => T("Language.Czech"),
            "de-DE" => T("Language.German"),
            "en-US" => T("Language.English"),
            _ => cultureCode
        };
    }

    private void ApplyLanguageDisplayTexts()
    {
        foreach (var item in LstLanguageModes.Items.OfType<ListBoxItem>())
        {
            var tag = item.Tag?.ToString();

            item.Content = tag switch
            {
                "Auto" => T("CLSET.LanguageAuto"),
                "cs-CZ" => T("Language.Czech"),
                "de-DE" => T("Language.German"),
                "en-US" => T("Language.English"),
                _ => item.Content
            };
        }

        SetSelectedLanguageCode(GetSelectedLanguageCode());
    }

    private void BtnLanguageSelector_Click(object sender, RoutedEventArgs e)
    {
        PopupLanguageMode.IsOpen = !PopupLanguageMode.IsOpen;
    }

    private void LstLanguageModes_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (LstLanguageModes.SelectedItem is not ListBoxItem item)
        {
            return;
        }

        var code = item.Tag?.ToString();

        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        SetSelectedLanguageCode(code);
        PopupLanguageMode.IsOpen = false;
    }

    private void SaveLanguageValuesToSettings()
    {
        var selectedCode = GetSelectedLanguageCode();

        if (string.Equals(selectedCode, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            _settings.LanguageMode = "Auto";
            _settings.CultureName = string.Empty;
            return;
        }

        _settings.LanguageMode = "Manual";
        _settings.CultureName = selectedCode;
    }

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        if (!SaveValuesToSettings())
        {
            _logClientSettingsAction(
                "ApplyClientSettingsFailed",
                "Validation failed while applying client settings.");

            return;
        }

        _applyTheme();
        _applyLocalization();
        ApplyLocalization();

        LogClientSettingsAction("ApplyClientSettings");
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (!SaveValuesToSettings())
        {
            _logClientSettingsAction(
                "SaveClientSettingsFailed",
                "Validation failed while saving client settings.");

            return;
        }

        _applyTheme();
        _applyLocalization();
        _saveSettings();
        ApplyLocalization();

        LogClientSettingsAction("SaveClientSettings");

        ShowInfo("CLSET.SavedTitle", "CLSET.SavedMessage");
    }

    private void ShowInfo(string titleKey, string messageKey)
    {
        DmsConfirmDialog.Show(
            Window.GetWindow(this),
            T(titleKey),
            T(messageKey),
            DmsDialogButtons.Ok);
    }

    private void ShowWarning(string titleKey, string messageKey)
    {
        DmsConfirmDialog.Show(
            Window.GetWindow(this),
            T(titleKey),
            T(messageKey),
            DmsDialogButtons.Ok);
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