using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MySqlManagementStudio.Converters;
using MySqlManagementStudio.Models;
using MySqlManagementStudio.ViewModels;

namespace MySqlManagementStudio.Views;

public partial class MainWindow : Window
{
    private QueryTabViewModel? _subscribedTab;
    private TextBox? _activeEditBox;
    private string? _activeEditColumn;
    private double _objectExplorerSavedHorizontalOffset;
    private bool _resultsScrollBarsConfigured;

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        DataContextChanged += (_, _) => HookViewModel();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        ApplyPlatformHotkeys();
        HookViewModel();
        HookTreeViewExpansion();
        HookResultsGridEditEvents();
        ApplyWindowChromeForPage(DataContext is MainViewModel { IsHomePage: true });
    }

    private void ApplyPlatformHotkeys()
    {
        var isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        if (this.FindControl<TextBlock>("QueryShortcutHint") is { } hint)
        {
            hint.Text = isMac
                ? "F5 or ⌘↩  ·  run query     ·     Ctrl+Space  ·  IntelliSense"
                : "F5 or Ctrl+Enter  ·  run query     ·     Ctrl+Space  ·  IntelliSense";
            hint.FontSize = isMac ? 15 : 14;
        }

        if (!isMac)
            return;

        // Show ⌘ gestures in menus on macOS.
        SetMenuGesture("MenuHome", Key.H, KeyModifiers.Meta | KeyModifiers.Shift);
        SetMenuGesture("MenuViewHome", Key.H, KeyModifiers.Meta | KeyModifiers.Shift);
        SetMenuGesture("MenuNewConnection", Key.O, KeyModifiers.Meta);
        SetMenuGesture("MenuNewQuery", Key.N, KeyModifiers.Meta);
        SetMenuGesture("MenuCloseQuery", Key.W, KeyModifiers.Meta);
        SetMenuGesture("MenuCloseQueryWindow", Key.W, KeyModifiers.Meta);
        SetMenuGesture("MenuExecute", Key.Enter, KeyModifiers.Meta);
        SetMenuGesture("MenuExportCsv", Key.E, KeyModifiers.Meta);
        SetMenuGesture("MenuRefresh", Key.R, KeyModifiers.Meta);

        if (this.FindControl<MenuItem>("MenuExit") is { } exit)
            exit.Header = "Quit";
    }

    private void SetMenuGesture(string name, Key key, KeyModifiers modifiers)
    {
        if (this.FindControl<MenuItem>(name) is { } item)
            item.InputGesture = new KeyGesture(key, modifiers);
    }

    private void HookResultsGridEditEvents()
    {
        var grid = this.FindControl<DataGrid>("ResultsGrid");
        if (grid is null)
            return;

        grid.PreparingCellForEdit -= OnResultsPreparingCellForEdit;
        grid.PreparingCellForEdit += OnResultsPreparingCellForEdit;
    }

    private void HookViewModel()
    {
        if (DataContext is not MainViewModel vm)
            return;

        vm.PropertyChanged -= OnMainPropertyChanged;
        vm.PropertyChanged += OnMainPropertyChanged;
        SubscribeTab(vm.SelectedTab);
        RebuildResultsGrid(vm.SelectedTab);
        UpdateWorkspaceLayout(vm.ShowResultsPane);
    }

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainViewModel vm)
            return;

        if (e.PropertyName is nameof(MainViewModel.SelectedTab))
        {
            SubscribeTab(vm.SelectedTab);
            RebuildResultsGrid(vm.SelectedTab);
        }

        if (e.PropertyName is nameof(MainViewModel.ShowResultsPane))
            UpdateWorkspaceLayout(vm.ShowResultsPane);

        if (e.PropertyName is nameof(MainViewModel.IsHomePage))
            ApplyWindowChromeForPage(vm.IsHomePage);
    }

    private void ApplyWindowChromeForPage(bool isHomePage)
    {
        if (isHomePage)
        {
            // Compact, centered welcome until a connection is opened (fits without scrolling).
            WindowState = WindowState.Normal;
            MinWidth = 780;
            MinHeight = 560;
            Width = 900;
            Height = 620;
            CenterOnScreen();
            return;
        }

        // Full workspace once the user opens a connection.
        MinWidth = 1000;
        MinHeight = 640;
        WindowState = WindowState.Maximized;
    }

    private void CenterOnScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
            return;

        var working = screen.WorkingArea;
        var size = PixelSize.FromSize(ClientSize, DesktopScaling);
        var x = working.X + Math.Max(0, (working.Width - size.Width) / 2);
        var y = working.Y + Math.Max(0, (working.Height - size.Height) / 2);
        Position = new PixelPoint(x, y);
    }

    private void UpdateWorkspaceLayout(bool showResultsPane)
    {
        var grid = this.FindControl<Grid>("WorkspaceRightGrid");
        if (grid is null || grid.RowDefinitions.Count < 3)
            return;

        // Keep 3 row definitions always (children are attached to rows 0/1/2).
        // Collapsing unused rows to height 0 avoids Avalonia Grid measure crashes and layout thrash
        // from replacing RowDefinitions while children still reference higher row indexes.
        if (showResultsPane)
        {
            grid.RowDefinitions[0].Height = new GridLength(2, GridUnitType.Star);
            grid.RowDefinitions[1].Height = new GridLength(6);
            grid.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
        }
        else
        {
            grid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            grid.RowDefinitions[1].Height = new GridLength(0);
            grid.RowDefinitions[2].Height = new GridLength(0);
        }
    }

    private void SubscribeTab(QueryTabViewModel? tab)
    {
        if (_subscribedTab is not null)
            _subscribedTab.PropertyChanged -= OnTabPropertyChanged;

        _subscribedTab = tab;

        if (_subscribedTab is not null)
            _subscribedTab.PropertyChanged += OnTabPropertyChanged;
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(QueryTabViewModel.ResultVersion)
            or nameof(QueryTabViewModel.ResultRows)
            or nameof(QueryTabViewModel.ResultColumns)
            or nameof(QueryTabViewModel.EditableTarget)
            or nameof(QueryTabViewModel.CanEditResults))
        {
            RebuildResultsGrid(sender as QueryTabViewModel);
        }
    }

    private void RebuildResultsGrid(QueryTabViewModel? tab)
    {
        var grid = this.FindControl<DataGrid>("ResultsGrid");
        if (grid is null)
            return;

        try
        {
            grid.Columns.Clear();
            grid.ItemsSource = null;

            if (tab is null || tab.ResultColumns.Count == 0)
                return;

            var canEdit = tab.CanEditResults;
            var pk = tab.EditableTarget?.PrimaryKeyColumns ?? Array.Empty<string>();
            grid.IsReadOnly = !canEdit;

            foreach (var column in tab.ResultColumns)
            {
                var colName = column;
                var isPk = pk.Contains(colName, StringComparer.OrdinalIgnoreCase);

                // Bind to the row itself + converter so column names with spaces/()/*/, never
                // enter Avalonia's binding path parser (that was crashing Select Top 1000 on views).
                var col = new DataGridTextColumn
                {
                    Header = isPk ? $"{colName} (PK)" : colName,
                    Tag = colName,
                    Binding = new Binding(".")
                    {
                        Mode = BindingMode.OneWay,
                        Converter = new ResultRowColumnConverter(colName)
                    },
                    // Size to content first; AutoFitResultsColumns may expand to fill if they fit.
                    Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
                    MinWidth = 60,
                    IsReadOnly = !canEdit || isPk
                };

                grid.Columns.Add(col);
            }

            grid.ItemsSource = tab.ResultRows;
            HookResultsGridChrome();
            HookResultsGridEditEvents();
            ScheduleAutoFitResultsColumns(grid);
        }
        catch (Exception ex)
        {
            if (DataContext is MainViewModel vm)
                vm.StatusText = $"Failed to render results grid: {ex.Message}";
        }
    }

    private void ScheduleAutoFitResultsColumns(DataGrid grid)
    {
        // Wait until layout has measured Auto column widths against the current window size.
        Dispatcher.UIThread.Post(() => AutoFitResultsColumns(grid), DispatcherPriority.Loaded);
        Dispatcher.UIThread.Post(() => AutoFitResultsColumns(grid), DispatcherPriority.Background);
    }

    private static void AutoFitResultsColumns(DataGrid grid)
    {
        if (grid.Columns.Count == 0 || grid.Bounds.Width <= 0)
            return;

        var scrollViewer = grid.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        var available = scrollViewer?.Viewport.Width ?? grid.Bounds.Width;
        if (available <= 0)
            available = grid.Bounds.Width;

        // Leave a little room for borders / vertical scrollbar.
        available = Math.Max(0, available - 24);

        var measured = grid.Columns.Sum(c => c.ActualWidth);
        if (measured <= 0)
            return;

        if (measured <= available)
        {
            // All columns fit — expand them to fill the results pane.
            foreach (var column in grid.Columns)
                column.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
        }
        else
        {
            // Too wide for the window — keep measured widths and use the horizontal scrollbar.
            foreach (var column in grid.Columns)
            {
                var width = Math.Max(column.MinWidth, column.ActualWidth);
                column.Width = new DataGridLength(width);
            }
        }
    }

    private void HookResultsGridChrome()
    {
        var grid = this.FindControl<DataGrid>("ResultsGrid");
        if (grid is null)
            return;

        grid.HorizontalScrollBarVisibility = ScrollBarVisibility.Visible;
        grid.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;

        // Only walk the visual tree once — LayoutUpdated was firing constantly and made the UI feel sticky.
        if (!_resultsScrollBarsConfigured)
        {
            _resultsScrollBarsConfigured = true;
            Dispatcher.UIThread.Post(() => ApplyAlwaysVisibleScrollBars(grid), DispatcherPriority.Loaded);
        }
    }

    private static void ApplyAlwaysVisibleScrollBars(DataGrid grid)
    {
        var scrollViewer = grid.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scrollViewer is not null)
        {
            scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Visible;
            scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
        }

        foreach (var bar in grid.GetVisualDescendants().OfType<ScrollBar>())
        {
            bar.AllowAutoHide = false;
            bar.Opacity = 1;
        }
    }

    private void OnResultsPreparingCellForEdit(object? sender, DataGridPreparingCellForEditEventArgs e)
    {
        _activeEditBox = e.EditingElement as TextBox;
        _activeEditColumn = e.Column?.Tag as string;
        if (string.IsNullOrWhiteSpace(_activeEditColumn))
        {
            var header = e.Column?.Header?.ToString() ?? string.Empty;
            _activeEditColumn = header.EndsWith(" (PK)", StringComparison.Ordinal)
                ? header[..^5]
                : header;
        }
    }

    private async void OnResultsCellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        try
        {
            if (e.EditAction != DataGridEditAction.Commit)
                return;

            if (DataContext is not MainViewModel { SelectedTab: { } tab })
                return;

            if (e.Row?.DataContext is not ResultRow row)
                return;

            var column = e.Column?.Tag as string ?? _activeEditColumn;
            if (string.IsNullOrWhiteSpace(column))
            {
                var header = e.Column?.Header?.ToString() ?? string.Empty;
                column = header.EndsWith(" (PK)", StringComparison.Ordinal)
                    ? header[..^5]
                    : header;
            }

            if (string.IsNullOrWhiteSpace(column))
                return;

            // OneWay converter bindings don't push edits back; read the editor TextBox.
            if (_activeEditBox is not null)
                row[column] = _activeEditBox.Text;

            await tab.CommitCellEditAsync(row, column);
            if (DataContext is MainViewModel vm)
                vm.StatusText = tab.StatusText;
        }
        finally
        {
            _activeEditBox = null;
            _activeEditColumn = null;
        }
    }

    private void HookTreeViewExpansion()
    {
        var tree = this.FindControl<TreeView>("ObjectExplorer");
        if (tree is null)
            return;

        tree.AddHandler(TreeViewItem.ExpandedEvent, OnTreeItemExpanded, RoutingStrategies.Bubble);
        tree.AddHandler(InputElement.PointerPressedEvent, OnObjectExplorerPointerPressed, RoutingStrategies.Tunnel);
        // Block BringIntoView from shifting the tree when selecting / right-clicking nodes.
        tree.AddHandler(
            Control.RequestBringIntoViewEvent,
            OnObjectExplorerRequestBringIntoView,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        tree.SelectionChanged += async (_, _) =>
        {
            RestoreObjectExplorerHorizontalScroll(tree);
            if (DataContext is MainViewModel vm)
                await vm.OnNodeSelectionChangedAsync(tree.SelectedItem as ExplorerNodeViewModel);
        };
    }

    private void OnObjectExplorerRequestBringIntoView(object? sender, RequestBringIntoViewEventArgs e)
    {
        // Fully cancel auto bring-into-view so right-click / Select Top 1000 cannot slide the tree.
        e.Handled = true;
        if (sender is TreeView tree)
            RestoreObjectExplorerHorizontalScroll(tree);
        else if (this.FindControl<TreeView>("ObjectExplorer") is { } explorer)
            RestoreObjectExplorerHorizontalScroll(explorer);
    }

    private void OnObjectExplorerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TreeView tree)
            return;

        // Remember where the user had the tree scrolled before selection changes.
        var scrollViewer = GetObjectExplorerScrollViewer(tree);
        if (scrollViewer is not null)
            _objectExplorerSavedHorizontalOffset = scrollViewer.Offset.X;

        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            return;

        var source = e.Source as Control;
        while (source is not null && source is not TreeViewItem)
            source = source.Parent as Control;

        if (source is TreeViewItem { DataContext: ExplorerNodeViewModel node })
        {
            tree.SelectedItem = node;
            if (DataContext is MainViewModel vm)
                vm.SelectedNode = node;

            RestoreObjectExplorerHorizontalScroll(tree);
        }
    }

    private void RestoreObjectExplorerHorizontalScroll(TreeView tree)
    {
        var scrollViewer = GetObjectExplorerScrollViewer(tree);
        if (scrollViewer is null)
            return;

        var x = _objectExplorerSavedHorizontalOffset;
        void restore()
        {
            scrollViewer.Offset = new Vector(x, scrollViewer.Offset.Y);
        }

        // Selection/context-menu layout can adjust scroll after the current event; restore twice.
        Dispatcher.UIThread.Post(restore, DispatcherPriority.Loaded);
        Dispatcher.UIThread.Post(restore, DispatcherPriority.Background);
    }

    private static ScrollViewer? GetObjectExplorerScrollViewer(TreeView tree) =>
        tree.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    private async void OnTreeItemExpanded(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not TreeViewItem { DataContext: ExplorerNodeViewModel node })
            return;

        if (DataContext is MainViewModel vm)
            await vm.NodeExpandedCommand.ExecuteAsync(node);
    }

    private async void OnHomeConnectionDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (sender is ListBox { SelectedItem: ConnectionProfile profile })
            await vm.ConnectSavedCommand.ExecuteAsync(profile);
    }

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnKeyboardShortcutsClick(object? sender, RoutedEventArgs e)
    {
        var isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        var rows = isMac
            ? new (string Keys, string Action)[]
            {
                ("F5  or  ⌘↩", "Execute query"),
                ("⌘N", "New query"),
                ("⌘O", "New connection"),
                ("⌘W", "Close current query"),
                ("⌘E", "Export results to CSV"),
                ("⌘R", "Refresh Object Explorer"),
                ("⌘⇧H", "Go to Home"),
                ("Ctrl+Space", "IntelliSense / autocomplete"),
            }
            : new (string Keys, string Action)[]
            {
                ("F5  or  Ctrl+Enter", "Execute query"),
                ("Ctrl+N", "New query"),
                ("Ctrl+O", "New connection"),
                ("Ctrl+W", "Close current query"),
                ("Ctrl+E", "Export results to CSV"),
                ("Ctrl+R", "Refresh Object Explorer"),
                ("Ctrl+Shift+H", "Go to Home"),
                ("Ctrl+Space", "IntelliSense / autocomplete"),
            };

        var list = new StackPanel { Spacing = 14, Margin = new Thickness(28, 24) };
        list.Children.Add(new TextBlock
        {
            Text = isMac ? "Keyboard Shortcuts (macOS)" : "Keyboard Shortcuts",
            FontSize = 26,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        foreach (var (keys, action) in rows)
        {
            list.Children.Add(new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("240,*"),
                Children =
                {
                    new TextBlock
                    {
                        Text = keys,
                        FontSize = 20,
                        FontWeight = Avalonia.Media.FontWeight.SemiBold,
                        FontFamily = new Avalonia.Media.FontFamily("Menlo, Consolas, monospace"),
                        [Grid.ColumnProperty] = 0
                    },
                    new TextBlock
                    {
                        Text = action,
                        FontSize = 20,
                        Opacity = 0.9,
                        [Grid.ColumnProperty] = 1
                    }
                }
            });
        }

        list.Children.Add(new Button
        {
            Content = "Close",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            MinWidth = 110,
            MinHeight = 40,
            FontSize = 16,
            Margin = new Thickness(0, 16, 0, 0),
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center
        });

        var dialog = new Window
        {
            Title = "Keyboard Shortcuts",
            Icon = Icon,
            Width = 640,
            Height = 520,
            MinWidth = 560,
            MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer { Content = list }
        };

        if (list.Children[^1] is Button closeBtn)
            closeBtn.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(this);
    }

    private async void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        var isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        var shortcutHint = isMac
            ? "Shortcuts: F5 / Cmd+Return Execute · Cmd+N New Query · Cmd+O Connection · Ctrl+Space IntelliSense"
            : "Shortcuts: F5 / Ctrl+Enter Execute · Ctrl+N New Query · Ctrl+O Connection · Ctrl+Space IntelliSense";

        Bitmap? logo = null;
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://MySqlManagementStudio/Assets/mysql-management-studio.png"));
            logo = new Bitmap(stream);
        }
        catch
        {
            // Logo asset missing should not break About.
        }

        var content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12
        };

        if (logo is not null)
        {
            content.Children.Add(new Border
            {
                Background = Avalonia.Media.Brushes.White,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Child = new Image
                {
                    Source = logo,
                    Width = 280,
                    Height = 186,
                    Stretch = Avalonia.Media.Stretch.Uniform
                }
            });
        }

        content.Children.Add(new TextBlock
        {
            Text = "MySQL Management Studio",
            FontSize = 20,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = "A desktop studio for MySQL — connect, browse objects, and run queries with an SSMS-style workspace.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            TextAlignment = Avalonia.Media.TextAlignment.Center,
            Opacity = 0.85
        });
        content.Children.Add(new TextBlock
        {
            Text = shortcutHint,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.7,
            TextAlignment = Avalonia.Media.TextAlignment.Center
        });

        var dialog = new Window
        {
            Title = "About MySQL Management Studio",
            Icon = Icon,
            Width = 480,
            Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = content
        };

        await dialog.ShowDialog(this);
    }
}
