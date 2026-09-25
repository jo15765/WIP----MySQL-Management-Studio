using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Runtime.CompilerServices;

namespace MySqlManagementStudio.Models;

public sealed class QueryExecutionResult
{
    public bool Succeeded { get; init; }
    public string Messages { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public int RecordsAffected { get; init; }
    public DataTable? Table { get; init; }
    public ObservableCollection<ResultRow> Rows { get; init; } = [];
    public List<string> Columns { get; init; } = [];
}

public sealed class EditableResultTarget
{
    public required string Database { get; init; }
    public required string Table { get; init; }
    public required IReadOnlyList<string> PrimaryKeyColumns { get; init; }

    public bool CanEdit => PrimaryKeyColumns.Count > 0;
}

public sealed class ResultRow : INotifyPropertyChanged
{
    public Dictionary<string, object?> Values { get; }
    public Dictionary<string, object?> OriginalValues { get; }

    public ResultRow(Dictionary<string, object?> values)
    {
        Values = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);
        OriginalValues = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);
    }

    public object? this[string column]
    {
        get => Values.TryGetValue(column, out var value) ? FormatDisplay(value) : null;
        set
        {
            var normalized = NormalizeIncoming(value);
            if (Values.TryGetValue(column, out var current) && EqualsNormalized(current, normalized))
                return;

            Values[column] = normalized;
            OnPropertyChanged("Item[]");
            OnPropertyChanged($"Item[{column}]");
        }
    }

    public void CommitOriginal(string column)
    {
        if (Values.TryGetValue(column, out var value))
            OriginalValues[column] = value;
    }

    public void Revert(string column)
    {
        if (!OriginalValues.TryGetValue(column, out var original))
            return;

        Values[column] = original;
        OnPropertyChanged("Item[]");
        OnPropertyChanged($"Item[{column}]");
    }

    public object? GetRaw(string column) =>
        Values.TryGetValue(column, out var value) ? value : null;

    public object? GetOriginal(string column) =>
        OriginalValues.TryGetValue(column, out var value) ? value : null;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static object? FormatDisplay(object? value) =>
        value is null or DBNull ? "NULL" : value;

    private static object? NormalizeIncoming(object? value)
    {
        if (value is null or DBNull)
            return null;

        if (value is string s)
        {
            if (string.Equals(s, "NULL", StringComparison.OrdinalIgnoreCase) || s.Length == 0)
                return null;
            return s;
        }

        return value;
    }

    private static bool EqualsNormalized(object? left, object? right)
    {
        if (left is null && right is null)
            return true;
        if (left is null || right is null)
            return false;
        return string.Equals(Convert.ToString(left), Convert.ToString(right), StringComparison.Ordinal);
    }
}
