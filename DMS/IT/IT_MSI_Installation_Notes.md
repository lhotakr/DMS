# DMS Desktop - poznámky pro IT

## Typ balíčku

Instalátor je vytvořen pomocí Visual Studio Installer Project.

Výstup:

- `setup.exe`
- `DMS.Setup.msi`

## Instalace

Doporučené:

```text
setup.exe
```

Pro enterprise deployment lze použít MSI podle interních pravidel IT.

## Aplikační data

Instalace obsahuje pouze desktop klienta. Sdílená data a konfigurace DMS jsou mimo instalační složku, typicky:

```text
Z:\SAP\DMS-db\DEV\Config
Z:\SAP\DMS-db\PROD\Config
```

Klient musí mít přístup ke sdílené cestě podle prostředí.

## První ověření po instalaci

1. Spustit DMS Desktop.
2. Ověřit načtení levého menu.
3. Ověřit přihlášeného Windows uživatele.
4. Otevřít HELP.
5. Otevřít SAP00 cache status.
6. Otevřít QA03 / QO05 / DOC03.
7. Ověřit LOG03.

## Odinstalace

Standardně přes Windows Apps / Programs and Features.
