using ClosedXML.Excel;
using DMS.Integration.Mes.Reporting.Definitions;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace DMS.Desktop.Views.Mes;

public partial class MesReportingView
{
    // Serialized with the per-report MES06 layout.
    private sealed class Mes06ConditionalRule
    {
        public bool Enabled { get; set; } = true;
        public string TargetProperty { get; set; } = "PrintedNet";
        public string ComparisonMode { get; set; } = "Column";
        public string ComparisonProperty { get; set; } = "PlannedPerformance";
        public string ComparisonValue { get; set; } = string.Empty;
        public string Operator { get; set; } = "<";
        public string Foreground { get; set; } = "#FF0000";
        public string Background { get; set; } = string.Empty;
        public bool Bold { get; set; }
        public bool Underline { get; set; }
        public bool Italic { get; set; }

        public Mes06ConditionalRule Copy() => (Mes06ConditionalRule)MemberwiseClone();
    }

    private sealed class Mes06ConditionalChoice
    {
        public required string Code { get; init; }
        public required string Name { get; init; }

        // DataGridComboBoxColumn can fall back to ToString in its popup template.
        public override string ToString() => Name;
    }

    private sealed class Mes06RuleMatchConverter : IValueConverter
    {
        private readonly Mes06ConditionalRule _rule;
        private readonly IReadOnlyList<Mes06ConditionalRule> _preceding;

        public Mes06RuleMatchConverter(
            Mes06ConditionalRule rule,
            IReadOnlyList<Mes06ConditionalRule> preceding)
        {
            _rule = rule;
            _preceding = preceding;
        }

        public object Convert(object value, Type targetType, object parameter,
            CultureInfo culture)
        {
            return MatchMes06Rule(_rule, value)
                && !_preceding.Any(rule =>
                    string.Equals(rule.TargetProperty, _rule.TargetProperty,
                        StringComparison.OrdinalIgnoreCase)
                    && MatchMes06Rule(rule, value));
        }

        public object ConvertBack(object value, Type targetType, object parameter,
            CultureInfo culture) => Binding.DoNothing;
    }

    private IReadOnlyList<Mes06ConditionalRule> GetMes06ConditionalRules(
        MesReportDefinition definition)
    {
        var layout = GetReportLayout(definition);
        if (layout.Rules is null)
        {
            // Migration: only existing Plachta reports receive an initial rule.
            // An empty saved collection means "user intentionally deleted all".
            layout.Rules = IsPlachtaReport(definition)
                ? new List<Mes06ConditionalRule>
                {
                    new Mes06ConditionalRule()
                }
                : new List<Mes06ConditionalRule>();
        }

        return layout.Rules;
    }

