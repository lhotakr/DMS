using DMS.Core.Sap;
using DMS.Desktop.Logging;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WpfPath = System.Windows.Shapes.Path;

namespace DMS.Desktop.Views.Technology;

public partial class Tec03ArticleTreeView : UserControl
{
    private const double NodeWidth = 250d;
    private const double NodeHeight = 96d;
    private const double ColumnGap = 44d;
    private const double LevelGap = 120d;
    private const double HorizontalMargin = 120d;
    private const double VerticalMargin = 70d;

    private readonly string _articleNumber;
    private readonly SapStoragePaths _storagePaths;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;

    private readonly ScaleTransform _scaleTransform = new(1d, 1d);
    private readonly TranslateTransform _translateTransform = new(0d, 0d);
    private readonly Dictionary<string, Point> _nodePositions = new(StringComparer.OrdinalIgnoreCase);

    private SapArticleTreeGraph? _graph;
    private bool _isLoaded;
    private bool _isRefreshing;
    private bool _isPanning;
    private Point _panStart;
    private double _panStartX;
    private double _panStartY;
    private int _refreshVersion;

    public event Action<string>? TransactionRequested;

    public Tec03ArticleTreeView(
        string articleNumber,
        SapStoragePaths storagePaths,
        DmsLogger? logger = null,
        string? currentUserName = null,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null)
    {
        InitializeComponent();

        _articleNumber = (articleNumber ?? string.Empty).Trim();
        _storagePaths = storagePaths ?? throw new ArgumentNullException(nameof(storagePaths));
        _logger = logger;
        _currentUserName = string.IsNullOrWhiteSpace(currentUserName) ? "UNKNOWN" : currentUserName;
        _translate = translate;
        _translateFormat = translateFormat;

        var transforms = new TransformGroup();
        transforms.Children.Add(_scaleTransform);
        transforms.Children.Add(_translateTransform);
        GraphCanvas.RenderTransform = transforms;
        GraphCanvas.RenderTransformOrigin = new Point(0d, 0d);

        InitializeDepthOptions();
        ApplyLocalization();

        Loaded += Tec03ArticleTreeView_Loaded;
    }

