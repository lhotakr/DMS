namespace DMS.Desktop.Settings;

/// <summary>
/// Uživatelská nastavení DMS klienta.
/// Později mohou být ukládána do databáze podle Windows loginu.
/// Pro první verzi se ukládají lokálně do JSON souboru.
/// </summary>
public sealed class DmsUserSettings
{
    /// <summary>
    /// Maximální počet posledních transakcí uložených v historii.
    /// Podobně jako v SAP GUI.
    /// </summary>
    public int MaxTransactionHistoryItems { get; set; } = 10;

    /// <summary>
    /// Historie posledních zadaných transakcí.
    /// Nejnovější položka je první.
    /// </summary>
    public List<string> TransactionHistory { get; set; } = new();

    /// <summary>
    /// Oblíbené transakce uživatele.
    /// Ukládá se jen kód transakce, například ART03 nebo DOC03.
    /// </summary>
    public List<string> FavoriteTransactions { get; set; } = new()
    {
        "ART03",
        "DOC03",
        "SCR03",
        "SCR10",
        "ORD10"
    };

    /// <summary>
    /// Volitelná transakce spuštěná po otevření hlavního okna, například QAMENU.
    /// Prázdná hodnota znamená bez automatického spuštění.
    /// </summary>
    public string StartupTransaction { get; set; } = string.Empty;

    /// <summary>
    /// Light / Dark.
    /// </summary>
    public string ThemeMode { get; set; } = "Light";

    /// <summary>
    /// Hlavní akcentní barva klienta ve formátu HEX.
    /// </summary>
    public string BackgroundColor { get; set; } = "#F4F6F8";
    public string PanelColor { get; set; } = "#FFFFFF";
    public string ForegroundColor { get; set; } = "#111111";
    public string MutedForegroundColor { get; set; } = "#666666";
    public string BorderColor { get; set; } = "#D0D7DE";
    public string AccentColor { get; set; } = "#0B2A4A";
    public string OnAccentColor { get; set; } = "#FFFFFF";

    /// <summary>
    /// Barevné zvýraznění stavů editovatelných řádků v DataGridu.
    /// Používá se jednotně napříč celým klientem.
    /// </summary>
    public string DataGridAddedRowColor { get; set; } = "#263A28";
    public string DataGridModifiedRowColor { get; set; } = "#4A3820";
    public string DataGridDeletedRowColor { get; set; } = "#4A2020";

    /// <summary>
    /// Auto = jazyk podle systému, Manual = uživatelem zvolený jazyk.
    /// </summary>
    public string LanguageMode { get; set; } = "Auto";

    /// <summary>
    /// Uživatelem zvolená kultura, například cs-CZ, de-DE nebo en-US.
    /// Používá se pouze při LanguageMode = Manual.
    /// </summary>
    public string CultureName { get; set; } = "";
}