    private static bool TryMes06Number(object? raw, out decimal number)
    {
        number = 0m;
        if (raw is null || raw is bool || raw is DateTime)
            return false;

        if (raw is string text)
        {
            text = string.Concat(text.Where(character =>
                !char.IsWhiteSpace(character)));
            if (text.Length == 0)
                return false;

            // Require a purely numeric plan; texts with units do not qualify.
            return decimal.TryParse(text, NumberStyles.Number,
                       CultureInfo.GetCultureInfo("cs-CZ"), out number)
                || decimal.TryParse(text, NumberStyles.Number,
                       CultureInfo.InvariantCulture, out number);
        }

        try
        {
            number = System.Convert.ToDecimal(raw, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidCastException
            or FormatException
            or OverflowException)
        {
            return false;
        }
    }

    private static bool MatchMes06Rule(Mes06ConditionalRule rule, object? row)
    {
        if (!rule.Enabled || row is null)
            return false;

        if (!TryMes06Number(ReadPrintableValue(row, rule.TargetProperty),
                out var left))
            return false;

        var comparison = rule.ComparisonMode == "Value"
            ? rule.ComparisonValue
            : ReadPrintableValue(row, rule.ComparisonProperty);

        if (!TryMes06Number(comparison, out var right))
            return false;

        return rule.Operator switch
        {
            "<" => left < right,
            "<=" => left <= right,
            ">" => left > right,
            ">=" => left >= right,
            "=" => left == right,
            "!=" => left != right,
            _ => false
        };
    }

    private static Brush? Mes06RuleBrush(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;

        try
        {
            return new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(hex.Trim())!);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private string? ValidateMes06Color(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var text = raw.Trim();
        if (text.Length == 7
            && text[0] == '#'
            && text.Skip(1).All(Uri.IsHexDigit))
            return null;

        return T("MES06.Conditional.Validation.Color",
            "Barva musí mít formát #RRGGBB, nebo zůstat prázdná.");
    }

    private static void AddMes06RuleStyleTriggers(
        Style style,
        MesReportDefinition definition,
        string property,
        IReadOnlyList<Mes06ConditionalRule> rules)
    {
        var preceding = new List<Mes06ConditionalRule>();

        foreach (var rule in rules)
        {
            if (!string.Equals(rule.TargetProperty, property,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            var converter = new Mes06RuleMatchConverter(rule, preceding.ToArray());
            var trigger = new DataTrigger
            {
                Binding = new Binding(".") { Converter = converter },
                Value = true
            };

            var foreground = Mes06RuleBrush(rule.Foreground);
            var background = Mes06RuleBrush(rule.Background);
            if (foreground is not null)
                trigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, foreground));
            if (background is not null)
                trigger.Setters.Add(new Setter(TextBlock.BackgroundProperty, background));
            if (rule.Bold)
                trigger.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.Bold));
            if (rule.Underline)
                trigger.Setters.Add(new Setter(
                    TextBlock.TextDecorationsProperty, TextDecorations.Underline));
            if (rule.Italic)
                trigger.Setters.Add(new Setter(TextBlock.FontStyleProperty, FontStyles.Italic));

            if (trigger.Setters.Count > 0)
                style.Triggers.Add(trigger);
            preceding.Add(rule);
        }
    }

    private void AppendConfiguredConditionalFormatting(
        Style style, MesReportDefinition definition, string property)
    {
        AddMes06RuleStyleTriggers(
            style, definition, property, GetMes06ConditionalRules(definition));
    }

    private void ApplyConfiguredConditionalPrintCell(
        TableCell cell, MesReportDefinition definition, object row, string property)
    {
        var matchingRule = GetMes06ConditionalRules(definition)
            .FirstOrDefault(rule =>
                string.Equals(rule.TargetProperty, property,
                    StringComparison.OrdinalIgnoreCase)
                && MatchMes06Rule(rule, row));

        if (matchingRule is null)
            return;

        var foreground = Mes06RuleBrush(matchingRule.Foreground);
        var background = Mes06RuleBrush(matchingRule.Background);
        if (foreground is not null)
            cell.Foreground = foreground;
        if (background is not null)
            cell.Background = background;

        foreach (var paragraph in cell.Blocks.OfType<Paragraph>())
        {
            if (foreground is not null)
                paragraph.Foreground = foreground;
            if (matchingRule.Bold)
                paragraph.FontWeight = FontWeights.Bold;
            if (matchingRule.Underline)
                paragraph.TextDecorations = TextDecorations.Underline;
            if (matchingRule.Italic)
                paragraph.FontStyle = FontStyles.Italic;
        }
    }

