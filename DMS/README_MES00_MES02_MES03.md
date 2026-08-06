# DMS MES transactions patch

This patch replaces the earlier NET00 idea with SAP-like MES transactions:

- `MES00` - central MES communication settings.
- `MES02` - editable device list, saved back into `Config/devices.txt`.
- `MES03` - read-only device monitor / availability overview.

## Data files

### Config/devices.txt

Plain text device list used by MES02 and MES03.

Format:

```text
address-or-hostname;category;name;note
```

Examples:

```text
10.131.10.5;SERVER;CZE-FASTEC01;Hlavni MES server
10.131.10.60;STROJ;BRIO K14-01;
10.131.10.10;TERMINAL;K14-07;SH15 Blackline
```

### Config/mes-communication-settings.json

Settings edited by MES00 and used by MES03:

- monitoring enabled/disabled,
- ping timeout,
- max parallel checks,
- auto refresh interval,
- future machine unlock signal parameters.

The machine unlock section is intentionally only a configuration placeholder. DMS must not become the machine safety system. The PLC / B&R side must still make the final safety decision.

## Installation

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\Install-MES00-MES02-MES03.ps1
```

Explicit paths:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\Install-MES00-MES02-MES03.ps1 `
  -ProjectRoot "C:\Users\rlhotak\DevelopmentEnv\DMS\DMS" `
  -DmsConfigRoot "Z:\SAP\DMS-db\DEV\Config"
```

By default the script removes the obsolete transaction config entry `NET00`, if it exists. To keep it:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\Install-MES00-MES02-MES03.ps1 -KeepNet00
```

## After installation

1. Rebuild `DMS.Desktop`.
2. Run `MES00` and review communication settings.
3. Run `MES02` and check/edit `devices.txt`.
4. Run `MES03` and test the monitor.

## Generated snapshot

MES03 stores its last status snapshot here:

```text
<DataRoot>\Data\MES\mes03-last-snapshot.json
```


## v3: parametrizace devices.txt

Cesta k zařízení je nově v MES00 jako `Soubor devices.txt`. MES02/MES03 ji načítají z `mes-communication-settings.json`. Výchozí doporučená cesta je `\\10.131.10.5\FISData\devices.txt`.
