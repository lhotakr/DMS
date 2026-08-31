# MES Reporting Integration

## Scope

DMS reads FASTEC SQL data in **read-only** mode and exposes a dynamic MES reporting transaction.

- `MESSET` – database connection settings and connection test
- `MES06` – dynamic report definitions, filters, chart/table rendering and Excel export
- main window status – `MES: connection established / disconnected / disabled`

## Project boundaries

`DMS.Integration.Mes` owns:

- SQL connection settings
- connection string creation
- health checks
- parameterized SQL queries
- FASTEC reporting DTOs
- JSON report definitions

`DMS.Desktop` owns:

- WPF views
- LiveCharts2 rendering
- Excel export
- status bar presentation

No WPF dependency is introduced into `DMS.Integration.Mes`.

## FASTEC source

Server: `CZE-SQL01`  
Database: `FastecCZE`  
Reporting schema: `ana`

Initial data sources:

- `FactMdaMes` + `DimMdaOperation` + `DimWorkcenter`
- `FactMdaState` + `DimMdaState`
- `FactMdaCounter` + `DimMdaCounter`

The current SQL account has demonstrated `SELECT` access. `VIEW DEFINITION` is not required by the DMS runtime.

## Read-only rule

The integration intentionally exposes no generic SQL execution API and no INSERT/UPDATE/DELETE methods. Reporting queries are built into `MesReportingDataService` and use parameters for user-provided filters.

## Counter caution

Counter data contains resets/corrections and may include negative values. `MES06` therefore exposes counter events without a default SUM chart until individual counter semantics are validated.

## Dynamic reports

`mes-report-definitions.json` controls:

- report name/description
- data source (`Production`, `States`, `Counters`)
- row limit
- DataGrid columns
- optional chart aggregation (`Column` / `Line`)

New layouts can therefore be added without creating another WPF view as long as they use an existing MES reporting data source.
