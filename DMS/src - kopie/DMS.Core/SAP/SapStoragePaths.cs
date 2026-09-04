namespace DMS.Core.Sap;

public sealed class SapStoragePaths
{
    public string RootDirectory { get; }

    public string ConfigDirectory =>
        Path.Combine(RootDirectory, "Config");

    public string SapMirrorDirectory =>
        Path.Combine(RootDirectory, "SapMirror");

    /// <summary>
    /// Starší cache složka. Používá se jako fallback, dokud všechny views
    /// nepřejdou na SapMirror. Sjednocení cest je samostatný refactor krok.
    /// </summary>
    public string DataDirectory =>
        Path.Combine(RootDirectory, "Data");

    public string DmsDataDirectory =>
        Path.Combine(RootDirectory, "DmsData");

    public string MaterialRangesFilePath =>
        Path.Combine(ConfigDirectory, "sap-material-rules.json");

    // Materiály — SAP00 ukládá do SapMirror, zde žádný fallback není potřeba
    public string SapMaterialsFilePath =>
        Path.Combine(SapMirrorDirectory, "sap-materials.json");

    // BOMs — nová cesta SapMirror, fallback na Data
    public string SapBomSnapshotsFilePath =>
        ResolveWithFallback("sap-boms.json");

    // Routings — nová cesta SapMirror, fallback na Data
    public string SapRoutingSnapshotsFilePath =>
        ResolveWithFallback("sap-routings.json");

    // Work centers — nová cesta SapMirror, fallback na Data
    public string SapWorkCentersFilePath =>
        ResolveWithFallback("sap-work-centers.json");

    public string SapImportHistoryFilePath =>
        Path.Combine(SapMirrorDirectory, "sap-import-history.json");

    public SapStoragePaths(string rootDirectory)
    {
        RootDirectory = rootDirectory;
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(SapMirrorDirectory);
        Directory.CreateDirectory(DmsDataDirectory);
    }

    /// <summary>
    /// Vrátí cestu ze SapMirror, pokud soubor existuje.
    /// Jinak vrátí cestu z Data (legacy). Tím fungují starší i nové importy.
    /// </summary>
    private string ResolveWithFallback(string fileName)
    {
        var mirrorPath = Path.Combine(SapMirrorDirectory, fileName);

        if (File.Exists(mirrorPath))
        {
            return mirrorPath;
        }

        return Path.Combine(DataDirectory, fileName);
    }
}