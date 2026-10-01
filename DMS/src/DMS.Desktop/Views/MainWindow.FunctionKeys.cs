using DMS.Desktop.UI.FunctionKeys;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private static readonly Key[] FunctionKeyOrder =
    {
        Key.F1,
        Key.F2,
        Key.F3,
        Key.F4,
        Key.F5,
        Key.F6,
        Key.F7,
        Key.F8,
        Key.F9,
        Key.F12
    };

    private void InitializeFunctionKeys()
    {
        PreviewKeyDown -= MainWindow_PreviewKeyDown;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        RefreshFunctionKeyBar();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_userSettings.FunctionKeysEnabled || !IsSupportedFunctionKey(e.Key))
        {
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.None || !IsFunctionKeyEnabled(e.Key))
        {
            return;
        }

        var action = GetEffectiveFunctionKeyActions()
            .FirstOrDefault(item => item.Key == e.Key);

        if (action is null || !action.IsEnabled)
        {
            return;
        }

        e.Handled = true;
        action.Execute();
        RefreshFunctionKeyBar();
    }

    private IReadOnlyList<DmsFunctionKeyAction> GetEffectiveFunctionKeyActions()
    {
        var actions = new Dictionary<Key, DmsFunctionKeyAction>();

        // Shell defaults. Active views can override the same key below.
        actions[Key.F1] = new DmsFunctionKeyAction(
            Key.F1,
            TranslateFunctionKeyLabel("FunctionKey.Help", "Nápověda"),
            ShowContextHelpWindow,
            () => _userSettings.DocumentationHelpEnabled);

        actions[Key.F3] = new DmsFunctionKeyAction(
            Key.F3,
            TranslateFunctionKeyLabel("FunctionKey.Back", "Zpět"),
            NavigateBackByFunctionKey,
            () => _navigationBackStack.Count > 0);

        actions[Key.F5] = new DmsFunctionKeyAction(
            Key.F5,
            TranslateFunctionKeyLabel("FunctionKey.Refresh", "Obnovit"),
            RefreshCurrentTransactionByFunctionKey,
            () => !string.IsNullOrWhiteSpace(_currentTransactionCommand));

        var host = FindFunctionKeyHost(WorkspacePanel);
        if (host is not null)
        {
            foreach (var hostAction in host.GetFunctionKeyActions())
            {
                if (hostAction is not null)
                {
                    actions[hostAction.Key] = hostAction;
                }
            }
        }

        return FunctionKeyOrder
            .Where(key => actions.ContainsKey(key) && IsFunctionKeyEnabled(key))
            .Select(key => actions[key])
            .ToList();
    }

    private void RefreshFunctionKeyBar()
    {
        if (FunctionKeyBar is null || FunctionKeyPanel is null)
        {
            return;
        }

        FunctionKeyPanel.Children.Clear();

        if (!_userSettings.FunctionKeysEnabled || !_userSettings.ShowFunctionKeyBar)
        {
            FunctionKeyBar.Visibility = Visibility.Collapsed;
            return;
        }

        var actions = GetEffectiveFunctionKeyActions();

        foreach (var action in actions)
        {
            var button = new Button
            {
                Content = $"{GetFunctionKeyText(action.Key)}  {action.Label}",
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(10, 5, 10, 5),
                MinWidth = 92,
                IsEnabled = action.IsEnabled,
                Tag = action
            };

            if (TryFindResource("DmsFormButtonStyle") is Style style)
            {
                button.Style = style;
            }

            button.Click += (_, _) =>
            {
                if (button.Tag is DmsFunctionKeyAction selectedAction && selectedAction.IsEnabled)
                {
                    selectedAction.Execute();
                    RefreshFunctionKeyBar();
                }
            };

            FunctionKeyPanel.Children.Add(button);
        }

        FunctionKeyBar.Visibility = FunctionKeyPanel.Children.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static IDmsFunctionKeyHost? FindFunctionKeyHost(DependencyObject root)
    {
        if (root is IDmsFunctionKeyHost host)
        {
            return host;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var found = FindFunctionKeyHost(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private void NavigateBackByFunctionKey()
    {
        if (_navigationBackStack.Count == 0)
        {
            return;
        }

        BtnBack_Click(this, new RoutedEventArgs());
    }

    private void RefreshCurrentTransactionByFunctionKey()
    {
        if (string.IsNullOrWhiteSpace(_currentTransactionCommand))
        {
            return;
        }

        NavigateWithoutRecording(_currentTransactionCommand);
    }

    private string TranslateFunctionKeyLabel(string key, string fallback)
    {
        var translated = T(key);
        return string.IsNullOrWhiteSpace(translated) ||
               string.Equals(translated, key, StringComparison.Ordinal) ||
               translated.StartsWith("[[", StringComparison.Ordinal)
            ? fallback
            : translated;
    }

    private bool IsFunctionKeyEnabled(Key key)
    {
        return key switch
        {
            Key.F1 => _userSettings.FunctionKeyF1Enabled,
            Key.F2 => _userSettings.FunctionKeyF2Enabled,
            Key.F3 => _userSettings.FunctionKeyF3Enabled,
            Key.F4 => _userSettings.FunctionKeyF4Enabled,
            Key.F5 => _userSettings.FunctionKeyF5Enabled,
            Key.F6 => _userSettings.FunctionKeyF6Enabled,
            Key.F7 => _userSettings.FunctionKeyF7Enabled,
            Key.F8 => _userSettings.FunctionKeyF8Enabled,
            Key.F9 => _userSettings.FunctionKeyF9Enabled,
            Key.F12 => _userSettings.FunctionKeyF12Enabled,
            _ => false
        };
    }

    private static bool IsSupportedFunctionKey(Key key)
    {
        return FunctionKeyOrder.Contains(key);
    }

    private static string GetFunctionKeyText(Key key)
    {
        return key.ToString();
    }
}
