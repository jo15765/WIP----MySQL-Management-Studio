using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MySqlManagementStudio.Models;

namespace MySqlManagementStudio.ViewModels;

public partial class ExplorerNodeViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isLoading;

    public ExplorerNodeKind Kind { get; }
    public ConnectionProfile? Profile { get; }
    public string? DatabaseName { get; }
    public string? ObjectName { get; }
    public string? Extra { get; }
    public ExplorerNodeViewModel? Parent { get; }
    public ObservableCollection<ExplorerNodeViewModel> Children { get; } = [];
    public bool ChildrenLoaded { get; set; }

    public string IconGlyph => Kind switch
    {
        ExplorerNodeKind.Server => "🖥",
        ExplorerNodeKind.Database => "🗄",
        ExplorerNodeKind.Folder => "📁",
        ExplorerNodeKind.Table => "▦",
        ExplorerNodeKind.View => "👁",
        ExplorerNodeKind.Procedure => "⚙",
        ExplorerNodeKind.Function => "ƒ",
        ExplorerNodeKind.Column => "▪",
        _ => "•"
    };

    public string ToolTipText => Extra is null ? Title : $"{Title} ({Extra})";

    public ExplorerNodeViewModel(
        string title,
        ExplorerNodeKind kind,
        ConnectionProfile? profile = null,
        string? databaseName = null,
        string? objectName = null,
        string? extra = null,
        ExplorerNodeViewModel? parent = null,
        bool addPlaceholder = false)
    {
        _title = title;
        Kind = kind;
        Profile = profile;
        DatabaseName = databaseName;
        ObjectName = objectName;
        Extra = extra;
        Parent = parent;

        if (addPlaceholder)
            Children.Add(CreatePlaceholder(this));
    }

    public static ExplorerNodeViewModel CreatePlaceholder(ExplorerNodeViewModel parent) =>
        new("Loading...", ExplorerNodeKind.Placeholder, parent: parent);

    public void ResetChildrenWithPlaceholder()
    {
        Children.Clear();
        Children.Add(CreatePlaceholder(this));
        ChildrenLoaded = false;
    }
}
