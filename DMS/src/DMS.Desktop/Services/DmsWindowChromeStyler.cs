using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DMS.Desktop.Services;

public static class DmsWindowChromeStyler
{
	private const int DwmwaUseImmersiveDarkMode = 20;
	private const int DwmwaBorderColor = 34;
	private const int DwmwaCaptionColor = 35;
	private const int DwmwaTextColor = 36;

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(
		IntPtr hwnd,
		int attribute,
		ref int pvAttribute,
		int cbAttribute);

	public static void ApplyFromResources(Window window)
	{
		var accent = GetBrushColor("DmsAccentBrush", Color.FromRgb(11, 42, 74));
		var onAccent = GetBrushColor("DmsOnAccentBrush", Colors.White);
		var border = GetBrushColor("DmsBorderBrush", Color.FromRgb(75, 75, 80));
		var background = GetBrushColor("DmsBackgroundBrush", Color.FromRgb(24, 24, 27));

		var isDark =
			background.R < 128 &&
			background.G < 128 &&
			background.B < 128;

		Apply(window, accent, onAccent, border, isDark);
	}

	public static void ApplyToAllOpenWindows()
	{
		foreach (Window window in Application.Current.Windows)
		{
			ApplyFromResources(window);
		}
	}

	public static void Apply(
		Window window,
		Color captionColor,
		Color textColor,
		Color borderColor,
		bool useDarkMode)
	{
		if (!window.IsLoaded)
		{
			window.SourceInitialized += (_, _) =>
				Apply(window, captionColor, textColor, borderColor, useDarkMode);

			return;
		}

		var hwnd = new WindowInteropHelper(window).Handle;

		if (hwnd == IntPtr.Zero)
		{
			return;
		}

		try
		{
			var darkValue = useDarkMode ? 1 : 0;

			DwmSetWindowAttribute(
				hwnd,
				DwmwaUseImmersiveDarkMode,
				ref darkValue,
				sizeof(int));

			var caption = ToColorRef(captionColor);
			var text = ToColorRef(textColor);
			var border = ToColorRef(borderColor);

			DwmSetWindowAttribute(
				hwnd,
				DwmwaCaptionColor,
				ref caption,
				sizeof(int));

			DwmSetWindowAttribute(
				hwnd,
				DwmwaTextColor,
				ref text,
				sizeof(int));

			DwmSetWindowAttribute(
				hwnd,
				DwmwaBorderColor,
				ref border,
				sizeof(int));
		}
		catch
		{
			// Na starších Windows se některé DWM atributy nemusí podporovat.
			// Aplikace kvůli tomu nesmí spadnout.
		}
	}

	private static int ToColorRef(Color color)
	{
		// COLORREF používá formát 0x00BBGGRR.
		return color.R | (color.G << 8) | (color.B << 16);
	}

	private static Color GetBrushColor(string resourceKey, Color fallback)
	{
		if (Application.Current.Resources[resourceKey] is SolidColorBrush brush)
		{
			return brush.Color;
		}

		return fallback;
	}
}