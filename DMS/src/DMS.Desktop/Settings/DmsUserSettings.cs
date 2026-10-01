namespace DMS.Desktop.Settings;

/// <summary>
/// Uživatelská nastavení DMS klienta.
/// Později mohou být ukládána do databáze podle Windows loginu.
/// Pro první verzi se ukládají lokálně do JSON souboru.
/// </summary>
public sealed class DmsUserSettings
{
    public int MaxTransactionHistoryItems { get; set; } = 10;

    public List<string> TransactionHistory { get; set; } = new();

    public List<string> FavoriteTransactions { get; set; } = new()
    {
        "ART03",
        "DOC03",
        "SCR03",
        "SCR10",
        "ORD10"
    };

    public string StartupTransaction { get; set; } = string.Empty;

    public string ThemeMode { get; set; } = "Light";

    public string BackgroundColor { get; set; } = "#F4F6F8";
    public string PanelColor { get; set; } = "#FFFFFF";
    public string ForegroundColor { get; set; } = "#111111";
    public string MutedForegroundColor { get; set; } = "#666666";
    public string BorderColor { get; set; } = "#D0D7DE";
    public string AccentColor { get; set; } = "#0B2A4A";
    public string OnAccentColor { get; set; } = "#FFFFFF";

    public string DataGridAddedRowColor { get; set; } = "#263A28";
    public string DataGridModifiedRowColor { get; set; } = "#4A3820";
    public string DataGridDeletedRowColor { get; set; } = "#4A2020";

    public string LanguageMode { get; set; } = "Auto";
    public string CultureName { get; set; } = "";

    // Client-local SAP-like function-key preferences.
    public bool FunctionKeysEnabled { get; set; } = true;
    public bool ShowFunctionKeyBar { get; set; } = true;
    public bool FunctionKeyF1Enabled { get; set; } = true;
    public bool FunctionKeyF2Enabled { get; set; } = true;
    public bool FunctionKeyF3Enabled { get; set; } = true;
    public bool FunctionKeyF4Enabled { get; set; } = true;
    public bool FunctionKeyF5Enabled { get; set; } = true;
    public bool FunctionKeyF6Enabled { get; set; } = true;
    public bool FunctionKeyF7Enabled { get; set; } = true;
    public bool FunctionKeyF8Enabled { get; set; } = true;
    public bool FunctionKeyF9Enabled { get; set; } = true;
    public bool FunctionKeyF12Enabled { get; set; } = true;

    // Built-in DMS documentation / contextual help preferences.
    public bool DocumentationHelpEnabled { get; set; } = true;
    public bool DocumentationTechnicalInfoEnabled { get; set; } = false;

    // SCR10 – lokální nastavení konkrétního DMS klienta.
    public string Scr10SelectedWorkcenterGroup { get; set; } = string.Empty;
    public bool Scr10AutoRefreshEnabled { get; set; } = true;
    public int Scr10AutoRefreshIntervalMs { get; set; } = 1000;
}