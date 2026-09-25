using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using MySqlManagementStudio.Models;
using MySqlManagementStudio.Services;

namespace MySqlManagementStudio.ViewModels;

public partial class QueryTabViewModel : ViewModelBase
{
    private readonly MySqlDatabaseService _db;
    private CancellationTokenSource? _executeCts;
    private int _resultVersion;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _sql = string.Empty;

    [ObservableProperty]
    private string? _database;

    [ObservableProperty]
    private string _messages = "Ready.";

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _isExecuting;

    [ObservableProperty]
    private string _executionElapsedText = "0.0s";

    [ObservableProperty]
    private int _selectedResultsTabIndex;

    [ObservableProperty]
    private ObservableCollection<ResultRow> _resultRows = [];

    [ObservableProperty]
    private List<string> _resultColumns = [];

    [ObservableProperty]
    private EditableResultTarget? _editableTarget;

    [ObservableProperty]
    private string _editabilityHint = "Results are read-only for this query.";

    public ConnectionProfile Profile { get; }
    public Guid Id { get; } = Guid.NewGuid();
    public SqlIntelliSenseService IntelliSense { get; }
    public bool CanEditResults => EditableTarget?.CanEdit == true;

    /// <summary>Bumps whenever results change so the view can rebuild DataGrid columns.</summary>
    public int ResultVersion
    {
        get => _resultVersion;
        private set => SetProperty(ref _resultVersion, value);
    }

    public QueryTabViewModel(ConnectionProfile profile, MySqlDatabaseService db, string? database = null, string? title = null)
    {
        Profile = profile;
        _db = db;
        IntelliSense = new SqlIntelliSenseService(db);
        _database = database ?? profile.DefaultDatabase;
        _title = title ?? $"Query {profile.DisplayName}";
    }

    partial void OnDatabaseChanged(string? value)
    {
        _ = IntelliSense.EnsureSchemaAsync(Profile, value);
    }

    partial void OnEditableTargetChanged(EditableResultTarget? value)
    {
        OnPropertyChanged(nameof(CanEditResults));
        EditabilityHint = value?.CanEdit == true
            ? $"Editable · {value.Database}.{value.Table} (changes save when you leave a cell)"
            : "Read-only · run a simple SELECT from a single table with a primary key to edit";
    }

    public async Task ExecuteAsync(string? selectedSql = null, IProgress<string>? statusProgress = null)
    {
        var sql = string.IsNullOrWhiteSpace(selectedSql) ? Sql : selectedSql;
        if (string.IsNullOrWhiteSpace(sql))
            return;

        _executeCts?.Cancel();
        _executeCts = new CancellationTokenSource();
        var sw = Stopwatch.StartNew();
        IsExecuting = true;
        ExecutionElapsedText = "0.0s";
        StatusText = "Executing... 0.0s";
        Messages = "Executing query... 0.0s elapsed";
        EditableTarget = null;
        statusProgress?.Report(StatusText);

        using var progressCts = CancellationTokenSource.CreateLinkedTokenSource(_executeCts.Token);
        var executeCts = _executeCts;
        var progressTask = RunElapsedProgressAsync(sw, progressCts.Token, statusProgress, executeCts);

        try
        {
            var result = await _db.ExecuteAsync(Profile, Database, sql, executeCts.Token);
            ResultColumns = result.Columns;
            ResultRows = result.Rows;
            Messages = result.Messages;
            StatusText = result.Succeeded
                ? $"Query executed successfully | {result.Duration.TotalSeconds:0.0}s | {result.RecordsAffected} row(s)"
                : "Query failed";
            SelectedResultsTabIndex = result.Succeeded && result.Columns.Count > 0 ? 0 : 1;
            if (result.Succeeded && result.Columns.Count > 0)
                await TryEnableEditingAsync(sql);
            ResultVersion++;
        }
        catch (OperationCanceledException)
        {
            Messages = "Query cancelled.";
            StatusText = "Cancelled";
        }
        catch (Exception ex)
        {
            Messages = ex.Message;
            StatusText = "Error";
            SelectedResultsTabIndex = 1;
        }
        finally
        {
            progressCts.Cancel();
            try { await progressTask; }
            catch (OperationCanceledException) { /* expected */ }

            sw.Stop();
            IsExecuting = false;
            // Only push final status if this execution is still the active one.
            if (ReferenceEquals(_executeCts, executeCts))
                statusProgress?.Report(StatusText);
        }
    }

