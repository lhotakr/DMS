using DMS.Core.Sap.Validation;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Sap;

public partial class SapValidationRulesEditorView : UserControl
{
    private readonly string _rulesPath;

    private SapValidationRuleSet _ruleSet = new();
    private ObservableCollection<SapValidationRule> _rules = new();

    private SapValidationRule? _selectedRule;
    private bool _isUpdatingUi;

    public SapValidationRulesEditorView()
    {
        InitializeComponent();

        _rulesPath = Path.Combine(
            @"Z:\SAP\DMS-db\DEV",
            "Config",
            "sap-validation-rules.json");

        TxtPath.Text = _rulesPath;

        LoadRules();
    }

    private void BtnLoad_Click(object sender, RoutedEventArgs e)
    {
        LoadRules();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentDetailToSelectedRule();

        _ruleSet.Rules = _rules.ToList();

        var repository = new JsonSapValidationRuleRepository(_rulesPath);
        repository.Save(_ruleSet);

        TxtStatus.Text = $"Uloženo: {_rules.Count} pravidel.";
    }

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        var rule = new SapValidationRule
        {
            Id = "NEW_RULE",
            Name = "Nové pravidlo",
            Enabled = true,
            Scope = "BOM_ITEM",
            Severity = "Warning",
            Message = "{Plant}: nové pravidlo.",
            Conditions =
            {
                new SapValidationCondition
                {
                    Field = "Plant",
                    Operator = "Equals",
                    Value = "2000"
                }
            }
        };

