# MES patch v3 - parametrizace devices.txt

Tato verze doplňuje do MES00 pole pro cestu k souboru `devices.txt`.

Výchozí serverová cesta k souboru:

```text
\\10.131.10.5\FISData\devices.txt
```

MES02 a MES03 už nepoužívají natvrdo `Config\devices.txt`. Do MES00 lze zadat i jen složku `\\10.131.10.5\FISData`; DMS pak použije `devices.txt` uvnitř. Cestu načtou z `mes-communication-settings.json`. Pokud cesta není vyplněná, použije se fallback `Config\devices.txt`.

Instalace:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\Install-MES00-MES02-MES03.ps1
```

Volitelně lze nastavit jinou výchozí cestu při instalaci:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\Install-MES00-MES02-MES03.ps1 `
  -DefaultMesDevicesFilePath "\\10.131.10.5\FISData\devices.txt"
```
