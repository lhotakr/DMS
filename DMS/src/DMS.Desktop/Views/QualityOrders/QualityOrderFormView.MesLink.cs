using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.QualityOrders;

public partial class QualityOrderFormView
{
    private TextBlock? _mesSourceNumberLabel;

    private void InitializeMesSourceField()
    {
        if (TxtOrderNumber.Parent is StackPanel panel)
        {
            _mesSourceNumberLabel = new TextBlock
            {
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.SemiBold,
                Foreground = (System.Windows.Media.Brush)FindResource("DmsForegroundBrush")
            };
            panel.Children.Add(_mesSourceNumberLabel);
        }
    }
    private void UpdateMesSourceField()
    {
        if (_mesSourceNumberLabel is not null)
            _mesSourceNumberLabel.Text = FormMesText("QO.MES.Order2000", "Zakázka 2000") + ": " +
                (string.IsNullOrWhiteSpace(_model.MesOrderNumber2000) ? "—" : _model.MesOrderNumber2000);
    }
    private string FormMesText(string key, string fallback)
    {
        var t = _translate?.Invoke(key);
        return string.IsNullOrWhiteSpace(t) || t == key || t == "[[" + key + "]]" ? fallback : t;
    }
}
