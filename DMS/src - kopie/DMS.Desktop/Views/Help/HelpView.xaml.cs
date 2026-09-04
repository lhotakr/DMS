using DMS.Core.Transactions;
using DMS.Desktop.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Help;

public partial class HelpView : UserControl
{
    private readonly IReadOnlyList<TransactionDefinition> _definitions;
    private readonly Func<string, string> _translate;
    private readonly Func<string, object[], string> _translateFormat;
    private readonly Action<string> _executeTransaction;
    private readonly Action<string, string>? _logHelpAction;

    private List<HelpTransactionRow> _allRows = new();

    public HelpView(
        IEnumerable<TransactionDefinition> definitions,
        Func<string, string> translate,
        Func<string, object[], string> translateFormat,
        Action<string> executeTransaction,
        Action<string, string>? logHelpAction = null)
    {
        InitializeComponent();

        _definitions = definitions
            .OrderBy(item => item.Module)
            .ThenBy(item => item.Code)
            .ToList();

        _translate = translate ?? throw new ArgumentNullException(nameof(translate));
        _translateFormat = translateFormat ?? throw new ArgumentNullException(nameof(translateFormat));
        _executeTransaction = executeTransaction ?? throw new ArgumentNullException(nameof(executeTransaction));
        _logHelpAction = logHelpAction;

        BuildRows();
        ApplyLocalization();
        LoadModules();
        ApplyFilter();
    }

    private string T(string key, string fallback)
    {
        var value = _translate(key);

        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }

        return value;
    }

    private string T(string key, string fallback, params object[] args)
    {
        var value = _translateFormat(key, args);

        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase))
        {
            return string.Format(fallback, args);
        }

        return value;
    }

    private void BuildRows()
    {
        _allRows = _definitions
            .Select(definition => new HelpTransactionRow
            {
                Code = definition.Code,
                Name = DmsTransactionText.Name(definition, _translate),
                Module = DmsTransactionText.Module(definition, _translate),
                OriginalModule = definition.Module,
                Description = DmsTransactionText.Description(definition, _translate),
                ParameterDisplay = definition.RequiresArticleNumber
                    ? T("HELP.RequiresArticleNumber", "SAP article number")
                    : T("HELP.NoParameter", "No parameter"),
                RolesDisplay = definition.Roles.Count == 0
                    ? T("HELP.AvailableToAll", "All users")
                    : string.Join(", ", definition.Roles),
                RunText = T("HELP.Run", "Run")
            })
            .ToList();
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("HELP.Title", "HELP - Transaction help");
        TxtSubtitle.Text = T("HELP.Subtitle", "List of available DMS transactions for the current user.");

        TxtSearchLabel.Text = T("HELP.Search", "Search:");
        TxtModuleLabel.Text = T("HELP.Module", "Module:");
        BtnClear.Content = T("HELP.Clear", "Clear");

        ColCode.Header = T("HELP.Column.Code", "Code");
        ColName.Header = T("HELP.Column.Name", "Name");
        ColModule.Header = T("HELP.Column.Module", "Module");
        ColDescription.Header = T("HELP.Column.Description", "Description");
        ColParameter.Header = T("HELP.Column.Parameter", "Parameter");
        ColRoles.Header = T("HELP.Column.Roles", "Roles");
        ColAction.Header = T("HELP.Column.Action", "Action");

        TxtHint.Text = T(
            "HELP.Hint",
            "Tip: transactions requiring an article number can be started without a parameter; DMS will ask for it.");

        foreach (var row in _allRows)
        {
            row.RunText = T("HELP.Run", "Run");
            row.ParameterDisplay = _definitions.Any(d => string.Equals(d.Code, row.Code, StringComparison.OrdinalIgnoreCase) && d.RequiresArticleNumber)
                ? T("HELP.RequiresArticleNumber", "SAP article number")
                : T("HELP.NoParameter", "No parameter");
        }
    }

    private void LoadModules()
    {
        CmbModule.Items.Clear();

        CmbModule.Items.Add(new ComboBoxItem
        {
            Content = T("HELP.AllModules", "All modules"),
            Tag = string.Empty
        });

        var modules = _allRows
            .Select(item => item.OriginalModule)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item)
            .ToList();

        foreach (var module in modules)
        {
            CmbModule.Items.Add(new ComboBoxItem
            {
                Content = TranslateModuleName(module),
                Tag = module
            });
        }

        CmbModule.SelectedIndex = 0;
    }

    private string TranslateModuleName(string moduleName)
    {
        return DmsTransactionText.Module(moduleName, _translate);
    }

    private void ApplyFilter()
    {
        var search = TxtSearch.Text.Trim();
        var selectedModule = (CmbModule.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;

        var rows = _allRows.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(selectedModule))
        {
            rows = rows.Where(item =>
                string.Equals(item.OriginalModule, selectedModule, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            rows = rows.Where(item =>
                Contains(item.Code, search) ||
                Contains(item.Name, search) ||
                Contains(item.Module, search) ||
                Contains(item.Description, search) ||
                Contains(item.ParameterDisplay, search) ||
                Contains(item.RolesDisplay, search));
        }

        var list = rows
            .OrderBy(item => item.Module)
            .ThenBy(item => item.Code)
            .ToList();

        GridTransactions.ItemsSource = list;

        TxtVisibleCount.Text = T("HELP.VisibleCount", "Visible transactions: {0}", list.Count);
    }

    private static bool Contains(string? value, string filter)
    {
        return value?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void CmbModule_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        TxtSearch.Text = string.Empty;

        if (CmbModule.Items.Count > 0)
        {
            CmbModule.SelectedIndex = 0;
        }

        ApplyFilter();

        _logHelpAction?.Invoke(
            "ClearHelpFilter",
            $"VisibleTransactions={GridTransactions.Items.Count}");
    }

    private void BtnRun_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var transactionCode = button.Tag?.ToString();

        if (string.IsNullOrWhiteSpace(transactionCode))
        {
            return;
        }

        _logHelpAction?.Invoke(
            "RunTransactionFromHelp",
            $"TransactionCode={transactionCode}");

        _executeTransaction(transactionCode);
    }

    private sealed class HelpTransactionRow
    {
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Module { get; init; } = string.Empty;
        public string OriginalModule { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string ParameterDisplay { get; set; } = string.Empty;
        public string RolesDisplay { get; init; } = string.Empty;
        public string RunText { get; set; } = string.Empty;
    }
}
