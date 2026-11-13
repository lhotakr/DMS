using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace DMS.Desktop.Views.Orders;

public sealed partial class MesOrderHistoryView
{
    private readonly Func<string,string>? _mesordTranslator;
    private readonly string _mesordCulture;
    private readonly string _mesordUserDisplayName;

    private static readonly IReadOnlyDictionary<string,(string Key,string Cz,string De,string En)> MesordTranslations =
        new Dictionary<string,(string Key,string Cz,string De,string En)>(StringComparer.OrdinalIgnoreCase)
    {
        ["Zakázka"] = ("OrderWord", "Zakázka", "Auftrag", "Order"),
        ["Operátoři - skutečné přihlášení a odhlášení"] = ("OperatorActual", "Operátoři - skutečné přihlášení a odhlášení", "Bediener – tatsächliche An- und Abmeldungen", "Operators – actual logins and logouts"),
        ["Žádné historické stavy."] = ("NoHistoricalStates", "Žádné historické stavy.", "Keine historischen Zustände.", "No historical states."),
        ["Není dostupný v MES"] = ("UnavailableMes", "Není dostupný v MES", "Im MES nicht verfügbar", "Not available in MES"),
        ["Popis"] = ("Description", "Popis", "Beschreibung", "Description"),
        ["Skutečný začátek"] = ("ActualStart", "Skutečný začátek", "Tatsächlicher Beginn", "Actual start"),
        ["Dobré kusy"] = ("GoodPieces", "Dobré kusy", "Gutteile", "Good pieces"),
        ["Odpad"] = ("Scrap", "Odpad", "Ausschuss", "Scrap"),
        ["Vytvořeno"] = ("CreateReport", "Vytvořeno", "Erstellt", "Created"),
        ["PDF nemá žádnou vykreslitelnou stránku."] = ("PdfNoPage", "PDF nemá žádnou vykreslitelnou stránku.", "Das PDF enthält keine darstellbare Seite.", "PDF has no renderable page."),
        ["Souhrn"] = ("SheetSummary", "Souhrn", "Zusammenfassung", "Summary"),
        ["Operace zakázky"] = ("OrderOperations", "Operace zakázky", "Auftragsarbeitsgänge", "Order operations"),
        ["Historie výrobní zakázky"] = ("HistoryTitle", "Historie výrobní zakázky", "Historie des Fertigungsauftrags", "Production order history"),
        ["Souhrn zakázky"] = ("OrderSummary", "Souhrn zakázky", "Auftragsübersicht", "Order summary"),
        ["Stavy a prostoje za zakázku"] = ("ProductionStatus", "Stavy a prostoje za zakázku", "Maschinenzustände und Stillstände des Auftrags", "Order states and downtime"),
        ["Stavy a prostoje"] = ("StatesAndDowntime", "Stavy a prostoje", "Maschinenzustände und Stillstände", "States and downtime"),
        ["Stavy a prostoje – podíl a trvání"] = ("StatesShare", "Stavy a prostoje – podíl a trvání", "Zustände und Stillstände – Anteil und Dauer", "States and downtime – shares and duration"),
        ["Průběh směny – chronologie stavů"] = ("StateTimeline", "Průběh směny – chronologie stavů", "Schichtverlauf – chronologische Zustände", "Shift timeline – chronological states"),
        ["Průběh stavů směny"] = ("StateSequence", "Průběh stavů směny", "Zeitlicher Verlauf der Schichtzustände", "Shift state timeline"),
        ["Rozložení stavů zakázky"] = ("StateDistribution", "Rozložení stavů zakázky", "Verteilung der Auftragszustände", "Order state distribution"),
        ["Procesní hodnoty – časová osa MES06"] = ("ProcessTimeline", "Procesní hodnoty – časová osa MES06", "Prozesswerte – MES06-Zeitachse", "Process values – MES06 timeline"),
        ["Procesní hodnoty MES06"] = ("ProcessValues", "Procesní hodnoty MES06", "MES06-Prozesswerte", "MES06 process values"),
        ["Všechny směnové dny zakázky – sloučeno podle typu směny"] = ("AllShiftDays", "Všechny směnové dny zakázky – sloučeno podle typu směny", "Alle Schichttage des Auftrags – nach Schichtart zusammengefasst", "All order shift days – grouped by shift type"),
        ["FASTEC: žádné směnové intervaly pro vybrané období. Směny se neodhadují."] = ("NoShiftEvents", "FASTEC: žádné směnové intervaly pro vybrané období. Směny se neodhadují.", "FASTEC: Für diesen Zeitraum liegen keine Schichten vor. Keine Schätzung.", "FASTEC: No shift intervals for this period. Shifts are not estimated."),
        ["Pro tuto směnu nejsou historické procesní záznamy."] = ("NoProcessValues", "Pro tuto směnu nejsou historické procesní záznamy.", "Für diese Schicht liegen keine historischen Prozesswerte vor.", "No historical process values for this shift."),
        ["Pro tento výběr nejsou zaznamenané stavy."] = ("NoStates", "Pro tento výběr nejsou zaznamenané stavy.", "Für diese Auswahl liegen keine Zustände vor.", "No state records for this selection."),
        ["Pro tuto směnu nejsou dostupné záznamy stavů."] = ("NoShiftStates", "Pro tuto směnu nejsou dostupné záznamy stavů.", "Für diese Schicht liegen keine Zustände vor.", "No state records for this shift."),
        ["Bez záznamů stavů."] = ("NoStateRecords", "Bez záznamů stavů.", "Keine Zustandsdaten.", "No state records."),
        ["Bez záznamů"] = ("NoRecords", "Bez záznamů", "Keine Datensätze", "No records"),
        ["Směnový den"] = ("ShiftDay", "Směnový den", "Schichttag", "Shift day"),
        ["Hranice podle FASTEC"] = ("ShiftBoundaries", "Hranice podle FASTEC", "Grenzen laut FASTEC", "Boundaries from FASTEC"),
        ["Ranní"] = ("Morning", "Ranní", "Frühschicht", "Morning"),
        ["Odpolední"] = ("Afternoon", "Odpolední", "Spätschicht", "Afternoon"),
        ["Noční"] = ("Night", "Noční", "Nachtschicht", "Night"),
        ["Nezařazené"] = ("Unclassified", "Nezařazené", "Nicht zugeordnet", "Unclassified"),
        ["Exportovat XLSX"] = ("ExportExcel", "Exportovat XLSX", "XLSX exportieren", "Export XLSX"),
        ["Exportovat PDF"] = ("ExportPdf", "Exportovat PDF", "PDF exportieren", "Export PDF"),
        ["Stav"] = ("State", "Stav", "Zustand", "State"),
        ["Minut"] = ("Minutes", "Minut", "Minuten", "Minutes"),
        ["Minuty"] = ("StateMinutes", "Minuty", "Minuten", "Minutes"),
        ["PodílProcent"] = ("SharePercent", "PodílProcent", "Anteil (%)", "Share (%)"),
        ["Operátoři"] = ("Operators", "Operátoři", "Bediener", "Operators"),
        ["Operátoři – přihlášení"] = ("OperatorLogins", "Operátoři – přihlášení", "Bediener – Anmeldungen", "Operators – logins"),
        ["Operátoři - přihlášení a odhlášení"] = ("OperatorLoginLogout", "Operátoři - přihlášení a odhlášení", "Bediener – An- und Abmeldungen", "Operators – logins and logouts"),
        ["Pracoviště"] = ("Workcenter", "Pracoviště", "Arbeitsplatz", "Work center"),
        ["Osobní číslo"] = ("PersonalNumber", "Osobní číslo", "Personalnummer", "Personnel number"),
        ["OsobníČíslo"] = ("PersonalNumberRaw", "OsobníČíslo", "Personalnummer", "Personnel number"),
        ["Jméno"] = ("Name", "Jméno", "Name", "Name"),
        ["Přihlášení"] = ("Login", "Přihlášení", "Anmeldung", "Login"),
        ["Odhlášení"] = ("Logout", "Odhlášení", "Abmeldung", "Logout"),
        ["Hodin ve směně"] = ("HoursInShift", "Hodin ve směně", "Stunden in der Schicht", "Hours in shift"),
        ["HodinVeSměně"] = ("HoursInShiftRaw", "HodinVeSměně", "Stunden in der Schicht", "Hours in shift"),
        ["Stále přihlášen"] = ("StillLogged", "Stále přihlášen", "Noch angemeldet", "Still logged in"),
        ["Otevřeno"] = ("Open", "Otevřeno", "Offen", "Open"),
        ["Čítače"] = ("Counters", "Čítače", "Zähler", "Counters"),
        ["Čítač"] = ("Counter", "Čítač", "Zähler", "Counter"),
        ["Souhrn čítačů"] = ("CounterSummary", "Souhrn čítačů", "Zählerübersicht", "Counter summary"),
        ["Čítače – součet hodnot"] = ("CounterSum", "Čítače – součet hodnot", "Zähler – Summe der Werte", "Counters – sum of values"),
        ["Čítače - součet hodnot za směnu"] = ("CounterShiftSum", "Čítače - součet hodnot za směnu", "Zähler – Summe der Schichtwerte", "Counters – sum of shift values"),
        ["Čítače - součet uložených hodnot"] = ("CounterOrderSum", "Čítače - součet uložených hodnot", "Zähler – Summe gespeicherter Werte", "Counters – sum of stored values"),
        ["Součet hodnot"] = ("CounterSumValues", "Součet hodnot", "Summe der Werte", "Sum of values"),
        ["SoučetHodnot"] = ("CounterSumValuesRaw", "SoučetHodnot", "Summe der Werte", "Sum of values"),
        ["Seznam směn"] = ("ShiftList", "Seznam směn", "Schichtliste", "Shift list"),
        ["Začátek"] = ("ShiftStart", "Začátek", "Beginn", "Start"),
        ["Konec"] = ("ShiftEnd", "Konec", "Ende", "End"),
        ["Směna"] = ("Shift", "Směna", "Schicht", "Shift"),
        ["Datum"] = ("Date", "Datum", "Datum", "Date"),
        ["Datum vytvoření"] = ("CreatedAt", "Datum vytvoření", "Erstellt am", "Created at"),
        ["Autor (Windows)"] = ("Author", "Autor (Windows)", "Autor (Windows)", "Author (Windows)"),
        ["Zakázka 2000"] = ("Order2000", "Zakázka 2000", "Auftrag 2000", "Order 2000"),
        ["Zakázka 9200"] = ("Order9200", "Zakázka 9200", "Auftrag 9200", "Order 9200"),
        ["Závod 2000"] = ("Plant2000", "Závod 2000", "Werk 2000", "Plant 2000"),
        ["Závod 9200"] = ("Plant9200", "Závod 9200", "Werk 9200", "Plant 9200"),
        ["Zatím nedoplněno (doplní kvalita)"] = ("QualityPending", "Zatím nedoplněno (doplní kvalita)", "Noch nicht ergänzt (Qualität)", "Not yet supplied (Quality)"),
        ["Trvání zakázky"] = ("OrderDuration", "Trvání zakázky", "Auftragsdauer", "Order duration"),
        ["Průběh zakázky"] = ("OrderProgress", "Průběh zakázky", "Auftragsverlauf", "Order period"),
        ["Operace"] = ("Operation", "Operace", "Arbeitsgang", "Operation"),
        ["Celá zakázka"] = ("AllOrder", "Celá zakázka", "Gesamter Auftrag", "Whole order"),
        ["Artikl / SAP"] = ("ArticleSap", "Artikl / SAP", "Artikel / SAP", "Article / SAP"),
        ["Artikl"] = ("Article", "Artikl", "Artikel", "Article"),
        ["Výroba (plán / OK / NOK)"] = ("PlannedOkNok", "Výroba (plán / OK / NOK)", "Produktion (Plan / Gut / Ausschuss)", "Production (planned / OK / NOK)"),
        ["Plán"] = ("Planned", "Plán", "Plan", "Planned"),
        ["OK"] = ("Good", "OK", "Gut", "OK"),
        ["NOK"] = ("Reject", "NOK", "Ausschuss", "NOK"),
        ["Hotovo"] = ("Finished", "Hotovo", "Fertig", "Finished"),
        ["Zbývá"] = ("Pending", "Zbývá", "Ausstehend", "Remaining"),
        ["Plnění zakázky"] = ("OrderFulfilment", "Plnění zakázky", "Auftragserfüllung", "Order completion"),
        ["Průběh stavů směny (světlý motiv)"] = ("ChartStates", "Průběh stavů směny (světlý motiv)", "Schichtzustände (helles Design)", "Shift state timeline (light theme)"),
        ["Procesní hodnoty - časová osa MES06"] = ("ProcessShift", "Procesní hodnoty - časová osa MES06", "Prozesswerte – MES06-Zeitachse", "Process values – MES06 timeline"),
        ["Pracoviště / proces"] = ("WorkcenterProcess", "Pracoviště / proces", "Arbeitsplatz / Prozess", "Work center / process"),
        ["Skutečná směna (FASTEC)"] = ("RealShift", "Skutečná směna (FASTEC)", "Tatsächliche Schicht (FASTEC)", "Actual shift (FASTEC)"),
        ["MESORD – export"] = ("ExportOpenTitle", "MESORD – export", "MESORD – Export", "MESORD – export"),
        ["Export se nepodařil:"] = ("ExportFailure", "Export se nepodařil:", "Export fehlgeschlagen:", "Export failed:"),
        ["Soubor se nepodařilo otevřít:"] = ("OpenFailure", "Soubor se nepodařilo otevřít:", "Datei konnte nicht geöffnet werden:", "Could not open file:"),
        ["Neúplná data:"] = ("DataIncomplete", "Neúplná data:", "Unvollständige Daten:", "Incomplete data:"),
        ["Kalendář výroby"] = ("ProductionCalendar", "Kalendář výroby", "Produktionskalender", "Production calendar"),
        ["Zobrazit všechny směny"] = ("AllShifts", "Zobrazit všechny směny", "Alle Schichten anzeigen", "Show all shifts"),
        ["Chceš jej nyní otevřít?"] = ("OpenFileQuestion", "Chceš jej nyní otevřít?", "Möchtest du die Datei jetzt öffnen?", "Open the file now?"),
        ["Soubor byl úspěšně vytvořen:"] = ("FileCreated", "Soubor byl úspěšně vytvořen:", "Datei wurde erfolgreich erstellt:", "File created successfully:"),
        ["Pro vybraný den nejsou dostupné historické směny."] = ("ShiftNone", "Pro vybraný den nejsou dostupné historické směny.", "Für den ausgewählten Tag sind keine historischen Schichten verfügbar.", "No historical shifts for selected day."),
        ["Pozn.: součet hodnot kumulativního čítače není automaticky počtem vyrobených kusů."] = ("RecordedSumCaveat", "Pozn.: součet hodnot kumulativního čítače není automaticky počtem vyrobených kusů.", "Hinweis: Die Summe kumulativer Zählerwerte entspricht nicht automatisch der Stückzahl.", "Note: Sum of cumulative counter values does not necessarily equal produced pieces."),
        ["Pozn.: součet čítačů je součet historických hodnot, ne ověřený počet kusů."] = ("CountCaveat", "Pozn.: součet čítačů je součet historických hodnot, ne ověřený počet kusů.", "Hinweis: Zählersumme ist die Summe historischer Werte, nicht die bestätigte Stückzahl.", "Note: Counter sum is historical values, not verified piece count."),
        ["Stavy – celá zakázka"] = ("ShiftStatesSummary", "Stavy – celá zakázka", "Zustände – gesamter Auftrag", "States – whole order"),
        ["Založeno"] = ("StatusCreated", "Založeno", "Angelegt", "Created"),
        ["Uvolněno"] = ("StatusReleased", "Uvolněno", "Freigegeben", "Released"),
        ["Ve výrobě"] = ("StatusProduction", "Ve výrobě", "In Produktion", "In production"),
        ["Částečně uvolněno"] = ("StatusPartReleased", "Částečně uvolněno", "Teilweise freigegeben", "Partially released"),
        ["Uvolnění zrušeno"] = ("StatusReleaseRevoked", "Uvolnění zrušeno", "Freigabe aufgehoben", "Release revoked"),
        ["Zrušeno"] = ("StatusCancelled", "Zrušeno", "Storniert", "Cancelled"),
        ["Nedokončeno"] = ("StatusIncomplete", "Nedokončeno", "Unvollständig", "Incomplete"),
        ["Uzavřeno"] = ("StatusClosed", "Uzavřeno", "Abgeschlossen", "Closed"),
        ["Export PDF/XLSX"] = ("ReportExport", "Export PDF/XLSX", "PDF/XLSX-Export", "PDF/XLSX export")
    };

    private string MesordText(string text)
    {
        if (!MesordTranslations.TryGetValue(text ?? string.Empty, out var e))
            return text ?? string.Empty; // FASTEC state names and business data are not translated.
        var key = "MESORD." + e.Key;
        var translated = _mesordTranslator?.Invoke(key);
        if (!string.IsNullOrWhiteSpace(translated) && !translated.StartsWith("[[", StringComparison.Ordinal))
            return translated;
        return _mesordCulture.StartsWith("de", StringComparison.OrdinalIgnoreCase) ? e.De :
            _mesordCulture.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? e.En : e.Cz;
    }

    private string MesordFormat(string source, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, MesordText(source), args);
}