    private async void Tec03ArticleTreeView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        await RefreshGraphAsync();
    }

    private void InitializeDepthOptions()
    {
        CmbSuccessorDepth.Items.Clear();
        CmbSuccessorDepth.Items.Add(new DepthOption(1, T("TEC03.Tree.Depth1", "1 úroveň")));
        CmbSuccessorDepth.Items.Add(new DepthOption(2, T("TEC03.Tree.Depth2", "2 úrovně")));
        CmbSuccessorDepth.Items.Add(new DepthOption(20, T("TEC03.Tree.DepthAll", "Vše")));
        CmbSuccessorDepth.DisplayMemberPath = nameof(DepthOption.Text);
        CmbSuccessorDepth.SelectedIndex = 1;
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("TEC03.Tree.Title", "Strom artiklu");
        TxtSubtitle.Text = T(
            "TEC03.Tree.Subtitle",
            "Tok flakonů podle skutečných kusovníkových vazeb závodu 9200. R / RB / RBD / RBDE… je pouze popis stupně; topologii určuje BOM.");
        LblSuccessorDepth.Text = T("TEC03.Tree.SuccessorDepth", "Následovníci:");
        ChkSiblings.Content = T("TEC03.Tree.IncludeSiblings", "Sousední artikly");
        BtnReload.Content = T("TEC03.Tree.Reload", "Obnovit");
        BtnCenter.Content = T("TEC03.Tree.Center", "Centrovat");
        TxtHint.Text = T(
            "TEC03.Tree.Hint",
            "Kolečko = přiblížení, tažení na prázdné ploše = posun, dvojklik na uzel = otevřít artikl v TEC03.");
        TxtLegend.Text = T(
            "TEC03.Tree.Legend",
            "Barvy: modrá = předchůdce / surovina, akcent = aktuální artikl, hnědá = soused na stejné úrovni, okrová = následovník, zelená = konečný známý výrobek.");
    }

    private async Task RefreshGraphAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        var version = ++_refreshVersion;

        try
        {
            SetControlsEnabled(false);
            TxtStatus.Text = T("TEC03.Tree.Loading", "Načítám strom artiklu…");
            TxtSelection.Text = string.Empty;
            TxtWarning.Visibility = Visibility.Collapsed;
            TxtWarning.Text = string.Empty;

            var depth = (CmbSuccessorDepth.SelectedItem as DepthOption)?.Depth ?? 2;
            var includeSiblings = ChkSiblings.IsChecked == true;

            var graph = await Task.Run(() =>
            {
                var service = new SapArticleTreeService(_storagePaths);
                return service.BuildGraph(
                    _articleNumber,
                    depth,
                    includeSiblings,
                    SapArticleTreeService.DefaultPlant);
            });

            if (version != _refreshVersion)
            {
                return;
            }

            _graph = graph;
            RenderGraph(graph);

            var predecessors = graph.Nodes.Count(node => node.IsPredecessor);
            var siblings = graph.Nodes.Count(node => node.IsSibling);
            var successors = graph.Nodes.Count(node => node.IsSuccessor);

            TxtStatus.Text = TF(
                "TEC03.Tree.Status",
                "Uzelů: {0} | Vazeb: {1} | Předchůdců: {2} | Sousedů: {3} | Následovníků: {4}",
                graph.Nodes.Count,
                graph.Edges.Count,
                predecessors,
                siblings,
                successors);

            if (graph.Edges.Count == 0)
            {
                TxtWarning.Text = T(
                    "TEC03.Tree.Empty",
                    "Pro tento artikl nebyly v kusovnících závodu 9200 nalezeny další vazby.");
                TxtWarning.Visibility = Visibility.Visible;
            }
            else if (graph.Warnings.Count > 0)
            {
                TxtWarning.Text = string.Join("  |  ", graph.Warnings);
                TxtWarning.Visibility = Visibility.Visible;
            }

            _logger?.Info(
                $"TX_OK TEC03_ARTICLE_TREE; Article={_articleNumber}; Plant=9200; " +
                $"Nodes={graph.Nodes.Count}; Edges={graph.Edges.Count}; " +
                $"SuccessorDepth={depth}; IncludeSiblings={includeSiblings}; User={_currentUserName}");

            Dispatcher.BeginInvoke(
                CenterCurrentNode,
                DispatcherPriority.Loaded);
        }
        catch (Exception ex)
        {
            GraphCanvas.Children.Clear();
            _nodePositions.Clear();
            TxtStatus.Text = T("TEC03.Tree.Failed", "Strom artiklu se nepodařilo načíst.");
            TxtWarning.Text = ex.Message;
            TxtWarning.Visibility = Visibility.Visible;

            _logger?.Info(
                $"TX_FAIL TEC03_ARTICLE_TREE; Article={_articleNumber}; User={_currentUserName}; Error={ex.Message}");
        }
        finally
        {
            _isRefreshing = false;
            SetControlsEnabled(true);
        }
    }

    private void SetControlsEnabled(bool enabled)
    {
        CmbSuccessorDepth.IsEnabled = enabled;
        ChkSiblings.IsEnabled = enabled;
        BtnReload.IsEnabled = enabled;
        BtnCenter.IsEnabled = enabled;
    }

    private void RenderGraph(SapArticleTreeGraph graph)
    {
        GraphCanvas.Children.Clear();
        _nodePositions.Clear();

        if (graph.Nodes.Count == 0)
        {
            return;
        }

        var groups = graph.Nodes
            .GroupBy(node => node.Level)
            .OrderBy(group => group.Key)
            .ToList();

        var minLevel = groups.Min(group => group.Key);
        var maxLevel = groups.Max(group => group.Key);
        var maxColumns = groups.Max(group => group.Count());
        var totalLevels = maxLevel - minLevel + 1;

        var canvasWidth = Math.Max(
            1500d,
            HorizontalMargin * 2d + maxColumns * NodeWidth + Math.Max(0, maxColumns - 1) * ColumnGap);
        var canvasHeight = Math.Max(
            760d,
            VerticalMargin * 2d + totalLevels * NodeHeight + Math.Max(0, totalLevels - 1) * LevelGap);
        var centerX = canvasWidth / 2d;

        GraphCanvas.Width = canvasWidth;
        GraphCanvas.Height = canvasHeight;

        foreach (var group in groups)
        {
            var ordered = OrderLevel(group.Key, group.ToList());
            var rowWidth = ordered.Count * NodeWidth + Math.Max(0, ordered.Count - 1) * ColumnGap;
            var startX = centerX - rowWidth / 2d;
            var y = VerticalMargin + (group.Key - minLevel) * (NodeHeight + LevelGap);

            for (var index = 0; index < ordered.Count; index++)
            {
                var node = ordered[index];
                var x = startX + index * (NodeWidth + ColumnGap);
                _nodePositions[node.MaterialNumber] = new Point(x, y);
            }
        }

        foreach (var edge in graph.Edges)
        {
            DrawEdge(edge);
        }

        foreach (var node in graph.Nodes)
        {
            DrawNode(node);
        }
    }

    private static List<SapArticleTreeNode> OrderLevel(
        int level,
        IReadOnlyList<SapArticleTreeNode> nodes)
    {
        var sorted = nodes
            .Where(node => !node.IsCurrent)
            .OrderBy(node => node.IsSibling ? 0 : 1)
            .ThenBy(node => node.StageCode.Length)
            .ThenBy(node => node.StageCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => node.MaterialNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var current = nodes.FirstOrDefault(node => node.IsCurrent);
        if (current is not null)
        {
            sorted.Insert(sorted.Count / 2, current);
        }

        return sorted;
    }

    private void DrawEdge(SapArticleTreeEdge edge)
    {
        if (!_nodePositions.TryGetValue(edge.FromMaterialNumber, out var from) ||
            !_nodePositions.TryGetValue(edge.ToMaterialNumber, out var to))
        {
            return;
        }

        var deltaY = to.Y - from.Y;
        if (Math.Abs(deltaY) >= 8d)
        {
            DrawVerticalEdge(edge, from, to);
            return;
        }

        DrawHorizontalEdge(edge, from, to);
    }

    private void DrawVerticalEdge(SapArticleTreeEdge edge, Point from, Point to)
    {
        var start = new Point(from.X + NodeWidth / 2d, from.Y + NodeHeight);
        var end = new Point(to.X + NodeWidth / 2d, to.Y);
        var controlOffset = Math.Max(50d, Math.Abs(end.Y - start.Y) * 0.45d);

        var figure = new PathFigure
        {
            StartPoint = start,
            IsClosed = false,
            IsFilled = false
        };

        figure.Segments.Add(new BezierSegment(
            new Point(start.X, start.Y + controlOffset),
            new Point(end.X, end.Y - controlOffset),
            end,
            true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        var path = new WpfPath
        {
            Data = geometry,
            StrokeThickness = 2d,
            ToolTip = BuildEdgeToolTip(edge)
        };
        path.SetResourceReference(Shape.StrokeProperty, "DmsMutedForegroundBrush");
        Canvas.SetZIndex(path, 0);
        GraphCanvas.Children.Add(path);

        var arrow = new Polygon
        {
            Points = new PointCollection
            {
                new(end.X, end.Y),
                new(end.X - 6d, end.Y - 11d),
                new(end.X + 6d, end.Y - 11d)
            },
            ToolTip = BuildEdgeToolTip(edge)
        };
        arrow.SetResourceReference(Shape.FillProperty, "DmsMutedForegroundBrush");
        Canvas.SetZIndex(arrow, 1);
        GraphCanvas.Children.Add(arrow);

        if (edge.Quantity.HasValue)
        {
            var label = new TextBlock
            {
                Text = FormatQuantity(edge.Quantity, edge.Unit),
                FontSize = 10,
                Padding = new Thickness(3, 1, 3, 1),
                Background = GetBrush("DmsBackgroundBrush", Colors.Transparent),
                Foreground = GetBrush("DmsMutedForegroundBrush", Colors.Gray),
                ToolTip = BuildEdgeToolTip(edge)
            };

            Canvas.SetLeft(label, (start.X + end.X) / 2d - 20d);
            Canvas.SetTop(label, (start.Y + end.Y) / 2d - 12d);
            Canvas.SetZIndex(label, 2);
            GraphCanvas.Children.Add(label);
        }
    }

    private void DrawHorizontalEdge(SapArticleTreeEdge edge, Point from, Point to)
    {
        var start = new Point(from.X + NodeWidth, from.Y + NodeHeight / 2d);
        var end = new Point(to.X, to.Y + NodeHeight / 2d);
        var controlOffset = Math.Max(55d, Math.Abs(end.X - start.X) * 0.45d);

        var figure = new PathFigure
        {
            StartPoint = start,
            IsClosed = false,
            IsFilled = false
        };

        figure.Segments.Add(new BezierSegment(
            new Point(start.X + controlOffset, start.Y),
            new Point(end.X - controlOffset, end.Y),
            end,
            true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        var path = new WpfPath
        {
            Data = geometry,
            StrokeThickness = 2d,
            ToolTip = BuildEdgeToolTip(edge)
        };
        path.SetResourceReference(Shape.StrokeProperty, "DmsMutedForegroundBrush");
        Canvas.SetZIndex(path, 0);
        GraphCanvas.Children.Add(path);

        var arrow = new Polygon
        {
            Points = new PointCollection
            {
                new(end.X, end.Y),
                new(end.X - 10d, end.Y - 5d),
                new(end.X - 10d, end.Y + 5d)
            },
            ToolTip = BuildEdgeToolTip(edge)
        };
        arrow.SetResourceReference(Shape.FillProperty, "DmsMutedForegroundBrush");
        Canvas.SetZIndex(arrow, 1);
        GraphCanvas.Children.Add(arrow);

        if (edge.Quantity.HasValue)
        {
            var label = new TextBlock
            {
                Text = FormatQuantity(edge.Quantity, edge.Unit),
                FontSize = 10,
                Padding = new Thickness(3, 1, 3, 1),
                Background = GetBrush("DmsBackgroundBrush", Colors.Transparent),
                Foreground = GetBrush("DmsMutedForegroundBrush", Colors.Gray),
                ToolTip = BuildEdgeToolTip(edge)
            };

            Canvas.SetLeft(label, (start.X + end.X) / 2d - 20d);
            Canvas.SetTop(label, (start.Y + end.Y) / 2d - 13d);
            Canvas.SetZIndex(label, 2);
            GraphCanvas.Children.Add(label);
        }
    }

    private void DrawNode(SapArticleTreeNode node)
    {
        if (!_nodePositions.TryGetValue(node.MaterialNumber, out var point))
        {
            return;
        }

        var border = new Border
        {
            Width = NodeWidth,
            Height = NodeHeight,
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(8),
            BorderThickness = node.IsCurrent ? new Thickness(3) : new Thickness(1.5),
            Tag = node,
            Cursor = Cursors.Hand,
            ToolTip = BuildNodeToolTip(node)
        };

        ApplyNodeColors(border, node);
        border.MouseLeftButtonDown += Node_MouseLeftButtonDown;

        var stack = new StackPanel();

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });

        var stage = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(node.StageCode) ? "—" : node.StageCode,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 8, 0)
        };
        ApplyNodeTextColor(stage, node);

        var role = new TextBlock
        {
            Text = GetNodeRoleText(node),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 10,
            Opacity = 0.85
        };
        ApplyNodeTextColor(role, node);

        Grid.SetColumn(stage, 0);
        Grid.SetColumn(role, 1);
        header.Children.Add(stage);
        header.Children.Add(role);

        var number = new TextBlock
        {
            Text = node.MaterialNumber,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 5, 0, 2)
        };
        ApplyNodeTextColor(number, node);

        var description = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(node.Description) ? "—" : node.Description,
            FontSize = 11,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Opacity = 0.9
        };
        ApplyNodeTextColor(description, node);

        stack.Children.Add(header);
        stack.Children.Add(number);
        stack.Children.Add(description);
        border.Child = stack;

        Canvas.SetLeft(border, point.X);
        Canvas.SetTop(border, point.Y);
        Canvas.SetZIndex(border, 10);
        GraphCanvas.Children.Add(border);
    }

    private void ApplyNodeColors(Border border, SapArticleTreeNode node)
    {
        if (node.IsCurrent)
        {
            border.SetResourceReference(Border.BackgroundProperty, "DmsAccentBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "DmsAccentBrush");
            return;
        }

        var color = node.IsSibling
            ? Color.FromRgb(132, 97, 49)
            : node.IsPredecessor
                ? (string.Equals(node.StageCode, "R", StringComparison.OrdinalIgnoreCase)
                    ? Color.FromRgb(39, 100, 135)
                    : Color.FromRgb(55, 87, 108))
                : !node.HasKnownSuccessors
                    ? Color.FromRgb(68, 109, 79)
                    : Color.FromRgb(152, 106, 46);

        border.Background = new SolidColorBrush(color);
        border.BorderBrush = new SolidColorBrush(Lighten(color, 0.28d));
    }

    private static Color Lighten(Color color, double factor)
    {
        byte Blend(byte value) =>
            (byte)Math.Clamp(value + (255 - value) * factor, 0d, 255d);

        return Color.FromRgb(Blend(color.R), Blend(color.G), Blend(color.B));
    }

    private static void ApplyNodeTextColor(TextBlock textBlock, SapArticleTreeNode node)
    {
        if (node.IsCurrent)
        {
            textBlock.SetResourceReference(TextBlock.ForegroundProperty, "DmsOnAccentBrush");
        }
        else
        {
            textBlock.Foreground = Brushes.White;
        }
    }

    private void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: SapArticleTreeNode node })
        {
            return;
        }

        e.Handled = true;
        ShowSelection(node);

        if (e.ClickCount >= 2)
        {
            _logger?.Info(
                $"TEC03_ARTICLE_TREE_NAVIGATE; From={_articleNumber}; To={node.MaterialNumber}; User={_currentUserName}");

            TransactionRequested?.Invoke($"TEC03 {node.MaterialNumber}");
        }
    }

    private void ShowSelection(SapArticleTreeNode node)
    {
        TxtSelection.Text = TF(
            "TEC03.Tree.Selection",
            "{0} | stupeň {1} | {2}",
            node.MaterialNumber,
            string.IsNullOrWhiteSpace(node.StageCode) ? "—" : node.StageCode,
            string.IsNullOrWhiteSpace(node.Description) ? "—" : node.Description);
    }

    private string BuildNodeToolTip(SapArticleTreeNode node)
    {
        var oldNumber = string.IsNullOrWhiteSpace(node.OldMaterialNumber) ? "—" : node.OldMaterialNumber;
        var stage = string.IsNullOrWhiteSpace(node.StageCode) ? "—" : node.StageCode;

        return TF(
            "TEC03.Tree.NodeTooltip",
            "SAP: {0}\nStupeň: {1}\nPopis: {2}\nStaré číslo: {3}\nÚroveň grafu: {4}\nPředchůdci: {5}\nNásledovníci: {6}",
            node.MaterialNumber,
            stage,
            string.IsNullOrWhiteSpace(node.Description) ? "—" : node.Description,
            oldNumber,
            node.Level,
            node.HasKnownPredecessors ? T("Common.Yes", "Ano") : T("Common.No", "Ne"),
            node.HasKnownSuccessors ? T("Common.Yes", "Ano") : T("Common.No", "Ne"));
    }

    private string BuildEdgeToolTip(SapArticleTreeEdge edge)
    {
        return TF(
            "TEC03.Tree.EdgeTooltip",
            "{0} → {1}\nZávod: {2}\nBOM: {3}\nAlternativa: {4}\nPozice: {5}\nMnožství: {6}",
            edge.FromMaterialNumber,
            edge.ToMaterialNumber,
            edge.Plant,
            NullDash(edge.BomNumber),
            NullDash(edge.Alternative),
            NullDash(edge.Position),
            edge.Quantity.HasValue ? FormatQuantity(edge.Quantity, edge.Unit) : "—");
    }

    private string GetNodeRoleText(SapArticleTreeNode node)
    {
        if (node.IsCurrent)
        {
            return T("TEC03.Tree.Role.Current", "AKTUÁLNÍ");
        }

        if (node.IsSibling)
        {
            return T("TEC03.Tree.Role.Sibling", "SOUSED");
        }

        if (node.IsPredecessor)
        {
            return T("TEC03.Tree.Role.Predecessor", "PŘEDCHŮDCE");
        }

        if (node.IsSuccessor && !node.HasKnownSuccessors)
        {
            return T("TEC03.Tree.Role.Final", "FINÁLNÍ");
        }

        return T("TEC03.Tree.Role.Successor", "NÁSLEDOVNÍK");
    }

    private void CenterCurrentNode()
    {
        if (_graph is null ||
            !_nodePositions.TryGetValue(_graph.CurrentArticleNumber, out var point))
        {
            return;
        }

        _scaleTransform.ScaleX = 1d;
        _scaleTransform.ScaleY = 1d;

        var viewportWidth = Viewport.ActualWidth > 20d ? Viewport.ActualWidth : 1000d;
        var viewportHeight = Viewport.ActualHeight > 20d ? Viewport.ActualHeight : 620d;
        var currentCenterX = point.X + NodeWidth / 2d;
        var currentCenterY = point.Y + NodeHeight / 2d;

        _translateTransform.X = viewportWidth / 2d - currentCenterX;
        _translateTransform.Y = viewportHeight / 2d - currentCenterY;
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var oldScale = _scaleTransform.ScaleX;
        var multiplier = e.Delta > 0 ? 1.12d : 1d / 1.12d;
        var newScale = Math.Clamp(oldScale * multiplier, 0.35d, 2.5d);

        if (Math.Abs(newScale - oldScale) < 0.0001d)
        {
            return;
        }

        var mouse = e.GetPosition(Viewport);
        var graphX = (mouse.X - _translateTransform.X) / oldScale;
        var graphY = (mouse.Y - _translateTransform.Y) / oldScale;

        _scaleTransform.ScaleX = newScale;
        _scaleTransform.ScaleY = newScale;
        _translateTransform.X = mouse.X - graphX * newScale;
        _translateTransform.Y = mouse.Y - graphY * newScale;

        e.Handled = true;
    }

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindNodeBorder(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        _isPanning = true;
        _panStart = e.GetPosition(Viewport);
        _panStartX = _translateTransform.X;
        _panStartY = _translateTransform.Y;
        Viewport.CaptureMouse();
        Viewport.Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(Viewport);
        _translateTransform.X = _panStartX + current.X - _panStart.X;
        _translateTransform.Y = _panStartY + current.Y - _panStart.Y;
        e.Handled = true;
    }

    private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndPan();
    }

    private void Viewport_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isPanning && e.LeftButton != MouseButtonState.Pressed)
        {
            EndPan();
        }
    }

    private void EndPan()
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        Viewport.ReleaseMouseCapture();
        Viewport.Cursor = Cursors.Arrow;
    }

    private static Border? FindNodeBorder(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is Border { Tag: SapArticleTreeNode } border)
            {
                return border;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private async void BtnReload_Click(object sender, RoutedEventArgs e)
    {
        await RefreshGraphAsync();
    }

    private void BtnCenter_Click(object sender, RoutedEventArgs e)
    {
        CenterCurrentNode();
    }

    private async void CmbSuccessorDepth_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoaded && !_isRefreshing)
        {
            await RefreshGraphAsync();
        }
    }

    private async void ChkSiblings_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoaded && !_isRefreshing)
        {
            await RefreshGraphAsync();
        }
    }

    private Brush GetBrush(string key, Color fallback)
    {
        return Application.Current.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
    }

    private static string FormatQuantity(decimal? value, string? unit)
    {
        if (!value.HasValue)
        {
            return string.Empty;
        }

        var number = value.Value.ToString("0.###", CultureInfo.CurrentCulture);
        return string.IsNullOrWhiteSpace(unit) ? number : $"{number} {unit}";
    }

    private static string NullDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;

    private string T(string key, string fallback)
    {
        var value = _translate?.Invoke(key);
        return IsMissing(value, key) ? fallback : value!;
    }

    private string TF(string key, string fallback, params object[] args)
    {
        string pattern;

        if (_translateFormat is not null)
        {
            var translated = _translateFormat(key, args);
            if (!IsMissing(translated, key))
            {
                return translated;
            }
        }

        pattern = T(key, fallback);

        try
        {
            return string.Format(CultureInfo.CurrentCulture, pattern, args);
        }
        catch
        {
            return pattern;
        }
    }

    private static bool IsMissing(string? value, string key) =>
        string.IsNullOrWhiteSpace(value) ||
        string.Equals(value, key, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);

    private sealed record DepthOption(int Depth, string Text);
}
