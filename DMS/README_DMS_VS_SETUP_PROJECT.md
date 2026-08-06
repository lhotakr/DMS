# DMS - Visual Studio Installer Project varianta

Tento balíček připravuje variantu bez Inno Setupu. Využije rozšíření **Microsoft Visual Studio Installer Projects 2022**, které už je ve Visual Studiu nainstalované.

Výstupem bude klasický Setup projekt ve Visual Studiu, typicky:

- `setup.exe`
- `DMS.Setup.msi`

## 1) Připrav publish profil

Z rootu projektu spusť:

```powershell
cd C:\Users\rlhotak\DevelopmentEnv\DMS\DMS
powershell -NoProfile -ExecutionPolicy Bypass -File .\Scripts\Prepare-DMS-VS-SetupProject.ps1
```

Tím se vytvoří:

```text
src\DMS.Desktop\Properties\PublishProfiles\DMS.Setup.pubxml
```

Profil je nastavený jako **Release / win-x64 / self-contained**, aby IT nemuselo na stanicích řešit .NET Desktop Runtime.

## 2) Volitelné ověření publish výstupu

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Scripts\Build-DMS-Publish-For-VSSetup.ps1 -Clean
```

Očekávaný výstup:

```text
src\DMS.Desktop\bin\Release\net9.0-windows\win-x64\publish
```

## 3) Vytvoření Setup projektu ve Visual Studiu

1. Otevři řešení / projekt DMS ve Visual Studiu.
2. Pravý klik na Solution → **Add** → **New Project**.
3. Vyhledej `Setup Project`.
4. Název projektu dej například:

```text
DMS.Setup
```

5. V Setup projektu otevři **File System**.
6. Pravý klik na **Application Folder** → **Add** → **Project Output...**
7. V dropdownu Project vyber:

```text
DMS.Desktop
```

8. Místo `Primary Output` vyber:

```text
Publish Items
```

9. Potvrď OK.

## 4) Nastavení self-contained publish profilu v Setup projektu

V Setup projektu klikni na položku:

```text
Publish Items from DMS.Desktop (Active)
```

Otevři Properties pomocí `F4` a nastav:

```text
PublishProfilePath = Properties\PublishProfiles\DMS.Setup.pubxml
```

## 5) Shortcuty

V Setup projektu:

1. File System → Application Folder.
2. Vytvoř shortcut na výstup aplikace / `DMS.Desktop.exe`.
3. Shortcut pojmenuj:

```text
DMS Desktop
```

4. Přesuň/copy shortcut do:

```text
User's Desktop
User's Programs Menu
```

Pokud Visual Studio nenabídne přímo `DMS.Desktop.exe` před prvním buildem, nejdřív Setup projekt jednou sestav a pak shortcut doplň.

## 6) Doporučené vlastnosti Setup projektu

Vyber projekt `DMS.Setup` a nastav v Properties:

```text
ProductName   = DMS Desktop
Manufacturer  = Heinz Glas Decor / Interní DMS
Version       = 0.9.0
RemovePreviousVersions = True
InstallAllUsers = False
TargetPlatform = x64
```

Při změně `Version` Visual Studio nabídne změnu `ProductCode`; potvrdit **Ano**. `UpgradeCode` nechávat stejný mezi verzemi.

## 7) Build instalačky

Pravý klik na `DMS.Setup` → **Build**.

Výstup bývá typicky zde:

```text
src\DMS.Setup\Release\setup.exe
src\DMS.Setup\Release\DMS.Setup.msi
```

## 8) Co předat IT

Předat celou složku `Release` ze Setup projektu:

```text
setup.exe
DMS.Setup.msi
```

K tomu přilož:

```text
IT\IT_MSI_Installation_Notes.md
IT\Release_Checklist_MSI.md
```

## Poznámka k DMS datům

Instalátor balí pouze aplikaci. Sdílený DMS root, například `Z:\SAP\DMS-db\DEV` nebo později `PROD`, zůstává mimo instalačku. App config musí mířit na správný `ConfigurationRootPath`.
