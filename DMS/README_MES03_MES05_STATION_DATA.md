# MES v4 – MES03 data stanic + MES05 soupis pracovišť

## Změna transakcí

- `MES00` zůstává centrální nastavení MES komunikace.
- `MES02` zůstává editace `devices.txt`.
- `MES03` je nově čtení datových bodů z jednotlivých stanic.
- Původní monitor/soupis zařízení je přesunut na `MES05` jako soupis pracovišť.

## Architektura

Backend MES logiky je přesunut do projektu:

```text
src/DMS.Integration.Mes
```

Desktop projekt obsahuje jen WPF obrazovky a řádkové modely pro grid.

## Datové body MES03

Výchozí `mes-stations.json` obsahuje šablonu pro:

```text
Counter1
Counter2
Input1 ... Input6
Output1 ... Output6
```

Pro Siemens stanice jsou připravené záznamy:

```text
PSL-3  -> 10.131.10.87
HPR-1  -> 10.131.10.88
KMP-L1 -> 10.131.10.89
```

Pro B&R/BRIO je připravený gateway režim. U B&R je potřeba doplnit konkrétní port / gateway nebo později schválenou vendor knihovnu. Patch záměrně nepřidává externí NuGet balíček, aby nerozbil build v interním prostředí.

## Podporované režimy v DMS.Integration.Mes

- `Simulated` – testovací hodnoty bez PLC.
- `FileMirror` – načítá JSON snímek stanice ze složky.
- `TcpText` – jednoduchý gateway protokol `READ <StationCode>`, odpověď `Counter1=123;Input1=True;...`.
- `BRGateway` – B&R/BRIO přes stejný textový gateway kontrakt.
- `SiemensS7` – bezpečný placeholder; UI a konfigurace jsou připravené, přímý S7 driver se doplní až po potvrzení adres.

## Instalace

Z rootu projektu:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\Install-MES03-MES05-StationData.ps1
```

Případně s explicitní cestou na config:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\Install-MES03-MES05-StationData.ps1 `
  -ProjectRoot "C:\Users\rlhotak\DevelopmentEnv\DMS\dms" `
  -DmsConfigRoot "Z:\SAP\DMS-db\DEV\Config"
```

Po instalaci:

```powershell
dotnet clean .\src\DMS.Desktop\DMS.Desktop.csproj
dotnet build .\src\DMS.Desktop\DMS.Desktop.csproj
```
