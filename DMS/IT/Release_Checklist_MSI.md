# DMS MSI release checklist

## Build

- [ ] `dotnet build -c Release` prošel
- [ ] `dotnet publish` přes `DMS.Setup.pubxml` prošel
- [ ] Setup project build prošel
- [ ] Vygenerováno `setup.exe`
- [ ] Vygenerováno `.msi`

## Smoke test po instalaci

- [ ] DMS se spustí
- [ ] Logo / shell se zobrazí správně
- [ ] Levé menu a oblíbené fungují
- [ ] HELP funguje
- [ ] SYS01 funguje
- [ ] SAP00 cache status funguje
- [ ] QA03 funguje
- [ ] QO05 filtrování funguje
- [ ] QO06 bez parametru otevře výběr blokovaných zakázek
- [ ] DOC03 funguje
- [ ] LOG03 funguje

## Data/config

- [ ] `appsettings.json` míří na správný `ConfigurationRootPath`
- [ ] Uživatel má práva na DMS data root
- [ ] Lokalizace se načítá
- [ ] Transakce se načítají z aktivního `transactions.json`