        _rules.Add(rule);
        DgvRules.SelectedItem = rule;
        TxtStatus.Text = "Přidáno nové pravidlo.";
    }

    private void BtnDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRule is null)
        {
            return;
        }

        SaveCurrentDetailToSelectedRule();

        var copy = new SapValidationRule
        {
            Id = _selectedRule.Id + "_COPY",
            Name = _selectedRule.Name + " - kopie",
            Enabled = _selectedRule.Enabled,
            Scope = _selectedRule.Scope,
            Severity = _selectedRule.Severity,
            Message = _selectedRule.Message,
            Note = _selectedRule.Note,
            Conditions = _selectedRule.Conditions
                .Select(item => new SapValidationCondition
                {
                    Field = item.Field,
                    Operator = item.Operator,
                    Value = item.Value
                })
                .ToList()
        };

        _rules.Add(copy);
        DgvRules.SelectedItem = copy;
        TxtStatus.Text = "Pravidlo duplikováno.";
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRule is null)
        {
            return;
        }

        var result = MessageBox.Show(
            $"Opravdu smazat pravidlo '{_selectedRule.Name}'?",
            "Pravidla validací",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _rules.Remove(_selectedRule);
        _selectedRule = null;
        ClearDetail();
        TxtStatus.Text = "Pravidlo smazáno.";
    }

    private void BtnValidate_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentDetailToSelectedRule();

        var errors = ValidateRules();

        TxtStatus.Text = errors.Count == 0
            ? "Validace pravidel OK."
            : $"Validace našla {errors.Count} problémů.";

        if (errors.Count > 0)
        {
            MessageBox.Show(
                string.Join("\n", errors.Take(20)),
                "Validace pravidel",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void DgvRules_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi)
        {
            return;
        }

        SaveCurrentDetailToSelectedRule();

        _selectedRule = DgvRules.SelectedItem as SapValidationRule;
        LoadRuleToDetail(_selectedRule);
    }

    private void DetailChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi)
        {
            return;
        }

        SaveCurrentDetailToSelectedRule();
        DgvRules.Items.Refresh();
        UpdateAvailableFields();
    }

    private void DgvConditions_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            SaveCurrentDetailToSelectedRule();
            DgvRules.Items.Refresh();
        }));
    }

    private void LoadRules()
    {
        var repository = new JsonSapValidationRuleRepository(_rulesPath);

        _ruleSet = repository.Load();
        _rules = new ObservableCollection<SapValidationRule>(_ruleSet.Rules);

        DgvRules.ItemsSource = _rules;

        TxtStatus.Text = $"Načteno: {_rules.Count} pravidel.";

        if (_rules.Count > 0)
        {
            DgvRules.SelectedIndex = 0;
        }
        else
        {
            ClearDetail();
        }
    }

    private void LoadRuleToDetail(SapValidationRule? rule)
    {
        _isUpdatingUi = true;

        try
        {
            if (rule is null)
            {
                ClearDetail();
                return;
            }

            TxtId.Text = rule.Id;
            TxtName.Text = rule.Name;
            TxtMessage.Text = rule.Message;
            ChkEnabled.IsChecked = rule.Enabled;

            TxtScope.Text = rule.Scope;
            TxtSeverity.Text = rule.Severity;

            DgvConditions.ItemsSource = rule.Conditions;

            UpdateAvailableFields();
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private void SaveCurrentDetailToSelectedRule()
    {
        if (_isUpdatingUi || _selectedRule is null)
        {
            return;
        }

        _selectedRule.Id = TxtId.Text.Trim();
        _selectedRule.Name = TxtName.Text.Trim();
        _selectedRule.Message = TxtMessage.Text;
        _selectedRule.Enabled = ChkEnabled.IsChecked == true;
        _selectedRule.Scope = TxtScope.Text.Trim();
        _selectedRule.Severity = TxtSeverity.Text.Trim();

        if (DgvConditions.ItemsSource is IEnumerable<SapValidationCondition> conditions)
        {
            _selectedRule.Conditions = conditions
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.Field) ||
                    !string.IsNullOrWhiteSpace(item.Operator) ||
                    !string.IsNullOrWhiteSpace(item.Value))
                .ToList();
        }
    }

    private List<string> ValidateRules()
    {
        var errors = new List<string>();

        var allowedScopes = new[]
        {
            "ARTICLE_SUMMARY",
            "BOM_HEADER",
            "BOM_ITEM",
            "ROUTING_OPERATION",
            "CROSS_PLANT",
            "DECORATION_CHECK"
        };

        var allowedSeverities = new[]
        {
        "Info",
        "Warning",
        "Error"
        };

        var allowedOperators = new[]
        {
        "Equals",
        "NotEquals",
        "Contains",
        "StartsWith",
        "EndsWith",
        "IsEmpty",
        "IsNotEmpty",
        "IsTrue",
        "IsFalse",
        "GreaterThan",
        "GreaterOrEqual",
        "LessThan",
        "LessOrEqual",
        "EqualsField",
        "NotEqualsField"
        };

        foreach (var rule in _rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id))
            {
                errors.Add("Pravidlo bez ID.");
            }

            if (string.IsNullOrWhiteSpace(rule.Name))
            {
                errors.Add($"{GetRuleLabel(rule)}: chybí název.");
            }

            if (string.IsNullOrWhiteSpace(rule.Scope))
            {
                errors.Add($"{GetRuleLabel(rule)}: chybí scope.");
            }
            else if (!allowedScopes.Contains(rule.Scope, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"{GetRuleLabel(rule)}: neplatný scope '{rule.Scope}'. " +
                    $"Povolené hodnoty: {string.Join(", ", allowedScopes)}.");
            }

            if (string.IsNullOrWhiteSpace(rule.Severity))
            {
                errors.Add($"{GetRuleLabel(rule)}: chybí typ/severity.");
            }
            else if (!allowedSeverities.Contains(rule.Severity, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"{GetRuleLabel(rule)}: neplatný typ/severity '{rule.Severity}'. " +
                    $"Povolené hodnoty: {string.Join(", ", allowedSeverities)}.");
            }

            if (string.IsNullOrWhiteSpace(rule.Message))
            {
                errors.Add($"{GetRuleLabel(rule)}: chybí zpráva.");
            }

            if (rule.Conditions is null || rule.Conditions.Count == 0)
            {
                errors.Add($"{GetRuleLabel(rule)}: nemá žádnou podmínku.");
                continue;
            }

            for (var index = 0; index < rule.Conditions.Count; index++)
            {
                var condition = rule.Conditions[index];
                var conditionLabel = $"{GetRuleLabel(rule)}, podmínka {index + 1}";

                if (string.IsNullOrWhiteSpace(condition.Field))
                {
                    errors.Add($"{conditionLabel}: chybí pole.");
                }

                if (string.IsNullOrWhiteSpace(condition.Operator))
                {
                    errors.Add($"{conditionLabel}: chybí operátor.");
                }
                else if (!allowedOperators.Contains(condition.Operator, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add(
                        $"{conditionLabel}: neplatný operátor '{condition.Operator}'. " +
                        $"Povolené hodnoty: {string.Join(", ", allowedOperators)}.");
                }

                if (OperatorRequiresValue(condition.Operator) &&
                    string.IsNullOrWhiteSpace(condition.Value))
                {
                    errors.Add($"{conditionLabel}: operátor '{condition.Operator}' vyžaduje hodnotu.");
                }

                if (!OperatorRequiresValue(condition.Operator) &&
                    !string.IsNullOrWhiteSpace(condition.Value))
                {
                    errors.Add($"{conditionLabel}: operátor '{condition.Operator}' hodnotu nepoužívá, ale hodnota je vyplněná.");
                }
            }
        }

        var duplicateIds = _rules
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        foreach (var id in duplicateIds)
        {
            errors.Add($"Duplicitní ID pravidla: {id}");
        }

        return errors;
    }

    private static string GetRuleLabel(SapValidationRule rule)
    {
        if (!string.IsNullOrWhiteSpace(rule.Id))
        {
            return rule.Id;
        }

        if (!string.IsNullOrWhiteSpace(rule.Name))
        {
            return rule.Name;
        }

        return "Neznámé pravidlo";
    }

    private static bool OperatorRequiresValue(string? operatorName)
    {
        if (string.IsNullOrWhiteSpace(operatorName))
        {
            return false;
        }

        return !operatorName.Equals("IsEmpty", StringComparison.OrdinalIgnoreCase)
               && !operatorName.Equals("IsNotEmpty", StringComparison.OrdinalIgnoreCase)
               && !operatorName.Equals("IsTrue", StringComparison.OrdinalIgnoreCase)
               && !operatorName.Equals("IsFalse", StringComparison.OrdinalIgnoreCase);
    }

    private void ClearDetail()
    {
        _isUpdatingUi = true;

        try
        {
            TxtId.Text = string.Empty;
            TxtName.Text = string.Empty;
            TxtMessage.Text = string.Empty;
            ChkEnabled.IsChecked = false;
            TxtScope.Text = string.Empty;
            TxtSeverity.Text = string.Empty;
            DgvConditions.ItemsSource = null;
            TxtAvailableFields.Text = string.Empty;
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private void UpdateAvailableFields()
    {
        var scope = TxtScope.Text.Trim();
        TxtAvailableFields.Text = GetAvailableFieldsText(scope);
    }

    private static string GetAvailableFieldsText(string scope)
    {
        return scope switch
        {
            "ARTICLE_SUMMARY" =>
                "ArticleNumber\nMaterialFound\nBom9200Count\nBom2000Count\nRouting9200Count\nRouting2000Count\nBomAlternative9200",

            "CROSS_PLANT" =>
                "ArticleNumber\nLastZpp2Scrap2000\nBomNumber9200\nPosition9200\nComponentNumber9200\nComponentScrap9200\nIsSortingAlternative9200",

            "DECORATION_CHECK" =>
                "ArticleNumber\nBomNumber9200\nPosition9200\nArticleDecorationCode\nComponentDecorationCode\nDecorationDifference",

            "BOM_HEADER" =>
                "Plant\nBomNumber\nAlternative\nBomUsage\nBaseQuantity\nBaseUnit",

            "BOM_ITEM" =>
                "ArticleNumber\nPlant\nBomNumber\nAlternative\nPosition\nItemCategory\nComponentNumber\nComponentDescription\nQuantity\nUnit\nScrapPercent\nIsFixedQuantity\nIsTextItem\nIsSelfComponent\nIsSortingAlternative",

            "ROUTING_OPERATION" =>
                "Plant\nGroupNumber\nAlternative\nOperationNumber\nWorkCenter\nWorkCenterText\nControlKey\nDescription\nBaseQuantity\nBaseUnit\nVgw01\nVgw03\nVgw04\nScrapPercent\nInfoRecord\nIsFirstOperation\nIsLastOperation",


            _ => ""
        };
    }

}