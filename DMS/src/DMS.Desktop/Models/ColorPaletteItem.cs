using System.Windows.Media;

namespace DMS.Desktop.Models;

public sealed class ColorPaletteItem
{
    public string Name { get; init; } = string.Empty;
    public string Hex { get; init; } = "#000000";

    public Brush Brush
    {
        get
        {
            try
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(Hex));
            }
            catch
            {
                return Brushes.Black;
            }
        }
    }

    public string DisplayText => $"{Name} ({Hex})";
}