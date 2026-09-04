using DMS.Core.Transactions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private string GetTransactionInputText()
    {
        return CmbTransaction.Text?.Trim() ?? string.Empty;
    }

    private void SetTransactionInputText(string text)
    {
        CmbTransaction.Text = text ?? string.Empty;
    }

    private TextBox? GetTransactionEditableTextBox()
    {
        CmbTransaction.ApplyTemplate();

        return CmbTransaction.Template.FindName(
            "PART_EditableTextBox",
            CmbTransaction) as TextBox;
    }

    private void FocusTransactionInput()
    {
        CmbTransaction.Focus();

        var textBox = GetTransactionEditableTextBox();

        if (textBox is not null)
        {
            textBox.Focus();
            textBox.SelectAll();
        }
    }

    private void ClearTransactionInput()
    {
        CmbTransaction.Text = string.Empty;
        CmbTransaction.SelectedIndex = -1;

        FocusTransactionInput();
    }

    private void CmbTransaction_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;

        var transactionText = GetTransactionInputText();

        if (string.IsNullOrWhiteSpace(transactionText))
        {
            return;
        }

        ExecuteTransaction(transactionText);
    }

    private void CmbTransaction_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbTransaction.SelectedItem is not string transaction)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(transaction))
        {
            return;
        }

        SetTransactionInputText(transaction);
        CmbTransaction.IsDropDownOpen = false;

        FocusTransactionInput();
    }

    private void RefreshTransactionHistoryList()
    {
        CmbTransaction.Items.Clear();

        foreach (var transaction in _userSettings.TransactionHistory)
        {
            CmbTransaction.Items.Add(transaction);
        }
    }

    private void AddTransactionToHistory(string transactionText)
    {
        if (string.IsNullOrWhiteSpace(transactionText))
        {
            return;
        }

        transactionText = transactionText.Trim();

        _userSettings.TransactionHistory.RemoveAll(item =>
            string.Equals(item, transactionText, StringComparison.OrdinalIgnoreCase));

        _userSettings.TransactionHistory.Insert(0, transactionText);

        var maxItems = _userSettings.MaxTransactionHistoryItems;

        if (maxItems <= 0)
        {
            maxItems = 10;
        }

        while (_userSettings.TransactionHistory.Count > maxItems)
        {
            _userSettings.TransactionHistory.RemoveAt(_userSettings.TransactionHistory.Count - 1);
        }

        _settingsService.Save(_userSettings);
        RefreshTransactionHistoryList();
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (_navigationBackStack.Count == 0)
        {
            return;
        }

        var previousCommand = _navigationBackStack.Pop();

        if (!string.IsNullOrWhiteSpace(_currentTransactionCommand))
        {
            _navigationForwardStack.Push(_currentTransactionCommand);
        }

        NavigateWithoutRecording(previousCommand);
    }

    private void BtnForward_Click(object sender, RoutedEventArgs e)
    {
        if (_navigationForwardStack.Count == 0)
        {
            return;
        }

        var nextCommand = _navigationForwardStack.Pop();

        if (!string.IsNullOrWhiteSpace(_currentTransactionCommand))
        {
            _navigationBackStack.Push(_currentTransactionCommand);
        }

        NavigateWithoutRecording(nextCommand);
    }

    private void BtnRefreshTransaction_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentTransactionCommand))
        {
            return;
        }

        NavigateWithoutRecording(_currentTransactionCommand);
    }

    private void RegisterNavigation(string newCommandText)
    {
        if (string.IsNullOrWhiteSpace(newCommandText))
        {
            return;
        }

        newCommandText = newCommandText.Trim();

        if (_isNavigatingFromHistory)
        {
            _currentTransactionCommand = newCommandText;
            UpdateCurrentTransactionText(_currentTransactionCommand);
            UpdateNavigationButtons();
            return;
        }

        if (string.Equals(_currentTransactionCommand, newCommandText, StringComparison.OrdinalIgnoreCase))
        {
            UpdateCurrentTransactionText(_currentTransactionCommand);
            UpdateNavigationButtons();
            return;
        }

        if (!string.IsNullOrWhiteSpace(_currentTransactionCommand))
        {
            _navigationBackStack.Push(_currentTransactionCommand);
            _navigationForwardStack.Clear();
        }

        _currentTransactionCommand = newCommandText;

        UpdateCurrentTransactionText(_currentTransactionCommand);
        UpdateNavigationButtons();
    }

    private void NavigateWithoutRecording(string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return;
        }

        _isNavigatingFromHistory = true;

        try
        {
            SetTransactionInputText(commandText);
            ExecuteTransaction(commandText);
        }
        finally
        {
            _isNavigatingFromHistory = false;
            UpdateNavigationButtons();
        }
    }

    private void UpdateNavigationButtons()
    {
        BtnBack.IsEnabled = _navigationBackStack.Count > 0;
        BtnForward.IsEnabled = _navigationForwardStack.Count > 0;
        BtnRefreshTransaction.IsEnabled = !string.IsNullOrWhiteSpace(_currentTransactionCommand);
    }

    private void BtnAddFavorite_Click(object sender, RoutedEventArgs e)
    {
        var command = TransactionParser.Parse(GetTransactionInputText());

        if (string.IsNullOrWhiteSpace(command.Code))
        {
            RenderTransactionResult(TransactionResult.Fail(
                "",
                "Nejdřív zadej transakci, kterou chceš přepnout v oblíbených."));
            return;
        }

        ToggleFavoriteTransaction(command.Code);
    }

    private void BtnExecuteModuleTransaction_Click(object sender, RoutedEventArgs e)
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

        ExecuteTransaction(transactionCode);
    }

    private void BtnToggleFavorite_Click(object sender, RoutedEventArgs e)
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

        ToggleFavoriteTransaction(transactionCode);

        e.Handled = true;
    }

    private void BtnAddModuleTransactionToFavorites_Click(object sender, RoutedEventArgs e)
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

        AddFavoriteTransaction(transactionCode);

        e.Handled = true;
    }
}