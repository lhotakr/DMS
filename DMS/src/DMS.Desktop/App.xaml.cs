using System.Windows;
using DMS.Desktop.Services;

namespace DMS.Desktop;

public partial class App : Application
{
    public App()
    {
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded));
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window)
        {
            DmsWindowChromeStyler.ApplyFromResources(window);
        }
    }
}