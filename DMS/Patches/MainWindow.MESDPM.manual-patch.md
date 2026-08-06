# MainWindow – ruční zapojení MESDPM

Cílový soubor:

`src/DMS.Desktop/Views/MainWindow.xaml.cs`

## 1. Using

Přidej mezi ostatní Views:

```csharp
using DMS.Desktop.Views.Mes;
```

## 2. Handler

Do pole `handlers` v `InitializeTransactions()` přidej před obecný fallback:

```csharp
// MES
new MesDataPointMonitorTransactionHandler(),
```

`DMS.Core.Transactions.Handlers` už je v MainWindow importovaný, takže další using není potřeba.

## 3. Přepínač vykreslení transakce

Do `RenderTransactionResult(TransactionResult result)` přidej:

```csharp
case "MESDPM":
    RenderMesDataPointMonitor(result.Parameter);
    break;
```

## 4. Tenký wrapper

Přidej vedle ostatních `Render...` metod:

```csharp
private void RenderMesDataPointMonitor(string? query)
{
    WorkspacePanel.Children.Clear();

    WorkspacePanel.Children.Add(
        new MesDataPointMonitorView(
            query,
            _appSettings.ConfigurationRootPath,
            _logger,
            _currentUser.DisplayName,
            key => T(key)));

    ResetWorkspaceScroll();
}
```

Business logika ani Modbus komunikace tím nevstupují do `MainWindow`; shell pouze vytvoří samostatný View.
