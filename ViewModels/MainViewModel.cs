using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MySqlManagementStudio.Models;
using MySqlManagementStudio.Services;
using MySqlManagementStudio.Views;

namespace MySqlManagementStudio.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ConnectionStore _store = new();
    private readonly MySqlDatabaseService _db = new();
    private int _queryCounter = 1;

    [ObservableProperty]
    private ExplorerNodeViewModel? _selectedNode;

    [ObservableProperty]
    private QueryTabViewModel? _selectedTab;

    [ObservableProperty]
    private string _statusText = "Ready — Connect to a MySQL server to begin.";

    [ObservableProperty]
    private string _connectionStatus = "Not connected";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isHomePage = true;

    [ObservableProperty]
    private ConnectionProfile? _selectedHomeConnection;

    public bool IsWorkspacePage => !IsHomePage;

    public bool HasSavedConnections => SavedConnections.Count > 0;

    public bool ShowEmptyConnections => SavedConnections.Count == 0;

    public bool ShowAlterViewMenu => SelectedNode?.Kind == ExplorerNodeKind.View;

    public bool ShowAlterProcedureMenu => SelectedNode?.Kind == ExplorerNodeKind.Procedure;

    public bool ShowAlterFunctionMenu => SelectedNode?.Kind == ExplorerNodeKind.Function;

    public bool ShowSelectTop1000Menu =>
        SelectedNode?.Kind is ExplorerNodeKind.Table or ExplorerNodeKind.View;

    public bool ShowScriptCreateMenu =>
        SelectedNode?.Kind is ExplorerNodeKind.Table
            or ExplorerNodeKind.View
            or ExplorerNodeKind.Procedure
            or ExplorerNodeKind.Function;

    public bool ShowResultsPane => Tabs.Count > 0;

    public bool IsQueryExecuting => SelectedTab?.IsExecuting == true;

    public ObservableCollection<ExplorerNodeViewModel> ExplorerRoots { get; } = [];
    public ObservableCollection<QueryTabViewModel> Tabs { get; } = [];
    public ObservableCollection<ConnectionProfile> SavedConnections { get; } = [];
    public ObservableCollection<string> AvailableDatabases { get; } = [];

    private QueryTabViewModel? _executionEventsTab;

    public MainViewModel()
    {
        foreach (var profile in _store.Load())
            SavedConnections.Add(profile);

        SavedConnections.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSavedConnections));
            OnPropertyChanged(nameof(ShowEmptyConnections));
        };

        Tabs.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ShowResultsPane));
            OnPropertyChanged(nameof(IsQueryExecuting));
        };

        StatusText = SavedConnections.Count == 0
            ? "Welcome — create a MySQL connection to get started."
            : "Welcome — double-click a connection to open it.";
    }

    partial void OnIsHomePageChanged(bool value)
    {
        OnPropertyChanged(nameof(IsWorkspacePage));
    }

    partial void OnSelectedTabChanged(QueryTabViewModel? value)
    {
        if (_executionEventsTab is not null)
            _executionEventsTab.PropertyChanged -= OnSelectedTabPropertyChanged;

        _executionEventsTab = value;

        if (_executionEventsTab is not null)
            _executionEventsTab.PropertyChanged += OnSelectedTabPropertyChanged;

        OnPropertyChanged(nameof(IsQueryExecuting));
        _ = RefreshDatabasesForTabAsync(value);
    }

    private void OnSelectedTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Only IsExecuting toggles the overlay. Do NOT refresh on ExecutionElapsedText
        // (that fires ~10x/sec while running and makes the UI feel sluggish).
        if (e.PropertyName is nameof(QueryTabViewModel.IsExecuting))
            OnPropertyChanged(nameof(IsQueryExecuting));
    }

    partial void OnSelectedNodeChanged(ExplorerNodeViewModel? value)
    {
        OnPropertyChanged(nameof(ShowAlterViewMenu));
        OnPropertyChanged(nameof(ShowAlterProcedureMenu));
        OnPropertyChanged(nameof(ShowAlterFunctionMenu));
        OnPropertyChanged(nameof(ShowSelectTop1000Menu));
        OnPropertyChanged(nameof(ShowScriptCreateMenu));
    }

    [RelayCommand]
    private void GoHome()
    {
        IsHomePage = true;
        ConnectionStatus = ExplorerRoots.Count == 0
            ? "Not connected"
            : $"{ExplorerRoots.Count} session(s) still open";
        StatusText = "Home — manage MySQL connections.";
    }

    /// <summary>Create/save a new connection from the home page (Workbench-style).</summary>
    [RelayCommand]
    private async Task NewConnectionAsync()
    {
        var dialogVm = new ConnectionDialogViewModel(_db);
        var ok = await ShowConnectionDialogAsync(dialogVm);
        if (!ok || dialogVm.Result is null)
            return;

        UpsertSavedConnection(dialogVm.Result);
        SelectedHomeConnection = dialogVm.Result;
        StatusText = $"Saved connection '{dialogVm.Result.DisplayName}'. Double-click it to open.";
    }

    /// <summary>Connect and open the workspace (used by toolbar Connect / menu).</summary>
    [RelayCommand]
    private async Task ConnectAsync()
    {
        var dialogVm = new ConnectionDialogViewModel(_db);
        var ok = await ShowConnectionDialogAsync(dialogVm);
        if (!ok || dialogVm.Result is null)
            return;

        await ConnectWithProfileAsync(dialogVm.Result, save: true);
    }

    [RelayCommand]
    private async Task ConnectSavedAsync(ConnectionProfile? profile)
    {
        if (profile is null)
            return;
        await ConnectWithProfileAsync(profile, save: false);
    }

    [RelayCommand]
    private async Task OpenSelectedHomeConnectionAsync()
    {
        if (SelectedHomeConnection is null)
        {
            StatusText = "Select a connection first.";
            return;
        }

        await ConnectWithProfileAsync(SelectedHomeConnection, save: false);
    }

    [RelayCommand]
    private async Task EditConnectionAsync(ConnectionProfile? profile)
    {
        profile ??= SelectedHomeConnection;
        if (profile is null)
        {
            StatusText = "Select a connection to edit.";
            return;
        }

        var dialogVm = new ConnectionDialogViewModel(_db, profile);
        var ok = await ShowConnectionDialogAsync(dialogVm);
        if (!ok || dialogVm.Result is null)
            return;

        UpsertSavedConnection(dialogVm.Result);
        SelectedHomeConnection = dialogVm.Result;
        StatusText = $"Updated connection '{dialogVm.Result.DisplayName}'.";
    }

    [RelayCommand]
    private void DeleteConnection(ConnectionProfile? profile)
    {
        profile ??= SelectedHomeConnection;
        if (profile is null)
        {
            StatusText = "Select a connection to delete.";
            return;
        }

        SavedConnections.Remove(profile);
        PersistConnections();

        if (SelectedHomeConnection?.Id == profile.Id)
            SelectedHomeConnection = null;

        var roots = ExplorerRoots.Where(r => r.Profile?.Id == profile.Id).ToList();
        foreach (var root in roots)
            ExplorerRoots.Remove(root);

        var tabs = Tabs.Where(t => t.Profile.Id == profile.Id).ToList();
        foreach (var tab in tabs)
            Tabs.Remove(tab);

        if (ExplorerRoots.Count == 0)
        {
            SelectedTab = null;
            IsHomePage = true;
            ConnectionStatus = "Not connected";
        }

        StatusText = $"Removed saved connection '{profile.DisplayName}'.";
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        var profile = SelectedNode?.Profile ?? ExplorerRoots.FirstOrDefault()?.Profile;
        if (profile is null)
        {
            GoHome();
            return;
        }

        var profileId = profile.Id;
        var roots = ExplorerRoots.Where(r => r.Profile?.Id == profileId).ToList();
        foreach (var root in roots)
            ExplorerRoots.Remove(root);

        var tabs = Tabs.Where(t => t.Profile.Id == profileId).ToList();
        foreach (var tab in tabs)
            Tabs.Remove(tab);

        SelectedTab = Tabs.FirstOrDefault();
        SelectedNode = null;

        if (ExplorerRoots.Count == 0)
        {
            IsHomePage = true;
            ConnectionStatus = "Not connected";
            StatusText = "Disconnected — back on Home.";
        }
        else
        {
            ConnectionStatus = $"{ExplorerRoots.Count} server(s)";
            StatusText = "Disconnected.";
        }

        await Task.CompletedTask;
    }

    [RelayCommand]
    private void NewQuery()
    {
        var profile = SelectedNode?.Profile ?? ExplorerRoots.FirstOrDefault()?.Profile;
        if (profile is null)
        {
            StatusText = "Connect to a MySQL server first.";
            return;
        }

        var db = SelectedNode?.DatabaseName ?? profile.DefaultDatabase;
        var tab = new QueryTabViewModel(profile, _db, db, $"SQLQuery{_queryCounter++}.sql");
        Tabs.Add(tab);
        SelectedTab = tab;
        StatusText = $"Opened new query against {profile.DisplayName}.";
    }

    [RelayCommand]
    private async Task ExecuteQueryAsync()
    {
        if (SelectedTab is null)
        {
            StatusText = "No query window is active.";
            return;
        }

        var progress = new Progress<string>(msg => StatusText = msg);
        await SelectedTab.ExecuteAsync(statusProgress: progress);
        StatusText = SelectedTab.StatusText;
    }

    [RelayCommand]
    private void CancelQuery()
    {
        if (SelectedTab is null)
            return;

        SelectedTab.CancelAndResetExecution();
        StatusText = "Cancel requested.";
        OnPropertyChanged(nameof(IsQueryExecuting));
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        if (SelectedTab is null || SelectedTab.ResultRows.Count == 0)
        {
            StatusText = "No results to export.";
            return;
        }

        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow is null)
        {
            StatusText = "Unable to open the save dialog.";
            return;
        }

        try
        {
            var csv = SelectedTab.BuildCsv();
            var file = await desktop.MainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export results to CSV",
                SuggestedFileName = $"{SelectedTab.Title.Replace(' ', '_')}.csv",
                DefaultExtension = "csv",
                FileTypeChoices =
                [
                    new FilePickerFileType("CSV files") { Patterns = ["*.csv"] },
                    new FilePickerFileType("All files") { Patterns = ["*"] }
                ]
            });

            if (file is null)
            {
                StatusText = "Export cancelled.";
                return;
            }

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            await writer.WriteAsync(csv);
            StatusText = $"Exported {SelectedTab.ResultRows.Count} row(s) to {file.Name}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CloseTab(QueryTabViewModel? tab)
    {
        if (tab is null)
            return;

        // Stop any running query/timer for this tab so the busy overlay cannot stick.
        tab.CancelAndResetExecution();

        var index = Tabs.IndexOf(tab);
        var wasSelected = SelectedTab == tab;
        Tabs.Remove(tab);

        if (wasSelected)
            SelectedTab = Tabs.Count == 0 ? null : Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];

        if (Tabs.Count == 0)
        {
            StatusText = "Query closed — open a new query to show results again.";
        }

        OnPropertyChanged(nameof(ShowResultsPane));
        OnPropertyChanged(nameof(IsQueryExecuting));
    }

    [RelayCommand]
    private void CloseCurrentQuery()
    {
        if (SelectedTab is null)
        {
            StatusText = "No query window is open.";
            return;
        }

        var title = SelectedTab.Title;
        CloseTab(SelectedTab);
        if (Tabs.Count > 0)
            StatusText = $"Closed query '{title}'.";
    }

    [RelayCommand]
    private async Task RefreshNodeAsync()
    {
        if (SelectedNode is null)
            return;

        SelectedNode.ChildrenLoaded = false;
        SelectedNode.ResetChildrenWithPlaceholder();
        SelectedNode.IsExpanded = true;
        await LoadChildrenAsync(SelectedNode);
        StatusText = $"Refreshed '{SelectedNode.Title}'.";
    }

    [RelayCommand]
    private async Task SelectTop1000Async()
    {
        if (SelectedNode is not { Kind: ExplorerNodeKind.Table or ExplorerNodeKind.View, Profile: not null, DatabaseName: not null, ObjectName: not null })
        {
            StatusText = "Select a table or view first.";
            return;
        }

        var sql = $"SELECT * FROM `{SelectedNode.DatabaseName}`.`{SelectedNode.ObjectName}` LIMIT 1000;\n";
        var tab = new QueryTabViewModel(
            SelectedNode.Profile,
            _db,
            SelectedNode.DatabaseName,
            $"{SelectedNode.ObjectName} — Top 1000");
        tab.SetScript(sql, SelectedNode.DatabaseName);
        Tabs.Add(tab);
        SelectedTab = tab;
        var progress = new Progress<string>(msg => StatusText = msg);
        await tab.ExecuteAsync(statusProgress: progress);
        StatusText = tab.StatusText;
    }

    [RelayCommand]
    private async Task ScriptCreateAsync()
    {
        if (SelectedNode?.Profile is null || SelectedNode.DatabaseName is null || SelectedNode.ObjectName is null)
        {
            StatusText = "Select a table, view, procedure, or function.";
            return;
        }

        var objectType = SelectedNode.Kind switch
        {
            ExplorerNodeKind.Table => "TABLE",
            ExplorerNodeKind.View => "VIEW",
            ExplorerNodeKind.Procedure => "PROCEDURE",
            ExplorerNodeKind.Function => "FUNCTION",
            _ => null
        };

        if (objectType is null)
        {
            StatusText = "Scripting is not available for this object.";
            return;
        }

        IsBusy = true;
        try
        {
            var script = await _db.GetCreateScriptAsync(
                SelectedNode.Profile,
                SelectedNode.DatabaseName,
                objectType,
                SelectedNode.ObjectName);

            if (string.IsNullOrWhiteSpace(script))
            {
                StatusText = "Could not retrieve CREATE script.";
                return;
            }

            var tab = new QueryTabViewModel(
                SelectedNode.Profile,
                _db,
                SelectedNode.DatabaseName,
                $"Script — {SelectedNode.ObjectName}");
            tab.SetScript(script + ";\n", SelectedNode.DatabaseName);
            Tabs.Add(tab);
            SelectedTab = tab;
            StatusText = $"Scripted CREATE for {SelectedNode.ObjectName}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Script failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AlterProcedureAsync()
    {
        if (SelectedNode is not { Kind: ExplorerNodeKind.Procedure, Profile: not null, DatabaseName: not null, ObjectName: not null })
        {
            StatusText = "Right-click a stored procedure in Object Explorer first.";
            return;
        }

        await OpenAlterScriptAsync(
            SelectedNode.Profile,
            SelectedNode.DatabaseName,
            "PROCEDURE",
            SelectedNode.ObjectName,
            createScript => SqlAlterScriptBuilder.BuildAlterProcedureScript(
                SelectedNode.DatabaseName,
                SelectedNode.ObjectName,
                createScript),
            $"Alter Procedure — {SelectedNode.ObjectName}");
    }

    [RelayCommand]
    private async Task AlterViewAsync()
    {
        if (SelectedNode is not { Kind: ExplorerNodeKind.View, Profile: not null, DatabaseName: not null, ObjectName: not null })
        {
            StatusText = "Right-click a view in Object Explorer first.";
            return;
        }

        await OpenAlterScriptAsync(
            SelectedNode.Profile,
            SelectedNode.DatabaseName,
            "VIEW",
            SelectedNode.ObjectName,
            createScript => SqlAlterScriptBuilder.BuildAlterViewScript(
                SelectedNode.DatabaseName,
                SelectedNode.ObjectName,
                createScript),
            $"Alter View — {SelectedNode.ObjectName}");
    }

    [RelayCommand]
    private async Task AlterFunctionAsync()
    {
        if (SelectedNode is not { Kind: ExplorerNodeKind.Function, Profile: not null, DatabaseName: not null, ObjectName: not null })
        {
            StatusText = "Right-click a function in Object Explorer first.";
            return;
        }

        await OpenAlterScriptAsync(
            SelectedNode.Profile,
            SelectedNode.DatabaseName,
            "FUNCTION",
            SelectedNode.ObjectName,
            createScript => SqlAlterScriptBuilder.BuildAlterFunctionScript(
                SelectedNode.DatabaseName,
                SelectedNode.ObjectName,
                createScript),
            $"Alter Function — {SelectedNode.ObjectName}");
    }

    private async Task OpenAlterScriptAsync(
        ConnectionProfile profile,
        string database,
        string objectType,
        string objectName,
        Func<string, string> buildAlterScript,
        string tabTitle)
    {
        IsBusy = true;
        try
        {
            var createScript = await _db.GetCreateScriptAsync(profile, database, objectType, objectName);
            if (string.IsNullOrWhiteSpace(createScript))
            {
                StatusText = $"Could not retrieve DDL for {objectName}.";
                return;
            }

            var alterScript = buildAlterScript(createScript);
            var tab = new QueryTabViewModel(profile, _db, database, tabTitle);
            tab.SetScript(alterScript, database);
            Tabs.Add(tab);
            SelectedTab = tab;
            StatusText = $"Loaded alter DDL for {objectName}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Alter script failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task NodeExpandedAsync(ExplorerNodeViewModel? node)
    {
        if (node is null || node.ChildrenLoaded || node.Kind == ExplorerNodeKind.Placeholder)
            return;

        await LoadChildrenAsync(node);
    }

    public async Task OnNodeSelectionChangedAsync(ExplorerNodeViewModel? node)
    {
        SelectedNode = node;
        if (node?.Profile is not null)
            ConnectionStatus = $"{node.Profile.DisplayName} ({node.Profile.Host}:{node.Profile.Port})";
    }

    private async Task ConnectWithProfileAsync(ConnectionProfile profile, bool save)
    {
        IsBusy = true;
        StatusText = $"Connecting to {profile.Host}:{profile.Port}...";
        try
        {
            if (save)
            {
                var existing = SavedConnections.FirstOrDefault(p => p.Id == profile.Id)
                               ?? SavedConnections.FirstOrDefault(p =>
                                   p.Host == profile.Host && p.Port == profile.Port && p.Username == profile.Username);
                if (existing is not null)
                {
                    var idx = SavedConnections.IndexOf(existing);
                    profile.Id = existing.Id;
                    SavedConnections[idx] = profile;
                }
                else
                {
                    SavedConnections.Add(profile);
                }

                PersistConnections();
            }

            // Avoid duplicate live roots for same profile
            var existingRoot = ExplorerRoots.FirstOrDefault(r => r.Profile?.Id == profile.Id);
            if (existingRoot is not null)
                ExplorerRoots.Remove(existingRoot);

            var root = new ExplorerNodeViewModel(
                profile.DisplayName,
                ExplorerNodeKind.Server,
                profile,
                addPlaceholder: true);
            ExplorerRoots.Add(root);
            root.IsExpanded = true;

            // Switch to workspace immediately so the click feels instant; load schema after paint.
            ConnectionStatus = $"{profile.DisplayName} — Connecting…";
            IsHomePage = false;
            await Task.Yield();

            // One network round-trip proves the connection (skip a separate TestConnection).
            await LoadChildrenAsync(root);

            ConnectionStatus = $"{profile.DisplayName} — Connected";
            StatusText = $"Connected to MySQL at {profile.Host}:{profile.Port}.";

            // Create the query editor after explorer data is ready — TextMate init is expensive.
            if (Tabs.All(t => t.Profile.Id != profile.Id))
            {
                var tab = new QueryTabViewModel(profile, _db, profile.DefaultDatabase, "Query");
                Tabs.Add(tab);
                SelectedTab = tab;
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Connection failed: {ex.Message}";
            var failedRoots = ExplorerRoots.Where(r => r.Profile?.Id == profile.Id).ToList();
            foreach (var failed in failedRoots)
                ExplorerRoots.Remove(failed);

            if (ExplorerRoots.Count == 0)
            {
                ConnectionStatus = "Not connected";
                IsHomePage = true;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadChildrenAsync(ExplorerNodeViewModel node)
    {
        if (node.Profile is null || node.ChildrenLoaded)
            return;

        node.IsLoading = true;
        try
        {
            switch (node.Kind)
            {
                case ExplorerNodeKind.Server:
                {
                    var databases = await _db.GetDatabasesAsync(node.Profile);
                    node.Children.Clear();
                    foreach (var db in databases)
                    {
                        node.Children.Add(new ExplorerNodeViewModel(
                            db,
                            ExplorerNodeKind.Database,
                            node.Profile,
                            databaseName: db,
                            parent: node,
                            addPlaceholder: true));
                    }

                    break;
                }
                case ExplorerNodeKind.Database:
                {
                    node.Children.Clear();
                    node.Children.Add(CreateFolder(node, "Tables", "tables"));
                    node.Children.Add(CreateFolder(node, "Views", "views"));
                    node.Children.Add(CreateFolder(node, "Stored Procedures", "procedures"));
                    node.Children.Add(CreateFolder(node, "Functions", "functions"));
                    break;
                }
                case ExplorerNodeKind.Folder when node.DatabaseName is not null:
                {
                    node.Children.Clear();
                    switch (node.ObjectName)
                    {
                        case "tables":
                        {
                            var tables = await _db.GetTablesAsync(node.Profile, node.DatabaseName);
                            foreach (var table in tables)
                            {
                                node.Children.Add(new ExplorerNodeViewModel(
                                    table,
                                    ExplorerNodeKind.Table,
                                    node.Profile,
                                    node.DatabaseName,
                                    table,
                                    parent: node,
                                    addPlaceholder: true));
                            }

                            break;
                        }
                        case "views":
                        {
                            var views = await _db.GetViewsAsync(node.Profile, node.DatabaseName);
                            foreach (var view in views)
                            {
                                node.Children.Add(new ExplorerNodeViewModel(
                                    view,
                                    ExplorerNodeKind.View,
                                    node.Profile,
                                    node.DatabaseName,
                                    view,
                                    parent: node,
                                    addPlaceholder: true));
                            }

                            break;
                        }
                        case "procedures":
                        {
                            var procs = await _db.GetRoutinesAsync(node.Profile, node.DatabaseName, "PROCEDURE");
                            foreach (var proc in procs)
                            {
                                node.Children.Add(new ExplorerNodeViewModel(
                                    proc,
                                    ExplorerNodeKind.Procedure,
                                    node.Profile,
                                    node.DatabaseName,
                                    proc,
                                    parent: node));
                            }

                            break;
                        }
                        case "functions":
                        {
                            var funcs = await _db.GetRoutinesAsync(node.Profile, node.DatabaseName, "FUNCTION");
                            foreach (var func in funcs)
                            {
                                node.Children.Add(new ExplorerNodeViewModel(
                                    func,
                                    ExplorerNodeKind.Function,
                                    node.Profile,
                                    node.DatabaseName,
                                    func,
                                    parent: node));
                            }

                            break;
                        }
                    }

                    break;
                }
                case ExplorerNodeKind.Table or ExplorerNodeKind.View
                    when node.DatabaseName is not null && node.ObjectName is not null:
                {
                    var columns = await _db.GetColumnsAsync(node.Profile, node.DatabaseName, node.ObjectName);
                    node.Children.Clear();
                    foreach (var (name, type, key, nullable) in columns)
                    {
                        var keyLabel = key switch
                        {
                            "PRI" => "PK",
                            "UNI" => "UQ",
                            "MUL" => "IX",
                            _ => null
                        };
                        var nullLabel = nullable ? "null" : "not null";
                        var extra = keyLabel is null ? $"{type}, {nullLabel}" : $"{keyLabel}, {type}, {nullLabel}";
                        node.Children.Add(new ExplorerNodeViewModel(
                            name,
                            ExplorerNodeKind.Column,
                            node.Profile,
                            node.DatabaseName,
                            name,
                            extra,
                            node));
                    }

                    break;
                }
            }

            node.ChildrenLoaded = true;
        }
        catch (Exception ex)
        {
            node.Children.Clear();
            node.Children.Add(new ExplorerNodeViewModel($"Error: {ex.Message}", ExplorerNodeKind.Placeholder, parent: node));
            StatusText = $"Failed to load '{node.Title}': {ex.Message}";
        }
        finally
        {
            node.IsLoading = false;
        }
    }

    private static ExplorerNodeViewModel CreateFolder(ExplorerNodeViewModel databaseNode, string title, string folderKey) =>
        new(title, ExplorerNodeKind.Folder, databaseNode.Profile, databaseNode.DatabaseName, folderKey, parent: databaseNode, addPlaceholder: true);

    private async Task RefreshDatabasesForTabAsync(QueryTabViewModel? tab)
    {
        AvailableDatabases.Clear();
        if (tab?.Profile is null)
            return;

        try
        {
            var databases = await _db.GetDatabasesAsync(tab.Profile);
            foreach (var db in databases.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                AvailableDatabases.Add(db);

            if (!string.IsNullOrWhiteSpace(tab.Database) &&
                !AvailableDatabases.Contains(tab.Database, StringComparer.OrdinalIgnoreCase))
            {
                var insertAt = AvailableDatabases
                    .TakeWhile(d => string.Compare(d, tab.Database, StringComparison.OrdinalIgnoreCase) < 0)
                    .Count();
                AvailableDatabases.Insert(insertAt, tab.Database!);
            }
        }
        catch
        {
            // Ignore — combo can still accept typed values via SelectedTab.Database binding
        }
    }

    private void PersistConnections() => _store.Save(SavedConnections);

    private void UpsertSavedConnection(ConnectionProfile profile)
    {
        var existing = SavedConnections.FirstOrDefault(p => p.Id == profile.Id)
                       ?? SavedConnections.FirstOrDefault(p =>
                           p.Host == profile.Host && p.Port == profile.Port && p.Username == profile.Username);

        if (existing is not null)
        {
            profile.Id = existing.Id;
            var idx = SavedConnections.IndexOf(existing);
            SavedConnections[idx] = profile;
        }
        else
        {
            SavedConnections.Add(profile);
        }

        PersistConnections();
    }

    private static async Task<bool> ShowConnectionDialogAsync(ConnectionDialogViewModel vm)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return false;

        var owner = desktop.MainWindow;
        var dialog = new ConnectionDialog
        {
            DataContext = vm
        };

        return await dialog.ShowDialog<bool>(owner!);
    }
}