    private async Task RunElapsedProgressAsync(
        Stopwatch sw,
        CancellationToken ct,
        IProgress<string>? statusProgress,
        CancellationTokenSource ownerCts)
    {
        while (!ct.IsCancellationRequested && ReferenceEquals(_executeCts, ownerCts) && IsExecuting)
        {
            var elapsed = FormatElapsed(sw.Elapsed);
            ExecutionElapsedText = elapsed;
            StatusText = $"Executing... {elapsed}";
            Messages = $"Query running... {elapsed} elapsed{Environment.NewLine}The app is working — please wait.";
            statusProgress?.Report(StatusText);

            try
            {
                await Task.Delay(100, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
            return $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        if (elapsed.TotalMinutes >= 1)
            return $"{elapsed.Minutes}:{elapsed.Seconds:00}.{elapsed.Milliseconds / 100}";
        return $"{elapsed.TotalSeconds:0.0}s";
    }

    public async Task<bool> CommitCellEditAsync(ResultRow row, string column)
    {
        if (EditableTarget is not { CanEdit: true } target)
            return false;

        if (target.PrimaryKeyColumns.Contains(column, StringComparer.OrdinalIgnoreCase))
        {
            row.Revert(column);
            StatusText = "Primary key columns are not editable inline.";
            return false;
        }

        var newValue = row.GetRaw(column);
        var oldValue = row.GetOriginal(column);
        if (string.Equals(Convert.ToString(newValue), Convert.ToString(oldValue), StringComparison.Ordinal))
        {
            row.CommitOriginal(column);
            return true;
        }

        try
        {
            var pkValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var pk in target.PrimaryKeyColumns)
            {
                if (!row.OriginalValues.TryGetValue(pk, out var pkValue))
                    throw new InvalidOperationException($"Primary key column '{pk}' is missing from the result set.");
                pkValues[pk] = pkValue;
            }

            await _db.UpdateCellAsync(Profile, target.Database, target.Table, column, newValue, pkValues);
            row.CommitOriginal(column);
            StatusText = $"Updated {target.Table}.{column}";
            Messages = $"{Messages.TrimEnd()}{Environment.NewLine}Updated `{target.Database}`.`{target.Table}`.`{column}` successfully.";
            return true;
        }
        catch (Exception ex)
        {
            row.Revert(column);
            StatusText = $"Update failed: {ex.Message}";
            Messages = $"{Messages.TrimEnd()}{Environment.NewLine}Update failed: {ex.Message}";
            SelectedResultsTabIndex = 1;
            return false;
        }
    }

    public string BuildCsv()
    {
        if (ResultColumns.Count == 0 || ResultRows.Count == 0)
            throw new InvalidOperationException("There are no results to export.");

        return CsvExportService.ToCsv(ResultColumns, ResultRows);
    }

    public void Cancel()
    {
        _executeCts?.Cancel();
    }

    /// <summary>Stops any in-flight execution and clears the busy/timer state (used when closing a tab).</summary>
    public void CancelAndResetExecution()
    {
        _executeCts?.Cancel();
        _executeCts = null;
        IsExecuting = false;
        ExecutionElapsedText = "0.0s";
        if (StatusText.StartsWith("Executing", StringComparison.OrdinalIgnoreCase))
            StatusText = "Cancelled";
    }

    public void SetScript(string sql, string? database = null, string? title = null)
    {
        Sql = sql;
        if (database is not null)
            Database = database;
        if (title is not null)
            Title = title;
    }

    private async Task TryEnableEditingAsync(string sql)
    {
        var detected = SqlEditabilityDetector.TryDetectSingleTable(sql, Database);
        if (detected is null || string.IsNullOrWhiteSpace(detected.Value.Table))
        {
            EditableTarget = null;
            return;
        }

        var database = detected.Value.Database ?? Database;
        if (string.IsNullOrWhiteSpace(database))
        {
            EditableTarget = null;
            return;
        }

        try
        {
            var pk = await _db.GetPrimaryKeyColumnsAsync(Profile, database, detected.Value.Table!);
            if (pk.Count == 0 || pk.Any(c => !ResultColumns.Contains(c, StringComparer.OrdinalIgnoreCase)))
            {
                EditableTarget = null;
                EditabilityHint = "Read-only · include primary key columns in the SELECT to enable editing";
                return;
            }

            EditableTarget = new EditableResultTarget
            {
                Database = database,
                Table = detected.Value.Table!,
                PrimaryKeyColumns = pk
            };
        }
        catch
        {
            EditableTarget = null;
        }
    }
}
