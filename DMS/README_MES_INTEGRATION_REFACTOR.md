# MES integration refactor - 2026-07-30

Tato úprava přesouvá MES backend logiku z `DMS.Desktop` do samostatného projektu `DMS.Integration.Mes`.

## Nové rozdělení

`DMS.Integration.Mes`:

- `Models/MesCommunicationSettings.cs`
- `Models/MesDevice.cs`
- `Models/MesProbeResult.cs`
- `Models/MesMonitorSnapshot.cs`
- `Services/MesCommunicationSettingsService.cs`
- `Services/MesDeviceFileService.cs`
- `Services/MesProbeService.cs`

`DMS.Desktop`:

- `Views/Mes/*` zůstávají WPF obrazovky pro transakce MES00, MES02, MES03.
- `Models/MesDeviceEditRow.cs` a `Models/MesDeviceStatusRow.cs` zůstávají UI row/view-model objekty pro gridy.

## Parametrizovaný devices.txt

MES00 ukládá cestu k souboru zařízení do:

```text
Config/mes-communication-settings.json
```

Výchozí cesta je:

```text
\\10.131.10.5\FISData\devices.txt
```

MES02 i MES03 používají cestu z MES00. Pokud cesta v nastavení chybí, shell použije fallback `Config/devices.txt`.

## Build z rootu

Původní root `DMS.csproj` byl starý .NET Framework EXE projekt bez `Main()`, takže `dotnet build` v rootu padal na CS5001. Je nahrazen SDK-style agregačním projektem, který postaví hlavní source projekty včetně `DMS.Desktop`.

Doporučená kontrola:

```powershell
dotnet clean .\src\DMS.Desktop\DMS.Desktop.csproj
dotnet build .\src\DMS.Desktop\DMS.Desktop.csproj
```

nebo z rootu:

```powershell
dotnet build
```
