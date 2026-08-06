# MES00/MES02/MES03 patch v2 - self-copy fix

This version fixes the installer script failure:

```text
Copy-Item : Cannot overwrite the item ... App.xaml with itself.
```

The error happens when the patch is extracted directly into the DMS project root and the installer script sees the project source folder as both source and target.

Run from the DMS project root:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\Install-MES00-MES02-MES03.ps1
```

Or with explicit paths:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\Install-MES00-MES02-MES03.ps1 `
  -ProjectRoot "C:\Users\rlhotak\DevelopmentEnv\DMS\DMS" `
  -DmsConfigRoot "Z:\SAP\DMS-db\DEV\Config"
```
