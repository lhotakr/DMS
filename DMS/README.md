# DMS – Documentation Management System
DMS je interní systém pro správu artiklové, technologické a zakázkové dokumentace.
Hlavní modul: Digitální Artikelmapa  
Primární klient: WPF FiGUI desktop klient


## Cíle první verze
- spustit DMS FiGUI Shell,
- zadat transakci `/nART03 1000018165`,
- zobrazit kartu artiklu,
- připravit základ pro dokumenty, síta, zakázky a SAP/MES integrace.

## Struktura solution
- `DMS.Desktop` – WPF klient
- `DMS.Core` – business logika, transakce, služby
- `DMS.Data` – databáze a repository
- `DMS.Shared` – společné modely
- `DMS.Integration.Sap` – SAP integrace
- `DMS.Integration.Mes` – MES / Excel import

## Pravidla
- Business logika nepatří do WPF code-behind.
- Citlivé údaje a hesla nepatří do repozitáře.
- Každá transakce musí mít popis účelu a parametrů.

## WPF poznámka
Hlavní okno aplikace je umístěné ve složce `Views`:

- XAML: `DMS.Desktop/Views/MainWindow.xaml`
- Code-behind: `DMS.Desktop/Views/MainWindow.xaml.cs`
- Třída: `DMS.Desktop.Views.MainWindow`
- StartupUri: `Views/MainWindow.xaml`