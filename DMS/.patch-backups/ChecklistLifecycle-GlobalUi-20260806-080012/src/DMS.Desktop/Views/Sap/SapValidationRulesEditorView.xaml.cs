using ClosedXML.Excel;
using DMS.Core.Sap.Validation;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Sap;

public partial class SapValidationRulesEditorView : UserControl
{
    private readonly string _rulesPath;
    private readonly Func<string, string>? _translate;
    private readonly Action<string, string>? _logAction;

    private SapValidationRuleSet _ruleSet = new();
    private ObservableCollection<SapValidationRule> _rules = new();

    private SapValidationRule? _selectedRule;
    private bool _isUpdatingUi;

    // Zachováno pro XAML designer
    public SapValidationRulesEditorView()
        : this(Path.Combine(
            @"Z:\SAP\DMS-db\DEV",
            "Config",
            "sap-validation-rules.json"))
    {
    }

    public SapValidationRulesEditorView(
        string rulesPath,
        Func<string, string>? translate = null,
        Action<string, string>? logAction = null)
    {
        InitializeComponent();

        _rulesPath = rulesPath;
        _translate = translate;
        _logAction = logAction;

        TxtPath.Text = _rulesPath;

        ApplyLocalization();
        LoadRules();
    }

    private void ApplyLocalization()
    {
        TxtValidationTitle.Text = T("SAPSET.Validation.Title");
        ColEnabled.Header = T("SAPSET.Validation.Col.Enabled");
        ColSeverity.Header = T("SAPSET.Validation.Col.Severity");
        ColScope.Header = T("SAPSET.Validation.Col.Scope");
        ColName.Header = T("SAPSET.Validation.Col.Name");
        TxtDetailTitle.Text = T("SAPSET.Validation.Detail.Title");
        LblId.Text = T("SAPSET.Validation.Detail.Id") + ":";
        LblName.Text = T("SAPSET.Validation.Detail.Name") + ":";
        LblScope.Text = T("SAPSET.Validation.Detail.Scope") + ":";
        LblSeverity.Text = T("SAPSET.Validation.Detail.Severity") + ":";
        ChkEnabled.Content = T("SAPSET.Validation.Detail.Active");
        TxtMessageLabel.Text = T("SAPSET.Validation.Detail.Message");
        TxtConditionsLabel.Text = T("SAPSET.Validation.Detail.Conditions");
        ColCondField.Header = T("SAPSET.Validation.Col.Field");
        ColCondOperator.Header = T("SAPSET.Validation.Col.Operator");
        ColCondValue.Header = T("SAPSET.Validation.Col.Value");
        TxtAvailableFieldsLabel.Text = T("SAPSET.Validation.Detail.AvailableFields");
        BtnLoad.Content = T("SAPSET.Validation.Btn.Load");
        BtnSave.Content = T("SAPSET.Validation.Btn.Save");
        BtnAdd.Content = T("SAPSET.Validation.Btn.Add");
        BtnDuplicate.Content = T("SAPSET.Validation.Btn.Duplicate");
        BtnDelete.Content = T("SAPSET.Validation.Btn.Delete");
        BtnValidate.Content = T("SAPSET.Validation.Btn.Validate");
    }

    // ── Event handlery ────────────────────────────────────────────────────────

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

        TxtStatus.Text = TF("SAPSET.Validation.Saved", _rules.Count);

