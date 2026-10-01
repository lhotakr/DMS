using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void WorkspaceScrollViewer_PreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        if (WorkspaceScrollViewer is null || e.Delta == 0)
        {
            return;
        }

        // Let an inner grid/list scroll while it still has room. Once the
        // inner viewer reaches its top/bottom edge, forward the wheel to the
        // workspace so users do not have to move the pointer to the margin.
        var inner = FindNearestInnerScrollViewer(
            e.OriginalSource as DependencyObject,
            WorkspaceScrollViewer);

        if (inner is not null && CanScrollVertically(inner, e.Delta))
        {
            return;
        }

        if (!CanScrollVertically(WorkspaceScrollViewer, e.Delta))
        {
            return;
        }

        var target = WorkspaceScrollViewer.VerticalOffset - e.Delta;
        target = Math.Max(0, Math.Min(WorkspaceScrollViewer.ScrollableHeight, target));

        WorkspaceScrollViewer.ScrollToVerticalOffset(target);
        e.Handled = true;
    }

    private static ScrollViewer? FindNearestInnerScrollViewer(
        DependencyObject? source,
        ScrollViewer outer)
    {
        var current = source;

        while (current is not null && !ReferenceEquals(current, outer))
        {
            if (current is ScrollViewer viewer)
            {
                return viewer;
            }

            current = GetParent(current);
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject current)
    {
        try
        {
            var visualParent = VisualTreeHelper.GetParent(current);
            if (visualParent is not null)
            {
                return visualParent;
            }
        }
        catch (InvalidOperationException)
        {
            // ContentElement / non-visual source; fall back to logical parent.
        }

        if (current is FrameworkContentElement contentElement)
        {
            return contentElement.Parent;
        }

        return LogicalTreeHelper.GetParent(current);
    }

    private static bool CanScrollVertically(ScrollViewer viewer, int wheelDelta)
    {
        if (viewer.ScrollableHeight <= 0)
        {
            return false;
        }

        const double epsilon = 0.5;

        return wheelDelta > 0
            ? viewer.VerticalOffset > epsilon
            : viewer.VerticalOffset < viewer.ScrollableHeight - epsilon;
    }
}