    private static DataGridComboBoxColumn Mes06RuleComboColumn(
        string heading,
        string property,
        IEnumerable<Mes06ConditionalChoice> choices,
        double width)
    {
        return new DataGridComboBoxColumn
        {
            Header = heading,
            Width = width,
            ItemsSource = choices.ToArray(),
            DisplayMemberPath = nameof(Mes06ConditionalChoice.Name),
            SelectedValuePath = nameof(Mes06ConditionalChoice.Code),
            EditingElementStyle = CreateMes06RuleEditingComboStyle(),
            SelectedValueBinding = new Binding(property)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            }
        };
    }

    private static DataGridTextColumn Mes06RuleTextColumn(
        string heading, string property, double width)
    {
        return new DataGridTextColumn
        {
            Header = heading,
            Width = width,
            EditingElementStyle = CreateMes06RuleEditingTextStyle(),
            Binding = new Binding(property)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.LostFocus
            }
        };
    }

    private static DataGridCheckBoxColumn Mes06RuleCheckColumn(
        string heading, string property)
    {
        return new DataGridCheckBoxColumn
        {
            Header = heading,
            Width = 69,
            Binding = new Binding(property) { Mode = BindingMode.TwoWay }
        };
    }

    private static Style CreateMes06RuleEditingComboStyle()
    {
        var style = new Style(typeof(ComboBox));
        style.Setters.Add(new Setter(Control.ForegroundProperty,
            new SolidColorBrush(Color.FromRgb(233, 239, 245))));
        style.Setters.Add(new Setter(Control.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(38, 49, 59))));
        style.Setters.Add(new Setter(Control.BorderBrushProperty,
            new SolidColorBrush(Color.FromRgb(88, 105, 119))));
        return style;
    }

    private static Style CreateMes06RuleEditingTextStyle()
    {
        var style = new Style(typeof(TextBox));
        style.Setters.Add(new Setter(Control.ForegroundProperty,
            new SolidColorBrush(Color.FromRgb(233, 239, 245))));
        style.Setters.Add(new Setter(Control.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(38, 49, 59))));
        style.Setters.Add(new Setter(Control.BorderBrushProperty,
            new SolidColorBrush(Color.FromRgb(88, 105, 119))));
        return style;
    }

    private static void ApplyMes06RuleGridSelectionStyle(DataGrid grid)
    {
        var background = new SolidColorBrush(Color.FromRgb(53, 70, 85));
        var foreground = new SolidColorBrush(Color.FromRgb(237, 242, 247));

        var rowStyle = new Style(typeof(DataGridRow));
        var selectedRow = new Trigger
        {
            Property = DataGridRow.IsSelectedProperty,
            Value = true
        };
        selectedRow.Setters.Add(new Setter(Control.BackgroundProperty, background));
        selectedRow.Setters.Add(new Setter(Control.ForegroundProperty, foreground));
        rowStyle.Triggers.Add(selectedRow);
        grid.RowStyle = rowStyle;

        var cellStyle = new Style(typeof(DataGridCell));
        var selectedCell = new Trigger
        {
            Property = DataGridCell.IsSelectedProperty,
            Value = true
        };
        selectedCell.Setters.Add(new Setter(Control.BackgroundProperty, background));
        selectedCell.Setters.Add(new Setter(Control.ForegroundProperty, foreground));
        cellStyle.Triggers.Add(selectedCell);
        grid.CellStyle = cellStyle;
    }

    private TabItem CreateMes06ConditionalFormattingTab(
        MesReportDefinition definition,
        out ObservableCollection<Mes06ConditionalRule> editorRules,
        out DataGrid rulesGrid)
    {
        var rules = new ObservableCollection<Mes06ConditionalRule>(
            GetMes06ConditionalRules(definition).Select(rule => rule.Copy()));

        var properties = definition.Columns
            .Where(column => !string.IsNullOrWhiteSpace(column.Property))
            .Select(column => new Mes06ConditionalChoice
            {
                Code = column.Property,
                Name = column.Header
            })
            .ToArray();

        var dataGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            SelectionMode = DataGridSelectionMode.Single,
            ItemsSource = rules,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        dataGrid.SetResourceReference(
            FrameworkElement.StyleProperty, "DmsDataGridStyle");
        ApplyMes06RuleGridSelectionStyle(dataGrid);

        dataGrid.Columns.Add(Mes06RuleCheckColumn(T("MES06.Conditional.Column.Enabled", "Zap."), nameof(Mes06ConditionalRule.Enabled)));
        dataGrid.Columns.Add(Mes06RuleComboColumn(
            T("MES06.Conditional.Column.Target", "Barvený sloupec"), nameof(Mes06ConditionalRule.TargetProperty), properties, 150));
        dataGrid.Columns.Add(Mes06RuleComboColumn(
            T("MES06.Conditional.Column.CompareMode", "Porovnat s"), nameof(Mes06ConditionalRule.ComparisonMode),
            new[]
            {
                new Mes06ConditionalChoice { Code = "Column", Name = T("MES06.Conditional.Choice.Column", "Sloupec") },
                new Mes06ConditionalChoice { Code = "Value", Name = T("MES06.Conditional.Choice.Value", "Hodnota") }
            }, 100));
        dataGrid.Columns.Add(Mes06RuleComboColumn(
            T("MES06.Conditional.Column.Reference", "Referenční sloupec"), nameof(Mes06ConditionalRule.ComparisonProperty),
            properties, 155));
        dataGrid.Columns.Add(Mes06RuleTextColumn(
            T("MES06.Conditional.Column.Value", "Hodnota"), nameof(Mes06ConditionalRule.ComparisonValue), 100));
        dataGrid.Columns.Add(Mes06RuleComboColumn(
            T("MES06.Conditional.Column.Operator", "Operátor"), nameof(Mes06ConditionalRule.Operator),
            new[] { "<", "<=", ">", ">=", "=", "!=" }.Select(code =>
                new Mes06ConditionalChoice { Code = code, Name = code }), 85));
        dataGrid.Columns.Add(Mes06RuleTextColumn(
            T("MES06.Conditional.Column.Foreground", "Barva textu"), nameof(Mes06ConditionalRule.Foreground), 105));
        dataGrid.Columns.Add(Mes06RuleTextColumn(
            T("MES06.Conditional.Column.Background", "Barva pozadí"), nameof(Mes06ConditionalRule.Background), 105));
        dataGrid.Columns.Add(Mes06RuleCheckColumn(T("MES06.Conditional.Column.Bold", "Tučně"), nameof(Mes06ConditionalRule.Bold)));
        dataGrid.Columns.Add(Mes06RuleCheckColumn(
            T("MES06.Conditional.Column.Underline", "Podtr."), nameof(Mes06ConditionalRule.Underline)));
        dataGrid.Columns.Add(Mes06RuleCheckColumn(
            T("MES06.Conditional.Column.Italic", "Kurzíva"), nameof(Mes06ConditionalRule.Italic)));

        var add = new Button
        {
            Content = T("MES06.Conditional.Button.Add", "Přidat pravidlo"), MinWidth = 125, Height = 30,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var remove = new Button
        {
            Content = T("MES06.Conditional.Button.Remove", "Odebrat vybrané"), MinWidth = 135, Height = 30
        };
        add.SetResourceReference(FrameworkElement.StyleProperty, "DmsFormButtonStyle");
        remove.SetResourceReference(FrameworkElement.StyleProperty, "DmsFormButtonStyle");
        add.Click += (_, _) =>
        {
            var created = new Mes06ConditionalRule
            {
                TargetProperty = properties.FirstOrDefault(item =>
                    item.Code == "PrintedNet")?.Code
                    ?? properties.FirstOrDefault()?.Code ?? string.Empty,
                ComparisonProperty = properties.FirstOrDefault(item =>
                    item.Code == "PlannedPerformance")?.Code
                    ?? properties.Skip(1).FirstOrDefault()?.Code
                    ?? properties.FirstOrDefault()?.Code ?? string.Empty,
                Foreground = "#FF0000"
            };
            rules.Add(created);
            dataGrid.SelectedItem = created;
            dataGrid.ScrollIntoView(created);
        };
        remove.Click += (_, _) =>
        {
            if (dataGrid.SelectedItem is Mes06ConditionalRule rule)
                rules.Remove(rule);
        };

        var moveUp = new Button
        {
            Content = T("MES06.Conditional.Button.Up", "Nahoru"), MinWidth = 80, Height = 30,
            Margin = new Thickness(8, 0, 8, 0)
        };
        var moveDown = new Button
        {
            Content = T("MES06.Conditional.Button.Down", "Dolů"), MinWidth = 80, Height = 30
        };
        moveUp.SetResourceReference(FrameworkElement.StyleProperty, "DmsFormButtonStyle");
        moveDown.SetResourceReference(FrameworkElement.StyleProperty, "DmsFormButtonStyle");
        moveUp.Click += (_, _) =>
        {
            if (dataGrid.SelectedItem is Mes06ConditionalRule item)
            {
                var index = rules.IndexOf(item);
                if (index > 0)
                {
                    rules.Move(index, index - 1);
                    dataGrid.SelectedItem = item;
                }
            }
        };
        moveDown.Click += (_, _) =>
        {
            if (dataGrid.SelectedItem is Mes06ConditionalRule item)
            {
                var index = rules.IndexOf(item);
                if (index >= 0 && index + 1 < rules.Count)
                {
                    rules.Move(index, index + 1);
                    dataGrid.SelectedItem = item;
                }
            }
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 8)
        };
        buttons.Children.Add(add);
        buttons.Children.Add(remove);
        buttons.Children.Add(moveUp);
        buttons.Children.Add(moveDown);

        var description = new TextBlock
        {
            Text = T("MES06.Conditional.Help",
                "První odpovídající pravidlo pro sloupec určuje vzhled. "
                + "Pravidla lze přesouvat tlačítky Nahoru/Dolů. "
                + "Barvy zadej jako #RRGGBB (např. #FF0000), prázdné = beze změny. "
                + "V režimu Sloupec se používá referenční sloupec; v režimu Hodnota zadané číslo. "
                + "Porovnání probíhá s plnou číselnou přesností, nikoli zaokrouhleným zobrazením."),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 8)
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(description, 0);
        Grid.SetRow(buttons, 1);
        Grid.SetRow(dataGrid, 2);
        grid.Children.Add(description);
        grid.Children.Add(buttons);
        grid.Children.Add(dataGrid);

        editorRules = rules;
        rulesGrid = dataGrid;
        return new TabItem { Header = T("MES06.Conditional.Tab.Title", "Podmíněné formátování"), Content = grid };
    }

    private bool TrySaveMes06ConditionalRules(
        MesReportDefinition definition,
        ObservableCollection<Mes06ConditionalRule> editorRules,
        DataGrid rulesGrid)
    {
        rulesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        rulesGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var properties = definition.Columns
            .Select(column => column.Property)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in editorRules)
        {
            if (!properties.Contains(rule.TargetProperty))
                return ReportMes06ConditionalValidation(
                    T("MES06.Conditional.Validation.Target", "Cílový sloupec musí existovat v definici reportu."));

            if (rule.ComparisonMode is not ("Column" or "Value"))
                return ReportMes06ConditionalValidation(
                    T("MES06.Conditional.Validation.Mode", "Vyber způsob porovnání: sloupec nebo hodnota."));

            if (rule.ComparisonMode == "Column"
                && !properties.Contains(rule.ComparisonProperty))
                return ReportMes06ConditionalValidation(
                    T("MES06.Conditional.Validation.Reference", "Referenční sloupec musí existovat v definici reportu."));

            if (rule.ComparisonMode == "Value"
                && !TryMes06Number(rule.ComparisonValue, out _))
                return ReportMes06ConditionalValidation(
                    T("MES06.Conditional.Validation.Value", "Zadaná porovnávací hodnota musí být číslo."));

            if (rule.Operator is not ("<" or "<=" or ">" or ">=" or "=" or "!="))
                return ReportMes06ConditionalValidation(
                    T("MES06.Conditional.Validation.Operator", "Nepodporovaný operátor porovnání."));

            var foregroundError = ValidateMes06Color(rule.Foreground);
            var backgroundError = ValidateMes06Color(rule.Background);
            if (foregroundError is not null || backgroundError is not null)
                return ReportMes06ConditionalValidation(
                    foregroundError ?? backgroundError!);

            if (rule.Enabled && string.IsNullOrWhiteSpace(rule.Foreground)
                && string.IsNullOrWhiteSpace(rule.Background)
                && !rule.Bold && !rule.Underline && !rule.Italic)
                return ReportMes06ConditionalValidation(
                    T("MES06.Conditional.Validation.Style", "Zapnuté pravidlo musí upravovat alespoň jednu vlastnost vzhledu."));
        }

        GetReportLayout(definition).Rules =
            editorRules.Select(rule => rule.Copy()).ToList();
        return true;
    }

    private bool ReportMes06ConditionalValidation(string message)
    {
        DmsMessage.Show(message, "MES06",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private static string Mes06ExcelNumber(object? value)
    {
        return TryMes06Number(value, out var number)
            ? number.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private void ApplyConfiguredConditionalExcel(
        IXLWorksheet worksheet,
        MesReportDefinition definition,
        IReadOnlyList<string> exportedProperties)
    {
        if (_currentRows.Count == 0)
            return;

        var properties = exportedProperties
            .Select((property, index) => (Property: property, Index: index + 1))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Property))
            .GroupBy(pair => pair.Property, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Index,
                StringComparer.OrdinalIgnoreCase);

        var precedingByTarget = new Dictionary<string, List<string>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var rule in GetMes06ConditionalRules(definition))
        {
            if (!rule.Enabled
                || !properties.TryGetValue(rule.TargetProperty, out var targetIndex))
                continue;

            string reference;
            if (rule.ComparisonMode == "Column")
            {
                if (!properties.TryGetValue(rule.ComparisonProperty, out var compareIndex))
                {
                    // The reference column is intentionally omitted by the
                    // user's display preset. Preserve its current highlighting
                    // without exposing that column in the workbook.
                    for (var rowIndex = 0; rowIndex < _currentRows.Count; rowIndex++)
                    {
                        if (!MatchMes06Rule(rule, _currentRows[rowIndex]))
                            continue;

                        var fortmattedCell = worksheet.Cell(rowIndex + 2, targetIndex);
                        if (ValidateMes06Color(rule.Foreground) is null
                            && !string.IsNullOrWhiteSpace(rule.Foreground))
                            fortmattedCell.Style.Font.FontColor = XLColor.FromHtml(rule.Foreground);
                        if (ValidateMes06Color(rule.Background) is null
                            && !string.IsNullOrWhiteSpace(rule.Background))
                            fortmattedCell.Style.Fill.BackgroundColor = XLColor.FromHtml(rule.Background);
                        if (rule.Bold)
                            fortmattedCell.Style.Font.Bold = true;
                        if (rule.Underline)
                            fortmattedCell.Style.Font.Underline = XLFontUnderlineValues.Single;
                        if (rule.Italic)
                            fortmattedCell.Style.Font.Italic = true;
                    }
                    continue;
                }

                var compareLetter = worksheet.Cell(1, compareIndex).Address.ColumnLetter;
                reference = $"IF(ISNUMBER({compareLetter}2),{compareLetter}2,VALUE({compareLetter}2))";
            }
            else if (rule.ComparisonMode == "Value")
            {
                reference = Mes06ExcelNumber(rule.ComparisonValue);
                if (string.IsNullOrEmpty(reference))
                    continue;
            }
            else
            {
                continue;
            }

            var targetLetter = worksheet.Cell(1, targetIndex).Address.ColumnLetter;
            var cell = $"{targetLetter}2";
            // Excel's not-equal operator is <> rather than C#'s !=.
            var excelOperator = rule.Operator == "!=" ? "<>" : rule.Operator;
            var baseCondition =
                $"IFERROR(AND(LEN(TRIM({cell}&\"\"))>0,VALUE({cell}){excelOperator}{reference}),FALSE)";

            if (!precedingByTarget.TryGetValue(rule.TargetProperty, out var previous))
            {
                previous = new List<string>();
                precedingByTarget[rule.TargetProperty] = previous;
            }

            var condition = previous.Count == 0
                ? $"={baseCondition}"
                : $"=AND({baseCondition},{string.Join(",", previous.Select(expr => $"NOT({expr})"))})";
            previous.Add(baseCondition);

            var format = worksheet.Range(
                    2, targetIndex, _currentRows.Count + 1, targetIndex)
                .AddConditionalFormat()
                .WhenIsTrue(condition);

            var foreground = rule.Foreground;
            var background = rule.Background;
            if (ValidateMes06Color(foreground) is not null
                || ValidateMes06Color(background) is not null)
                continue;
            if (!string.IsNullOrWhiteSpace(foreground))
                format.Font.SetFontColor(XLColor.FromHtml(foreground));
            if (!string.IsNullOrWhiteSpace(background))
                format.Fill.SetBackgroundColor(XLColor.FromHtml(background));
            if (rule.Bold)
                format.Font.SetBold();
            if (rule.Underline)
                format.Font.SetUnderline(XLFontUnderlineValues.Single);
            if (rule.Italic)
                format.Font.SetItalic();
        }
    }
}