        _logAction?.Invoke("SaveValidationRules", $"File={_rulesPath}; Count={_rules.Count}");
    }

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        var rule = new SapValidationRule
        {
            Id = "NEW_RULE",
            Name = T("SAPSET.Validation.NewRuleName"),
            Enabled = true,
            Scope = "BOM_ITEM",
            Severity = "Warning",
            Message = "{Plant}: " + T("SAPSET.Validation.NewRuleName") + ".",
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

        TxtStatus.Text = T("SAPSET.Validation.RuleAdded");

        _logAction?.Invoke("AddValidationRule", $"File={_rulesPath}");
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
            Name = _selectedRule.Name + " - " + T("SAPSET.Validation.CopySuffix"),
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

        TxtStatus.Text = T("SAPSET.Validation.RuleDuplicated");

        _logAction?.Invoke("DuplicateValidationRule", $"RuleId={_selectedRule.Id}; File={_rulesPath}");
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRule is null)
        {
            return;
        }

        var result = MessageBox.Show(
            TF("SAPSET.Validation.DeleteConfirm", _selectedRule.Name),
            T("SAPSET.Validation.Title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _logAction?.Invoke("DeleteValidationRule", $"RuleId={_selectedRule.Id}; File={_rulesPath}");

        _rules.Remove(_selectedRule);
        _selectedRule = null;
        ClearDetail();

        TxtStatus.Text = T("SAPSET.Validation.RuleDeleted");
    }

    private void BtnValidate_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentDetailToSelectedRule();

        var errors = ValidateRules();

        TxtStatus.Text = errors.Count == 0
            ? T("SAPSET.Validation.ValidationOk")
            : TF("SAPSET.Validation.ValidationErrors", errors.Count);

        if (errors.Count > 0)
        {
            MessageBox.Show(
                string.Join("\n", errors.Take(20)),
                T("SAPSET.Validation.Title"),
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

    // ── Interní logika ────────────────────────────────────────────────────────

    private void LoadRules()
    {
        var repository = new JsonSapValidationRuleRepository(_rulesPath);

        _ruleSet = repository.Load();
        _rules = new ObservableCollection<SapValidationRule>(_ruleSet.Rules);

        DgvRules.ItemsSource = _rules;

        TxtStatus.Text = TF("SAPSET.Validation.Loaded", _rules.Count);

        _logAction?.Invoke("LoadValidationRules", $"File={_rulesPath}; Count={_rules.Count}");

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
        // Beze změny — zachováváme business logiku 1:1
        var errors = new List<string>();

        var allowedScopes = new[]
        {
            "ARTICLE_SUMMARY", "BOM_HEADER", "BOM_ITEM",
            "ROUTING_OPERATION", "CROSS_PLANT", "DECORATION_CHECK"
        };

        var allowedSeverities = new[] { "Info", "Warning", "Error" };

        var allowedOperators = new[]
        {
            "Equals", "NotEquals", "Contains", "StartsWith", "EndsWith",
            "IsEmpty", "IsNotEmpty", "IsTrue", "IsFalse",
            "GreaterThan", "GreaterOrEqual", "LessThan", "LessOrEqual",
            "EqualsField", "NotEqualsField"
        };

        foreach (var rule in _rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id))
                errors.Add("Rule without ID.");

            if (string.IsNullOrWhiteSpace(rule.Name))
                errors.Add($"{GetRuleLabel(rule)}: missing name.");

            if (string.IsNullOrWhiteSpace(rule.Scope))
                errors.Add($"{GetRuleLabel(rule)}: missing scope.");
            else if (!allowedScopes.Contains(rule.Scope, StringComparer.OrdinalIgnoreCase))
                errors.Add($"{GetRuleLabel(rule)}: invalid scope '{rule.Scope}'. Allowed: {string.Join(", ", allowedScopes)}.");

            if (string.IsNullOrWhiteSpace(rule.Severity))
                errors.Add($"{GetRuleLabel(rule)}: missing severity.");
            else if (!allowedSeverities.Contains(rule.Severity, StringComparer.OrdinalIgnoreCase))
                errors.Add($"{GetRuleLabel(rule)}: invalid severity '{rule.Severity}'. Allowed: {string.Join(", ", allowedSeverities)}.");

            if (string.IsNullOrWhiteSpace(rule.Message))
                errors.Add($"{GetRuleLabel(rule)}: missing message.");

            if (rule.Conditions is null || rule.Conditions.Count == 0)
            {
                errors.Add($"{GetRuleLabel(rule)}: no conditions defined.");
                continue;
            }

            for (var i = 0; i < rule.Conditions.Count; i++)
            {
                var cond = rule.Conditions[i];
                var label = $"{GetRuleLabel(rule)}, condition {i + 1}";

                if (string.IsNullOrWhiteSpace(cond.Field))
                    errors.Add($"{label}: missing field.");

                if (string.IsNullOrWhiteSpace(cond.Operator))
                    errors.Add($"{label}: missing operator.");
                else if (!allowedOperators.Contains(cond.Operator, StringComparer.OrdinalIgnoreCase))
                    errors.Add($"{label}: invalid operator '{cond.Operator}'.");

                if (OperatorRequiresValue(cond.Operator) && string.IsNullOrWhiteSpace(cond.Value))
                    errors.Add($"{label}: operator '{cond.Operator}' requires a value.");

                if (!OperatorRequiresValue(cond.Operator) && !string.IsNullOrWhiteSpace(cond.Value))
                    errors.Add($"{label}: operator '{cond.Operator}' does not use a value.");
            }
        }

        var duplicateIds = _rules
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        foreach (var id in duplicateIds)
            errors.Add($"Duplicate rule ID: {id}");

        return errors;
    }

    private static string GetRuleLabel(SapValidationRule rule)
    {
        if (!string.IsNullOrWhiteSpace(rule.Id)) return rule.Id;
        if (!string.IsNullOrWhiteSpace(rule.Name)) return rule.Name;
        return "Unknown rule";
    }

    private static bool OperatorRequiresValue(string? op)
    {
        if (string.IsNullOrWhiteSpace(op)) return false;
        return !op.Equals("IsEmpty", StringComparison.OrdinalIgnoreCase)
            && !op.Equals("IsNotEmpty", StringComparison.OrdinalIgnoreCase)
            && !op.Equals("IsTrue", StringComparison.OrdinalIgnoreCase)
            && !op.Equals("IsFalse", StringComparison.OrdinalIgnoreCase);
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
        TxtAvailableFields.Text = GetAvailableFieldsText(TxtScope.Text.Trim());
    }

    private static string GetAvailableFieldsText(string scope) => scope switch
    {
        "ARTICLE_SUMMARY" => "ArticleNumber\nMaterialFound\nBom9200Count\nBom2000Count\nRouting9200Count\nRouting2000Count\nBomAlternative9200",
        "CROSS_PLANT" => "ArticleNumber\nLastZpp2Scrap2000\nBomNumber9200\nPosition9200\nComponentNumber9200\nComponentScrap9200\nIsSortingAlternative9200",
        "DECORATION_CHECK" => "ArticleNumber\nBomNumber9200\nPosition9200\nArticleDecorationCode\nComponentDecorationCode\nDecorationDifference",
        "BOM_HEADER" => "Plant\nBomNumber\nAlternative\nBomUsage\nBaseQuantity\nBaseUnit",
        "BOM_ITEM" => "ArticleNumber\nPlant\nBomNumber\nAlternative\nPosition\nItemCategory\nComponentNumber\nComponentDescription\nQuantity\nUnit\nScrapPercent\nIsFixedQuantity\nIsTextItem\nIsSelfComponent\nIsSortingAlternative",
        "ROUTING_OPERATION" => "Plant\nGroupNumber\nAlternative\nOperationNumber\nWorkCenter\nWorkCenterText\nControlKey\nDescription\nBaseQuantity\nBaseUnit\nVgw01\nVgw03\nVgw04\nScrapPercent\nInfoRecord\nIsFirstOperation\nIsLastOperation",
        _ => ""
    };

    private string T(string key)
    {
        var value = _translate?.Invoke(key) ?? key;
        return IsMissing(value, key) ? key : value;
    }

    private string TF(string key, params object[] args)
    {
        var pattern = T(key);
        try { return string.Format(pattern, args); }
        catch { return pattern; }
    }

    private static bool IsMissing(string? value, string key)
        => string.IsNullOrWhiteSpace(value)
           || string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);
}