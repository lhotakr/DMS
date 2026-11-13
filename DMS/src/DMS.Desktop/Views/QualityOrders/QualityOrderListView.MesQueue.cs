using DMS.Core.Quality;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DMS.Desktop.Views.QualityOrders;

public partial class QualityOrderListView
{
    private Button? _mesQueueButton;
    private string _mesQueueRoot = string.Empty;
    private DataGridTextColumn? _mesOrderColumn;

    private void InitializeMesQaQueue(string rootPath)
    {
        _mesQueueRoot = rootPath;
        _mesOrderColumn = new DataGridTextColumn
        {
            Header = "Zakázka 2000", Binding = new Binding(nameof(QualityOrderListRow.MesOrderNumber2000)), Width = new DataGridLength(110)
        };
        GridOrders.Columns.Insert(3, _mesOrderColumn);
        if (BtnRefresh.Parent is WrapPanel panel)
        {
            _mesQueueButton = new Button { Content = "Převzít z MES", Margin = new Thickness(10, 18, 0, 8),
                Height = 32, MinWidth = 130 };
            _mesQueueButton.Click += (_, _) =>
            {
                var window = new MesQaRegistrationWindow(_mesQueueRoot, _logger, _currentUserName, _translate)
                { Owner = Window.GetWindow(this) };
                window.ShowDialog();
                ReloadData();
            };
            panel.Children.Add(_mesQueueButton);
        }
    }

    private void LocalizeMesQaQueue()
    {
        if (_mesQueueButton is not null) _mesQueueButton.Content = MesQueueText("QO.MES.Queue", "Převzít z MES");
        if (_mesOrderColumn is not null) _mesOrderColumn.Header = MesQueueText("QO.MES.Order2000", "Zakázka 2000");
    }
    private string MesQueueText(string key, string fallback)
    {
        var s = _translate?.Invoke(key);
        return string.IsNullOrWhiteSpace(s) || s == key || s == "[[" + key + "]]" ? fallback : s;
    }
}
