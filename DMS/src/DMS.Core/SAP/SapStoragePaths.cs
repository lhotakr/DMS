namespace DMS.Core.Sap;

public sealed class SapStoragePaths
{
    public string RootDirectory { get; }

    public string ConfigDirectory =>
        Path.Combine(RootDirectory, "Config");

    public string SapMirrorDirectory =>
        Path.Combine(RootDirectory, "SapMirror");

    public string DmsDataDirectory =>
        Path.Combine(RootDirectory, "DmsData");

    public string MaterialRangesFilePath =>
        Path.Combine(ConfigDirectory, "sap-material-ranges.json");

    public string SapMaterialsFilePath =>
        Path.Combine(SapMirrorDirectory, "sap-materials.json");

    public string SapBomSnapshotsFilePath =>
        Path.Combine(SapMirrorDirectory, "sap-boms.json");

    public string SapRoutingSnapshotsFilePath =>
        Path.Combine(SapMirrorDirectory, "sap-routings.json");

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
}